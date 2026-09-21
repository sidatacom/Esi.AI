using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

public sealed class ModelSearchState
{
    public List<HuggingFaceModel> Results { get; } = [];
    public string Query { get; set; } = string.Empty;
    public string Library { get; set; } = "gguf";
    public string Task { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public string Hardware { get; set; } = string.Empty;
    public string Filters { get; set; } = string.Empty;
    public string InferenceProvider { get; set; } = string.Empty;
    public string ParameterRange { get; set; } = string.Empty;
    public string Sort { get; set; } = "trending";
    public HashSet<string> Apps { get; } = new(StringComparer.Ordinal);
    public int? VramBudgetGiB { get; set; }
    public int ContextLength { get; set; } = 131_072;
    public bool BaseOnly { get; set; }
    public bool InferenceAvailable { get; set; }
}