namespace Esi.AI.LiteLlm.Client.Contracts;

/// <summary>Clientseitige Chat-Completion-Anfrage (OpenAI-kompatibel).</summary>
public sealed class ChatCompletionRequest
{
    /// <summary>Modellname (z.B. "gpt-4o").</summary>
    [System.Text.Json.Serialization.JsonPropertyName("model")]
    public string Model { get; set; } = default!;

    /// <summary>Messagelist der Konversation.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("messages")]
    public IReadOnlyList<ChatMessage> Messages { get; set; } = default!;

    /// <summary>Maximale Antwort-Token.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }

    /// <summary>Streukoeffizient.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("temperature")]
    public double? Temperature { get; set; }
}