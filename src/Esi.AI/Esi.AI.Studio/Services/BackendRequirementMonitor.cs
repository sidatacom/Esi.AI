using System.Text.Json;
using System.Threading.Channels;
using Esi.AI.Models;
using Esi.AI.Studio.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Esi.AI.Studio.Services;

/// <summary>Maintains backend prerequisite diagnostics outside the page request path.</summary>
public sealed class BackendRequirementMonitor : BackgroundService, IBackendRequirementState
{
    private static readonly BackendRoute[] Routes =
    [
        new("llama.vulkan", ConfigurationBackend.Llama, "Vulkan", ["vulkan:0"]),
        new("llama.cuda12", ConfigurationBackend.Llama, "NVIDIA / CUDA", ["cuda:0"]),
        new("llama.sycl", ConfigurationBackend.Llama, "Intel / XPU", ["sycl:0"]),
        new("openvino", ConfigurationBackend.OpenVino, "Intel / XPU", []),
        new("vllm.cuda12", ConfigurationBackend.Vllm, "NVIDIA / CUDA", ["cuda:0"]),
        new("vllm.xpu", ConfigurationBackend.Vllm, "Intel / XPU", ["xpu:0"]),
        new("sglang", ConfigurationBackend.Sglang, "NVIDIA / CUDA", ["cuda:0"]),
        new("sglang", ConfigurationBackend.Sglang, "Intel / XPU", ["xpu:0"]),
        new("dotllm.cpu", ConfigurationBackend.DotLlm, "NVIDIA / CUDA", [], IsBundled: true),
        new("dotllm.cpu", ConfigurationBackend.DotLlm, "Intel / XPU", [], IsBundled: true),
        new("dotllm.cpu", ConfigurationBackend.DotLlm, "AMD / ROCm", [], IsBundled: true)
    ];

    private static readonly string[] BackendIds = Routes
        .Select(route => route.BackendId)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private readonly BackendSandboxBroker sandbox;
    private readonly ApplicationSettingsService applicationSettings;
    private readonly IHubContext<DataHub> hubContext;
    private readonly ILogger<BackendRequirementMonitor> logger;
    private readonly Channel<BackendRequirementRefreshRequest> refreshRequests = Channel.CreateUnbounded<BackendRequirementRefreshRequest>();
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private BackendRequirementState current = new([], DateTimeOffset.MinValue);
    private DateTimeOffset lastPublishedAtUtc = DateTimeOffset.MinValue;

    public BackendRequirementMonitor(
        BackendSandboxBroker sandbox,
        ApplicationSettingsService applicationSettings,
        IHubContext<DataHub> hubContext,
        ILogger<BackendRequirementMonitor> logger)
    {
        this.sandbox = sandbox;
        this.applicationSettings = applicationSettings;
        this.hubContext = hubContext;
        this.logger = logger;
    }

    /// <summary>Gets the most recent cached state without starting a diagnostic process.</summary>
    public BackendRequirementState Current => Volatile.Read(ref current);

