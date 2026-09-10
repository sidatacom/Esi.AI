using System.Collections.Concurrent;
using Esi.AI.Core.Chat;
using Esi.AI.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Esi.AI.Core.ModelLoading;

/// <summary>
/// Coordinates all model runtimes and exposes one backend-independent loaded-model view.
/// </summary>
/// <summary>Exposes process-wide model runtime cleanup to application lifecycle services.</summary>
public interface IModelRuntimeShutdown
{
    /// <summary>Stops every active model runtime and releases its resources.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed class ModelRuntime : IHostedService, IModelRuntimeShutdown, IDisposable
{
    private readonly LlamaModelLoader llama;
    private readonly OpenVinoModelLoader openVino;
    private readonly PythonInferenceServer python;
    private readonly DotLlmInProcessRuntime dotLlm;
    private readonly LlamaRuntimeAdapter llamaAdapter;
    private readonly OpenVinoRuntimeAdapter openVinoAdapter;
    private readonly PythonRuntimeAdapter pythonAdapter;
    private readonly DotLlmRuntimeAdapter dotLlmAdapter;
    private readonly BackendRuntimeRegistry runtimeRegistry;
    private readonly BackendPrerequisiteProvisioner prerequisites;
    private readonly IModelRuntimeStatusPublisher statusPublisher;
    private readonly ModelLifecycleCoordinator lifecycleCoordinator;
    private readonly OpenVinoLoadGate openVinoLoadGate;
    private readonly ILogger<ModelRuntime> logger;
    private readonly ConcurrentDictionary<string, PendingModel> pendingModels = new(StringComparer.OrdinalIgnoreCase);
    private int stopStarted;

    public ModelRuntime()
        : this(new LlamaModelLoader(), new OpenVinoModelLoader(), new PythonInferenceServer(), new DotLlmInProcessRuntime())
    {
    }

    public ModelRuntime(LlamaModelLoader llama, OpenVinoModelLoader openVino)
        : this(llama, openVino, new PythonInferenceServer(), new DotLlmInProcessRuntime())
    {
    }

    public ModelRuntime(
        LlamaModelLoader llama,
        OpenVinoModelLoader openVino,
        PythonInferenceServer python,
        DotLlmInProcessRuntime dotLlm,
        BackendPrerequisiteProvisioner? prerequisites = null,
        IModelRuntimeStatusPublisher? statusPublisher = null,
        ModelLifecycleCoordinator? lifecycleCoordinator = null,
        ILogger<ModelRuntime>? logger = null,
        OpenVinoLoadGate? openVinoLoadGate = null)
    {
        this.llama = llama ?? throw new ArgumentNullException(nameof(llama));
        this.openVino = openVino ?? throw new ArgumentNullException(nameof(openVino));
        this.python = python ?? throw new ArgumentNullException(nameof(python));
        this.dotLlm = dotLlm ?? throw new ArgumentNullException(nameof(dotLlm));
        llamaAdapter = new LlamaRuntimeAdapter(this.llama);
        openVinoAdapter = new OpenVinoRuntimeAdapter(this.openVino);
        pythonAdapter = new PythonRuntimeAdapter(this.python);
        dotLlmAdapter = new DotLlmRuntimeAdapter(this.dotLlm);
        runtimeRegistry = new BackendRuntimeRegistry([llamaAdapter, openVinoAdapter, pythonAdapter, dotLlmAdapter]);
        this.prerequisites = prerequisites ?? new BackendPrerequisiteProvisioner();
        this.statusPublisher = statusPublisher ?? NoOpModelRuntimeStatusPublisher.Instance;
        this.lifecycleCoordinator = lifecycleCoordinator ?? new ModelLifecycleCoordinator();
        this.logger = logger ?? NullLogger<ModelRuntime>.Instance;
        this.openVinoLoadGate = openVinoLoadGate ?? new OpenVinoLoadGate();
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpenVinoModelLoader.InitializeRuntime();
        return Task.CompletedTask;
    }

    /// <summary>Reads the current active and loading loaded-model collection.</summary>
    public ModelLoadStatus LoadedModel_Read()
    {
        var llamaStatus = llama.GetStatus();
        var openVinoStatus = openVino.GetStatus();
        var pythonStatus = python.GetStatus();
        var dotLlmStatus = dotLlm.GetStatus();
        var loadedModels = llamaStatus.LoadedModels
            .Concat(CreateOpenVinoLoadedModels(openVinoStatus))
            .Concat(pythonStatus.LoadedModels)
            .Concat(dotLlmStatus.LoadedModels)
            .Concat(CreatePendingModelStatuses(llamaStatus, openVinoStatus, pythonStatus, dotLlmStatus))
            .ToArray();

        if (dotLlmStatus.IsModelLoaded)
            return dotLlmStatus with { LoadedModels = loadedModels };
        if (pythonStatus.IsModelLoaded || pythonStatus.ModelPath is not null)
            return pythonStatus with { LoadedModels = loadedModels };
        if (openVinoStatus.IsModelLoaded)
            return llamaStatus with
            {
                ModelPath = llamaStatus.IsModelLoaded ? llamaStatus.ModelPath : openVinoStatus.ModelPath,
                Backend = llamaStatus.IsModelLoaded ? llamaStatus.Backend : "OpenVINO",
                IsModelLoaded = true,
                LoadedModels = loadedModels
            };

        return llamaStatus with { LoadedModels = loadedModels };
    }

    public OpenVinoModelLoadStatus GetOpenVinoStatus() => openVino.GetStatus();

    /// <summary>Returns the current lifecycle state for all model loading operations.</summary>
    public IReadOnlyList<ModelLifecycleState> ReadLifecycleStates() => lifecycleCoordinator.ReadAll();

    public bool SupportsImageInput(string backend, string? modelPath)
    {
        try
        {
            return runtimeRegistry.Resolve(backend).SupportsImageInput(modelPath);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public Task LoadAsync(LoadModelRequest request, CancellationToken cancellationToken = default) =>
        TrackPendingModelAsync(request.ModelPath, ConfigurationBackend.Llama, request.Backend, async () =>
        {
            await prerequisites.PrepareAsync(
                ConfigurationBackend.Llama,
                cancellationToken: cancellationToken,
                devices: [$"{request.Backend.ToLowerInvariant()}:0"]).ConfigureAwait(false);
            await llamaAdapter.LoadAsync(request, cancellationToken).ConfigureAwait(false);
        });

    public Task LoadAsync(
        OpenVinoLoadRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteOpenVinoLoadAsync(() => TrackPendingModelAsync(request.ModelPath, ConfigurationBackend.OpenVino, request.Device, async () =>
        {
            await prerequisites.PrepareAsync(ConfigurationBackend.OpenVino, cancellationToken: cancellationToken).ConfigureAwait(false);
            await openVinoAdapter.LoadAsync(request, cancellationToken).ConfigureAwait(false);
        }));

    public Task LoadAsync(PythonInferenceLoadRequest request, CancellationToken cancellationToken = default) =>
        TrackPendingModelAsync(request.ModelPath, request.Backend, request.Backend switch
        {
            ConfigurationBackend.Vllm => "vLLM",
            ConfigurationBackend.Sglang => "SGLang",
            _ => request.Backend.ToString()
        }, () => pythonAdapter.LoadAsync(request, cancellationToken));

    public Task LoadAsync(DotLlmLoadRequest request, CancellationToken cancellationToken = default) =>
        TrackPendingModelAsync(request.ModelPath, ConfigurationBackend.DotLlm, "dotLLM / In-Process", async () =>
        {
            await prerequisites.PrepareAsync(ConfigurationBackend.DotLlm, cancellationToken: cancellationToken).ConfigureAwait(false);
            await dotLlmAdapter.LoadAsync(request, cancellationToken).ConfigureAwait(false);
        });

    public Task LoadLlamaAsync(
        string modelPath,
        string backend,
        int gpuLayerCount,
        uint contextSize,
        IReadOnlyDictionary<string, float>? vulkanDeviceWeights,
        LlamaLoadOptions? advanced,
        CancellationToken cancellationToken = default) =>
        llama.LoadAsync(modelPath, backend, gpuLayerCount, contextSize, vulkanDeviceWeights, advanced, cancellationToken);

    public async Task StopLlamaAsync(CancellationToken cancellationToken = default)
    {
        await llama.StopAsync(cancellationToken).ConfigureAwait(false);
        await statusPublisher.LoadedModel_DeleteAsync(LoadedModel_Read(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops every active model runtime and releases its loaded model resources.
    /// </summary>
    /// <param name="cancellationToken">A token that observes the host shutdown deadline.</param>
    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref stopStarted, 1) != 0)
            return;

        var failures = new List<Exception>();
        await StopRuntimeAsync("LLama", () => llama.StopAsync(cancellationToken), failures).ConfigureAwait(false);
        await StopRuntimeAsync("OpenVINO", () => ExecuteOpenVinoOperationAsync(() => openVino.UnloadAsync(cancellationToken)), failures).ConfigureAwait(false);
        await StopRuntimeAsync("Python", () => python.StopAsync(cancellationToken), failures).ConfigureAwait(false);
        await StopRuntimeAsync("dotLLM", () => dotLlm.StopAsync(cancellationToken), failures).ConfigureAwait(false);
        await PublishStatusAsync(
            "LoadedModel_Delete",
            () => statusPublisher.LoadedModel_DeleteAsync(LoadedModel_Read(), cancellationToken)).ConfigureAwait(false);

        if (failures.Count > 0)
            throw new AggregateException("One or more model runtimes failed to stop.", failures);
    }

    public async Task UnloadLlamaAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        await llama.UnloadAsync(modelPath, cancellationToken).ConfigureAwait(false);
        await statusPublisher.LoadedModel_DeleteAsync(LoadedModel_Read(), cancellationToken).ConfigureAwait(false);
    }

    public LlamaChatSession CreateLlamaChatSession(string systemPrompt, string? modelPath = null) =>
        llama.CreateChatSession(systemPrompt, modelPath);

    public Task LoadOpenVinoAsync(
        string modelPath,
        string device,
        CancellationToken cancellationToken,
        OpenVinoGenerationOptions? generationOptions,
        string? cacheDirectory,
        OpenVinoNpuOptions? npuOptions) =>
        ExecuteOpenVinoLoadAsync(() => openVino.LoadAsync(modelPath, device, cancellationToken, generationOptions, cacheDirectory, npuOptions));

    public OpenVinoChatSession CreateOpenVinoChatSession() => openVino.CreateChatSession();

    public async Task UnloadOpenVinoAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteOpenVinoOperationAsync(() => openVino.UnloadAsync(cancellationToken)).ConfigureAwait(false);
        await statusPublisher.LoadedModel_DeleteAsync(LoadedModel_Read(), cancellationToken).ConfigureAwait(false);
    }

    public async Task StopPythonAsync(CancellationToken cancellationToken = default)
    {
        await python.StopAsync(cancellationToken).ConfigureAwait(false);
        await statusPublisher.LoadedModel_DeleteAsync(LoadedModel_Read(), cancellationToken).ConfigureAwait(false);
    }

    public PythonInferenceChatSession CreatePythonChatSession() => python.CreateChatSession();

    public DotLlmInProcessChatSession CreateDotLlmChatSession() => dotLlm.CreateChatSession();

    public async Task StopDotLlmAsync(CancellationToken cancellationToken = default)
    {
        await dotLlm.StopAsync(cancellationToken).ConfigureAwait(false);
        await statusPublisher.LoadedModel_DeleteAsync(LoadedModel_Read(), cancellationToken).ConfigureAwait(false);
    }

    public async Task UnloadAsync(string modelPath, ConfigurationBackend backend, CancellationToken cancellationToken = default)
    {
        if (backend == ConfigurationBackend.OpenVino)
            await ExecuteOpenVinoOperationAsync(() => openVino.UnloadAsync(cancellationToken));
        else if (backend is ConfigurationBackend.Vllm or ConfigurationBackend.Sglang)
            await python.StopAsync(cancellationToken);
        else if (backend == ConfigurationBackend.DotLlm)
            await dotLlm.StopAsync(cancellationToken);
        else
            await llama.UnloadAsync(modelPath, cancellationToken);

        await statusPublisher.LoadedModel_DeleteAsync(LoadedModel_Read(), cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        llama.Dispose();
        openVino.Dispose();
        python.Dispose();
        dotLlm.Dispose();
    }

    private async Task TrackPendingModelAsync(string modelPath, ConfigurationBackend backend, string runtime, Func<Task> load)
    {
        var key = $"{backend}|{modelPath}";
        pendingModels[key] = new PendingModel(modelPath, backend, runtime);
        lifecycleCoordinator.Begin(modelPath, backend, runtime);
        var completed = false;
        Exception? loadFailure = null;
        try
        {
            await PublishStatusAsync(
                "LoadedModel_Create",
                () => statusPublisher.LoadedModel_CreateAsync(LoadedModel_Read())).ConfigureAwait(false);
            await load().ConfigureAwait(false);
            completed = true;
        }
        catch (Exception exception)
        {
            loadFailure = exception;
            throw;
        }
        finally
        {
            pendingModels.TryRemove(key, out _);
            if (completed)
                lifecycleCoordinator.Complete(modelPath, backend, runtime);
            else if (loadFailure is not null)
                lifecycleCoordinator.Fail(modelPath, backend, runtime, loadFailure.Message);
            var status = LoadedModel_Read();
            await PublishStatusAsync(
                "LoadedModel_Update",
                () => statusPublisher.LoadedModel_UpdateAsync(status)).ConfigureAwait(false);
            if (!completed)
            {
                await PublishStatusAsync(
                    "LoadedModel_Delete",
                    () => statusPublisher.LoadedModel_DeleteAsync(status)).ConfigureAwait(false);
            }
        }
    }

    private async Task ExecuteOpenVinoLoadAsync(Func<Task> load)
        => await ExecuteOpenVinoOperationAsync(load).ConfigureAwait(false);

    private async Task ExecuteOpenVinoOperationAsync(Func<Task> operation)
    {
        if (!openVinoLoadGate.TryEnter())
            throw new InvalidOperationException("An OpenVINO model operation is already in progress.");

        try
        {
            await operation().ConfigureAwait(false);
        }
        finally
        {
            openVinoLoadGate.Exit();
        }
    }

    private async Task PublishStatusAsync(string eventName, Func<Task> publish)
    {
        try
        {
            await publish().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to publish model runtime status event {EventName}.", eventName);
        }
    }

    private async Task StopRuntimeAsync(string runtime, Func<Task> stop, ICollection<Exception> failures)
    {
        try
        {
            await stop().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            logger.LogError(exception, "Failed to stop the {Runtime} model runtime.", runtime);
        }
    }

    private IReadOnlyList<LoadedModelStatus> CreatePendingModelStatuses(
        ModelLoadStatus llamaStatus,
        OpenVinoModelLoadStatus openVinoStatus,
        ModelLoadStatus pythonStatus,
        ModelLoadStatus dotLlmStatus) =>
        pendingModels.Values.Select(pending => new LoadedModelStatus(
            pending.ModelPath,
            pending.Backend,
            pending.Runtime,
            0,
            0,
            0,
            [],
            null,
            GetPendingLoadLog(pending.Backend, llamaStatus, openVinoStatus, pythonStatus, dotLlmStatus),
            true)).ToArray();

    private static string GetPendingLoadLog(
        ConfigurationBackend backend,
        ModelLoadStatus llamaStatus,
        OpenVinoModelLoadStatus openVinoStatus,
        ModelLoadStatus pythonStatus,
        ModelLoadStatus dotLlmStatus) => backend switch
        {
            ConfigurationBackend.Llama => llamaStatus.LoadLog,
            ConfigurationBackend.OpenVino => openVinoStatus.LoadLog,
            ConfigurationBackend.Vllm or ConfigurationBackend.Sglang => pythonStatus.LoadLog,
            ConfigurationBackend.DotLlm => dotLlmStatus.LoadLog,
            _ => string.Empty
        };

    private static IEnumerable<LoadedModelStatus> CreateOpenVinoLoadedModels(OpenVinoModelLoadStatus status)
    {
        if (!status.IsModelLoaded || string.IsNullOrWhiteSpace(status.ModelPath))
            return [];

        var modelSize = File.Exists(status.ModelPath)
            ? (ulong)new FileInfo(status.ModelPath).Length
            : 0;
        return [new LoadedModelStatus(
            status.ModelPath,
            ConfigurationBackend.OpenVino,
            status.Device ?? "OpenVINO",
            0,
            0,
            modelSize,
            status.VramUsageMiB is double vramUsageMiB && !string.IsNullOrWhiteSpace(status.Device)
                ? [new VulkanDeviceStatus(status.Device, "OpenVINO", 0, vramUsageMiB, "Intel", "OpenVINO")]
                : [],
            null,
            status.LoadLog)];
    }

    private sealed record PendingModel(string ModelPath, ConfigurationBackend Backend, string Runtime);

    private sealed class NoOpModelRuntimeStatusPublisher : IModelRuntimeStatusPublisher
    {
        public static NoOpModelRuntimeStatusPublisher Instance { get; } = new();

        public Task LoadedModel_CreateAsync(ModelLoadStatus status, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LoadedModel_UpdateAsync(ModelLoadStatus status, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LoadedModel_DeleteAsync(ModelLoadStatus status, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}