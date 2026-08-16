namespace Esi.AI.Llm.Cost;

/// <summary>
/// Pricing information for a specific model/provider combination.
/// Stores token pricing for input and output tokens, as well as any fixed costs.
/// </summary>
public sealed class ModelPricing
{
    /// <summary>Name of the model (e.g., "gpt-4o", "claude-3-opus-20240229").</summary>
    public string ModelName { get; set; } = default!;

    /// <summary>Name of the provider (e.g., "openai", "anthropic", "gemini").</summary>
    public string ProviderName { get; set; } = default!;

    /// <summary>Cost per input token in USD.</summary>
    public decimal InputTokenPrice { get; set; }

    /// <summary>Cost per output token in USD.</summary>
    public decimal OutputTokenPrice { get; set; }

    /// <summary>Fixed cost per request (e.g., overhead, context setup).</summary>
    public decimal FixedCost { get; set; }

    /// <summary>Whether this pricing is active/available.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Pricing configuration container that holds pricing for multiple models.
/// Manages lookup and retrieval of model pricing by provider and model name.
/// </summary>
public sealed class PricingConfiguration
{
    /// <summary>All configured model prices.</summary>
    public List<ModelPricing> ModelPricings { get; set; } = new List<ModelPricing>();

    /// <summary>
    /// Adds pricing for a specific model/provider combination.
    /// </summary>
    /// <param name="modelName">Name of the model.</param>
    /// <param name="providerName">Name of the provider.</param>
    /// <param name="inputTokenPrice">Cost per input token.</param>
    /// <param name="outputTokenPrice">Cost per output token.</param>
    /// <param name="fixedCost">Fixed cost per request.</param>
    public void AddPricing(string modelName, string providerName, decimal inputTokenPrice, decimal outputTokenPrice, decimal fixedCost = 0)
    {
        ModelPricings.Add(new ModelPricing
        {
            ModelName = modelName,
            ProviderName = providerName,
            InputTokenPrice = inputTokenPrice,
            OutputTokenPrice = outputTokenPrice,
            FixedCost = fixedCost
        });
    }

    /// <summary>
    /// Gets pricing for a specific model/provider combination.
    /// </summary>
    /// <param name="modelName">Name of the model.</param>
    /// <param name="providerName">Name of the provider.</param>
    /// <returns>The ModelPricing if found, null otherwise.</returns>
    public ModelPricing? GetPricing(string modelName, string providerName)
    {
        return ModelPricings.FirstOrDefault(p =>
            string.Equals(p.ModelName, modelName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Calculates the total cost for a given token usage.
    /// </summary>
    /// <param name="modelName">Name of the model used.</param>
    /// <param name="providerName">Name of the provider.</param>
    /// <param name="inputTokens">Number of input tokens.</param>
    /// <param name="outputTokens">Number of output tokens.</param>
    /// <returns>Total cost in USD.</returns>
    public decimal CalculateCost(string modelName, string providerName, int inputTokens, int outputTokens)
    {
        var pricing = GetPricing(modelName, providerName);
        if (pricing == null)
        {
            return 0m; // Default to 0 if pricing not configured
        }

        var inputCost = inputTokens * pricing.InputTokenPrice;
        var outputCost = outputTokens * pricing.OutputTokenPrice;
        return inputCost + outputCost + pricing.FixedCost;
    }

    /// <summary>
    /// Calculates the cost per token (weighted average) for a given model/provider.
    /// </summary>
    /// <param name="modelName">Name of the model.</param>
    /// <param name="providerName">Name of the provider.</param>
    /// <returns>Cost per token in USD, or 0 if pricing not found.</returns>
    public decimal CostPerToken(string modelName, string providerName)
    {
        var pricing = GetPricing(modelName, providerName);
        if (pricing == null || pricing.InputTokenPrice + pricing.OutputTokenPrice == 0)
        {
            return 0m;
        }

        // Weighted average based on typical usage ratio (assuming 1:1 for simplicity)
        return (pricing.InputTokenPrice + pricing.OutputTokenPrice) / 2m;
    }
}