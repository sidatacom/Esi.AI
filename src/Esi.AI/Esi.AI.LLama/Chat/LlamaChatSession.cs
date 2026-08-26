using LLama;
using LLama.Common;
using LLama.Sampling;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Esi.AI.Llm.Chat;

public sealed class LlamaChatSession : IDisposable
{
    private readonly LLamaContext context;
    private readonly Action onDispose;
    private int disposed;

    public ChatSession Session { get; }

    public LlamaChatSession(LLamaContext context, string systemPrompt, Action onDispose)
    {
        this.context = context;
        this.onDispose = onDispose;
        Session = new ChatSession(new InteractiveExecutor(context));
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            Session.AddSystemMessage(systemPrompt);
    }

    public async Task<string> GenerateAsync(IReadOnlyList<LlamaChatMessage> messages, CancellationToken cancellationToken = default)
    {
        return (await GenerateWithStatsAsync(messages, cancellationToken).ConfigureAwait(false)).Text;
    }

    public async Task<LlamaGenerationResult> GenerateWithStatsAsync(IReadOnlyList<LlamaChatMessage> messages, CancellationToken cancellationToken = default)
    {
        var result = string.Empty;
        var tokenCount = 0;
        var stopwatch = Stopwatch.StartNew();
        await foreach (var token in GenerateStreamingAsync(messages, cancellationToken).ConfigureAwait(false))
        {
            result += token;
            tokenCount++;
        }
        stopwatch.Stop();
        var tokensPerSecond = stopwatch.Elapsed.TotalSeconds > 0 ? tokenCount / stopwatch.Elapsed.TotalSeconds : 0;
        var cleanedResult = CleanGeneratedText(result);
        if (string.IsNullOrWhiteSpace(cleanedResult))
            throw new InvalidOperationException("The model returned an empty answer.");

        return new LlamaGenerationResult(cleanedResult, tokenCount, stopwatch.Elapsed, tokensPerSecond);
    }

    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        IReadOnlyList<LlamaChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
            throw new ArgumentException("At least one chat message is required.", nameof(messages));

        for (var index = 0; index < messages.Count - 1; index++)
        {
            var message = messages[index];
            Session.AddMessage(new ChatHistory.Message(ParseRole(message.Role), message.Content));
        }

        var lastMessage = messages[^1];
        await foreach (var token in Session.ChatAsync(
            new ChatHistory.Message(ParseRole(lastMessage.Role), lastMessage.Content),
            new InferenceParams
            {
                AntiPrompts = ["\nUser:", "User:"],
                SamplingPipeline = new DefaultSamplingPipeline { RepeatPenalty = 1.0f }
            },
            cancellationToken).ConfigureAwait(false))
        {
            yield return token;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        context.Dispose();
        onDispose();
    }

    private static AuthorRole ParseRole(string role) => role.ToLowerInvariant() switch
    {
        "system" => AuthorRole.System,
        "assistant" => AuthorRole.Assistant,
        "user" => AuthorRole.User,
        _ => throw new ArgumentException($"Unsupported chat role '{role}'.", nameof(role))
    };

    private static string CleanGeneratedText(string text)
    {
        var cleaned = text.Trim();
        if (cleaned.StartsWith("Assistant:", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned["Assistant:".Length..].TrimStart();

        var userMarker = cleaned.IndexOf("\nUser:", StringComparison.OrdinalIgnoreCase);
        if (userMarker >= 0)
            cleaned = cleaned[..userMarker].TrimEnd();
        else if (cleaned.StartsWith("User:", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned["User:".Length..].TrimStart();
        if (cleaned.Equals("Assistant:", StringComparison.OrdinalIgnoreCase))
            cleaned = string.Empty;

        return cleaned;
    }
}

public sealed record LlamaChatMessage(string Role, string Content);

public sealed record LlamaGenerationResult(string Text, int TokenCount, TimeSpan Duration, double TokensPerSecond);