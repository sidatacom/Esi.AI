# Esi.AI Studio Audio Chat

The audio-chat API is exposed through the existing SignalR hub at `/hubs/data`. The terminal owns microphone capture, playback, and avatar rendering. Studio owns VAD, Whisper transcription, chat generation, and Piper synthesis.

## Runtime configuration

Configure these environment variables for the Studio host:

```text
AudioChat__WhisperExecutablePath=/path/to/whisper-cli
AudioChat__WhisperModelPath=/path/to/ggml-large-v3-turbo.bin
AudioChat__PiperExecutablePath=/path/to/piper
AudioChat__PiperModelPath=/path/to/de_DE-thorsten-medium.onnx
```

`whisper-cli` must be built with Vulkan support to use a supported NVIDIA or Intel GPU. Piper runs as a local CPU process. Studio does not download either executable or model; `AudioChat_Readiness` reports missing configuration before a session starts.

## Terminal protocol

1. Invoke `AudioChat_Readiness`; start only when `isReady` is `true`.
2. Invoke `AudioChat_Start` with `chatId`, `modelPath`, `backend`, and optional `backendVariantId`. The reply gives a `sessionId`, `sampleRate` (16000), input format (`pcm_s16le_mono`), and the maximum accepted chunk size.
3. While the radar is enabled, invoke `AudioChat_AudioChunk` with the `sessionId` and a `pcm16Mono` byte array. Send signed little-endian PCM16, mono, 16 kHz audio in short chunks (about 100–200 ms).
4. Handle `AudioChat_State`, `AudioChat_Transcript`, `AudioChat_Text`, `AudioChat_AudioChunk`, `AudioChat_TurnCompleted`, and `AudioChat_Error`. Synthesized chunks are WAV audio, emitted per sentence.
5. After all queued chunks for a turn have finished playing, invoke `AudioChat_PlaybackCompleted(sessionId, turnId)`. This lets the server resume listening only after playback, reducing speaker-to-microphone feedback.
6. Invoke `AudioChat_Stop(sessionId)` when the radar is disabled. The server flushes the final detected utterance before stopping.

States are `listening`, `transcribing`, `responding`, `awaiting-playback`, and `stopped`. While the assistant is responding or awaiting playback, input chunks are rejected with reason `assistant-speaking`; this initial implementation is half-duplex and does not support barge-in.

Audio is kept in memory during VAD and chat orchestration. Whisper's temporary WAV and transcript files are removed after each transcription.

Audio-runtime and SignalR failures are logged and reported through `AudioChat_Error` without stopping Studio. Backend inference failures continue to use Studio's existing fatal inference-failure handling.
