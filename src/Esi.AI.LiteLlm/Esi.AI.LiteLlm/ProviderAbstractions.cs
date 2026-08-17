namespace Esi.AI.LiteLlm;

/// <summary>
/// Abstrahiert einen einzelnen Chat-Completion-Provider (OpenAI, Anthropic, Google, Ollama, etc.).
/// Alle Provider-Implementierungen sollen dieses Interface erfüllen.
/// </summary>
public interface IChatCompletionProvider
{
    /// <summary>
    /// Eindeutiger Name des Providers (z.B. "openai", "anthropic", "gemini").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Nicht-streaming Chat Completion über den zugewiesenen Provider.
    /// </summary>
    /// <param name="request">Anfrage mit Model, Messages und optionalen Parametern.</param>
    /// <param name="cancellationToken">Token zum Abbrechen.</param>
    /// <returns>Fertige Antwort (Content, Usage, Error, FinishReason).</returns>
    Task<ProviderResult> CompleteAsync(
        Esi.AI.LiteLlm.Client.Contracts.ChatCompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streaming Chat Completion. Gibt Token-Folgen zurück.
    /// </summary>
    /// <param name="request">Anfrage mit Model, Messages und Stream=true.</param>
    /// <param name="cancellationToken">Token zum Abbrechen.</param>
    /// <returns>Async-Enumerable von Chunks.</returns>
    IAsyncEnumerable<Chunk> CompleteStreamingAsync(
        Esi.AI.LiteLlm.Client.Contracts.ChatCompletionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Ergebnis eines Provider-Aufrufs (nicht-streaming).
/// </summary>
public sealed class ProviderResult
{
    /// <summary>Eindeutige Id des Aufrufs.</summary>
    public required string Id { get; init; }

    /// <summary>Fertiger Antworttext.</summary>
    public required string Content { get; init; }

    /// <summary>Warum die Antwort endet (z.B. stop, length).</summary>
    public string? FinishReason { get; init; }

    /// <summary>Token-Usage-Informationen.</summary>
    public UsageInfo? Usage { get; init; }

    /// <summary>Evtl. Provider-spezifischer Fehler.</summary>
    public ErrorInfo? Error { get; init; }

    /// <summary>
    /// Token-Usage-Informationen.
    /// </summary>
    public sealed class UsageInfo
    {
        public int InputTokens { get; init; }
        public int OutputTokens { get; init; }
        public int TotalTokens => InputTokens + OutputTokens;
    }

    /// <summary>
    /// Provider-spezifischer Fehler.
    /// </summary>
    public sealed class ErrorInfo
    {
        public string? Reason { get; init; }
        public string? Code { get; init; }
        public int? StatusCode { get; init; }
    }
}

/// <summary>
/// Ein einzelner Chunk aus einem Provider-Streaming.
/// </summary>
public sealed class Chunk
{
    /// <summary>Eindeutige Chunk-Id.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; init; } = default!;

    /// <summary>Objekt-Klasse (fest: chat.completion.chunk).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("object")]
    public string Object { get; init; } = "chat.completion.chunk";

    /// <summary>Modellname.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("model")]
    public string Model { get; init; } = default!;

    /// <summary>Dieser Chunks Content-Delta.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>Finish-Reason (kann erst im finalen Chunk gesetzt sein).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}