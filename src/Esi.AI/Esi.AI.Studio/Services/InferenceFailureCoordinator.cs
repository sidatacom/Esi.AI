using Esi.AI.Core.ModelLoading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Esi.AI.Studio.Services;

/// <summary>Handles an unrecoverable backend inference failure for the current Studio process.</summary>
public interface IInferenceFailureCoordinator
{
    /// <summary>Stops every model runtime and requests host shutdown once.</summary>
    Task FailAsync(Exception exception);
}

/// <summary>Unloads all backends before terminating the host after a backend inference failure.</summary>
public sealed class InferenceFailureCoordinator(
    IModelRuntimeShutdown modelRuntime,
    IHostApplicationLifetime applicationLifetime,
    ILogger<InferenceFailureCoordinator> logger) : IInferenceFailureCoordinator
{
    private int shutdownStarted;

    /// <inheritdoc />
    public async Task FailAsync(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Interlocked.Exchange(ref shutdownStarted, 1) != 0)
            return;

        logger.LogCritical(exception, "Fatal inference failure; stopping model runtimes and shutting down Studio.");
        try
        {
            await modelRuntime.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception cleanupException)
        {
            logger.LogCritical(cleanupException, "Model runtime cleanup failed after a fatal inference error.");
        }
        finally
        {
            try
            {
                applicationLifetime.StopApplication();
            }
            catch (Exception shutdownException)
            {
                logger.LogCritical(shutdownException, "Host shutdown could not be requested after a fatal inference error.");
            }
        }
    }
}

internal sealed class NoOpInferenceFailureCoordinator : IInferenceFailureCoordinator
{
    public static NoOpInferenceFailureCoordinator Instance { get; } = new();

    public Task FailAsync(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Task.CompletedTask;
    }
}