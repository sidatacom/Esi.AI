using System.Text.Json;
using Esi.AI.Backend.Abstractions;
using Esi.AI.Backend.Llama.Vulkan;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Backend.Llama.Vulkan.Tests;

[TestClass]
public sealed class LlamaVulkanBackendTests
{
    [TestMethod]
    public void Descriptor_WhenModuleIsCreated_UsesStableVulkanIdentity()
    {
        var module = new LlamaVulkanBackendModule();

        Assert.AreEqual("llama.vulkan", module.Descriptor.Id);
        Assert.AreEqual("Vulkan", module.Descriptor.Route);
    }

    [TestMethod]
    public void AddServices_WhenModuleIsRegistered_ProvidesUnifiedBackendRuntime()
    {
        var services = new ServiceCollection();
        new LlamaVulkanBackendModule().AddServices(services);
        using var provider = services.BuildServiceProvider();

        var runtime = provider.GetRequiredService<IBackendRuntime>();

        Assert.IsInstanceOfType<LlamaVulkanRuntime>(runtime);
        Assert.AreEqual("llama.vulkan", runtime.Descriptor.Id);
    }

    [TestMethod]
    public void AddBackendModule_WhenMultipleModulesAreRegistered_ResolvesEachVariant()
    {
        var services = new ServiceCollection();
        services.AddBackendModule(new LlamaVulkanBackendModule());
        services.AddSingleton<IBackendRuntime>(new TestBackendRuntime("llama.cuda12"));
        using var provider = services.BuildServiceProvider();

        var resolver = provider.GetRequiredService<IBackendRuntimeResolver>();

        Assert.AreEqual(2, resolver.Runtimes.Count);
        Assert.AreEqual("llama.vulkan", resolver.Resolve("llama.vulkan").Descriptor.Id);
        Assert.AreEqual("llama.cuda12", resolver.Resolve("llama.cuda12").Descriptor.Id);
        Assert.AreEqual("llama.vulkan", resolver.Resolve(Esi.AI.Models.ConfigurationBackend.Llama, "Vulkan").Descriptor.Id);
    }

    private sealed class TestBackendRuntime(string variantId) : IBackendRuntime
    {
        public BackendVariantDescriptor Descriptor { get; } = new(
            variantId,
            Esi.AI.Models.ConfigurationBackend.Llama,
            variantId,
            variantId,
            variantId);

        public Esi.AI.Models.ModelLoadStatus GetStatus() => throw new NotSupportedException();

        public bool SupportsImageInput(string? modelPath) => false;

        public Task LoadAsync(BackendLoadRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UnloadAsync(string modelPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task StopAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Esi.AI.Models.GenerationResult> GenerateAsync(
            Esi.AI.Models.OpenAiBackendChatRequest request,
            Func<string, Task>? onToken = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    [TestMethod]
    public async Task LoadAsync_WhenVariantDoesNotMatch_RejectsBeforeNativeInitialization()
    {
        using var runtime = new LlamaVulkanRuntime();
        using var configuration = JsonDocument.Parse("{}");
        var request = new BackendLoadRequest("missing.gguf", "llama.cuda12", configuration.RootElement);

        var exception = await Assert.ThrowsExactlyAsync<ArgumentException>(() => runtime.LoadAsync(request));

        Assert.Contains("llama.vulkan", exception.Message);
    }

    [TestMethod]
    public async Task LoadAsync_WhenModelPathIsEmpty_RejectsBeforeNativeInitialization()
    {
        using var runtime = new LlamaVulkanRuntime();
        using var configuration = JsonDocument.Parse("{}");
        var request = new BackendLoadRequest(" ", "llama.vulkan", configuration.RootElement);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => runtime.LoadAsync(request));
    }
}