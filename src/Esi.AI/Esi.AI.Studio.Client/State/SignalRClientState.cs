namespace Esi.AI.Studio.Client.State;

/// <summary>Base state populated by SignalR CRUD events and shared by page states.</summary>
public sealed class SignalRClientState
{
    public ActiveModelsSignalRState ActiveModels { get; } = new();
    public DownloadsState Downloads { get; } = new();
    public BackendRequirementsState BackendRequirements { get; } = new();
    public BackendRuntimesState BackendRuntimes { get; } = new();
}
