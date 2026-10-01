using System.Diagnostics;
using System.Text.Json;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Microsoft.Extensions.Options;

namespace Esi.AI.Studio.Services;

/// <summary>Executes backend diagnostics in an operating-system constrained worker process.</summary>
public sealed class BackendSandboxBroker
{
    private const string WorkerProjectName = "Esi.AI.Backend.Worker.dll";
    private readonly BackendSandboxOptions options;
    private readonly ApplicationSettingsService applicationSettings;
    private readonly string workerPath;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public BackendSandboxBroker(
        IOptions<BackendSandboxOptions> options,
        ApplicationSettingsService applicationSettings)
    {
        this.options = options.Value;
        this.applicationSettings = applicationSettings;
        workerPath = ResolveWorkerPath(this.options.WorkerPath);
    }

    /// <summary>Runs prerequisite diagnostics without executing backend probes in the Studio process.</summary>
    public async Task<BackendPrerequisiteDiagnostics> DiagnoseRequirementsAsync(
        ConfigurationBackend backend,
        string pythonExecutable,
        string applicationDirectory,
        IReadOnlyList<string>? devices,
        CancellationToken cancellationToken = default)
    {
        var request = new BackendWorkerRequest(
            "diagnose-requirements",
            backend,
            pythonExecutable,
            applicationDirectory,
            options.DiagnosticTimeoutSeconds,
            devices);
        var response = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        return response.Prerequisites ?? new BackendPrerequisiteDiagnostics(
            backend,
            backend.ToString(),
            false,
            [new("sandbox", "Backend sandbox", false, response.Error ?? "The backend worker returned no diagnostics.", false)],
            response.Error);
    }

    /// <summary>Discovers one backend variant's devices in a fresh constrained worker process.</summary>
    public async Task<IReadOnlyList<DeviceStatus>> DiscoverDevicesAsync(
        string backendVariantId,
        string applicationDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backendVariantId);
        var backend = backendVariantId.StartsWith("vllm.", StringComparison.OrdinalIgnoreCase)
            ? ConfigurationBackend.Vllm
            : string.Equals(backendVariantId, "sglang", StringComparison.OrdinalIgnoreCase)
                ? ConfigurationBackend.Sglang
                : string.Equals(backendVariantId, "openvino", StringComparison.OrdinalIgnoreCase)
                    ? ConfigurationBackend.OpenVino
                    : string.Equals(backendVariantId, "dotllm.cpu", StringComparison.OrdinalIgnoreCase)
                        ? ConfigurationBackend.DotLlm
                        : ConfigurationBackend.Llama;
        var response = await ExecuteAsync(new BackendWorkerRequest(
            "discover-devices",
            backend,
            ApplicationDirectory: applicationDirectory,
            TimeoutSeconds: options.DiagnosticTimeoutSeconds,
            BackendVariantId: backendVariantId), cancellationToken).ConfigureAwait(false);
        if (!response.Succeeded)
            throw new InvalidOperationException(response.Error ?? "The backend worker could not discover devices.");

