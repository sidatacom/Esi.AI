using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;

namespace Esi.AI.Studio.Services;

/// <summary>Application-facing diagnostics and repair operations for backend requirements.</summary>
public interface IBackendDiagnosticsService
{
    OpenVinoDiagnosticsDto GetOpenVinoDiagnostics();

    Task<OpenVinoSolveResultDto> SolveOpenVinoDiagnosticAsync(string checkId, CancellationToken cancellationToken = default);
}

/// <summary>Maps backend diagnostic infrastructure into transport-independent application DTOs.</summary>
public sealed class BackendDiagnosticsService(
    OpenVinoDiagnosticsService openVinoDiagnostics,
    OpenVinoDriverInstaller openVinoInstaller) : IBackendDiagnosticsService
{
    /// <inheritdoc />
    public OpenVinoDiagnosticsDto GetOpenVinoDiagnostics()
    {
        var result = openVinoDiagnostics.Diagnose();
        return new OpenVinoDiagnosticsDto
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
                Name = check.Name,
                Id = check.Id,
                IsAvailable = check.IsAvailable,
                Detail = check.Detail,
                CanSolve = check.CanSolve
            }).ToArray(),
            Error = result.Error
        };
    }

    /// <inheritdoc />
    public async Task<OpenVinoSolveResultDto> SolveOpenVinoDiagnosticAsync(string checkId, CancellationToken cancellationToken = default)
    {
        var result = checkId switch
        {
            "level-zero-loader" or "intel-level-zero-gpu" => await openVinoInstaller.InstallAsync(cancellationToken).ConfigureAwait(false),
            "render-permissions" => await openVinoInstaller.AddUserToRenderGroupsAsync(cancellationToken).ConfigureAwait(false),
            _ => new OpenVinoInstallResult(false, "This diagnostic cannot be repaired automatically.", string.Empty)
        };
        return new OpenVinoSolveResultDto
        {
            Succeeded = result.Succeeded,
            Message = result.Message,
            Output = string.IsNullOrWhiteSpace(result.Output)
                ? "No installer output was returned by the server."
                : result.Output
        };
    }
}
