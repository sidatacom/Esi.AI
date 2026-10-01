using System.Text.Json.Serialization;
using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

/// <summary>Base state for the provider page.</summary>
public sealed class ProviderPageState
{
    public ModelLoadStatus? LoadedModelStatus { get; set; }

    [JsonIgnore]
    public bool IsRefreshing { get; set; }

    [JsonIgnore]
    public string? ErrorMessage { get; set; }

    [JsonIgnore]
    public List<ProviderTraceEntry> TraceEntries { get; } = [];
}