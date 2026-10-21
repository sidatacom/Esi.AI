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

/// <summary>Stores payload-free metadata for one inference request.</summary>
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
    string? ErrorMessage = null);
