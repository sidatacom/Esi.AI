using Esi.AI.Llm;
using Esi.AI.Llm.Redis;
using System.Text.Json;

namespace Esi.AI.Llm.Budget;

/// <summary>
/// Budget service for managing user/model budgets.
/// </summary>
public class BudgetService
{
    private readonly IRedisCacheService _redisCache;
    private readonly BudgetConfig _defaultBudget;
    private readonly ILogger<BudgetService>? _logger;

    public BudgetService(
        IRedisCacheService redisCache,
        BudgetConfig defaultBudget,
        ILogger<BudgetService>? logger = null)
    {
        _redisCache = redisCache;
        _defaultBudget = defaultBudget;
        _logger = logger;
    }

    /// <summary>
    /// Checks if the user/model is over budget.
    /// </summary>
    public async Task<BudgetStatus> CheckBudgetAsync(string model, CancellationToken cancellationToken = default)
    {
        var budgetConfig = await GetBudgetConfigAsync(model, cancellationToken);
        var currentUsage = await GetCurrentUsageAsync(model, cancellationToken);

        var status = new BudgetStatus
        {
            IsOverBudget = currentUsage.TotalTokens >= budgetConfig.MaxTokens,
            RemainingTokens = budgetConfig.MaxTokens - currentUsage.TotalTokens,
            BudgetConfig = budgetConfig
        };

        return status;
    }

    /// <summary>
    /// Gets the budget configuration for a model.
    /// </summary>
    public async Task<BudgetConfig> GetBudgetConfigAsync(string model, CancellationToken cancellationToken = default)
    {
        var configKey = $"budget:{model}";
        var config = await _redisCache.GetAsync<BudgetConfig>(configKey);
        return config ?? _defaultBudget;
    }

    /// <summary>
    /// Records usage for a model.
    /// </summary>
    public async Task RecordUsageAsync(string model, int inputTokens, int outputTokens, double cost, CancellationToken cancellationToken = default)
    {
        var usageKey = $"usage:{model}";
        var usage = await _redisCache.GetAsync<UsageInfo>(usageKey);

        if (usage == null)
        {
            usage = new UsageInfo { InputTokens = inputTokens, OutputTokens = outputTokens };
        }
        else
        {
            usage.InputTokens += inputTokens;
            usage.OutputTokens += outputTokens;
        }

        await _redisCache.SetAsync(usageKey, usage, 3600); // 1 hour TTL
    }

    /// <summary>
    /// Gets the current usage for a model.
    /// </summary>
    public async Task<UsageInfo> GetCurrentUsageAsync(string model, CancellationToken cancellationToken = default)
    {
        var usageKey = $"usage:{model}";
        var usage = await _redisCache.GetAsync<UsageInfo>(usageKey);
        return usage ?? new UsageInfo { InputTokens = 0, OutputTokens = 0 };
    }

    /// <summary>
    /// Budget status.
    /// </summary>
    public class BudgetStatus
    {
        /// <summary>Whether the user/model is over budget.</summary>
        public bool IsOverBudget { get; set; }

        /// <summary>Remaining tokens in the budget.</summary>
        public int RemainingTokens { get; set; }

        /// <summary>The budget configuration.</summary>
        public BudgetConfig? BudgetConfig { get; set; }
    }

    /// <summary>
    /// Usage information.
    /// </summary>
    public class UsageInfo
    {
        /// <summary>Input tokens used.</summary>
        public int InputTokens { get; set; }

        /// <summary>Output tokens used.</summary>
        public int OutputTokens { get; set; }

        /// <summary>Total tokens (input + output).</summary>
        public int TotalTokens => InputTokens + OutputTokens;
    }
}
