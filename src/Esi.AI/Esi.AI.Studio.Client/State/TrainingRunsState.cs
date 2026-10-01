using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

/// <summary>SignalR collection state for local PyTorch training runs.</summary>
public sealed class TrainingRunsState
{
    private readonly Dictionary<Guid, TrainingRunStatus> items = [];

    public IReadOnlyDictionary<Guid, TrainingRunStatus> Items => items;

    internal void Read(IEnumerable<TrainingRunStatus> statuses)
    {
        items.Clear();
        foreach (var status in statuses)
            items[status.Id] = status;
    }

    internal void Create(TrainingRunStatus status) => items[status.Id] = status;

    internal void Update(TrainingRunStatus status) => items[status.Id] = status;

    internal void Delete(TrainingRunStatus status) => items.Remove(status.Id);
}