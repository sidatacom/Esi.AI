using System.Text.Json;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var input = await Console.In.ReadLineAsync();
if (string.IsNullOrWhiteSpace(input))
{
    await Console.Error.WriteLineAsync("The backend worker received no request.");
    return 2;
}

try
{
    var request = JsonSerializer.Deserialize<BackendWorkerRequest>(input, jsonOptions)
        ?? throw new InvalidOperationException("The backend worker request was empty.");
    var response = await ExecuteAsync(request);
    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, jsonOptions));
    return response.Succeeded ? 0 : 1;
}
catch (Exception exception)
{
    var response = new BackendWorkerResponse(false, Error: exception.ToString());
    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, jsonOptions));
    return 1;
}

static async Task<BackendWorkerResponse> ExecuteAsync(BackendWorkerRequest request)
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
        "diagnose-openvino" => new BackendWorkerResponse(
            true,
            OpenVino: MapOpenVino(new OpenVinoDiagnosticsService().Diagnose())),
        _ => throw new ArgumentException($"Unsupported backend worker operation '{request.Operation}'.", nameof(request))
    };
}

static OpenVinoDiagnosticsDto MapOpenVino(OpenVinoDiagnostics result) => new()
{
    IsGpuReady = result.IsGpuReady,
    IsNpuReady = result.IsNpuReady,
    Devices = result.Devices.Select(device => new OpenVinoDeviceDto
    {
        Id = device.Id,
        Name = device.Name,
        IsCompatible = device.IsCompatible,
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
