using Esi.AI.Llm;
using StackExchange.Redis;
using System.Text.Json;

namespace Esi.AI.Llm.Redis;

/// <summary>
/// Basic Redis cache service implementation using StackExchange.Redis.
/// Provides distributed caching and tracking for provider usage, rate limiting, and metrics.
/// </summary>
public class RedisCacheService : IRedisCacheService
{
    private readonly Lazy<ConnectionMultiplexer> _connection;
    private readonly string _configuration;

    /// <summary>
    /// Initializes a new instance of the RedisCacheService class.
    /// </summary>
    /// <param name="configuration">Redis connection configuration string.</param>
    public RedisCacheService(string configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _connection = new Lazy<ConnectionMultiplexer>(() =>
        {
            var config = ConfigurationOptions.Parse(_configuration);
            return ConnectionMultiplexer.Connect(config);
        });
    }

    /// <summary>
    /// Gets a value from Redis cache.
    /// </summary>
    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        var db = _connection.Value.GetDatabase();
        var value = await db.StringGetAsync(key);
        if (value.IsNullOrEmpty)
        {
            return null;
        }
        return value.ToString()?.FromJson<T>();
    }

    /// <summary>
    /// Sets a value in Redis cache with expiration.
    /// </summary>
    public async Task<bool> SetAsync<T>(string key, T value, int expirationSeconds) where T : class
    {
        var db = _connection.Value.GetDatabase();
        var json = value.ToJson();
        return await db.StringSetAsync(key, json, TimeSpan.FromSeconds(expirationSeconds));
    }

    /// <summary>
    /// Increments a counter in Redis (for usage tracking, rate limiting, etc.).
    /// </summary>
    public async Task<long> IncrementAsync(string key, long increment = 1)
    {
        var db = _connection.Value.GetDatabase();
        return await db.StringIncrementAsync(key, increment);
    }

    /// <summary>
    /// Gets the current value of a counter without incrementing.
    /// </parameter>
    public async Task<long> GetCounterAsync(string key)
    {
        var db = _connection.Value.GetDatabase();
        var value = await db.StringGetAsync(key);
        if (value.IsNullOrEmpty)
        {
            return 0;
        }
        return (long)value;
    }

    /// <summary>
    /// Sets a counter to a specific value.
    /// </parameter>
    public async Task<bool> SetCounterAsync(string key, long value)
    {
        var db = _connection.Value.GetDatabase();
        return await db.StringSetAsync(key, value.ToString());
    }

    /// <summary>
    /// Checks if a key exists in Redis.
    /// </parameter>
    public async Task<bool> KeyExistsAsync(string key)
    {
        var db = _connection.Value.GetDatabase();
        return await db.KeyExistsAsync(key);
    }

    /// <summary>
    /// Removes a key from Redis.
    /// </parameter>
    public async Task<bool> KeyDeleteAsync(string key)
    {
        var db = _connection.Value.GetDatabase();
        return await db.KeyDeleteAsync(key);
    }

    /// <summary>
    /// Gets all keys matching a pattern.
    /// </parameter>
    public async Task<List<string>> KeysMatchingAsync(string pattern)
    {
        var db = _connection.Value.GetDatabase();
        var endpoints = _connection.Value.GetEndPoints();
        var results = new List<string>();

        foreach (var endpoint in endpoints)
        {
            var server = _connection.Value.GetServer(endpoint);
            var keys = server.Keys(database: 0, pattern: pattern);
            results.AddRange(keys.Select(k => k.ToString()));
        }

        return results.Distinct().ToList();
    }

    /// <summary>
    /// Stores usage information for a provider call in Redis.
    /// </parameter>
    public async Task<bool> StoreUsageAsync(string providerName, string model, int inputTokens, int outputTokens, int ttlSeconds = 3600)
    {
        var db = _connection.Value.GetDatabase();
        var key = $"usage:{providerName}:{model}";
        var usage = new UsageInfo { InputTokens = inputTokens, OutputTokens = outputTokens };
        var json = usage.ToJson();
        return await db.StringSetAsync(key, json, TimeSpan.FromSeconds(ttlSeconds));
    }
}