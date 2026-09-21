namespace Esi.AI.Models;

/// <summary>Describes one persisted visual workflow definition.</summary>
public sealed record FlowDefinition(
    Guid Id,
    string Name,
    int Version,
    bool IsPublished,
    string DefinitionJson,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
