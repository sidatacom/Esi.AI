using Esi.AI.Core.ModelLoading;
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
}