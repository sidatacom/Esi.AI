using Esi.AI.Models;
using Esi.AI.Studio.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Esi.AI.Studio.Services;

/// <summary>Maintains a bounded, transient provider trace and broadcasts new entries to connected clients.</summary>
public sealed class ProviderTraceStore(IHubContext<DataHub> hubContext)
{
    private const int MaximumEntries = 200;
    private readonly object syncRoot = new();
    private readonly Queue<ProviderTraceEntry> entries = new();

    internal event Func<ProviderTraceEntry, Task>? Published;

    public IReadOnlyList<ProviderTraceEntry> Read()
    {
        lock (syncRoot)
            return entries.ToArray();
    }

    public async Task PublishAsync(ProviderTraceEntry entry, CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            entries.Enqueue(entry);
            while (entries.Count > MaximumEntries)
                entries.Dequeue();
        }

        await hubContext.Clients.All.SendAsync("ProviderTrace_Create", entry, cancellationToken).ConfigureAwait(false);
        var handler = Published;
        if (handler is not null)
            await handler(entry).ConfigureAwait(false);
    }
}
