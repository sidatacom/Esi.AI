using System.Diagnostics;
using System.Text.Json;
using Esi.AI.Models;

namespace Esi.AI.Backend.Vllm.Xpu;

internal sealed class VllmXpuPythonEnvironment
{
    private static readonly SemaphoreSlim ProvisioningLock = new(1, 1);

    internal async Task<PythonEnvironmentPreparation> PrepareAsync(
        string requestedPythonExecutable,
        string packageDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPythonExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var requirementsPath = Path.Combine(packageDirectory, "Python", "vllm-xpu-requirements.txt");
        if (!File.Exists(requirementsPath))
            throw new FileNotFoundException("The vLLM XPU requirements file was not deployed.", requirementsPath);

        var configuredExecutable = ResolveConfiguredExecutable(requestedPythonExecutable);
        if (!IsAutomaticExecutable(requestedPythonExecutable))
        {
            await ValidateDependenciesAsync(configuredExecutable, packageDirectory, timeout, cancellationToken).ConfigureAwait(false);
            return new(configuredExecutable, requirementsPath, false,
                $"Using the explicitly configured Python executable for vLLM Intel XPU: {configuredExecutable}.");
        }

        var environmentPath = GetDefaultEnvironmentPath();
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
                EnsureProcessSucceeded(createResult, $"Creating the vLLM Intel XPU Python environment at '{environmentPath}'");
                environmentCreated = true;
            }

            if (!await HasDependenciesAsync(environmentPython, packageDirectory, timeout, preparationTimeout.Token).ConfigureAwait(false))
            {
                var installResult = await RunProcessAsync(
                    environmentPython,
                    ["-m", "pip", "install", "--disable-pip-version-check", "-r", requirementsPath],
                    timeout,
                    preparationTimeout.Token).ConfigureAwait(false);
                EnsureProcessSucceeded(installResult, "Installing vLLM Intel XPU Python dependencies");
                await ValidateDependenciesAsync(environmentPython, packageDirectory, timeout, preparationTimeout.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            ProvisioningLock.Release();
        }

        var action = environmentCreated ? "Created and prepared" : "Validated";
        return new(environmentPython, requirementsPath, environmentCreated,
            $"{action} the vLLM Intel XPU Python environment at '{environmentPath}'.");
    }

    internal static string GetDefaultEnvironmentPath()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("ESI_PYTHON_ENV_ROOT");
        var root = !string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredRoot.Trim()))
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".venvs");
        return Path.Combine(root, "esi-ai-vllm-xpu");
    }

    internal static string GetEnvironmentPythonPath(string environmentPath) =>
        Path.Combine(environmentPath, OperatingSystem.IsWindows() ? "Scripts" : "bin", OperatingSystem.IsWindows() ? "python.exe" : "python");

    private static async Task ValidateDependenciesAsync(
        string pythonExecutable,
        string packageDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var pythonDirectory = Path.Combine(packageDirectory, "Python");
        var check = $"import sys; sys.path.insert(0, {JsonSerializer.Serialize(pythonDirectory)}); " +
            "import vllm_xpu_bootstrap; vllm_xpu_bootstrap.disable_cuda_platform_probe(); " +
            "import importlib; [importlib.import_module(name) for name in ('grpc', 'google.protobuf', 'vllm', 'vllm_xpu_kernels')]";
        var result = await RunProcessAsync(pythonExecutable, ["-c", check], timeout, cancellationToken).ConfigureAwait(false);
        EnsureProcessSucceeded(result,
            $"The Python executable '{pythonExecutable}' does not provide grpcio, protobuf, vLLM and vLLM XPU kernels");
    }

    private static async Task<bool> HasDependenciesAsync(
        string pythonExecutable,
        string packageDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            await ValidateDependenciesAsync(pythonExecutable, packageDirectory, timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (InvalidOperationException)
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
        if (Path.IsPathFullyQualified(executable))
        {
            var libraryDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executable))!, "..", "lib");
            if (Directory.Exists(libraryDirectory))
            {
                var currentLibraryPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
                startInfo.Environment["LD_LIBRARY_PATH"] = string.IsNullOrWhiteSpace(currentLibraryPath)
                    ? libraryDirectory
                    : string.Join(Path.PathSeparator, libraryDirectory, currentLibraryPath);
            }
        }
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException($"Could not start Python executable '{executable}'.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token).ConfigureAwait(false);
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        return new(process.ExitCode, string.Join(Environment.NewLine,
            new[] { output.Trim(), error.Trim() }.Where(value => value.Length > 0)));
    }

    private static void EnsureProcessSucceeded(ProcessResult result, string operation)
    {
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"{operation} failed with exit code {result.ExitCode}.{Environment.NewLine}{result.Output}");
    }

    private static string ResolveConfiguredExecutable(string requestedPythonExecutable)
    {
        if (!IsAutomaticExecutable(requestedPythonExecutable))
            return requestedPythonExecutable;

        var configuredExecutable = Environment.GetEnvironmentVariable("ESI_VLLM_PYTHON_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(configuredExecutable) && !IsAutomaticExecutable(configuredExecutable))
            return configuredExecutable;
        var legacyExecutable = Environment.GetEnvironmentVariable("ESI_PYTHON_REFERENCE_EXECUTABLE");
        return string.IsNullOrWhiteSpace(legacyExecutable) ? requestedPythonExecutable : legacyExecutable;
    }

    private static bool IsAutomaticExecutable(string executable) =>
        string.Equals(executable, "python3", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(executable, "python", StringComparison.OrdinalIgnoreCase);

    private sealed record ProcessResult(int ExitCode, string Output);

    internal sealed record PythonEnvironmentPreparation(
        string PythonExecutable,
        string RequirementsPath,
        bool EnvironmentCreated,
        string Message);
}
