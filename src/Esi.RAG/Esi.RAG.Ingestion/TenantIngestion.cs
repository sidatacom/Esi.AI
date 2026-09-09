using System.Security.Cryptography;
using System.Text;
using Esi.RAG.Application;
using Esi.RAG.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Esi.RAG.Ingestion;

public sealed class TenantIngestionService(
    ITenantContextAccessor tenantContextAccessor,
    ITenantConfigurationResolver configurationResolver,
    ITenantSqlSource sqlSource,
    ITenantGraphSource graphSource,
    ITenantSyncStateStore stateStore,
    ITenantVectorStoreFactory vectorStoreFactory,
    IEmbeddingClient embeddingClient) : ITenantIngestionService
{
    public async Task<TenantSyncReport> SynchronizeAsync(IProgress<TenantSyncProgress>? progress, CancellationToken cancellationToken)
    {
        var context = tenantContextAccessor.GetRequiredContext();
        var configuration = await configurationResolver.ResolveAsync(context, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(configuration.TenantId, context.TenantId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Resolved tenant configuration does not match the authenticated tenant.");
        }

        var startedAt = DateTimeOffset.UtcNow;
        var store = vectorStoreFactory.Get(context);
        var errors = new List<IngestionIssue>();
        var discovered = 0;
        var indexed = 0;
        var skipped = 0;
        var deleted = 0;

        foreach (var source in configuration.Sources.Where(source => source.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await stateStore.GetAsync(context.TenantId, source.SourceType, source.SourceId, cancellationToken).ConfigureAwait(false);
            TenantSourceBatch batch;
            try
            {
                batch = source.SourceType == TenantSourceType.Sql
                    ? await sqlSource.ReadAsync(configuration, source, state, cancellationToken).ConfigureAwait(false)
                    : await graphSource.ReadAsync(configuration, source, state, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add(new IngestionIssue(source.SourceId, $"source failure: {ex.Message}"));
                continue;
            }

            discovered += batch.Items.Count;
            var sourceState = state;
            foreach (var item in batch.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(item.TenantId, context.TenantId, StringComparison.Ordinal) ||
                    item.SourceType != source.SourceType ||
                    !string.Equals(item.SourceId, source.SourceId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("A source returned an item outside its authenticated tenant and source scope.");
                }

                progress?.Report(new TenantSyncProgress(context.TenantId, source.SourceType, source.SourceId, discovered, indexed, skipped, deleted, item.SourcePath));
                var embedding = await embeddingClient.CreateEmbeddingAsync(item.Content, cancellationToken).ConfigureAwait(false);
                var document = new TenantDataDocument(
                    StableId(context.TenantId, source.SourceType, source.SourceId, item.SourcePath),
                    context.TenantId,
                    item.SourceType,
                    item.SourceId,
                    item.SourcePath,
                    item.Content,
                    item.LastModifiedUtc,
                    item.ContentHash,
                    item.AllowedRoles,
                    item.AllowedPermissions,
                    embedding,
                    embeddingClient.ModelName,
                             ComputeHash($"{embeddingClient.ModelName}|{item.SourceType}|{source.SourceId}"));
                        await store.UpsertAsync([document], cancellationToken).ConfigureAwait(false);
                        indexed++;
                    }

                    if (batch.DeletedSourcePaths.Count > 0)
                    {
                        deleted += await store.DeleteAsync(context.TenantId, source.SourceType, source.SourceId, batch.DeletedSourcePaths, cancellationToken).ConfigureAwait(false);
                    }

                    await stateStore.SaveAsync(new TenantSyncState(context.TenantId, source.SourceType, source.SourceId, batch.ChangeToken ?? state?.ChangeToken, DateTimeOffset.UtcNow, batch.Cursor ?? state?.Cursor), cancellationToken).ConfigureAwait(false);
        }

        progress?.Report(new TenantSyncProgress(context.TenantId, TenantSourceType.Sql, "complete", discovered, indexed, skipped, deleted, null));
        return new TenantSyncReport(context.TenantId, startedAt, DateTimeOffset.UtcNow, discovered, indexed, skipped, deleted, errors);
    }

    private static string StableId(string tenantId, TenantSourceType sourceType, string sourceId, string path)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId}|{sourceType}|{sourceId}|{path}"))).ToLowerInvariant();

    private static string ComputeHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public static class TenantIngestionDependencyInjection
{
    public static IServiceCollection AddTenantRagIngestion(this IServiceCollection services)
    {
        services.AddScoped<ITenantIngestionService, TenantIngestionService>();
        return services;
    }
}
