using NUnit.Framework;
using Esi.AI.Llm.Providers;
using System.Net;
using System.Net.Http;
using System.Text;

namespace Esi.AI.Llm.Tests;

[TestFixture]
public class OpenAiProviderTests
{
    private static ChatCompletionRequest CreateRequest() => new()
    {
        Model = "gpt-4o-mini",
        Messages = new List<ChatMessage> { new() { Role = "user", Content = "Hello" } }
    };

    [Test]
    public async Task CompleteAsync_WithValidRequest_ReturnsProviderResult()
    {
        var json = """
        {
          "choices": [ { "message": { "content": "Test response" }, "finish_reason": "stop" } ],
          "usage": { "prompt_tokens": 10, "completion_tokens": 20 }
        }
        """;
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        var result = await provider.CompleteAsync(CreateRequest());

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Content, Is.EqualTo("Test response"));
        Assert.That(result.FinishReason, Is.EqualTo("stop"));
        Assert.That(result.Usage, Is.Not.Null);
        Assert.That(result.Usage!.InputTokens, Is.EqualTo(10));
        Assert.That(result.Usage!.OutputTokens, Is.EqualTo(20));
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task CompleteStreamingAsync_WithValidRequest_ReturnsChunks()
    {
        var sse =
            "data: {\"id\":\"1\",\"choices\":[{\"delta\":{\"content\":\"Hel\"},\"finish_reason\":null}]}\n\n" +
            "data: {\"id\":\"1\",\"choices\":[{\"delta\":{\"content\":\"lo\"},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: [DONE]\n\n";
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
        });

        var chunks = new List<Chunk>();
        await foreach (var chunk in provider.CompleteStreamingAsync(CreateRequest()))
        {
            chunks.Add(chunk);
        }

        Assert.That(chunks, Has.Count.EqualTo(2));
        Assert.That(chunks[0].Content, Is.EqualTo("Hel"));
        Assert.That(chunks[1].Content, Is.EqualTo("lo"));
        Assert.That(chunks[1].FinishReason, Is.EqualTo("stop"));
    }

    [Test]
    public async Task CompleteAsync_WithHttpError_ReturnsRetryableErrorResult()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("service unavailable")
        });

        var result = await provider.CompleteAsync(CreateRequest());

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error!.IsRetryable, Is.True);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(503));
    }

    private static OpenAiProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(responder));
        return new OpenAiProvider("sk-test", "http://test", httpClient);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}