# Esi.AI Studio Audio Chat

The audio-chat API is exposed through the existing SignalR hub at `/hubs/data`. The terminal owns microphone capture, playback, and avatar rendering. Studio owns VAD, Whisper transcription, chat generation, and Piper synthesis.

## Runtime configuration

Configure these environment variables for the Studio host:

```text
AudioChat__WhisperExecutablePath=/path/to/whisper-cli
AudioChat__WhisperModelPath=/path/to/ggml-large-v3-turbo.bin
AudioChat__PiperExecutablePath=/path/to/piper
AudioChat__PiperModelPath=/path/to/de_DE-thorsten-medium.onnx
AudioChat__VisemeAnalyzerExecutablePath=/path/to/rhubarb
AudioChat__VisemeAnalyzerTimeoutSeconds=30
AudioChat__VisemeAnalyzerThreadCount=1
```

`whisper-cli` must be built with Vulkan support to use a supported NVIDIA or Intel GPU. Piper runs as a local CPU process. Studio does not download either executable or model; `AudioChat_Readiness` reports missing configuration before a session starts.

Viseme analysis uses [Rhubarb Lip Sync](https://github.com/DanielSWolf/rhubarb-lip-sync), an offline command-line analyzer with Windows, macOS, and Linux releases. Rhubarb is MIT-licensed; its upstream license page lists permissively licensed third-party dependencies and the bundled CMU US-English model's BSD license. Configure the executable path to the unpacked `rhubarb`/`rhubarb.exe`; no separate viseme model is required. The analyzer is optional: `isReady` continues to mean STT/TTS audio chat is available, while `visemeAnalyzerReady` and `visemeAnalyzerIssue` report cue-analysis availability independently.

Rhubarb's phonetic recognizer is intended for non-English audio and does not use a language model, so the exact generated WAV can be analyzed without sending text to the analyzer. Upstream describes this mode as less precise than its English-only PocketSphinx recognizer; German-specific accuracy has not been benchmarked here. Treat these as approximate animation cues, not phoneme ground truth or guaranteed lip synchronization. Rhubarb uses CPU threads; the default of one worker and 30-second timeout limit its impact on the Studio host.

The [Montreal Forced Aligner (MFA)](https://github.com/MontrealCorpusTools/Montreal-Forced-Aligner) can produce German phone timings when supplied with both the audio and its transcript, but requires separately installed German acoustic/dictionary models and model-version management; its German model is CC BY 4.0. Rhubarb was chosen for this first integration because it directly emits coarse timed mouth shapes, has a native Windows release, and requires no transcript/model setup. If the requirement becomes German phoneme-level timing, evaluate MFA separately rather than presenting these cues as that level of alignment.

## Terminal protocol

1. Invoke `AudioChat_Readiness`; start only when `isReady` is `true`.
2. Invoke `AudioChat_Start` with `chatId`, `modelPath`, `backend`, and optional `backendVariantId`. The reply gives a `sessionId`, `sampleRate` (16000), input format (`pcm_s16le_mono`), and the maximum accepted chunk size.
3. While the radar is enabled, invoke `AudioChat_AudioChunk` with the `sessionId` and a `pcm16Mono` byte array. Send signed little-endian PCM16, mono, 16 kHz audio in short chunks (about 100–200 ms).
4. Handle `AudioChat_State`, `AudioChat_Transcript`, `AudioChat_Text`, `AudioChat_AudioChunk`, `AudioChat_TurnCompleted`, and `AudioChat_Error`. Synthesized chunks are WAV audio, emitted per sentence. Each audio event now includes `audioDurationMilliseconds` and `visemeCues`; these fields are additive, so existing audio-only clients may ignore them.
5. After all queued chunks for a turn have finished playing, invoke `AudioChat_PlaybackCompleted(sessionId, turnId)`. This lets the server resume listening only after playback, reducing speaker-to-microphone feedback.
6. Invoke `AudioChat_Stop(sessionId)` when the radar is disabled. The server flushes the final detected utterance before stopping.

States are `listening`, `transcribing`, `responding`, `awaiting-playback`, and `stopped`. While the assistant is responding or awaiting playback, input chunks are rejected with reason `assistant-speaking`; this initial implementation is half-duplex and does not support barge-in.

Audio is kept in memory during VAD and chat orchestration. Whisper's temporary WAV and transcript files are removed after each transcription.

Audio-runtime and SignalR failures are logged and reported through `AudioChat_Error` without stopping Studio. Backend inference failures continue to use Studio's existing fatal inference-failure handling.

## Viseme cue contract

When `visemeAnalyzerReady` was true at session start, Studio passes the exact WAV byte array returned by Piper to Rhubarb and sends that same byte array with the resulting cues in one `AudioChat_AudioChunk` event. Text is not supplied to Rhubarb. If no analyzer was ready when the session began, `visemeCues` is `null`; if configured analysis fails, Studio reports `AudioChat_Error` instead of sending a successful-looking empty cue list.

`audioDurationMilliseconds` is the WAV duration rounded up to the nearest millisecond. Each cue has:

- `offsetMilliseconds`: start offset from the beginning of this specific WAV chunk.
- `durationMilliseconds`: interval length; its end is offset plus duration.
- `visemeId`: stable renderer-independent shape ID: `neutral`, `closed`, `teethClosed`, `open`, `wideOpen`, `rounded`, `pursed`, `teethOnLowerLip`, or `tongueRaised`.
- `weight`: normalized target weight from 0 to 1. Rhubarb supplies categorical mouth shapes rather than blend weights, so generated cues use `1`.

Cues are ordered, non-overlapping intervals whose bounds are validated against the WAV duration. The terminal must schedule them against the playback clock for that chunk, keyed by its `sessionId`, `turnId`, and `sequence`; do not use SignalR arrival time or the server clock. When playback stops or a turn/session ends, the eventual client should return its mapped GLB morphs to neutral. Audio chunks and cues are atomic in the existing event; playback acknowledgements and audio-only payload fields remain unchanged.

Rhubarb analyzes recorded audio and emits time ranges for broad mouth shapes, not a phoneme transcript or per-frame facial animation. The client owns the mapping from these IDs to its GLB's available morph targets. Inspect the upstream [recognizer and mouth-shape documentation](https://github.com/DanielSWolf/rhubarb-lip-sync/blob/master/README.adoc) and [license and bundled dependency notices](https://github.com/DanielSWolf/rhubarb-lip-sync/blob/master/LICENSE.md) before redistributing its binary.
