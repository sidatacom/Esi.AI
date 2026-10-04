using Esi.AI.Models;
using Esi.AI.Studio.Client.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Studio.Tests;

[TestClass]
public sealed class ClientStateStoreTests
{
    [TestMethod]
    public void LoadedModel_Update_WhenUpdateArrives_ReplacesSnapshot()
    {
        var store = new ClientStateStore();
        var status = new ModelLoadStatus("model.gguf", "CUDA", 1, 4096, 10, 0, [], null, "loaded", new Dictionary<string, float>(), true, []);

        store.LoadedModel_Update(status);

        Assert.AreSame(status, store.ActiveModels.Snapshot);
    }

    [TestMethod]
    public void ModelDownload_CreateAndDelete_WhenCrudEventsArrive_ReconcilesCollection()
    {
        var store = new ClientStateStore();
        var id = Guid.NewGuid();
        var download = new ModelDownloadUpdate(new DownloadStatus(id, "owner/model", "model.gguf", "/models", 10, 100, false, null));

        store.ModelDownload_Create(download);
        Assert.IsTrue(store.Downloads.Items.ContainsKey(id));

        store.ModelDownload_Delete(download);
        Assert.IsFalse(store.Downloads.Items.ContainsKey(id));
    }

    [TestMethod]
    public void VulkanLog_ReadCreateUpdateDelete_WhenCrudEventsArrive_ReconcilesCollection()
    {
        var store = new ClientStateStore();
        var id = Guid.NewGuid();
        var pending = new VulkanLogStatus(id, string.Empty, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var completed = pending with { LoadLog = "ggml_vulkan: Found 3 Vulkan devices:", IsLoading = false };

        store.VulkanLog_Read([]);
        store.VulkanLog_Create(pending);
        Assert.IsTrue(store.VulkanLogs.Items.ContainsKey(id));

        store.VulkanLog_Update(completed);
        Assert.AreEqual(completed.LoadLog, store.VulkanLogs.Items[id].LoadLog);

        store.VulkanLog_Delete(completed);
        Assert.IsFalse(store.VulkanLogs.Items.ContainsKey(id));
    }
}
