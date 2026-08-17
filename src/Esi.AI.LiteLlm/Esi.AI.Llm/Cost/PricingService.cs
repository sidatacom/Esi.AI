namespace Esi.AI.Llm.Cost;

/// <summary>
/// Singleton service that provides access to the global pricing configuration.
/// In a real application, this would be registered as a scoped service in DI.
/// </summary>
public static class PricingService
{
    private static PricingConfiguration? _configuration;
    private static readonly object _lock = new object();

    /// <summary>
    /// Gets or sets the global pricing configuration.
    /// </summary>
    public static PricingConfiguration? Configuration
    {
        get => _configuration;
        set
        {
            lock (_lock)
            {
                _configuration = value;
            }
        }
    }

    /// <summary>
    /// Resets the pricing configuration to null.
    /// </parameter>
    public static void Reset()
    {
        lock (_lock)
        {
            _configuration = null;
        }
    }

    /// <summary>
    /// Calculates the cost for a provider call.
    /// </summary>
    /// <param name="modelName">Name of the model used.</param>
    /// <param name="providerName">Name of the provider.</param>
    /// <param name="inputTokens">Number of input tokens.</param>
    /// <param name="outputTokens">Number of output tokens.</param>
    /// <returns>Total cost in USD.</returns>
    public static decimal CalculateCost(string modelName, string providerName, int inputTokens, int outputTokens)
    {
        lock (_lock)
        {
            return _configuration?.CalculateCost(modelName, providerName, inputTokens, outputTokens) ?? 0m;
        }
    }

    /// <summary>
    /// Gets the cost per token for a model/provider combination.
    /// </summary>
    /// <param name="modelName">Name of the model.</param>
    /// <param name="providerName">Name of the provider.</param>
    /// <returns>Cost per token in USD, or 0 if not configured.</returns>
    public static decimal CostPerToken(string modelName, string providerName)
    {
        lock (_lock)
        {
            return _configuration?.CostPerToken(modelName, providerName) ?? 0m;
        }
    }

    /// <summary>
    /// Checks if pricing is configured for a specific model/provider.
    /// </summary>
    /// <param name="modelName">Name of the model.</param>
    /// <param name="providerName">Name of the provider.</param>
    /// <returns>True if pricing is configured.</returns>
    public static bool HasPricing(string modelName, string providerName)
    {
        lock (_lock)
        {
            return _configuration?.GetPricing(modelName, providerName) != null;
        }
    }
}