using Esi.AI.Core.Chat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Core.Tests;

[TestClass]
public sealed class OpenAiToolCallParserTests
{
    [TestMethod]
    public void Parse_WhenXmlToolCallIsReturned_ProducesOpenAiToolCall()
    {
        var result = OpenAiToolCallParser.Parse("Before <tool_call><function=lookup><parameter=query>weather</parameter></function></tool_call> After");

        Assert.AreEqual("Before  After", result.Text);
        var toolCall = result.ToolCalls.Single();
        Assert.AreEqual("lookup", toolCall.Function.Name);
        Assert.AreEqual("{\"query\":\"weather\"}", toolCall.Function.Arguments);
    }

    [TestMethod]
    public void Parse_WhenJsonToolCallIsReturned_ProducesOpenAiToolCall()
    {
        var result = OpenAiToolCallParser.Parse("{\"name\":\"lookup\",\"arguments\":{\"query\":\"weather\"}}");

        var toolCall = result.ToolCalls.Single();
        Assert.AreEqual("lookup", toolCall.Function.Name);
        Assert.AreEqual("{\"query\":\"weather\"}", toolCall.Function.Arguments);
        Assert.IsEmpty(result.Text);
    }

    [TestMethod]
    public void Parse_WhenJsonToolCallArrayIsReturned_ProducesAllOpenAiToolCalls()
    {
        var result = OpenAiToolCallParser.Parse("[{\"name\":\"lookup\",\"arguments\":{\"query\":\"weather\"}}]");

        var toolCall = result.ToolCalls.Single();
        Assert.AreEqual("lookup", toolCall.Function.Name);
        Assert.AreEqual("{\"query\":\"weather\"}", toolCall.Function.Arguments);
        Assert.IsEmpty(result.Text);
    }
}
