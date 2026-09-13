using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;

namespace Esi.AI.Studio.Services;

/// <summary>Application-facing diagnostics and repair operations for backend requirements.</summary>
public interface IBackendDiagnosticsService
{
    Task<OpenVinoDiagnosticsDto> GetOpenVinoDiagnosticsAsync(CancellationToken cancellationToken = default);

    Task<OpenVinoSolveResultDto> SolveOpenVinoDiagnosticAsync(string checkId, CancellationToken cancellationToken = default);
}

/// <summary>Maps backend diagnostic infrastructure into transport-independent application DTOs.</summary>
public sealed class BackendDiagnosticsService(
    BackendSandboxBroker sandbox,
    OpenVinoDriverInstaller openVinoInstaller) : IBackendDiagnosticsService
{
    /// <inheritdoc />
    public Task<OpenVinoDiagnosticsDto> GetOpenVinoDiagnosticsAsync(CancellationToken cancellationToken = default) =>
        sandbox.DiagnoseOpenVinoAsync(cancellationToken);

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
