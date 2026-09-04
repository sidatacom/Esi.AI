using Esi.AI.Models;
using Esi.AI.Studio.Client.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Studio.Tests;

[TestClass]
public sealed class ClientStateStoreTests
{
    [TestMethod]
    public void ApplyLoadedModels_WhenUpdateArrives_ReplacesSnapshot()
    {
        var store = new ClientStateStore();
        var status = new ModelLoadStatus("model.gguf", "CUDA", 1, 4096, 10, 0, [], null, "loaded", new Dictionary<string, float>(), true, []);

        store.ApplyLoadedModels(status);

        Assert.AreSame(status, store.LoadedModels);
    }

    [TestMethod]
    public void ApplyAndRemoveDownload_WhenCrudEventsArrive_ReconcilesCollection()
    {
        var store = new ClientStateStore();
        var id = Guid.NewGuid();
        var download = new ModelDownloadUpdate(new DownloadStatus(id, "owner/model", "model.gguf", "/models", 10, 100, false, null));

        store.ApplyDownload(download);
        Assert.IsTrue(store.Downloads.ContainsKey(id));

        store.RemoveDownload(download);
        Assert.IsFalse(store.Downloads.ContainsKey(id));
    }
}
