using Esi.AI.Models;

namespace Esi.AI.Backend.Abstractions;

/// <summary>Provides one backend variant's model lifecycle, capability, and generation operations.</summary>
public interface IBackendRuntime : IDisposable
{
    /// <summary>Gets the identity and routing metadata for this runtime.</summary>
    BackendVariantDescriptor Descriptor { get; }

    /// <summary>Gets the current backend-independent model status.</summary>
    ModelLoadStatus GetStatus();

    /// <summary>Gets whether the loaded model accepts image input.</summary>
    /// <param name="modelPath">The model path whose capability is queried.</param>
    bool SupportsImageInput(string? modelPath);

    /// <summary>Loads a model using the configuration owned by this backend variant.</summary>
    /// <param name="request">The normalized model path and opaque variant-specific configuration.</param>
    /// <param name="cancellationToken">A token that cancels the load operation.</param>
    Task LoadAsync(BackendLoadRequest request, CancellationToken cancellationToken = default);

    /// <summary>Unloads a model owned by this runtime.</summary>
    /// <param name="modelPath">The model path to unload.</param>
    /// <param name="cancellationToken">A token that cancels the unload operation.</param>
    Task UnloadAsync(string modelPath, CancellationToken cancellationToken = default);

    /// <summary>Stops the runtime and releases its active resources.</summary>
    /// <param name="cancellationToken">A token that cancels the stop operation.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Generates a normalized chat response for this runtime's loaded model.</summary>
    /// <param name="request">The normalized chat request.</param>
    /// <param name="onToken">An optional callback invoked for each generated token.</param>
    /// <param name="cancellationToken">A token that cancels generation.</param>
    Task<GenerationResult> GenerateAsync(
        OpenAiBackendChatRequest request,
        Func<string, Task>? onToken = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Contains a model path and backend-owned load settings.</summary>
/// <param name="ModelPath">The model path to load.</param>
/// <param name="Configuration">The serialized settings interpreted by the selected backend module.</param>
public sealed record BackendLoadRequest(string ModelPath, string VariantId, System.Text.Json.JsonElement Configuration);