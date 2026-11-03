using Esi.AI.Models;

namespace Esi.AI.Studio.Services;

/// <summary>Serializes configured model loads across scoped data-service instances.</summary>
public sealed class ModelConfigurationLoadCoordinator
{
    private readonly SemaphoreSlim loadSemaphore = new(1, 1);

    /// <summary>Runs a configured model operation after any earlier configured load has completed.</summary>
    /// <param name="operation">The configured model operation to run.</param>
    /// <param name="cancellationToken">A token that cancels waiting for the active operation.</param>
    public async Task<ModelLoadStatus> RunAsync(
        Func<Task<ModelLoadStatus>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await loadSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            loadSemaphore.Release();
        }
    }
}
