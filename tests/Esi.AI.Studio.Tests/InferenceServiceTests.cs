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

    [TestMethod]
    public async Task GenerateAsync_WhenBackendRuntimeIsStopping_DoesNotMarkFailureFatal()
    {
        const string modelPath = "/models/shared.gguf";
        var stoppingException = new BackendRuntimeStoppingException("Runtime is reloading.");
        var backendRuntime = new TestBackendRuntime("llama.cuda12", stoppingException);
        using var modelRuntime = CreateModelRuntime(backendRuntime);
        using var configuration = JsonDocument.Parse("{}");
        await modelRuntime.LoadBackendAsync(new BackendLoadRequest(modelPath, backendRuntime.Descriptor.Id, configuration.RootElement));
        using var scheduler = new InferenceScheduler();
        var failureCoordinator = new RecordingInferenceFailureCoordinator();
        var service = new InferenceService(modelRuntime, scheduler, failureCoordinator);
        var chat = new PersistedChat(Guid.NewGuid(), "Reload test", DateTime.UtcNow, DateTime.UtcNow, []);
        var request = new ChatExchangeRequest("hello", modelPath, "CUDA", BackendVariantId: backendRuntime.Descriptor.Id);

        var exception = await Assert.ThrowsExactlyAsync<BackendRuntimeStoppingException>(
            () => service.GenerateAsync(chat, request, "CUDA"));

        Assert.AreSame(stoppingException, exception);
        Assert.AreEqual(0, failureCoordinator.FailureCount);
    }

    [TestMethod]
    public async Task OpenAiMiddleware_WhenBackendRuntimeIsStopping_DoesNotMarkFailureFatal()
    {
        const string modelPath = "/models/shared.gguf";
        var stoppingException = new BackendRuntimeStoppingException("Runtime is reloading.");
        var backendRuntime = new TestBackendRuntime("llama.cuda12", stoppingException);
        using var modelRuntime = CreateModelRuntime(backendRuntime);
        using var configuration = JsonDocument.Parse("{}");
        await modelRuntime.LoadBackendAsync(new BackendLoadRequest(modelPath, backendRuntime.Descriptor.Id, configuration.RootElement));
        using var scheduler = new InferenceScheduler();
        var failureCoordinator = new RecordingInferenceFailureCoordinator();
        var middleware = new OpenAiCompatibleBackendMiddleware(modelRuntime, scheduler, failureCoordinator);
        var request = new OpenAiBackendChatRequest(
            "CUDA",
            "shared",
            modelPath,
            [new OpenAiChatMessage("user", "hello")],
            [new ChatMessage("user", "hello")],
            null,
            new ChatGenerationOptions(),
            BackendVariantId: backendRuntime.Descriptor.Id);

        var exception = await Assert.ThrowsExactlyAsync<BackendRuntimeStoppingException>(
            () => middleware.GenerateAsync(request, null));

        Assert.AreSame(stoppingException, exception);
        Assert.AreEqual(0, failureCoordinator.FailureCount);
    }

    [TestMethod]
    public async Task OpenAiMiddleware_WhenBackendRuntimeReturnsQwenXmlToolCall_ParsesItInSharedFlow()
    {
        const string modelPath = "/models/qwen3";
        var tool = new OpenAiToolDefinition("function", new OpenAiToolFunction("lookup"));
        var backendRuntime = new TestBackendRuntime(
            "vllm.xpu",
            family: ConfigurationBackend.Vllm,
            route: "vLLM",
            generationResult: new GenerationResult(
                "<tool_call><function=lookup><parameter=query>weather</parameter></function></tool_call>",
                3,
                TimeSpan.Zero,
                0));
        using var modelRuntime = CreateModelRuntime(backendRuntime);
        using var configuration = JsonDocument.Parse("{}");
        await modelRuntime.LoadBackendAsync(new BackendLoadRequest(modelPath, backendRuntime.Descriptor.Id, configuration.RootElement));
        using var scheduler = new InferenceScheduler();
        var middleware = new OpenAiCompatibleBackendMiddleware(modelRuntime, scheduler);
        var request = middleware.Prepare(
            new OpenAiChatRequest("qwen3", [new OpenAiChatMessage("user", "Use lookup.")])
            {
                Tools = [tool]
            },
            modelRuntime.LoadedModel_Read());

        var result = await middleware.GenerateAsync(request, null);

        Assert.AreEqual(string.Empty, result.Text);
        Assert.AreEqual("tool_calls", result.FinishReason);
        Assert.AreEqual("lookup", result.ToolCalls?.Single().Function.Name);
        Assert.AreEqual("{\"query\":\"weather\"}", result.ToolCalls?.Single().Function.Arguments);
    }

    [TestMethod]
    public async Task OpenAiMiddleware_WhenBackendRuntimeReturnsStructuredToolCalls_PreservesThem()
    {
        const string modelPath = "/models/qwen3";
        var toolCalls = new[]
        {
            new OpenAiToolCall("call_1", "function", new OpenAiToolCallFunction("lookup", "{\"query\":\"weather\"}"))
        };
        var backendRuntime = new TestBackendRuntime(
            "vllm.xpu",
            family: ConfigurationBackend.Vllm,
            route: "vLLM",
            generationResult: new GenerationResult("visible text", 3, TimeSpan.Zero, 0, ToolCalls: toolCalls));
        using var modelRuntime = CreateModelRuntime(backendRuntime);
        using var configuration = JsonDocument.Parse("{}");
        await modelRuntime.LoadBackendAsync(new BackendLoadRequest(modelPath, backendRuntime.Descriptor.Id, configuration.RootElement));
        using var scheduler = new InferenceScheduler();
        var middleware = new OpenAiCompatibleBackendMiddleware(modelRuntime, scheduler);
        var request = middleware.Prepare(
            new OpenAiChatRequest("qwen3", [new OpenAiChatMessage("user", "Use lookup.")])
            {
                Tools = [new OpenAiToolDefinition("function", new OpenAiToolFunction("lookup"))]
            },
            modelRuntime.LoadedModel_Read());

        var result = await middleware.GenerateAsync(request, null);

        Assert.AreSame(toolCalls, result.ToolCalls);
        Assert.AreEqual("visible text", result.Text);
        Assert.AreEqual("tool_calls", result.FinishReason);
    }

    private static ModelRuntime CreateModelRuntime(params TestBackendRuntime[] runtimes) => new(
        new LlamaModelLoader(),
        new OpenVinoModelLoader(),
        new PythonInferenceServer(),
        backendRuntimeResolver: new BackendRuntimeResolver(runtimes));

    private sealed class TestBackendRuntime(
        string variantId,
        Exception? generationException = null,
        ConfigurationBackend family = ConfigurationBackend.Llama,
        string? route = null,
        GenerationResult? generationResult = null) : IBackendRuntime
    {
        private string? modelPath;

        public BackendVariantDescriptor Descriptor { get; } = new(
            variantId,
            family,
            variantId,
            $"Test {variantId}",
            route ?? "CUDA");

        public OpenAiBackendChatRequest? LastRequest { get; private set; }

        public ModelLoadStatus GetStatus()
        {
            var loadedModels = modelPath is null
                ? Array.Empty<LoadedModelStatus>()
                : [new LoadedModelStatus(modelPath, Descriptor.Family, Descriptor.RuntimeName, 0, 0, 0, [], null, BackendVariantId: Descriptor.Id)];
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
            if (generationException is not null)
                return Task.FromException<GenerationResult>(generationException);
            return Task.FromResult(generationResult ?? new GenerationResult($"{Descriptor.Id} response", 2, TimeSpan.Zero, 0));
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingInferenceFailureCoordinator : IInferenceFailureCoordinator
    {
        public int FailureCount { get; private set; }

        public Task FailAsync(Exception exception, string? inferenceId = null)
        {
            ArgumentNullException.ThrowIfNull(exception);
            FailureCount++;
            return Task.CompletedTask;
        }
    }
}