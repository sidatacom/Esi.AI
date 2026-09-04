using Esi.AI.Core.Chat;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Core.Tests;

[TestClass]
public sealed class LlamaModelLoaderTests
{
    [TestMethod]
    public async Task LoadAsync_RejectsEmptyModelPath()
    {
        using var loader = new LlamaModelLoader();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => loader.LoadAsync(" ", "CPU", 0));
    }

    [TestMethod]
    public async Task LoadAsync_RejectsMissingModelPath()
    {
        using var loader = new LlamaModelLoader();
        var missingPath = Path.Combine(Path.GetTempPath(), $"esi-ai-missing-{Guid.NewGuid():N}.gguf");

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => loader.LoadAsync(missingPath, "CPU", 0));
    }

    [TestMethod]
    public async Task LoadAsync_RejectsNonGgufFile()
    {
        using var loader = new LlamaModelLoader();
        var filePath = Path.Combine(Path.GetTempPath(), $"esi-ai-unsupported-{Guid.NewGuid():N}.bin");
        await File.WriteAllTextAsync(filePath, "not a GGUF model");

        try
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => loader.LoadAsync(filePath, "CPU", 0));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [TestMethod]
    public async Task LoadAsync_SyclRuntimeMissing_ThrowsBeforeNativeModelLoad()
    {
        var applicationDirectory = Path.Combine(Path.GetTempPath(), $"esi-ai-sycl-{Guid.NewGuid():N}");
        var modelPath = Path.Combine(Path.GetTempPath(), $"esi-ai-sycl-{Guid.NewGuid():N}.gguf");
        await File.WriteAllTextAsync(modelPath, "not a GGUF model");

        try
        {
            using var loader = new LlamaModelLoader(applicationDirectory);

            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => loader.LoadAsync(modelPath, "SYCL", 20));

            StringAssert.Contains(exception.Message, "SYCL 16 native runtime");
            Assert.IsFalse(loader.GetStatus().IsModelLoaded);
        }
        finally
        {
            File.Delete(modelPath);
            if (Directory.Exists(applicationDirectory))
                Directory.Delete(applicationDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void BuildMultimodalContent_WhenImageIsBetweenTextParts_PreservesImagePosition()
    {
        var message = new ChatMessage(
            "user",
            "beforeafter",
            [new ChatImage("image/png", [1])],
            [
                new ChatMessageContentPart("before"),
                new ChatMessageContentPart(ImageIndex: 0),
                new ChatMessageContentPart("after")
            ]);

        var content = LlamaChatSession.BuildMultimodalContent(message, "<media>");

        Assert.AreEqual("before<media>after", content);
    }
}