using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Core.Tests;

[TestClass]
public sealed class PythonBackendProvisionerTests
{
    [TestMethod]
    public void GetDefaultEnvironmentPath_Vllm_UsesVllmSpecificDirectory()
    {
        var originalRoot = Environment.GetEnvironmentVariable("ESI_PYTHON_ENV_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("ESI_PYTHON_ENV_ROOT", Path.Combine(Path.GetTempPath(), "esi-ai-test-root"));

            var path = PythonBackendProvisioner.GetDefaultEnvironmentPath(ConfigurationBackend.Vllm);

            StringAssert.EndsWith(path, Path.Combine("esi-ai-test-root", "esi-ai-vllm"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ESI_PYTHON_ENV_ROOT", originalRoot);
        }
    }

    [TestMethod]
    public void GetDefaultEnvironmentPath_VllmXpu_UsesXpuSpecificDirectory()
    {
        var originalRoot = Environment.GetEnvironmentVariable("ESI_PYTHON_ENV_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("ESI_PYTHON_ENV_ROOT", Path.Combine(Path.GetTempPath(), "esi-ai-test-root"));

            var path = PythonBackendProvisioner.GetDefaultEnvironmentPath(ConfigurationBackend.Vllm, ["xpu:1"]);

            StringAssert.EndsWith(path, Path.Combine("esi-ai-test-root", "esi-ai-vllm-xpu"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ESI_PYTHON_ENV_ROOT", originalRoot);
        }
    }

    [TestMethod]
    public void GetDefaultEnvironmentPath_MixedDeviceVendors_ThrowsArgumentException()
    {
        Assert.ThrowsException<ArgumentException>(() => PythonBackendProvisioner.GetDefaultEnvironmentPath(
            ConfigurationBackend.Vllm,
            ["cuda:0", "xpu:1"]));
    }

    [TestMethod]
    public async Task DiagnoseAsync_XpuRoute_MakesXpuRequired()
    {
        var result = await new PythonBackendProvisioner().DiagnoseAsync(
            ConfigurationBackend.Vllm,
            "/missing/python",
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            TimeSpan.FromSeconds(5),
            devices: ["xpu:1"]);

        var cudaCheck = result.Checks.Single(check => check.Id == "cuda-runtime");
        var xpuCheck = result.Checks.Single(check => check.Id == "xpu-runtime");

        Assert.IsTrue(cudaCheck.IsOptional);
        Assert.IsFalse(xpuCheck.IsOptional);
        Assert.IsFalse(result.IsReady);
    }

    [TestMethod]
    public async Task DiagnoseAsync_MixedDeviceVendors_ReturnsRoutingFailure()
    {
        var result = await new PythonBackendProvisioner().DiagnoseAsync(
            ConfigurationBackend.Vllm,
            "/missing/python",
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            TimeSpan.FromSeconds(5),
            devices: ["cuda:0", "xpu:1"]);

        var routingCheck = result.Checks.Single(check => check.Id == "device-routing");

        Assert.IsFalse(result.IsReady);
        Assert.IsFalse(routingCheck.IsAvailable);
        StringAssert.Contains(routingCheck.Detail, "cannot use the same Python environment");
    }

    [TestMethod]
    public void GetDefaultEnvironmentPath_Sglang_UsesSglangSpecificDirectory()
    {
        var originalRoot = Environment.GetEnvironmentVariable("ESI_PYTHON_ENV_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("ESI_PYTHON_ENV_ROOT", Path.Combine(Path.GetTempPath(), "esi-ai-test-root"));

            var path = PythonBackendProvisioner.GetDefaultEnvironmentPath(ConfigurationBackend.Sglang);

            StringAssert.EndsWith(path, Path.Combine("esi-ai-test-root", "esi-ai-sglang-cuda"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ESI_PYTHON_ENV_ROOT", originalRoot);
        }
    }

    [TestMethod]
    public async Task PrepareAsync_InvalidBackend_ThrowsArgumentException()
    {
        var provisioner = new PythonBackendProvisioner();

        await Assert.ThrowsExceptionAsync<ArgumentException>(() => provisioner.PrepareAsync(
            ConfigurationBackend.OpenVino,
            "python3",
            AppContext.BaseDirectory,
            TimeSpan.FromSeconds(5)));
    }
}