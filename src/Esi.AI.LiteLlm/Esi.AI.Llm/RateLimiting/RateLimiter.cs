using Esi.AI.Llm;
using Esi.AI.Llm.Redis;
using System.Text.Json;

namespace Esi.AI.Llm.RateLimiting;

/// <summary>
/// Rate limiter for controlling request rates per model/user.
/// </summary>
public class RateLimiter
{
    private readonly IRedisCacheService _redisCache;
    private readonly RateLimitConfig _defaultLimit;
    private readonly ILogger<RateLimiter>? _logger;

    public RateLimiter(
        IRedisCacheService redisCache,
        RateLimitConfig defaultLimit,
        ILogger<RateLimiter>? logger = null)
    {
        _redisCache = redisCache;
        _defaultLimit = defaultLimit;
        _logger = logger;
    }

    /// <summary>
    /// Checks if the request is within the rate limit.
    /// </summary>
    public async Task<RateLimitStatus> CheckRateLimitAsync(string model, CancellationToken cancellationToken = default)
    {
        var limitConfig = await GetRateLimitConfigAsync(model, cancellationToken);
        var currentCount = await GetCurrentCountAsync(model, cancellationToken);

        var status = new RateLimitStatus
        {
            IsOverLimit = currentCount >= limitConfig.MaxRequests,
            RemainingRequests = limitConfig.MaxRequests - (int)currentCount,
            LimitConfig = limitConfig
        };

        return status;
    }

    /// <summary>
    /// Increments the request count for a model.
    /// </summary>
    public async Task IncrementRequestCountAsync(string model, CancellationToken cancellationToken = default)
    {
        var countKey = $"rate_limit:{model}";
        await _redisCache.IncrementAsync(countKey);
    }

    /// <summary>
    /// Resets the request count for a model.
    /// </summary>
    public async Task ResetRequestCountAsync(string model, CancellationToken cancellationToken = default)
    {
        var countKey = $"rate_limit:{model}";
        await _redisCache.SetCounterAsync(countKey, 0);
    }

    /// <summary>
    /// Gets the current request count for a model.
    /// </summary>
    public async Task<long> GetCurrentCountAsync(string model, CancellationToken cancellationToken = default)
    {
        var countKey = $"rate_limit:{model}";
        return await _redisCache.GetCounterAsync(countKey);
    }

    /// <summary>
    /// Gets the rate limit configuration for a model.
    /// </summary>
    public async Task<RateLimitConfig> GetRateLimitConfigAsync(string model, CancellationToken cancellationToken = default)
    {
        var configKey = $"rate_limit:{model}";
        var config = await _redisCache.GetAsync<RateLimitConfig>(configKey);
        return config ?? _defaultLimit;
    }

    /// <summary>
    /// Rate limit status.
    /// </summary>
    public class RateLimitStatus
    {
        /// <summary>Whether the request is over the rate limit.</summary>
        public bool IsOverLimit { get; set; }

        /// <summary>Remaining requests allowed.</summary>
        public int RemainingRequests { get; set; }

        /// <summary>The rate limit configuration.</summary>
        public RateLimitConfig? LimitConfig { get; set; }
    }
}
