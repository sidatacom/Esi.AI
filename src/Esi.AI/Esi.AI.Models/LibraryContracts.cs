namespace Esi.AI.Models;

public sealed record LocalModel(
    string Name,
    string Path,
    long SizeInBytes,
    DateTime LastWriteTimeUtc,
    ReferenceModelFormat Format = ReferenceModelFormat.Gguf,
    IReadOnlyList<ConfigurationBackend>? CompatibleBackends = null,
    string? HuggingFaceModelId = null,
    ModelCapabilities? Capabilities = null);

public sealed record HuggingFaceModel(
    string Id,
    string? Author,
    long Downloads,
    long Likes,
    DateTime? LastModified,
    string? LibraryName = null,
    string? PipelineTag = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<ConfigurationBackend>? CompatibleBackends = null);

public sealed record ModelCompatibilityUpdate(
    string ModelPath,
    IReadOnlyList<ConfigurationBackend>? CompatibleBackends = null,
    string? HuggingFaceModelId = null,
    ModelCapabilities? Capabilities = null);

public sealed record ModelDeletionRequest(string ModelPath, bool DeleteFiles);

/// <summary>Infers supported inference backends from model format and Hugging Face metadata.</summary>
public static class ModelBackendCompatibility
{
    /// <summary>Gets the default backends for a locally detected model format.</summary>
    public static IReadOnlyList<ConfigurationBackend> ForFormat(ReferenceModelFormat format) => format switch
    {
        ReferenceModelFormat.Gguf => [ConfigurationBackend.Llama, ConfigurationBackend.DotLlm],
        ReferenceModelFormat.OpenVinoIr => [ConfigurationBackend.OpenVino],
        ReferenceModelFormat.Transformers => [ConfigurationBackend.Vllm, ConfigurationBackend.Sglang],
        _ => []
    };

    /// <summary>Infers compatible backends from Hugging Face repository metadata.</summary>
    public static IReadOnlyList<ConfigurationBackend> FromHuggingFace(
        string? libraryName,
        IReadOnlyList<string>? tags)
    {
        var values = new HashSet<ConfigurationBackend>();
        var library = libraryName?.Trim().ToLowerInvariant() ?? string.Empty;
        var normalizedTags = tags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var isGguf = library.Contains("gguf", StringComparison.Ordinal) || library.Contains("llama.cpp", StringComparison.Ordinal) || normalizedTags.Contains("gguf");
        var isOpenVino = library.Contains("openvino", StringComparison.Ordinal) || normalizedTags.Contains("openvino");

        if (isGguf)
        {
            values.Add(ConfigurationBackend.Llama);
            values.Add(ConfigurationBackend.DotLlm);
        }

        if (isOpenVino)
            values.Add(ConfigurationBackend.OpenVino);

        if (!isGguf && !isOpenVino &&
            (library.Contains("transformers", StringComparison.Ordinal) || library.Contains("pytorch", StringComparison.Ordinal)))
        {
            values.Add(ConfigurationBackend.Vllm);
            values.Add(ConfigurationBackend.Sglang);
        }

        if (!isGguf && !isOpenVino && normalizedTags.Contains("vllm"))
            values.Add(ConfigurationBackend.Vllm);
        if (!isGguf && !isOpenVino && normalizedTags.Contains("sglang"))
            values.Add(ConfigurationBackend.Sglang);

        return Enum.GetValues<ConfigurationBackend>().Where(values.Contains).ToArray();
    }

    /// <summary>Infers provider capabilities from Hugging Face repository metadata.</summary>
    public static ModelCapabilities CapabilitiesFromHuggingFace(
        string? pipelineTag,
        IReadOnlyList<string>? tags)
    {
        var normalizedTags = tags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var normalizedPipeline = pipelineTag?.Trim().ToLowerInvariant() ?? string.Empty;
        var tooling = normalizedTags.Any(tag => tag is "tool-use" or "tool_use" or "tool-calling" or "tool_calling" or "function-calling" or "function_calling" or "function-calling-capable" or "agents");
        var vision = normalizedPipeline is "image-text-to-text" or "image-to-text" or "visual-question-answering" or "video-text-to-text"
            || normalizedTags.Any(tag => tag is "vision" or "multimodal" or "vision-language-model" or "image-text-to-text");
        var thinking = normalizedTags.Any(tag => tag is "thinking" or "reasoning" or "chain-of-thought" or "chain_of_thought" or "cot");

        return new ModelCapabilities(ToolCalling: tooling, ImageInput: vision, Thinking: thinking);
    }
}

public sealed record DownloadStatus(Guid Id, string ModelId, string FileName, string DestinationPath, long BytesDownloaded, long? TotalBytes, bool Completed, string? Error, bool Paused = false, bool Queued = false, IReadOnlyList<DownloadFileStatus>? Files = null)
{
    public double Percent => TotalBytes is > 0 ? BytesDownloaded * 100d / TotalBytes.Value : Completed ? 100 : 0;
}

public sealed record DownloadFileStatus(string FileName, long BytesDownloaded, long? TotalBytes, bool Completed)
{
    public double Percent => TotalBytes is > 0 ? BytesDownloaded * 100d / TotalBytes.Value : Completed ? 100 : 0;
}

public sealed record ModelDownloadUpdate(DownloadStatus Download, IReadOnlyList<LocalModel>? LocalModels = null, bool Cancelled = false);

public sealed record DownloadStarted(Guid Id);

public sealed record ModelStatus(string? ModelPath, string Backend, int GpuLayerCount, uint ContextSize, int FoundVulkanGpuCount, bool IsModelLoaded);
