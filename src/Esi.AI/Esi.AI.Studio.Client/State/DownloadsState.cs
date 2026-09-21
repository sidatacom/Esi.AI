using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

/// <summary>SignalR state for model downloads.</summary>
public sealed class DownloadsState
{
    private readonly Dictionary<Guid, ModelDownloadUpdate> items = [];
    public IReadOnlyDictionary<Guid, ModelDownloadUpdate> Items => items;
    internal void Read(IEnumerable<ModelDownloadUpdate> updates) { items.Clear(); foreach (var update in updates) items[update.Download.Id] = update; }
    internal void Create(ModelDownloadUpdate update) => items[update.Download.Id] = update;
    internal void Update(ModelDownloadUpdate update) => items[update.Download.Id] = update;
    internal void Delete(ModelDownloadUpdate update) => items.Remove(update.Download.Id);
}