using Esi.AI.Models;
using Esi.AI.PyTorch;
using Esi.AI.Studio.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Esi.AI.Studio.Services;

internal sealed class SignalRTrainingRunStatusPublisher(IHubContext<DataHub> hubContext) : ITrainingRunStatusPublisher
{
    public Task PublishCreateAsync(TrainingRunStatus status, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(DataHub.LocalTrainingClientsGroup).SendAsync("TrainingRun_Create", status, cancellationToken);

    public Task PublishUpdateAsync(TrainingRunStatus status, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(DataHub.LocalTrainingClientsGroup).SendAsync("TrainingRun_Update", status, cancellationToken);

    public Task PublishDeleteAsync(TrainingRunStatus status, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(DataHub.LocalTrainingClientsGroup).SendAsync("TrainingRun_Delete", status, cancellationToken);
}