using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Esi.AI.Studio.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Esi.AI.Studio.Services;

internal sealed class SignalRModelRuntimeStatusPublisher(IHubContext<DataHub> hubContext) : IModelRuntimeStatusPublisher
{
    public Task LoadedModel_CreateAsync(ModelLoadStatus status, CancellationToken cancellationToken = default) =>
        hubContext.Clients.All.SendAsync("LoadedModel_Create", status, cancellationToken);

    public Task LoadedModel_UpdateAsync(ModelLoadStatus status, CancellationToken cancellationToken = default) =>
        hubContext.Clients.All.SendAsync("LoadedModel_Update", status, cancellationToken);

    public Task LoadedModel_DeleteAsync(ModelLoadStatus status, CancellationToken cancellationToken = default) =>
        hubContext.Clients.All.SendAsync("LoadedModel_Delete", status, cancellationToken);
}