    public async Task<BackendRequirementState> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var settings = await applicationSettings.ReadAsync(cancellationToken).ConfigureAwait(false);
        return await RefreshAsync(GetEnabledBackendIds(settings.EnabledBackendIds), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Refreshes only the requested backend variants and preserves unrelated cached results.</summary>
    public async Task<BackendRequirementState> RefreshAsync(IReadOnlyCollection<string> backendIds, CancellationToken cancellationToken = default)
    {
        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RefreshCoreAsync(backendIds, cancellationToken).ConfigureAwait(false);
            return Current;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    /// <summary>Gets the enabled backend IDs, treating a missing setting as all known backends enabled.</summary>
    public static IReadOnlyList<string> GetEnabledBackendIds(IReadOnlyCollection<string>? configuredBackendIds) =>
        (configuredBackendIds ?? BackendIds)
            .Where(id => !string.IsNullOrWhiteSpace(id) && BackendIds.Contains(id, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>Returns known backends enabled by an update but not by the previous settings.</summary>
    public static IReadOnlyList<string> GetNewlyEnabledBackendIds(
        IReadOnlyCollection<string>? previousBackendIds,
        IReadOnlyCollection<string>? updatedBackendIds)
    {
        var previouslyEnabled = GetEnabledBackendIds(previousBackendIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return GetEnabledBackendIds(updatedBackendIds)
            .Where(backendId => !previouslyEnabled.Contains(backendId))
            .ToArray();
    }

    /// <summary>Maps a backend family and selected device routes to the affected backend variants.</summary>
    public static IReadOnlyList<string> GetBackendIdsForRoute(ConfigurationBackend backend, IReadOnlyCollection<string>? devices)
    {
        var requestedDevices = devices?.Where(device => !string.IsNullOrWhiteSpace(device)).ToArray() ?? [];
        return Routes
            .Where(route => route.Backend == backend &&
                (requestedDevices.Length == 0 || route.Devices.Count == 0 || route.Devices.Any(routeDevice =>
                    requestedDevices.Any(device => device.StartsWith(routeDevice.Split(':', 2)[0] + ":", StringComparison.OrdinalIgnoreCase)))))
            .Select(route => route.BackendId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Maps a native runtime package route to the backend variant whose requirements it affects.</summary>
    public static IReadOnlyList<string> GetBackendIdsForRuntimeRoute(ConfigurationBackend backend, string route)
    {
        var normalizedRoute = route.Trim().ToLowerInvariant();
        var device = normalizedRoute switch
        {
            var value when value.Contains("vulkan", StringComparison.Ordinal) => "vulkan:0",
            var value when value.Contains("cuda", StringComparison.Ordinal) => "cuda:0",
            var value when value.Contains("sycl", StringComparison.Ordinal) => "sycl:0",
            _ => null
        };

        return device is null ? [] : GetBackendIdsForRoute(backend, [device]);
    }

    /// <summary>Requests a refresh for all currently enabled backends.</summary>
    public void RequestRefresh() => refreshRequests.Writer.TryWrite(new(null));

    /// <summary>Requests an out-of-band refresh for one backend variant.</summary>
    public void RequestRefresh(string backendId) => refreshRequests.Writer.TryWrite(new([backendId]));

    /// <summary>Requests refreshes for the backend variants matching a preparation route.</summary>
    public void RequestRefresh(ConfigurationBackend backend, IReadOnlyCollection<string>? devices)
    {
        foreach (var backendId in GetBackendIdsForRoute(backend, devices))
            RequestRefresh(backendId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var settings = await applicationSettings.ReadAsync(stoppingToken).ConfigureAwait(false);
            await RefreshAsync(GetEnabledBackendIds(settings.EnabledBackendIds), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Initial backend requirement refresh failed.");
        }

        await foreach (var request in refreshRequests.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                if (request.BackendIds is null)
                {
                    var settings = await applicationSettings.ReadAsync(stoppingToken).ConfigureAwait(false);
                    await RefreshAsync(GetEnabledBackendIds(settings.EnabledBackendIds), stoppingToken).ConfigureAwait(false);
                }
                else
                    await RefreshAsync(request.BackendIds, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Backend requirement refresh failed for {BackendIds}.", request.BackendIds is null ? "enabled backends" : string.Join(", ", request.BackendIds));
            }
        }
    }

    private async Task RefreshCoreAsync(IReadOnlyCollection<string> backendIds, CancellationToken cancellationToken)
    {
        var requestedBackendIds = GetEnabledBackendIds(backendIds);
        var requestedBackendSet = requestedBackendIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entries = Current.Entries
            .Where(entry => !requestedBackendSet.Contains(entry.BackendId))
            .ToList();

        await PublishAsync(entries, true, cancellationToken).ConfigureAwait(false);

        foreach (var backendId in requestedBackendIds)
        {
            foreach (var route in Routes.Where(route => route.BackendId.Equals(backendId, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    entries.Add(await DiagnoseRouteAsync(route, cancellationToken).ConfigureAwait(false));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    entries.Add(CreateFailedSnapshot(route, exception));
                }

                await PublishAsync(entries, true, cancellationToken).ConfigureAwait(false);
            }
        }

        await PublishAsync(entries, false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<BackendRequirementSnapshot> DiagnoseRouteAsync(BackendRoute route, CancellationToken cancellationToken)
    {
        if (route.IsBundled)
            return CreateBundledSnapshot(route);

        if (route.Backend == ConfigurationBackend.OpenVino)
        {
            var result = await sandbox.DiagnoseOpenVinoAsync(cancellationToken).ConfigureAwait(false);
            var checks = result.Checks
                .Select(check => new BackendPrerequisiteCheck(check.Id, check.Name, check.IsAvailable, check.Detail, check.CanSolve))
                .ToArray();
            return new(
                route.Backend,
                route.Vendor,
                route.Devices,
                new(route.Backend, "OpenVINO", result.IsGpuReady || result.IsNpuReady, checks, result.Error),
                route.BackendId);
        }

        var diagnostics = await sandbox.DiagnoseRequirementsAsync(
            route.Backend,
            "python3",
            AppContext.BaseDirectory,
            route.Devices,
            cancellationToken).ConfigureAwait(false);
        return new(route.Backend, route.Vendor, route.Devices, diagnostics, route.BackendId);
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
        await hubContext.Clients.All.SendAsync("BackendRequirement_Update", state, cancellationToken).ConfigureAwait(false);
    }

    private static BackendRequirementSnapshot CreateBundledSnapshot(BackendRoute route) =>
        new(route.Backend, route.Vendor, [], new(
            route.Backend,
            route.Backend.ToString(),
            true,
            [new("bundled-runtime", $"{route.Backend} runtime", true, "The native runtime is bundled with the application.", false)]),
            route.BackendId);

    private static BackendRequirementSnapshot CreateFailedSnapshot(BackendRoute route, Exception exception) =>
        new(route.Backend, route.Vendor, route.Devices, new(
            route.Backend,
            route.Backend.ToString(),
            false,
            [new("diagnostics", "Backend diagnostics", false, exception.Message, false)],
            exception.ToString()),
            route.BackendId);

    private sealed record BackendRoute(string BackendId, ConfigurationBackend Backend, string Vendor, IReadOnlyList<string> Devices, bool IsBundled = false);

    private sealed record BackendRequirementRefreshRequest(IReadOnlyList<string>? BackendIds);
}