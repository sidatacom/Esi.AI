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
    int MaximumChunkBytes);

/// <summary>Reports whether a microphone chunk was queued for processing.</summary>
public sealed record AudioChatChunkReceipt(bool Accepted, string? Reason = null);

/// <summary>Reports an audio-chat state transition to the terminal client.</summary>
public sealed record AudioChatStateUpdate(Guid SessionId, string State);

/// <summary>Contains the recognized text for one detected speech turn.</summary>
public sealed record AudioChatTranscriptUpdate(Guid SessionId, Guid TurnId, string Text);

/// <summary>Contains an incremental assistant-text update for one speech turn.</summary>
public sealed record AudioChatTextUpdate(Guid SessionId, Guid TurnId, string Delta);

/// <summary>Contains one synthesized WAV chunk for terminal playback.</summary>
public sealed record AudioChatAudioChunk(
    Guid SessionId,
    Guid TurnId,
    int Sequence,
    string ContentType,
    byte[] Data);

/// <summary>Signals that the assistant response and its audio have completed.</summary>
public sealed record AudioChatTurnCompleted(Guid SessionId, Guid TurnId, PersistedChat Chat);

/// <summary>Reports an audio-chat failure to the terminal client.</summary>
public sealed record AudioChatError(Guid SessionId, string Message);
