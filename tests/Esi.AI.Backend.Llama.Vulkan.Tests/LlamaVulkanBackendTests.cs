using System.Text.Json;
using Esi.AI.Backend.Abstractions;
using Esi.AI.Backend.Llama.Vulkan;
using Esi.AI.Models;
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

    [TestMethod]
    public void GetStatus_BeforeDiscoverDevices_DoesNotAccessNativeDevices()
    {
        using var runtime = new LlamaVulkanRuntime();

        var status = runtime.GetStatus();

        Assert.IsEmpty(status.Devices);
    }

    [TestMethod]
    [TestCategory("LlamaVulkan.Integration")]
    public async Task GenerateAsync_WithConfiguredVulkanModel_ReturnsAndStreamsText()
    {
        var modelPath = Environment.GetEnvironmentVariable("ESI_LLAMA_VULKAN_MODEL_PATH");
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            Assert.Inconclusive("Set ESI_LLAMA_VULKAN_MODEL_PATH to run the native Vulkan runtime test.");
            return;
        }

        var device = Environment.GetEnvironmentVariable("ESI_LLAMA_VULKAN_DEVICE") ?? "Vulkan2";
        using var runtime = new LlamaVulkanRuntime();
        using var configuration = JsonSerializer.SerializeToDocument(new LoadModelRequest(
            modelPath,
            "Vulkan",
            -1,
            4096,
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase) { [device] = 1 },
            null,
            Devices: [device]));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await runtime.LoadAsync(
            new BackendLoadRequest(modelPath, "llama.vulkan", configuration.RootElement),
            timeout.Token);

        var content = "Reply with exactly: LLAMA_VULKAN_OK";
        var messages = new[] { new ChatMessage("user", content) };
        var request = new OpenAiBackendChatRequest(
            "LLama Vulkan",
            Path.GetFileName(modelPath),
            modelPath,
            [new OpenAiChatMessage("user", content)],
            messages,
            null,
            new ChatGenerationOptions(MaxTokens: 8, Temperature: 0, TopP: 1, TopK: 1, RepetitionPenalty: 1),
            BackendVariantId: "llama.vulkan");
        var streamedText = new List<string>();

        var result = await runtime.GenerateAsync(
            request,
            text =>
            {
                streamedText.Add(text);
                return Task.CompletedTask;
            },
            timeout.Token);

        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Text));
        Assert.IsNotEmpty(streamedText);
        Assert.IsFalse(string.IsNullOrWhiteSpace(string.Concat(streamedText)));
        Assert.IsTrue(result.TokenCount <= 8);
    }
}