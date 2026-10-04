using Esi.AI.Models;
using Esi.AI.Studio.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Studio.Tests;

[TestClass]
public sealed class BackendRequirementMonitorTests
{
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
}
