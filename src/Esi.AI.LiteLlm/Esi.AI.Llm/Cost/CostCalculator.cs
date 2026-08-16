using System.Text.Json;
using System.Text.Json.Serialization;

namespace Esi.AI.Llm.Cost;

/// <summary>
/// Cost Calculator for token usage and pricing.
/// </summary>
public class CostCalculator
{
    private readonly Dictionary<string, ModelPrice> _prices = new();
    private readonly string _defaultFallbackModel;
    private readonly double _defaultFallbackCost;

    /// <summary>
    /// Initializes a new CostCalculator.
    /// </summary>
    /// <param name="defaultFallbackModel">Default model to use for unknown models.</param>
    /// <param name="defaultFallbackCost">Default cost per token for unknown models.</param>
    public CostCalculator(string defaultFallbackModel = "unknown", double defaultFallbackCost = 0.0)
    {
        _defaultFallbackModel = defaultFallbackModel;
        _defaultFallbackCost = defaultFallbackCost;
    }

    /// <summary>
    /// Loads prices from a JSON string.
    /// </summary>
    /// <param name="json">JSON string containing the price definitions.</param>
    public void LoadPrices(string json)
    {
        var prices = JsonSerializer.Deserialize<Dictionary<string, ModelPrice>>(json);
        if (prices != null)
        {
            foreach (var kvp in prices)
            {
                _prices[kvp.Key] = kvp.Value;
            }
        }
    }

    /// <summary>
    /// Calculates the cost for a given model and token usage.
    /// </summary>
    /// <param name="model">Model name.</param>
    /// <param name="inputTokens">Number of input tokens.</param>
    /// <param name="outputTokens">Number of output tokens.</param>
    /// <returns>The calculated cost.</returns>
    public double CalculateCost(string model, int inputTokens, int outputTokens)
    {
        if (_prices.TryGetValue(model, out var price))
        {
            return price.InputCost * inputTokens + price.OutputCost * outputTokens;
        }

        return _defaultFallbackCost * (inputTokens + outputTokens);
    }

    /// <summary>
    /// Model price definition.
    /// </summary>
    public class ModelPrice
    {
        /// <summary>Cost per input token.</summary>
        [JsonPropertyName("input_cost")]
        public double InputCost { get; set; }

        /// <summary>Cost per output token.</summary>
        [JsonPropertyName("output_cost")]
        public double OutputCost { get; set; }
    }
}
