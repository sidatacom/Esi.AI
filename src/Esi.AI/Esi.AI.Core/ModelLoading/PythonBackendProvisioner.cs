using System.Diagnostics;
using System.Text;
using Esi.AI.Models;

namespace Esi.AI.Core.ModelLoading;

/// <summary>Prepares isolated Python environments for the supported Python inference engines.</summary>
public sealed class PythonBackendProvisioner
{
    private static readonly SemaphoreSlim ProvisioningLock = new(1, 1);

    /// <summary>Ensures that the selected backend can be imported by the resolved Python executable.</summary>
    public async Task<PythonEnvironmentPreparation> PrepareAsync(
        ConfigurationBackend backend,
        string requestedPythonExecutable,
        string applicationDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (backend is not (ConfigurationBackend.Vllm or ConfigurationBackend.Sglang))
            throw new ArgumentException("Only vLLM and SGLang have Python environments.", nameof(backend));
        if (string.IsNullOrWhiteSpace(requestedPythonExecutable))
            throw new ArgumentException("A Python executable is required.", nameof(requestedPythonExecutable));
        if (string.IsNullOrWhiteSpace(applicationDirectory))
            throw new ArgumentException("The application directory is required.", nameof(applicationDirectory));
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "The Python environment timeout must be positive.");

        var definition = GetDefinition(backend);
        var requirementsPath = Path.Combine(applicationDirectory, "Python", definition.RequirementsFileName);
        if (!File.Exists(requirementsPath))
            throw new FileNotFoundException($"The {definition.DisplayName} requirements file was not deployed.", requirementsPath);

        var configuredExecutable = ResolveConfiguredExecutable(backend, requestedPythonExecutable);
        if (!IsAutomaticExecutable(requestedPythonExecutable))
        {
            await ValidateDependenciesAsync(configuredExecutable, definition, timeout, cancellationToken).ConfigureAwait(false);
            return new(configuredExecutable, requirementsPath, false,
                $"Using the explicitly configured Python executable for {definition.DisplayName}: {configuredExecutable}.");
        }

