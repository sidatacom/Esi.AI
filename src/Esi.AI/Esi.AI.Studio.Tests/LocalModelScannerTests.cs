using Esi.AI.Models;
using Esi.AI.Studio.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Studio.Tests;

[TestClass]
public sealed class LocalModelScannerTests
{
    [TestMethod]
    public async Task ScanAsync_WhenConfiguredFormatsExist_ReturnsDetectedModels()
    {
        var root = Directory.CreateTempSubdirectory("esi-ai-model-scanner-");
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root.FullName, "model.gguf"), [0x47, 0x47, 0x55, 0x46]);

            var openVinoDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "openvino-model"));
            await File.WriteAllTextAsync(Path.Combine(openVinoDirectory.FullName, "openvino_language_model.xml"), "<net/>");

            var transformersDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "transformers-model"));
            await File.WriteAllTextAsync(Path.Combine(transformersDirectory.FullName, "config.json"), "{}");
            await File.WriteAllBytesAsync(Path.Combine(transformersDirectory.FullName, "model.safetensors"), [1, 2, 3]);

            var scanner = new LocalModelScanner();
            var models = await scanner.ScanAsync([root.FullName]);

            Assert.AreEqual(3, models.Count);
            CollectionAssert.AreEquivalent(
                new[] { ReferenceModelFormat.Gguf, ReferenceModelFormat.OpenVinoIr, ReferenceModelFormat.Transformers },
                models.Select(model => model.Format).ToArray());
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [TestMethod]
    public async Task ScanAsync_WhenCancellationIsRequested_ThrowsOperationCanceledException()
    {
        var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            new LocalModelScanner().ScanAsync([], cancellation.Token));
    }
}