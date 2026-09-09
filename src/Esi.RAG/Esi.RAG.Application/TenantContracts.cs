using Esi.RAG.Domain;

namespace Esi.RAG.Application;

public interface ITenantContextAccessor
{
    TenantContext GetRequiredContext();
}

public interface ITenantConfigurationResolver
{
    Task<TenantConfiguration> ResolveAsync(TenantContext context, CancellationToken cancellationToken);
}

public interface ITenantSqlSource
{
    Task<TenantSourceBatch> ReadAsync(
        TenantConfiguration configuration,
        TenantSourceConfiguration source,
        TenantSyncState? state,
        CancellationToken cancellationToken);
}

public interface ITenantGraphSource
{
    Task<TenantSourceBatch> ReadAsync(
        TenantConfiguration configuration,
        TenantSourceConfiguration source,
        TenantSyncState? state,
        CancellationToken cancellationToken);
}

public interface ITenantSyncStateStore
{
    Task<TenantSyncState?> GetAsync(string tenantId, TenantSourceType sourceType, string sourceId, CancellationToken cancellationToken);

    Task SaveAsync(TenantSyncState state, CancellationToken cancellationToken);
}

public interface ITenantVectorStore
{
    string CollectionName { get; }

    Task UpsertAsync(IReadOnlyList<TenantDataDocument> documents, CancellationToken cancellationToken);

    Task<int> DeleteAsync(string tenantId, TenantSourceType sourceType, string sourceId, IReadOnlyCollection<string> sourcePaths, CancellationToken cancellationToken);

    Task<IReadOnlyList<(TenantDataDocument Document, float Score)>> SearchAsync(
        IReadOnlyList<float> queryEmbedding,
        int limit,
        TenantContext context,
        CancellationToken cancellationToken);
}

public interface ITenantVectorStoreFactory
{
    ITenantVectorStore Get(TenantContext context);
}

public interface ITenantIngestionService
{
    Task<TenantSyncReport> SynchronizeAsync(IProgress<TenantSyncProgress>? progress, CancellationToken cancellationToken);
}

public interface ITenantSearchService
{
    Task<IReadOnlyList<TenantDataDocument>> SearchAsync(string query, int limit, CancellationToken cancellationToken);
}