        return response.Devices ?? [];
    }

    /// <summary>Runs OpenVINO diagnostics in a constrained worker process.</summary>
    public async Task<OpenVinoDiagnosticsDto> DiagnoseOpenVinoAsync(CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync(new BackendWorkerRequest(
            "diagnose-openvino",
            TimeoutSeconds: options.DiagnosticTimeoutSeconds), cancellationToken).ConfigureAwait(false);
        return response.OpenVino ?? new OpenVinoDiagnosticsDto
        {
            Error = response.Error ?? "The backend worker returned no OpenVINO diagnostics."
        };
    }

    private async Task<BackendWorkerResponse> ExecuteAsync(BackendWorkerRequest request, CancellationToken cancellationToken)
    {
        var settings = await applicationSettings.ReadAsync(cancellationToken).ConfigureAwait(false);
        var sandbox = settings.BackendSandbox ?? new BackendSandboxSettings();

        if (!OperatingSystem.IsLinux())
        {
            return new(false, Error: "The backend sandbox currently requires Linux systemd user scopes.");
        }

        var systemdRun = FindExecutable("systemd-run");
        if (systemdRun is null)
        {
            return new(false, Error: "The backend sandbox requires 'systemd-run' and will not use an unbounded fallback.");
        }

        using var process = new Process
        {
            StartInfo = CreateStartInfo(systemdRun, request, sandbox),
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
                return new(false, Error: "The constrained backend worker could not be started.");

            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, jsonOptions)).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
            process.StandardInput.Close();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(sandbox.WorkerTimeoutSeconds, 1, 300)));
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(output))
                return new(false, Error: string.IsNullOrWhiteSpace(error) ? "The backend worker returned no output." : error.Trim());

            return JsonSerializer.Deserialize<BackendWorkerResponse>(output, jsonOptions)
                ?? new BackendWorkerResponse(false, Error: "The backend worker returned invalid JSON.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, Error: $"The backend worker exceeded its {sandbox.WorkerTimeoutSeconds}-second limit.");
        }
        catch (OperationCanceledException)
        {
            return new(false, Error: "The backend worker diagnostic was cancelled.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(false, Error: exception.Message);
        }
        finally
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
    }

    private ProcessStartInfo CreateStartInfo(string systemdRun, BackendWorkerRequest request, BackendSandboxSettings sandbox)
    {
        var dotnetHost = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(dotnetHost) ||
            !string.Equals(Path.GetFileNameWithoutExtension(dotnetHost), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            dotnetHost = FindExecutable("dotnet")
                ?? throw new InvalidOperationException("The dotnet host executable could not be found for the backend worker.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = systemdRun,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        ConfigureCuda12Environment(startInfo, request);
        if (string.Equals(request.BackendVariantId, "llama.cuda12", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add("--setenv=CUDA_VERSION=12");
            if (GetEnvironmentVariable(startInfo, "LD_LIBRARY_PATH") is { } libraryPath)
                startInfo.ArgumentList.Add($"--setenv=LD_LIBRARY_PATH={libraryPath}");
        }

        startInfo.ArgumentList.Add("--user");
        startInfo.ArgumentList.Add("--scope");
        startInfo.ArgumentList.Add("--quiet");
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add($"CPUQuota={sandbox.CpuQuotaPercent}%");
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add($"MemoryMax={sandbox.MemoryLimitBytes}");
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add($"TasksMax={sandbox.TaskLimit}");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(dotnetHost);
        startInfo.ArgumentList.Add(workerPath);
        return startInfo;
    }

    private static void ConfigureCuda12Environment(ProcessStartInfo startInfo, BackendWorkerRequest request)
    {
        if (!OperatingSystem.IsLinux() ||
            !string.Equals(request.BackendVariantId, "llama.cuda12", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        startInfo.Environment["CUDA_VERSION"] = "12";
        var runtimeDirectory = ResolveCuda12RuntimeDirectory(startInfo);
        if (runtimeDirectory is null)
            return;

        var libraryPaths = (GetEnvironmentVariable(startInfo, "LD_LIBRARY_PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (!libraryPaths.Contains(runtimeDirectory, StringComparer.Ordinal))
            libraryPaths.Insert(0, runtimeDirectory);

        startInfo.Environment["LD_LIBRARY_PATH"] = string.Join(Path.PathSeparator, libraryPaths);
    }

    private static string? ResolveCuda12RuntimeDirectory(ProcessStartInfo startInfo)
    {
        var candidates = new List<string>();
        var configuredDirectory = GetEnvironmentVariable(startInfo, "ESI_CUDA12_RUNTIME_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
            candidates.Add(configuredDirectory);

        foreach (var variableName in new[] { "CUDA_HOME", "CUDA_PATH" })
        {
            var cudaRoot = GetEnvironmentVariable(startInfo, variableName);
            if (string.IsNullOrWhiteSpace(cudaRoot))
                continue;

            candidates.Add(Path.Combine(cudaRoot, "lib64"));
            candidates.Add(Path.Combine(cudaRoot, "lib"));
        }

        candidates.Add("/usr/local/cuda-12/lib64");
        candidates.Add("/usr/local/cuda/lib64");
        var currentLibraryPath = GetEnvironmentVariable(startInfo, "LD_LIBRARY_PATH");
        if (!string.IsNullOrWhiteSpace(currentLibraryPath))
            candidates.AddRange(currentLibraryPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            var vendorDirectory = Path.Combine(userProfile, ".lmstudio", "extensions", "backends", "vendor");
            try
            {
                if (Directory.Exists(vendorDirectory))
                    candidates.AddRange(Directory.EnumerateDirectories(vendorDirectory, "linux-llama-cuda12-vendor-*"));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return candidates
            .Where(Directory.Exists)
            .Distinct(StringComparer.Ordinal)
            .FirstOrDefault(directory =>
                File.Exists(Path.Combine(directory, "libcudart.so.12")) &&
                File.Exists(Path.Combine(directory, "libcublas.so.12")));
    }

    private static string? GetEnvironmentVariable(ProcessStartInfo startInfo, string variableName) =>
        startInfo.Environment.TryGetValue(variableName, out var value) ? value : null;

    private static string ResolveWorkerPath(string? configuredPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredPath))
            candidates.Add(Path.GetFullPath(configuredPath));

        candidates.Add(Path.Combine(AppContext.BaseDirectory, WorkerProjectName));
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var index = 0; index < 6 && directory is not null; index++, directory = directory.Parent)
            candidates.Add(Path.Combine(directory.FullName, "Esi.AI.Backend.Worker", "bin", "Debug", "net10.0", WorkerProjectName));

        var worker = candidates.FirstOrDefault(File.Exists);
        return worker ?? throw new FileNotFoundException("The backend worker was not built.", WorkerProjectName);
    }

    private static string? FindExecutable(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return path.Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, command))
            .FirstOrDefault(file => File.Exists(file));
    }
}

/// <summary>Controls operating-system limits applied to each backend worker.</summary>
public sealed class BackendSandboxOptions
{
    public string? WorkerPath { get; set; }
    public int CpuQuotaPercent { get; set; } = 200;
    public string MemoryLimitBytes { get; set; } = "4G";
    public int TaskLimit { get; set; } = 64;
    public int DiagnosticTimeoutSeconds { get; set; } = 20;
    public int WorkerTimeoutSeconds { get; set; } = 60;
}
