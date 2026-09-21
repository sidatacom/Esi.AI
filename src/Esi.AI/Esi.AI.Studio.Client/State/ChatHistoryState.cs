using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

/// <summary>State for the chat archive and local model catalog.</summary>
public sealed class ChatHistoryState
{
    public List<ChatSummary> Items { get; } = [];
    public List<LocalModel> LocalModels { get; } = [];
}