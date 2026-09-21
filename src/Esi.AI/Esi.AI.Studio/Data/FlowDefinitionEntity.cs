namespace Esi.AI.Studio.Data;

public sealed class FlowDefinitionEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Version { get; set; } = 1;

    public bool IsPublished { get; set; }

    public string DefinitionJson { get; set; } = "{}";

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
