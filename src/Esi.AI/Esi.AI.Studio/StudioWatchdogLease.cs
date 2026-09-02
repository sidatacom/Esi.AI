using Microsoft.Extensions.Hosting;

namespace Esi.AI.Studio;

/// <summary>Stops Studio when its external process supervisor is no longer valid.</summary>
internal sealed class StudioWatchdogLease(IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    /// <summary>Checks the supervisor lease periodically until Studio shuts down.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pidFile = Environment.GetEnvironmentVariable("ESI_AI_STUDIO_WATCHDOG_PID_FILE");
        if (string.IsNullOrWhiteSpace(pidFile))
        {
            applicationLifetime.StopApplication();
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                if (!StudioProcessIsolation.IsWatchdogAlive(pidFile))
                {
                    applicationLifetime.StopApplication();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
