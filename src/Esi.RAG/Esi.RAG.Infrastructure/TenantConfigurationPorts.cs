using Esi.RAG.Application;
using Esi.RAG.Domain;

namespace Esi.RAG.Infrastructure;

public sealed class UnconfiguredTenantConfigurationResolver : ITenantConfigurationResolver
{
    public Task<TenantConfiguration> ResolveAsync(TenantContext context, CancellationToken cancellationToken)
        => throw new InvalidOperationException("MultiTenant EsiDbContext configuration has not been registered.");
}

public sealed class UnconfiguredTenantSqlSource : ITenantSqlSource
{
    public Task<TenantSourceBatch> ReadAsync(TenantConfiguration configuration, TenantSourceConfiguration source, TenantSyncState? state, CancellationToken cancellationToken)
        => throw new InvalidOperationException("The tenant SQL connector has not been registered.");
}

public sealed class UnconfiguredTenantGraphSource : ITenantGraphSource
{
    public Task<TenantSourceBatch> ReadAsync(TenantConfiguration configuration, TenantSourceConfiguration source, TenantSyncState? state, CancellationToken cancellationToken)
        => throw new InvalidOperationException("The Microsoft Graph connector has not been registered.");
}
