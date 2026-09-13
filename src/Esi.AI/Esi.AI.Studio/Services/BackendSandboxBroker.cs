using System.Diagnostics;
using System.Text.Json;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Microsoft.Extensions.Options;

namespace Esi.AI.Studio.Services;

/// <summary>Executes backend diagnostics in an operating-system constrained worker process.</summary>
public sealed class BackendSandboxBroker
{
    private const string WorkerProjectName = "Esi.AI.BackendWorker.dll";
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
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
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
        var startInfo = new ProcessStartInfo
        {
            FileName = systemdRun,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
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
        startInfo.ArgumentList.Add(Environment.ProcessPath ?? "dotnet");
        startInfo.ArgumentList.Add(workerPath);
        return startInfo;
    }

    private static string ResolveWorkerPath(string? configuredPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredPath))
            candidates.Add(Path.GetFullPath(configuredPath));

        candidates.Add(Path.Combine(AppContext.BaseDirectory, WorkerProjectName));
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var index = 0; index < 6 && directory is not null; index++, directory = directory.Parent)
            candidates.Add(Path.Combine(directory.FullName, "Esi.AI.BackendWorker", "bin", "Debug", "net10.0", WorkerProjectName));

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
