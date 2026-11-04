using System.Diagnostics;
using System.Text.Json;
using Esi.AI.Backend.Abstractions;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Esi.AI.Studio.Services;

/// <summary>Executes backend diagnostics in an operating-system constrained worker process.</summary>
public sealed class BackendSandboxBroker : IHostedService, IDisposable
{
    private const string WorkerProjectName = "Esi.AI.Backend.Worker.dll";
    private readonly object lifecycleSync = new();
    private readonly HashSet<ActiveExecution> activeExecutions = [];
    private readonly BackendSandboxOptions options;
    private readonly ApplicationSettingsService applicationSettings;
    private readonly string workerPath;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CancellationTokenSource stopping = new();
    private Task? stopTask;
    private bool isStopping;

    public BackendSandboxBroker(
        IOptions<BackendSandboxOptions> options,
        ApplicationSettingsService applicationSettings)
    {
        this.options = options.Value;
        this.applicationSettings = applicationSettings;
        workerPath = ResolveWorkerPath(this.options.WorkerPath);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        ActiveExecution[] active;
        TaskCompletionSource stopCompletion;
        lock (lifecycleSync)
        {
            if (stopTask is not null)
                return stopTask;

            isStopping = true;
            active = activeExecutions.ToArray();
            stopCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            stopTask = stopCompletion.Task;
        }

        _ = FinishStopAsync(active, cancellationToken, stopCompletion);
        return stopCompletion.Task;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (lifecycleSync)
        {
            if (activeExecutions.Count == 0)
                stopping.Dispose();
        }
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
    public async Task<IReadOnlyList<BackendAcceleratorDevice>> DiscoverDevicesAsync(
        string backendVariantId,
        string applicationDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backendVariantId);
        var backend = backendVariantId.StartsWith("vllm.", StringComparison.OrdinalIgnoreCase)
            ? ConfigurationBackend.Vllm
            : string.Equals(backendVariantId, "openvino", StringComparison.OrdinalIgnoreCase)
                ? ConfigurationBackend.OpenVino
                : backendVariantId.StartsWith("llama.", StringComparison.OrdinalIgnoreCase)
                    ? ConfigurationBackend.Llama
                    : throw new ArgumentException($"Backend variant '{backendVariantId}' is not supported.", nameof(backendVariantId));
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

    /// <summary>Returns Vulkan's native device-discovery output from the constrained worker.</summary>
    public async Task<string> ReadVulkanLogAsync(string applicationDirectory, CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync(new BackendWorkerRequest(
            "discover-devices",
            ConfigurationBackend.Llama,
            ApplicationDirectory: applicationDirectory,
            TimeoutSeconds: options.DiagnosticTimeoutSeconds,
            BackendVariantId: "llama.vulkan"), cancellationToken).ConfigureAwait(false);
        if (!response.Succeeded)
            throw new InvalidOperationException(response.Error ?? "The Vulkan worker could not read its runtime log.");

        return response.LoadLog ?? string.Empty;
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
        var execution = RegisterExecution();
        if (execution is null)
            return new(false, Error: "The backend sandbox is shutting down.");

        try
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                stopping.Token);
            return await ExecuteWorkerAsync(request, cancellationToken, linkedCancellation.Token, execution)
                .ConfigureAwait(false);
        }
        finally
        {
            CompleteExecution(execution);
        }
    }

    private async Task<BackendWorkerResponse> ExecuteWorkerAsync(
        BackendWorkerRequest request,
        CancellationToken callerCancellationToken,
        CancellationToken operationCancellationToken,
        ActiveExecution execution)
    {
        var settings = await applicationSettings.ReadAsync(operationCancellationToken).ConfigureAwait(false);
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
        var processStarted = false;

        try
        {
            operationCancellationToken.ThrowIfCancellationRequested();
            if (!process.Start())
                return new(false, Error: "The constrained backend worker could not be started.");
            processStarted = true;

            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, jsonOptions)).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(operationCancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(sandbox.WorkerTimeoutSeconds, 1, 300)));
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            var outputLines = new List<string>();
            string? responseJson = null;
            string? outputLine;
            while ((outputLine = await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false)) is not null)
            {
                if (outputLine.TrimStart().StartsWith('{'))
                {
                    responseJson = outputLine;
                    break;
                }

                outputLines.Add(outputLine);
            }

            process.StandardInput.Close();
            var remainingOutput = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(remainingOutput))
                outputLines.Add(remainingOutput);
            var output = responseJson ?? string.Join(Environment.NewLine, outputLines);
            var error = await errorTask.ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(output))
                return new(false, Error: string.IsNullOrWhiteSpace(error) ? "The backend worker returned no output." : error.Trim());

            if (responseJson is null && process.ExitCode != 0)
            {
                var diagnostics = string.Join(Environment.NewLine,
                    new[] { output.Trim(), error.Trim() }.Where(value => !string.IsNullOrWhiteSpace(value)));
                return new(false, Error: $"The backend worker exited with code {process.ExitCode} without returning a response.{Environment.NewLine}{diagnostics}");
            }

            return responseJson is null
                ? new(false, Error: $"The backend worker returned no JSON response.{Environment.NewLine}{output}")
                : JsonSerializer.Deserialize<BackendWorkerResponse>(responseJson, jsonOptions)
                ?? new BackendWorkerResponse(false, Error: "The backend worker returned invalid JSON.");
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            return new(false, Error: "The backend worker was stopped because the host is shutting down.");
        }
        catch (OperationCanceledException) when (!callerCancellationToken.IsCancellationRequested)
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
            if (processStarted)
            {
                try
                {
                    await BackendProcessLifecycle.StopAsync(process).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    execution.StopFailure = exception;
                    throw;
                }
            }
        }
    }

    private ActiveExecution? RegisterExecution()
    {
        lock (lifecycleSync)
        {
            if (isStopping)
                return null;

            var execution = new ActiveExecution();
            activeExecutions.Add(execution);
            return execution;
        }
    }

    private void CompleteExecution(ActiveExecution execution)
    {
        lock (lifecycleSync)
            activeExecutions.Remove(execution);
        execution.Completion.TrySetResult();
    }

    private async Task FinishStopAsync(
        ActiveExecution[] active,
        CancellationToken cancellationToken,
        TaskCompletionSource completion)
    {
        try
        {
            stopping.Cancel();
            await Task.WhenAll(active.Select(execution => execution.Completion.Task))
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            var failures = active
                .Select(execution => execution.StopFailure)
                .OfType<Exception>()
                .ToArray();
            if (failures.Length > 0)
                throw new AggregateException("One or more backend sandbox workers failed to stop.", failures);

            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
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
        startInfo.ArgumentList.Add("--setenv=ESI_PARENT_LIFETIME_PIPE=1");
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
        BackendProcessLifecycle.ConfigureStartInfo(startInfo);
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

    private sealed class ActiveExecution
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? StopFailure { get; set; }
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
