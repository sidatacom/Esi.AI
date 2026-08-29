using Esi.AI.Models;

namespace Esi.AI.Core.ModelLoading;

/// <summary>Applies the common prerequisite workflow to every supported backend.</summary>
public sealed class BackendPrerequisiteProvisioner
{
    private readonly PythonBackendProvisioner pythonProvisioner;

    /// <summary>Creates a prerequisite provisioner for native and Python backends.</summary>
    public BackendPrerequisiteProvisioner(PythonBackendProvisioner? pythonProvisioner = null)
    {
        this.pythonProvisioner = pythonProvisioner ?? new PythonBackendProvisioner();
    }

    /// <summary>Prepares the selected backend and returns the runtime details for its loader.</summary>
    public async Task<BackendPreparationResult> PrepareAsync(
        ConfigurationBackend backend,
        string requestedPythonExecutable = "python3",
        string? applicationDirectory = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (backend is ConfigurationBackend.Vllm or ConfigurationBackend.Sglang)
        {
            var preparation = await pythonProvisioner.PrepareAsync(
                backend,
                requestedPythonExecutable,
                applicationDirectory ?? AppContext.BaseDirectory,
                timeout ?? TimeSpan.FromMinutes(10),
                cancellationToken).ConfigureAwait(false);
            return new(backend, preparation.PythonExecutable, preparation.RequirementsPath, preparation.EnvironmentCreated, preparation.Message);
        }

        var message = backend switch
        {
            ConfigurationBackend.Llama => "LLama native runtime is bundled with the application.",
            ConfigurationBackend.OpenVino => "OpenVINO native runtime is bundled with the application.",
            ConfigurationBackend.DotLlm => "dotLLM native runtime is bundled with the application.",
            _ => throw new ArgumentException("The selected backend is not supported.", nameof(backend))
        };
        return new(backend, null, null, false, message);
    }

    /// <summary>Diagnoses backend requirements without mutating the host or installing packages.</summary>
    public async Task<BackendPrerequisiteDiagnostics> DiagnoseAsync(
        ConfigurationBackend backend,
        string requestedPythonExecutable = "python3",
        string? applicationDirectory = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (backend is ConfigurationBackend.Vllm or ConfigurationBackend.Sglang)
            return await pythonProvisioner.DiagnoseAsync(backend, requestedPythonExecutable, applicationDirectory ?? AppContext.BaseDirectory, timeout ?? TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);

        var name = backend switch
        {
            ConfigurationBackend.Llama => "LLama",
            ConfigurationBackend.OpenVino => "OpenVINO",
            ConfigurationBackend.DotLlm => "dotLLM",
            _ => throw new ArgumentException("The selected backend is not supported.", nameof(backend))
        };
        return new(backend, name, true,
            [new("bundled-runtime", $"{name} runtime", true, $"{name} runtime is bundled with the application.", false)]);
    }
}

/// <summary>Contains the prepared runtime details shared by all backend loaders.</summary>
public sealed record BackendPreparationResult(
    ConfigurationBackend Backend,
    string? PythonExecutable,
    string? RequirementsPath,
    bool EnvironmentCreated,
    string Message);