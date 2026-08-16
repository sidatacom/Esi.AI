namespace Esi.AI.LiteLlm.Contracts;

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