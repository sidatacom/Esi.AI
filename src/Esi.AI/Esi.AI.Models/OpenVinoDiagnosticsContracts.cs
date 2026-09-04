namespace Esi.AI.Models;

/// <summary>
/// Describes the current OpenVINO device and prerequisite state.
/// </summary>
public sealed class OpenVinoDiagnosticsDto
{
    public bool IsGpuReady { get; set; }
    public bool IsNpuReady { get; set; }
    public IReadOnlyList<OpenVinoDeviceDto> Devices { get; set; } = [];
    public IReadOnlyList<OpenVinoDiagnosticCheckDto> Checks { get; set; } = [];
    public string? Error { get; set; }
}

/// <summary>
/// Describes an OpenVINO device discovered by the diagnostics service.
/// </summary>
public sealed class OpenVinoDeviceDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsCompatible { get; set; }
    public string Vendor { get; set; } = string.Empty;
    public string Driver { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

/// <summary>
/// Describes one repairable or informational OpenVINO diagnostic check.
/// </summary>
public sealed class OpenVinoDiagnosticCheckDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public string Detail { get; set; } = string.Empty;
    public bool CanSolve { get; set; }
}

/// <summary>
/// Contains the result of an OpenVINO prerequisite repair operation.
/// </summary>
public sealed class OpenVinoSolveResultDto
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
}
