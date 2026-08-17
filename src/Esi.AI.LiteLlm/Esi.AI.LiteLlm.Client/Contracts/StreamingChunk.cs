namespace Esi.AI.LiteLlm.Client.Contracts;

/// <summary>Ein einzelner Chunk aus einem streaming-Antwortstrom.</summary>
public sealed class StreamingChunk
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [System.Text.Json.Serialization.JsonPropertyName("delta")]
    public DeltaInfo? Delta { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }

    /// <summary>Wrapper für den delta-Teil einer Stream-Chunk-Nachricht.</summary>
    public sealed class DeltaInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("role")]
        public string? Role { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
