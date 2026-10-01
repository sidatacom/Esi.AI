using Esi.AI.Models;

namespace Esi.AI.Studio.Services;

/// <summary>Owns the current server-side Vulkan log collection.</summary>
public sealed class VulkanLogStore
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, VulkanLogStatus> items = [];

    /// <summary>Returns a stable snapshot of all current Vulkan logs.</summary>
    public IReadOnlyList<VulkanLogStatus> Read()
    {
        lock (sync)
            return items.Values.OrderByDescending(item => item.UpdatedAtUtc).ToArray();
    }

    /// <summary>Creates a pending Vulkan log entry.</summary>
    public VulkanLogStatus Create()
    {
        var now = DateTimeOffset.UtcNow;
        var status = new VulkanLogStatus(Guid.NewGuid(), string.Empty, true, now, now);
        lock (sync)
            items.Add(status.Id, status);
        return status;
    }

    /// <summary>Marks an existing Vulkan log entry as refreshing.</summary>
    public VulkanLogStatus BeginUpdate(Guid id)
    {
        lock (sync)
        {
            if (!items.TryGetValue(id, out var current))
                throw new KeyNotFoundException($"Vulkan log '{id}' was not found.");
            if (current.IsLoading)
                throw new InvalidOperationException("A Vulkan device discovery is already running.");

            var status = current with { IsLoading = true, UpdatedAtUtc = DateTimeOffset.UtcNow };
            items[id] = status;
            return status;
        }
    }

    /// <summary>Replaces an existing Vulkan log entry.</summary>
    public VulkanLogStatus Update(VulkanLogStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        lock (sync)
        {
            if (!items.ContainsKey(status.Id))
                throw new KeyNotFoundException($"Vulkan log '{status.Id}' was not found.");
            items[status.Id] = status;
        }

        return status;
    }

    /// <summary>Removes a Vulkan log entry, if it exists.</summary>
    public VulkanLogStatus? Delete(Guid id)
    {
        lock (sync)
            return items.Remove(id, out var status) ? status : null;
    }
}