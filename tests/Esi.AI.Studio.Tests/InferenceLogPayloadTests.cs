using System.Text.Json;
using Esi.AI.Models;
using Esi.AI.Studio.Data;
using Esi.AI.Studio.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Studio.Tests;

[TestClass]
public sealed class InferenceLogPayloadTests
{
    [TestMethod]
    public void FormatOpenAiRequest_IncludesTextAndImageMetadataWithoutImageBytes()
    {
        byte[] imageData = [1, 2, 3];
        var request = new OpenAiBackendChatRequest(
            "OpenVINO",
            "test-model",
            "/models/test",
            [new OpenAiChatMessage("user", "Describe this.")],
            [new ChatMessage("user", "Describe this.", [new ChatImage("image/png", imageData)])],
            null,
            new ChatGenerationOptions(MaxTokens: 32));

        var payload = InferenceLogPayloadFormatter.FormatOpenAiRequest(request, streaming: false);
        using var document = JsonDocument.Parse(payload);
        var message = document.RootElement.GetProperty("messages")[0];

        Assert.AreEqual("Describe this.", message.GetProperty("content").GetString());
        Assert.AreEqual(3, message.GetProperty("images")[0].GetProperty("sizeBytes").GetInt32());
        Assert.IsFalse(payload.Contains(Convert.ToBase64String(imageData), StringComparison.Ordinal));
    }

    [TestMethod]
    public void FormatStudioRequest_IncludesPriorAndCurrentMessages()
    {
        var chat = new PersistedChat(
            Guid.NewGuid(),
            "Test chat",
            DateTime.UtcNow,
            DateTime.UtcNow,
            [new PersistedChatMessage("assistant", "Earlier response", DateTime.UtcNow)]);
        var request = new ChatExchangeRequest("Current prompt");

        var payload = InferenceLogPayloadFormatter.FormatStudioRequest(chat, request, streaming: true);
        using var document = JsonDocument.Parse(payload);
        var messages = document.RootElement.GetProperty("messages");

        Assert.AreEqual(2, messages.GetArrayLength());
        Assert.AreEqual("Earlier response", messages[0].GetProperty("content").GetString());
        Assert.AreEqual("Current prompt", messages[1].GetProperty("content").GetString());
        Assert.IsTrue(document.RootElement.GetProperty("stream").GetBoolean());
    }

    [TestMethod]
    public void FormatResponse_IncludesGeneratedTextRateAndCompletionMetadata()
    {
        var result = new GenerationResult("Generated answer", 3, TimeSpan.Zero, 58.4, FinishReason: "stop");

        var payload = InferenceLogPayloadFormatter.FormatResponse(result);
        using var document = JsonDocument.Parse(payload);

        Assert.AreEqual("Generated answer", document.RootElement.GetProperty("text").GetString());
        Assert.AreEqual(58.4, document.RootElement.GetProperty("tokensPerSecond").GetDouble(), 0.001);
        Assert.AreEqual("stop", document.RootElement.GetProperty("finishReason").GetString());
        Assert.AreEqual(3, document.RootElement.GetProperty("tokenCount").GetInt32());
    }

    [TestMethod]
    public async Task InferenceLogService_ReadAsync_RetainsRequestAndResponsePayloads()
    {
        var directory = Directory.CreateTempSubdirectory("esi-inference-logs-").FullName;
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "inference-logs.db")}")
                .Options;
            var factory = new TestDbContextFactory(options);
            await using (var db = await factory.CreateDbContextAsync())
                await db.Database.EnsureCreatedAsync();

            var service = new InferenceLogService(factory, new TestPublisher());
            var created = await service.InferenceLog_CreateAsync(new InferenceLog(
                Guid.NewGuid(),
                "inference-test",
                DateTimeOffset.UtcNow,
                "OpenAI",
                "OpenVINO",
                "test-model",
                null,
                1,
                0,
                0,
                false,
                InferenceLogStatus.Running,
                RequestPayload: """{"messages":[{"role":"user","content":"hello"}]}"""));

            await service.InferenceLog_UpdateAsync(created with
            {
                Status = InferenceLogStatus.Succeeded,
                ResponsePayload = """{"text":"hello back"}""",
                PromptTokenCount = 128,
                TimeToFirstTokenMs = 120,
                PrefillDurationMs = 110,
                DecodeDurationMs = 80,
                GenerationTokensPerSecond = 58.4,
                OutputTokenCount = 16,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                DurationMilliseconds = 1
            });

            var entry = (await service.InferenceLog_ReadAsync()).Single();

            Assert.AreEqual(created.RequestPayload, entry.RequestPayload);
            Assert.AreEqual("""{"text":"hello back"}""", entry.ResponsePayload);
            Assert.AreEqual(58.4, entry.GenerationTokensPerSecond.GetValueOrDefault(), 0.001);
            Assert.AreEqual(128, entry.PromptTokenCount);
            Assert.AreEqual(120d, entry.TimeToFirstTokenMs);
            Assert.AreEqual(110d, entry.PrefillDurationMs);
            Assert.AreEqual(80d, entry.DecodeDurationMs);
            Assert.AreEqual(16, entry.OutputTokenCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task InferenceLog_ClearCompletedAsync_DeletesCompletedLogsAndPreservesRunningLogs()
    {
        var directory = Directory.CreateTempSubdirectory("esi-inference-logs-").FullName;
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "inference-logs.db")}")
                .Options;
            var factory = new TestDbContextFactory(options);
            await using (var db = await factory.CreateDbContextAsync())
                await db.Database.EnsureCreatedAsync();

            var publisher = new TestPublisher();
            var service = new InferenceLogService(factory, publisher);
            var running = await service.InferenceLog_CreateAsync(CreateRunningLog("running"));
            var completedOne = await service.InferenceLog_CreateAsync(CreateRunningLog("completed-one"));
            var completedTwo = await service.InferenceLog_CreateAsync(CreateRunningLog("completed-two"));
            await service.InferenceLog_UpdateAsync(MarkCompleted(completedOne));
            await service.InferenceLog_UpdateAsync(MarkCompleted(completedTwo));

            var deletedCount = await service.InferenceLog_ClearCompletedAsync();
            var remaining = await service.InferenceLog_ReadAsync();

            Assert.AreEqual(2, deletedCount);
            Assert.AreEqual(running.Id, remaining.Single().Id);
            CollectionAssert.AreEquivalent(
                new[] { completedOne.Id, completedTwo.Id },
                publisher.Deleted.Select(log => log.Id).ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static InferenceLog CreateRunningLog(string inferenceId) => new(
        Guid.NewGuid(),
        inferenceId,
        DateTimeOffset.UtcNow,
        "SignalR",
        "OpenVINO",
        "test-model",
        null,
        1,
        0,
        0,
        false,
        InferenceLogStatus.Running);

    private static InferenceLog MarkCompleted(InferenceLog log) => log with
    {
        Status = InferenceLogStatus.Succeeded,
        CompletedAtUtc = DateTimeOffset.UtcNow,
        DurationMilliseconds = 1
    };

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }

    private sealed class TestPublisher : IInferenceLogPublisher
    {
        public List<InferenceLog> Deleted { get; } = [];

        public Task PublishCreateAsync(InferenceLog log, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PublishUpdateAsync(InferenceLog log, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PublishDeleteAsync(InferenceLog log, CancellationToken cancellationToken = default)
        {
            Deleted.Add(log);
            return Task.CompletedTask;
        }
    }
}
