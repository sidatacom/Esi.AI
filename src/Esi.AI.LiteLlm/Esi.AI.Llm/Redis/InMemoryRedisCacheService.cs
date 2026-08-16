using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Esi.AI.Llm.Redis;

/// <summary>
/// Process-local fallback for <see cref="IRedisCacheService"/>.
/// Lets hosts and tests run without a running Redis instance; state is not shared across instances or restarts.
/// </summary>
public sealed class InMemoryRedisCacheService : IRedisCacheService
{
    private readonly ConcurrentDictionary<string, object> _values = new();
    private readonly ConcurrentDictionary<string, long> _counters = new();

    /// <inheritdoc />
    public Task<T?> GetAsync<T>(string key) where T : class
        => Task.FromResult(_values.TryGetValue(key, out var value) ? value as T : null);

    /// <inheritdoc />
    public Task<bool> SetAsync<T>(string key, T value, int expirationSeconds) where T : class
    {
        _values[key] = value;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<long> IncrementAsync(string key, long increment = 1)
        => Task.FromResult(_counters.AddOrUpdate(key, increment, (_, current) => current + increment));

    /// <inheritdoc />
    public Task<long> GetCounterAsync(string key)
        => Task.FromResult(_counters.TryGetValue(key, out var value) ? value : 0);

    /// <inheritdoc />
    public Task<bool> SetCounterAsync(string key, long value)
    {
        _counters[key] = value;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> KeyExistsAsync(string key)
        => Task.FromResult(_values.ContainsKey(key) || _counters.ContainsKey(key));

    /// <inheritdoc />
    public Task<bool> KeyDeleteAsync(string key)
    {
        var removedValue = _values.TryRemove(key, out _);
        var removedCounter = _counters.TryRemove(key, out _);
        return Task.FromResult(removedValue || removedCounter);
    }

    /// <inheritdoc />
    public Task<List<string>> KeysMatchingAsync(string pattern)
    {
        var regex = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$");
        var keys = _values.Keys.Concat(_counters.Keys).Distinct().Where(k => regex.IsMatch(k)).ToList();
        return Task.FromResult(keys);
    }

    /// <inheritdoc />
    public Task<bool> StoreUsageAsync(string providerName, string model, int inputTokens, int outputTokens, int ttlSeconds = 3600)
    {
        var key = $"usage:{providerName}:{model}";
        _values[key] = new UsageInfo { InputTokens = inputTokens, OutputTokens = outputTokens };
        return Task.FromResult(true);
    }
}
