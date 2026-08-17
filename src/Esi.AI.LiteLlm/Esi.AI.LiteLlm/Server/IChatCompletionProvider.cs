using System.Runtime.CompilerServices;

namespace Esi.AI.LiteLlm.Server;

/// <summary>
/// Abstrahiert einen einzelnen Chat-Completion-Provider (LiteLLM, OpenAI, Ollama).
/// </summary>
public interface IChatCompletionProvider
{
    /// <summary>Eindeutiger Name des Providers.</summary>
    string Name { get; }

    Task<ChatCompletionResult> CompleteAsync(
        Client.Contracts.ChatCompletionRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ChatCompletionChunk> CompleteStreamingAsync(
        Client.Contracts.ChatCompletionRequest request,
        CancellationToken cancellationToken = default);
}
