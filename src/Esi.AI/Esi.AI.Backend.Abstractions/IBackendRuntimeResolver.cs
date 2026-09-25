using Esi.AI.Models;

namespace Esi.AI.Backend.Abstractions;

/// <summary>Resolves all registered backend runtimes by their stable variant identity.</summary>
public interface IBackendRuntimeResolver
{
    /// <summary>Gets all runtimes registered by backend modules.</summary>
    IReadOnlyCollection<IBackendRuntime> Runtimes { get; }

    /// <summary>Resolves one backend runtime by its variant ID.</summary>
    /// <param name="variantId">The stable backend variant ID.</param>
    /// <returns>The runtime registered for the variant ID.</returns>
    IBackendRuntime Resolve(string variantId);

    /// <summary>Resolves one backend runtime by its persisted family and selected route.</summary>
    /// <param name="family">The persisted backend family.</param>
    /// <param name="route">The selected device or engine route.</param>
    /// <returns>The unique runtime registered for the family and route.</returns>
    IBackendRuntime Resolve(ConfigurationBackend family, string route);
}

/// <summary>Provides deterministic runtime selection and rejects ambiguous variant identities.</summary>
public sealed class BackendRuntimeResolver : IBackendRuntimeResolver
{
    private readonly IReadOnlyDictionary<string, IBackendRuntime> runtimesById;

    /// <summary>Creates a catalog from all backend runtimes registered in dependency injection.</summary>
    /// <param name="runtimes">All registered backend runtimes.</param>
    /// <exception cref="ArgumentException">Two runtimes declare the same variant ID.</exception>
    public BackendRuntimeResolver(IEnumerable<IBackendRuntime> runtimes)
    {
        ArgumentNullException.ThrowIfNull(runtimes);
        var runtimeArray = runtimes.ToArray();
        runtimesById = runtimeArray.ToDictionary(runtime => runtime.Descriptor.Id, StringComparer.OrdinalIgnoreCase);
        Runtimes = Array.AsReadOnly(runtimeArray);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<IBackendRuntime> Runtimes { get; }

    /// <inheritdoc />
    public IBackendRuntime Resolve(string variantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variantId);
        return runtimesById.TryGetValue(variantId, out var runtime)
            ? runtime
            : throw new ArgumentException($"No backend runtime is registered for variant '{variantId}'.", nameof(variantId));
    }

    /// <inheritdoc />
    public IBackendRuntime Resolve(ConfigurationBackend family, string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        var matches = Runtimes.Where(runtime =>
            runtime.Descriptor.Family == family &&
            string.Equals(runtime.Descriptor.Route, route, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new ArgumentException($"No backend runtime is registered for '{family}' route '{route}'.", nameof(route)),
            _ => throw new InvalidOperationException($"Multiple backend runtimes are registered for '{family}' route '{route}'.")
        };
    }
}