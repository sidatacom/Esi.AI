namespace Esi.AI.Studio.Data;

public sealed class ChatConversationEntity
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "Neuer Chat";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<ChatMessageEntity> Messages { get; set; } = [];
}

public sealed class ChatMessageEntity
{
    public long Id { get; set; }
    public Guid ConversationId { get; set; }
    public ChatConversationEntity Conversation { get; set; } = null!;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ModelPath { get; set; }
    public int? TokenCount { get; set; }
    public double? TokensPerSecond { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}