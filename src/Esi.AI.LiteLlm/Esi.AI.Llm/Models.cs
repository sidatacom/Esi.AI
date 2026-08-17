namespace Esi.AI.Llm;

/// <summary>
/// Ein einzelner Chat-Nachrichteneintrag.
/// </summary>
public sealed class ChatMessage
{
    /// <summary>Die Rolle der Nachricht (system, user, assistant, tool).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("role")]
    public string Role { get; set; } = default!;

    /// <summary>Der Inhalt der Nachricht.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("content")]
    public string Content { get; set; } = default!;
}

/// <summary>
/// Anfrage für eine Chat-Vervollständigung (OpenAI-kompatibles Format).
/// </summary>
public sealed class ChatCompletionRequest
{
    /// <summary>Modellname (z.B. "gpt-4o").</summary>
    [System.Text.Json.Serialization.JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>Maximale Antwort-Token.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }

    /// <summary>Streukoeffizient.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    /// <summary>Messagelist der Konversation.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = new();

    /// <summary>Streaming aktivieren.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("stream")]
    public bool? Stream { get; set; }
}

/// <summary>
/// Streaming-Chunk aus einem Provider-Streaming.
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

/// <summary>
/// Token-Usage-Informationen.
/// </summary>
public sealed class UsageInfo
{
    /// <summary>Anzahl der Input-Token.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("prompt_tokens")]
    public int InputTokens { get; init; }

    /// <summary>Anzahl der Output-Token.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("completion_tokens")]
    public int OutputTokens { get; init; }

    /// <summary>Gesamtt Tokens.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("total_tokens")]
    public int TotalTokens => InputTokens + OutputTokens;
}

/// <summary>
/// Fertige Antwort eines Chat-Completion-Aufrufs.
/// </summary>
public sealed class ChatCompletionResponse
{
    /// <summary>Eindeutige Id der Antwort.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; init; } = default!;

    /// <summary>Objekt-Klasse (fest: chat.completion).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("object")]
    public string Object { get; init; } = "chat.completion";

    /// <summary>Modellname.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>Auswahl-Optionen.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("choices")]
    public IReadOnlyList<Choice> Choices { get; init; } = default!;

    /// <summary>Token-Usage-Informationen (falls verfügbar).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("usage")]
    public UsageInfo? Usage { get; init; }

    /// <summary>Bequemlichkeit: Content vom ersten Choice.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Content => Choices?.FirstOrDefault()?.Message?.Content;

    /// <summary>Bequemlichkeit: Finish-Reason vom ersten Choice.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? FinishReason => Choices?.FirstOrDefault()?.FinishReason;

    /// <summary>Ausgewerteter Provider-Fehler (falls vorhanden).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public ProviderError? Error { get; set; }

    public sealed class Choice
    {
        /// <summary>Index der Wahl.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("index")]
        public int Index { get; set; }

        /// <summary>The message content.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public Message? Message { get; set; }

        /// <summary>Finish reason.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }

        /// <summary>A message in the response.</summary>
        public sealed class Message
        {
            /// <summary>The role (assistant, user, system, tool).</summary>
            [System.Text.Json.Serialization.JsonPropertyName("role")]
            public string Role { get; init; } = default!;

            /// <summary>The content of the message.</summary>
            [System.Text.Json.Serialization.JsonPropertyName("content")]
            public string? Content { get; init; }
        }
    }
}

/// <summary>
/// Provider-spezifischer Fehler.
/// </summary>
public sealed class ProviderError
{
    /// <summary>Fehlermeldung.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>Fehlercode.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>HTTP-Statuscode.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("status_code")]
    public int? StatusCode { get; set; }

    /// <summary>Ist dieser Fehler vorübergehend (retry-fähig)?</summary>
    [System.Text.Json.Serialization.JsonPropertyName("is_retryable")]
    public bool IsRetryable { get; set; }

    /// <summary>Grund/Details des Fehlers.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

/// <summary>
/// Konfigurationsinformationen für ein LLM-Modell.
/// </summary>
public sealed class ModelConfig
{
    /// <summary>Modellname (z.B. "gpt-4o", "claude-3-opus").</summary>
    public string Name { get; set; } = default!;

    /// <summary>Der Provider, der dieses Modell bereitstellt.</summary>
    public string Provider { get; set; } = default!;

    /// <summary>Der API-Endpoint.</summary>
    public string Endpoint { get; set; } = default!;

    /// <summary>Der API-Key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maximale Token-Limit.</summary>
    public int? MaxTokens { get; set; }

