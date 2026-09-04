using System.Runtime.InteropServices;
using Esi.AI.Models;

namespace Esi.AI.Core.ModelLoading;

/// <summary>Applies the common prerequisite workflow to every supported backend.</summary>
public sealed class BackendPrerequisiteProvisioner
{
    private readonly PythonBackendProvisioner pythonProvisioner;
    private readonly BackendRuntimeInstaller? runtimeInstaller;

    /// <summary>Creates a prerequisite provisioner for native and Python backends.</summary>
    public BackendPrerequisiteProvisioner(PythonBackendProvisioner? pythonProvisioner = null, BackendRuntimeInstaller? runtimeInstaller = null)
    {
        this.pythonProvisioner = pythonProvisioner ?? new PythonBackendProvisioner();
        this.runtimeInstaller = runtimeInstaller;
    }

    /// <summary>Prepares the selected backend and returns the runtime details for its loader.</summary>
    public async Task<BackendPreparationResult> PrepareAsync(
        ConfigurationBackend backend,
        string requestedPythonExecutable = "python3",
        string? applicationDirectory = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? devices = null)
    {
        if (backend is ConfigurationBackend.Vllm or ConfigurationBackend.Sglang)
        {
            var preparation = await pythonProvisioner.PrepareAsync(
                backend,
                requestedPythonExecutable,
                applicationDirectory ?? AppContext.BaseDirectory,
                timeout ?? TimeSpan.FromMinutes(10),
                cancellationToken,
                devices).ConfigureAwait(false);
            return new(backend, preparation.PythonExecutable, preparation.RequirementsPath, preparation.EnvironmentCreated, preparation.Message);
        }

        if (backend == ConfigurationBackend.Llama)
        {
            var route = devices?.FirstOrDefault()?.Split(':', 2)[0] ?? "CPU";
            EnsureLlamaReady(route, applicationDirectory);
            return new(backend, null, null, false, $"LLama {route} native runtime is ready.");
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

    /// <summary>Throws when the selected LLama native route is not available.</summary>
    public void EnsureLlamaReady(string backend, string? applicationDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(backend))
            throw new ArgumentException("A LLama backend is required.", nameof(backend));

        var normalizedBackend = backend.Trim().ToLowerInvariant();
        if (normalizedBackend is not ("cpu" or "vulkan" or "cuda" or "sycl"))
            throw new ArgumentException("Backend must be Vulkan, CUDA, SYCL, or CPU.", nameof(backend));

        var diagnostics = DiagnoseLlama(applicationDirectory ?? AppContext.BaseDirectory, normalizedBackend == "cpu" ? null : [$"{normalizedBackend}:0"]);
        if (diagnostics.IsReady)
            return;

        var failedChecks = diagnostics.Checks
            .Where(check => !check.IsOptional && !check.IsAvailable)
            .Select(check => $"{check.Name}: {check.Detail}");
        throw new InvalidOperationException($"LLama backend '{backend}' is not ready. {string.Join(" ", failedChecks)}");
    }

    /// <summary>Diagnoses backend requirements without mutating the host or installing packages.</summary>
    public async Task<BackendPrerequisiteDiagnostics> DiagnoseAsync(
        ConfigurationBackend backend,
        string requestedPythonExecutable = "python3",
        string? applicationDirectory = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? devices = null)
    {
        if (backend is ConfigurationBackend.Vllm or ConfigurationBackend.Sglang)
            return await pythonProvisioner.DiagnoseAsync(backend, requestedPythonExecutable, applicationDirectory ?? AppContext.BaseDirectory, timeout ?? TimeSpan.FromSeconds(20), cancellationToken, devices).ConfigureAwait(false);

        if (backend == ConfigurationBackend.Llama)
            return await DiagnoseLlamaAsync(applicationDirectory ?? AppContext.BaseDirectory, devices, cancellationToken).ConfigureAwait(false);

        var name = backend switch
        {
            ConfigurationBackend.OpenVino => "OpenVINO",
            ConfigurationBackend.DotLlm => "dotLLM",
            _ => throw new ArgumentException("The selected backend is not supported.", nameof(backend))
        };
        return new(backend, name, true,
            [new("bundled-runtime", $"{name} runtime", true, $"{name} runtime is bundled with the application.", false)]);
    }

    private async Task<BackendPrerequisiteDiagnostics> DiagnoseLlamaAsync(string applicationDirectory, IReadOnlyList<string>? devices, CancellationToken cancellationToken)
    {
        var route = devices?.FirstOrDefault()?.Split(':', 2)[0] ?? "cpu";
        var canInstall = runtimeInstaller is not null && await runtimeInstaller.CanInstallAsync(ConfigurationBackend.Llama, route, cancellationToken).ConfigureAwait(false);
        return DiagnoseLlama(applicationDirectory, devices, canInstall);
    }

    private BackendPrerequisiteDiagnostics DiagnoseLlama(string applicationDirectory, IReadOnlyList<string>? devices, bool canInstall = false)
    {
        var nativeRoot = Path.Combine(applicationDirectory, "runtimes", GetLlamaRuntimeIdentifier(), "native");
        var cpuRuntimeDirectory = new[] { "avx512", "avx2", "avx", "noavx" }
            .Select(directory => Path.Combine(nativeRoot, directory))
            .FirstOrDefault(directory => new[] { "libllama.so", "libggml.so", "libggml-base.so" }.All(file => File.Exists(Path.Combine(directory, file))))
            ?? Path.Combine(nativeRoot, "avx2");
        var route = devices?.FirstOrDefault()?.Split(':', 2)[0].ToLowerInvariant();
        var checks = new List<BackendPrerequisiteCheck>
        {
            CreateNativeCheck(
                "llama-runtime",
                "LLama native runtime",
                cpuRuntimeDirectory,
                ["libllama.so", "libggml.so", "libggml-base.so"],
                "The CPU-compatible LLama native libraries are bundled with the application.")
        };

        switch (route)
        {
            case "cuda":
                var cudaCheck = CreateNativeCheck(
                    "cuda12-runtime",
                    "CUDA 12 runtime",
                    Path.Combine(nativeRoot, "cuda12"),
                    ["libllama.so", "libggml-cuda.so"],
                    "The LLamaSharp CUDA 12 native libraries are bundled with the application.");
                checks.Add(canInstall && !cudaCheck.IsAvailable
                    ? cudaCheck with { CanSolve = true, Detail = $"{cudaCheck.Detail} A verified CUDA 12 package is available in the backend gallery." }
                    : cudaCheck);
                checks.Add(CreateCommandCheck(
                    "cuda-driver",
                    "NVIDIA driver",
                    "nvidia-smi",
                    "The NVIDIA driver must expose the RTX GPU to CUDA."));
                break;
            case "sycl":
            case "xpu":
                var syclCheck = CreateNativeCheck(
                    "sycl-runtime",
                    "SYCL 16 native runtime",
                    Path.Combine(nativeRoot, "sycl"),
                    ["libllama.so", "libggml-sycl.so"],
                    "The LLamaSharp SYCL native libraries must be built and bundled for Intel Arc.");
                checks.Add(canInstall && !syclCheck.IsAvailable
                    ? syclCheck with { CanSolve = true, Detail = $"{syclCheck.Detail} A verified SYCL 16 package is available in the backend gallery." }
                    : syclCheck);
                checks.Add(CreateLevelZeroHostCheck());
                checks.Add(CreateToolchainCheck(
                    "oneapi-build-toolchain",
                    "oneAPI build toolchain",
                    ["icpx", "sycl-ls", "ze_info"],
                    "The SYCL runtime is built with Intel oneAPI. These tools are not required after installation.",
                    true));
                break;
            case "vulkan":
                checks.Add(CreateNativeCheck(
                    "vulkan-runtime",
                    "Vulkan runtime",
                    Path.Combine(nativeRoot, "vulkan"),
                    ["libllama.so", "libggml-vulkan.so"],
                    "The LLamaSharp Vulkan native libraries are bundled with the application."));
                break;
        }

        var requiredChecks = checks.Where(check => !check.IsOptional).ToArray();
        return new BackendPrerequisiteDiagnostics(
            ConfigurationBackend.Llama,
            "LLama",
            requiredChecks.All(check => check.IsAvailable),
            checks);
    }

    private static BackendPrerequisiteCheck CreateNativeCheck(
        string id,
        string name,
        string directory,
        IReadOnlyList<string> files,
        string availableDetail)
    {
        var missing = files.Where(file => !File.Exists(Path.Combine(directory, file))).ToArray();
        return missing.Length == 0
            ? new(id, name, true, availableDetail, false)
            : new(id, name, false, $"Missing native file(s): {string.Join(", ", missing)} in {directory}.", false);
    }

    private static BackendPrerequisiteCheck CreateCommandCheck(string id, string name, string command, string detail)
    {
        var available = FindExecutable(command) is not null;
        return new(id, name, available, available ? $"{detail} Found {command}." : $"{detail} Command '{command}' was not found on PATH.", false);
    }

    private static BackendPrerequisiteCheck CreateToolchainCheck(string id, string name, IReadOnlyList<string> commands, string detail, bool isOptional = false)
    {
        var missing = commands.Where(command => FindExecutable(command) is null).ToArray();
        return missing.Length == 0
            ? new(id, name, true, $"{detail} Found: {string.Join(", ", commands)}.", false, isOptional)
            : new(id, name, false, $"{detail} Missing command(s): {string.Join(", ", missing)}.", false, isOptional);
    }

    private static BackendPrerequisiteCheck CreateLevelZeroHostCheck()
    {
        IReadOnlyList<string> libraryNames = OperatingSystem.IsWindows()
            ? ["ze_loader.dll"]
            : ["libze_loader.so.1", "libze_loader.so"];
        IEnumerable<string> searchDirectories = (Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(OperatingSystem.IsWindows()
                ? [Environment.SystemDirectory]
                : ["/usr/lib", "/usr/lib64", "/usr/lib/x86_64-linux-gnu", "/usr/lib/i386-linux-gnu"])
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var libraryPath = searchDirectories
            .SelectMany(directory => libraryNames.Select(name => Path.Combine(directory, name)))
            .FirstOrDefault(File.Exists);
        return libraryPath is not null
            ? new("intel-level-zero", "Intel Level Zero runtime", true, $"The Intel GPU host runtime was found at {libraryPath}.", false)
            : new("intel-level-zero", "Intel Level Zero runtime", false, "The Intel Level Zero loader was not found on the host.", false);
    }

    private static string? FindExecutable(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return path.Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, command))
            .FirstOrDefault(File.Exists);
    }

    private static string GetLlamaRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";

        return RuntimeInformation.RuntimeIdentifier;
    }
}

/// <summary>Contains the prepared runtime details shared by all backend loaders.</summary>
public sealed record BackendPreparationResult(
    ConfigurationBackend Backend,
    string? PythonExecutable,
    string? RequirementsPath,
    bool EnvironmentCreated,
    string Message);