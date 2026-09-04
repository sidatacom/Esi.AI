using Esi.AI.Core.Chat;
using Esi.AI.Models;

namespace Esi.AI.Core.ModelLoading;

/// <summary>Common lifecycle and capability contract for one model runtime.</summary>
public interface IBackendRuntimeAdapter : IDisposable
{
    ConfigurationBackend Backend { get; }

    string RuntimeName { get; }

    ModelLoadStatus GetStatus();

    bool SupportsImageInput(string? modelPath);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task UnloadAsync(string modelPath, CancellationToken cancellationToken = default);
}

/// <summary>Typed loading contract for a backend runtime adapter.</summary>
public interface IBackendRuntimeAdapter<in TRequest> : IBackendRuntimeAdapter
{
    Task LoadAsync(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Adapts LLamaSharp loading and chat sessions to the runtime contract.</summary>
public sealed class LlamaRuntimeAdapter(LlamaModelLoader loader) : IBackendRuntimeAdapter<LoadModelRequest>
{
    public ConfigurationBackend Backend => ConfigurationBackend.Llama;

    public string RuntimeName => "LLamaSharp";

    public ModelLoadStatus GetStatus() => loader.GetStatus();

    public bool SupportsImageInput(string? modelPath) => loader.SupportsImageInput(modelPath);

    public Task LoadAsync(LoadModelRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var advanced = request.Advanced;
        return loader.LoadAsync(
            request.ModelPath,
            request.Backend,
            request.GpuLayerCount,
            request.ContextSize,
            request.VulkanDeviceWeights,
            new LlamaLoadOptions(advanced.MainGpu, advanced.SeqMax, advanced.RecurrentRollbackSnapshots, advanced.UseMemorymap,
                advanced.UseDirectIO, advanced.UseMemoryLock, advanced.Threads, advanced.BatchThreads, advanced.BatchSize,
                advanced.UBatchSize, advanced.Embeddings, advanced.NoKqvOffload, advanced.FlashAttention, advanced.VocabOnly,
                advanced.OpOffload, advanced.SwaFull, advanced.KVUnified, advanced.RopeFrequencyBase, advanced.RopeFrequencyScale,
                advanced.YarnExtrapolationFactor, advanced.YarnAttentionFactor, advanced.YarnBetaFast, advanced.YarnBetaSlow,
                advanced.YarnOriginalContext, request.MmprojPath), cancellationToken);
    }

    public LlamaChatSession CreateChatSession(string systemPrompt, string? modelPath = null) => loader.CreateChatSession(systemPrompt, modelPath);

    public Task StopAsync(CancellationToken cancellationToken = default) => loader.StopAsync(cancellationToken);

    public Task UnloadAsync(string modelPath, CancellationToken cancellationToken = default) => loader.UnloadAsync(modelPath, cancellationToken);

    public void Dispose() => loader.Dispose();
}

/// <summary>Adapts OpenVINO loading and image capability reporting.</summary>
public sealed class OpenVinoRuntimeAdapter(OpenVinoModelLoader loader) : IBackendRuntimeAdapter<OpenVinoLoadRequest>
{
    public ConfigurationBackend Backend => ConfigurationBackend.OpenVino;

    public string RuntimeName => "OpenVINO";

    public OpenVinoModelLoadStatus GetOpenVinoStatus() => loader.GetStatus();

    public ModelLoadStatus GetStatus()
    {
        var status = loader.GetStatus();
        var loadedModels = status.IsModelLoaded && status.ModelPath is not null
            ? new[] { new LoadedModelStatus(status.ModelPath, Backend, status.Device ?? RuntimeName, 0, 0, 0, [], null, status.LoadLog) }
            : Array.Empty<LoadedModelStatus>();
        return new ModelLoadStatus(status.ModelPath, status.Device ?? RuntimeName, 0, 0, 0, 0, [], null, status.LoadLog,
            new Dictionary<string, float>(), status.IsModelLoaded, loadedModels);
    }

    public bool SupportsImageInput(string? modelPath) => loader.SupportsImageInput;

    public Task LoadAsync(OpenVinoLoadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return loader.LoadAsync(
            request.ModelPath,
            request.Device,
            cancellationToken,
            new OpenVinoGenerationOptions(request.MaxNewTokens, request.Temperature, request.TopP, request.DoSample, request.TopK, request.RepetitionPenalty),
            request.CacheDirectory,
            new OpenVinoNpuOptions(
                request.Npu?.MaxPromptLength ?? 1024,
                request.Npu?.MinResponseLength ?? 128,
                request.Npu?.PrefillHint ?? "DYNAMIC",
                request.Npu?.GenerateHint ?? "FAST_COMPILE"));
    }

    public OpenVinoChatSession CreateChatSession() => loader.CreateChatSession();

    public Task StopAsync(CancellationToken cancellationToken = default) => loader.UnloadAsync(cancellationToken);

    public Task UnloadAsync(string modelPath, CancellationToken cancellationToken = default) => loader.UnloadAsync(cancellationToken);

    public void Dispose() => loader.Dispose();
}

/// <summary>Adapts the vLLM and SGLang Python bridge.</summary>
public sealed class PythonRuntimeAdapter(PythonInferenceServer server) : IBackendRuntimeAdapter<PythonInferenceLoadRequest>
{
    public ConfigurationBackend Backend => ConfigurationBackend.Vllm;

    public string RuntimeName => "Python inference bridge";

    public ModelLoadStatus GetStatus() => server.GetStatus();

    public bool SupportsImageInput(string? modelPath) => false;

    public Task LoadAsync(PythonInferenceLoadRequest request, CancellationToken cancellationToken = default) => server.LoadAsync(request, cancellationToken);

    public PythonInferenceChatSession CreateChatSession() => server.CreateChatSession();

    public Task StopAsync(CancellationToken cancellationToken = default) => server.StopAsync(cancellationToken);

    public Task UnloadAsync(string modelPath, CancellationToken cancellationToken = default) => server.StopAsync(cancellationToken);

    public void Dispose() => server.Dispose();
}

/// <summary>Adapts the in-process dotLLM runtime.</summary>
public sealed class DotLlmRuntimeAdapter(DotLlmInProcessRuntime runtime) : IBackendRuntimeAdapter<DotLlmLoadRequest>
{
    public ConfigurationBackend Backend => ConfigurationBackend.DotLlm;

    public string RuntimeName => "dotLLM / In-Process";

    public ModelLoadStatus GetStatus() => runtime.GetStatus();

    public bool SupportsImageInput(string? modelPath) => false;

    public Task LoadAsync(DotLlmLoadRequest request, CancellationToken cancellationToken = default) => runtime.LoadAsync(request, cancellationToken);

    public DotLlmInProcessChatSession CreateChatSession() => runtime.CreateChatSession();

    public Task StopAsync(CancellationToken cancellationToken = default) => runtime.StopAsync(cancellationToken);

    public Task UnloadAsync(string modelPath, CancellationToken cancellationToken = default) => runtime.StopAsync(cancellationToken);

    public void Dispose() => runtime.Dispose();
}

/// <summary>Resolves normalized backend aliases to runtime adapters.</summary>
public sealed class BackendRuntimeRegistry : IDisposable
{
    private readonly IReadOnlyDictionary<ConfigurationBackend, IBackendRuntimeAdapter> adapters;

    public BackendRuntimeRegistry(IEnumerable<IBackendRuntimeAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        this.adapters = adapters.ToDictionary(adapter => adapter.Backend);
    }

    public IBackendRuntimeAdapter Resolve(ConfigurationBackend backend) =>
        adapters.TryGetValue(backend, out var adapter)
            ? adapter
            : backend == ConfigurationBackend.Sglang && adapters.TryGetValue(ConfigurationBackend.Vllm, out var pythonAdapter)
                ? pythonAdapter
                : throw new ArgumentException($"No runtime adapter is registered for '{backend}'.", nameof(backend));

    public IBackendRuntimeAdapter Resolve(string backend) => Resolve(Normalize(backend));

    public static ConfigurationBackend Normalize(string backend) => backend.Trim().ToUpperInvariant() switch
    {
        "OPENVINO" => ConfigurationBackend.OpenVino,
        "VLLM" => ConfigurationBackend.Vllm,
        "SGLANG" => ConfigurationBackend.Sglang,
        "DOTLLM" => ConfigurationBackend.DotLlm,
        "VULKAN" or "CUDA" or "SYCL" or "CPU" => ConfigurationBackend.Llama,
        _ => throw new ArgumentException($"Unsupported backend '{backend}'.", nameof(backend))
    };

    public void Dispose()
    {
        foreach (var adapter in adapters.Values.Distinct())
            adapter.Dispose();
    }
}
