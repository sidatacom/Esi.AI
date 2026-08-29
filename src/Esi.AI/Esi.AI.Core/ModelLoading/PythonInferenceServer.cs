using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Esi.AI.Core.Chat;
using Esi.AI.Core.Grpc;
using Esi.AI.Models;
using Grpc.Core;
using Grpc.Net.Client;

namespace Esi.AI.Core.ModelLoading;

/// <summary>
/// Starts the local Python inference bridge and exposes its gRPC-backed model session.
/// </summary>
public sealed class PythonInferenceServer : IDisposable
{
    private readonly BackendPrerequisiteProvisioner provisioner;
    private readonly object sync = new();
    private readonly HashSet<PythonInferenceChatSession> sessions = [];
    private Process? process;
    private GrpcChannel? channel;
    private Inference.InferenceClient? client;
    private string? modelPath;
    private string? modelId;
    private ConfigurationBackend backend;
    private string loadLog = string.Empty;

    /// <summary>Creates a Python inference server with automatic backend environment preparation.</summary>
    public PythonInferenceServer(BackendPrerequisiteProvisioner? provisioner = null)
    {
        this.provisioner = provisioner ?? new BackendPrerequisiteProvisioner();
    }

    /// <summary>Returns the current server status.</summary>
    public ModelLoadStatus GetStatus()
    {
        lock (sync)
        {
            var isRunning = process is { HasExited: false } && client is not null && modelPath is not null;
            return new ModelLoadStatus(
                isRunning ? modelPath : null,
                isRunning ? GetBackendName(backend) : string.Empty,
                0,
                0,
                0,
                0,
                [],
                null,
                loadLog,
                new Dictionary<string, float>(),
                isRunning,
                isRunning ? [new LoadedModelStatus(modelPath!, backend, GetBackendName(backend), 0, 0, 0, [], null, loadLog)] : []);
        }
    }

    /// <summary>Starts the local Python gRPC bridge and loads the configured model.</summary>
    public async Task LoadAsync(PythonInferenceLoadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ModelPath))
            throw new ArgumentException("A model path or Hugging Face model id is required.", nameof(request));
        if (request.Backend is not (ConfigurationBackend.Vllm or ConfigurationBackend.Sglang))
            throw new ArgumentException("The Python inference server supports only vLLM and SGLang.", nameof(request));
        if (request.Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(request), "The local gRPC port must be between 1 and 65535.");
        if (request.TensorParallelSize < 1)
            throw new ArgumentOutOfRangeException(nameof(request), "Tensor parallel size must be at least one.");

        var startupTimeout = request.StartupTimeout ?? TimeSpan.FromMinutes(10);
        if (startupTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request), "The startup timeout must be positive.");

        await StopAsync(cancellationToken).ConfigureAwait(false);
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Python", "inference_server.py");
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("The Python gRPC inference bridge was not deployed.", scriptPath);

        var preparation = await provisioner.PrepareAsync(
            request.Backend,
            request.PythonExecutable,
            AppContext.BaseDirectory,
            startupTimeout,
            cancellationToken).ConfigureAwait(false);
        var pythonExecutable = preparation.PythonExecutable;
        lock (sync)
            loadLog = preparation.Message;
        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExecutable,
            WorkingDirectory = request.WorkingDirectory ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            scriptPath,
            "--engine", GetEngineName(request.Backend),
            "--host", "127.0.0.1",
            "--grpc-port", request.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)
        })
            startInfo.ArgumentList.Add(argument);

        var newProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        newProcess.OutputDataReceived += CaptureOutput;
        newProcess.ErrorDataReceived += CaptureOutput;
        if (!newProcess.Start())
            throw new InvalidOperationException($"Could not start the {GetBackendName(request.Backend)} gRPC bridge.");
        newProcess.BeginOutputReadLine();
        newProcess.BeginErrorReadLine();

        var newChannel = GrpcChannel.ForAddress($"http://127.0.0.1:{request.Port}");
        var newClient = new Inference.InferenceClient(newChannel);
        lock (sync)
        {
            process = newProcess;
            channel = newChannel;
            client = newClient;
            modelPath = request.ModelPath;
            modelId = null;
            backend = request.Backend;
            loadLog = string.Concat(loadLog, Environment.NewLine,
                $"Started {GetBackendName(request.Backend)} gRPC bridge using {pythonExecutable}.");
        }

        try
        {
            using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startupCancellation.CancelAfter(startupTimeout);
            await WaitUntilReadyAsync(newClient, newProcess, startupTimeout, startupCancellation.Token).ConfigureAwait(false);
            var response = await newClient.LoadModelAsync(
                PythonInferenceGrpcMapper.ToGrpcRequest(request),
                cancellationToken: startupCancellation.Token).ResponseAsync.ConfigureAwait(false);
            if (!response.Succeeded)
                throw new InvalidOperationException($"{GetBackendName(request.Backend)} could not load '{request.ModelPath}': {response.Error}{Environment.NewLine}{loadLog}");

            modelId = response.ModelId;
            await WaitUntilModelReadyAsync(newClient, newProcess, startupTimeout, startupCancellation.Token).ConfigureAwait(false);
        }
        catch
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Creates a gRPC chat session against the active Python server.</summary>
    public PythonInferenceChatSession CreateChatSession()
    {
        lock (sync)
        {
            if (client is null || modelPath is null)
                throw new InvalidOperationException("No Python inference server is loaded.");

            var session = new PythonInferenceChatSession(
                client,
                () => GetBackendName(backend),
                () => modelId ?? modelPath,
                UnregisterSession);
            sessions.Add(session);
            return session;
        }
    }

    /// <summary>Stops the active bridge, cancels all streams and releases the gRPC channel.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Process? activeProcess;
        GrpcChannel? activeChannel;
        PythonInferenceChatSession[] activeSessions;
        lock (sync)
        {
            activeProcess = process;
            activeChannel = channel;
            activeSessions = sessions.ToArray();
            sessions.Clear();
            process = null;
            channel = null;
            client = null;
            modelPath = null;
            modelId = null;
        }

        foreach (var session in activeSessions)
            session.Dispose();
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

    /// <inheritdoc />
    public void Dispose() => StopAsync(CancellationToken.None).GetAwaiter().GetResult();

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
                throw new InvalidOperationException($"The Python gRPC bridge exited with code {activeProcess.ExitCode}. {loadLog}");
            try
            {
                var response = await activeClient.CheckReadinessAsync(new ReadinessRequest(), cancellationToken: timeout.Token).ResponseAsync.ConfigureAwait(false);
                if (response.Ready)
                    return;
            }
            catch (RpcException) when (timeout.Token.IsCancellationRequested is false)
            {
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token).ConfigureAwait(false);
        }
    }

    private async Task WaitUntilModelReadyAsync(
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
                throw new InvalidOperationException($"The Python gRPC bridge exited while loading the model. {loadLog}");
            try
            {
                var response = await activeClient.CheckReadinessAsync(new ReadinessRequest(), cancellationToken: timeout.Token).ResponseAsync.ConfigureAwait(false);
                if (response.Ready && response.ModelLoaded)
                {
                    modelId = string.IsNullOrWhiteSpace(response.ModelId) ? modelId : response.ModelId;
                    return;
                }
            }
            catch (RpcException) when (timeout.Token.IsCancellationRequested is false)
            {
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token).ConfigureAwait(false);
        }
    }

    private void CaptureOutput(object sender, DataReceivedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(args.Data))
            lock (sync)
                loadLog = string.Concat(loadLog, Environment.NewLine, args.Data);
    }

    private void UnregisterSession(PythonInferenceChatSession session)
    {
        lock (sync)
            sessions.Remove(session);
    }

    private static string GetEngineName(ConfigurationBackend value) => value == ConfigurationBackend.Vllm ? "vllm" : "sglang";
    private static string GetBackendName(ConfigurationBackend value) => value == ConfigurationBackend.Vllm ? "vLLM" : "SGLang";

}

