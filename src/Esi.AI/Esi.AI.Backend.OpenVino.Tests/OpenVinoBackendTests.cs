using System.Buffers.Binary;
using System.Text.Json;
using Esi.AI.Backend.Abstractions;
using Esi.AI.Backend.OpenVino;
using Esi.AI.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenVinoSharp;

namespace Esi.AI.Backend.OpenVino.Tests;

[TestClass]
public sealed class OpenVinoBackendTests
{
    [TestMethod]
    public void Descriptor_WhenModuleIsCreated_UsesStableOpenVinoIdentity()
    {
        var descriptor = new OpenVinoBackendModule().Descriptor;

        Assert.AreEqual("openvino", descriptor.Id);
        Assert.AreEqual(ConfigurationBackend.OpenVino, descriptor.Family);
        Assert.AreEqual("OpenVINO", descriptor.Route);
    }

    [TestMethod]
    public void AddServices_WhenModuleIsRegistered_ProvidesUnifiedBackendRuntime()
    {
        var services = new ServiceCollection();
        new OpenVinoBackendModule().AddServices(services);
        using var provider = services.BuildServiceProvider();

        var runtime = provider.GetRequiredService<IBackendRuntime>();

        Assert.IsInstanceOfType<OpenVinoRuntime>(runtime);
        Assert.AreEqual("openvino", runtime.Descriptor.Id);
    }

    [TestMethod]
    public async Task LoadAsync_WhenConfigurationIsOpenVinoRequest_ValidatesItsDeviceBeforeNativeInitialization()
    {
        var modelPath = Path.Combine(Path.GetTempPath(), $"esi-ai-openvino-{Guid.NewGuid():N}.gguf");
        await File.WriteAllBytesAsync(modelPath, []);
        using var runtime = new OpenVinoRuntime();
        var configuration = JsonSerializer.SerializeToElement(new OpenVinoLoadRequest(modelPath, "CPU"));
        var request = new BackendLoadRequest(modelPath, "openvino", configuration);

        try
        {
            var exception = await Assert.ThrowsExactlyAsync<ArgumentException>(() => runtime.LoadAsync(request));

            StringAssert.Contains(exception.Message, "GPU, MULTI:GPU, or NPU");
        }
        finally
        {
            File.Delete(modelPath);
        }
    }

    [TestMethod]
    public void SupportsImageInput_WhenRuntimeIsUnloaded_ReturnsFalse()
    {
        using var runtime = new OpenVinoRuntime();

        Assert.IsFalse(runtime.SupportsImageInput(null));
    }

    [TestMethod]
    public void SerializeChatMessageForHistory_WhenAssistantHasToolCalls_NormalizesArgumentsToJsonObject()
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
        var arguments = document.RootElement.GetProperty("tool_calls")[0].GetProperty("function").GetProperty("arguments");

        Assert.AreEqual(JsonValueKind.Object, arguments.ValueKind);
        Assert.AreEqual("weather", arguments.GetProperty("query").GetString());
    }

    [TestMethod]
    public void Parse_WhenQwenXmlToolCallIsReturned_ProducesNormalizedToolCall()
    {
        var parsed = OpenVinoToolCallParser.Parse(
            "<tool_call><function=lookup><parameter=query>weather in Berlin</parameter></function></tool_call>");

        Assert.AreEqual(string.Empty, parsed.Text);
        var toolCall = parsed.ToolCalls.Single();
        Assert.AreEqual("lookup", toolCall.Function.Name);
        StringAssert.Contains(toolCall.Function.Arguments, "weather in Berlin");
    }

    [TestMethod]
    public void CreateImageTensors_WhenBmpImageIsProvided_ReturnsRgbNhwcTensor()
    {
        OpenVinoRuntime.InitializeRuntime();
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