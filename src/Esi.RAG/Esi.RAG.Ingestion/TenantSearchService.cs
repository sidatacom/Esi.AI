using Esi.RAG.Application;
using Esi.RAG.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Esi.RAG.Ingestion;

public sealed class TenantSearchService(
    ITenantContextAccessor tenantContextAccessor,
    ITenantVectorStoreFactory vectorStoreFactory,
    IEmbeddingClient embeddingClient) : ITenantSearchService
{
    public async Task<IReadOnlyList<TenantDataDocument>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("A search query is required.", nameof(query));
        }

        var context = tenantContextAccessor.GetRequiredContext();
        var embedding = await embeddingClient.CreateEmbeddingAsync(query, cancellationToken).ConfigureAwait(false);
        var matches = await vectorStoreFactory.Get(context).SearchAsync(embedding, Math.Clamp(limit, 1, 50), context, cancellationToken).ConfigureAwait(false);
        return matches.Select(match => match.Document).ToArray();
    }
}

public static class TenantSearchDependencyInjection
{
    public static IServiceCollection AddTenantRagSearch(this IServiceCollection services)
    {
        services.AddScoped<ITenantSearchService, TenantSearchService>();
        return services;
    }
}
