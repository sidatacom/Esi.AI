using System.Text.Json;
using Esi.AI.Models;
using Esi.AI.Studio.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Studio.Tests;

[TestClass]
public sealed class RhubarbLipSyncVisemeAnalyzerTests
{
    private static readonly JsonSerializerOptions SignalRJsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void ParseCues_WithOrderedMouthShapes_ReturnsChunkRelativeVisemeIntervals()
    {
        const string json = """
            {
              "metadata": { "duration": 1.00 },
              "mouthCues": [
                { "start": 0.00, "end": 0.10, "value": "A" },
                { "start": 0.10, "end": 0.40, "value": "D" },
                { "start": 0.40, "end": 1.00, "value": "X" }
              ]
            }
            """;

        var cues = RhubarbLipSyncVisemeAnalyzer.ParseCues(json, 1000);

        Assert.HasCount(3, cues);
        Assert.AreEqual(0, cues[0].OffsetMilliseconds);
        Assert.AreEqual(100, cues[0].DurationMilliseconds);
        Assert.AreEqual(AudioChatVisemeIds.Closed, cues[0].VisemeId);
        Assert.AreEqual(1f, cues[0].Weight);
        Assert.AreEqual(100, cues[1].OffsetMilliseconds);
        Assert.AreEqual(300, cues[1].DurationMilliseconds);
        Assert.AreEqual(AudioChatVisemeIds.WideOpen, cues[1].VisemeId);
        Assert.AreEqual(400, cues[2].OffsetMilliseconds);
        Assert.AreEqual(600, cues[2].DurationMilliseconds);
        Assert.AreEqual(AudioChatVisemeIds.Neutral, cues[2].VisemeId);
    }

    [TestMethod]
    public void ParseCues_WhenCueExceedsAudioDuration_ThrowsAnalysisException()
    {
        const string json = """
            { "mouthCues": [{ "start": 0.90, "end": 1.01, "value": "A" }] }
            """;

        Assert.ThrowsExactly<AudioRuntimeException>(() =>
            RhubarbLipSyncVisemeAnalyzer.ParseCues(json, 1000));
    }

    [TestMethod]
    public void ParseCues_WhenCuesOverlap_ThrowsAnalysisException()
    {
        const string json = """
            {
              "mouthCues": [
                { "start": 0.00, "end": 0.60, "value": "A" },
                { "start": 0.50, "end": 0.90, "value": "D" }
              ]
            }
            """;

        Assert.ThrowsExactly<AudioRuntimeException>(() =>
            RhubarbLipSyncVisemeAnalyzer.ParseCues(json, 1000));
    }

    [TestMethod]
    public void ParseCues_WhenOutputHasNoCues_ThrowsAnalysisException()
    {
        Assert.ThrowsExactly<AudioRuntimeException>(() =>
            RhubarbLipSyncVisemeAnalyzer.ParseCues("""{ "mouthCues": [] }""", 1000));
    }

    [TestMethod]
    public async Task AnalyzeAsync_WhenCallerIsCanceled_PropagatesCancellation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rhubarb-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "rhubarb");
        File.WriteAllText(executable, string.Empty);

        try
        {
            var analyzer = new RhubarbLipSyncVisemeAnalyzer(
                Options.Create(new AudioChatOptions { VisemeAnalyzerExecutablePath = executable }),
                new TestHostEnvironment { ContentRootPath = directory },
                NullLogger<RhubarbLipSyncVisemeAnalyzer>.Instance);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var wav = PcmAudio.CreateWave(new byte[32000], 16000);

            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await analyzer.AnalyzeAsync(wav, cancellation.Token));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void AudioChunk_WhenSerializedWithVisemes_PreservesAudioCueCorrelation()
    {
        var sessionId = Guid.NewGuid();
        var turnId = Guid.NewGuid();
        var cue = new AudioChatVisemeCue(100, 200, AudioChatVisemeIds.Open, 1f);
        var chunk = new AudioChatAudioChunk(
            sessionId,
            turnId,
            2,
            "audio/wav",
            [1, 2, 3],
            1000,
            [cue]);

        var serialized = JsonSerializer.Serialize(chunk, SignalRJsonOptions);
        var roundTrip = JsonSerializer.Deserialize<AudioChatAudioChunk>(serialized, SignalRJsonOptions);

        Assert.IsNotNull(roundTrip);
        Assert.AreEqual(sessionId, roundTrip.SessionId);
        Assert.AreEqual(turnId, roundTrip.TurnId);
        Assert.AreEqual(2, roundTrip.Sequence);
        Assert.AreEqual(1000, roundTrip.AudioDurationMilliseconds);
        Assert.AreEqual(cue, Assert.ContainsSingle(roundTrip.VisemeCues!));
    }

    [TestMethod]
    public void AudioChunk_WhenDeserializedFromLegacyPayload_DefaultsVisemeMetadata()
    {
        var sessionId = Guid.NewGuid();
        var turnId = Guid.NewGuid();
        var legacyPayload = $$"""
            {
              "sessionId": "{{sessionId}}",
              "turnId": "{{turnId}}",
              "sequence": 0,
              "contentType": "audio/wav",
              "data": "AQID"
            }
            """;

        var chunk = JsonSerializer.Deserialize<AudioChatAudioChunk>(legacyPayload, SignalRJsonOptions);

        Assert.IsNotNull(chunk);
        Assert.AreEqual(sessionId, chunk.SessionId);
        Assert.AreEqual(turnId, chunk.TurnId);
        Assert.AreEqual(0, chunk.AudioDurationMilliseconds);
        Assert.IsNull(chunk.VisemeCues);
    }

    [TestMethod]
    public void AudioChunk_WhenDeserializedByLegacyClient_IgnoresVisemeMetadata()
    {
        var chunk = new AudioChatAudioChunk(
            Guid.NewGuid(),
            Guid.NewGuid(),
            0,
            "audio/wav",
            [1, 2, 3],
            1000,
            [new AudioChatVisemeCue(0, 1000, AudioChatVisemeIds.Neutral, 1f)]);

        var serialized = JsonSerializer.Serialize(chunk, SignalRJsonOptions);
        var legacyChunk = JsonSerializer.Deserialize<LegacyAudioChatAudioChunk>(serialized, SignalRJsonOptions);

        Assert.IsNotNull(legacyChunk);
        Assert.AreEqual(chunk.SessionId, legacyChunk.SessionId);
        Assert.AreEqual(chunk.TurnId, legacyChunk.TurnId);
        Assert.AreEqual(chunk.Sequence, legacyChunk.Sequence);
        CollectionAssert.AreEqual(chunk.Data, legacyChunk.Data);
    }

    private sealed record LegacyAudioChatAudioChunk(
        Guid SessionId,
        Guid TurnId,
        int Sequence,
        string ContentType,
        byte[] Data);

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "AudioChat.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
