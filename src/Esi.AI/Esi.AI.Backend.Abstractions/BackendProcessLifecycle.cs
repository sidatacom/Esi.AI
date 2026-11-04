using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Esi.AI.Backend.Abstractions;

/// <summary>Starts and stops backend processes with an explicit host-lifetime signal.</summary>
public static class BackendProcessLifecycle
{
    private const string ParentLifetimePipeEnvironmentVariable = "ESI_PARENT_LIFETIME_PIPE";
    private const int Sigterm = 15;
    private const int Sigkill = 9;
    private const int NoSuchProcess = 3;
    private static readonly TimeSpan GracefulShutdownTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SignalShutdownTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ForcedShutdownTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Configures the parent-lifetime pipe and, on Linux, an isolated session for a backend process.</summary>
    /// <param name="startInfo">The process configuration to update.</param>
    /// <exception cref="ArgumentNullException">The start info is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">The Linux <c>setsid</c> executable is unavailable.</exception>
    public static void ConfigureStartInfo(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (startInfo.RedirectStandardInput)
            startInfo.Environment[ParentLifetimePipeEnvironmentVariable] = "1";
        if (!OperatingSystem.IsLinux())
            return;

        ArgumentException.ThrowIfNullOrWhiteSpace(startInfo.FileName);
        var executable = startInfo.FileName;
        var arguments = startInfo.ArgumentList.ToArray();
        startInfo.FileName = ResolveSetSidExecutable();
        startInfo.ArgumentList.Clear();
        startInfo.ArgumentList.Add("--wait");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(executable);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
    }

    /// <summary>Waits until the configured parent-lifetime pipe reaches EOF.</summary>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    public static async Task WaitForParentShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(ParentLifetimePipeEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return;
        }

        await WaitForParentPipeClosureAsync(Console.In, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Waits until the supplied lifetime stream reaches EOF.</summary>
    /// <param name="lifetimeStream">The stream owned by the parent process.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <exception cref="ArgumentNullException">The lifetime stream is <see langword="null" />.</exception>
    public static Task WaitForParentPipeClosureAsync(TextReader lifetimeStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lifetimeStream);
        return Task.Factory.StartNew(
            () => lifetimeStream.ReadToEndAsync(cancellationToken),
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }

    /// <summary>Gracefully stops a backend process and escalates to terminating its owned group if needed.</summary>
    /// <param name="process">The process previously configured with <see cref="ConfigureStartInfo(ProcessStartInfo)" />.</param>
    /// <exception cref="ArgumentNullException">The process is <see langword="null" />.</exception>
    /// <exception cref="TimeoutException">The process group remains alive after forced termination.</exception>
    public static async Task StopAsync(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (!OperatingSystem.IsLinux())
        {
            if (!process.HasExited && process.StartInfo.RedirectStandardInput)
            {
                process.StandardInput.Close();
                using var timeout = new CancellationTokenSource(GracefulShutdownTimeout);
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                }
            }

            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            return;
        }

        var processGroupId = process.Id;
        if (process.HasExited && !HasLiveProcessInGroup(processGroupId))
            return;
        if (!process.HasExited && GetProcessGroupId(process.Id) != processGroupId)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            return;
        }

        if (!process.HasExited && process.StartInfo.RedirectStandardInput)
        {
            process.StandardInput.Close();
            if (await WaitForProcessGroupExitAsync(processGroupId, GracefulShutdownTimeout).ConfigureAwait(false))
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            }
        }

        SignalProcessGroup(processGroupId, Sigterm);
        if (!await WaitForProcessGroupExitAsync(processGroupId, SignalShutdownTimeout).ConfigureAwait(false))
        {
            SignalProcessGroup(processGroupId, Sigkill);
            if (!await WaitForProcessGroupExitAsync(processGroupId, ForcedShutdownTimeout).ConfigureAwait(false))
                throw new TimeoutException($"Backend process group {processGroupId} remained alive after forced termination.");
        }

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static string ResolveSetSidExecutable()
    {
        var candidates = new List<string> { "/usr/bin/setsid", "/bin/setsid" };
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(directory => Path.Combine(directory, "setsid")));
        }

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException("Could not start a managed backend process group because the Linux 'setsid' executable was not found.");
    }

    private static int GetProcessGroupId(int processId)
    {
        var stat = File.ReadAllText($"/proc/{processId}/stat");
        var closingParenthesis = stat.LastIndexOf(')');
        if (closingParenthesis < 0)
            throw new InvalidOperationException($"Could not read process group information for backend process {processId}.");

        var fields = stat[(closingParenthesis + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 3 ||
            !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var processGroupId))
            throw new InvalidOperationException($"Could not read process group information for backend process {processId}.");

        return processGroupId;
    }

    private static void SignalProcessGroup(int processGroupId, int signal)
    {
        if (Kill(-processGroupId, signal) == 0)
            return;

        var error = Marshal.GetLastPInvokeError();
        if (error == NoSuchProcess)
            return;

        throw new Win32Exception(error, $"Could not send signal {signal} to backend process group {processGroupId}.");
    }

    private static async Task<bool> WaitForProcessGroupExitAsync(int processGroupId, TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            if (!HasLiveProcessInGroup(processGroupId))
                return true;
            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
        }

        return !HasLiveProcessInGroup(processGroupId);
    }

    private static bool HasLiveProcessInGroup(int processGroupId)
    {
        foreach (var processDirectory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(processDirectory), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                continue;

            string stat;
            try
            {
                stat = File.ReadAllText(Path.Combine(processDirectory, "stat"));
            }
            catch (FileNotFoundException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            var closingParenthesis = stat.LastIndexOf(')');
            if (closingParenthesis < 0)
                continue;
            var fields = stat[(closingParenthesis + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 3 ||
                fields[0] is "Z" or "X" ||
                !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var memberGroupId))
                continue;
            if (memberGroupId == processGroupId)
                return true;
        }

        return false;
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int processId, int signal);
}
