using NUnit.Framework;
using Esi.AI.Llm.Providers;
using System.Net.Http;
using System.Text.Json;

namespace Esi.AI.Llm.Tests;

[TestFixture]
public class OpenAiProviderTests
{
    private TestOpenAiProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _provider = new TestOpenAiProvider();
    }

    [Test]
    public async Task CompleteAsync_WithValidRequest_ReturnsProviderResult()
    {
        // Arrange
        var request = new ChatCompletionRequest
        {
            Messages = new[]
            {
                new ChatMessage { Role = "user", Content = "Hello" }
            }
        };

        // Act
        var result = await _provider.CompleteAsync(request);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Content, Is.Not.Null);
        Assert.That(result.FinishReason, Is.EqualTo("stop"));
    }

    [Test]
    public async Task CompleteStreamingAsync_WithValidRequest_ReturnsChunks()
    {
        // Arrange
        var request = new ChatCompletionRequest
        {
            Messages = new[]
            {
                new ChatMessage { Role = "user", Content = "Hello" }
            }
        };

        // Act
        var chunks = await _provider.CompleteStreamingAsync(request);

        // Assert
        Assert.That(chunks, Is.Not.Null);
        Assert.That(chunks.Count(), Is.GreaterThan(0));
    }

    [Test]
    public async Task CompleteAsync_WithError_ReturnsErrorResult()
    {
        // Arrange
        var request = new ChatCompletionRequest
        {
            Messages = new[]
            {
                new ChatMessage { Role = "user", Content = "" } // May cause error
            }
        };

        // Act
        var result = await _provider.CompleteAsync(request);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error.IsRetryable, Is.True);
    }
}

// Helper test provider that doesn't need real API keys
public class TestOpenAiProvider : OpenAiProvider
{
    public TestOpenAiProvider() 
        : base(new HttpClient { BaseAddress = new Uri("http://test") }, 
              "test-model", 
              "sk-test", 
              "http://test") 
    {
    }

    protected override async Task<ProviderResult> SendRequestAsync(ChatCompletionRequest request)
    {
        // Return a test result without making actual HTTP call
        return new ProviderResult
        {
            Id = "chat-test-123",
            Content = "Test response",
            FinishReason = "stop",
            Usage = new ProviderResult.UsageInfo
            {
                InputTokens = 10,
                OutputTokens = 20,
                TotalTokens = 30
            }
        };
    }

    protected override IReadOnlyDictionary<string, string> GetHeaders() => new Dictionary<string, string>
    {
        { "Authorization", "Bearer sk-test" },
        { "Content-Type", "application/json" }
    };
}