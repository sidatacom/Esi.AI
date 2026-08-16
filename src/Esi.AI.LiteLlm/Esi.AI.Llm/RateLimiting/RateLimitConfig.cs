namespace Esi.AI.Llm.RateLimiting;

/// <summary>
/// Configuration for rate limiting.
/// </summary>
public class RateLimitConfig
{
    /// <summary>
    /// Maximum number of requests per minute.
    /// </summary>
    public int MaxRequests { get; set; } = 100;

    /// <summary>
    /// Maximum number of requests per second.
    /// </summary>
    public int MaxRequestsPerSecond { get; set; } = 10;

    /// <summary>
    /// Maximum number of requests per hour.
    /// </summary>
    public int MaxRequestsPerHour { get; set; } = 10000;
}
