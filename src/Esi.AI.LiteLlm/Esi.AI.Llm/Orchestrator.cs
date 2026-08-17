using Esi.AI.Llm.Budget;
using Esi.AI.Llm.Cost;
using Esi.AI.Llm.Redis;
using Esi.AI.Llm.Router;
using Esi.AI.Llm.Providers;
using Esi.AI.Llm.RateLimiting;

namespace Esi.AI.Llm;

/// <summary>
/// Orchestrator that ties together the router, cost calculator, and providers.
/// </summary>
public class Orchestrator
{
    private readonly ProviderRouter _router;
    private readonly CostCalculator _costCalculator;
    private readonly BudgetService _budgetService;
    private readonly RateLimiter _rateLimiter;
    private readonly IRedisCacheService _redisCache;
    private readonly ILogger<Orchestrator>? _logger;

    public Orchestrator(
        ProviderRouter router,
        CostCalculator costCalculator,
        BudgetService budgetService,
        RateLimiter rateLimiter,
        IRedisCacheService redisCache,
        ILogger<Orchestrator>? logger = null)
    {
        _router = router;
        _costCalculator = costCalculator;
        _budgetService = budgetService;
        _rateLimiter = rateLimiter;
        _redisCache = redisCache;
        _logger = logger;
    }

    /// <summary>
    /// Orchestrates a chat completion request.
    /// </summary>
    public async Task<ProviderResult> OrchestrateAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate budget
        var budgetStatus = await _budgetService.CheckBudgetAsync(request.Model, cancellationToken);
        if (budgetStatus.IsOverBudget)
        {
            return new ProviderResult
            {
                Id = Guid.NewGuid().ToString(),
                Content = string.Empty,
                FinishReason = "budget_exceeded",
                Error = new ProviderResult.ErrorInfo
                {
                    Reason = "Budget exceeded for model",
                    Code = "BUDGET_EXCEEDED",
                    StatusCode = 429
                }
            };
        }

        // Check rate limits
        var rateLimitStatus = await _rateLimiter.CheckRateLimitAsync(request.Model, cancellationToken);
        if (rateLimitStatus.IsOverLimit)
        {
            return new ProviderResult
            {
                Id = Guid.NewGuid().ToString(),
                Content = string.Empty,
                FinishReason = "rate_limit_exceeded",
                Error = new ProviderResult.ErrorInfo
                {
                    Reason = "Rate limit exceeded for model",
                    Code = "RATE_LIMIT_EXCEEDED",
                    StatusCode = 429
                }
            };
        }

        // Select deployment
        var (deployment, provider) = _router.SelectDeployment(
            request.Temperature.HasValue ? RoutingStrategy.LowestCost : RoutingStrategy.RoundRobin,
            null);

        if (deployment == null || provider == null)
        {
            return new ProviderResult
            {
                Id = Guid.NewGuid().ToString(),
                Content = string.Empty,
                FinishReason = "no_deployment",
                Error = new ProviderResult.ErrorInfo
                {
                    Reason = "No available deployment for model",
                    Code = "NO_DEPLOYMENT",
                    StatusCode = 503
                }
            };
        }

        // Complete the request
        var result = await provider.CompleteAsync(request, cancellationToken);

        // Calculate and store cost
        if (result.Usage != null)
        {
            var cost = _costCalculator.CalculateCost(request.Model, result.Usage.InputTokens, result.Usage.OutputTokens);
            await _budgetService.RecordUsageAsync(request.Model, result.Usage.InputTokens, result.Usage.OutputTokens, cost, cancellationToken);
            await _redisCache.StoreUsageAsync(provider.Name, request.Model, result.Usage.InputTokens, result.Usage.OutputTokens);
        }

        // Update metrics

        return result;
    }

    /// <summary>
    /// Orchestrates a streaming chat completion request.
    /// </summary>
    public async IAsyncEnumerable<Chunk> OrchestrateStreamingAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate budget
        var budgetStatus = await _budgetService.CheckBudgetAsync(request.Model, cancellationToken);
        if (budgetStatus.IsOverBudget)
        {
            yield return new Chunk
            {
                Id = Guid.NewGuid().ToString(),
                Content = string.Empty,
                FinishReason = "budget_exceeded"
            };
            yield break;
        }

        // Check rate limits
        var rateLimitStatus = await _rateLimiter.CheckRateLimitAsync(request.Model, cancellationToken);
        if (rateLimitStatus.IsOverLimit)
        {
            yield return new Chunk
            {
                Id = Guid.NewGuid().ToString(),
                Content = string.Empty,
                FinishReason = "rate_limit_exceeded"
            };
            yield break;
        }

        // Select deployment
        var (deployment, provider) = _router.SelectDeployment(
            request.Temperature.HasValue ? RoutingStrategy.LowestCost : RoutingStrategy.RoundRobin,
            null);

        if (deployment == null || provider == null)
        {
            yield return new Chunk
            {
                Id = Guid.NewGuid().ToString(),
                Content = string.Empty,
                FinishReason = "no_deployment"
            };
            yield break;
        }

        // Stream the response
        await foreach (var chunk in provider.CompleteStreamingAsync(request, cancellationToken))
        {
            yield return chunk;
        }
    }
}
