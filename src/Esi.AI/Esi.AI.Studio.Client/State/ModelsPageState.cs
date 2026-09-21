namespace Esi.AI.Studio.Client.State;

/// <summary>Base state for the model library page.</summary>
public sealed class ModelsPageState
{
    public ModelLibraryState Library { get; } = new();
    public ModelSearchState Search { get; } = new();
    public ModelDownloadState Downloads { get; } = new();
    public ModelsUiState Ui { get; } = new();

}