using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

/// <summary>Identifies the selectable backend configuration tab.</summary>
public enum BackendTab
{
    /// <summary>LLama Vulkan backend.</summary>
    LlamaVulkan,
    /// <summary>LLama CUDA 12 backend.</summary>
    LlamaCuda12,
    /// <summary>LLama SYCL backend.</summary>
    LlamaSycl,
    /// <summary>OpenVINO backend.</summary>
    OpenVino,
    /// <summary>vLLM CUDA 12 backend.</summary>
    VllmCuda12,
    /// <summary>vLLM Intel XPU backend.</summary>
    VllmXpu
}

/// <summary>Single root state for the backends page.</summary>
public sealed class BackendPageState
{
    /// <summary>Resolves a backend family and persisted runtime variant ID to its selectable tab.</summary>
    public static BackendTab ResolveBackendTab(ConfigurationBackend backend, string? backendVariantId)
    {
        var variantId = backendVariantId?.Trim().ToLowerInvariant();
        return (backend, variantId) switch
        {
            (ConfigurationBackend.Llama, "cuda" or "cuda12" or "llama.cuda12") => BackendTab.LlamaCuda12,
            (ConfigurationBackend.Llama, "sycl" or "sycl16" or "xpu" or "llama.sycl") => BackendTab.LlamaSycl,
            (ConfigurationBackend.Llama, "vulkan" or "llama.vulkan") => BackendTab.LlamaVulkan,
            (ConfigurationBackend.OpenVino, "openvino") => BackendTab.OpenVino,
            (ConfigurationBackend.Vllm, "vllm.cuda12") => BackendTab.VllmCuda12,
            (ConfigurationBackend.Vllm, "vllm.xpu") => BackendTab.VllmXpu,
            _ => throw new ArgumentException(
                $"Backend variant '{backendVariantId}' is not supported for '{backend}'.", nameof(backendVariantId))
        };
    }

    /// <summary>Maps the LLama backend name stored in model settings to its tab.</summary>
    public static BackendTab ResolveLlamaBackendName(string? backend) => backend?.Trim().ToLowerInvariant() switch
    {
        "cuda" or "cuda12" or "llama.cuda12" => BackendTab.LlamaCuda12,
        "sycl" or "sycl16" or "xpu" or "llama.sycl" => BackendTab.LlamaSycl,
        "vulkan" or "llama.vulkan" => BackendTab.LlamaVulkan,
        _ => BackendTab.LlamaVulkan
    };

    public BackendTab ActiveBackend { get; set; } = BackendTab.LlamaVulkan;
    public BackendTabPreferencesSnapshot? TabPreferences { get; set; }
    public LlamaState Llama { get; set; } = new();
    public OpenVinoState OpenVino { get; set; } = new();
    public PythonState Python { get; set; } = new();
    public ProfileState Profiles { get; set; } = new();
    public LoadingState Loading { get; set; } = new();
    public RequirementsState Requirements { get; set; } = new();
}