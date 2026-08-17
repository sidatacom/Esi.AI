namespace Esi.AI.Llm.Budget;

/// <summary>
/// Configuration for a model's budget.
/// </summary>
public class BudgetConfig
{
    /// <summary>
    /// Maximum number of tokens allowed.
    /// </summary>
    public int MaxTokens { get; set; } = 1000000;

    /// <summary>
    /// Maximum number of requests allowed.
    /// </summary>
    public int MaxRequests { get; set; } = 10000;

    /// <summary>
    /// Maximum cost in dollars allowed.
    /// </summary>
    public double MaxCost { get; set; } = 1000.0;
}
