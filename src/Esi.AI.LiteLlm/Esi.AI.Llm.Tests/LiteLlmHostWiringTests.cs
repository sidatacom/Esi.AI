using NUnit.Framework;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Esi.AI.Llm.Gateway;
using Esi.AI.Llm.Providers;
using Esi.AI.Llm.Router;
using Esi.AI.Llm.Redis;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Esi.AI.Llm.Tests;

[TestFixture]
public class LiteLlmHostWiringTests
{
    [Test]
    public void AddLiteLlmServices_WithRegisteredProvider_WiresRouterDeployment()
    {
        var services = new ServiceCollection();
        services.AddLiteLlmServices();
        services.AddSingleton<IChatCompletionProvider>(new StubProvider("stub"));

        using var provider = services.BuildServiceProvider();

        var router = provider.GetRequiredService<ProviderRouter>();
        var (deployment, resolvedProvider) = router.SelectDeployment(RoutingStrategy.RoundRobin);

        Assert.That(deployment, Is.Not.Null);
        Assert.That(resolvedProvider, Is.Not.Null);
        Assert.That(resolvedProvider!.Name, Is.EqualTo("stub"));
        Assert.That(provider.GetService<IRedisCacheService>(), Is.InstanceOf<InMemoryRedisCacheService>());
        Assert.That(provider.GetService<Orchestrator>(), Is.Not.Null);
    }

    [Test]
    public async Task Orchestrator_CompletesRequest_UsingRegisteredProvider()
    {
        var services = new ServiceCollection();
        services.AddLiteLlmServices();
        services.AddSingleton<IChatCompletionProvider>(new StubProvider("stub"));
        using var provider = services.BuildServiceProvider();

        var orchestrator = provider.GetRequiredService<Orchestrator>();
        var result = await orchestrator.OrchestrateAsync(new ChatCompletionRequest
        {
            Model = "stub",
            Messages = new List<ChatMessage> { new() { Role = "user", Content = "hi" } }
        });

        Assert.That(result.Error, Is.Null);
        Assert.That(result.Content, Is.EqualTo("Hello!"));
    }
}

[TestFixture]
public class ChatCompletionGatewaySseTests
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    [SetUp]
    public async Task SetUp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLiteLlmServices();
        builder.Services.AddSingleton<IChatCompletionProvider>(new StubProvider("stub"));

        _app = builder.Build();
        _app.MapLiteLlmGateway();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Test]
    public async Task ChatCompletions_NonStreaming_ReturnsOpenAiCompatibleJson()
    {
        var response = await _client.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "stub",
            messages = new[] { new { role = "user", content = "hi" } },
            stream = false
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(json.RootElement.GetProperty("object").GetString(), Is.EqualTo("chat.completion"));
        Assert.That(json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString(), Is.EqualTo("Hello!"));
    }

    [Test]
    public async Task ChatCompletions_Streaming_ReturnsServerSentEventChunks()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "stub",
                messages = new[] { new { role = "user", content = "hi" } },
                stream = true
            })
        };

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/event-stream"));

        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Does.Contain("data: "));
        Assert.That(body, Does.Contain("\"content\":\"Hel\""));
        Assert.That(body, Does.Contain("\"finish_reason\":\"stop\""));
        Assert.That(body.TrimEnd(), Does.EndWith("data: [DONE]"));
    }
}

internal sealed class StubProvider : IChatCompletionProvider
{
    public StubProvider(string name) => Name = name;

    public string Name { get; }

    public Task<ProviderResult> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new ProviderResult
        {
            Id = "stub-1",
            Content = "Hello!",
            FinishReason = "stop",
            Usage = new ProviderResult.UsageInfo { InputTokens = 1, OutputTokens = 2, TotalTokens = 3 }
        });

    public async IAsyncEnumerable<Chunk> CompleteStreamingAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new Chunk { Id = "stub-1", Model = request.Model ?? Name, Content = "Hel" };
        yield return new Chunk { Id = "stub-1", Model = request.Model ?? Name, Content = "lo", FinishReason = "stop" };
        await Task.CompletedTask;
    }
}
