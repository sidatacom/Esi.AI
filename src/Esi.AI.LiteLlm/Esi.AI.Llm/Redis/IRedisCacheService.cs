namespace Esi.AI.Llm.Redis;

/// <summary>
/// Interface for Redis-based distributed caching and tracking.
/// Provides methods for rate limiting, usage tracking, and distributed state management.
/// </summary>
public interface IRedisCacheService
{
    /// <summary>
    /// Get a value from Redis cache.
    /// </summary>
    /// <typeparam name="T">The type of value to retrieve.</typeparam>
    /// <param name key="key">The cache key.</param>
    /// <returns>The cached value, or default if not found.</returns>
    Task<T?> GetAsync<T>(string key) where T : class;

    /// <summary>
    /// Set a value in Redis cache with expiration.
    /// </summary>
    /// <typeparam name="T">The type of value to store.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param value="value">The value to store.</param>
    /// <param expirationSeconds>Expiration time in seconds.</param>
    /// <returns>True if successful.</returns>
    Task<bool> SetAsync<T>(string key, T value, int expirationSeconds) where T : class;

    /// <summary>
    /// Increment a counter in Redis (for usage tracking, rate limiting, etc.).
    /// </summary>
    /// <param name="key">The counter key.</param>
    /// <param name="increment">The amount to increment by.</param>
    /// <returns>The new counter value.</returns>
    Task<long> IncrementAsync(string key, long increment = 1);

    /// <summary>
    /// Get the current value of a counter without incrementing.
    /// </parameter>
    /// <param name="key">The counter key.</param>
    /// <returns>The current counter value.</returns>
    Task<long> GetCounterAsync(string key);

    /// <summary>
    /// Set a counter to a specific value.
    /// </parameter>
    /// <param name="key">The counter key.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>True if successful.</returns>
    Task<bool> SetCounterAsync(string key, long value);

    /// <summary>
    /// Check if a key exists in Redis.
    /// </parameter>
    /// <param name="key">The key to check.</param>
    /// <returns>True if key exists.</returns>
    Task<bool> KeyExistsAsync(string key);

    /// <summary>
    /// Remove a key from Redis.
    /// </parameter>
    /// <param name="key">The key to remove.</param>
    /// <returns>True if successful.</returns>
    Task<bool> KeyDeleteAsync(string key);

    /// <summary>
    /// Get all keys matching a pattern (for cleanup, monitoring).
    /// </parameter>
    /// <param name="pattern">The pattern to match (e.g., "usage:*").</param>
    /// <returns>List of matching keys.</returns>
    Task<List<string>> KeysMatchingAsync(string pattern);

    /// <summary>
    /// Store usage information for a provider call.
    /// </parameter>
    /// <param name="providerName">Name of the provider (e.g., "openai", "anthropic").</param>
    /// <param name="model">Model name.</param>
    /// <param name="inputTokens">Number of input tokens.</param>
    /// <param name="outputTokens">Number of output tokens.</param>
    /// <param name="ttlSeconds">Time-to-live in seconds.</param>
    /// <returns>True if successful.</returns>
    Task<bool> StoreUsageAsync(string providerName, string model, int inputTokens, int outputTokens, int ttlSeconds = 3600);
}