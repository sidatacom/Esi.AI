namespace Esi.AI.Llm.Models;

/// <summary>
/// Represents a single chunk of a streaming chat completion.
/// </summary>
public sealed class Chunk
{
    /// <summary>Eindeutige Id des Chunks.</summary>
    public required string Id { get; init; }

    /// <summary>Der Inhalt des Chunks.</summary>
    public string? Content { get; init; }

    /// <summary>Warum die Antwort endet (z.B. stop, length).</summary>
    public string? FinishReason { get; init; }
}
