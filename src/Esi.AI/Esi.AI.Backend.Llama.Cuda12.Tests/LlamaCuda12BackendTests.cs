using System.Text.Json;
using Esi.AI.Backend.Abstractions;
using Esi.AI.Backend.Llama.Cuda12;
using Esi.AI.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Backend.Llama.Cuda12.Tests;

[TestClass]
public sealed class LlamaCuda12BackendTests
{
    [TestMethod]
    public void Descriptor_WhenModuleIsCreated_UsesStableCuda12Identity()
    {
        var descriptor = new LlamaCuda12BackendModule().Descriptor;

        Assert.AreEqual("llama.cuda12", descriptor.Id);
        Assert.AreEqual(ConfigurationBackend.Llama, descriptor.Family);
        Assert.AreEqual("CUDA", descriptor.Route);
    }

    [TestMethod]
    public void AddServices_WhenModuleIsRegistered_ProvidesUnifiedBackendRuntime()
    {
        var services = new ServiceCollection();
        new LlamaCuda12BackendModule().AddServices(services);
        using var provider = services.BuildServiceProvider();

        var runtime = provider.GetRequiredService<IBackendRuntime>();

        Assert.IsInstanceOfType<LlamaCuda12Runtime>(runtime);
        Assert.AreEqual("llama.cuda12", runtime.Descriptor.Id);
    }

    [TestMethod]
    public async Task LoadAsync_WhenVariantDoesNotMatch_RejectsBeforeNativeInitialization()
    {
        using var runtime = new LlamaCuda12Runtime();
        using var configuration = JsonDocument.Parse("{}");
        var request = new BackendLoadRequest("missing.gguf", "llama.vulkan", configuration.RootElement);

        var exception = await Assert.ThrowsExactlyAsync<ArgumentException>(() => runtime.LoadAsync(request));

        Assert.Contains("llama.cuda12", exception.Message);
    }

    [TestMethod]
    public async Task LoadAsync_WhenModelPathIsEmpty_RejectsBeforeNativeInitialization()
    {
        using var runtime = new LlamaCuda12Runtime();
        using var configuration = JsonDocument.Parse("{}");
        var request = new BackendLoadRequest(" ", "llama.cuda12", configuration.RootElement);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => runtime.LoadAsync(request));
    }

    [TestMethod]
    public void Validate_WhenRuntimeDirectoryIsMissingCudaLibrary_ThrowsWithMissingFile()
    {
        var runtimeDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runtimeDirectory);
        try
        {
            foreach (var file in new[] { "libllama.so", "libggml.so", "libggml-base.so", "libmtmd.so" })
                File.WriteAllBytes(Path.Combine(runtimeDirectory, file), []);

            var exception = Assert.ThrowsExactly<InvalidOperationException>(() => Cuda12RuntimeFiles.Validate(runtimeDirectory));

            Assert.Contains("libggml-cuda.so", exception.Message);
        }
        finally
        {
            Directory.Delete(runtimeDirectory, recursive: true);
        }
    }
}