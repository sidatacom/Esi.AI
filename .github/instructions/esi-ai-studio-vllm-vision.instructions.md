---
name: Esi.AI Studio vLLM Vision
description: "Guidance for vLLM image handling in Esi.AI Studio and the current limitation that the local Python/gRPC bridge is text-only."
applyTo: 'src/Esi.AI/Esi.AI.Core/ModelLoading/PythonInferenceServer.cs'
---

# vLLM Vision Processing

## Current capability

- The Esi.AI Studio vLLM integration uses a local Python process and a gRPC bridge.
- The current `PythonInferenceChatSession` maps normalized chat messages into the existing text-generation gRPC request. It does not load image bytes or expose multimodal tensors.
- The OpenAI controller therefore rejects image requests for the vLLM backend with `unsupported_request_error`.
- Keep `imageInput` false unless a complete, tested multimodal implementation exists end to end.

## Change boundary

- Do not forward local Base64 data URLs as arbitrary text and do not silently discard `ChatMessage.Images`.
- Do not add a one-off HTTP route around the Python bridge. The application architecture requires the existing controller contract and Core bridge boundary.
- A future vLLM vision implementation must define an explicit transport-neutral media contract, Python bridge serialization, image lifetime rules, capability discovery, and semantic tests before enabling the capability.
- Preserve current vLLM loading, device selection, startup readiness, streaming, cancellation, and process cleanup behavior.

## Validation

- Text-only vLLM requests must remain unchanged.
- Image requests must be rejected clearly until the multimodal path is implemented.
- Do not use an upstream vLLM feature as evidence that the Esi.AI Studio local bridge supports images; validate this integration specifically.
