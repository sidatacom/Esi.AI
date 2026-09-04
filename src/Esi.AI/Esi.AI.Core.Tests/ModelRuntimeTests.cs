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
}