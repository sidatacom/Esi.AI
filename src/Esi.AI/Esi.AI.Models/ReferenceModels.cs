namespace Esi.AI.Models;

/// <summary>
/// Describes the on-disk or remote format expected by a backend reference model.
/// </summary>
public enum ReferenceModelFormat
{
    Gguf,
    OpenVinoIr,
    Transformers
}

/// <summary>
/// Identifies a small, reproducible model used for backend smoke testing.
/// </summary>
public sealed record BackendReferenceModel(
    ConfigurationBackend Backend,
    string Name,
    string ModelId,
    ReferenceModelFormat Format,
    string? FileName,
    string EnvironmentVariable,
    string DocumentationUrl);

/// <summary>
/// Provides the canonical reference model for each supported backend.
/// </summary>
public static class BackendReferenceModels
{
    /// <summary>
    /// Gets the supported backend reference entries.
    /// </summary>
    public static IReadOnlyList<BackendReferenceModel> All { get; } =
    [
        new(ConfigurationBackend.Llama, "Qwen2.5 0.5B Instruct", "Qwen/Qwen2.5-0.5B-Instruct", ReferenceModelFormat.Gguf,
            "Qwen2.5-0.5B-Instruct-Q4_K_M.gguf", "ESI_LLAMA_MODEL_PATH", "https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct"),
        new(ConfigurationBackend.OpenVino, "Qwen2.5 1.5B Instruct INT4", "OpenVINO/Qwen2.5-1.5B-Instruct-int4-ov", ReferenceModelFormat.OpenVinoIr,
            null, "ESI_OPENVINO_MODEL_PATH", "https://huggingface.co/OpenVINO/Qwen2.5-1.5B-Instruct-int4-ov"),
        new(ConfigurationBackend.Vllm, "Qwen2.5 0.5B Instruct", "Qwen/Qwen2.5-0.5B-Instruct", ReferenceModelFormat.Transformers,
            null, "ESI_VLLM_REFERENCE_MODEL", "https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct")
    ];
}