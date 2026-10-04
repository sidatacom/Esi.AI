using System.Text.Json;
using Esi.AI.Backend.Abstractions;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Esi.AI.Studio.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Studio.Tests;

[TestClass]
public sealed class InferenceServiceTests
{
    [TestMethod]
    public async Task GenerateAsync_WhenModelPathIsSharedAcrossVariants_UsesRequestedVariant()
    {
        const string modelPath = "/models/shared.gguf";
        var vulkanRuntime = new TestBackendRuntime("llama.vulkan");
        var cudaRuntime = new TestBackendRuntime("llama.cuda12");
        using var modelRuntime = CreateModelRuntime(vulkanRuntime, cudaRuntime);
        using var configuration = JsonDocument.Parse("{}");
        await modelRuntime.LoadBackendAsync(new BackendLoadRequest(modelPath, vulkanRuntime.Descriptor.Id, configuration.RootElement));
        await modelRuntime.LoadBackendAsync(new BackendLoadRequest(modelPath, cudaRuntime.Descriptor.Id, configuration.RootElement));
        using var scheduler = new InferenceScheduler();
        var service = new InferenceService(modelRuntime, scheduler);
        var chat = new PersistedChat(Guid.NewGuid(), "Variant test", DateTime.UtcNow, DateTime.UtcNow, []);
        var request = new ChatExchangeRequest("hello", modelPath, "CUDA", BackendVariantId: "llama.cuda12");

        var result = await service.GenerateAsync(chat, request, "CUDA");

        Assert.AreEqual("llama.cuda12 response", result.Text);
        Assert.IsNull(vulkanRuntime.LastRequest);
        Assert.AreEqual("llama.cuda12", cudaRuntime.LastRequest?.BackendVariantId);
    }

    [TestMethod]
    public async Task GenerateAsync_WhenRequestedVariantIsNotLoaded_RejectsWithoutFallback()
    {
        const string modelPath = "/models/shared.gguf";
        var vulkanRuntime = new TestBackendRuntime("llama.vulkan");
        var cudaRuntime = new TestBackendRuntime("llama.cuda12");
        using var modelRuntime = CreateModelRuntime(vulkanRuntime, cudaRuntime);
        using var configuration = JsonDocument.Parse("{}");
        await modelRuntime.LoadBackendAsync(new BackendLoadRequest(modelPath, vulkanRuntime.Descriptor.Id, configuration.RootElement));
        using var scheduler = new InferenceScheduler();
        var service = new InferenceService(modelRuntime, scheduler);
        var chat = new PersistedChat(Guid.NewGuid(), "Variant test", DateTime.UtcNow, DateTime.UtcNow, []);
        var request = new ChatExchangeRequest("hello", modelPath, "CUDA", BackendVariantId: "llama.cuda12");

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.GenerateAsync(chat, request, "CUDA"));

        Assert.Contains("llama.cuda12", exception.Message);
        Assert.IsNull(vulkanRuntime.LastRequest);
        Assert.IsNull(cudaRuntime.LastRequest);
    }

    private static ModelRuntime CreateModelRuntime(params TestBackendRuntime[] runtimes) => new(
        new LlamaModelLoader(),
        new OpenVinoModelLoader(),
        new PythonInferenceServer(),
        backendRuntimeResolver: new BackendRuntimeResolver(runtimes));

    private sealed class TestBackendRuntime(string variantId) : IBackendRuntime
    {
        private string? modelPath;

        public BackendVariantDescriptor Descriptor { get; } = new(
            variantId,
            ConfigurationBackend.Llama,
            variantId,
            $"Test {variantId}",
            "CUDA");

        public OpenAiBackendChatRequest? LastRequest { get; private set; }

        public ModelLoadStatus GetStatus()
        {
            var loadedModels = modelPath is null
                ? Array.Empty<LoadedModelStatus>()
                : [new LoadedModelStatus(modelPath, ConfigurationBackend.Llama, Descriptor.RuntimeName, 0, 0, 0, [], null, BackendVariantId: Descriptor.Id)];
            return new ModelLoadStatus(modelPath, Descriptor.Route, 0, 0, 0, 0, [], null, string.Empty, new Dictionary<string, float>(), loadedModels.Length > 0, loadedModels, Descriptor.Id);
        }

        public bool SupportsImageInput(string? path) => false;

        public Task LoadAsync(BackendLoadRequest request, CancellationToken cancellationToken = default)
        {
            modelPath = request.ModelPath;
            return Task.CompletedTask;
        }

        public Task UnloadAsync(string path, CancellationToken cancellationToken = default)
        {
            modelPath = null;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            modelPath = null;
            return Task.CompletedTask;
        }

        public Task<GenerationResult> GenerateAsync(
            OpenAiBackendChatRequest request,
            Func<string, Task>? onToken = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new GenerationResult($"{Descriptor.Id} response", 2, TimeSpan.Zero, 0));
        }

        public void Dispose()
        {
        }
    }
}