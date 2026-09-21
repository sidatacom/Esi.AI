namespace Esi.AI.Studio.Client.State;

/// <summary>Base state for the settings page.</summary>
public sealed class SettingsPageState
{
    public bool IsSaving { get; set; }
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }
}