using System.Text.Json;
using Esi.AI.Backend.Vllm.Xpu;
using Esi.AI.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Backend.Vllm.Xpu.Tests;

[TestClass]
public sealed class VllmXpuGrpcMapperTests
{
    [TestMethod]
    public void ToGrpcRequest_WhenStructuredOutputSchemaIsSet_MapsSchema()
    {
        var request = VllmXpuGrpcMapper.ToGrpcRequest(
            [new ChatMessage("user", "Return JSON.")],
            "model-id",
            new ChatGenerationOptions(StructuredOutputJsonSchema: """{"type":"object"}"""));

        Assert.AreEqual("""{"type":"object"}""", request.StructuredOutputJsonSchema);
    }

    [TestMethod]
    public void ToGrpcRequest_WhenAssistantHasToolCall_SerializesOpenAiFieldNames()
    {
        var request = VllmXpuGrpcMapper.ToGrpcRequest(
            [
                new ChatMessage(
                    "assistant",
                    string.Empty,
                    ToolCalls:
                    [
                        new OpenAiToolCall(
                            "call_1",
                            "function",
                            new OpenAiToolCallFunction("lookup", "{\"query\":\"weather\"}"))
                    ])
            ],
            "model-id",
            new ChatGenerationOptions());

        using var toolCalls = JsonDocument.Parse(request.Messages[0].ToolCallsJson);
        var toolCall = toolCalls.RootElement[0];
        Assert.AreEqual("call_1", toolCall.GetProperty("id").GetString());
        Assert.AreEqual("function", toolCall.GetProperty("type").GetString());
        Assert.AreEqual("lookup", toolCall.GetProperty("function").GetProperty("name").GetString());
        Assert.AreEqual("{\"query\":\"weather\"}", toolCall.GetProperty("function").GetProperty("arguments").GetString());
    }

    [TestMethod]
    public void ToGrpcRequest_WhenReasoningEffortIsSet_MapsReasoningEffort()
    {
        var request = VllmXpuGrpcMapper.ToGrpcRequest(
            [new ChatMessage("user", "Hello.")],
            "model-id",
            new ChatGenerationOptions(ReasoningEffort: "none"));

        Assert.AreEqual("none", request.ReasoningEffort);
    }

    [TestMethod]
    public void ToGrpcRequest_WhenMessageContainsImages_PreservesImageBytesAndContentOrder()
    {
        var imageData = new byte[] { 137, 80, 78, 71 };
        var request = VllmXpuGrpcMapper.ToGrpcRequest(
            [
                new ChatMessage(
                    "user",
                    "Describe this.",
                    Images: [new ChatImage("image/png", imageData)],
                    ContentParts:
                    [
                        new ChatMessageContentPart(Text: "Describe this: "),
                        new ChatMessageContentPart(ImageIndex: 0)
                    ])
            ],
            "model-id",
            new ChatGenerationOptions());

        var message = request.Messages.Single();
        Assert.AreEqual("image/png", message.Images.Single().MediaType);
        CollectionAssert.AreEqual(imageData, message.Images.Single().Data.ToByteArray());
        Assert.AreEqual("Describe this: ", message.ContentParts[0].Text);
        Assert.AreEqual((uint)0, message.ContentParts[1].ImageIndex);
    }
}
