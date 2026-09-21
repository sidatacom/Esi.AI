using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

/// <summary>Base state for the provider page.</summary>
public sealed class ProviderPageState
{
    public bool IsRefreshing { get; set; }
    public string? ErrorMessage { get; set; }
    public List<ProviderTraceEntry> TraceEntries { get; } = [];
}