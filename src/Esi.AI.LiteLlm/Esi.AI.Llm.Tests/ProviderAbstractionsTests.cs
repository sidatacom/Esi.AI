using NUnit.Framework;

namespace Esi.AI.Llm.Tests;

[TestFixture]
public class ProviderAbstractionsTests
{
    [Test]
    public void ProviderResult_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var result = new ProviderResult { Id = string.Empty, Content = string.Empty };

        // Assert
        Assert.That(result.Id, Is.Empty);
        Assert.That(result.Content, Is.Empty);
        Assert.That(result.FinishReason, Is.Null);
        Assert.That(result.Usage, Is.Null);
    }

    [Test]
    public void ProviderResult_WithValues_SetsProperties()
    {
        // Arrange & Act
        var result = new ProviderResult
        {
            Id = "chat-123",
            Content = "Hello",
            FinishReason = "stop"
        };

        // Assert
        Assert.That(result.Id, Is.EqualTo("chat-123"));
        Assert.That(result.Content, Is.EqualTo("Hello"));
        Assert.That(result.FinishReason, Is.EqualTo("stop"));
    }

    [Test]
    public void ProviderResult_UsageInfo_DefaultValues()
    {
        // Arrange & Act
        var result = new ProviderResult { Id = "chat-1", Content = string.Empty, Usage = new ProviderResult.UsageInfo() };

        // Assert
        Assert.That(result.Usage.InputTokens, Is.Zero);
        Assert.That(result.Usage.OutputTokens, Is.Zero);
        Assert.That(result.Usage.TotalTokens, Is.Zero);
    }

    [Test]
    public void ProviderResult_UsageInfo_SetsValues()
    {
        // Arrange & Act
        var result = new ProviderResult
        {
            Id = "chat-1",
            Content = string.Empty,
            Usage = new ProviderResult.UsageInfo
            {
                InputTokens = 50,
                OutputTokens = 100,
                TotalTokens = 150
            }
        };

        // Assert
        Assert.That(result.Usage.InputTokens, Is.EqualTo(50));
        Assert.That(result.Usage.OutputTokens, Is.EqualTo(100));
        Assert.That(result.Usage.TotalTokens, Is.EqualTo(150));
    }

    [Test]
    public void ProviderResult_ErrorInfo_DefaultValues()
    {
        // Arrange & Act
        var result = new ProviderResult { Id = "chat-1", Content = string.Empty, Error = new ProviderResult.ErrorInfo() };

        // Assert
        Assert.That(result.Error!.Message, Is.Null);
        Assert.That(result.Error!.Code, Is.Null);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(500));
        Assert.That(result.Error.IsRetryable, Is.False);
    }

    [Test]
    public void ProviderResult_ErrorInfo_SetsValues()
    {
        // Arrange & Act
        var result = new ProviderResult
        {
            Id = "chat-1",
            Content = string.Empty,
            Error = new ProviderResult.ErrorInfo
            {
                Reason = "Rate limit exceeded",
                Code = "rate_limit",
                StatusCode = 429,
                IsRetryable = true
            }
        };

        // Assert
        Assert.That(result.Error!.Message, Is.EqualTo("Rate limit exceeded"));
        Assert.That(result.Error!.Code, Is.EqualTo("rate_limit"));
        Assert.That(result.Error!.StatusCode, Is.EqualTo(429));
        Assert.That(result.Error!.IsRetryable, Is.True);
    }

    [Test]
    public void Chunk_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var chunk = new Chunk();

        // Assert
        Assert.That(chunk.Id, Is.Null);
        Assert.That(chunk.Object, Is.EqualTo("chat.completion.chunk"));
        Assert.That(chunk.Model, Is.Null);
        Assert.That(chunk.Content, Is.Null);
        Assert.That(chunk.FinishReason, Is.Null);
    }

    [Test]
    public void Chunk_SetsValues()
    {
        // Arrange & Act
        var chunk = new Chunk
        {
            Id = "chunk-1",
            Model = "gpt-4o",
            Content = "Hello",
            FinishReason = "stop"
        };

        // Assert
        Assert.That(chunk.Id, Is.EqualTo("chunk-1"));
        Assert.That(chunk.Model, Is.EqualTo("gpt-4o"));
        Assert.That(chunk.Content, Is.EqualTo("Hello"));
        Assert.That(chunk.FinishReason, Is.EqualTo("stop"));
    }

    [Test]
    public void ChatMessage_DefaultValues()
    {
        // Arrange & Act
        var message = new ChatMessage();

        // Assert
        Assert.That(message.Role, Is.Null.Or.Empty);
        Assert.That(message.Content, Is.Null);
    }

    [Test]
    public void ChatMessage_SetsValues()
    {
        // Arrange & Act
        var message = new ChatMessage
        {
            Role = "user",
            Content = "Hello world"
        };

        // Assert
        Assert.That(message.Role, Is.EqualTo("user"));
        Assert.That(message.Content, Is.EqualTo("Hello world"));
    }
}