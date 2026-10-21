using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Esi.AI.Models;

namespace Esi.AI.Studio.Client.Services;

public sealed class Qwen38BenchmarkRunner(HttpClient httpClient)
{
    private const string PromptVersion = "ESI-QWEN38-AGENT-BENCH-V1";
    private const int MaxContextTokens = 131_072;
    private const double MinimumTokensPerSecond = 1;
    private const double RequestTimeoutBuffer = 1.25;
    private static readonly TimeSpan RequestTimeout =
        TimeSpan.FromSeconds(MaxContextTokens / MinimumTokensPerSecond * RequestTimeoutBuffer);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BenchmarkMeasurement> RunAsync(
        ModelConfiguration configuration,
        BenchmarkRunSettings settings,
        string runId,
        string sampleKind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sampleKind);
        if (settings.RepeatCount < 1)
            throw new ArgumentOutOfRangeException(nameof(settings), "Repeat count must be positive.");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(RequestTimeout);
        var token = timeoutSource.Token;
        var requestStart = Stopwatch.GetTimestamp();
        var request = new OpenAiChatRequest(
            configuration.Id.ToString("D"),
            [new OpenAiChatMessage("user", CreatePrompt(settings.RepeatCount, runId))],
            Stream: settings.Stream)
        {
            MaxTokens = settings.MaxTokens,
            Temperature = settings.Temperature,
            TopP = settings.TopP,
            TopK = settings.TopK,
            MinP = settings.MinP,
            RepetitionPenalty = settings.RepetitionPenalty,
            FrequencyPenalty = settings.FrequencyPenalty,
            PresencePenalty = settings.PresencePenalty,
            Seed = settings.Seed,
            ReasoningEffort = settings.ReasoningEffort,
            StreamOptions = new OpenAiStreamOptions(IncludeUsage: settings.IncludeUsage)
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        using var response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Benchmark request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {errorBody}",
                null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var state = new SseState();
        while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
            ConsumeSseLine(line, state);

        var completedAt = Stopwatch.GetTimestamp();
        if (!state.HasCompletionUsage)
            throw new InvalidDataException("The final SSE usage chunk is missing; stream_options.include_usage must be enabled.");

        double? decodeDurationMs = state.FirstContentAt is long firstContentAt && state.LastContentAt is long lastContentAt
            ? Math.Max(0, Stopwatch.GetElapsedTime(firstContentAt, lastContentAt).TotalMilliseconds)
            : null;
        var completionTokens = state.CompletionTokens!.Value;
        return new BenchmarkMeasurement(
            sampleKind,
            runId,
            (int)response.StatusCode,
            state.PromptTokens,
            completionTokens,
            state.FinishReason,
            state.FirstContentAt is long firstTokenAt
                ? Round(Stopwatch.GetElapsedTime(requestStart, firstTokenAt).TotalMilliseconds)
                : null,
            decodeDurationMs is > 0 && completionTokens > 1
                ? Round((completionTokens - 1) * 1000d / decodeDurationMs.Value)
                : null,
            decodeDurationMs is null ? null : Round(decodeDurationMs.Value),
            Round(Stopwatch.GetElapsedTime(requestStart, completedAt).TotalMilliseconds),
            state.ServerTokensPerSecond,
            completionTokens == settings.MaxTokens && string.Equals(state.FinishReason, "length", StringComparison.OrdinalIgnoreCase),
            state.PromptTokens is int promptTokens && promptTokens + completionTokens <= 131072);
    }

    public static BenchmarkRunSettings CreateSettings(string kind, int repeatCount, int warmupCount, int runCount) =>
        new(
            kind,
            repeatCount,
            warmupCount,
            runCount,
            MaxTokens: 128,
            Temperature: 0f,
            TopP: 1f,
            TopK: 1,
            MinP: 0f,
            RepetitionPenalty: 1f,
            FrequencyPenalty: 0f,
            PresencePenalty: 0f,
            Seed: 42,
            ReasoningEffort: "none",
            Stream: true,
            IncludeUsage: true,
            PromptVersion: PromptVersion);

    public static string CreatePrompt(int repeatCount, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        if (repeatCount < 1)
            throw new ArgumentOutOfRangeException(nameof(repeatCount), "Repeat count must be positive.");

        return string.Join("\n",
        [
            PromptVersion,
            "You are GitHub Copilot, an autonomous coding agent working in a VS Code repository.",
            $"Run: {runId}",
            "Repository context padding (synthetic token-fill; no source files or tools are supplied):",
            string.Concat(Enumerable.Repeat("hello ", repeatCount)),
            "Agent request:",
            "Investigate a streaming-cancellation bug in a C# service: cancelling a client request must stop generation, release backend resources, and preserve caller cancellation semantics. Trace the owning code path, propose the smallest root-cause fix, and include a focused regression test. Do not invent APIs or claim that tools were run. Continue with concrete patch and test details until the response token limit; do not end with an early summary."
        ]);
    }

    private static void ConsumeSseLine(string line, SseState state)
    {
        var normalized = line.TrimEnd('\r');
        if (!normalized.StartsWith("data:", StringComparison.Ordinal))
            return;

        var payload = normalized[5..].Trim();
        if (payload.Length == 0 || payload == "[DONE]")
            return;

        using var chunk = JsonDocument.Parse(payload);
        var root = chunk.RootElement;
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            state.PromptTokens = ReadInt(usage, "prompt_tokens") ?? state.PromptTokens;
            state.CompletionTokens = ReadInt(usage, "completion_tokens") ?? state.CompletionTokens;
            state.ServerTokensPerSecond = ReadDouble(usage, "tokens_per_second") ?? state.ServerTokensPerSecond;
            state.HasCompletionUsage = state.CompletionTokens is not null;
        }

        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
            return;

        var choice = choices[0];
        if (choice.TryGetProperty("finish_reason", out var finishReason) &&
            finishReason.ValueKind == JsonValueKind.String)
            state.FinishReason = finishReason.GetString() ?? state.FinishReason;

        if (!choice.TryGetProperty("delta", out var delta) ||
            !delta.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.String ||
            string.IsNullOrEmpty(content.GetString()))
            return;

        var now = Stopwatch.GetTimestamp();
        state.FirstContentAt ??= now;
        state.LastContentAt = now;
    }

    private static int? ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.TryGetInt32(out var value) ? value : null;

    private static double? ReadDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.TryGetDouble(out var value) ? value : null;

    private static double Round(double value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed class SseState
    {
        public int? PromptTokens { get; set; }
        public int? CompletionTokens { get; set; }
        public double? ServerTokensPerSecond { get; set; }
        public string? FinishReason { get; set; }
        public long? FirstContentAt { get; set; }
        public long? LastContentAt { get; set; }
        public bool HasCompletionUsage { get; set; }
    }
}

public sealed record Qwen38BenchmarkSummary(
    double? MedianClientDecodeTokensPerSecond,
    double? MedianServerTokensPerSecond,
    bool AllMeasuredRunsReachedTokenLimit);
