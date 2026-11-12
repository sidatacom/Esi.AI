namespace Esi.AI.Models;

/// <summary>Starts a continuous audio-chat session for an existing persisted chat.</summary>
/// <param name="ChatId">The persisted chat to continue.</param>
/// <param name="ModelPath">The loaded chat model path.</param>
/// <param name="Backend">The selected chat backend.</param>
/// <param name="BackendVariantId">The selected backend route, when applicable.</param>
public sealed record AudioChatStartRequest(
    Guid ChatId,
    string ModelPath,
    string Backend,
    string? BackendVariantId = null);

/// <summary>Contains one little-endian, signed 16-bit mono PCM microphone chunk.</summary>
/// <param name="SessionId">The active audio-chat session.</param>
/// <param name="Pcm16Mono">Audio sampled at the rate reported by the session.</param>
public sealed record AudioChatAudioChunkRequest(Guid SessionId, byte[] Pcm16Mono);

/// <summary>Describes the audio format and limits for an active session.</summary>
public sealed record AudioChatSessionInfo(
    Guid SessionId,
    int SampleRate,
    string InputFormat,
    int MaximumChunkBytes);

/// <summary>Reports whether the configured speech runtimes and models are available.</summary>
public sealed record AudioChatRuntimeReadiness(
    bool IsReady,
    bool WhisperReady,
    string? WhisperIssue,
    bool PiperReady,
    string? PiperIssue,
    string? ConfigurationIssue,
    int SampleRate,
    int MaximumChunkBytes,
    bool VisemeAnalyzerReady = false,
    string? VisemeAnalyzerIssue = null);

/// <summary>Reports whether a microphone chunk was queued for processing.</summary>
public sealed record AudioChatChunkReceipt(bool Accepted, string? Reason = null);

/// <summary>Reports an audio-chat state transition to the terminal client.</summary>
public sealed record AudioChatStateUpdate(Guid SessionId, string State);

/// <summary>Contains the recognized text for one detected speech turn.</summary>
public sealed record AudioChatTranscriptUpdate(Guid SessionId, Guid TurnId, string Text);

/// <summary>Contains an incremental assistant-text update for one speech turn.</summary>
public sealed record AudioChatTextUpdate(Guid SessionId, Guid TurnId, string Delta);

/// <summary>Contains one synthesized WAV chunk and optional playback-relative viseme cues.</summary>
/// <param name="SessionId">The active audio-chat session.</param>
/// <param name="TurnId">The assistant turn that produced the audio.</param>
/// <param name="Sequence">The zero-based order of this chunk within the turn.</param>
/// <param name="ContentType">The audio media type.</param>
/// <param name="Data">The synthesized WAV bytes.</param>
/// <param name="AudioDurationMilliseconds">The WAV duration rounded up to milliseconds.</param>
/// <param name="VisemeCues">Cue intervals relative to this WAV chunk, or null when unavailable.</param>
public sealed record AudioChatAudioChunk(
    Guid SessionId,
    Guid TurnId,
    int Sequence,
    string ContentType,
    byte[] Data,
    int AudioDurationMilliseconds = 0,
    IReadOnlyList<AudioChatVisemeCue>? VisemeCues = null);

/// <summary>One normalized mouth-shape interval on an audio-chunk-relative timeline.</summary>
/// <param name="OffsetMilliseconds">The interval start from the associated WAV chunk's beginning.</param>
/// <param name="DurationMilliseconds">The interval length in milliseconds.</param>
/// <param name="VisemeId">A stable renderer-independent mouth-shape identifier.</param>
/// <param name="Weight">The normalized target weight from 0 to 1.</param>
public sealed record AudioChatVisemeCue(
    int OffsetMilliseconds,
    int DurationMilliseconds,
    string VisemeId,
    float Weight);

/// <summary>Stable normalized mouth-shape identifiers used by AudioChat viseme cues.</summary>
public static class AudioChatVisemeIds
{
    /// <summary>Relaxed mouth during silence.</summary>
    public const string Neutral = "neutral";
    /// <summary>Closed lips for bilabial sounds.</summary>
    public const string Closed = "closed";
    /// <summary>Slightly open mouth with clenched teeth.</summary>
    public const string TeethClosed = "teethClosed";
    /// <summary>Open mouth for mid-open vowels.</summary>
    public const string Open = "open";
    /// <summary>Wide-open mouth.</summary>
    public const string WideOpen = "wideOpen";
    /// <summary>Slightly rounded mouth.</summary>
    public const string Rounded = "rounded";
    /// <summary>Puckered lips.</summary>
    public const string Pursed = "pursed";
    /// <summary>Upper teeth touch the lower lip.</summary>
    public const string TeethOnLowerLip = "teethOnLowerLip";
    /// <summary>Tongue raised behind the upper teeth.</summary>
    public const string TongueRaised = "tongueRaised";
}

/// <summary>Signals that the assistant response and its audio have completed.</summary>
public sealed record AudioChatTurnCompleted(Guid SessionId, Guid TurnId, PersistedChat Chat);

/// <summary>Reports an audio-chat failure to the terminal client.</summary>
public sealed record AudioChatError(Guid SessionId, string Message);
