namespace Esi.AI.Llm;

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
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streaming Chat Completion. Gibt Token-Folgen zurück.
    /// </summary>
    /// <param name="request">Anfrage mit Model, Messages und Stream=true.</param>
    /// <param name="cancellationToken">Token zum Abbrechen.</param>
    /// <returns>Async-Enumerable von Chunks.</returns>
    IAsyncEnumerable<Chunk> CompleteStreamingAsync(
        ChatCompletionRequest request,
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
    public ProviderError? Error { get; init; }

    /// <summary>
    /// Fehlerinformationen.
    /// </summary>
    public sealed class ErrorInfo
    {
        /// <summary>Fehlermeldung.</summary>
        public string? Reason { get; set; }

        /// <summary>Fehlercode.</summary>
        public string? Code { get; set; }

        /// <summary>HTTP-Statuscode.</summary>
        public int? StatusCode { get; set; }

        /// <summary>Ist dieser Fehler vorübergehend (retry-fähig)?</summary>
        public bool IsRetryable { get; set; }

        /// <summary>
        /// Konvertiert ErrorInfo nach ProviderError.
        /// </summary>
        public static implicit operator ProviderError(ErrorInfo info)
        {
            return new ProviderError
            {
                Message = info.Reason,
                Code = info.Code,
                StatusCode = info.StatusCode ?? 500,
                IsRetryable = info.IsRetryable
            };
        }
    }

    /// <summary>
    /// Token-Usage-Informationen.
    /// </summary>
    public sealed class UsageInfo
    {
        /// <summary>Anzahl der Input-Token.</summary>
        public int InputTokens { get; set; }

        /// <summary>Anzahl der Output-Token.</summary>
        public int OutputTokens { get; set; }

        /// <summary>Gesamtt Tokens.</summary>
        public int TotalTokens { get; set; }
    }
}