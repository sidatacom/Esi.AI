namespace Esi.AI.LiteLlm.Client.Contracts;

/// <summary>Eine vollständige Chat-Completion-Antwort.</summary>
public sealed class ChatCompletionResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [System.Text.Json.Serialization.JsonPropertyName("object")]
    public string Object { get; set; } = default!;

    [System.Text.Json.Serialization.JsonPropertyName("choices")]
    public IReadOnlyList<Choice> Choices { get; set; } = default!;

    [System.Text.Json.Serialization.JsonPropertyName("usage")]
    public UsageInfo? Usage { get; set; }

    public sealed class Choice
    {
        [System.Text.Json.Serialization.JsonPropertyName("index")]
        public int Index { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    /// <summary>Nutzungsinformationen einer Chat-Antwort.</summary>
    public sealed class UsageInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
}
