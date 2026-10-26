using System.Text.Json;
using Esi.AI.Backend.Abstractions;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Core.Tests;

[TestClass]
public sealed class ModelRuntimeTests
{
    [TestMethod]
    public async Task StopAsync_WhenNoModelsAreLoaded_LeavesAllRuntimesUnloaded()
    {
        using var llama = new LlamaModelLoader();
        using var openVino = new OpenVinoModelLoader();
        using var loader = new ModelRuntime(llama, openVino);

        await loader.StopAsync();

        Assert.IsFalse(loader.LoadedModel_Read().IsModelLoaded);
        Assert.IsFalse(loader.GetOpenVinoStatus().IsModelLoaded);
    }

    [TestMethod]
    public async Task StopAsync_WhenBackendRuntimeStopFails_AggregatesFailureAfterPublishingDelete()
    {
        var runtime = new RecordingBackendRuntime { ThrowOnStop = true };
        var publisher = new RecordingStatusPublisher();
        using var modelRuntime = new ModelRuntime(
            new LlamaModelLoader(),
            new OpenVinoModelLoader(),
            new PythonInferenceServer(),
            statusPublisher: publisher,
            backendRuntimeResolver: new BackendRuntimeResolver([runtime]));

        var exception = await Assert.ThrowsExceptionAsync<AggregateException>(() => modelRuntime.StopAsync());

        Assert.AreEqual(1, exception.InnerExceptions.Count);
        CollectionAssert.AreEqual(new[] { "delete" }, publisher.Events);
    }

    [TestMethod]
    public async Task PackagedRuntime_LoadGenerateAndUnload_UsesSelectedVariant()
    {
        var runtime = new RecordingBackendRuntime();
        using var modelRuntime = new ModelRuntime(
            new LlamaModelLoader(),
            new OpenVinoModelLoader(),
            new PythonInferenceServer(),
            backendRuntimeResolver: new BackendRuntimeResolver([runtime]));
        const string modelPath = "/models/variant.gguf";
        var loadRequest = new BackendLoadRequest(modelPath, runtime.Descriptor.Id, JsonSerializer.SerializeToElement(new { }));

        await modelRuntime.LoadBackendAsync(loadRequest);
        Assert.AreEqual(runtime.Descriptor.Id, modelRuntime.LoadedModel_Read().LoadedModels.Single().BackendVariantId);

        var chatRequest = new OpenAiBackendChatRequest(
            "CUDA",
            "variant",
            modelPath,
            [],
            [new ChatMessage("user", "hello")],
            null,
            new ChatGenerationOptions(),
            BackendVariantId: runtime.Descriptor.Id);
        var generated = await modelRuntime.GenerateBackendAsync(chatRequest);

        Assert.AreSame(chatRequest, runtime.LastRequest);
        Assert.AreEqual("fake response", generated.Text);
        await modelRuntime.UnloadAsync(modelPath, ConfigurationBackend.Llama);
        Assert.IsFalse(modelRuntime.LoadedModel_Read().LoadedModels.Any());
    }

    [TestMethod]
    public async Task GetLoadedBackendVariantId_WhenSameModelIsLoadedByMultipleVariants_ReturnsRequestedVariant()
    {
        const string modelPath = "/models/shared.gguf";
        var vulkanRuntime = new RecordingBackendRuntime("llama.vulkan");
        var cudaRuntime = new RecordingBackendRuntime("llama.cuda12");
        using var modelRuntime = new ModelRuntime(
            new LlamaModelLoader(),
            new OpenVinoModelLoader(),
            new PythonInferenceServer(),
            backendRuntimeResolver: new BackendRuntimeResolver([vulkanRuntime, cudaRuntime]));
        using var configuration = JsonDocument.Parse("{}");

        await modelRuntime.LoadBackendAsync(new BackendLoadRequest(modelPath, "llama.vulkan", configuration.RootElement));
        await modelRuntime.LoadBackendAsync(new BackendLoadRequest(modelPath, "llama.cuda12", configuration.RootElement));

        Assert.AreEqual("llama.vulkan", modelRuntime.GetLoadedBackendVariantId(modelPath, "llama.vulkan"));
        Assert.AreEqual("llama.cuda12", modelRuntime.GetLoadedBackendVariantId(modelPath, "llama.cuda12"));
        Assert.ThrowsExactly<InvalidOperationException>(() => modelRuntime.GetLoadedBackendVariantId(modelPath));
    }

