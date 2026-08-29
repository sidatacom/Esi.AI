using Esi.AI.Core.Chat;
using Esi.AI.Models;
using Microsoft.Extensions.Hosting;

namespace Esi.AI.Core.ModelLoading;

/// <summary>
/// Coordinates all model runtimes and exposes one backend-independent loaded-model view.
/// </summary>
public sealed class ModelRuntime : IHostedService, IDisposable
{
    private readonly LlamaModelLoader llama;
    private readonly OpenVinoModelLoader openVino;
    private readonly PythonInferenceServer python;
    private readonly DotLlmInProcessRuntime dotLlm;
    private readonly BackendPrerequisiteProvisioner prerequisites;

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
        BackendPrerequisiteProvisioner? prerequisites = null)
    {
        this.llama = llama ?? throw new ArgumentNullException(nameof(llama));
        this.openVino = openVino ?? throw new ArgumentNullException(nameof(openVino));
        this.python = python ?? throw new ArgumentNullException(nameof(python));
        this.dotLlm = dotLlm ?? throw new ArgumentNullException(nameof(dotLlm));
        this.prerequisites = prerequisites ?? new BackendPrerequisiteProvisioner();
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpenVinoModelLoader.InitializeRuntime();
        return Task.CompletedTask;
    }

    public ModelLoadStatus GetStatus()
    {
        var llamaStatus = llama.GetStatus();
        var openVinoStatus = openVino.GetStatus();
        var pythonStatus = python.GetStatus();
        var dotLlmStatus = dotLlm.GetStatus();
        var loadedModels = llamaStatus.LoadedModels
            .Concat(CreateOpenVinoLoadedModels(openVinoStatus))
            .Concat(pythonStatus.LoadedModels)
            .Concat(dotLlmStatus.LoadedModels)
            .ToArray();

        if (dotLlmStatus.IsModelLoaded)
            return dotLlmStatus with { LoadedModels = loadedModels };
        if (pythonStatus.IsModelLoaded)
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

    public async Task LoadAsync(LoadModelRequest request, CancellationToken cancellationToken = default)
    {
        await prerequisites.PrepareAsync(ConfigurationBackend.Llama, cancellationToken: cancellationToken).ConfigureAwait(false);
        var advanced = request.Advanced;
        await llama.LoadAsync(request.ModelPath, request.Backend, request.GpuLayerCount, request.ContextSize,
            request.VulkanDeviceWeights,
            new LlamaLoadOptions(advanced.MainGpu, advanced.SeqMax, advanced.RecurrentRollbackSnapshots, advanced.UseMemorymap,
                advanced.UseDirectIO, advanced.UseMemoryLock, advanced.Threads, advanced.BatchThreads, advanced.BatchSize,
                advanced.UBatchSize, advanced.Embeddings, advanced.NoKqvOffload, advanced.FlashAttention, advanced.VocabOnly,
                advanced.OpOffload, advanced.SwaFull, advanced.KVUnified, advanced.RopeFrequencyBase, advanced.RopeFrequencyScale,
                advanced.YarnExtrapolationFactor, advanced.YarnAttentionFactor, advanced.YarnBetaFast, advanced.YarnBetaSlow,
                advanced.YarnOriginalContext), cancellationToken).ConfigureAwait(false);
    }

    public async Task LoadAsync(
        OpenVinoLoadRequest request,
        CancellationToken cancellationToken = default)
    {
        await prerequisites.PrepareAsync(ConfigurationBackend.OpenVino, cancellationToken: cancellationToken).ConfigureAwait(false);
        await openVino.LoadAsync(
            request.ModelPath,
            request.Device,
            cancellationToken,
            new OpenVinoGenerationOptions(request.MaxNewTokens, request.Temperature, request.TopP, request.DoSample, request.TopK, request.RepetitionPenalty),
            request.CacheDirectory,
            new OpenVinoNpuOptions(
                request.Npu?.MaxPromptLength ?? 1024,
                request.Npu?.MinResponseLength ?? 128,
                request.Npu?.PrefillHint ?? "DYNAMIC",
                request.Npu?.GenerateHint ?? "FAST_COMPILE")).ConfigureAwait(false);
    }

    public async Task LoadAsync(PythonInferenceLoadRequest request, CancellationToken cancellationToken = default)
    {
        var preparation = await prerequisites.PrepareAsync(
            request.Backend,
            request.PythonExecutable,
            AppContext.BaseDirectory,
            request.StartupTimeout,
            cancellationToken).ConfigureAwait(false);
        await python.LoadAsync(request with { PythonExecutable = preparation.PythonExecutable! }, cancellationToken).ConfigureAwait(false);
    }

    public async Task LoadAsync(DotLlmLoadRequest request, CancellationToken cancellationToken = default)
    {
        await prerequisites.PrepareAsync(ConfigurationBackend.DotLlm, cancellationToken: cancellationToken).ConfigureAwait(false);
        await dotLlm.LoadAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public Task LoadLlamaAsync(
        string modelPath,
        string backend,
        int gpuLayerCount,
        uint contextSize,
        IReadOnlyDictionary<string, float>? vulkanDeviceWeights,
        LlamaLoadOptions? advanced,
        CancellationToken cancellationToken = default) =>
        llama.LoadAsync(modelPath, backend, gpuLayerCount, contextSize, vulkanDeviceWeights, advanced, cancellationToken);

    public Task StopLlamaAsync(CancellationToken cancellationToken = default) => llama.StopAsync(cancellationToken);

    /// <summary>
    /// Stops every active model runtime and releases its loaded model resources.
    /// </summary>
    /// <param name="cancellationToken">A token that observes the host shutdown deadline.</param>
    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await llama.StopAsync(cancellationToken).ConfigureAwait(false);
        await openVino.UnloadAsync(cancellationToken).ConfigureAwait(false);
        await python.StopAsync(cancellationToken).ConfigureAwait(false);
        await dotLlm.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task UnloadLlamaAsync(string modelPath, CancellationToken cancellationToken = default) =>
        llama.UnloadAsync(modelPath, cancellationToken);

    public LlamaChatSession CreateLlamaChatSession(string systemPrompt, string? modelPath = null) =>
        llama.CreateChatSession(systemPrompt, modelPath);

    public Task LoadOpenVinoAsync(
        string modelPath,
        string device,
        CancellationToken cancellationToken,
        OpenVinoGenerationOptions? generationOptions,
        string? cacheDirectory,
        OpenVinoNpuOptions? npuOptions) =>
        openVino.LoadAsync(modelPath, device, cancellationToken, generationOptions, cacheDirectory, npuOptions);

    public OpenVinoChatSession CreateOpenVinoChatSession() => openVino.CreateChatSession();

    public Task UnloadOpenVinoAsync(CancellationToken cancellationToken = default) =>
        openVino.UnloadAsync(cancellationToken);

    public Task StopPythonAsync(CancellationToken cancellationToken = default) => python.StopAsync(cancellationToken);

    public PythonInferenceChatSession CreatePythonChatSession() => python.CreateChatSession();

    public DotLlmInProcessChatSession CreateDotLlmChatSession() => dotLlm.CreateChatSession();

    public Task StopDotLlmAsync(CancellationToken cancellationToken = default) => dotLlm.StopAsync(cancellationToken);

    public async Task UnloadAsync(string modelPath, ConfigurationBackend backend, CancellationToken cancellationToken = default)
    {
        if (backend == ConfigurationBackend.OpenVino)
            await openVino.UnloadAsync(cancellationToken);
        else if (backend is ConfigurationBackend.Vllm or ConfigurationBackend.Sglang)
            await python.StopAsync(cancellationToken);
        else if (backend == ConfigurationBackend.DotLlm)
            await dotLlm.StopAsync(cancellationToken);
        else
            await llama.UnloadAsync(modelPath, cancellationToken);
    }

    public void Dispose()
    {
        llama.Dispose();
        openVino.Dispose();
        python.Dispose();
        dotLlm.Dispose();
    }

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
            [],
            null,
            status.LoadLog)];
    }
}