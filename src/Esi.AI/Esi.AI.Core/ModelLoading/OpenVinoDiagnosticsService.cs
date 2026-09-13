using System.Diagnostics;

namespace Esi.AI.Core.ModelLoading;

public sealed class OpenVinoDiagnosticsService
{
    private readonly OpenVinoLoadGate loadGate;
    private OpenVinoDiagnostics? cachedDiagnostics;

    public OpenVinoDiagnosticsService(OpenVinoLoadGate? loadGate = null)
    {
        this.loadGate = loadGate ?? new OpenVinoLoadGate();
    }

    public OpenVinoDiagnostics Diagnose()
    {
        if (loadGate.IsEntered)
        {
            return Volatile.Read(ref cachedDiagnostics) ?? new OpenVinoDiagnostics(
                false,
                false,
                [],
                [new OpenVinoDiagnosticCheck(
                    "openvino-load-in-progress",
                    "OpenVINO diagnostics",
                    false,
                    "Diagnostics are paused while an OpenVINO model load owns the accelerator.",
                    false)],
                null);
        }

        var checks = new List<OpenVinoDiagnosticCheck>();
        AddLinuxDriverChecks(checks);

        var hasRenderDevice = !OperatingSystem.IsLinux() ||
            Directory.Exists("/dev/dri") && Directory.EnumerateFileSystemEntries("/dev/dri", "renderD*").Any();
        var hasNpuDevice = OperatingSystem.IsLinux() &&
            Directory.Exists("/dev/accel") && Directory.EnumerateFileSystemEntries("/dev/accel", "accel*").Any();
        IReadOnlyList<OpenVinoDeviceStatus> gpuDevices = hasRenderDevice
            ? new[] { new OpenVinoDeviceStatus(
                "GPU",
                "OpenVINO GPU (native probe deferred until load)",
                true,
                "Intel",
                "OS render device",
                "GPU route is available from OS device checks; native OpenVINO probing is deferred until model load.") }
            : Array.Empty<OpenVinoDeviceStatus>();
        var devices = hasNpuDevice
            ? gpuDevices.Append(new OpenVinoDeviceStatus(
                "NPU",
                "OpenVINO NPU (native probe deferred until load)",
                true,
                "Intel",
                "OS accelerator device",
                "NPU route is available from OS device checks; native OpenVINO probing is deferred until model load.")).ToArray()
            : gpuDevices;
        checks.Add(new OpenVinoDiagnosticCheck(
            "openvino-gpu-plugin",
            "OpenVINO GPU route",
            hasRenderDevice,
            hasRenderDevice
                ? "A render device is available. Native OpenVINO probing is deferred until model load."
                : "No DRM render device was found under /dev/dri.",
            false));
        checks.Add(new OpenVinoDiagnosticCheck(
            "openvino-npu-plugin",
            "OpenVINO NPU route",
            hasNpuDevice,
            hasNpuDevice
                ? "An accelerator device is available. Native OpenVINO probing is deferred until model load."
                : "No accelerator device was found under /dev/accel.",
            false));

        return Cache(new OpenVinoDiagnostics(hasRenderDevice, hasNpuDevice, devices, checks, null));
    }

    private OpenVinoDiagnostics Cache(OpenVinoDiagnostics diagnostics)
    {
        Volatile.Write(ref cachedDiagnostics, diagnostics);
        return diagnostics;
    }

