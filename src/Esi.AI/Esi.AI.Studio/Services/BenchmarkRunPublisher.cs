using Esi.AI.Models;
using Esi.AI.Studio.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Esi.AI.Studio.Services;

/// <summary>Publishes persisted benchmark history changes over SignalR.</summary>
public interface IBenchmarkRunPublisher
{
    /// <summary>Publishes a newly persisted benchmark run.</summary>
    /// <param name="run">The created benchmark run.</param>
    /// <param name="cancellationToken">A token that cancels the publication.</param>
    Task PublishCreateAsync(BenchmarkRun run, CancellationToken cancellationToken = default);

    /// <summary>Publishes an updated benchmark run.</summary>
    /// <param name="run">The updated benchmark run.</param>
    /// <param name="cancellationToken">A token that cancels the publication.</param>
    Task PublishUpdateAsync(BenchmarkRun run, CancellationToken cancellationToken = default);

    /// <summary>Publishes a deleted benchmark run.</summary>
    /// <param name="run">The deleted benchmark run.</param>
    /// <param name="cancellationToken">A token that cancels the publication.</param>
    Task PublishDeleteAsync(BenchmarkRun run, CancellationToken cancellationToken = default);
}

internal sealed class SignalRBenchmarkRunPublisher(IHubContext<DataHub> hubContext) : IBenchmarkRunPublisher
{
    public Task PublishCreateAsync(BenchmarkRun run, CancellationToken cancellationToken = default) =>
        hubContext.Clients.All.SendAsync("BenchmarkRun_Create", run, cancellationToken);

    public Task PublishUpdateAsync(BenchmarkRun run, CancellationToken cancellationToken = default) =>
        hubContext.Clients.All.SendAsync("BenchmarkRun_Update", run, cancellationToken);

    public Task PublishDeleteAsync(BenchmarkRun run, CancellationToken cancellationToken = default) =>
        hubContext.Clients.All.SendAsync("BenchmarkRun_Delete", run, cancellationToken);
}
