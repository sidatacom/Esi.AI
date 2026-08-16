namespace Esi.AI.LiteLlm.Contracts;

/// <summary>OpenAI-kompatible Antwort.</summary>
public sealed class ChatCompletionResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; init; } = default!;

    [System.Text.Json.Serialization.JsonPropertyName("object")]
    public string Object { get; init; } = "chat.completion";

    [System.Text.Json.Serialization.JsonPropertyName("model")]
    public string? Model { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("choices")]
    public System.Collections.Generic.IReadOnlyList<Choice> Choices { get; init; } = default!;

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
    public ChatError? Error { get; set; }

    public sealed class Choice
    {
        [System.Text.Json.Serialization.JsonPropertyName("index")]
        public int Index { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public Esi.AI.LiteLlm.Client.Contracts.ChatMessage? Message { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    public sealed class ChatError
    {
        [System.Text.Json.Serialization.JsonPropertyName("error")]
        public string? Reason { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("error_code")]
        public string? Code { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("status_code")]
        public int? StatusCode { get; init; }
    }

    public sealed class UsageInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("prompt_tokens")]
        public int InputTokens { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("completion_tokens")]
        public int OutputTokens { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("total_tokens")]
        public int TotalTokens => InputTokens + OutputTokens;
    }
}
