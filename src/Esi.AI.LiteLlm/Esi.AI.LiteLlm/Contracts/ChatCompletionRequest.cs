namespace Esi.AI.LiteLlm.Contracts;

/// <summary>
/// Anfrage für eine Chat-Vervollständigung (serverseitig, verweist auf Client-Typen).
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
    public System.Collections.Generic.List<Esi.AI.LiteLlm.Client.Contracts.ChatMessage> Messages { get; set; } = new();
}
