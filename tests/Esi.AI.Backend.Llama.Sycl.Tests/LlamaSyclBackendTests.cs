using System.Text.Json;
using Esi.AI.Backend.Abstractions;
using Esi.AI.Backend.Llama.Sycl;
using Esi.AI.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Backend.Llama.Sycl.Tests;

[TestClass]
public sealed class LlamaSyclBackendTests
{
    [TestMethod]
    public void Descriptor_WhenModuleIsCreated_UsesStableSyclIdentity()
    {
        var descriptor = new LlamaSyclBackendModule().Descriptor;

        Assert.AreEqual("llama.sycl", descriptor.Id);
        Assert.AreEqual(ConfigurationBackend.Llama, descriptor.Family);
        Assert.AreEqual("SYCL", descriptor.Route);
    }

    [TestMethod]
    public void AddServices_WhenModuleIsRegistered_ProvidesUnifiedBackendRuntime()
    {
        var services = new ServiceCollection();
        new LlamaSyclBackendModule().AddServices(services);
        using var provider = services.BuildServiceProvider();

        var runtime = provider.GetRequiredService<IBackendRuntime>();

        Assert.IsInstanceOfType<LlamaSyclRuntime>(runtime);
        Assert.AreEqual("llama.sycl", runtime.Descriptor.Id);
    }

    [TestMethod]
    public async Task LoadAsync_WhenVariantDoesNotMatch_RejectsBeforeNativeInitialization()
    {
        using var runtime = new LlamaSyclRuntime();
        using var configuration = JsonDocument.Parse("{}");
        var request = new BackendLoadRequest("missing.gguf", "llama.vulkan", configuration.RootElement);

        var exception = await Assert.ThrowsExactlyAsync<ArgumentException>(() => runtime.LoadAsync(request));

        Assert.Contains("llama.sycl", exception.Message);
    }

    [TestMethod]
    public async Task LoadAsync_WhenSyclLibraryIsMissing_RejectsBeforeNativeInitialization()
    {
        var applicationDirectory = Path.Combine(Path.GetTempPath(), $"esi-ai-sycl-{Guid.NewGuid():N}");
        var runtimeDirectory = Path.Combine(applicationDirectory, "runtimes", "linux-x64", "native", "sycl");
        var modelPath = Path.Combine(Path.GetTempPath(), $"esi-ai-sycl-{Guid.NewGuid():N}.gguf");
        Directory.CreateDirectory(runtimeDirectory);
        await File.WriteAllTextAsync(modelPath, "not a model");

        try
        {
            using var runtime = new LlamaSyclRuntime(applicationDirectory);
            var configuration = new LoadModelRequest(modelPath, "SYCL", -1, 4096, new Dictionary<string, float>(), null);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(configuration));
            var request = new BackendLoadRequest(modelPath, "llama.sycl", json.RootElement);

            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => runtime.LoadAsync(request));

            Assert.Contains("libggml-base.so", exception.Message);
            Assert.Contains("libggml-sycl.so", exception.Message);
        }
        finally
        {
            File.Delete(modelPath);
            Directory.Delete(applicationDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void PrepareSyclRuntimeEnvironment_WhenOldDriverBundleExists_PreservesSystemDriverSelection()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Inconclusive("The SYCL runtime environment is configured only on Linux.");

        var runtimeDirectory = Path.Combine(Path.GetTempPath(), $"esi-ai-sycl-env-{Guid.NewGuid():N}");
        Directory.CreateDirectory(runtimeDirectory);
        var originalLibraryPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
        var originalSelector = Environment.GetEnvironmentVariable("ONEAPI_DEVICE_SELECTOR");
        var originalAdapter = Environment.GetEnvironmentVariable("UR_ADAPTERS_FORCE_LOAD");
        var originalDriver = Environment.GetEnvironmentVariable("ZE_ENABLE_ALT_DRIVERS");
        var originalSysman = Environment.GetEnvironmentVariable("ZES_ENABLE_SYSMAN");

        try
        {
            File.WriteAllBytes(Path.Combine(runtimeDirectory, "libze_loader.so.1"), []);
            File.WriteAllBytes(Path.Combine(runtimeDirectory, "libze_intel_gpu.so.1"), []);
            File.WriteAllBytes(Path.Combine(runtimeDirectory, "libur_adapter_level_zero.so"), []);
            var adapterPath = Path.Combine(runtimeDirectory, "libur_adapter_level_zero_v2.so.0");
            File.WriteAllBytes(adapterPath, []);
            Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", "system-library-path");
            Environment.SetEnvironmentVariable("ONEAPI_DEVICE_SELECTOR", null);
            Environment.SetEnvironmentVariable("UR_ADAPTERS_FORCE_LOAD", null);
            Environment.SetEnvironmentVariable("ZE_ENABLE_ALT_DRIVERS", null);
            Environment.SetEnvironmentVariable("ZES_ENABLE_SYSMAN", null);

            SyclRuntimeFiles.PrepareSyclRuntimeEnvironment(runtimeDirectory);

            Assert.AreEqual("system-library-path", Environment.GetEnvironmentVariable("LD_LIBRARY_PATH"));
            Assert.AreEqual("level_zero:gpu", Environment.GetEnvironmentVariable("ONEAPI_DEVICE_SELECTOR"));
            Assert.AreEqual(adapterPath, Environment.GetEnvironmentVariable("UR_ADAPTERS_FORCE_LOAD"));
            Assert.IsNull(Environment.GetEnvironmentVariable("ZE_ENABLE_ALT_DRIVERS"));
            Assert.AreEqual("1", Environment.GetEnvironmentVariable("ZES_ENABLE_SYSMAN"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", originalLibraryPath);
            Environment.SetEnvironmentVariable("ONEAPI_DEVICE_SELECTOR", originalSelector);
            Environment.SetEnvironmentVariable("UR_ADAPTERS_FORCE_LOAD", originalAdapter);
            Environment.SetEnvironmentVariable("ZE_ENABLE_ALT_DRIVERS", originalDriver);
            Environment.SetEnvironmentVariable("ZES_ENABLE_SYSMAN", originalSysman);
            Directory.Delete(runtimeDirectory, recursive: true);
        }
    }
}