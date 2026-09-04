using LLama;
using LLama.Abstractions;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Esi.AI.Models;

namespace Esi.AI.Core.Chat;

public sealed class LlamaChatSession : IDisposable
{
    private readonly LLamaContext context;
    private readonly Action onDispose;
    private readonly string? systemPrompt;
    private readonly MtmdWeights? mtmdWeights;
    private int disposed;

    public ChatSession Session { get; }

    public LlamaChatSession(LLamaContext context, string systemPrompt, MtmdWeights? mtmdWeights, Action onDispose)
    {
        this.context = context;
        this.onDispose = onDispose;
        this.systemPrompt = systemPrompt;
        this.mtmdWeights = mtmdWeights;
        Session = new ChatSession(mtmdWeights is null
            ? new InteractiveExecutor(context)
            : new InteractiveExecutor(context, mtmdWeights));
        Session.HistoryTransform = new ChatMlHistoryTransform();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            Session.AddSystemMessage(systemPrompt);
    }

    public async Task<string> GenerateAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        return (await GenerateWithStatsAsync(messages, cancellationToken).ConfigureAwait(false)).Text;
    }

    public Task<GenerationResult> GenerateWithStatsAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default) =>
        GenerateWithStatsAsync(messages, null, new ChatGenerationOptions(), cancellationToken);

    public Task<GenerationResult> GenerateWithStatsAsync(
        IReadOnlyList<ChatMessage> messages,
        Func<string, Task>? onToken,
        CancellationToken cancellationToken = default) =>
        GenerateWithStatsAsync(messages, onToken, new ChatGenerationOptions(), cancellationToken);

    public async Task<GenerationResult> GenerateWithStatsAsync(
        IReadOnlyList<ChatMessage> messages,
        Func<string, Task>? onToken,
        ChatGenerationOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = string.Empty;
        var stopwatch = Stopwatch.StartNew();
        await foreach (var token in GenerateStreamingAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            result += token;
            if (onToken is not null)
                await onToken(token).ConfigureAwait(false);
        }
        stopwatch.Stop();
        var tokenCount = context.Tokenize(result, addBos: false, special: true).Length;
        var tokensPerSecond = stopwatch.Elapsed.TotalSeconds > 0 ? tokenCount / stopwatch.Elapsed.TotalSeconds : 0;
        var cleanedResult = CleanGeneratedText(result);
        if (string.IsNullOrWhiteSpace(cleanedResult))
            throw new InvalidOperationException("The model returned an empty answer.");

        var promptTokenCount = context.Tokenize(CreatePrompt(messages), addBos: true, special: true).Length;
        return new GenerationResult(cleanedResult, tokenCount, stopwatch.Elapsed, tokensPerSecond, promptTokenCount);
    }

    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatGenerationOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
            throw new ArgumentException("At least one chat message is required.", nameof(messages));

        var hasMedia = messages.Any(message => message.Images is { Count: > 0 });
        try
        {
            var preparedMessages = PrepareMessages(messages);
            for (var index = 0; index < preparedMessages.Count - 1; index++)
            {
                var message = preparedMessages[index];
                Session.AddMessage(new ChatHistory.Message(ParseRole(message.Role), message.Content));
            }

            var lastMessage = preparedMessages[^1];
            await foreach (var token in Session.ChatAsync(
                new ChatHistory.Message(ParseRole(lastMessage.Role), lastMessage.Content),
                new InferenceParams
                {
                    MaxTokens = options.MaxTokens,
                    AntiPrompts = ["<|im_end|>", "\nUser:", .. options.StopSequences ?? []],
                    SamplingPipeline = new DefaultSamplingPipeline
                    {
                        Temperature = options.Temperature,
                        TopP = options.TopP,
                        TopK = options.TopK,
                        MinP = options.MinP,
                        RepeatPenalty = options.RepetitionPenalty,
                        FrequencyPenalty = options.FrequencyPenalty,
                        PresencePenalty = options.PresencePenalty,
                        PenaltyCount = options.PenaltyCount,
                        Seed = (uint)(options.Seed ?? Random.Shared.Next())
                    }
                },
                cancellationToken).ConfigureAwait(false))
            {
                yield return token;
            }
        }
        finally
        {
            if (hasMedia)
                mtmdWeights?.ClearMedia();
        }
    }

    private IReadOnlyList<ChatMessage> PrepareMessages(IReadOnlyList<ChatMessage> messages)
    {
        if (!messages.Any(message => message.Images is { Count: > 0 }))
            return messages;
        if (mtmdWeights is null || !mtmdWeights.SupportsVision)
            throw new InvalidOperationException("The loaded Llama model does not support image input.");

        var marker = MtmdContextParams.Default().MediaMarker ?? NativeApi.MtmdDefaultMarker() ?? "<media>";
        var prepared = new List<ChatMessage>(messages.Count);
        try
        {
            foreach (var message in messages)
            {
                var images = message.Images;
                if (images is null or { Count: 0 })
                {
                    prepared.Add(message);
                    continue;
                }

                foreach (var image in images)
                    mtmdWeights.LoadMedia(image.Data);
                var content = BuildMultimodalContent(message, marker);
                prepared.Add(message with { Content = content, Images = null, ContentParts = null });
            }
        }
        catch
        {
            mtmdWeights.ClearMedia();
            throw;
        }

        return prepared;
    }

    internal static string BuildMultimodalContent(ChatMessage message, string marker)
    {
        if (message.ContentParts is not { Count: > 0 } contentParts ||
            !contentParts.Any(part => part.ImageIndex is not null))
            return message.Content + string.Concat(Enumerable.Repeat(marker, message.Images?.Count ?? 0));

        var builder = new StringBuilder(message.Content.Length + marker.Length * (message.Images?.Count ?? 0));
        foreach (var part in contentParts)
        {
            if (part.ImageIndex is not null)
                builder.Append(marker);
            else
                builder.Append(part.Text);
        }

        return builder.ToString();
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

    private string CreatePrompt(IReadOnlyList<ChatMessage> messages)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            builder.Append("<|im_start|>system\n").Append(systemPrompt).Append("<|im_end|>\n");
        foreach (var message in messages)
            builder.Append("<|im_start|>").Append(message.Role).Append('\n').Append(message.Content).Append("<|im_end|>\n");
        builder.Append("<|im_start|>assistant\n");
        return builder.ToString();
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

public sealed record GenerationResult(
    string Text,
    int TokenCount,
    TimeSpan Duration,
    double TokensPerSecond,
    int? PromptTokenCount = null,
    string FinishReason = "stop",
    IReadOnlyList<OpenAiToolCall>? ToolCalls = null);