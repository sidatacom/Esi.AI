using Esi.RAG.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Esi.RAG.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRagInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RagOptions>(configuration.GetSection("Rag"));
        services.Configure<IngestionOptions>(configuration.GetSection("Rag"));
        services.Configure<LmStudioOptions>(configuration.GetSection("LmStudio"));
        services.Configure<QdrantOptions>(configuration.GetSection("Qdrant"));

        services.AddHttpClient(nameof(LmStudioClient), (sp, client) =>
        {
            var lmStudioOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LmStudioOptions>>().Value;
            client.BaseAddress = new Uri(lmStudioOptions.BaseUrl);
            if (!string.IsNullOrWhiteSpace(lmStudioOptions.ApiKey))
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", lmStudioOptions.ApiKey);
            }

            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, lmStudioOptions.TimeoutSeconds));
        });

        services.AddHttpClient(nameof(QdrantVectorStoreAdapter), (sp, client) =>
        {
            var qdrantOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<QdrantOptions>>().Value;
            client.BaseAddress = new Uri(qdrantOptions.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, qdrantOptions.TimeoutSeconds));
        });

        services.AddSingleton<IGitMetadataReader, GitMetadataReader>();
        services.AddSingleton<IRepositoryDiscovery, FileDiscoveryService>();
        services.AddSingleton<ISourceExtractor, CompositeSourceExtractor>();
        services.AddSingleton<IChunker, LinePreservingChunker>();

        services.AddSingleton<LmStudioClient>();
        services.AddSingleton<IEmbeddingClient>(sp => sp.GetRequiredService<LmStudioClient>());
        services.AddSingleton<ITextGenerationClient>(sp => sp.GetRequiredService<LmStudioClient>());

        services.AddSingleton<InMemoryVectorStore>();
        services.AddSingleton<QdrantVectorStoreAdapter>();
        services.AddSingleton<IVectorStore>(sp => sp.GetRequiredService<QdrantVectorStoreAdapter>());

        services.AddSingleton<IHealthService, InfrastructureHealthService>();
        return services;
    }
}
