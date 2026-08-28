using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenVinoSharp;

namespace Esi.AI.Core.ModelLoading;

public sealed class OpenVinoDiagnosticsService
{
    public OpenVinoDiagnostics Diagnose()
    {
        var checks = new List<OpenVinoDiagnosticCheck>();
        AddLinuxDriverChecks(checks);

        try
        {
            OpenVinoModelLoader.InitializeRuntime();
            using var core = new OpenVinoSharp.Core();
            var devices = core.GetAvailableDevices();
            var acceleratorDevices = devices
                .Where(device => IsOpenVinoGpuDevice(device) || IsOpenVinoNpuDevice(device))
                .Select(device =>
                {
                    var fullDeviceName = GetPropertyOrFallback(
                        core,
                        device,
                        "FULL_DEVICE_NAME",
                        device);
                    var deviceId = GetPropertyOrFallback(core, device, "DEVICE_ID", string.Empty);
                    var displayName = ResolveDeviceName(fullDeviceName, deviceId);
                    var isNpu = device.StartsWith("NPU", StringComparison.OrdinalIgnoreCase);
                    var isCompatible = isNpu || (displayName.Contains("Intel", StringComparison.OrdinalIgnoreCase)
                        && !displayName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));

                    return new OpenVinoDeviceStatus(
                        device,
                        displayName,
                        isCompatible,
                        isCompatible
                            ? $"OpenVINO {(isNpu ? "NPU" : "GPU")} device detected and compatible ({device})."
                            : $"OpenVINO GPU device detected but is not compatible with the Intel OpenVINO route ({device}).");
                    })
                .ToList();
            var gpuDevices = acceleratorDevices
                .Where(device => IsOpenVinoGpuDevice(device.Id))
                .ToList();
            var npuDevices = acceleratorDevices
                .Where(device => device.Id.StartsWith("NPU", StringComparison.OrdinalIgnoreCase))
                .ToList();

            checks.Add(new OpenVinoDiagnosticCheck(
                "openvino-gpu-plugin",
                "OpenVINO GPU plugin",
                gpuDevices.Any(device => device.IsCompatible),
                gpuDevices.Any(device => device.IsCompatible)
                    ? $"OpenVINO detected: {string.Join(", ", gpuDevices.Select(device => device.Name))}"
                    : $"OpenVINO did not detect a GPU device. Available devices: {FormatDeviceList(devices)}",
                false));

            checks.Add(new OpenVinoDiagnosticCheck(
                "openvino-npu-plugin",
                "OpenVINO NPU plugin",
                npuDevices.Any(device => device.IsCompatible),
                npuDevices.Any(device => device.IsCompatible)
                    ? $"OpenVINO detected: {string.Join(", ", npuDevices.Select(device => device.Name))}"
                    : $"OpenVINO did not detect an NPU device. Available devices: {FormatDeviceList(devices)}",
                false));

            return new OpenVinoDiagnostics(
                gpuDevices.Any(device => device.IsCompatible),
                npuDevices.Any(device => device.IsCompatible),
                acceleratorDevices,
                checks,
                null);
        }
        catch (Exception exception)
        {
            var detail = exception.ToString();
            checks.Add(new OpenVinoDiagnosticCheck("openvino-runtime", "OpenVINO runtime", false, detail, false));
            return new OpenVinoDiagnostics(false, false, [], checks, detail);
        }
    }

    private static string GetPropertyOrFallback(OpenVinoSharp.Core core, string device, string property, string fallback)
    {
        try
        {
            var value = core.GetProperty(device, property);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }

    private static string ResolveDeviceName(string fullDeviceName, string deviceId)
    {
        if (deviceId.Contains("e223", StringComparison.OrdinalIgnoreCase)
            || fullDeviceName.Contains("e223", StringComparison.OrdinalIgnoreCase))
            return "Intel(R) Arc(TM) Pro B70 Graphics (0xe223)";

        return fullDeviceName;
    }

    private static bool IsOpenVinoGpuDevice(string device) =>
        device.Equals("GPU", StringComparison.OrdinalIgnoreCase) ||
        device.StartsWith("GPU.", StringComparison.OrdinalIgnoreCase);

    private static bool IsOpenVinoNpuDevice(string device) =>
        device.Equals("NPU", StringComparison.OrdinalIgnoreCase) ||
        device.StartsWith("NPU.", StringComparison.OrdinalIgnoreCase);

    private static string FormatDeviceList(IReadOnlyList<string> devices) =>
        devices.Count == 0 ? "none" : string.Join(", ", devices);

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

    private readonly record struct CommandResult(bool Success, string Output);
}

public sealed record OpenVinoDiagnostics(
    bool IsGpuReady,
    bool IsNpuReady,
    IReadOnlyList<OpenVinoDeviceStatus> Devices,
    IReadOnlyList<OpenVinoDiagnosticCheck> Checks,
    string? Error);

public sealed record OpenVinoDeviceStatus(string Id, string Name, bool IsCompatible, string Detail);

public sealed record OpenVinoDiagnosticCheck(
    string Id,
    string Name,
    bool IsAvailable,
    string Detail,
    bool CanSolve);
