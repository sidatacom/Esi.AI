using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

/// <summary>SignalR state for backend runtime installations.</summary>
public sealed class BackendRuntimesState
{
    private readonly Dictionary<string, BackendRuntimeStatus> items = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, BackendRuntimeStatus> Items => items;
    internal void Read(IEnumerable<BackendRuntimeStatus> statuses) { items.Clear(); foreach (var status in statuses) items[status.PackageId] = status; }
    internal void Create(BackendRuntimeStatus status) => items[status.PackageId] = status;
    internal void Update(BackendRuntimeStatus status) => items[status.PackageId] = status;
    internal void Delete(BackendRuntimeStatus status) => items.Remove(status.PackageId);
}