    /// <summary>Standard-Temperature.</summary>
    public double? Temperature { get; set; }

    /// <summary>Ob Streaming standardmäßig aktiviert ist.</summary>
    public bool DefaultStream { get; set; }
}

/// <summary>
/// Konfigurationsinformationen für ein Deployment.
/// </summary>
public sealed class DeploymentConfig
{
    /// <summary>Deployment-Name.</summary>
    public string Name { get; set; } = default!;

    /// <summary>Assoziiertes Modell.</summary>
    public string Model { get; set; } = default!;

    /// <summary>Der Provider, der dieses Deployment bereitstellt.</summary>
    public string Provider { get; set; } = default!;

    /// <summary>Der API-Endpoint.</summary>
    public string Endpoint { get; set; } = default!;

    /// <summary>Der API-Key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Priorität für das Routing.</summary>
    public int Priority { get; set; }

    /// <summary>Ob dieses Deployment aktiv ist.</summary>
    public bool IsActive { get; set; }

    /// <summary>Durchschnittliche Latenz in ms.</summary>
    public int AverageLatencyMs { get; set; }

    /// <summary>Aktuelle Anzahl aktiver Requests.</summary>
    public int ActiveRequests { get; set; }
}

/// <summary>
/// Routing-Strategie-Enumeration.
/// </summary>
public enum RoutingStrategy
{
    /// <summary>Round-Robin durch alle verfügbaren Deployments.</summary>
    RoundRobin,

    /// <summary>Das Deployment mit der geringsten Latenz wählen.</summary>
    LowestLatency,

    /// <summary>Das Deployment mit dem geringsten Kosten pro Token wählen.</summary>
    LowestCost,

    /// <summary>Das Deployment mit den geringsten aktiven Requests wählen.</summary>
    LeastBusy
}

/// <summary>
/// Budget-Konfiguration für Token-Tracking.
/// </summary>
public sealed class BudgetConfig
{
    /// <summary>Maximale Input-Token pro Zeitraum.</summary>
    public int MaxInputTokens { get; set; }

    /// <summary>Maximale Output-Token pro Zeitraum.</summary>
    public int MaxOutputTokens { get; set; }

    /// <summary>Zurücksetzungsintervall in Minuten.</summary>
    public int ResetIntervalMinutes { get; set; }

    /// <summary>Bereits verbrauchte Input-Token.</summary>
    public int ConsumedInputTokens { get; set; }

    /// <summary>Bereits verbrauchte Output-Token.</summary>
    public int ConsumedOutputTokens { get; set; }
}

/// <summary>
/// Informationen über einen registrierten Provider.
/// </summary>
public sealed class ProviderInfo
{
    /// <summary>Name des Providers (z.B. "openai", "anthropic", "gemini").</summary>
    public string Name { get; set; } = default!;

    /// <summary>Type des Providers.</summary>
    public string Type { get; set; } = default!;

    /// <summary>Durchschnittliche Latenz in ms.</summary>
    public int AverageLatencyMs { get; set; }

    /// <summary>Kosten pro Token.</summary>
    public decimal CostPerToken { get; set; }

    /// <summary>Aktuelle Anzahl aktiver Requests.</summary>
    public int ActiveRequests { get; set; }
}

/// <summary>
/// Rate-Limit-Konfiguration.
/// </summary>
public sealed class RateLimitConfig
{
    /// <summary>Maximale Requests pro Sekunde.</summary>
    public int MaxRequestsPerSecond { get; set; }

    /// <summary>Maximale Tokens pro Sekunde.</summary>
    public int MaxTokensPerSecond { get; set; }

    /// <summary>Zuletzt gesendete Request-Zeitstempel.</summary>
    public DateTime LastRequestTime { get; set; } = DateTime.MinValue;
}

/// <summary>
/// Metriken für ein Provider-Deployment.
/// </summary>
public sealed class ProviderMetrics
{
    /// <summary>Anzahl erfolgreicher Requests.</summary>
    public int SuccessfulRequests { get; set; }

    /// <summary>Anzahl fehlgeschlagener Requests.</summary>
    public int FailedRequests { get; set; }

    /// <summary>Gesamte Latenz in Millisekunden.</summary>
    public int TotalLatencyMs { get; set; }

    /// <summary>Durchschnittliche Latenz in Millisekunden.</summary>
    public double AverageLatencyMs => TotalLatencyMs > 0 ? (double)TotalLatencyMs / SuccessfulRequests : 0;

    /// <summary>Anzahl der aktiven/parallelen Requests.</summary>
    public int ActiveRequests { get; set; }
}