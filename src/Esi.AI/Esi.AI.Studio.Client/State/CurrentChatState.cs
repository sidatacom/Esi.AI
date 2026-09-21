using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

/// <summary>State for the selected chat, model and active exchange.</summary>
public sealed class CurrentChatState
{
    public PersistedChat? Value { get; set; }
    public string? SelectedModelPath { get; set; }
    public ConfigurationBackend? SelectedBackend { get; set; }
    public string? SelectedRuntime { get; set; }
    public string? SelectedModelKey { get; set; }
    public bool IsSending { get; set; }
    public string PendingAssistantContent { get; set; } = string.Empty;
    public string? StatusMessage { get; set; }
}