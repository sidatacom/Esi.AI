using System.Buffers.Binary;

namespace Esi.AI.Studio.Services;

internal static class PcmAudio
{
    private const int WaveHeaderSize = 44;

    public static byte[] CreateWave(ReadOnlySpan<byte> pcm16Mono, int sampleRate)
    {
        if (pcm16Mono.Length % sizeof(short) != 0)
            throw new ArgumentException("PCM16 audio must contain complete 16-bit samples.", nameof(pcm16Mono));

        var wave = new byte[WaveHeaderSize + pcm16Mono.Length];
        "RIFF"u8.CopyTo(wave);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4, 4), wave.Length - 8);
        "WAVE"u8.CopyTo(wave.AsSpan(8, 4));
        "fmt "u8.CopyTo(wave.AsSpan(12, 4));
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(22, 2), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(28, 4), sampleRate * sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(32, 2), sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(34, 2), 16);
        "data"u8.CopyTo(wave.AsSpan(36, 4));
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40, 4), pcm16Mono.Length);
        pcm16Mono.CopyTo(wave.AsSpan(WaveHeaderSize));
        return wave;
    }
}

/// <summary>Segments continuous PCM16 microphone audio into speech turns using energy-based VAD.</summary>
public sealed class AudioVoiceActivityDetector : IDisposable
{
    private readonly int sampleRate;
    private readonly int voiceThresholdRms;
    private readonly int endOfSpeechSilenceMilliseconds;
    private readonly int preRollMilliseconds;
    private readonly int minimumSpeechMilliseconds;
    private readonly int maximumUtteranceSeconds;
    private readonly Queue<byte[]> preRoll = new();
    private readonly MemoryStream utterance = new();
    private int preRollByteCount;
    private int silenceMilliseconds;
    private int speechMilliseconds;
    private bool isSpeaking;

    /// <summary>Creates a detector from the configured audio-chat VAD settings.</summary>
    public AudioVoiceActivityDetector(AudioChatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.SampleRate <= 0 ||
            options.VoiceThresholdRms <= 0 ||
            options.EndOfSpeechSilenceMilliseconds <= 0 ||
            options.PreRollMilliseconds < 0 ||
            options.MinimumSpeechMilliseconds < 0 ||
            options.MaximumUtteranceSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Audio VAD settings must be positive and valid.");

        sampleRate = options.SampleRate;
        voiceThresholdRms = options.VoiceThresholdRms;
        endOfSpeechSilenceMilliseconds = options.EndOfSpeechSilenceMilliseconds;
        preRollMilliseconds = options.PreRollMilliseconds;
        minimumSpeechMilliseconds = options.MinimumSpeechMilliseconds;
        maximumUtteranceSeconds = options.MaximumUtteranceSeconds;
    }

    /// <summary>Adds one PCM16 mono chunk and returns a completed utterance when silence closes a turn.</summary>
    public byte[]? ProcessChunk(ReadOnlySpan<byte> pcm16Mono)
    {
        if (pcm16Mono.Length == 0 || pcm16Mono.Length % sizeof(short) != 0)
            throw new ArgumentException("Audio chunks must contain non-empty, complete PCM16 samples.", nameof(pcm16Mono));

        var chunk = pcm16Mono.ToArray();
        var chunkMilliseconds = (int)Math.Max(1, pcm16Mono.Length * 1000d / (sampleRate * sizeof(short)));
        var isVoice = CalculateRms(pcm16Mono) >= voiceThresholdRms;

        if (!isSpeaking)
        {
            AddPreRoll(chunk);
            if (!isVoice)
                return null;

            isSpeaking = true;
            if (preRoll.Count == 0)
                utterance.Write(chunk);
            foreach (var preRollChunk in preRoll)
                utterance.Write(preRollChunk);
            preRoll.Clear();
            preRollByteCount = 0;
        }
        else
        {
            utterance.Write(chunk);
        }

        if (isVoice)
        {
            speechMilliseconds += chunkMilliseconds;
            silenceMilliseconds = 0;
        }
        else
        {
            silenceMilliseconds += chunkMilliseconds;
        }

        if (silenceMilliseconds >= endOfSpeechSilenceMilliseconds ||
            utterance.Length >= (long)sampleRate * sizeof(short) * maximumUtteranceSeconds)
            return CompleteUtterance();

        return null;
    }

    /// <summary>Returns a final utterance when the microphone stream ends.</summary>
    public byte[]? Flush() => isSpeaking ? CompleteUtterance() : null;

    /// <summary>Releases the buffered audio memory.</summary>
    public void Dispose() => utterance.Dispose();

    private void AddPreRoll(byte[] chunk)
    {
        var maximumPreRollBytes = (int)(sampleRate * sizeof(short) * preRollMilliseconds / 1000d);
        if (maximumPreRollBytes == 0)
            return;

        preRoll.Enqueue(chunk);
        preRollByteCount += chunk.Length;
        while (preRollByteCount > maximumPreRollBytes && preRoll.TryDequeue(out var removed))
            preRollByteCount -= removed.Length;
    }

    private byte[]? CompleteUtterance()
    {
        var result = speechMilliseconds >= minimumSpeechMilliseconds ? utterance.ToArray() : null;
        utterance.SetLength(0);
        preRoll.Clear();
        preRollByteCount = 0;
        silenceMilliseconds = 0;
        speechMilliseconds = 0;
        isSpeaking = false;
        return result;
    }

    private static double CalculateRms(ReadOnlySpan<byte> pcm16Mono)
    {
        double sumSquares = 0;
        var sampleCount = pcm16Mono.Length / sizeof(short);
        for (var index = 0; index < pcm16Mono.Length; index += sizeof(short))
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(pcm16Mono[index..]);
            sumSquares += (double)sample * sample;
        }

        return Math.Sqrt(sumSquares / sampleCount);
    }
}
