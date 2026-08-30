using LLama;
using LLama.Abstractions;
using LLama.Common;
using LLama.Sampling;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Esi.AI.Core.Chat;

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
        Session.HistoryTransform = new ChatMlHistoryTransform();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            Session.AddSystemMessage(systemPrompt);
    }

    public async Task<string> GenerateAsync(IReadOnlyList<LlamaChatMessage> messages, CancellationToken cancellationToken = default)
    {
        return (await GenerateWithStatsAsync(messages, cancellationToken).ConfigureAwait(false)).Text;
    }

    public Task<LlamaGenerationResult> GenerateWithStatsAsync(IReadOnlyList<LlamaChatMessage> messages, CancellationToken cancellationToken = default) =>
        GenerateWithStatsAsync(messages, null, cancellationToken);

    public async Task<LlamaGenerationResult> GenerateWithStatsAsync(
        IReadOnlyList<LlamaChatMessage> messages,
        Func<string, Task>? onToken,
        CancellationToken cancellationToken = default)
    {
        var result = string.Empty;
        var tokenCount = 0;
        var stopwatch = Stopwatch.StartNew();
        await foreach (var token in GenerateStreamingAsync(messages, cancellationToken).ConfigureAwait(false))
        {
            result += token;
            tokenCount++;
            if (onToken is not null)
                await onToken(token).ConfigureAwait(false);
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
                MaxTokens = 128,
                AntiPrompts = ["<|im_end|>", "\nUser:"],
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
        var endMarker = cleaned.IndexOf("<|im_end|>", StringComparison.OrdinalIgnoreCase);
        if (endMarker >= 0)
            cleaned = cleaned[..endMarker].TrimEnd();

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

    private sealed class ChatMlHistoryTransform : IHistoryTransform
    {
        public string HistoryToText(ChatHistory history)
        {
            var builder = new StringBuilder();
            foreach (var message in history.Messages)
            {
                builder.Append("<|im_start|>")
                    .Append(GetRoleName(message.AuthorRole))
                    .Append('\n')
                    .Append(message.Content)
                    .Append("<|im_end|>\n");
            }

            builder.Append("<|im_start|>assistant\n");
            return builder.ToString();
        }

        public ChatHistory TextToHistory(AuthorRole role, string text)
        {
            var history = new ChatHistory();
            history.AddMessage(role, text);
            return history;
        }

        public IHistoryTransform Clone() => new ChatMlHistoryTransform();

        private static string GetRoleName(AuthorRole role) => role switch
        {
            AuthorRole.System => "system",
            AuthorRole.User => "user",
            AuthorRole.Assistant => "assistant",
            _ => "user"
        };
    }
}

public sealed record LlamaChatMessage(string Role, string Content);

public sealed record LlamaGenerationResult(string Text, int TokenCount, TimeSpan Duration, double TokensPerSecond);