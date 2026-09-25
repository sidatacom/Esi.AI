using System.Collections.Concurrent;
using System.Text.Json;
using Esi.AI.Backend.Abstractions;
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
    private static readonly JsonSerializerOptions BackendConfigurationJsonOptions = new(JsonSerializerDefaults.Web);
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
    private readonly IBackendRuntimeResolver? backendRuntimeResolver;
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
        OpenVinoLoadGate? openVinoLoadGate = null,
        IBackendRuntimeResolver? backendRuntimeResolver = null)
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
        this.backendRuntimeResolver = backendRuntimeResolver;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (backendRuntimeResolver?.Runtimes.Any(runtime => runtime.Descriptor.Family == ConfigurationBackend.OpenVino) != true)
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
        var backendRuntimeStatuses = backendRuntimeResolver?.Runtimes.Select(runtime => runtime.GetStatus()).ToArray() ?? [];
        var backendLoadedModels = backendRuntimeStatuses.SelectMany(status => status.LoadedModels).ToArray();
        var loadedModels = llamaStatus.LoadedModels
            .Concat(CreateOpenVinoLoadedModels(openVinoStatus))
            .Concat(pythonStatus.LoadedModels)
            .Concat(dotLlmStatus.LoadedModels)
            .Concat(backendLoadedModels)
            .Concat(CreatePendingModelStatuses(llamaStatus, openVinoStatus, pythonStatus, dotLlmStatus, backendLoadedModels))
            .ToArray();

        var activeBackendStatus = backendRuntimeStatuses.FirstOrDefault(status => status.IsModelLoaded);
        if (activeBackendStatus is not null)
            return activeBackendStatus with { LoadedModels = loadedModels };
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

    /// <summary>Reads the LLama status without allowing another backend to replace the response.</summary>
    public ModelLoadStatus LoadedLlamaModel_Read()
    {
        var aggregateStatus = LoadedModel_Read();
        return llama.GetStatus() with { LoadedModels = aggregateStatus.LoadedModels };
    }

    public OpenVinoModelLoadStatus GetOpenVinoStatus()
    {
        var packagedStatus = TryResolveBackendRuntime("openvino")?.GetStatus();
        if (packagedStatus is not null)
            return new OpenVinoModelLoadStatus(
                packagedStatus.ModelPath,
                packagedStatus.Backend,
                packagedStatus.IsModelLoaded,
                null,
                null,
                packagedStatus.LoadLog);
        return openVino.GetStatus();
    }

    /// <summary>Returns the current lifecycle state for all model loading operations.</summary>
    public IReadOnlyList<ModelLifecycleState> ReadLifecycleStates() => lifecycleCoordinator.ReadAll();

    public bool SupportsImageInput(string backend, string? modelPath)
    {
        var matchingRuntimes = backendRuntimeResolver?.Runtimes
            .Where(runtime => runtime.GetStatus().LoadedModels.Any(model =>
                string.Equals(model.ModelPath, modelPath, StringComparison.OrdinalIgnoreCase)))
            .ToArray() ?? [];
        if (matchingRuntimes.Length == 1)
            return matchingRuntimes[0].SupportsImageInput(modelPath);
        if (matchingRuntimes.Length > 1)
        {
            var matchingRoute = matchingRuntimes.SingleOrDefault(runtime =>
                string.Equals(runtime.Descriptor.Route, backend, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(runtime.Descriptor.Id, backend, StringComparison.OrdinalIgnoreCase));
            if (matchingRoute is not null)
                return matchingRoute.SupportsImageInput(modelPath);
        }

        try
        {
            return runtimeRegistry.Resolve(backend).SupportsImageInput(modelPath);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public Task LoadAsync(LoadModelRequest request, CancellationToken cancellationToken = default)
    {
        var packagedRuntime = TryResolveBackendRuntime(ConfigurationBackend.Llama, request.Backend);
        if (packagedRuntime is not null)
        {
            var packagedRequest = new BackendLoadRequest(
                request.ModelPath,
                packagedRuntime.Descriptor.Id,
                JsonSerializer.SerializeToElement(request, BackendConfigurationJsonOptions));
            return LoadBackendAsync(packagedRequest, cancellationToken);
        }

        return TrackPendingModelAsync(request.ModelPath, ConfigurationBackend.Llama, request.Backend, async () =>
        {
            await prerequisites.PrepareAsync(
                ConfigurationBackend.Llama,
                cancellationToken: cancellationToken,
                devices: [$"{request.Backend.ToLowerInvariant()}:0"]).ConfigureAwait(false);
            await llamaAdapter.LoadAsync(request, cancellationToken).ConfigureAwait(false);
        });
    }

    public Task LoadAsync(
        OpenVinoLoadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (TryResolveBackendRuntime("openvino") is { } packagedRuntime)
        {
            var packagedRequest = new BackendLoadRequest(
                request.ModelPath,
                packagedRuntime.Descriptor.Id,
                JsonSerializer.SerializeToElement(request, BackendConfigurationJsonOptions));
            return LoadBackendAsync(packagedRequest, cancellationToken);
        }

        return ExecuteOpenVinoLoadAsync(() => TrackPendingModelAsync(request.ModelPath, ConfigurationBackend.OpenVino, request.Device, async () =>
        {
            await prerequisites.PrepareAsync(ConfigurationBackend.OpenVino, cancellationToken: cancellationToken).ConfigureAwait(false);
            await openVinoAdapter.LoadAsync(request, cancellationToken).ConfigureAwait(false);
        }));
    }

    public Task LoadAsync(PythonInferenceLoadRequest request, CancellationToken cancellationToken = default)
    {
        var variantId = request.Backend == ConfigurationBackend.Vllm
            ? request.EnableXpuGraph || request.Device.Contains("xpu", StringComparison.OrdinalIgnoreCase) || request.Devices?.Any(device => device.Contains("xpu", StringComparison.OrdinalIgnoreCase)) == true
                ? "vllm.xpu"
                : "vllm.cuda12"
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(variantId) && TryResolveBackendRuntime(variantId) is { } packagedRuntime)
        {
            var packagedRequest = new BackendLoadRequest(
                request.ModelPath,
                packagedRuntime.Descriptor.Id,
                JsonSerializer.SerializeToElement(request, BackendConfigurationJsonOptions));
            return LoadBackendAsync(packagedRequest, cancellationToken);
        }

        return TrackPendingModelAsync(request.ModelPath, request.Backend, request.Backend switch
        {
            ConfigurationBackend.Vllm => "vLLM",
            ConfigurationBackend.Sglang => "SGLang",
            _ => request.Backend.ToString()
        }, () => pythonAdapter.LoadAsync(request, cancellationToken));
    }

    public Task LoadBackendAsync(BackendLoadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var runtime = backendRuntimeResolver?.Resolve(request.VariantId)
            ?? throw new InvalidOperationException("No packaged backend runtime resolver is registered.");
        if (runtime.Descriptor.Family == ConfigurationBackend.Sglang || runtime.Descriptor.Family == ConfigurationBackend.DotLlm)
            throw new InvalidOperationException($"Backend family '{runtime.Descriptor.Family}' cannot be loaded by a packaged runtime in this host.");

        return TrackPendingModelAsync(request.ModelPath, runtime.Descriptor.Family, runtime.Descriptor.RuntimeName,
            () => runtime.LoadAsync(request, cancellationToken), request.VariantId);
    }

    public bool HasBackendRuntime(string variantId) => TryResolveBackendRuntime(variantId) is not null;

    public string? GetBackendRoute(string variantId) => TryResolveBackendRuntime(variantId)?.Descriptor.Route;

    public string? GetLoadedBackendVariantId(string? modelPath)
    {
        if (backendRuntimeResolver is null || string.IsNullOrWhiteSpace(modelPath))
            return null;
        var matches = backendRuntimeResolver.Runtimes
            .SelectMany(runtime => runtime.GetStatus().LoadedModels)
            .Where(model => model.IsModelLoaded && string.Equals(model.ModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
            .Select(model => model.BackendVariantId)
            .Where(variantId => !string.IsNullOrWhiteSpace(variantId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException($"Multiple packaged backend variants have loaded '{modelPath}'.")
        };
    }

    public Task<GenerationResult> GenerateBackendAsync(
        OpenAiBackendChatRequest request,
        Func<string, Task>? onToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.BackendVariantId))
            throw new ArgumentException("A packaged backend variant ID is required.", nameof(request));
        var runtime = backendRuntimeResolver?.Resolve(request.BackendVariantId)
            ?? throw new InvalidOperationException("No packaged backend runtime resolver is registered.");
        return runtime.GenerateAsync(request, onToken, cancellationToken);
    }

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
        if (backendRuntimeResolver is not null)
        {
            foreach (var runtime in backendRuntimeResolver.Runtimes.Where(runtime => runtime.Descriptor.Family == ConfigurationBackend.Llama))
                await runtime.StopAsync(cancellationToken).ConfigureAwait(false);
        }
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
        if (backendRuntimeResolver is not null)
        {
            foreach (var runtime in backendRuntimeResolver.Runtimes)
                await StopRuntimeAsync(runtime.Descriptor.Id, () => runtime.StopAsync(cancellationToken), failures).ConfigureAwait(false);
        }
        await PublishStatusAsync(
            "LoadedModel_Delete",
            () => statusPublisher.LoadedModel_DeleteAsync(LoadedModel_Read(), cancellationToken)).ConfigureAwait(false);

        if (failures.Count > 0)
            throw new AggregateException("One or more model runtimes failed to stop.", failures);
    }

    public async Task UnloadLlamaAsync(string modelPath, CancellationToken cancellationToken = default)
    {
        var variantId = GetLoadedBackendVariantId(modelPath);
        if (!string.IsNullOrWhiteSpace(variantId))
        {
            await backendRuntimeResolver!.Resolve(variantId).UnloadAsync(modelPath, cancellationToken).ConfigureAwait(false);
            await statusPublisher.LoadedModel_DeleteAsync(LoadedModel_Read(), cancellationToken).ConfigureAwait(false);
            return;
        }

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
        var packagedRuntime = TryResolveBackendRuntime("openvino");
        var packagedModelPath = packagedRuntime?.GetStatus().ModelPath;
        if (packagedRuntime is not null && !string.IsNullOrWhiteSpace(packagedModelPath))
        {
            await packagedRuntime.UnloadAsync(packagedModelPath, cancellationToken).ConfigureAwait(false);
            await statusPublisher.LoadedModel_DeleteAsync(LoadedModel_Read(), cancellationToken).ConfigureAwait(false);
            return;
        }

        await ExecuteOpenVinoOperationAsync(() => openVino.UnloadAsync(cancellationToken)).ConfigureAwait(false);
        await statusPublisher.LoadedModel_DeleteAsync(LoadedModel_Read(), cancellationToken).ConfigureAwait(false);
    }

    public async Task StopPythonAsync(CancellationToken cancellationToken = default)
    {
        await python.StopAsync(cancellationToken).ConfigureAwait(false);
        if (backendRuntimeResolver is not null)
        {
            foreach (var runtime in backendRuntimeResolver.Runtimes.Where(runtime => runtime.Descriptor.Family == ConfigurationBackend.Vllm))
                await runtime.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        await statusPublisher.LoadedModel_DeleteAsync(LoadedModel_Read(), cancellationToken).ConfigureAwait(false);
    }

    public PythonInferenceChatSession CreatePythonChatSession() => python.CreateChatSession();

    public DotLlmInProcessChatSession CreateDotLlmChatSession() => dotLlm.CreateChatSession();

    public async Task StopDotLlmAsync(CancellationToken cancellationToken = default)
    {
        await dotLlm.StopAsync(cancellationToken).ConfigureAwait(false);
        await statusPublisher.LoadedModel_DeleteAsync(LoadedModel_Read(), cancellationToken).ConfigureAwait(false);
    }

    public async Task UnloadAsync(
        string modelPath,
        ConfigurationBackend backend,
        CancellationToken cancellationToken = default,
        string backendVariantId = "")
    {
        var packagedVariantId = string.IsNullOrWhiteSpace(backendVariantId)
            ? GetLoadedBackendVariantId(modelPath)
            : backendVariantId;
        if (!string.IsNullOrWhiteSpace(packagedVariantId))
        {
            var packagedRuntime = backendRuntimeResolver!.Resolve(packagedVariantId);
            if (packagedRuntime.Descriptor.Family != backend)
                throw new ArgumentException(
                    $"Backend variant '{packagedVariantId}' belongs to '{packagedRuntime.Descriptor.Family}', not '{backend}'.",
                    nameof(backendVariantId));

            await packagedRuntime.UnloadAsync(modelPath, cancellationToken).ConfigureAwait(false);
            await statusPublisher.LoadedModel_DeleteAsync(LoadedModel_Read(), cancellationToken).ConfigureAwait(false);
            return;
        }

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

    private async Task TrackPendingModelAsync(
        string modelPath,
        ConfigurationBackend backend,
        string runtime,
        Func<Task> load,
        string backendVariantId = "")
    {
        var key = $"{backend}|{backendVariantId}|{modelPath}";
        pendingModels[key] = new PendingModel(modelPath, backend, runtime, backendVariantId);
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
        ModelLoadStatus dotLlmStatus,
        IReadOnlyCollection<LoadedModelStatus> backendLoadedModels) =>
        pendingModels.Values
            .Where(pending => !IsAlreadyLoaded(pending, llamaStatus, openVinoStatus, pythonStatus, dotLlmStatus, backendLoadedModels))
            .Select(pending => new LoadedModelStatus(
                pending.ModelPath,
                pending.Backend,
                pending.Runtime,
                0,
                0,
                0,
                [],
                null,
                GetPendingLoadLog(pending.Backend, llamaStatus, openVinoStatus, pythonStatus, dotLlmStatus),
                true,
                false,
                pending.BackendVariantId)).ToArray();

    private static bool IsAlreadyLoaded(
        PendingModel pending,
        ModelLoadStatus llamaStatus,
        OpenVinoModelLoadStatus openVinoStatus,
        ModelLoadStatus pythonStatus,
        ModelLoadStatus dotLlmStatus,
        IReadOnlyCollection<LoadedModelStatus> backendLoadedModels)
    {
        if (!string.IsNullOrWhiteSpace(pending.BackendVariantId))
            return backendLoadedModels.Any(model =>
                model.IsModelLoaded &&
                string.Equals(model.BackendVariantId, pending.BackendVariantId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(model.ModelPath, pending.ModelPath, StringComparison.OrdinalIgnoreCase));

        if (pending.Backend == ConfigurationBackend.OpenVino)
            return openVinoStatus.IsModelLoaded && string.Equals(openVinoStatus.ModelPath, pending.ModelPath, StringComparison.OrdinalIgnoreCase);

        var loadedModels = pending.Backend switch
        {
            ConfigurationBackend.Llama => llamaStatus.LoadedModels,
            ConfigurationBackend.Vllm or ConfigurationBackend.Sglang => pythonStatus.LoadedModels,
            ConfigurationBackend.DotLlm => dotLlmStatus.LoadedModels,
            _ => []
        };
        return loadedModels.Any(model =>
            model.Backend == pending.Backend &&
            string.Equals(model.ModelPath, pending.ModelPath, StringComparison.OrdinalIgnoreCase));
    }

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
                ? [new DeviceStatus(status.Device, "OpenVINO", 0, vramUsageMiB, "Intel", "OpenVINO", status.VramTotalMiB)]
                : [],
            null,
            status.LoadLog,
            IsModelLoaded: true)];
    }

    private IBackendRuntime? TryResolveBackendRuntime(string variantId) =>
        string.IsNullOrWhiteSpace(variantId)
            ? null
            : backendRuntimeResolver?.Runtimes.FirstOrDefault(runtime =>
                string.Equals(runtime.Descriptor.Id, variantId, StringComparison.OrdinalIgnoreCase));

    private IBackendRuntime? TryResolveBackendRuntime(ConfigurationBackend family, string route) =>
        backendRuntimeResolver?.Runtimes.FirstOrDefault(runtime =>
            runtime.Descriptor.Family == family &&
            string.Equals(runtime.Descriptor.Route, route, StringComparison.OrdinalIgnoreCase));

    private sealed record PendingModel(string ModelPath, ConfigurationBackend Backend, string Runtime, string BackendVariantId);

    private sealed class NoOpModelRuntimeStatusPublisher : IModelRuntimeStatusPublisher
    {
        public static NoOpModelRuntimeStatusPublisher Instance { get; } = new();

        public Task LoadedModel_CreateAsync(ModelLoadStatus status, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LoadedModel_UpdateAsync(ModelLoadStatus status, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LoadedModel_DeleteAsync(ModelLoadStatus status, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}