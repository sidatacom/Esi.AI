using System.Text.Json;
using System.Threading.Channels;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Esi.AI.Studio.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Esi.AI.Studio.Services;

/// <summary>Maintains backend prerequisite diagnostics outside the page request path.</summary>
public sealed class BackendRequirementMonitor : BackgroundService
{
    private static readonly BackendRoute[] Routes =
    [
        new(ConfigurationBackend.Vllm, "NVIDIA / CUDA", ["cuda:0"]),
        new(ConfigurationBackend.Vllm, "Intel / XPU", ["xpu:1"]),
        new(ConfigurationBackend.Sglang, "NVIDIA / CUDA", ["cuda:0"]),
        new(ConfigurationBackend.Sglang, "Intel / XPU", ["xpu:1"])
    ];

    private readonly BackendPrerequisiteProvisioner prerequisites;
    private readonly OpenVinoDiagnosticsService openVinoDiagnostics;
    private readonly IHubContext<DataHub> hubContext;
    private readonly Channel<bool> refreshRequests = Channel.CreateBounded<bool>(1);
    private BackendRequirementState current = new([], DateTimeOffset.MinValue);
    private DateTimeOffset lastPublishedAtUtc = DateTimeOffset.MinValue;

    public BackendRequirementMonitor(
        BackendPrerequisiteProvisioner prerequisites,
        OpenVinoDiagnosticsService openVinoDiagnostics,
        IHubContext<DataHub> hubContext)
    {
        this.prerequisites = prerequisites;
        this.openVinoDiagnostics = openVinoDiagnostics;
        this.hubContext = hubContext;
    }

    /// <summary>Gets the most recent cached state without starting a diagnostic process.</summary>
    public BackendRequirementState Current => Volatile.Read(ref current);

    /// <summary>Requests an out-of-band refresh after a requirement action completes.</summary>
    public void RequestRefresh() => refreshRequests.Writer.TryWrite(true);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshAsync(stoppingToken).ConfigureAwait(false);

            try
            {
                var interval = Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                var requested = refreshRequests.Reader.WaitToReadAsync(stoppingToken).AsTask();
                await Task.WhenAny(interval, requested).ConfigureAwait(false);
                while (refreshRequests.Reader.TryRead(out _))
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var entries = new List<BackendRequirementSnapshot>
        {
            CreateBundledSnapshot(ConfigurationBackend.Llama, "NVIDIA / CUDA"),
            CreateBundledSnapshot(ConfigurationBackend.Llama, "Intel / XPU"),
            CreateBundledSnapshot(ConfigurationBackend.Llama, "AMD / ROCm"),
            CreateBundledSnapshot(ConfigurationBackend.DotLlm, "NVIDIA / CUDA"),
            CreateBundledSnapshot(ConfigurationBackend.DotLlm, "Intel / XPU"),
            CreateBundledSnapshot(ConfigurationBackend.DotLlm, "AMD / ROCm")
        };

        await PublishAsync(entries, true, cancellationToken).ConfigureAwait(false);

        try
        {
            var result = openVinoDiagnostics.Diagnose();
            var checks = result.Checks
                .Select(check => new BackendPrerequisiteCheck(check.Id, check.Name, check.IsAvailable, check.Detail, check.CanSolve))
                .ToArray();
            entries.Add(new(
                ConfigurationBackend.OpenVino,
                "Intel / XPU",
                [],
                new(ConfigurationBackend.OpenVino, "OpenVINO", result.IsGpuReady || result.IsNpuReady, checks, result.Error)));
        }
        catch (Exception exception)
        {
            entries.Add(CreateFailedSnapshot(ConfigurationBackend.OpenVino, "Intel / XPU", [], exception));
        }

        await PublishAsync(entries, true, cancellationToken).ConfigureAwait(false);

        foreach (var route in Routes)
        {
            BackendPrerequisiteDiagnostics diagnostics;
            try
            {
                diagnostics = await prerequisites.DiagnoseAsync(
                    route.Backend,
                    "python3",
                    AppContext.BaseDirectory,
                    TimeSpan.FromSeconds(20),
                    cancellationToken,
                    route.Devices).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                entries.Add(CreateFailedSnapshot(route.Backend, route.Vendor, route.Devices, exception));
                await PublishAsync(entries, true, cancellationToken).ConfigureAwait(false);
                continue;
            }

            entries.Add(new(route.Backend, route.Vendor, route.Devices, diagnostics));
            await PublishAsync(entries, true, cancellationToken).ConfigureAwait(false);
        }

        await PublishAsync(entries, false, cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishAsync(List<BackendRequirementSnapshot> entries, bool isRefreshing, CancellationToken cancellationToken)
    {
        var state = new BackendRequirementState(entries.ToArray(), DateTimeOffset.UtcNow, isRefreshing);
        var previous = Volatile.Read(ref current);
        Interlocked.Exchange(ref current, state);
        if (previous.IsRefreshing == state.IsRefreshing &&
            JsonSerializer.Serialize(previous.Entries) == JsonSerializer.Serialize(state.Entries))
            return;

        var elapsed = DateTimeOffset.UtcNow - lastPublishedAtUtc;
        if (elapsed < TimeSpan.FromSeconds(1))
            await Task.Delay(TimeSpan.FromSeconds(1) - elapsed, cancellationToken).ConfigureAwait(false);

        lastPublishedAtUtc = DateTimeOffset.UtcNow;
        await hubContext.Clients.All.SendAsync("BackendRequirementStateUpdated", state, cancellationToken).ConfigureAwait(false);
    }

    private static BackendRequirementSnapshot CreateBundledSnapshot(ConfigurationBackend backend, string vendor) =>
        new(backend, vendor, [], new(
            backend,
            backend.ToString(),
            true,
            [new("bundled-runtime", $"{backend} runtime", true, "The native runtime is bundled with the application.", false)]));

    private static BackendRequirementSnapshot CreateFailedSnapshot(ConfigurationBackend backend, string vendor, IReadOnlyList<string> devices, Exception exception) =>
        new(backend, vendor, devices, new(
            backend,
            backend.ToString(),
            false,
            [new("diagnostics", "Backend diagnostics", false, exception.Message, false)],
            exception.ToString()));

    private sealed record BackendRoute(ConfigurationBackend Backend, string Vendor, IReadOnlyList<string> Devices);
}