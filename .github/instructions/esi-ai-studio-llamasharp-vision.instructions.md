---
name: Esi.AI Studio LLamaSharp Vision
description: "Guidance for LLamaSharp MTMD image processing in Esi.AI Studio, including mmproj discovery, media markers, executor lifetime, and multimodal chat ordering."
applyTo: 'src/Esi.AI/Esi.AI.Core/ModelLoading/LlamaModelLoader.cs'
---

# LLamaSharp Vision Processing

## Required model assets

- LLamaSharp vision requires a GGUF text model plus a compatible MTMD projector (`mmproj`) GGUF.
- Pass an explicit `MmprojPath` when configuration identifies one. The loader may resolve an unambiguous `*mmproj*.gguf` sidecar in the model directory; multiple candidates require an explicit path.
- Load the projector with `MtmdWeights.LoadFromFileAsync` and `MtmdContextParams.Default()` after loading the text weights.
- `MtmdWeights.SupportsVision` is the runtime source of truth for `SupportsImageInput`. Do not infer vision support from a filename or catalog flag alone.
- If the projector is missing or incompatible, fail model loading clearly and dispose the already-loaded text weights.
- Models without an MTMD projector remain valid text-only models.

## Executor and media flow

- A text-only session uses `InteractiveExecutor(context)`.
- A multimodal session uses `InteractiveExecutor(context, mtmdWeights)`.
- For every image, call `MtmdWeights.LoadMedia(image.Data)` before generation.
- Replace image positions in the complete message content with the MTMD media marker. Resolve the marker from `MtmdContextParams.Default().MediaMarker`, then `NativeApi.MtmdDefaultMarker()`, with `<media>` only as a fallback.
- Use `ChatMessageContentPart` order to interleave text and media markers. Do not append all markers after the text when structured content provides positions.
- Clear pending media after a turn and on preparation failure. Media state is owned by the MTMD runtime and must not leak across requests.
- Preserve historical message order and keep image count, loaded media count, and marker count aligned.

## API boundary

- The controller accepts only local Base64 image data URLs and normalizes them into `ChatImage` bytes before Core code runs.
- The Core session must receive transport-neutral `ChatMessage` values, not OpenAI JSON.
- CPU and Vulkan Llama routes can support images only when the loaded model's MTMD runtime reports vision support. Text-only Llama sessions must continue to work without MTMD.
- Never silently remove image parts. If MTMD vision is unavailable, return an unsupported-image error before generation.

## Lifetime and concurrency

- The loader owns `LLamaWeights` and optional `MtmdWeights` together in the loaded-model record.
- Dispose projector and text weights together when unloading or replacing a model.
- Do not dispose shared MTMD weights while active chat sessions still use them; coordinate session and model lifetime through the existing loader locks.
- Keep cancellation and streaming behavior unchanged while adding media handling.

## Tests

Cover explicit and automatic `mmproj` resolution, missing or incompatible projectors, `SupportsVision`, text-only fallback, media-marker ordering, media cleanup, and a real `/v1/chat/completions` request with the test PNG and a compatible MTMD model.
