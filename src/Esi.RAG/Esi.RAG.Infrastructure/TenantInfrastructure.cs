using System.Collections.Concurrent;
using Esi.RAG.Application;
using Esi.RAG.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Esi.RAG.Infrastructure;

public sealed class TenantRagOptions
{
    public string CollectionPrefix { get; init; } = "data-";
}

public sealed class InMemoryTenantSyncStateStore : ITenantSyncStateStore
{
    private readonly ConcurrentDictionary<string, TenantSyncState> _states = new(StringComparer.Ordinal);

    public Task<TenantSyncState?> GetAsync(string tenantId, TenantSourceType sourceType, string sourceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _states.TryGetValue(Key(tenantId, sourceType, sourceId), out var state);
        return Task.FromResult(state);
    }

    public Task SaveAsync(TenantSyncState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _states[Key(state.TenantId, state.SourceType, state.SourceId)] = state;
        return Task.CompletedTask;
    }

    private static string Key(string tenantId, TenantSourceType sourceType, string sourceId) => $"{tenantId}|{sourceType}|{sourceId}";
}

public sealed class IsolatedInMemoryTenantVectorStore(string tenantId, string collectionName) : ITenantVectorStore
{
    private readonly ConcurrentDictionary<string, TenantDataDocument> _documents = new(StringComparer.Ordinal);
    public string CollectionName { get; } = collectionName;
    private string TenantId { get; } = tenantId;

    public Task UpsertAsync(IReadOnlyList<TenantDataDocument> documents, CancellationToken cancellationToken)
    {
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(document.TenantId, TenantId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Tenant document does not belong to this tenant collection.");
            }
            _documents[document.Id] = document;
        }
        return Task.CompletedTask;
    }

    public Task<int> DeleteAsync(string tenantId, TenantSourceType sourceType, string sourceId, IReadOnlyCollection<string> sourcePaths, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sourcePaths.Count == 0)
        {
            return Task.FromResult(0);
        }

        var matches = _documents.Values
            .Where(document => document.TenantId == tenantId && document.SourceType == sourceType && document.SourceId == sourceId && sourcePaths.Contains(document.SourcePath, StringComparer.Ordinal))
            .ToArray();
        foreach (var document in matches) _documents.TryRemove(document.Id, out _);
        return Task.FromResult(matches.Length);
    }

    public Task<IReadOnlyList<(TenantDataDocument Document, float Score)>> SearchAsync(IReadOnlyList<float> queryEmbedding, int limit, TenantContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = _documents.Values.Where(document => document.TenantId == context.TenantId && Allowed(document, context)).Select(document => (document, Score: Cosine(queryEmbedding, document.Embedding))).OrderByDescending(result => result.Score).Take(Math.Max(1, limit)).ToArray();
        return Task.FromResult<IReadOnlyList<(TenantDataDocument Document, float Score)>>(results);
    }

    private static bool Allowed(TenantDataDocument document, TenantContext context)
        => (document.AllowedRoles.Count == 0 || document.AllowedRoles.Overlaps(context.Roles)) &&
           (document.AllowedPermissions.Count == 0 || document.AllowedPermissions.Overlaps(context.Permissions));

    private static float Cosine(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        var length = Math.Min(left.Count, right.Count);
        double dot = 0, leftMagnitude = 0, rightMagnitude = 0;
        for (var index = 0; index < length; index++) { dot += left[index] * right[index]; leftMagnitude += left[index] * left[index]; rightMagnitude += right[index] * right[index]; }
        return leftMagnitude == 0 || rightMagnitude == 0 ? 0 : (float)(dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude)));
    }
}

public sealed class TenantVectorStoreFactory(IOptions<TenantRagOptions> options) : ITenantVectorStoreFactory
{
    private readonly ConcurrentDictionary<string, ITenantVectorStore> _stores = new(StringComparer.Ordinal);
    public ITenantVectorStore Get(TenantContext context)
    {
        var collection = $"{options.Value.CollectionPrefix}{context.TenantId}";
        return _stores.GetOrAdd(context.TenantId, _ => new IsolatedInMemoryTenantVectorStore(context.TenantId, collection));
    }
}

public static class TenantInfrastructureDependencyInjection
{
    public static IServiceCollection AddTenantRagInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TenantRagOptions>(configuration.GetSection("TenantRag"));
        services.AddSingleton<ITenantConfigurationResolver, UnconfiguredTenantConfigurationResolver>();
        services.AddSingleton<ITenantSqlSource, UnconfiguredTenantSqlSource>();
        services.AddSingleton<ITenantGraphSource, UnconfiguredTenantGraphSource>();
        services.AddSingleton<ITenantSyncStateStore, InMemoryTenantSyncStateStore>();
        services.AddSingleton<ITenantVectorStoreFactory, TenantVectorStoreFactory>();
        services.AddSingleton<ITenantVectorStoreFactory, TenantQdrantVectorStoreFactory>();
        return services;
    }
}
