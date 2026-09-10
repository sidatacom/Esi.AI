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
    private static readonly TimeSpan RuntimeCleanupTimeout = TimeSpan.FromSeconds(5);
    private int shutdownStarted;

    /// <inheritdoc />
    public async Task FailAsync(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Interlocked.Exchange(ref shutdownStarted, 1) != 0)
            return;

        logger.LogCritical(exception, "Fatal inference failure; shutting down Studio and stopping model runtimes.");
        try
        {
            applicationLifetime.StopApplication();
        }
        catch (Exception shutdownException)
        {
            logger.LogCritical(shutdownException, "Host shutdown could not be requested after a fatal inference error.");
        }

        try
        {
            using var cleanupCancellation = new CancellationTokenSource(RuntimeCleanupTimeout);
            await modelRuntime.StopAsync(cleanupCancellation.Token).WaitAsync(RuntimeCleanupTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            logger.LogCritical("Model runtime cleanup exceeded {TimeoutSeconds} seconds after a fatal inference error.", RuntimeCleanupTimeout.TotalSeconds);
        }
        catch (OperationCanceledException) when (RuntimeCleanupTimeout > TimeSpan.Zero)
        {
            logger.LogCritical("Model runtime cleanup was cancelled after a fatal inference error.");
        }
        catch (Exception cleanupException)
        {
            logger.LogCritical(cleanupException, "Model runtime cleanup failed after a fatal inference error.");
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