using System.Net;
using System.Text.Json;
using Esi.AI.Models;
using Esi.AI.Studio.Services;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Studio.Tests;

[TestClass]
public sealed class OmniRouteClientTests
{
    [TestMethod]
    public async Task ListModelsAsync_WhenUpstreamReturnsModels_ParsesModelList()
    {
        using var handler = new RecordingHandler(
            HttpStatusCode.OK,
            "{\"object\":\"list\",\"data\":[{\"id\":\"qwen3\",\"object\":\"model\",\"created\":1,\"owned_by\":\"omniroute\"}]}");
        var (client, httpClient) = CreateClient(handler, new OmniRouteOptions());
        using (httpClient)
        {
            var result = await client.ListModelsAsync(CancellationToken.None);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual("qwen3", result.Models!.Data[0].Id);
            Assert.AreEqual("http://localhost/v1/models", handler.Request!.RequestUri!.ToString());
        }
    }

    [TestMethod]
    public async Task CreateChatCompletionAsync_WhenConfigured_ForwardsPayloadAndApiKey()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, "data: [DONE]\n\n", "text/event-stream");
        var (client, httpClient) = CreateClient(handler, new OmniRouteOptions { ApiKey = "test-key" });
        using (httpClient)
        {
        var request = new OpenAiChatRequest(
            "qwen3",
            new[] { new OpenAiChatMessage("user", "Hello") },
            true)
        {
            AdditionalProperties = new Dictionary<string, JsonElement>
            {
                ["temperature"] = JsonSerializer.Deserialize<JsonElement>("0.2")
            }
        };

            using var response = await client.CreateChatCompletionAsync(request, null, CancellationToken.None);
            var body = await handler.Request!.Content!.ReadAsStringAsync();

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("Bearer test-key", handler.Request.Headers.Authorization!.ToString());
            StringAssert.Contains(body, "\"model\":\"qwen3\"");
            StringAssert.Contains(body, "\"stream\":true");
            StringAssert.Contains(body, "\"temperature\":0.2");
        }
    }

    [TestMethod]
    public async Task CreateChatCompletionAsync_WhenToolHistoryIsProvided_ForwardsOpenAiToolFields()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var (client, httpClient) = CreateClient(handler, new OmniRouteOptions());
        using (httpClient)
        {
            var request = new OpenAiChatRequest(
                "qwen3",
                [
                    new OpenAiChatMessage(
                        "assistant",
                        ToolCalls: [new OpenAiToolCall("call_1", "function", new OpenAiToolCallFunction("read_file", "{\"path\":\"README.md\"}"))]),
                    new OpenAiChatMessage("tool", "contents", ToolCallId: "call_1")
                ]);

            using var response = await client.CreateChatCompletionAsync(request, null, CancellationToken.None);
            var body = await handler.Request!.Content!.ReadAsStringAsync();

            StringAssert.Contains(body, "\"tool_calls\"");
            StringAssert.Contains(body, "\"tool_call_id\":\"call_1\"");
            StringAssert.Contains(body, "\"read_file\"");
        }
    }

    [TestMethod]
    public async Task CreateChatCompletionAsync_WhenForwardingEnabled_UsesCallerAuthorization()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var (client, httpClient) = CreateClient(handler, new OmniRouteOptions
        {
            ApiKey = "server-key",
            ForwardAuthorizationHeader = true
        });

        using (httpClient)
        {
            using var response = await client.CreateChatCompletionAsync(
                new OpenAiChatRequest("qwen3", new[] { new OpenAiChatMessage("user", "Hello") }),
                "Bearer caller-key",
                CancellationToken.None);

            Assert.AreEqual("Bearer caller-key", handler.Request!.Headers.Authorization!.ToString());
        }
    }

    [TestMethod]
    public async Task CreateChatCompletionAsync_WhenCallerAuthorizationIsNotBearer_UsesConfiguredApiKey()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var (client, httpClient) = CreateClient(handler, new OmniRouteOptions
        {
            ApiKey = "server-key",
            ForwardAuthorizationHeader = true
        });

        using (httpClient)
        {
            using var response = await client.CreateChatCompletionAsync(
                new OpenAiChatRequest("qwen3", new[] { new OpenAiChatMessage("user", "Hello") }),
                "Basic caller-credentials",
                CancellationToken.None);

            Assert.AreEqual("Bearer server-key", handler.Request!.Headers.Authorization!.ToString());
        }
    }

    private static (OmniRouteClient Client, HttpClient HttpClient) CreateClient(RecordingHandler handler, OmniRouteOptions options)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/v1/")
        };
        return (new OmniRouteClient(httpClient, Options.Create(options)), httpClient);
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode, string body, string? mediaType = "application/json") : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType!);
            return Task.FromResult(response);
        }
    }
}