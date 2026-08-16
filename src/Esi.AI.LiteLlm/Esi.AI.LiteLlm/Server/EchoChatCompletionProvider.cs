using System.Runtime.CompilerServices;
using System.Text;

namespace Esi.AI.LiteLlm.Server;

/// <summary>
/// Development-only, deterministic echo provider.
/// Echoes the last user message back as the completion content.
/// No external API calls, no secrets, no side effects.
/// Intended as a DI fallback for testing the service layer and POST /api/chat without real model access.
/// </summary>
public sealed class EchoChatCompletionProvider : IChatCompletionProvider
{
    public string Name => "echo-dev";

    public Task<ChatCompletionResult> CompleteAsync(
        Client.Contracts.ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var lastUserMessage = request.Messages
            .Where(m => m.Role?.Equals("user", StringComparison.OrdinalIgnoreCase) == true)
            .Select(m => m.Content ?? string.Empty)
            .LastOrDefault() ?? string.Empty;

        return Task.FromResult(new ChatCompletionResult
        {
            Id = Guid.NewGuid().ToString("N"),
            Content = lastUserMessage,
            FinishReason = "stop",
            Usage = new ChatCompletionResult.UsageInfo
            {
                InputTokens = CountTokens(lastUserMessage),
                OutputTokens = CountTokens(lastUserMessage),
            },
        });
    }

    public async IAsyncEnumerable<ChatCompletionChunk> CompleteStreamingAsync(
        Client.Contracts.ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var lastUserMessage = request.Messages
            .Where(m => m.Role?.Equals("user", StringComparison.OrdinalIgnoreCase) == true)
            .Select(m => m.Content ?? string.Empty)
            .LastOrDefault() ?? string.Empty;

        var id = Guid.NewGuid().ToString("N");

        if (!string.IsNullOrEmpty(lastUserMessage))
        {
            var words = lastUserMessage.Split(' ', (char)0x0A, StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                cancellationToken.ThrowIfCancellationRequested();

                yield return new ChatCompletionChunk
                {
                    Id = id,
                    Content = word + " ",
                };

                // Small fixed delay so clients can consume the stream incrementally.
                await Task.Delay(15, cancellationToken).ConfigureAwait(false);
            }
        }

        yield return new ChatCompletionChunk
        {
            Id = id,
            Content = string.Empty,
            FinishReason = "stop",
        };
    }

    /// <summary>
    /// Very rough, language-agnostic token approximation for echo-only use.
    /// Not suitable for any provider token budgeting.
    /// </summary>
    private static int CountTokens(string text) => text.Length > 0 ? text.Split(' ').Length : 0;
}
