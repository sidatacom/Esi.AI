namespace Esi.AI.Models;

/// <summary>Describes the lifecycle state of a persisted inference request.</summary>
public enum InferenceLogStatus
{
    /// <summary>The inference request has started and has not reached a terminal state.</summary>
    Running,

    /// <summary>The inference request completed successfully.</summary>
    Succeeded,

    /// <summary>The inference request failed.</summary>
    Failed,

    /// <summary>The caller cancelled the inference request.</summary>
    Canceled,

    /// <summary>The inference request exceeded its calculated deadline.</summary>
    TimedOut,

    /// <summary>The Studio process stopped before the request reached a terminal state.</summary>
    Interrupted
}

/// <summary>Stores diagnostic metadata and readable payloads for one inference request.</summary>
/// <param name="Id">The persistent entry identifier.</param>
/// <param name="InferenceId">The identifier used to correlate the request across server logs.</param>
/// <param name="StartedAtUtc">The time the request entered inference processing.</param>
/// <param name="Path">The request path, such as <c>OpenAI</c> or <c>SignalR</c>.</param>
/// <param name="Backend">The selected inference backend.</param>
/// <param name="Model">The selected model identifier.</param>
/// <param name="BackendVariantId">The selected backend variant, if any.</param>
/// <param name="MessageCount">The number of messages in the request.</param>
/// <param name="ImageCount">The number of image inputs.</param>
/// <param name="ToolCount">The number of tools supplied to the request.</param>
/// <param name="Streaming">Whether the request streamed its response.</param>
/// <param name="Status">The current lifecycle state.</param>
/// <param name="EstimatedPrefillTokens">The approximate input-token count, when available.</param>
/// <param name="OutputTokenCount">The generated-token count, when available.</param>
/// <param name="FinishReason">The backend finish reason, when available.</param>
/// <param name="CompletedAtUtc">The time the request reached a terminal state.</param>
/// <param name="DurationMilliseconds">The total request duration in milliseconds.</param>
/// <param name="ErrorType">The exception type for a failed request, when available.</param>
/// <param name="ErrorMessage">A bounded exception message for a failed request, when available.</param>
/// <param name="RequestPayload">The readable request payload, when available.</param>
/// <param name="ResponsePayload">The readable response payload, when available.</param>
/// <param name="PromptTokenCount">The actual prompt-token count reported by the backend, when available.</param>
/// <param name="TimeToFirstTokenMs">The time to the first generated token in milliseconds, when available.</param>
/// <param name="PrefillDurationMs">The prompt-prefill duration in milliseconds, when available.</param>
/// <param name="DecodeDurationMs">The generated-token decode duration in milliseconds, when available.</param>
public sealed record InferenceLog(
    Guid Id,
    string InferenceId,
    DateTimeOffset StartedAtUtc,
    string Path,
    string Backend,
    string Model,
    string? BackendVariantId,
    int MessageCount,
    int ImageCount,
    int ToolCount,
    bool Streaming,
    InferenceLogStatus Status,
    int? EstimatedPrefillTokens = null,
    int? OutputTokenCount = null,
    string? FinishReason = null,
    DateTimeOffset? CompletedAtUtc = null,
    long? DurationMilliseconds = null,
    string? ErrorType = null,
    string? ErrorMessage = null,
    string? RequestPayload = null,
    string? ResponsePayload = null,
    int? PromptTokenCount = null,
    double? TimeToFirstTokenMs = null,
    double? PrefillDurationMs = null,
    double? DecodeDurationMs = null)
{
    /// <summary>Gets a value that indicates whether the prompt-token count is estimated.</summary>
    /// <value><see langword="true" /> if the actual prompt-token count is unavailable and an estimate exists; otherwise, <see langword="false" />.</value>
    public bool PrefillTokenCountIsEstimated =>
        PromptTokenCount is null && EstimatedPrefillTokens is not null;

    /// <summary>Gets the prompt-prefill throughput in tokens per second.</summary>
    /// <value>A measured or estimated prompt-prefill rate, or <see langword="null" /> when the token count or duration is unavailable.</value>
    public double? PrefillTokensPerSecond =>
        CalculateTokensPerSecond(PromptTokenCount ?? EstimatedPrefillTokens, PrefillDurationMs);

    /// <summary>Gets the generated-token decode throughput in tokens per second.</summary>
    /// <value>A decode rate, or <see langword="null" /> when the output-token count or duration is unavailable.</value>
    public double? DecodeTokensPerSecond =>
        CalculateTokensPerSecond(OutputTokenCount, DecodeDurationMs);

    /// <summary>Gets the total prompt and generated-token throughput across the request.</summary>
    /// <value>An end-to-end rate, or <see langword="null" /> when token counts or total duration are unavailable.</value>
    public double? EndToEndTokensPerSecond
    {
        get
        {
            var promptTokenCount = PromptTokenCount ?? EstimatedPrefillTokens;
            if (promptTokenCount is not int promptTokens || OutputTokenCount is not int outputTokens)
                return null;

            return CalculateTokensPerSecond(
                (long)promptTokens + outputTokens,
                DurationMilliseconds);
        }
    }

    private static double? CalculateTokensPerSecond(long? tokenCount, double? durationMilliseconds)
    {
        if (tokenCount is not long tokens || tokens < 0 ||
            durationMilliseconds is not double duration || duration <= 0 ||
            !double.IsFinite(duration))
            return null;

        var tokensPerSecond = tokens * 1_000d / duration;
        return double.IsFinite(tokensPerSecond) ? tokensPerSecond : null;
    }
}
