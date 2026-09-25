using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Esi.AI.Backend.Abstractions;
using Esi.AI.Models;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcProtocol = Esi.AI.Core.Grpc;

namespace Esi.AI.Backend.Vllm.Cuda12;

/// <summary>Runs a local vLLM CUDA 12 Python bridge and implements normalized model operations.</summary>
public sealed class VllmCuda12Runtime : IBackendRuntime
{
    private const string VariantId = "vllm.cuda12";
    private static readonly JsonSerializerOptions ConfigurationJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim ProvisioningLock = new(1, 1);
    private readonly object sync = new();
    private readonly SemaphoreSlim lifecycleLock = new(1, 1);
    private Process? process;
    private GrpcChannel? channel;
    private GrpcProtocol.Inference.InferenceClient? client;
    private string? modelPath;
    private string? modelId;
    private IReadOnlyList<string> deviceRoutes = [];
    private uint maxModelLength;
    private string loadLog = string.Empty;
    private bool isLoading;
    private bool modelReady;
    private int disposed;

    /// <inheritdoc />
    public BackendVariantDescriptor Descriptor { get; } = new(
        VariantId,
        ConfigurationBackend.Vllm,
        "vLLM CUDA 12",
        "vLLM",
        "CUDA12");

    /// <inheritdoc />
    public ModelLoadStatus GetStatus()
    {
        lock (sync)
        {
            var isRunning = process is { HasExited: false } && client is not null && modelPath is not null;
            var hasActivity = isLoading || isRunning;
            var statusDevices = deviceRoutes.Select(device => new DeviceStatus(device, "CUDA 12", 0, null, "NVIDIA", "CUDA 12")).ToArray();
            IReadOnlyList<LoadedModelStatus> loadedModels = isRunning && modelReady
                ? [new LoadedModelStatus(modelPath!, ConfigurationBackend.Vllm, Descriptor.RuntimeName, 0, maxModelLength, 0, statusDevices, null, loadLog, BackendVariantId: Descriptor.Id)]
                : [];
            return new ModelLoadStatus(
                hasActivity ? modelPath : null,
                hasActivity ? Descriptor.Route : string.Empty,
                0,
                maxModelLength,
                0,
                statusDevices.Length,
                statusDevices,
                null,
                loadLog,
                new Dictionary<string, float>(),
                isRunning && modelReady,
                loadedModels,
                Descriptor.Id);
        }
    }

    /// <inheritdoc />
    public bool SupportsImageInput(string? modelPath) => false;

