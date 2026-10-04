using Esi.AI.Core.ModelLoading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Core.Tests;

[TestClass]
public sealed class BackendRuntimePathsTests
{
    [TestMethod]
    public void PrepareSyclRuntimeEnvironment_WhenBundledDriverExists_DoesNotForceIt()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Inconclusive("The SYCL runtime environment is configured only on Linux.");

        var applicationDirectory = Path.Combine(Path.GetTempPath(), $"esi-ai-sycl-path-{Guid.NewGuid():N}");
        var runtimeDirectory = Path.Combine(applicationDirectory, "runtimes", "linux-x64", "native", "sycl");
        Directory.CreateDirectory(runtimeDirectory);
        File.WriteAllBytes(Path.Combine(runtimeDirectory, "libze_loader.so.1"), []);
        File.WriteAllBytes(Path.Combine(runtimeDirectory, "libze_intel_gpu.so.1"), []);
        var adapterPath = Path.Combine(runtimeDirectory, "libur_adapter_level_zero_v2.so.0");
        File.WriteAllBytes(adapterPath, []);
        var originalSelector = Environment.GetEnvironmentVariable("ONEAPI_DEVICE_SELECTOR");
        var originalDriver = Environment.GetEnvironmentVariable("ZE_ENABLE_ALT_DRIVERS");
        var originalAdapter = Environment.GetEnvironmentVariable("UR_ADAPTERS_FORCE_LOAD");
        var originalSysman = Environment.GetEnvironmentVariable("ZES_ENABLE_SYSMAN");

        try
        {
            Environment.SetEnvironmentVariable("ONEAPI_DEVICE_SELECTOR", null);
            Environment.SetEnvironmentVariable("ZE_ENABLE_ALT_DRIVERS", null);
            Environment.SetEnvironmentVariable("UR_ADAPTERS_FORCE_LOAD", null);
            Environment.SetEnvironmentVariable("ZES_ENABLE_SYSMAN", null);

            BackendRuntimePaths.PrepareSyclRuntimeEnvironment(applicationDirectory);

            Assert.AreEqual("level_zero:gpu", Environment.GetEnvironmentVariable("ONEAPI_DEVICE_SELECTOR"));
            Assert.IsNull(Environment.GetEnvironmentVariable("ZE_ENABLE_ALT_DRIVERS"));
            Assert.AreEqual(adapterPath, Environment.GetEnvironmentVariable("UR_ADAPTERS_FORCE_LOAD"));
            Assert.AreEqual("1", Environment.GetEnvironmentVariable("ZES_ENABLE_SYSMAN"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ONEAPI_DEVICE_SELECTOR", originalSelector);
            Environment.SetEnvironmentVariable("ZE_ENABLE_ALT_DRIVERS", originalDriver);
            Environment.SetEnvironmentVariable("UR_ADAPTERS_FORCE_LOAD", originalAdapter);
            Environment.SetEnvironmentVariable("ZES_ENABLE_SYSMAN", originalSysman);
            Directory.Delete(applicationDirectory, recursive: true);
        }
    }
}