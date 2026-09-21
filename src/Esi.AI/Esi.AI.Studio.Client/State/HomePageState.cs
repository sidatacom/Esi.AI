namespace Esi.AI.Studio.Client.State;

/// <summary>Base state for the home page.</summary>
public sealed class HomePageState
{
    public bool IsRefreshing { get; set; }
    public string? ErrorMessage { get; set; }
}