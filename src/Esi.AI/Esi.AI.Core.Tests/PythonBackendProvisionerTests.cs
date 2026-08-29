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
    public void GetDefaultEnvironmentPath_Sglang_UsesSglangSpecificDirectory()
    {
        var originalRoot = Environment.GetEnvironmentVariable("ESI_PYTHON_ENV_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("ESI_PYTHON_ENV_ROOT", Path.Combine(Path.GetTempPath(), "esi-ai-test-root"));

            var path = PythonBackendProvisioner.GetDefaultEnvironmentPath(ConfigurationBackend.Sglang);

            StringAssert.EndsWith(path, Path.Combine("esi-ai-test-root", "esi-ai-sglang"));
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