namespace Esi.AI.Models;

/// <summary>Describes the lifecycle state of a persisted benchmark run.</summary>
public enum BenchmarkRunState
{
    /// <summary>Indicates that the benchmark series is still collecting samples.</summary>
    Running,

    /// <summary>Indicates that all configured samples were collected.</summary>
    Completed,

    /// <summary>Indicates that a request or persistence operation failed.</summary>
    Failed,

    /// <summary>Indicates that the user cancelled the benchmark series.</summary>
    Cancelled
}

/// <summary>Captures the complete profile-independent request configuration for a benchmark series.</summary>
/// <param name="Kind">The benchmark mode, such as <c>short</c> or <c>long</c>.</param>
/// <param name="RepeatCount">The number of synthetic context repetitions.</param>
/// <param name="WarmupCount">The number of warm-up requests.</param>
/// <param name="MeasuredRunCount">The number of measured requests.</param>
/// <param name="MaxTokens">The maximum generated token count.</param>
/// <param name="Temperature">The request temperature.</param>
/// <param name="TopP">The nucleus sampling value.</param>
/// <param name="TopK">The top-k sampling value.</param>
/// <param name="MinP">The minimum probability sampling value.</param>
/// <param name="RepetitionPenalty">The repetition penalty.</param>
/// <param name="FrequencyPenalty">The frequency penalty.</param>
/// <param name="PresencePenalty">The presence penalty.</param>
/// <param name="Seed">The deterministic request seed.</param>
/// <param name="ReasoningEffort">The requested reasoning effort.</param>
/// <param name="Stream">Whether the request uses streaming.</param>
/// <param name="IncludeUsage">Whether the stream includes token usage.</param>
/// <param name="PromptVersion">The identifier of the benchmark prompt template.</param>
public sealed record BenchmarkRunSettings(
    string Kind,
    int RepeatCount,
    int WarmupCount,
    int MeasuredRunCount,
    int MaxTokens,
    float Temperature,
    float TopP,
    int TopK,
    float MinP,
    float RepetitionPenalty,
    float FrequencyPenalty,
    float PresencePenalty,
    int Seed,
    string ReasoningEffort,
    bool Stream,
    bool IncludeUsage,
    string PromptVersion);

/// <summary>Contains the measured request and streaming metrics for one benchmark sample.</summary>
/// <param name="Kind">The sample kind, such as warm-up, measured, or long-context.</param>
/// <param name="RunId">The unique identifier sent with the benchmark prompt.</param>
/// <param name="HttpStatus">The response HTTP status code.</param>
/// <param name="PromptTokens">The prompt token count reported by the backend.</param>
/// <param name="CompletionTokens">The completion token count reported by the backend.</param>
/// <param name="FinishReason">The backend completion reason.</param>
/// <param name="ClientTimeToFirstTokenMs">The client-observed time to first token in milliseconds.</param>
/// <param name="ClientDecodeTokensPerSecond">The client-calculated decode rate.</param>
/// <param name="ClientDecodeDurationMs">The client-observed decode duration in milliseconds.</param>
/// <param name="ClientWallTimeMs">The total client-observed request duration in milliseconds.</param>
/// <param name="ServerTokensPerSecond">The backend-reported generation rate.</param>
/// <param name="CompletionLengthMatchesLimit">Whether the completion reached the configured token limit.</param>
/// <param name="Fits131072Context">Whether the reported prompt and completion fit within 131072 tokens.</param>
public sealed record BenchmarkMeasurement(
    string Kind,
    string RunId,
    int HttpStatus,
    int? PromptTokens,
    int CompletionTokens,
    string? FinishReason,
    double? ClientTimeToFirstTokenMs,
    double? ClientDecodeTokensPerSecond,
    double? ClientDecodeDurationMs,
    double ClientWallTimeMs,
    double? ServerTokensPerSecond,
    bool CompletionLengthMatchesLimit,
    bool Fits131072Context);

/// <summary>Stores a benchmark series and an immutable snapshot of its selected profile.</summary>
/// <param name="Id">The unique benchmark history identifier.</param>
/// <param name="ProfileId">The source profile identifier captured for filtering, without a profile relationship.</param>
/// <param name="ProfileSnapshot">The complete profile configuration as it existed when the series started.</param>
/// <param name="Settings">The benchmark request settings used for the series.</param>
/// <param name="Measurements">The samples recorded so far.</param>
/// <param name="State">The current lifecycle state.</param>
/// <param name="CreatedAtUtc">The time the history entry was created.</param>
/// <param name="CompletedAtUtc">The time the benchmark series reached a terminal state, if applicable.</param>
/// <param name="ErrorMessage">The failure detail, when the series failed.</param>
public sealed record BenchmarkRun(
    Guid Id,
    Guid ProfileId,
    ModelConfiguration ProfileSnapshot,
    BenchmarkRunSettings Settings,
    IReadOnlyList<BenchmarkMeasurement> Measurements,
    BenchmarkRunState State,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    string? ErrorMessage);
