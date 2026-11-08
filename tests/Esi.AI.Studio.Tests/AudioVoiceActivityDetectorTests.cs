using System.Buffers.Binary;
using System.Text;
using Esi.AI.Studio.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Studio.Tests;

[TestClass]
public sealed class AudioVoiceActivityDetectorTests
{
    [TestMethod]
    public void ProcessChunk_WhenVoiceIsFollowedBySilence_ReturnsUtterance()
    {
        var detector = new AudioVoiceActivityDetector(new AudioChatOptions
        {
            VoiceThresholdRms = 1000,
            PreRollMilliseconds = 100,
            EndOfSpeechSilenceMilliseconds = 200,
            MinimumSpeechMilliseconds = 100
        });
        var silence = CreatePcmChunk(0);
        var voice = CreatePcmChunk(2000);

        Assert.IsNull(detector.ProcessChunk(silence));
        Assert.IsNull(detector.ProcessChunk(voice));
        Assert.IsNull(detector.ProcessChunk(silence));
        var utterance = detector.ProcessChunk(silence);

        Assert.IsNotNull(utterance);
        Assert.AreEqual(9600, utterance.Length);
    }

    [TestMethod]
    public void Flush_WhenSpeechHasNotReachedMinimumDuration_DiscardsNoise()
    {
        var detector = new AudioVoiceActivityDetector(new AudioChatOptions
        {
            VoiceThresholdRms = 1000,
            PreRollMilliseconds = 0,
            MinimumSpeechMilliseconds = 200
        });

        detector.ProcessChunk(CreatePcmChunk(2000));

        Assert.IsNull(detector.Flush());
    }

    [TestMethod]
    public void CreateWave_WritesPcm16MonoHeaderAndPayload()
    {
        var pcm = new byte[] { 1, 2, 3, 4 };

        var wave = PcmAudio.CreateWave(pcm, 16000);

        Assert.AreEqual(48, wave.Length);
        Assert.AreEqual("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
        Assert.AreEqual(40, BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(4, 4)));
        Assert.AreEqual(16000, BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(24, 4)));
        Assert.AreEqual(4, BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(40, 4)));
        CollectionAssert.AreEqual(pcm, wave[44..]);
    }

    [TestMethod]
    public void ProcessChunk_WithOddByteCount_ThrowsArgumentException()
    {
        var detector = new AudioVoiceActivityDetector(new AudioChatOptions());

        Assert.ThrowsExactly<ArgumentException>(() => detector.ProcessChunk([1]));
    }

    private static byte[] CreatePcmChunk(short value)
    {
        var bytes = new byte[3200];
        for (var index = 0; index < bytes.Length; index += sizeof(short))
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index, sizeof(short)), value);

        return bytes;
    }
}