    [TestMethod]
    public async Task LoadBackendAsync_WhenStartingAnotherBackend_StopsInactiveRuntimesAndKeepsLoadedRuntimes()
    {
        var activeRuntime = new RecordingBackendRuntime("llama.vulkan");
        var inactiveRuntime = new RecordingBackendRuntime("vllm.xpu", ConfigurationBackend.Vllm);
        var requestedRuntime = new RecordingBackendRuntime("llama.cuda12");
        using var modelRuntime = new ModelRuntime(
            new LlamaModelLoader(),
            new OpenVinoModelLoader(),
            new PythonInferenceServer(),
            backendRuntimeResolver: new BackendRuntimeResolver([activeRuntime, inactiveRuntime, requestedRuntime]));
        using var configuration = JsonDocument.Parse("{}");

        await modelRuntime.LoadBackendAsync(new BackendLoadRequest("/models/active.gguf", activeRuntime.Descriptor.Id, configuration.RootElement));
        await modelRuntime.LoadBackendAsync(new BackendLoadRequest("/models/requested.gguf", requestedRuntime.Descriptor.Id, configuration.RootElement));

        Assert.AreEqual(2, inactiveRuntime.StopCount);
        Assert.AreEqual(0, activeRuntime.StopCount);
        Assert.IsTrue(activeRuntime.GetStatus().IsModelLoaded);
        Assert.IsTrue(requestedRuntime.GetStatus().IsModelLoaded);
    }

    [TestMethod]
    public async Task LoadBackendAsync_WhenInactiveRuntimeCannotStop_DoesNotStartRequestedRuntime()
    {
        var inactiveRuntime = new RecordingBackendRuntime("vllm.xpu", ConfigurationBackend.Vllm)
        {
            ThrowOnStop = true
        };
        var requestedRuntime = new RecordingBackendRuntime("llama.cuda12");
        using var modelRuntime = new ModelRuntime(
            new LlamaModelLoader(),
            new OpenVinoModelLoader(),
            new PythonInferenceServer(),
            backendRuntimeResolver: new BackendRuntimeResolver([inactiveRuntime, requestedRuntime]));
        using var configuration = JsonDocument.Parse("{}");

        var exception = await Assert.ThrowsExceptionAsync<AggregateException>(() =>
            modelRuntime.LoadBackendAsync(new BackendLoadRequest("/models/requested.gguf", requestedRuntime.Descriptor.Id, configuration.RootElement)));

        Assert.Contains("Could not stop unused backend runtimes", exception.Message);
        Assert.AreEqual(0, requestedRuntime.LoadCount);
    }

    [TestMethod]
    public async Task LoadBackendAsync_WhenAnotherBackendIsLoading_DoesNotStopItsRuntime()
    {
        var loadingRuntime = new RecordingBackendRuntime("llama.vulkan")
        {
            WaitForLoadRelease = true
        };
        var requestedRuntime = new RecordingBackendRuntime("vllm.xpu", ConfigurationBackend.Vllm);
        using var modelRuntime = new ModelRuntime(
            new LlamaModelLoader(),
            new OpenVinoModelLoader(),
            new PythonInferenceServer(),
            backendRuntimeResolver: new BackendRuntimeResolver([loadingRuntime, requestedRuntime]));
        using var configuration = JsonDocument.Parse("{}");

        var pendingLoad = modelRuntime.LoadBackendAsync(
            new BackendLoadRequest("/models/loading.gguf", loadingRuntime.Descriptor.Id, configuration.RootElement));
        await loadingRuntime.LoadStarted.Task;

        try
        {
            await modelRuntime.LoadBackendAsync(
                new BackendLoadRequest("/models/requested.gguf", requestedRuntime.Descriptor.Id, configuration.RootElement));

            Assert.AreEqual(0, loadingRuntime.StopCount);
        }
        finally
        {
            loadingRuntime.ReleaseLoad.TrySetResult(true);
            await pendingLoad;
        }
    }

