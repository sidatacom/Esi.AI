using Esi.AI.Models;
using Esi.AI.Studio.Contracts;
using Esi.AI.Studio.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Esi.AI.Studio.Services;

internal sealed class SignalRVulkanLogStatusPublisher(IHubContext<DataHub> hubContext) : IVulkanLogStatusPublisher
{
    public Task PublishCreateAsync(VulkanLogStatus status, CancellationToken cancellationToken = default) =>
        hubContext.Clients.All.SendAsync("VulkanLog_Create", status, cancellationToken);

    public Task PublishUpdateAsync(VulkanLogStatus status, CancellationToken cancellationToken = default) =>
        hubContext.Clients.All.SendAsync("VulkanLog_Update", status, cancellationToken);

    public Task PublishDeleteAsync(VulkanLogStatus status, CancellationToken cancellationToken = default) =>
        hubContext.Clients.All.SendAsync("VulkanLog_Delete", status, cancellationToken);
}