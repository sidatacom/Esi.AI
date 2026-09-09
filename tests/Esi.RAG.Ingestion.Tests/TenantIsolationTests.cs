using Esi.RAG.Application;
using Esi.RAG.Domain;
using Esi.RAG.Infrastructure;
using Esi.RAG.Ingestion;

namespace Esi.RAG.Ingestion.Tests;

public sealed class TenantIsolationTests
{
    [Fact]
    public async Task Uses_separate_collections_and_never_returns_another_tenants_data()
    {
        var factory = new TenantVectorStoreFactory(Microsoft.Extensions.Options.Options.Create(new TenantRagOptions()));
        var first = TenantContext.Create("tenant-001", permissions: ["read"]);
        var second = TenantContext.Create("tenant-002", permissions: ["read"]);
        var firstStore = factory.Get(first);
        var secondStore = factory.Get(second);

        await firstStore.UpsertAsync([Document(first, "one")], CancellationToken.None);
        await secondStore.UpsertAsync([Document(second, "two")], CancellationToken.None);

        Assert.Equal("data-tenant-001", firstStore.CollectionName);
        Assert.Equal("data-tenant-002", secondStore.CollectionName);
        Assert.Single(await firstStore.SearchAsync([1, 0], 10, first, CancellationToken.None));
        Assert.Empty(await firstStore.SearchAsync([1, 0], 10, second, CancellationToken.None));
    }

    [Fact]
    public async Task Applies_acl_filtering_during_retrieval()
    {
        var context = TenantContext.Create("tenant-001", permissions: Set("public"));
        var store = new IsolatedInMemoryTenantVectorStore(context.TenantId, "data-tenant-001");
        await store.UpsertAsync([
            Document(context, "allowed", permissions: Set("public")),
            Document(context, "restricted", permissions: Set("admin")),
        ], CancellationToken.None);

        var results = await store.SearchAsync([1, 0], 10, context, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("allowed", results[0].Document.SourcePath);
    }

    [Fact]
    public async Task Synchronization_rejects_items_from_the_wrong_tenant()
    {
        var context = TenantContext.Create("tenant-001");
        var source = new FakeSqlSource(new TenantSourceBatch([
            new TenantDataItem("tenant-002", TenantSourceType.Sql, "orders", "1", "secret", DateTimeOffset.UtcNow, "hash", Set(), Set()),
        ], Set(), null, null));
        var service = CreateService(context, source);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SynchronizeAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task Synchronization_persists_change_tokens_and_deletes_removed_items()
    {
        var context = TenantContext.Create("tenant-001");
        var source = new FakeSqlSource(new TenantSourceBatch([
            new TenantDataItem("tenant-001", TenantSourceType.Sql, "orders", "1", "value", DateTimeOffset.UtcNow, "hash", Set(), Set()),
        ], Set(), "token-1", "cursor-1"));
        var service = CreateService(context, source);
        var first = await service.SynchronizeAsync(null, CancellationToken.None);
        Assert.Equal(1, first.ItemsIndexed);

        source.Batch = new TenantSourceBatch([], Set("1"), "token-2", "cursor-2");
        var second = await service.SynchronizeAsync(null, CancellationToken.None);

        Assert.Equal(1, second.ItemsDeleted);
    }

    private static TenantIngestionService CreateService(TenantContext context, FakeSqlSource source)
        => new(
            new FakeTenantContextAccessor(context),
            new FakeConfigurationResolver(),
            source,
            new FakeGraphSource(),
            new InMemoryTenantSyncStateStore(),
            new TenantVectorStoreFactory(Microsoft.Extensions.Options.Options.Create(new TenantRagOptions())),
            new FixedEmbeddingClient());

    private static TenantDataDocument Document(TenantContext context, string path, IReadOnlySet<string>? permissions = null)
        => new(path, context.TenantId, TenantSourceType.Sql, "orders", path, path, DateTimeOffset.UtcNow, path, Set(), permissions ?? Set(), [1, 0], "test", "fingerprint");

    private static IReadOnlySet<string> Set(params string[] values) => values.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private sealed class FakeTenantContextAccessor(TenantContext context) : ITenantContextAccessor
    {
        public TenantContext GetRequiredContext() => context;
    }

    private sealed class FakeConfigurationResolver : ITenantConfigurationResolver
    {
        public Task<TenantConfiguration> ResolveAsync(TenantContext context, CancellationToken cancellationToken)
            => Task.FromResult(new TenantConfiguration(context.TenantId, "Server=tenant-specific", [new TenantSourceConfiguration(TenantSourceType.Sql, "orders", true, new Dictionary<string, string>())]));
    }

    private sealed class FakeSqlSource(TenantSourceBatch batch) : ITenantSqlSource
    {
        public TenantSourceBatch Batch { get; set; } = batch;
        public Task<TenantSourceBatch> ReadAsync(TenantConfiguration configuration, TenantSourceConfiguration source, TenantSyncState? state, CancellationToken cancellationToken)
            => Task.FromResult(Batch);
    }

    private sealed class FakeGraphSource : ITenantGraphSource
    {
        public Task<TenantSourceBatch> ReadAsync(TenantConfiguration configuration, TenantSourceConfiguration source, TenantSyncState? state, CancellationToken cancellationToken)
            => Task.FromResult(new TenantSourceBatch([], Set(), null, null));
    }

    private sealed class FixedEmbeddingClient : IEmbeddingClient
    {
        public string ModelName => "test";
        public Task<IReadOnlyList<float>> CreateEmbeddingAsync(string text, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<float>>([1, 0]);
    }
}
