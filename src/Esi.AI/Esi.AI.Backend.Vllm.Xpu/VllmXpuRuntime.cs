using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Esi.AI.Backend.Abstractions;
using Esi.AI.Backend.Vllm.Xpu.Grpc;
using Esi.AI.Models;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LoadModelRequest = Esi.AI.Models.LoadModelRequest;
using PythonLoadRequest = Esi.AI.Models.PythonInferenceLoadRequest;

namespace Esi.AI.Backend.Vllm.Xpu;

/// <summary>Owns the vLLM Intel XPU Python process and its local gRPC model session.</summary>
public sealed class VllmXpuRuntime(ILogger<VllmXpuRuntime>? logger = null) : IBackendRuntime
{
    private const string VariantId = "vllm.xpu";
    private const int MaximumCapturedLogCharacters = 32 * 1024;
    private const int MaximumProcessExitOutputCharacters = 8 * 1024;
    private static readonly JsonSerializerOptions ConfigurationJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly ILogger<VllmXpuRuntime> effectiveLogger = logger ?? NullLogger<VllmXpuRuntime>.Instance;
    private readonly object sync = new();
    private readonly SemaphoreSlim lifecycleLock = new(1, 1);
    private readonly HashSet<VllmXpuChatSession> sessions = [];
    private readonly Dictionary<Process, string> expectedProcessExitReasons = new();
    private readonly VllmXpuPythonEnvironment provisioner = new();
    private Process? process;
    private GrpcChannel? channel;
    private Inference.InferenceClient? client;
    private string? modelPath;
    private string? modelId;
    private string loadLog = string.Empty;
    private IReadOnlyList<string> devices = [];
    private uint contextSize;
    private bool supportsImageInput;
    private bool isLoading;
    private int disposed;

    /// <inheritdoc />
    public BackendVariantDescriptor Descriptor { get; } = new(
        VariantId,
        ConfigurationBackend.Vllm,
        "vLLM Intel XPU",
        "vLLM",
        "XPU");

    /// <inheritdoc />
    public ModelLoadStatus GetStatus()
    {
        lock (sync)
        {
            var isRunning = process is { HasExited: false } && client is not null && modelPath is not null;
            var hasActivity = isLoading || isRunning;
            var loadedModels = isRunning && !isLoading && !string.IsNullOrWhiteSpace(modelId)
                ? new[]
                {
                    new LoadedModelStatus(
                        modelPath!,
                        ConfigurationBackend.Vllm,
                        Descriptor.RuntimeName,
                        0,
                        contextSize,
                        GetModelSize(modelPath!),
                        GetDeviceStatuses(devices),
                        null,
                        loadLog,
                        BackendVariantId: Descriptor.Id)
                }
                : [];
            var deviceStatuses = GetDeviceStatuses(devices);
            return new ModelLoadStatus(
                hasActivity ? modelPath : null,
                hasActivity ? Descriptor.Route : string.Empty,
                0,
                contextSize,
                hasActivity ? GetModelSize(modelPath) : 0,
                deviceStatuses.Count,
                deviceStatuses,
                null,
                loadLog,
                new Dictionary<string, float>(),
                loadedModels.Length > 0,
                loadedModels,
                Descriptor.Id);
        }
    }

