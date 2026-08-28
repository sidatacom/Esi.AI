using Esi.AI.Core.ModelLoading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenVinoSharp.GenAI;

namespace Esi.AI.Core.Tests;

[TestClass]
public sealed class OpenVinoModelLoaderTests
{
    [TestMethod]
    public async Task LoadAsync_RejectsEmptyModelPath()
    {
        using var loader = new OpenVinoModelLoader();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => loader.LoadAsync(" "));
    }

    [TestMethod]
    public async Task LoadAsync_RejectsMissingModelPath()
    {
        using var loader = new OpenVinoModelLoader();
        var missingPath = Path.Combine(Path.GetTempPath(), $"esi-ai-missing-{Guid.NewGuid():N}.gguf");

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => loader.LoadAsync(missingPath));
    }

    [TestMethod]
    public async Task LoadAsync_RejectsUnsupportedFile()
    {
        using var loader = new OpenVinoModelLoader();
        var filePath = Path.Combine(Path.GetTempPath(), $"esi-ai-unsupported-{Guid.NewGuid():N}.bin");
        await File.WriteAllTextAsync(filePath, "not an OpenVINO model");

        try
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => loader.LoadAsync(filePath));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [TestMethod]
    public async Task LoadAsync_RejectsUnsupportedQwenVlmRuntimeBeforeNativeLoading()
    {
        using var loader = new OpenVinoModelLoader();
        var modelPath = Path.Combine(Path.GetTempPath(), $"esi-ai-qwen-vlm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(modelPath);
        await File.WriteAllTextAsync(Path.Combine(modelPath, "openvino_vision_embeddings_model.xml"), "model");
        await File.WriteAllTextAsync(Path.Combine(modelPath, "config.json"), "{\"model_type\":\"qwen3_5\"}");
        var previousRuntimeDirectory = Environment.GetEnvironmentVariable("OPENVINO_RUNTIME_DIR");
        Environment.SetEnvironmentVariable("OPENVINO_RUNTIME_DIR", modelPath);

        try
        {
            var exception = await Assert.ThrowsExactlyAsync<NotSupportedException>(() => loader.LoadAsync(modelPath, "GPU.1"));
            StringAssert.Contains(exception.Message, "OpenVINO GenAI 2026.4");
            Assert.IsFalse(loader.IsLoaded);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENVINO_RUNTIME_DIR", previousRuntimeDirectory);
            Directory.Delete(modelPath, true);
        }
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("CPU")]
    [DataRow("AUTO")]
    public async Task LoadAsync_RejectsInvalidDevice(string device)
    {
        using var loader = new OpenVinoModelLoader();
        var modelPath = CreateTemporaryGgufFile();

        try
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => loader.LoadAsync(modelPath, device));
        }
        finally
        {
            File.Delete(modelPath);
        }
    }

    [TestMethod]
    public async Task LoadAsync_RejectsInvalidNpuOptionsBeforeNativeLoading()
    {
        using var loader = new OpenVinoModelLoader();
        var modelPath = CreateTemporaryGgufFile();
        var npuOptions = new OpenVinoNpuOptions { MaxPromptLength = 0 };

        try
        {
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
                loader.LoadAsync(modelPath, "NPU", npuOptions: npuOptions));
        }
        finally
        {
            File.Delete(modelPath);
        }
    }

    [TestMethod]
    public async Task LoadAsync_ThrowsWhenAlreadyCancelled()
    {
        using var loader = new OpenVinoModelLoader();
        using var cancellation = new CancellationTokenSource();
        var modelPath = CreateTemporaryGgufFile();
        cancellation.Cancel();

        try
        {
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
                loader.LoadAsync(modelPath, cancellationToken: cancellation.Token));
        }
        finally
        {
            File.Delete(modelPath);
        }
    }

    [TestMethod]
    public void NewLoader_IsUnloaded()
    {
        using var loader = new OpenVinoModelLoader();

        Assert.IsFalse(loader.IsLoaded);
        Assert.IsNull(loader.LoadedModelPath);
        Assert.IsNull(loader.LoadedDevice);
        Assert.IsFalse(loader.GetStatus().IsModelLoaded);
    }

    [TestMethod]
    public void UnloadOnNewLoader_IsHarmless()
    {
        using var loader = new OpenVinoModelLoader();

        loader.UnloadAsync().GetAwaiter().GetResult();

        Assert.IsFalse(loader.IsLoaded);
    }

    [TestMethod]
    public void OpenVinoModelLoadException_WithNativeError_ExposesFailureContext()
    {
        var nativeException = new GenAIException(-17, "CreatePipeline", "Unknown error (-17)");
        var exception = new OpenVinoModelLoadException(
            "/models/test.gguf",
            "GPU.1",
            true,
            "LLMPipeline.Create",
            nativeException);

        Assert.AreEqual("/models/test.gguf", exception.ModelPath);
        Assert.AreEqual("GPU.1", exception.Device);
        Assert.AreEqual("GGUF", exception.ModelFormat);
        Assert.AreEqual("LLMPipeline.Create", exception.Operation);
        Assert.AreEqual(-17, exception.NativeStatusCode);
        Assert.AreEqual("CreatePipeline", exception.NativeOperation);
        Assert.AreSame(nativeException, exception.InnerException);
        StringAssert.Contains(exception.Message, "Direct GGUF loading depends on architecture support");
        StringAssert.Contains(exception.Message, "status -17");
    }

    [TestMethod]
    [TestCategory("OpenVINO.Integration")]
    public void LoadAsync_WithConfiguredRealModel_LoadsAndGenerates()
    {
        var modelPath = Environment.GetEnvironmentVariable("ESI_OPENVINO_MODEL_PATH");
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            Assert.Inconclusive("Set ESI_OPENVINO_MODEL_PATH to run the native OpenVINO model test.");
            return;
        }

        var device = Environment.GetEnvironmentVariable("ESI_OPENVINO_DEVICE");
        if (string.IsNullOrWhiteSpace(device))
            device = "GPU";

        using var loader = new OpenVinoModelLoader();
        loader.LoadAsync(
                modelPath,
                device,
                generationOptions: new OpenVinoGenerationOptions(
                    MaxNewTokens: 16,
                    Temperature: 0,
                    TopP: 1,
                    DoSample: false,
                    TopK: 1,
                    RepetitionPenalty: 1))
            .GetAwaiter()
            .GetResult();

        var status = loader.GetStatus();
        Assert.IsTrue(status.IsModelLoaded);
        Assert.AreEqual(device, status.Device);

        using var session = loader.CreateChatSession();
        var response = session.Generate("Say exactly: OpenVINO test passed.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(response));
    }

    private static string CreateTemporaryGgufFile()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"esi-ai-test-{Guid.NewGuid():N}.gguf");
        File.WriteAllBytes(filePath, Array.Empty<byte>());
        return filePath;
    }
}
