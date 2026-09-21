namespace Esi.AI.Studio.Client.State;

/// <summary>Single root state for the chat page.</summary>
public sealed class ChatPageState
{
    public ChatHistoryState History { get; } = new();
    public ActiveModelsState Models { get; }
    public ComposerState Composer { get; } = new();
    public CurrentChatState CurrentChat { get; } = new();

    public ChatPageState(Services.IClientStateStore clientState)
    {
        ArgumentNullException.ThrowIfNull(clientState);
        Models = new ActiveModelsState(clientState.ActiveModels);
    }
}