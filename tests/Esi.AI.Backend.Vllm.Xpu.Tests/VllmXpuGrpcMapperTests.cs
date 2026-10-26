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
}
