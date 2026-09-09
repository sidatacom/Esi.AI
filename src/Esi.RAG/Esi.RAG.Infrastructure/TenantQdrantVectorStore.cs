using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Esi.RAG.Application;
using Esi.RAG.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Esi.RAG.Infrastructure;

public sealed class TenantQdrantVectorStoreFactory(
    IHttpClientFactory httpClientFactory,
    IOptions<QdrantOptions> qdrantOptions,
    IOptions<TenantRagOptions> tenantOptions) : ITenantVectorStoreFactory
{
    private readonly ConcurrentDictionary<string, ITenantVectorStore> _stores = new(StringComparer.Ordinal);

    public ITenantVectorStore Get(TenantContext context)
    {
        var collection = $"{tenantOptions.Value.CollectionPrefix}{context.TenantId}";
        return _stores.GetOrAdd(context.TenantId, _ => new TenantQdrantVectorStore(
            context.TenantId,
            collection,
            httpClientFactory.CreateClient(nameof(QdrantVectorStoreAdapter)),
            qdrantOptions.Value));
    }
}

public sealed class TenantQdrantVectorStore(
    string tenantId,
    string collectionName,
    HttpClient httpClient,
    QdrantOptions options) : ITenantVectorStore
{
    private readonly IsolatedInMemoryTenantVectorStore fallback = new(tenantId, collectionName);
    public string CollectionName { get; } = collectionName;

    public async Task UpsertAsync(IReadOnlyList<TenantDataDocument> documents, CancellationToken cancellationToken)
    {
        await fallback.UpsertAsync(documents, cancellationToken).ConfigureAwait(false);
        if (documents.Count == 0)
        {
            return;
        }

        try
        {
            await EnsureCollectionAsync(documents[0].Embedding.Count, cancellationToken).ConfigureAwait(false);
            var points = documents.Select(document => new
            {
                id = document.Id,
                vector = document.Embedding,
                payload = new
                {
                    tenantId = document.TenantId,
                    sourceType = document.SourceType.ToString(),
                    sourceId = document.SourceId,
                    sourcePath = document.SourcePath,
                    content = document.Content,
                    lastModifiedUtc = document.LastModifiedUtc,
                    contentHash = document.ContentHash,
                    allowedRoles = document.AllowedRoles,
                    allowedPermissions = document.AllowedPermissions,
                    embeddingModel = document.EmbeddingModel,
                    ingestionFingerprint = document.IngestionFingerprint,
                    changeToken = document.ChangeToken,
                },
            }).ToArray();
            using var response = await httpClient.PutAsJsonAsync($"/collections/{CollectionName}/points?wait=true", new { points }, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch when (options.UseInMemoryFallback)
        {
        }
    }

    public async Task<int> DeleteAsync(string tenantId, TenantSourceType sourceType, string sourceId, IReadOnlyCollection<string> sourcePaths, CancellationToken cancellationToken)
    {
        var removed = await fallback.DeleteAsync(tenantId, sourceType, sourceId, sourcePaths, cancellationToken).ConfigureAwait(false);
        if (sourcePaths.Count == 0)
        {
            return removed;
        }

        try
        {
            var pathFilter = new { should = sourcePaths.Select(path => new { key = "sourcePath", match = new { value = path } }).ToArray() };
            using var response = await httpClient.PostAsJsonAsync($"/collections/{CollectionName}/points/delete?wait=true", new
            {
                filter = new { must = new object[] { Match("tenantId", tenantId), Match("sourceType", sourceType.ToString()), Match("sourceId", sourceId), pathFilter } },
            }, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch when (options.UseInMemoryFallback)
        {
        }

        return removed;
    }

    public async Task<IReadOnlyList<(TenantDataDocument Document, float Score)>> SearchAsync(IReadOnlyList<float> queryEmbedding, int limit, TenantContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync($"/collections/{CollectionName}/points/search", new
            {
                vector = queryEmbedding,
                limit = Math.Max(1, limit * 4),
                with_payload = true,
                filter = new { must = new[] { Match("tenantId", context.TenantId) } },
            }, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
            var results = json.RootElement.GetProperty("result").EnumerateArray()
                .Select(result => (Document: ParseDocument(result.GetProperty("id").GetString()!, result.GetProperty("payload")), Score: result.GetProperty("score").GetSingle()))
                .Where(result => Allowed(result.Document, context))
                .Take(Math.Max(1, limit))
                .ToArray();
            return results;
        }
        catch when (options.UseInMemoryFallback)
        {
            return await fallback.SearchAsync(queryEmbedding, limit, context, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureCollectionAsync(int vectorSize, CancellationToken cancellationToken)
    {
        using var probe = await httpClient.GetAsync($"/collections/{CollectionName}", cancellationToken).ConfigureAwait(false);
        if (probe.IsSuccessStatusCode)
        {
            return;
        }

        using var response = await httpClient.PutAsJsonAsync($"/collections/{CollectionName}", new { vectors = new { size = vectorSize, distance = "Cosine" } }, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static object Match(string key, string value) => new { key, match = new { value } };

    private static bool Allowed(TenantDataDocument document, TenantContext context)
        => (document.AllowedRoles.Count == 0 || document.AllowedRoles.Overlaps(context.Roles)) &&
           (document.AllowedPermissions.Count == 0 || document.AllowedPermissions.Overlaps(context.Permissions));

    private static TenantDataDocument ParseDocument(string id, JsonElement payload)
        => new(
            id,
            payload.GetProperty("tenantId").GetString()!,
            Enum.Parse<TenantSourceType>(payload.GetProperty("sourceType").GetString()!, true),
            payload.GetProperty("sourceId").GetString()!,
            payload.GetProperty("sourcePath").GetString()!,
            payload.GetProperty("content").GetString()!,
            payload.GetProperty("lastModifiedUtc").GetDateTimeOffset(),
            payload.GetProperty("contentHash").GetString()!,
            payload.GetProperty("allowedRoles").EnumerateArray().Select(value => value.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase),
            payload.GetProperty("allowedPermissions").EnumerateArray().Select(value => value.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase),
            [],
            payload.GetProperty("embeddingModel").GetString()!,
            payload.GetProperty("ingestionFingerprint").GetString()!,
            payload.TryGetProperty("changeToken", out var token) ? token.GetString() : null);
}
