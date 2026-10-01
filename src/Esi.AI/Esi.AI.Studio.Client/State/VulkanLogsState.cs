using Esi.AI.Models;

namespace Esi.AI.Studio.Client.State;

/// <summary>SignalR collection state for Vulkan runtime logs.</summary>
public sealed class VulkanLogsState
{
    private readonly Dictionary<Guid, VulkanLogStatus> items = [];

    public IReadOnlyDictionary<Guid, VulkanLogStatus> Items => items;

    internal void Read(IEnumerable<VulkanLogStatus> statuses)
    {
        items.Clear();
        foreach (var status in statuses)
            items[status.Id] = status;
    }

    internal void Create(VulkanLogStatus status) => items[status.Id] = status;

    internal void Update(VulkanLogStatus status) => items[status.Id] = status;

    internal void Delete(VulkanLogStatus status) => items.Remove(status.Id);
}