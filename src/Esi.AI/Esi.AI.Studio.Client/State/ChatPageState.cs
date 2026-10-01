using System.Text.Json.Serialization;
using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

/// <summary>Single root state for the chat page.</summary>
public sealed class ChatPageState
{
    public ModelLoadStatus? LoadedModelStatus { get; set; }
    public LocalModel[]? LocalModels { get; set; }

    [JsonIgnore]
    public ChatHistoryState History { get; } = new();

    [JsonIgnore]
    public ComposerState Composer { get; } = new();

    [JsonIgnore]
    public CurrentChatState CurrentChat { get; } = new();
}