    [TestMethod]
    public void SupportsImageInput_WhenBackendIsUnknown_ReturnsFalse()
    {
        using var loader = new ModelRuntime();

        Assert.IsFalse(loader.SupportsImageInput("unknown", null));
    }

    [TestMethod]
    public void ModelLifecycleCoordinator_WhenOperationTransitions_StoresLatestState()
    {
        var coordinator = new ModelLifecycleCoordinator();
        const string modelPath = "model.gguf";

        coordinator.Begin(modelPath, ConfigurationBackend.Llama, "CUDA");
        coordinator.Complete(modelPath, ConfigurationBackend.Llama, "CUDA");

        var state = coordinator.Read(modelPath, ConfigurationBackend.Llama);

        Assert.IsNotNull(state);
        Assert.AreEqual(ModelLifecyclePhase.Loaded, state.Phase);
        Assert.AreEqual("CUDA", state.Runtime);
        Assert.IsNull(state.Error);
    }

    [TestMethod]
    public void ModelLifecycleCoordinator_WhenOperationFails_StoresDiagnostic()
    {
        var coordinator = new ModelLifecycleCoordinator();

        coordinator.Fail("model.gguf", ConfigurationBackend.Llama, "CUDA", "load failed");

        var state = coordinator.Read("model.gguf", ConfigurationBackend.Llama);

        Assert.IsNotNull(state);
        Assert.AreEqual(ModelLifecyclePhase.Failed, state.Phase);
        Assert.AreEqual("load failed", state.Error);
    }

    [TestMethod]
    public void OpenVinoLoadGate_WhenAlreadyEntered_RejectsSecondLoadUntilReleased()
    {
        var gate = new OpenVinoLoadGate();

        Assert.IsTrue(gate.TryEnter());
        Assert.IsFalse(gate.TryEnter());

        gate.Exit();

        Assert.IsTrue(gate.TryEnter());
    }

    [TestMethod]
    public async Task LoadAsync_WhenOpenVinoModelPathIsMissing_PublishesCreateUpdateAndDelete()
    {
        var publisher = new RecordingStatusPublisher();
        using var loader = new ModelRuntime(
            new LlamaModelLoader(),
            new OpenVinoModelLoader(),
            new PythonInferenceServer(),
            statusPublisher: publisher);
        var modelPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        await Assert.ThrowsExceptionAsync<FileNotFoundException>(() => loader.LoadAsync(new OpenVinoLoadRequest(modelPath, "GPU.1")));

        CollectionAssert.AreEqual(new[] { "create", "update", "delete" }, publisher.Events);
        Assert.IsTrue(publisher.Statuses[0].LoadedModels.Any(model => model.ModelPath == modelPath && model.IsLoading));
        Assert.IsFalse(publisher.Statuses[1].LoadedModels.Any(model => model.ModelPath == modelPath));
    }

    [TestMethod]
    public async Task LoadAsync_WhenCreatePublicationFails_StillAttemptsBackendLoad()
    {
        var publisher = new RecordingStatusPublisher { ThrowOnCreate = true };
        using var loader = new ModelRuntime(
            new LlamaModelLoader(),
            new OpenVinoModelLoader(),
            new PythonInferenceServer(),
            statusPublisher: publisher);
        var modelPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        await Assert.ThrowsExceptionAsync<FileNotFoundException>(() => loader.LoadAsync(new OpenVinoLoadRequest(modelPath, "GPU.1")));

        CollectionAssert.AreEqual(new[] { "create", "update", "delete" }, publisher.Events);
    }

