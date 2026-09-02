using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Esi.AI.Studio;

/// <summary>Establishes the Linux process boundary used by the Studio watchdog.</summary>
internal static class StudioProcessIsolation
{
    private const int ParentDeathSignalOption = 1;
    private const int TerminateSignal = 15;

    /// <summary>Checks the watchdog PID and kernel start-time token recorded in the state file.</summary>
    internal static bool IsWatchdogAlive(string pidFile)
    {
        try
        {
            var watchdogPid = ReadStateValue(pidFile, "watchdog_pid");
            var expectedStart = ReadStateValue(pidFile, "watchdog_start");
            if (!int.TryParse(watchdogPid, out var processId) || processId < 1 || string.IsNullOrWhiteSpace(expectedStart))
                return false;

            using var watchdog = Process.GetProcessById(processId);
            if (watchdog.HasExited || GetProcessStartTime(processId) != expectedStart)
                return false;

            return File.Exists($"/proc/{processId}/cmdline")
                && File.ReadAllText($"/proc/{processId}/cmdline").Contains("studio-watchdog.sh", StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Places Studio in its own process group and terminates it if its launcher disappears.</summary>
    internal static void Configure()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var processId = Environment.ProcessId;
        var currentProcessGroupId = GetProcessGroupId();
        if (currentProcessGroupId != processId && SetProcessGroup(0, 0) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Esi.AI Studio could not create its process group.");

        if (GetProcessGroupId() != processId)
            throw new InvalidOperationException("Esi.AI Studio did not enter its dedicated process group.");

        if (SetParentDeathSignal(ParentDeathSignalOption, TerminateSignal) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Esi.AI Studio could not configure its parent-death signal.");
    }

    private static string? ReadStateValue(string pidFile, string key)
    {
        if (!File.Exists(pidFile))
            return null;

        foreach (var line in File.ReadLines(pidFile))
        {
            var separator = line.IndexOf('=');
            if (separator > 0 && line[..separator].Equals(key, StringComparison.Ordinal))
                return line[(separator + 1)..];
        }

        return null;
    }

    private static string? GetProcessStartTime(int processId)
    {
        var stat = File.ReadAllText($"/proc/{processId}/stat");
        var commandEnd = stat.IndexOf(") ", StringComparison.Ordinal);
        if (commandEnd < 0)
            return null;

        var fields = stat[(commandEnd + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 19 ? fields[19] : null;
    }

    [DllImport("libc", EntryPoint = "getpgrp", SetLastError = true)]
    private static extern int GetProcessGroupId();

    [DllImport("libc", EntryPoint = "setpgid", SetLastError = true)]
    private static extern int SetProcessGroup(int processId, int processGroupId);

    [DllImport("libc", EntryPoint = "prctl", SetLastError = true)]
    private static extern int SetParentDeathSignal(int option, int argument);
}
