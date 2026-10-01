using System.Text.Json.Serialization;
using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

/// <summary>Base state for the home page.</summary>
public sealed class HomePageState
{
    public ModelLoadStatus? LoadedModelStatus { get; set; }

    [JsonIgnore]
    public bool IsRefreshing { get; set; }

    [JsonIgnore]
    public string? ErrorMessage { get; set; }
}