/// <summary>Streams generation responses from the local Python gRPC bridge.</summary>
public sealed class PythonInferenceChatSession : IDisposable
{
    private readonly Func<Grpc.GenerateRequest, CancellationToken, IAsyncEnumerable<GenerateResponse>> generate;
    private readonly Func<string> backendName;
    private readonly Func<string> modelId;
    private readonly Action<PythonInferenceChatSession>? onDispose;
    private readonly CancellationTokenSource disposeCancellation = new();
    private int disposed;

    internal PythonInferenceChatSession(
        Inference.InferenceClient client,
        Func<string> backendName,
        Func<string> modelId,
        Action<PythonInferenceChatSession> onDispose)
        : this((request, cancellationToken) => StreamAsync(client, request, cancellationToken), backendName, modelId, onDispose)
    {
    }

    public PythonInferenceChatSession(
        Func<Grpc.GenerateRequest, CancellationToken, IAsyncEnumerable<GenerateResponse>> generate,
        Func<string> backendName,
        Func<string> modelId,
        Action<PythonInferenceChatSession>? onDispose = null)
    {
        this.generate = generate ?? throw new ArgumentNullException(nameof(generate));
        this.backendName = backendName ?? throw new ArgumentNullException(nameof(backendName));
        this.modelId = modelId ?? throw new ArgumentNullException(nameof(modelId));
        this.onDispose = onDispose;
    }

    /// <summary>Generates a response and collects token statistics from the gRPC stream.</summary>
    public async Task<LlamaGenerationResult> GenerateWithStatsAsync(
        IReadOnlyList<LlamaChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
            throw new ArgumentException("At least one chat message is required.", nameof(messages));
        ObjectDisposedException.ThrowIf(disposed != 0, this);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, disposeCancellation.Token);
        var request = PythonInferenceGrpcMapper.ToGrpcRequest(messages, modelId());
        var started = Stopwatch.StartNew();
        var text = new StringBuilder();
        var tokenCount = 0;
        var tokensPerSecond = 0d;

        await foreach (var response in generate(request, linkedCancellation.Token).WithCancellation(linkedCancellation.Token).ConfigureAwait(false))
        {
            if (!string.IsNullOrWhiteSpace(response.Error))
                throw new InvalidOperationException($"{backendName()} generation failed: {response.Error}");
            text.Append(response.Delta);
            tokenCount = Math.Max(tokenCount, (int)response.GeneratedTokens);
            if (response.TokensPerSecond > 0)
                tokensPerSecond = response.TokensPerSecond;
        }
        started.Stop();
        if (string.IsNullOrWhiteSpace(text.ToString()))
            throw new InvalidOperationException($"{backendName()} returned an empty answer.");
        if (tokensPerSecond <= 0 && started.Elapsed.TotalSeconds > 0)
            tokensPerSecond = tokenCount / started.Elapsed.TotalSeconds;
        return new LlamaGenerationResult(text.ToString(), tokenCount, started.Elapsed, tokensPerSecond);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
        onDispose?.Invoke(this);
    }

    private static async IAsyncEnumerable<GenerateResponse> StreamAsync(
        Inference.InferenceClient client,
        Grpc.GenerateRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var call = client.Generate(request, cancellationToken: cancellationToken);
        while (await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
            yield return call.ResponseStream.Current;
    }
}