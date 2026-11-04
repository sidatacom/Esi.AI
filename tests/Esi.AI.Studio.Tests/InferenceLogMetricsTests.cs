using Esi.AI.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Studio.Tests;

[TestClass]
public sealed class InferenceLogMetricsTests
{
    [TestMethod]
    public void PrefillTokensPerSecond_UsesActualPromptTokenCount()
    {
        var log = CreateLog(
            estimatedPrefillTokens: 1_200,
            promptTokenCount: 1_000,
            prefillDurationMs: 500);

        Assert.AreEqual(2_000d, log.PrefillTokensPerSecond.GetValueOrDefault(), 0.001d);
        Assert.IsFalse(log.PrefillTokenCountIsEstimated);
    }

    [TestMethod]
    public void PrefillTokensPerSecond_UsesEstimateWhenActualPromptCountIsUnavailable()
    {
        var log = CreateLog(
            estimatedPrefillTokens: 600,
            prefillDurationMs: 200);

        Assert.AreEqual(3_000d, log.PrefillTokensPerSecond.GetValueOrDefault(), 0.001d);
        Assert.IsTrue(log.PrefillTokenCountIsEstimated);
    }

    [TestMethod]
    public void DecodeTokensPerSecond_UsesOutputTokensAndDecodeDuration()
    {
        var log = CreateLog(outputTokenCount: 40, decodeDurationMs: 2_000);

        Assert.AreEqual(20d, log.DecodeTokensPerSecond.GetValueOrDefault(), 0.001d);
    }

    [TestMethod]
    public void EndToEndTokensPerSecond_UsesInputAndOutputTokensOverRequestDuration()
    {
        var log = CreateLog(
            estimatedPrefillTokens: 900,
            outputTokenCount: 100,
            durationMilliseconds: 10_000);

        Assert.AreEqual(100d, log.EndToEndTokensPerSecond.GetValueOrDefault(), 0.001d);
    }

    [TestMethod]
    public void TokenRates_AreUnavailableWhenCountsOrDurationsAreUnavailableOrNonPositive()
    {
        var log = CreateLog(
            promptTokenCount: 100,
            outputTokenCount: 20,
            durationMilliseconds: 0,
            prefillDurationMs: 0,
            decodeDurationMs: null);

        Assert.IsNull(log.PrefillTokensPerSecond);
        Assert.IsNull(log.DecodeTokensPerSecond);
        Assert.IsNull(log.EndToEndTokensPerSecond);
    }

    private static InferenceLog CreateLog(
        int? estimatedPrefillTokens = null,
        int? promptTokenCount = null,
        int? outputTokenCount = null,
        long? durationMilliseconds = null,
        double? prefillDurationMs = null,
        double? decodeDurationMs = null) =>
        new(
            Guid.NewGuid(),
            "inference-test",
            DateTimeOffset.UtcNow,
            "SignalR",
            "test-backend",
            "test-model",
            null,
            1,
            0,
            0,
            false,
            InferenceLogStatus.Succeeded,
            estimatedPrefillTokens,
            outputTokenCount,
            DurationMilliseconds: durationMilliseconds,
            PromptTokenCount: promptTokenCount,
            PrefillDurationMs: prefillDurationMs,
            DecodeDurationMs: decodeDurationMs);
}