        var environmentPath = ResolveEnvironmentPath(definition.EnvironmentName);
        var environmentPython = GetEnvironmentPythonPath(environmentPath);
        var environmentCreated = false;
        using var preparationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        preparationTimeout.CancelAfter(timeout);
        await ProvisioningLock.WaitAsync(preparationTimeout.Token).ConfigureAwait(false);
        try
        {
            if (!File.Exists(environmentPython))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(environmentPath)!);
                var createResult = await RunProcessAsync(
                    configuredExecutable,
                    ["-m", "venv", environmentPath],
                    timeout,
                    preparationTimeout.Token).ConfigureAwait(false);
                EnsureProcessSucceeded(createResult, $"Creating the {definition.DisplayName} Python environment at '{environmentPath}'");
                environmentCreated = true;
            }

            if (!await HasDependenciesAsync(environmentPython, definition, timeout, preparationTimeout.Token).ConfigureAwait(false))
            {
                var installResult = await RunProcessAsync(
                    environmentPython,
                    ["-m", "pip", "install", "--disable-pip-version-check", "-r", requirementsPath],
                    timeout,
                    preparationTimeout.Token).ConfigureAwait(false);
                EnsureProcessSucceeded(installResult, $"Installing {definition.DisplayName} Python dependencies", installResult.Output);
                await ValidateDependenciesAsync(environmentPython, definition, timeout, preparationTimeout.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            ProvisioningLock.Release();
        }

        var action = environmentCreated ? "Created and prepared" : "Validated";
        return new(environmentPython, requirementsPath, environmentCreated,
            $"{action} the {definition.DisplayName} Python environment at '{environmentPath}'.");
    }

    /// <summary>Checks the Python runtime and backend dependencies without installing anything.</summary>
    public async Task<BackendPrerequisiteDiagnostics> DiagnoseAsync(
        ConfigurationBackend backend,
        string requestedPythonExecutable,
        string applicationDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var definition = GetDefinition(backend);
        var checks = new List<BackendPrerequisiteCheck>();
        var requirementsPath = Path.Combine(applicationDirectory, "Python", definition.RequirementsFileName);
        var requirementsAvailable = File.Exists(requirementsPath);
        checks.Add(new("requirements-file", "Backend requirements", requirementsAvailable,
            requirementsAvailable ? $"{definition.RequirementsFileName} is available." : $"{definition.RequirementsFileName} was not deployed.", false));

        var executable = IsAutomaticExecutable(requestedPythonExecutable)
            ? GetEnvironmentPythonPath(ResolveEnvironmentPath(definition.EnvironmentName))
            : requestedPythonExecutable;
        var pythonAvailable = File.Exists(executable) || (!Path.IsPathFullyQualified(executable) && await CanStartProcessAsync(executable, timeout, cancellationToken).ConfigureAwait(false));
        checks.Add(new("python-environment", "Python environment", pythonAvailable,
            pythonAvailable ? $"Using {executable}." : $"Python executable was not found: {executable}.", true));

        var dependenciesAvailable = false;
        if (pythonAvailable)
        {
            try
            {
                dependenciesAvailable = await HasDependenciesAsync(executable, definition, timeout, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                dependenciesAvailable = false;
            }
        }
        checks.Add(new("python-dependencies", "Engine and gRPC packages", dependenciesAvailable,
            dependenciesAvailable
                ? $"grpcio, protobuf and {definition.PackageName} can be imported."
                : $"grpcio, protobuf or {definition.PackageName} is missing from {executable}.", true));

        var cudaAvailable = false;
        var xpuAvailable = false;
        if (pythonAvailable)
        {
            cudaAvailable = await HasAcceleratorAsync(executable, "cuda", timeout, cancellationToken).ConfigureAwait(false);
            xpuAvailable = await HasAcceleratorAsync(executable, "xpu", timeout, cancellationToken).ConfigureAwait(false);
        }
        checks.Add(new("cuda-runtime", "CUDA accelerator", cudaAvailable,
            cudaAvailable ? "PyTorch reports a CUDA accelerator." : "PyTorch did not report an available CUDA accelerator.", false));
        checks.Add(new("xpu-runtime", "Intel XPU accelerator (optional)", xpuAvailable,
            xpuAvailable ? "PyTorch reports an Intel XPU accelerator." : "No Intel XPU accelerator is available; CUDA remains the active route.", false, true));

        var requiredChecks = checks.Where(check => check.Id != "xpu-runtime");
        return new(backend, definition.DisplayName, requiredChecks.All(check => check.IsAvailable), checks, null);
    }

    /// <summary>Returns the default isolated environment path for a backend.</summary>
    public static string GetDefaultEnvironmentPath(ConfigurationBackend backend)
    {
        var definition = GetDefinition(backend);
        return ResolveEnvironmentPath(definition.EnvironmentName);
    }

    private static async Task ValidateDependenciesAsync(
        string pythonExecutable,
        BackendDefinition definition,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!await HasDependenciesAsync(pythonExecutable, definition, timeout, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"The Python executable '{pythonExecutable}' does not provide grpcio, protobuf and {definition.PackageName}. " +
                "Use Python executable 'python3' for automatic environment preparation or install the backend requirements manually.");
        }
    }

    private static async Task<bool> HasDependenciesAsync(
        string pythonExecutable,
        BackendDefinition definition,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var importCheck = "import importlib.util,sys; sys.exit(0 if all(importlib.util.find_spec(name) is not None for name in ('grpc','google.protobuf','" + definition.PackageName + "')) else 1)";
        var result = await RunProcessAsync(pythonExecutable, ["-c", importCheck], timeout, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    private static async Task<bool> HasAcceleratorAsync(
        string pythonExecutable,
        string accelerator,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var expression = accelerator == "cuda"
            ? "import torch,sys; sys.exit(0 if torch.cuda.is_available() else 1)"
            : "import torch,sys; sys.exit(0 if hasattr(torch,'xpu') and torch.xpu.is_available() else 1)";
        try
        {
            var result = await RunProcessAsync(pythonExecutable, ["-c", expression], timeout, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> CanStartProcessAsync(string executable, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunProcessAsync(executable, ["--version"], timeout, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        var processStarted = false;
        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Could not start Python executable '{executable}'.");
            processStarted = true;

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(timeout);
            await process.WaitForExitAsync(timeoutCancellation.Token).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            return new(process.ExitCode, CombineOutput(output, error));
        }
        catch
        {
            if (processStarted && !process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private static void EnsureProcessSucceeded(ProcessResult result, string operation, string? additionalOutput = null)
    {
        if (result.ExitCode == 0)
            return;

        var output = string.IsNullOrWhiteSpace(additionalOutput) ? result.Output : additionalOutput;
        throw new InvalidOperationException($"{operation} failed with exit code {result.ExitCode}.{Environment.NewLine}{output}");
    }

    private static string ResolveConfiguredExecutable(ConfigurationBackend backend, string requestedExecutable)
    {
        if (!IsAutomaticExecutable(requestedExecutable))
            return requestedExecutable;

        var backendVariable = backend == ConfigurationBackend.Vllm
            ? "ESI_VLLM_PYTHON_EXECUTABLE"
            : "ESI_SGLANG_PYTHON_EXECUTABLE";
        var configuredExecutable = Environment.GetEnvironmentVariable(backendVariable);
        if (!string.IsNullOrWhiteSpace(configuredExecutable) && !IsAutomaticExecutable(configuredExecutable))
            return configuredExecutable;

        var legacyExecutable = Environment.GetEnvironmentVariable("ESI_PYTHON_REFERENCE_EXECUTABLE");
        return string.IsNullOrWhiteSpace(legacyExecutable) ? requestedExecutable : legacyExecutable;
    }

    private static bool IsAutomaticExecutable(string executable) =>
        string.Equals(executable, "python3", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(executable, "python", StringComparison.OrdinalIgnoreCase);

    private static string ResolveEnvironmentPath(string environmentName)
    {
        var root = Environment.GetEnvironmentVariable("ESI_PYTHON_ENV_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfile))
                userProfile = AppContext.BaseDirectory;
            root = Path.Combine(
                userProfile,
                ".venvs");
        }

        return Path.Combine(Path.GetFullPath(root), environmentName);
    }

    private static string GetEnvironmentPythonPath(string environmentPath) =>
        Path.Combine(environmentPath, OperatingSystem.IsWindows() ? "Scripts" : "bin", OperatingSystem.IsWindows() ? "python.exe" : "python");

    private static BackendDefinition GetDefinition(ConfigurationBackend backend) => backend switch
    {
        ConfigurationBackend.Vllm => new("vLLM", "vllm", "vllm-requirements.txt", "esi-ai-vllm"),
        ConfigurationBackend.Sglang => new("SGLang", "sglang", "sglang-requirements.txt", "esi-ai-sglang"),
        _ => throw new ArgumentException("Only vLLM and SGLang have Python environments.", nameof(backend))
    };

    private static string CombineOutput(string standardOutput, string standardError)
    {
        var output = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(standardOutput))
            output.AppendLine(standardOutput.Trim());
        if (!string.IsNullOrWhiteSpace(standardError))
            output.AppendLine(standardError.Trim());
        return output.ToString().Trim();
    }

    private sealed record BackendDefinition(string DisplayName, string PackageName, string RequirementsFileName, string EnvironmentName);
    private sealed record ProcessResult(int ExitCode, string Output);
}

/// <summary>Describes the Python executable and requirements used for a backend.</summary>
public sealed record PythonEnvironmentPreparation(
    string PythonExecutable,
    string RequirementsPath,
    bool EnvironmentCreated,
    string Message);