    /// <inheritdoc />
    public async Task LoadAsync(BackendLoadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        var configuration = ValidateLoadRequest(request);

        await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
            lock (sync)
            {
                isLoading = true;
                modelPath = request.ModelPath;
                modelReady = false;
                deviceRoutes = ResolveDevices(configuration);
                maxModelLength = configuration.MaxModelLength;
                loadLog = "Preparing vLLM CUDA 12 runtime...";
            }

            try
            {
                var applicationDirectory = AppContext.BaseDirectory;
                var scriptPath = Path.Combine(applicationDirectory, "Python", "inference_server.py");
                if (!File.Exists(scriptPath))
                    throw new FileNotFoundException("The vLLM CUDA 12 gRPC bridge was not deployed.", scriptPath);

                var startupTimeout = configuration.StartupTimeout ?? TimeSpan.FromMinutes(10);
                var python = await PreparePythonAsync(configuration.PythonExecutable, applicationDirectory, startupTimeout, cancellationToken).ConfigureAwait(false);
                lock (sync)
                    loadLog = python.Message;

                var startInfo = new ProcessStartInfo
                {
                    FileName = python.Executable,
                    WorkingDirectory = configuration.WorkingDirectory ?? applicationDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                startInfo.ArgumentList.Add(scriptPath);
                startInfo.ArgumentList.Add("--host");
                startInfo.ArgumentList.Add("127.0.0.1");
                startInfo.ArgumentList.Add("--grpc-port");
                startInfo.ArgumentList.Add(configuration.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
                startInfo.Environment["CUDA_VISIBLE_DEVICES"] = string.Join(",", deviceRoutes.Select(device => device[(device.IndexOf(':') + 1)..]));

                var newProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                newProcess.OutputDataReceived += CaptureOutput;
                newProcess.ErrorDataReceived += CaptureOutput;
                if (!newProcess.Start())
                    throw new InvalidOperationException("Could not start the vLLM CUDA 12 gRPC bridge.");
                newProcess.BeginOutputReadLine();
                newProcess.BeginErrorReadLine();
                lock (sync)
                    process = newProcess;
                var newChannel = GrpcChannel.ForAddress($"http://127.0.0.1:{configuration.Port}");
                var newClient = new GrpcProtocol.Inference.InferenceClient(newChannel);
                lock (sync)
                {
                    channel = newChannel;
                    client = newClient;
                    loadLog = string.Concat(loadLog, Environment.NewLine, $"Started vLLM CUDA 12 bridge using {python.Executable}.");
                }

                using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                startupCancellation.CancelAfter(startupTimeout);
                await WaitUntilReadyAsync(newClient, newProcess, startupTimeout, startupCancellation.Token).ConfigureAwait(false);
                var response = await newClient.LoadModelAsync(
                    VllmCuda12GrpcMapper.ToGrpcRequest(configuration),
                    cancellationToken: startupCancellation.Token).ResponseAsync.ConfigureAwait(false);
                if (!response.Succeeded)
                    throw new InvalidOperationException(response.Error);

                lock (sync)
                    modelId = response.ModelId;
                await WaitUntilModelReadyAsync(newClient, newProcess, startupTimeout, startupCancellation.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                lock (sync)
                    loadLog = ExtractRootCause(exception.Message);
                await StopCoreAsync().ConfigureAwait(false);
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
            lock (sync)
            {
                if (!string.Equals(this.modelPath, modelPath, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            await StopCoreAsync().ConfigureAwait(false);
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
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<GenerationResult> GenerateAsync(
        OpenAiBackendChatRequest request,
        Func<string, Task>? onToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        GrpcProtocol.Inference.InferenceClient activeClient;
        string activeModelId;
        lock (sync)
        {
            activeClient = client ?? throw new InvalidOperationException("Load a model before starting a chat.");
            activeModelId = modelId ?? modelPath ?? throw new InvalidOperationException("Load a model before starting a chat.");
        }

        var grpcRequest = VllmCuda12GrpcMapper.ToGrpcRequest(request, activeModelId);
        using var call = activeClient.Generate(grpcRequest, cancellationToken: cancellationToken);
        var started = Stopwatch.StartNew();
        var text = new StringBuilder();
        var tokenCount = 0;
        var promptTokenCount = 0;
        var tokensPerSecond = 0d;
        IReadOnlyList<OpenAiToolCall>? toolCalls = null;
        while (await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            var response = call.ResponseStream.Current;
            if (!string.IsNullOrWhiteSpace(response.Error))
                throw new InvalidOperationException($"vLLM generation failed: {response.Error}");
            text.Append(response.Delta);
            if (!string.IsNullOrEmpty(response.Delta) && onToken is not null)
                await onToken(response.Delta).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(response.ToolCallsJson))
                toolCalls = JsonSerializer.Deserialize<OpenAiToolCall[]>(response.ToolCallsJson, ConfigurationJsonOptions);
            tokenCount = Math.Max(tokenCount, (int)response.GeneratedTokens);
            promptTokenCount = Math.Max(promptTokenCount, (int)response.PromptTokens);
            if (response.TokensPerSecond > 0)
                tokensPerSecond = response.TokensPerSecond;
        }
        started.Stop();

        var resultText = text.ToString();
        if (string.IsNullOrWhiteSpace(resultText) && toolCalls is not { Count: > 0 })
            throw new InvalidOperationException("vLLM returned an empty answer.");
        if (tokensPerSecond <= 0 && started.Elapsed.TotalSeconds > 0)
            tokensPerSecond = tokenCount / started.Elapsed.TotalSeconds;
        return new GenerationResult(
            resultText,
            tokenCount,
            started.Elapsed,
            tokensPerSecond,
            promptTokenCount,
            toolCalls is { Count: > 0 } ? "tool_calls" : "stop",
            toolCalls);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        lifecycleLock.Dispose();
    }

    internal static PythonInferenceLoadRequest ValidateLoadRequest(BackendLoadRequest request)
    {
        if (!string.Equals(request.VariantId, VariantId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"This runtime supports only '{VariantId}'.", nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModelPath);
        if (request.Configuration.ValueKind is not JsonValueKind.Object)
            throw new ArgumentException("A vLLM CUDA 12 configuration object is required.", nameof(request));

        PythonInferenceLoadRequest configuration;
        try
        {
            configuration = request.Configuration.Deserialize<PythonInferenceLoadRequest>(ConfigurationJsonOptions)
                ?? throw new ArgumentException("A vLLM CUDA 12 configuration object is required.", nameof(request));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The vLLM CUDA 12 configuration is invalid.", nameof(request), exception);
        }

        if (configuration.Backend is not ConfigurationBackend.Vllm)
            throw new ArgumentException("The configuration backend must be Vllm.", nameof(request));
        if (configuration.Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(request), "The local gRPC port must be between 1 and 65535.");
        if (configuration.TensorParallelSize < 1)
            throw new ArgumentOutOfRangeException(nameof(request), "Tensor parallel size must be at least one.");
        if (configuration.GpuMemoryUtilization is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(request), "GPU memory utilization must be between 0 and 100 percent.");
        if (configuration.StartupTimeout is { } timeout && timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request), "The startup timeout must be positive.");
        if (string.IsNullOrWhiteSpace(configuration.PythonExecutable))
            throw new ArgumentException("A Python executable is required.", nameof(request));
        if (configuration.WorkingDirectory is not null)
        {
            if (string.IsNullOrWhiteSpace(configuration.WorkingDirectory) || !Directory.Exists(configuration.WorkingDirectory))
                throw new DirectoryNotFoundException($"The Python working directory was not found: {configuration.WorkingDirectory}");
            configuration = configuration with { WorkingDirectory = Path.GetFullPath(configuration.WorkingDirectory) };
        }

        if (Path.IsPathFullyQualified(request.ModelPath))
        {
            var fullModelPath = Path.GetFullPath(request.ModelPath);
            if (!File.Exists(fullModelPath) && !Directory.Exists(fullModelPath))
                throw new FileNotFoundException("The model path was not found.", fullModelPath);
        }

        var selectedDevices = ResolveDevices(configuration);
        configuration = configuration with { ModelPath = request.ModelPath, Devices = selectedDevices, Device = selectedDevices[0] };
        return configuration;
    }

    private static IReadOnlyList<string> ResolveDevices(PythonInferenceLoadRequest configuration)
    {
        var requested = configuration.Devices is { Count: > 0 } ? configuration.Devices : [configuration.Device];
        var resolved = new List<string>();
        foreach (var device in requested.Where(device => !string.IsNullOrWhiteSpace(device)))
        {
            var route = device.Trim();
            var parts = route.Split(':', 2);
            if (parts.Length != 2 || !string.Equals(parts[0], "cuda", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(parts[1], out var ordinal) || ordinal < 0)
                throw new ArgumentException($"Invalid CUDA device route '{route}'. Expected 'cuda:<index>'.", nameof(configuration));
            var normalized = $"cuda:{ordinal}";
            if (!resolved.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                resolved.Add(normalized);
        }
        if (resolved.Count == 0)
            throw new ArgumentException("At least one CUDA device route is required.", nameof(configuration));
        return resolved;
    }

    private async Task StopCoreAsync()
    {
        Process? activeProcess;
        GrpcChannel? activeChannel;
        lock (sync)
        {
            activeProcess = process;
            activeChannel = channel;
            process = null;
            channel = null;
            client = null;
            modelReady = false;
            modelPath = null;
            modelId = null;
            deviceRoutes = [];
            maxModelLength = 0;
            isLoading = false;
        }

        activeChannel?.Dispose();
        if (activeProcess is null)
            return;
        try
        {
            if (!activeProcess.HasExited)
                activeProcess.Kill(entireProcessTree: true);
            await activeProcess.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            activeProcess.Dispose();
        }
    }

    private async Task WaitUntilReadyAsync(
        GrpcProtocol.Inference.InferenceClient activeClient,
        Process activeProcess,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer.CancelAfter(timeout);
        while (true)
        {
            timer.Token.ThrowIfCancellationRequested();
            if (activeProcess.HasExited)
                throw new InvalidOperationException(ExtractRootCause(loadLog));
            try
            {
                var response = await activeClient.CheckReadinessAsync(new GrpcProtocol.ReadinessRequest(), cancellationToken: timer.Token).ResponseAsync.ConfigureAwait(false);
                if (response.Ready)
                    return;
            }
            catch (RpcException) when (!timer.Token.IsCancellationRequested)
            {
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), timer.Token).ConfigureAwait(false);
        }
    }

    private async Task WaitUntilModelReadyAsync(
        GrpcProtocol.Inference.InferenceClient activeClient,
        Process activeProcess,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer.CancelAfter(timeout);
        while (true)
        {
            timer.Token.ThrowIfCancellationRequested();
            if (activeProcess.HasExited)
                throw new InvalidOperationException(ExtractRootCause(loadLog));
            try
            {
                var response = await activeClient.CheckReadinessAsync(new GrpcProtocol.ReadinessRequest(), cancellationToken: timer.Token).ResponseAsync.ConfigureAwait(false);
                if (response.Ready && response.ModelLoaded)
                {
                    lock (sync)
                    {
                        modelId = string.IsNullOrWhiteSpace(response.ModelId) ? modelId : response.ModelId;
                        modelReady = true;
                    }
                    return;
                }
            }
            catch (RpcException) when (!timer.Token.IsCancellationRequested)
            {
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), timer.Token).ConfigureAwait(false);
        }
    }

    private void CaptureOutput(object sender, DataReceivedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(args.Data))
            lock (sync)
                loadLog = string.Concat(loadLog, Environment.NewLine, args.Data);
    }

    private static async Task<PythonPreparation> PreparePythonAsync(
        string requestedExecutable,
        string applicationDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var pythonDirectory = Path.Combine(applicationDirectory, "Python");
        var requirementsPath = Path.Combine(pythonDirectory, "vllm-requirements.txt");
        if (!File.Exists(requirementsPath))
            throw new FileNotFoundException("The vLLM CUDA 12 requirements file was not deployed.", requirementsPath);

        var configuredExecutable = ResolveConfiguredExecutable(requestedExecutable);
        if (!IsAutomaticExecutable(configuredExecutable))
        {
            await ValidatePythonAsync(configuredExecutable, timeout, cancellationToken).ConfigureAwait(false);
            return new(configuredExecutable, $"Using configured Python executable: {configuredExecutable}.");
        }

        var pythonRoot = Environment.GetEnvironmentVariable("ESI_PYTHON_ENV_ROOT");
        if (string.IsNullOrWhiteSpace(pythonRoot))
            pythonRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".venvs");
        var environmentPath = Path.Combine(Path.GetFullPath(pythonRoot), "esi-ai-vllm-cuda12");
        var environmentPython = Path.Combine(environmentPath, OperatingSystem.IsWindows() ? "Scripts" : "bin", OperatingSystem.IsWindows() ? "python.exe" : "python");

        using var installTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        installTimeout.CancelAfter(timeout);
        await ProvisioningLock.WaitAsync(installTimeout.Token).ConfigureAwait(false);
        try
        {
            if (!File.Exists(environmentPython))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(environmentPath)!);
                var createResult = await RunProcessAsync(configuredExecutable, ["-m", "venv", environmentPath], timeout, installTimeout.Token).ConfigureAwait(false);
                EnsureProcessSucceeded(createResult, "Creating the vLLM CUDA 12 Python environment");
            }
            if (!await HasPythonDependenciesAsync(environmentPython, timeout, installTimeout.Token).ConfigureAwait(false))
            {
                var installResult = await RunProcessAsync(environmentPython, ["-m", "pip", "install", "--disable-pip-version-check", "-r", requirementsPath], timeout, installTimeout.Token).ConfigureAwait(false);
                EnsureProcessSucceeded(installResult, "Installing vLLM CUDA 12 Python dependencies");
            }
            await ValidatePythonAsync(environmentPython, timeout, installTimeout.Token).ConfigureAwait(false);
        }
        finally
        {
            ProvisioningLock.Release();
        }
        return new(environmentPython, $"Prepared vLLM CUDA 12 Python environment at '{environmentPath}'.");
    }

    private static string ResolveConfiguredExecutable(string requestedExecutable)
    {
        var configured = Environment.GetEnvironmentVariable("ESI_VLLM_CUDA12_PYTHON_EXECUTABLE")
            ?? Environment.GetEnvironmentVariable("ESI_VLLM_PYTHON_EXECUTABLE")
            ?? Environment.GetEnvironmentVariable("ESI_PYTHON_REFERENCE_EXECUTABLE");
        return IsAutomaticExecutable(requestedExecutable) && !string.IsNullOrWhiteSpace(configured) && !IsAutomaticExecutable(configured)
            ? configured
            : requestedExecutable;
    }

    private static bool IsAutomaticExecutable(string executable) =>
        string.Equals(executable, "python3", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(executable, "python", StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> HasPythonDependenciesAsync(string executable, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(executable, ["-c", "import grpc, google.protobuf, vllm"], timeout, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    private static async Task ValidatePythonAsync(string executable, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(executable, ["-c", "import grpc, google.protobuf, vllm"], timeout, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Python executable '{executable}' must provide grpcio, protobuf and vLLM. Install Python dependencies from the packaged requirements files.");
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException($"Could not start Python executable '{executable}'.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timer.Token).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            var message = string.Join(Environment.NewLine, new[] { output.Trim(), error.Trim() }.Where(value => value.Length > 0));
            return new(process.ExitCode, message);
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private static void EnsureProcessSucceeded(ProcessResult result, string operation)
    {
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"{operation} failed with exit code {result.ExitCode}.{Environment.NewLine}{result.Output}");
    }

    private static string ExtractRootCause(string message)
    {
        var lines = message.Split(["\r\n", "\n"], StringSplitOptions.None);
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var candidate = lines[index].Trim();
            var separator = candidate.IndexOf(": ", StringComparison.Ordinal);
            if (separator > 0 && (candidate.EndsWith("Error", StringComparison.Ordinal) || candidate.EndsWith("Exception", StringComparison.Ordinal)))
                return string.Join(Environment.NewLine, lines.Skip(index).TakeWhile(line => !line.TrimStart().StartsWith("[", StringComparison.Ordinal))).Trim();
        }
        return message.Trim();
    }

    private sealed record PythonPreparation(string Executable, string Message);

    private sealed record ProcessResult(int ExitCode, string Output);
}