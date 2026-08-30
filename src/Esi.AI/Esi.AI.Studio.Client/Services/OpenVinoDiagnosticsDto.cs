namespace Esi.AI.Studio.Client.Services;

public sealed class OpenVinoDiagnosticsDto
{
    public bool IsGpuReady { get; set; }
    public bool IsNpuReady { get; set; }
    public IReadOnlyList<OpenVinoDeviceDto> Devices { get; set; } = [];
    public IReadOnlyList<OpenVinoDiagnosticCheckDto> Checks { get; set; } = [];
    public string? Error { get; set; }
}

public sealed class OpenVinoDeviceDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsCompatible { get; set; }
    public string Vendor { get; set; } = string.Empty;
    public string Driver { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public sealed class OpenVinoDiagnosticCheckDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public string Detail { get; set; } = string.Empty;
    public bool CanSolve { get; set; }
}

public sealed class OpenVinoSolveResultDto
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
}