    /// <inheritdoc />
    public bool SupportsImageInput(string? requestedModelPath)
    {
        lock (sync)
        {
            return supportsImageInput &&
                modelPath is not null &&
                string.Equals(modelPath, requestedModelPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <inheritdoc />
    public async Task LoadAsync(BackendLoadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        if (!string.Equals(request.VariantId, VariantId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"This runtime supports only '{VariantId}'.", nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModelPath);

        var configuration = NormalizeLoadConfiguration(request);
        if (configuration.Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(request), "The local gRPC port must be between 1 and 65535.");
        if (configuration.TensorParallelSize < 1)
            throw new ArgumentOutOfRangeException(nameof(request), "Tensor parallel size must be at least one.");
        var startupTimeout = configuration.StartupTimeout ?? TimeSpan.FromMinutes(10);
        if (startupTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request), "The startup timeout must be positive.");

        await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync("load_replacement").ConfigureAwait(false);
            lock (sync)
            {
                isLoading = true;
                modelPath = request.ModelPath;
                modelId = null;
                devices = configuration.Devices!;
                contextSize = configuration.MaxModelLength;
                loadLog = "Preparing vLLM Intel XPU runtime...";
            }

            try
            {
                var preparation = await provisioner.PrepareAsync(
                    configuration.PythonExecutable,
                    AppContext.BaseDirectory,
                    startupTimeout,
                    cancellationToken).ConfigureAwait(false);
                lock (sync)
                    loadLog = GetBoundedTail(preparation.Message, MaximumCapturedLogCharacters);

                var pythonDirectory = Path.Combine(AppContext.BaseDirectory, "Python");
                var bootstrapScriptPath = Path.Combine(pythonDirectory, "vllm_xpu_bootstrap.py");
                var scriptPath = Path.Combine(pythonDirectory, "inference_server.py");
                if (!File.Exists(bootstrapScriptPath))
                    throw new FileNotFoundException("The vLLM XPU Python bootstrap was not deployed.", bootstrapScriptPath);
                if (!File.Exists(scriptPath))
                    throw new FileNotFoundException("The Python gRPC inference bridge was not deployed.", scriptPath);

                var startInfo = new ProcessStartInfo
                {
                    FileName = preparation.PythonExecutable,
                    WorkingDirectory = configuration.WorkingDirectory ?? AppContext.BaseDirectory,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                foreach (var argument in BuildBootstrapArguments(bootstrapScriptPath, scriptPath, configuration))
                    startInfo.ArgumentList.Add(argument);

                BackendProcessLifecycle.ConfigureStartInfo(startInfo);
                if (!IsExecutable(preparation.PythonExecutable))
                    throw new InvalidOperationException($"The Python executable '{preparation.PythonExecutable}' was not found or is not executable.");

                var newProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                newProcess.OutputDataReceived += CaptureOutput;
                newProcess.ErrorDataReceived += CaptureOutput;
                newProcess.Exited += CaptureProcessExit;
                if (!newProcess.Start())
                    throw new InvalidOperationException("Could not start the vLLM XPU gRPC bridge.");
                lock (sync)
                    process = newProcess;
                newProcess.BeginOutputReadLine();
                newProcess.BeginErrorReadLine();
                effectiveLogger.LogInformation(
                    "Started vLLM XPU Python bridge. ProcessId={ProcessId} Device={Device} Port={Port}",
                    newProcess.Id,
                    configuration.Device,
                    configuration.Port);

                var newChannel = GrpcChannel.ForAddress(
                    $"http://127.0.0.1:{configuration.Port}",
                    new GrpcChannelOptions { MaxSendMessageSize = 64 * 1024 * 1024 });
                var newClient = new Inference.InferenceClient(newChannel);
                lock (sync)
                {
                    channel = newChannel;
                    client = newClient;
                }
                AppendLoadLog($"Started vLLM XPU gRPC bridge using {preparation.PythonExecutable}.");

                using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                startupCancellation.CancelAfter(startupTimeout);
                await WaitUntilReadyAsync(newClient, newProcess, startupTimeout, startupCancellation.Token).ConfigureAwait(false);
                using var loadCall = newClient.LoadModelAsync(VllmXpuGrpcMapper.ToGrpcRequest(configuration),
                    cancellationToken: startupCancellation.Token);
                var response = await loadCall.ResponseAsync.ConfigureAwait(false);
                var loadedModelId = GetLoadedModelId(response);
                lock (sync)
                    supportsImageInput = response.SupportsImageInput;
                await WaitUntilModelReadyAsync(
                    newClient,
                    newProcess,
                    loadedModelId,
                    startupTimeout,
                    startupCancellation.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                AppendLoadLog(exception.Message);
                if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
                    effectiveLogger.LogInformation(exception, "vLLM XPU model loading was cancelled. Device={Device} Port={Port}",
                        configuration.Device, configuration.Port);
                else
                    effectiveLogger.LogError(exception, "vLLM XPU model loading failed. Device={Device} Port={Port}",
                        configuration.Device, configuration.Port);
                await StopCoreAsync("load_failure").ConfigureAwait(false);
                throw;
            }
            finally
            {
                lock (sync)
                    isLoading = false;
            }
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task UnloadAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Inference.InferenceClient? activeClient;
            string? activeModelPath;
            lock (sync)
            {
                activeClient = client;
                activeModelPath = this.modelPath;
            }
            if (activeClient is null || activeModelPath is null || !SameModelPath(modelPath, activeModelPath))
                return;

            using var call = activeClient.UnloadModelAsync(new UnloadModelRequest(), cancellationToken: cancellationToken);
            var response = await call.ResponseAsync.ConfigureAwait(false);
            if (!response.Succeeded)
                throw new InvalidOperationException(response.Error);
            await StopCoreAsync("unload").ConfigureAwait(false);
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync("stop_requested").ConfigureAwait(false);
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    private async Task StopCoreAsync(string stopReason)
    {
        Process? activeProcess;
        GrpcChannel? activeChannel;
        VllmXpuChatSession[] activeSessions;
        lock (sync)
        {
            activeProcess = process;
            activeChannel = channel;
            activeSessions = sessions.ToArray();
            if (activeProcess is { HasExited: false })
                expectedProcessExitReasons[activeProcess] = stopReason;
            sessions.Clear();
            process = null;
            channel = null;
            client = null;
            modelPath = null;
            modelId = null;
            isLoading = false;
            devices = [];
            contextSize = 0;
            supportsImageInput = false;
        }

        foreach (var session in activeSessions)
            session.Dispose();
        activeChannel?.Dispose();
        if (activeProcess is null)
            return;

        effectiveLogger.LogInformation(
            "Stopping vLLM XPU Python bridge. StopReason={StopReason} ProcessId={ProcessId}",
            stopReason,
            activeProcess.Id);
        try
        {
            await BackendProcessLifecycle.StopAsync(activeProcess).ConfigureAwait(false);
        }
        finally
        {
            activeProcess.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<GenerationResult> GenerateAsync(
        OpenAiBackendChatRequest request,
        Func<string, Task>? onToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        if (request.Messages.Count == 0)
            throw new ArgumentException("At least one chat message is required.", nameof(request));

        VllmXpuChatSession session;
        lock (sync)
        {
            if (client is null || modelId is null || modelPath is null)
                throw new InvalidOperationException("Load a vLLM XPU model before starting a chat.");
            if (!string.IsNullOrWhiteSpace(request.ModelPath) && !SameModelPath(request.ModelPath, modelPath))
                throw new InvalidOperationException("The requested model is not loaded by this vLLM XPU runtime.");
            session = new VllmXpuChatSession(client, modelId);
            sessions.Add(session);
        }

        var options = request.Options.Tools is null && request.Tools is not null
            ? request.Options with { Tools = request.Tools }
            : request.Options;
        try
        {
            return await session.GenerateAsync(request.Messages, options, onToken, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (sync)
                sessions.Remove(session);
            session.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        lifecycleLock.Dispose();
    }

    internal static PythonLoadRequest NormalizeLoadConfiguration(BackendLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var configuration = request.Configuration.Deserialize<PythonLoadRequest>(ConfigurationJsonOptions)
            ?? throw new ArgumentException("A vLLM XPU model configuration is required.", nameof(request));
        var configuredDevices = configuration.Devices?.Where(device => !string.IsNullOrWhiteSpace(device)).ToArray() ?? [];
        var devices = configuredDevices.Length > 0
            ? configuredDevices
            : [GetConfiguredDevice(request.Configuration) ?? "xpu:0"];
        ValidateXpuDevices(devices);
        return configuration with
        {
            ModelPath = request.ModelPath,
            Backend = ConfigurationBackend.Vllm,
            Device = devices[0],
            Devices = devices
        };
    }

    internal static string GetLoadedModelId(ModelOperationResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!response.Succeeded)
        {
            var error = string.IsNullOrWhiteSpace(response.Error)
                ? "The vLLM XPU model load failed without an error message."
                : response.Error;
            throw new InvalidOperationException(error);
        }
        if (string.IsNullOrWhiteSpace(response.ModelId))
            throw new InvalidOperationException("The vLLM XPU model load succeeded without returning a model ID.");

        return response.ModelId;
    }

    internal static string GetReadyModelId(ReadinessResponse response, string expectedModelId)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedModelId);
        if (!response.Ready || !response.ModelLoaded)
            throw new InvalidOperationException("The vLLM XPU runtime has not finished loading the model.");
        if (string.IsNullOrWhiteSpace(response.ModelId))
            throw new InvalidOperationException("The vLLM XPU readiness response did not include a model ID.");
        if (!string.Equals(response.ModelId, expectedModelId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"The vLLM XPU runtime reported model '{response.ModelId}' as loaded instead of '{expectedModelId}'.");

        return response.ModelId;
    }

    internal static IReadOnlyList<string> BuildBootstrapArguments(
        string bootstrapScriptPath,
        string serverScriptPath,
        PythonLoadRequest configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapScriptPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverScriptPath);
        ArgumentNullException.ThrowIfNull(configuration);
        return
        [
            bootstrapScriptPath,
            "--esi-xpu-settings",
            JsonSerializer.Serialize(configuration.XpuRuntimeSettings ?? new VllmXpuRuntimeSettings(), ConfigurationJsonOptions),
            "--esi-xpu-graph",
            configuration.EnableXpuGraph ? "1" : "0",
            "--esi-xpu-bf16-mtp-draft",
            configuration.EnableBf16MtpDraft ? "1" : "0",
            "--",
            serverScriptPath,
            "--host", "127.0.0.1",
            "--grpc-port", configuration.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)
        ];
    }
    private async Task WaitUntilReadyAsync(
        Inference.InferenceClient activeClient,
        Process activeProcess,
        TimeSpan startupTimeout,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(startupTimeout);
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (activeProcess.HasExited)
                throw new InvalidOperationException(FormatPythonBridgeExitDetails(activeProcess.ExitCode, GetLoadLogSnapshot()));
            try
            {
                using var call = activeClient.CheckReadinessAsync(new ReadinessRequest(), cancellationToken: timeout.Token);
                if ((await call.ResponseAsync.ConfigureAwait(false)).Ready)
                    return;
            }
            catch (RpcException) when (!timeout.Token.IsCancellationRequested)
            {
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token).ConfigureAwait(false);
        }
    }

    private async Task WaitUntilModelReadyAsync(
        Inference.InferenceClient activeClient,
        Process activeProcess,
        string expectedModelId,
        TimeSpan startupTimeout,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(startupTimeout);
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (activeProcess.HasExited)
                throw new InvalidOperationException(FormatPythonBridgeExitDetails(activeProcess.ExitCode, GetLoadLogSnapshot()));
            try
            {
                using var call = activeClient.CheckReadinessAsync(new ReadinessRequest(), cancellationToken: timeout.Token);
                var response = await call.ResponseAsync.ConfigureAwait(false);
                if (response.Ready && response.ModelLoaded)
                {
                    var readyModelId = GetReadyModelId(response, expectedModelId);
                    lock (sync)
                        modelId = readyModelId;
                    return;
                }
            }
            catch (RpcException) when (!timeout.Token.IsCancellationRequested)
            {
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token).ConfigureAwait(false);
        }
    }

    private void CaptureOutput(object sender, DataReceivedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(args.Data))
            AppendLoadLog(args.Data);
    }

    private void CaptureProcessExit(object? sender, EventArgs args)
    {
        if (sender is not Process exitedProcess)
            return;

        string? stopReason;
        lock (sync)
            expectedProcessExitReasons.Remove(exitedProcess, out stopReason);

        try
        {
            exitedProcess.WaitForExit();
            var exitCode = exitedProcess.ExitCode;
            var processId = exitedProcess.Id;
            string output;
            lock (sync)
                output = GetBoundedTail(loadLog, MaximumProcessExitOutputCharacters);
            var failureDetails = ExtractPythonFailureDetails(output);
            if (stopReason is null)
            {
                effectiveLogger.LogError(
                    "vLLM XPU Python bridge exited unexpectedly. ProcessId={ProcessId} ExitCode={ExitCode} PythonOutputTail={PythonOutputTail} FailureDetails={FailureDetails}",
                    processId,
                    exitCode,
                    output,
                    failureDetails);
            }
            else
            {
                effectiveLogger.LogInformation(
                    "vLLM XPU Python bridge stopped. StopReason={StopReason} ProcessId={ProcessId} ExitCode={ExitCode} PythonOutputTail={PythonOutputTail}",
                    stopReason,
                    processId,
                    exitCode,
                    output);
            }
        }
        catch (InvalidOperationException exception)
        {
            effectiveLogger.LogWarning(exception, "Could not read the vLLM XPU Python bridge exit details.");
        }
    }

    private static void ValidateXpuDevices(IReadOnlyList<string> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if (devices.Count == 0)
            throw new ArgumentException("At least one XPU device route is required.", nameof(devices));
        foreach (var device in devices)
        {
            if (string.IsNullOrWhiteSpace(device) || !device.StartsWith("xpu:", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(device.AsSpan(4), out var ordinal) || ordinal < 0)
                throw new ArgumentException($"The vLLM XPU runtime requires routes in the 'xpu:<index>' format; received '{device}'.", nameof(devices));
        }
    }

    private static string? GetConfiguredDevice(JsonElement configuration)
    {
        if (configuration.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var property in configuration.EnumerateObject())
        {
            if (string.Equals(property.Name, "device", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString();
        }
        return null;
    }

    private static IReadOnlyList<DeviceStatus> GetDeviceStatuses(IReadOnlyList<string> devices) => devices
        .Select(device => new DeviceStatus(device, null, 0, null, "Intel", "Level Zero driver"))
        .ToArray();

    private static ulong GetModelSize(string? path) =>
        path is not null && File.Exists(path) ? (ulong)new FileInfo(path).Length : 0;

    private static bool SameModelPath(string requestedPath, string loadedPath)
    {
        var normalizedRequested = Path.IsPathFullyQualified(requestedPath) || File.Exists(requestedPath)
            ? Path.GetFullPath(requestedPath)
            : requestedPath;
        var normalizedLoaded = Path.IsPathFullyQualified(loadedPath) || File.Exists(loadedPath)
            ? Path.GetFullPath(loadedPath)
            : loadedPath;
        return string.Equals(normalizedRequested, normalizedLoaded, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExecutable(string path)
    {
        if (!File.Exists(path))
            return false;
        return !OperatingSystem.IsLinux() ||
            (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }

    internal static string ExtractPythonFailureDetails(string message)
    {
        var lines = message.Split(["\r\n", "\n"], StringSplitOptions.None);
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var candidate = lines[index].Trim();
            var separator = candidate.IndexOf(": ", StringComparison.Ordinal);
            if (separator <= 0)
                continue;
            var exceptionType = candidate[..separator];
            if (!exceptionType.EndsWith("Error", StringComparison.Ordinal) &&
                !exceptionType.EndsWith("Exception", StringComparison.Ordinal) &&
                !exceptionType.EndsWith("Failure", StringComparison.Ordinal))
                continue;

            var tracebackStart = index;
            while (tracebackStart >= 0 &&
                !lines[tracebackStart].Trim().StartsWith("Traceback (most recent call last):", StringComparison.Ordinal))
                tracebackStart--;

            var errorLines = tracebackStart >= 0
                ? lines[tracebackStart..(index + 1)].ToList()
                : new List<string> { candidate };
            for (var following = index + 1; following < lines.Length; following++)
            {
                var line = lines[following].Trim();
                if (line.StartsWith("[", StringComparison.Ordinal))
                    break;
                if (line.Length > 0)
                    errorLines.Add(line);
            }
            return string.Join(Environment.NewLine, errorLines);
        }
        return message.Trim();
    }

    internal static string FormatPythonBridgeExitDetails(int exitCode, string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var failureDetails = ExtractPythonFailureDetails(
            GetBoundedTail(output, MaximumProcessExitOutputCharacters));
        return string.IsNullOrWhiteSpace(failureDetails)
            ? $"vLLM XPU Python bridge exited with code {exitCode}."
            : $"vLLM XPU Python bridge exited with code {exitCode}.{Environment.NewLine}{failureDetails}";
    }

    private void AppendLoadLog(string message)
    {
        lock (sync)
        {
            var combined = string.IsNullOrEmpty(loadLog)
                ? message
                : string.Concat(loadLog, Environment.NewLine, message);
            loadLog = GetBoundedTail(combined, MaximumCapturedLogCharacters);
        }
    }

    private string GetLoadLogSnapshot()
    {
        lock (sync)
            return loadLog;
    }

    private static string GetBoundedTail(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters ? value : value[^maximumCharacters..];

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
}
