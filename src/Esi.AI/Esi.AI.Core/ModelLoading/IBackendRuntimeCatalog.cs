using Esi.AI.Models;

namespace Esi.AI.Core.ModelLoading;

/// <summary>Reads the current persisted backend runtime catalog.</summary>
public interface IBackendRuntimeCatalog
{
    /// <summary>Returns the current runtime options from the application source of truth.</summary>
    Task<BackendRuntimeOptions> ReadAsync(CancellationToken cancellationToken = default);
}
