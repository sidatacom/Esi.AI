using System.Text.Json.Serialization;
using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

/// <summary>Base state for the model library page.</summary>
public sealed class ModelsPageState
{
    public ModelLoadStatus? LoadedModelStatus { get; set; }
    public LocalModel[]? LocalModels { get; set; }
    public string[]? ModelDirectories { get; set; }

    [JsonIgnore]
    public ModelLibraryState Library { get; } = new();

    [JsonIgnore]
    public ModelSearchState Search { get; } = new();

    [JsonIgnore]
    public ModelDownloadState Downloads { get; } = new();

    [JsonIgnore]
    public ModelsUiState Ui { get; } = new();
}