using System.Text.Json.Serialization;

namespace Esi.AI.Studio.Client.State;

/// <summary>Base state for the settings page.</summary>
public sealed class SettingsPageState
{
    [JsonIgnore]
    public bool IsSaving { get; set; }

    [JsonIgnore]
    public string? Message { get; set; }

    [JsonIgnore]
    public string? ErrorMessage { get; set; }
}