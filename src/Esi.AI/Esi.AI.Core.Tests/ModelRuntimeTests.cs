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
            new DotLlmInProcessRuntime(),
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
            new DotLlmInProcessRuntime(),
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
            new DotLlmInProcessRuntime(),
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
}