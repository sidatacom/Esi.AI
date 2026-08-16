using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Esi.AI.LiteLlm.Server;
using Esi.AI.LiteLlm.Contracts;

namespace Esi.AI.LiteLlm;

/// <summary>
/// Serverseitige Abstraktion der Chat-Completion-Engine.
/// 
/// Implementieren Sie diese Schnittstelle für LiteLLM-Integration (Provider), Ollama, native Calls etc.
/// Die Service-Schicht ruft den Provider auf und kapselt Fehler, Logik und Streaming.
/// </summary>
public interface IChatCompletionService
{
    /// <summary>
    /// Nicht-streaming Chat Completion über den zugewiesenen Provider.
    /// </summary>
    /// <param name="request">Anfrage mit Model, Messages und optionalen Parametern.</param>
    /// <param name="cancellationToken">Token zum Abbrechen.</param>
    /// <returns>Fertige Antwort (Content, Usage, Error).</returns>
    Task<Esi.AI.LiteLlm.Contracts.ChatCompletionResponse> CompleteAsync(
        Client.Contracts.ChatCompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streaming Chat Completion. Gibt Token-Folgen zurück.
    /// </summary>
    /// <param name="request">Anfrage mit Model, Messages und Stream=true.</param>
    /// <param name="onToken">Aufgerufen für jeden auszugebenden Token (mit optionaler Metadata).</param>
    /// <param name="cancellationToken">Token zum Abbrechen.</param>
    /// <returns>
    /// Abschlussinfo nach dem letzten Token (FinishReason + OutputTokens).
    /// Der Content wird nur im finalen Return geliefert, nicht durch onToken.
    /// </returns>
    Task<FinishingState> CompleteStreamingAsync(
        Client.Contracts.ChatCompletionRequest request,
        Action<string, ChatTokenMetadata> onToken,
        CancellationToken cancellationToken = default);

    /// <summary>Eindeutige Id des verwendeten Provider.</summary>
    string ProviderName { get; }
}

/// <summary>Abschlussinfo eines Streamings.</summary>
public readonly record struct FinishingState(FinishReason Reason, int OutputTokens);

/// <summary>Grund für den Finaleintritt eines Streams.</summary>
public enum FinishReason
{
    /// Die Antwort lief normal aus (Stopp-Zeichen).
    Stopped,
    /// MaxTokens wurde erreicht.
    LimitsExceeded,
    /// Provider hat die Antwort abgebrochen.
    Aborted,
    /// Ein Fehler trat auf.
    Failed,
}

/// <summary>Metadaten eines einzelnen Tokens (Streaming).</summary>
public readonly record struct ChatTokenMetadata(string? Model, DateTime SentAt);