    [TestMethod]
    public async Task LoadAsync_WhenUpdatePublicationFails_PreservesBackendFailureAndPublishesDelete()
    {
        var publisher = new RecordingStatusPublisher { ThrowOnUpdate = true };
        using var loader = new ModelRuntime(
            new LlamaModelLoader(),
            new OpenVinoModelLoader(),
            new PythonInferenceServer(),
            statusPublisher: publisher);
        var modelPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        await Assert.ThrowsExceptionAsync<FileNotFoundException>(() => loader.LoadAsync(new OpenVinoLoadRequest(modelPath, "GPU.1")));

        CollectionAssert.AreEqual(new[] { "create", "update", "delete" }, publisher.Events);
    }

    private sealed class RecordingStatusPublisher : IModelRuntimeStatusPublisher
    {
        public List<string> Events { get; } = [];

        public List<ModelLoadStatus> Statuses { get; } = [];

        public bool ThrowOnCreate { get; init; }

        public bool ThrowOnUpdate { get; init; }

        public bool ThrowOnDelete { get; init; }

        public Task LoadedModel_CreateAsync(ModelLoadStatus status, CancellationToken cancellationToken = default)
        {
            Events.Add("create");
            Statuses.Add(status);
            if (ThrowOnCreate)
                throw new InvalidOperationException("create publication failed");
            return Task.CompletedTask;
        }

        public Task LoadedModel_UpdateAsync(ModelLoadStatus status, CancellationToken cancellationToken = default)
        {
            Events.Add("update");
            Statuses.Add(status);
            if (ThrowOnUpdate)
                throw new InvalidOperationException("update publication failed");
            return Task.CompletedTask;
        }

        public Task LoadedModel_DeleteAsync(ModelLoadStatus status, CancellationToken cancellationToken = default)
        {
            Events.Add("delete");
            Statuses.Add(status);
            if (ThrowOnDelete)
                throw new InvalidOperationException("delete publication failed");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingBackendRuntime(
        string variantId = "llama.test",
        ConfigurationBackend backend = ConfigurationBackend.Llama) : IBackendRuntime
    {
        private string? modelPath;

        public BackendVariantDescriptor Descriptor { get; } = new(
            variantId,
            backend,
            variantId,
            $"Test {variantId}",
            "CUDA");

        public OpenAiBackendChatRequest? LastRequest { get; private set; }

        public bool ThrowOnStop { get; init; }

        public bool WaitForLoadRelease { get; init; }

        public int LoadCount { get; private set; }

        public int StopCount { get; private set; }

        public TaskCompletionSource<bool> LoadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> ReleaseLoad { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ModelLoadStatus GetStatus()
        {
            var loadedModels = modelPath is null
                ? Array.Empty<LoadedModelStatus>()
                : [new LoadedModelStatus(modelPath, backend, Descriptor.RuntimeName, 0, 0, 0, [], null, BackendVariantId: Descriptor.Id)];
            return new ModelLoadStatus(modelPath, Descriptor.Route, 0, 0, 0, 0, [], null, string.Empty, new Dictionary<string, float>(), loadedModels.Length > 0, loadedModels, Descriptor.Id);
        }

        public bool SupportsImageInput(string? path) => false;

        public async Task LoadAsync(BackendLoadRequest request, CancellationToken cancellationToken = default)
        {
            LoadCount++;
            LoadStarted.TrySetResult(true);
            if (WaitForLoadRelease)
                await ReleaseLoad.Task.WaitAsync(cancellationToken);
            modelPath = request.ModelPath;
        }

        public Task UnloadAsync(string path, CancellationToken cancellationToken = default)
        {
            modelPath = null;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            modelPath = null;
            return ThrowOnStop
                ? Task.FromException(new InvalidOperationException("stop failed"))
                : Task.CompletedTask;
        }

        public Task<GenerationResult> GenerateAsync(OpenAiBackendChatRequest request, Func<string, Task>? onToken = null, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new GenerationResult("fake response", 2, TimeSpan.Zero, 0));
        }

        public void Dispose()
        {
        }
    }
}