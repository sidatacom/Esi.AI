namespace Esi.AI.Llm;

/// <summary>
/// Provider status information.
/// </summary>
public class ProviderStatus
{
    /// <summary>
    /// Provider identifier.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Whether the provider is healthy.
    /// </summary>
    public bool IsHealthy { get; set; } = true;

    /// <summary>
    /// Last health check timestamp.
    /// </summary>
    public DateTime LastHealthCheck { get; set; } = DateTime.Now;

    /// <summary>
    /// Error message if the provider is unhealthy.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
