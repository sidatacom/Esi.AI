using System.Collections.Concurrent;
using Esi.AI.Models;

namespace Esi.AI.Core.ModelLoading;

/// <summary>Represents the observable lifecycle phase of one model operation.</summary>
public enum ModelLifecyclePhase
{
    Loading,
    Loaded,
    Failed
}

/// <summary>Describes the current lifecycle state for one backend/model pair.</summary>
public sealed record ModelLifecycleState(
    string ModelPath,
    ConfigurationBackend Backend,
    string Runtime,
    ModelLifecyclePhase Phase,
    DateTimeOffset UpdatedAtUtc,
    string? Error = null);

/// <summary>Maintains explicit, concurrency-safe model lifecycle transitions.</summary>
public sealed class ModelLifecycleCoordinator
{
    private readonly ConcurrentDictionary<string, ModelLifecycleState> states = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Starts a loading transition and publishes it as the current state.</summary>
    public ModelLifecycleState Begin(string modelPath, ConfigurationBackend backend, string runtime)
    {
        var state = new ModelLifecycleState(modelPath, backend, runtime, ModelLifecyclePhase.Loading, DateTimeOffset.UtcNow);
        states[CreateKey(modelPath, backend)] = state;
        return state;
    }

    /// <summary>Marks an active loading transition as successfully loaded.</summary>
    public ModelLifecycleState Complete(string modelPath, ConfigurationBackend backend, string runtime)
    {
        var state = new ModelLifecycleState(modelPath, backend, runtime, ModelLifecyclePhase.Loaded, DateTimeOffset.UtcNow);
        states[CreateKey(modelPath, backend)] = state;
        return state;
    }

    /// <summary>Marks an active loading transition as failed and stores its diagnostic.</summary>
    public ModelLifecycleState Fail(string modelPath, ConfigurationBackend backend, string runtime, string error)
    {
        var state = new ModelLifecycleState(modelPath, backend, runtime, ModelLifecyclePhase.Failed, DateTimeOffset.UtcNow, error);
        states[CreateKey(modelPath, backend)] = state;
        return state;
    }

    /// <summary>Returns the current state for one model/backend pair, if present.</summary>
    public ModelLifecycleState? Read(string modelPath, ConfigurationBackend backend) =>
        states.TryGetValue(CreateKey(modelPath, backend), out var state) ? state : null;

    /// <summary>Returns a stable snapshot of every tracked lifecycle state.</summary>
    public IReadOnlyList<ModelLifecycleState> ReadAll() => states.Values.OrderBy(state => state.ModelPath, StringComparer.OrdinalIgnoreCase).ToArray();

    private static string CreateKey(string modelPath, ConfigurationBackend backend) => $"{backend}|{modelPath}";
}
