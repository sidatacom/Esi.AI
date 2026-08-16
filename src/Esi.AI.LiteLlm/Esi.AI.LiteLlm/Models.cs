namespace Esi.AI.LiteLlm.Models;

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

    /// <summary>Temperatur-Steuerungsfaktor.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    /// <summary>Messagelist der Konversation.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = new();

    /// <summary>Zusätzliche Parameter für Provider-spezifische Konfiguration.</summary>
    public Dictionary<string, object>? AdditionalParameters { get; set; }
}

/// <summary>
/// Einzelne Chat-Nachricht.
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
/// Streaming-Chunk für Token-Folgen.
/// </summary>
public sealed class ChatCompletionChunk
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

    /// <summary>Index des Chunks.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("index")]
    public int Index { get; set; }
}

/// <summary>
/// Vollständige Chat-Completion-Antwort (OpenAI-kompatibles Format).
/// </summary>
public sealed class ChatCompletionResponse
{
    /// <summary>Die eindeutige Id der Antwort.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; init; } = default!;

    /// <summary>Objekt-Klasse (fest: chat.completion).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("object")]
    public string Object { get; init; } = "chat.completion";

    /// <summary>Modellname.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>Auswahloptionen.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("choices")]
    public List<Choice> Choices { get; set; } = new();

    /// <summary>Token-Usage-Informationen.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("usage")]
    public UsageInfo? Usage { get; init; }

    /// <summary>Bequemer Zugriff auf den Content der ersten Wahl.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Content => Choices?.FirstOrDefault()?.Message?.Content;

    /// <summary>Bequemer Zugriff auf den Finish-Reason der ersten Wahl.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? FinishReason => Choices?.FirstOrDefault()?.FinishReason;

    /// <summary>Evtl. Fehler des Providers.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public ChatError? Error { get; set; }

    public sealed class Choice
    {
        /// <summary>Index der Wahl.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("index")]
        public int Index { get; set; }

        /// <summary>Die Chat-Nachricht.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }

        /// <summary>Finish-Reason.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    public sealed class ChatError
    {
        /// <summary>Mensale Beschreibung des Fehlers.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("error")]
        public string? Reason { get; init; }

        /// <summary>Fehlercode.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("error_code")]
        public string? Code { get; init; }

        /// <summary>HTTP-Statuscode.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("status_code")]
        public int? StatusCode { get; init; }
    }

    public sealed class UsageInfo
    {
        /// <summary>Anzahl der Input-Token.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("prompt_tokens")]
        public int InputTokens { get; init; }

        /// <summary>Anzahl der Output-Token.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("completion_tokens")]
        public int OutputTokens { get; init; }

        /// <summary>Gesamtzahl der Token.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("total_tokens")]
        public int TotalTokens => InputTokens + OutputTokens;
    }
}