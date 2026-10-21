using Esi.AI.Models;

namespace Esi.AI.Studio.Data;

public sealed class InferenceLogEntity
{
    public Guid Id { get; set; }

    public string InferenceId { get; set; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; set; }

    public string Path { get; set; } = string.Empty;

    public string Backend { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string? BackendVariantId { get; set; }

    public int MessageCount { get; set; }

    public int ImageCount { get; set; }

    public int ToolCount { get; set; }

    public bool Streaming { get; set; }

    public InferenceLogStatus Status { get; set; }

    public int? EstimatedPrefillTokens { get; set; }

    public int? OutputTokenCount { get; set; }

    public string? FinishReason { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public long? DurationMilliseconds { get; set; }

    public string? ErrorType { get; set; }

    public string? ErrorMessage { get; set; }
}
