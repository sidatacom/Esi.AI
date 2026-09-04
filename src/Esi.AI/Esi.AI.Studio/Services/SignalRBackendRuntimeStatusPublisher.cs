using Esi.AI.Models;
using Esi.AI.Studio.Hubs;
using Esi.AI.Studio.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace Esi.AI.Studio.Services;

/// <summary>Publishes backend runtime status changes over the central data hub.</summary>
public sealed class SignalRBackendRuntimeStatusPublisher(IHubContext<DataHub> hubContext) : IBackendRuntimeStatusPublisher
{
    /// <summary>Publishes a newly started runtime installation.</summary>
    public Task PublishCreateAsync(BackendRuntimeStatus status, CancellationToken cancellationToken = default) =>
        hubContext.Clients.All.SendAsync("BackendRuntime_Create", status, cancellationToken);

    /// <summary>Publishes an updated runtime installation.</summary>
    public Task PublishUpdateAsync(BackendRuntimeStatus status, CancellationToken cancellationToken = default) =>
        hubContext.Clients.All.SendAsync("BackendRuntime_Update", status, cancellationToken);

    /// <summary>Publishes removal of a failed runtime installation operation.</summary>
    public Task PublishDeleteAsync(BackendRuntimeStatus status, CancellationToken cancellationToken = default) =>
        hubContext.Clients.All.SendAsync("BackendRuntime_Delete", status, cancellationToken);
}