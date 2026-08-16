using System.Runtime.CompilerServices;

namespace Esi.AI.LiteLlm.Server;

/// <summary>Robuste Antwort aus einem Provider-Aufruf.</summary>
public sealed class ChatCompletionResult
{
    /// <summary>Id des Aufrufes.</summary>
    public required string Id { get; init; }

    /// <summary>Fertiger Antworttext.</summary>
    public required string Content { get; init; }

    /// <summary>Warum die Antwort endet (z.B. stop, length).</summary>
    public string? FinishReason { get; init; }

    public UsageInfo? Usage { get; init; }

    public sealed class UsageInfo
    {
        public int InputTokens { get; init; }
        public int OutputTokens { get; init; }
        public int TotalTokens => InputTokens + OutputTokens;
    }
}

/// <summary>Ein einzelner Chunk aus einem Provider-Streaming.</summary>
public sealed class ChatCompletionChunk
{
    public required string Id { get; init; }
    public string? Content { get; init; }
    public string? FinishReason { get; init; }
}
