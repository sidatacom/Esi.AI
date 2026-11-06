---
name: Esi.AI Studio vLLM Vision
description: "Guidance for vLLM image handling in Esi.AI Studio through the local Python/gRPC bridge."
applyTo: 'src/Esi.AI/Esi.AI.Core/ModelLoading/PythonInferenceServer.cs'
---

# vLLM Vision Processing

## Current capability

- The Esi.AI Studio vLLM integration uses a local Python process and a gRPC bridge.
- The gRPC contract transports decoded-image source bytes and ordered text/image content parts. The shared Python helper decodes images, builds the model chat-template content, and returns image tensors through vLLM `multi_modal_data`.
- Advertise image input only when the loaded model's Hugging Face configuration exposes `vision_config`; text-only vLLM models remain unable to accept images.
- Use a local Base64 data URL with an `image/*` media type. Remote URLs are rejected before reaching the bridge.
- Keep per-image input bounded by the WebAPI's 20 MiB limit and preserve image ordering across the gRPC request and vLLM prompt.

## Change boundary

- Do not forward local Base64 data URLs as arbitrary text and do not silently discard `ChatMessage.Images`.
- Do not add a one-off HTTP route around the Python bridge. The application architecture requires the existing controller contract and Core bridge boundary.
- Keep the Core, CUDA 12, and XPU protobuf contracts and Python descriptors synchronized.
- Close decoded image objects after generation or on every error/cancellation path.
- Retain the image capability check in the API middleware and the Python bridge; the former reports unsupported requests while the latter protects direct bridge callers.
- Preserve current vLLM loading, device selection, startup readiness, streaming, cancellation, and process cleanup behavior.

## Validation

- Text-only vLLM requests must remain unchanged and continue to report the backend's decode rate separately from end-to-end request throughput.
- Verify gRPC image-byte/content-part mapping, image order and lifetime, model capability detection, text-only model rejection, and a semantic image request against a loaded vision-capable model.
- Do not use an upstream vLLM feature as evidence that the Esi.AI Studio local bridge supports images; validate this integration specifically.
