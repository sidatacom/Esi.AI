using Esi.AI.Core.ModelLoading;
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
}