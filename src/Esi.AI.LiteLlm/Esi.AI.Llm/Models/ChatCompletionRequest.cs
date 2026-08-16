namespace Esi.AI.Llm.Models;

/// <summary>
/// Represents a request for a chat completion.
/// </summary>
public sealed class ChatCompletionRequest
{
    /// <summary>The model ID to use (e.g., "gpt-4", "gemma-4").</summary>
    public required string Model { get; init; }

    /// <summary>The list of messages in the conversation.</summary>
    public required List<ChatMessage> Messages { get; init; }

    /// <summary>The maximum number of tokens to generate.</summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>The temperature of the model (0.0 to 2.0).</summary>
    public double Temperature { get; init; } = 0.7;

    /// <summary>Whether to stream the response.</summary>
    public bool Stream { get; init; } = false;

    /// <summary>Additional parameters for the provider.</summary>
    public Dictionary<string, string>? Parameters { get; init; }
}

/// <summary>
/// Represents a single message in a conversation.
/// </summary>
public sealed class ChatMessage
{
    /// <summary>The role of the message (e.g., "user", "assistant", "system").</summary>
    public required string Role { get; init; }

    /// <summary>The content of the message.</summary>
    public required string Content { get; init; }
}
