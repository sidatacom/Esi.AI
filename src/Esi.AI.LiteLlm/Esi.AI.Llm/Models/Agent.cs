namespace Esi.AI.Llm.Models;

/// <summary>
/// Repräsentiert einen Agenten mit spezifischen Stärken und einer zugeordneten Modell-ID.
/// </summary>
public sealed class Agent
{
    /// <summary>Eindeutige ID des Agenten.</summary>
    public string Id { get; init; } = default!;

    /// <summary>Name des Agenten.</summary>
    public string Name { get; init; } = default!;

    /// <summary>Beschreibung der Aufgaben, für die dieser Agent spezialisiert ist.</summary>
    public string Description { get; init; } = default!;

    /// <summary>Die Modell-ID, die dieser Agent primär verwendet.</summary>
    public string PrimaryModelId { get; init; } = default!;

    /// <summary>Liste der Stärken des Agenten.</summary>
    public List<string> Strengths { get; init; } = new();
}

/// <summary>
/// Repräsentiert ein Modell mit Informationen zu seinen Stärken und seiner Hardware-Zuweisung.
/// </summary>
public sealed class ModelAgent
{
    /// <summary>Modell-ID (z.B. "gemma-4", "nemotron").</summary>
    public string ModelId { get; init; } = default!;

    /// <summary>Beschreibung der Stärken des Modells.</summary>
    public string StrengthsDescription { get; init; } = default!;

    /// <summary>Hardware-Zuweisung (z.B. "Nvidia", "Intel").</summary>
    public string Hardware { get; init; } = default!;
}