    private static void AddLinuxDriverChecks(ICollection<OpenVinoDiagnosticCheck> checks)
    {
        if (!OperatingSystem.IsLinux())
            return;

        var gpuInfo = RunCommand("lspci", "-nnk");
        var intelGpuLines = gpuInfo.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => (line.Contains("VGA compatible controller", StringComparison.OrdinalIgnoreCase)
                || line.Contains("3D controller", StringComparison.OrdinalIgnoreCase))
                && line.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var hasIntelGpu = intelGpuLines.Length > 0;
        var hasBattlemageGpu = intelGpuLines.Any(line => line.Contains("Battlemage", StringComparison.OrdinalIgnoreCase)
            || line.Contains("[8086:e223]", StringComparison.OrdinalIgnoreCase));
        checks.Add(new OpenVinoDiagnosticCheck(
            "intel-gpu-hardware",
            "Intel discrete GPU hardware",
            hasIntelGpu,
            hasBattlemageGpu
                ? "Intel Battlemage GPU found by lspci (8086:e223)."
                : hasIntelGpu
                    ? $"Intel GPU found: {string.Join("; ", intelGpuLines)}"
                        : "No Intel GPU found by lspci.",
                    false));

        var kernelDriver = hasBattlemageGpu
            ? HasDriverForDevice(gpuInfo.Output, "Battlemage", "xe")
            : gpuInfo.Output.Contains("Kernel driver in use: xe", StringComparison.OrdinalIgnoreCase);
        checks.Add(new OpenVinoDiagnosticCheck(
            "intel-kernel-driver",
            "Intel xe kernel driver",
            kernelDriver,
            kernelDriver ? "xe is active for the Intel discrete GPU." : "The xe driver is not active for the Intel discrete GPU.",
            false));

        var renderDevice = Directory.Exists("/dev/dri") && Directory.EnumerateFileSystemEntries("/dev/dri", "renderD*").Any();
        checks.Add(new OpenVinoDiagnosticCheck(
            "drm-render-device",
            "DRM render device",
            renderDevice,
            renderDevice ? "/dev/dri/renderD* is available." : "No DRM render device found under /dev/dri.",
            false));

        var levelZero = RunCommand("ldconfig", "-p");
        var hasLevelZero = levelZero.Success && levelZero.Output.Contains("libze_loader.so", StringComparison.OrdinalIgnoreCase);
        checks.Add(new OpenVinoDiagnosticCheck(
            "level-zero-loader",
            "Level Zero loader",
            hasLevelZero,
            hasLevelZero ? "libze_loader.so is installed." : "libze_loader.so was not found in ldconfig.",
            true));

        var hasIntelLevelZero = levelZero.Success && (levelZero.Output.Contains("libze_intel_gpu.so", StringComparison.OrdinalIgnoreCase)
            || levelZero.Output.Contains("libze_intel_gpu", StringComparison.OrdinalIgnoreCase));
        checks.Add(new OpenVinoDiagnosticCheck(
            "intel-level-zero-gpu",
            "Intel Level Zero GPU driver",
            hasIntelLevelZero,
                hasIntelLevelZero ? "Intel GPU Level Zero plugin is installed." : "Intel GPU Level Zero plugin was not found in the configured system runtime.",
                true));

        var renderAccess = RunCommand("id", "-nG");
        var hasRenderAccess = renderDevice && (renderAccess.Output.Contains("render", StringComparison.OrdinalIgnoreCase)
            || renderAccess.Output.Contains("video", StringComparison.OrdinalIgnoreCase));
        checks.Add(new OpenVinoDiagnosticCheck(
            "render-permissions",
            "Render device permissions",
            hasRenderAccess,
            hasRenderAccess ? "Current user can access the render/video device group." : "Current user is not in the render/video group.",
            true));
    }

    private static bool HasDriverForDevice(string output, string deviceMarker, string driver)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var deviceIndex = Array.FindIndex(lines, line => line.Contains(deviceMarker, StringComparison.OrdinalIgnoreCase));
        if (deviceIndex < 0)
            return false;

        var deviceBlock = lines.Skip(deviceIndex + 1)
            .TakeWhile(line => line.StartsWith(' ') || line.StartsWith('\t'));
        return deviceBlock.Any(line => line.Contains($"Kernel driver in use: {driver}", StringComparison.OrdinalIgnoreCase));
    }

    private static CommandResult RunCommand(string command, string arguments)
    {
        if (FindExecutable(command) is null)
            return new(false, string.Empty);

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
                return new(false, string.Empty);

            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new(process.ExitCode == 0, output);
        }
        catch (Exception)
        {
            return new(false, string.Empty);
        }
    }

    private static string? FindExecutable(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return path.Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, command))
            .FirstOrDefault(IsExecutable);
    }

    private static bool IsExecutable(string path)
    {
        if (!File.Exists(path))
            return false;

        return !OperatingSystem.IsLinux()
            || (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }

    private readonly record struct CommandResult(bool Success, string Output);
}

public sealed record OpenVinoDiagnostics(
    bool IsGpuReady,
    bool IsNpuReady,
    IReadOnlyList<OpenVinoDeviceStatus> Devices,
    IReadOnlyList<OpenVinoDiagnosticCheck> Checks,
    string? Error);

public sealed record OpenVinoDeviceStatus(
    string Id,
    string Name,
    bool IsCompatible,
    string Vendor,
    string Driver,
    string Detail);

public sealed record OpenVinoDiagnosticCheck(
    string Id,
    string Name,
    bool IsAvailable,
    string Detail,
    bool CanSolve);
