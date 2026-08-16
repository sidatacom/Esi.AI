namespace Esi.AI.Llm.Models;

/// <summary>
/// Definitionen für Modell-Agenten mit festem Hardware-Slot.
/// Jedes Modell ist exklusiv genau einer GPU/Backend zugeordnet.
/// </summary>
public static class ModelAgents
{
    // ──────────────────────────────────────────────
    // NVIDIA-Slot (GPU 0)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Gemma-4 auf NVIDIA. Dediziert, darf nicht gestoppt oder ersetzt werden.
    /// </summary>
    public static readonly ModelAgent Gemma4 = new()
    {
        ModelId = "gemma-4",
        StrengthsDescription = "Stark in logischem Denken und komplexen Instruktionen. Dediziertes NVIDIA-Modell.",
        Hardware = "NVIDIA"
    };

    /// <summary>
    /// Qwen3.6-14b auf NVIDIA.
    /// </summary>
    public static readonly ModelAgent Qwen36_14b = new()
    {
        ModelId = "qwen3.6-14b",
        StrengthsDescription = "Schnelles NVIDIA-Modell fuer Standard-Coding und Routineaufgaben.",
        Hardware = "NVIDIA"
    };

    // ──────────────────────────────────────────────
    // Intel-Slot (GPU 1)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Nemotron auf Intel. Schnellstes Modell im System.
    /// </summary>
    public static readonly ModelAgent Nemotron = new()
    {
        ModelId = "nemotron",
        StrengthsDescription = "Schnellstes Modell. Erste Wahl fuer komplexes Coding und tiefgehende Analysen.",
        Hardware = "Intel"
    };

    /// <summary>
    /// Qwen3.6-27b auf Intel.
    /// </summary>
    public static readonly ModelAgent Qwen36_27b = new()
    {
        ModelId = "qwen3.6-27b",
        StrengthsDescription = "Stark in logischem Denken, mathematischen Aufgaben und komplexen Instruktionen.",
        Hardware = "Intel"
    };

    /// <summary>
    /// Qwen3.8-27b-q4 auf Intel. Quantisiertes Modell mit guter Performance.
    /// </summary>
    public static readonly ModelAgent Qwen38_27b_q4 = new()
    {
        ModelId = "qwen3.8-27b-q4",
        StrengthsDescription = "Quantisiertes Qwen-Modell. Fallback fuer komplexes Coding wenn Nemotron nicht ausreicht.",
        Hardware = "Intel"
    };

    /// <summary>
    /// GLM auf Intel.
    /// </summary>
    public static readonly ModelAgent Glm = new()
    {
        ModelId = "glm",
        StrengthsDescription = "Allround-Modell fuer allgemeine Aufgaben.",
        Hardware = "Intel"
    };

    /// <summary>
    /// Alle registrierten ModelAgents zurueckgeben.
    /// </summary>
    public static IEnumerable<ModelAgent> All => new[]
    {
        Gemma4,
        Qwen36_14b,
        Nemotron,
        Qwen36_27b,
        Qwen38_27b_q4,
        Glm
    };

    /// <summary>
    /// ModelAgents nach Hardware filtern.
    /// </summary>
    public static IEnumerable<ModelAgent> ByHardware(string hardware) =>
        All.Where(m => m.Hardware.Equals(hardware, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Bestimme das beste Modell fuer eine komplexe Coding-Aufgabe.
    /// Reihenfolge: Nemotron -> Qwen3.8-27b-q4 -> Qwen3.6-27b (alle Intel).
    /// </summary>
    public static ModelAgent BestForComplexCoding() => Nemotron;
}
