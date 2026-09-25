namespace Esi.AI.Backend.Abstractions;

/// <summary>Resolves all registered backend runtimes by their stable variant identity.</summary>
public interface IBackendRuntimeCatalog
{
    /// <summary>Gets all runtimes registered by backend modules.</summary>
    IReadOnlyCollection<IBackendRuntime> Runtimes { get; }

    /// <summary>Resolves one backend runtime by its variant ID.</summary>
    /// <param name="variantId">The stable backend variant ID.</param>
    /// <returns>The runtime registered for the variant ID.</returns>
    IBackendRuntime Resolve(string variantId);
}

/// <summary>Provides deterministic runtime selection and rejects ambiguous variant identities.</summary>
public sealed class BackendRuntimeCatalog : IBackendRuntimeCatalog
{
    private readonly IReadOnlyDictionary<string, IBackendRuntime> runtimesById;

    /// <summary>Creates a catalog from all backend runtimes registered in dependency injection.</summary>
    /// <param name="runtimes">All registered backend runtimes.</param>
    /// <exception cref="ArgumentException">Two runtimes declare the same variant ID.</exception>
    public BackendRuntimeCatalog(IEnumerable<IBackendRuntime> runtimes)
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
}