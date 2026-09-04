using System.Buffers.Binary;
using System.Text.Json;
using Esi.AI.Core.ModelLoading;
using Esi.AI.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenVinoSharp;
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
    public void OpenVinoImageTensorFactory_WhenBmpImageIsProvided_ReturnsRgbNhwcTensor()
    {
        OpenVinoModelLoader.InitializeRuntime();
        var messages = new[]
        {
            new ChatMessage("user", "Describe", [new ChatImage("image/bmp", CreateTwoPixelBmp())])
        };
        var tensors = OpenVinoImageTensorFactory.Create(messages);

        try
        {
            Assert.AreEqual(1, tensors.Length);
            using var shape = tensors[0].Shape;
            CollectionAssert.AreEqual(new long[] { 1, 1, 2, 3 }, shape.get_dims());
            CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 0, 255, 0 }, tensors[0].GetData<byte>(6));
        }
        finally
        {
            foreach (var tensor in tensors)
                tensor.Dispose();
        }
    }

    [TestMethod]
    public void UnloadOnNewLoader_IsHarmless()
    {
        using var loader = new OpenVinoModelLoader();

        loader.UnloadAsync().GetAwaiter().GetResult();

        Assert.IsFalse(loader.IsLoaded);
    }

    [TestMethod]
    public void ParseToolCalls_WhenQwenXmlCallIsReturned_ProducesOpenAiToolCall()
    {
        var parsed = OpenVinoChatSession.ParseToolCalls(
            "<tool_call>\n<function=lookup>\n<parameter=query>\nweather in Berlin\n</parameter>\n</function>\n</tool_call>");

        Assert.AreEqual(string.Empty, parsed.Text);
        var toolCall = parsed.ToolCalls.Single();
        Assert.AreEqual("lookup", toolCall.Function.Name);
        StringAssert.Contains(toolCall.Function.Arguments, "\"query\":\"weather in Berlin\"");
    }

    [TestMethod]
    public void ParseToolCalls_WhenJsonCallIsReturned_PreservesArguments()
    {
        var parsed = OpenVinoChatSession.ParseToolCalls(
            "Before\n<tool_call>{\"name\":\"lookup\",\"arguments\":{\"query\":\"weather\"}}</tool_call>");

        Assert.AreEqual("Before", parsed.Text);
        var toolCall = parsed.ToolCalls.Single();
        Assert.AreEqual("lookup", toolCall.Function.Name);
        Assert.AreEqual("{\"query\":\"weather\"}", toolCall.Function.Arguments);
    }

    [TestMethod]
    public void SerializeChatMessageForHistory_NormalizesAssistantToolArgumentsToObject()
    {
        var message = new OpenAiChatMessage(
            "assistant",
            ToolCalls:
            [
                new OpenAiToolCall(
                    "call_1",
                    "function",
                    new OpenAiToolCallFunction("lookup", "{\"query\":\"weather\"}"))
            ]);

        using var document = JsonDocument.Parse(OpenVinoChatSession.SerializeChatMessageForHistory(message));
        Assert.AreEqual(string.Empty, document.RootElement.GetProperty("content").GetString());
        var arguments = document.RootElement
            .GetProperty("tool_calls")[0]
            .GetProperty("function")
            .GetProperty("arguments");

        Assert.AreEqual(JsonValueKind.Object, arguments.ValueKind);
        Assert.AreEqual("weather", arguments.GetProperty("query").GetString());
    }

    [TestMethod]
    public void SerializeChatMessageForHistory_WhenMultimodalContentIsProvided_PreservesContentParts()
    {
        using var content = JsonDocument.Parse("[{\"type\":\"text\",\"text\":\"Describe\"},{\"type\":\"image_url\",\"image_url\":{\"url\":\"data:image/png;base64,AA==\"}}]");
        var message = new OpenAiChatMessage("user", content.RootElement.Clone());

        using var document = JsonDocument.Parse(OpenVinoChatSession.SerializeChatMessageForHistory(message));
        var parts = document.RootElement.GetProperty("content");

        Assert.AreEqual(JsonValueKind.Array, parts.ValueKind);
        Assert.AreEqual("image_url", parts[1].GetProperty("type").GetString());
    }

    [TestMethod]
    public void CreateChatTemplateContext_WhenReasoningIsDisabled_DisablesThinking()
    {
        using var document = JsonDocument.Parse(OpenVinoChatSession.CreateChatTemplateContext("none")!);

        Assert.IsFalse(document.RootElement.GetProperty("enable_thinking").GetBoolean());
    }

    [TestMethod]
    public void CreateChatTemplateContext_WhenCloudEffortIsHigh_UsesSupportedNativeEffort()
    {
        using var document = JsonDocument.Parse(OpenVinoChatSession.CreateChatTemplateContext("high")!);

        Assert.IsTrue(document.RootElement.GetProperty("enable_thinking").GetBoolean());
        Assert.AreEqual("xhigh", document.RootElement.GetProperty("reasoning_effort").GetString());
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

    [TestMethod]
    [TestCategory("OpenVINO.Integration")]
    public void LoadAsync_WithConfiguredRealModel_StreamsGeneratedText()
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
                    MaxNewTokens: 64,
                    Temperature: 0,
                    TopP: 1,
                    DoSample: false,
                    TopK: 1,
                    RepetitionPenalty: 1))
            .GetAwaiter()
            .GetResult();

        using var session = loader.CreateChatSession();
        var chunks = new List<string>();
        var result = session.GenerateWithStats(
            "Say exactly: OpenVINO streaming test passed.",
            chunks.Add);

        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Text));
        Assert.IsTrue(chunks.Count > 0);
        Assert.IsFalse(string.IsNullOrWhiteSpace(string.Concat(chunks)));
    }

    private static string CreateTemporaryGgufFile()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"esi-ai-test-{Guid.NewGuid():N}.gguf");
        File.WriteAllBytes(filePath, Array.Empty<byte>());
        return filePath;
    }

    private static byte[] CreateTwoPixelBmp()
    {
        var bytes = new byte[62];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), 24);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(34), 8);
        new byte[] { 0, 0, 255, 0, 255, 0, 0, 0 }.AsSpan().CopyTo(bytes.AsSpan(54));
        return bytes;
    }
}
