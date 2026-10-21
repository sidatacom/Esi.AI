using Esi.AI.Models;
using Esi.AI.Studio.Services;
using Esi.AI.Core.ModelLoading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Studio.Tests;

[TestClass]
public sealed class BackendRequirementMonitorTests
{
    [TestMethod]
    public async Task BackendRequirement_UpdateAsync_QueuesRefreshAndReturnsCachedState()
    {
        var cachedState = new BackendRequirementState([], DateTimeOffset.UtcNow);
        var requirementState = new FakeBackendRequirementState(cachedState);
        using var modelRuntime = new ModelRuntime();
        var dataService = CreateDataService(requirementState, modelRuntime);

        var result = await dataService.BackendRequirement_UpdateAsync();

        Assert.AreSame(cachedState, result);
        Assert.AreEqual(1, requirementState.RefreshRequests);
    }

    [TestMethod]
    public async Task BackendRequirement_InitializeAsync_QueuesRefreshWhenIdle()
    {
        var requirementState = new FakeBackendRequirementState(new([], DateTimeOffset.UtcNow));
        using var modelRuntime = new ModelRuntime();
        var dataService = CreateDataService(requirementState, modelRuntime);

        await dataService.BackendRequirement_InitializeAsync();

        Assert.AreEqual(1, requirementState.RefreshRequests);
    }

    [TestMethod]
    public async Task BackendRequirement_InitializeAsync_DoesNotQueueWhileRefreshing()
    {
        var requirementState = new FakeBackendRequirementState(new([], DateTimeOffset.UtcNow, IsRefreshing: true));
        using var modelRuntime = new ModelRuntime();
        var dataService = CreateDataService(requirementState, modelRuntime);

        await dataService.BackendRequirement_InitializeAsync();

        Assert.AreEqual(0, requirementState.RefreshRequests);
    }

    [TestMethod]
    public void GetEnabledBackendIds_WhenSettingIsMissing_ReturnsAllKnownBackends()
    {
        var backendIds = BackendRequirementMonitor.GetEnabledBackendIds(null);

        CollectionAssert.AreEqual(
            new[] { "llama.vulkan", "llama.cuda12", "llama.sycl", "openvino", "vllm.cuda12", "vllm.xpu" },
            backendIds.ToArray());
    }

    [TestMethod]
    public void GetNewlyEnabledBackendIds_ReturnsOnlyNewlyEnabledKnownBackends()
    {
        var backendIds = BackendRequirementMonitor.GetNewlyEnabledBackendIds(
            ["llama.vulkan", "vllm.cuda12"],
            ["llama.vulkan", "vllm.cuda12", "vllm.xpu", "unknown"]);

        CollectionAssert.AreEqual(new[] { "vllm.xpu" }, backendIds.ToArray());
    }

    [TestMethod]
    public void GetNewlyEnabledBackendIds_MissingPreviousSettingTreatsAllAsEnabled()
    {
        var backendIds = BackendRequirementMonitor.GetNewlyEnabledBackendIds(
            null,
            ["llama.vulkan"]);

        Assert.AreEqual(0, backendIds.Count);
    }

    [TestMethod]
    public void GetBackendIdsForRoute_MapsEachAcceleratorToItsBackendVariant()
    {
        CollectionAssert.AreEqual(
            new[] { "llama.vulkan" },
            BackendRequirementMonitor.GetBackendIdsForRoute(ConfigurationBackend.Llama, ["vulkan:0"]).ToArray());
        CollectionAssert.AreEqual(
            new[] { "llama.cuda12" },
            BackendRequirementMonitor.GetBackendIdsForRoute(ConfigurationBackend.Llama, ["cuda:1"]).ToArray());
        CollectionAssert.AreEqual(
            new[] { "llama.sycl" },
            BackendRequirementMonitor.GetBackendIdsForRoute(ConfigurationBackend.Llama, ["sycl:0"]).ToArray());
        CollectionAssert.AreEqual(
            new[] { "vllm.xpu" },
            BackendRequirementMonitor.GetBackendIdsForRoute(ConfigurationBackend.Vllm, ["xpu:1"]).ToArray());
    }

    [TestMethod]
    public void GetBackendIdsForRuntimeRoute_MapsNativePackageRouteToMatchingBackend()
    {
        CollectionAssert.AreEqual(
            new[] { "llama.cuda12" },
            BackendRequirementMonitor.GetBackendIdsForRuntimeRoute(ConfigurationBackend.Llama, "cuda12-runtime").ToArray());
        CollectionAssert.AreEqual(
            new[] { "llama.sycl" },
            BackendRequirementMonitor.GetBackendIdsForRuntimeRoute(ConfigurationBackend.Llama, "sycl16").ToArray());
    }

    private sealed class FakeBackendRequirementState(BackendRequirementState current) : IBackendRequirementState
    {
        public BackendRequirementState Current => current;
        public int RefreshRequests { get; private set; }

        public Task<BackendRequirementState> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(current);

        public Task<BackendRequirementState> RefreshAsync(IReadOnlyCollection<string> backendIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(current);

        public void RequestRefresh() => RefreshRequests++;

        public void RequestRefresh(string backendId) => RefreshRequests++;

        public void RequestRefresh(ConfigurationBackend backend, IReadOnlyCollection<string>? devices) => RefreshRequests++;
    }

    private static DataService CreateDataService(FakeBackendRequirementState requirementState, ModelRuntime modelRuntime) =>
        new(
            null!,
            null!,
            null!,
            null!,
            null!,
            new OpenVinoDiagnosticsService(),
            new OpenVinoDriverInstaller(),
            modelRuntime,
            requirementMonitor: requirementState);
}
