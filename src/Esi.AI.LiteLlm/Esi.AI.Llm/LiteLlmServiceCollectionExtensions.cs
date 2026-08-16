using Esi.AI.Llm;
using Esi.AI.Llm.Cost;
using Esi.AI.Llm.Redis;
using Esi.AI.Llm.Providers;
using Esi.AI.Llm.Router;
using Esi.AI.Llm.Budget;
using Esi.AI.Llm.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Esi.AI.Llm;

/// <summary>
/// Service collection extensions for registering LiteLLM services.
/// </summary>
public static class LiteLlmServiceCollectionExtensions
{
    /// <summary>
    /// Registers all LiteLLM services with the service collection.
    /// </summary>
    public static IServiceCollection AddLiteLlmServices(this IServiceCollection services)
    {
        services.AddHttpClient();

        services.AddSingleton<IProviderRegistry>(sp =>
        {
            var registry = new ProviderRegistry();
            foreach (var provider in sp.GetServices<IChatCompletionProvider>())
            {
                registry.Register(new DeploymentConfig
                {
                    Name = provider.Name,
                    Model = provider.Name,
                    Provider = provider.Name,
                    IsActive = true
                }, provider);
            }

            return registry;
        });

        // Built lazily so that every IChatCompletionProvider registered via AddXProvider (in any
        // order, before the ServiceProvider is built) is already present as an active deployment.
        services.AddSingleton<ProviderRouter>(sp =>
        {
            var router = new ProviderRouter();
            foreach (var provider in sp.GetServices<IChatCompletionProvider>())
            {
                router.RegisterDeployment(new DeploymentConfig
                {
                    Name = provider.Name,
                    Model = provider.Name,
                    Provider = provider.Name,
                    IsActive = true
                }, provider);
            }

            return router;
        });

        // Register the cost calculator
        services.AddSingleton<CostCalculator>();

        // Register default budget/rate-limit configuration; hosts can override via TryAddSingleton before this call.
        services.TryAddSingleton(new Esi.AI.Llm.Budget.BudgetConfig());
        services.TryAddSingleton(new Esi.AI.Llm.RateLimiting.RateLimitConfig());

        // Register the budget service
        services.AddSingleton<BudgetService>();

        // Register the rate limiter
        services.AddSingleton<RateLimiter>();

        // Register the orchestrator
        services.AddSingleton<Orchestrator>();

        // Defaults to an in-memory cache so local dev/tests work without a running Redis instance.
        // Call AddRedisCache(connectionString) to opt into a real distributed Redis backend.
        services.TryAddSingleton<IRedisCacheService, InMemoryRedisCacheService>();

        // Register the pricing configuration service
        services.AddSingleton<PricingConfiguration>();

        // Register the provider status service
        services.AddSingleton<ProviderStatus>();

        return services;
    }

    /// <summary>
    /// Registers a distributed Redis-backed cache, replacing the in-memory default.
    /// </summary>
    public static IServiceCollection AddRedisCache(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IRedisCacheService>(_ => new RedisCacheService(connectionString));
        return services;
    }

    /// <summary>
    /// Registers the OpenAI provider.
    /// </summary>
    public static IServiceCollection AddOpenAiProvider(this IServiceCollection services, string apiKey, string endpoint = "https://api.openai.com/v1")
    {
        services.AddSingleton<IChatCompletionProvider, OpenAiProvider>(sp =>
        {
            var config = sp.GetRequiredService<PricingConfiguration>();
            return new OpenAiProvider(apiKey, endpoint, sp.GetRequiredService<IHttpClientFactory>().CreateClient());
        });

        return services;
    }

    /// <summary>
    /// Registers the Anthropic provider.
    /// </summary>
    public static IServiceCollection AddAnthropicProvider(this IServiceCollection services, string apiKey, string endpoint = "https://anthropic.com/v1")
    {
        services.AddSingleton<IChatCompletionProvider, AnthropicProvider>(sp =>
        {
            var config = sp.GetRequiredService<PricingConfiguration>();
            return new AnthropicProvider(apiKey, endpoint);
        });

        return services;
    }

    /// <summary>
    /// Registers the Google Gemini provider.
    /// </summary>
    public static IServiceCollection AddGoogleGeminiProvider(this IServiceCollection services, string apiKey, string endpoint = "https://google.com/v1/gemini")
    {
        services.AddSingleton<IChatCompletionProvider, GoogleGeminiProvider>(sp =>
        {
            var config = sp.GetRequiredService<PricingConfiguration>();
            return new GoogleGeminiProvider(apiKey, endpoint);
        });

        return services;
    }

    /// <summary>
    /// Registers the Azure OpenAI provider.
    /// </summary>
    public static IServiceCollection AddAzureOpenAiProvider(this IServiceCollection services, string apiKey, string endpoint, string deploymentName)
    {
        services.AddSingleton<IChatCompletionProvider, AzureOpenAiProvider>(sp =>
        {
            var config = sp.GetRequiredService<PricingConfiguration>();
            return new AzureOpenAiProvider(apiKey, endpoint, deploymentName);
        });

        return services;
    }

    /// <summary>
    /// Registers the Ollama provider.
    /// </summary>
    public static IServiceCollection AddOllamaProvider(this IServiceCollection services, string endpoint = "http://localhost:11434")
    {
        services.AddSingleton<IChatCompletionProvider, OllamaProvider>(sp =>
        {
            var config = sp.GetRequiredService<PricingConfiguration>();
            return new OllamaProvider(endpoint);
        });

        return services;
    }
}
