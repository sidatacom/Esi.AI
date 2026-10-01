using System.Text.Json;
using Esi.AI.Backend.Abstractions;
using Esi.AI.Backend.Llama.Cuda12;
using Esi.AI.Backend.Llama.Sycl;
using Esi.AI.Backend.Llama.Vulkan;
using Esi.AI.Backend.OpenVino;
using Esi.AI.Backend.Vllm.Cuda12;
using Esi.AI.Backend.Vllm.Xpu;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;

namespace Esi.AI.Backend.Worker;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> Main()
    {
        var input = await Console.In.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(input))
        {
            await Console.Error.WriteLineAsync("The backend worker received no request.");
            return 2;
        }

        try
        {
            var request = JsonSerializer.Deserialize<BackendWorkerRequest>(input, JsonOptions)
                ?? throw new InvalidOperationException("The backend worker request was empty.");
            var response = await ExecuteAsync(request);
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
            return response.Succeeded ? 0 : 1;
        }
        catch (Exception exception)
        {
            var response = new BackendWorkerResponse(false, Error: exception.ToString());
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
            return 1;
        }
    }

    private static async Task<BackendWorkerResponse> ExecuteAsync(BackendWorkerRequest request)
    {
        var applicationDirectory = string.IsNullOrWhiteSpace(request.ApplicationDirectory)
            ? AppContext.BaseDirectory
            : request.ApplicationDirectory;
        var timeout = TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 1, 300));
        var provisioner = new BackendPrerequisiteProvisioner();

        return request.Operation switch
        {
            "diagnose-requirements" => new BackendWorkerResponse(
                true,
                Prerequisites: await provisioner.DiagnoseAsync(
                    request.Backend,
                    request.PythonExecutable,
                    applicationDirectory,
                    timeout,
                    devices: request.Devices)),
            "discover-devices" => await DiscoverDevicesAsync(request, applicationDirectory, timeout),
            "diagnose-openvino" => new BackendWorkerResponse(
                true,
                OpenVino: MapOpenVino(DiagnoseOpenVino())),
            _ => throw new ArgumentException($"Unsupported backend worker operation '{request.Operation}'.", nameof(request))
        };
    }

    private static OpenVinoDiagnostics DiagnoseOpenVino()
    {
        var originalOutput = Console.Out;
        try
        {
            Console.SetOut(Console.Error);
            return new OpenVinoDiagnosticsService().Diagnose(OpenVinoRuntime.GetGpuDeviceName);
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
    }

    private static async Task<BackendWorkerResponse> DiscoverDevicesAsync(
        BackendWorkerRequest request,
        string applicationDirectory,
        TimeSpan timeout)
    {
        var variantId = request.BackendVariantId?.Trim().ToLowerInvariant()
            ?? throw new ArgumentException("A backend variant ID is required for device discovery.", nameof(request));
        if (variantId == "openvino")
        {
            return new BackendWorkerResponse(true, Devices: DiagnoseOpenVino().Devices
                .Where(device => device.IsCompatible)
                .Select(device => new BackendAcceleratorDevice(device.Id, device.Name, device.Vendor, device.Driver))
                .ToArray());
        }

        if (variantId is "vllm.cuda12" or "vllm.xpu")
        {
            if (variantId == "vllm.cuda12")
                return new BackendWorkerResponse(true, Devices: (await NvidiaDeviceDiscovery.DiscoverAsync(timeout).ConfigureAwait(false))
                    .Select(device => new BackendAcceleratorDevice(
                        device.DeviceId,
                        device.DeviceCaption ?? device.DeviceId,
                        device.Vendor ?? string.Empty,
                        device.Driver ?? string.Empty))
                    .ToArray());

            const string devicePrefix = "xpu:";
            using IBackendRuntime nativeRuntime = new LlamaSyclRuntime(applicationDirectory);
            return new BackendWorkerResponse(true, Devices: nativeRuntime.DiscoverDevices()
                .Select((device, index) => new BackendAcceleratorDevice(
                    $"{devicePrefix}{index}",
                    device.DeviceCaption ?? device.DeviceId,
                    device.Vendor ?? string.Empty,
                    device.Driver ?? string.Empty))
                .ToArray());
        }

        if (variantId == "sglang")
        {
            var diagnostics = await new BackendPrerequisiteProvisioner().DiagnoseAsync(
                request.Backend,
                request.PythonExecutable,
                applicationDirectory,
                timeout,
                devices: request.Devices);
            return new BackendWorkerResponse(true, Devices: diagnostics.AvailableDevices ?? []);
        }

        if (variantId == "dotllm.cpu")
            return new BackendWorkerResponse(true, Devices: []);

        IBackendRuntime? runtime = variantId switch
        {
            "llama.vulkan" => new LlamaVulkanRuntime(applicationDirectory),
            "llama.cuda12" => new LlamaCuda12Runtime(applicationDirectory),
            "llama.sycl" => new LlamaSyclRuntime(applicationDirectory),
            _ => null
        };
        if (runtime is null)
            throw new ArgumentException($"No device discovery is registered for backend variant '{variantId}'.", nameof(request));

        using (runtime)
        {
            var devices = runtime.DiscoverDevices()
                .Select(device => new BackendAcceleratorDevice(
                    device.DeviceId,
                    device.DeviceCaption ?? device.DeviceId,
                    device.Vendor ?? string.Empty,
                    device.Driver ?? string.Empty))
                .ToArray();
            return new BackendWorkerResponse(true, Devices: devices, LoadLog: runtime.GetStatus().LoadLog);
        }
    }

    private static OpenVinoDiagnosticsDto MapOpenVino(OpenVinoDiagnostics result) => new()
    {
        IsGpuReady = result.IsGpuReady,
        IsNpuReady = result.IsNpuReady,
        Devices = result.Devices.Select(device => new OpenVinoDeviceDto
        {
            Id = device.Id,
            Name = device.Name,
            IsCompatible = device.IsCompatible,
            Vendor = device.Vendor,
            Driver = device.Driver,
            Detail = device.Detail
        }).ToArray(),
        Checks = result.Checks.Select(check => new OpenVinoDiagnosticCheckDto
        {
            Id = check.Id,
            Name = check.Name,
            IsAvailable = check.IsAvailable,
            Detail = check.Detail,
            CanSolve = check.CanSolve
        }).ToArray(),
        Error = result.Error
    };
}