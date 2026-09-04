---
name: Esi.AI Studio OpenVINO Vision
description: "Guidance for OpenVINO VLM image processing in Esi.AI Studio, including RGB tensor construction, structured chat history, model compatibility, and native runtime diagnostics."
applyTo: 'src/Esi.AI/Esi.AI.Core/ModelLoading/OpenVino*.cs'
---

# OpenVINO Vision Processing

## Supported model shape

- OpenVINO image input is available only when `OpenVinoModelLoader` loads a vision-language model directory containing `openvino_vision_embeddings_model.xml`.
- An OpenVINO GGUF text model is not automatically a vision model.
- The loader currently accepts OpenVINO GPU, `MULTI:GPU`, and NPU routes for this integration. Do not advertise CPU as an OpenVINO VLM route without changing and testing the loader.
- Qwen3.5 VLM models are guarded by the runtime compatibility check. The current code requires OpenVINO GenAI 2026.4 or later for `model_type: qwen3_5`.
- `SupportsImageInput` must reflect the active `VLMPipeline`, not only catalog metadata.

## Image decoding and tensor contract

- The transport-neutral input is `ChatImage(MediaType, Data)`.
- Decode image bytes with ImageSharp as `Rgb24` so PNG alpha and other source formats become RGB.
- Create one OpenVINO tensor per image with element type `U8` and shape `[1, height, width, 3]` (RGB NHWC).
- Preserve image order by flattening `ChatMessage.Images` in message order.
- The tensor is borrowed by the native pipeline during generation. Keep every tensor alive until the native call completes, then dispose it in the owning operation.
- Dispose already-created tensors when tensor creation fails. Do not leak native tensor handles.

The implementation belongs in `OpenVinoImageTensorFactory`; do not decode images in the controller or duplicate tensor construction in tests.

## Prompt and history

- Keep the original OpenAI structured content parts for OpenVINO. The Qwen template recognizes `image_url` parts and emits `<|vision_start|><|image_pad|><|vision_end|>`.
- Build a native `ChatHistory` from the structured JSON content and pass the image tensor array separately.
- Use `VLMPipeline.GenerateWithHistory(history, images, generationConfig)`; do not manually insert vision marker tokens into the OpenVINO history.
- The number and order of image markers in the rendered history must match the image tensor array.
- Use the current history-based API. The stateful `StartChat`/`FinishChat` path is deprecated by the wrapper.
- Preserve tool definitions and other structured fields when serializing the history.

## Runtime diagnostics

- Initialize the selected OpenVINO runtime before creating native GenAI objects.
- Keep native load errors tied to the operation (`GenAI.Initialize`, `VLMPipeline.Create`, generation configuration, or inference) so diagnostics identify the failing boundary.
- The current Qwen3.8 validation reaches native generation but has an unresolved `UNKNOWN_EXCEPTION (-17)` failure. Do not mark the OpenVINO image path complete until the real PNG WebAPI test succeeds.
- Compare text-only generation and image generation on the same loaded VLM when diagnosing failures. This separates runtime/model problems from image decoding or history problems.

## Tests

Cover RGB NHWC tensor shape and byte order, structured `text` plus `image_url` history preservation, image order, malformed image data, and a real semantic request using `test-chat-with-picture.png`.
