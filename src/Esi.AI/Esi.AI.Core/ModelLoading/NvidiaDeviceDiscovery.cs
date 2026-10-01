using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Esi.AI.Models;

namespace Esi.AI.Core.ModelLoading;

/// <summary>Discovers NVIDIA devices through the installed driver tooling.</summary>
public static class NvidiaDeviceDiscovery
{
    /// <summary>Queries visible NVIDIA devices without depending on a backend-specific native runtime.</summary>
    public static async Task<IReadOnlyList<DeviceStatus>> DiscoverAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "The NVIDIA device discovery timeout must be positive.");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        try
        {
            process.StartInfo.ArgumentList.Add("--query-gpu=index,name,driver_version");
            process.StartInfo.ArgumentList.Add("--format=csv,noheader,nounits");
            if (!process.Start())
                throw new InvalidOperationException("The NVIDIA device query could not be started.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("NVIDIA device discovery requires nvidia-smi from an installed NVIDIA driver.", exception);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
                throw;

            throw new TimeoutException($"NVIDIA device discovery exceeded its {timeout.TotalSeconds:0.##}-second limit.");
        }

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"nvidia-smi failed with exit code {process.ExitCode}: {error.Trim()}");

        return ParseOutput(output);
    }

    internal static IReadOnlyList<DeviceStatus> ParseOutput(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var devices = new List<DeviceStatus>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split(',', 3, StringSplitOptions.TrimEntries);
            if (fields.Length != 3 ||
                !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                index < 0 ||
                string.IsNullOrWhiteSpace(fields[1]) ||
                string.IsNullOrWhiteSpace(fields[2]))
            {
                throw new FormatException($"nvidia-smi returned an invalid device row: '{line}'.");
            }

            devices.Add(new DeviceStatus(
                $"cuda:{index}",
                fields[1],
                0,
                null,
                "NVIDIA",
                $"NVIDIA Driver {fields[2]}"));
        }

        return devices;
    }
}