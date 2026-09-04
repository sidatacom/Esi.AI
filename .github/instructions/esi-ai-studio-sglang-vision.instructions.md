---
name: Esi.AI Studio SGLang Vision
description: "Guidance for SGLang image handling in Esi.AI Studio and the current limitation that the local Python/gRPC bridge is text-only."
applyTo: 'src/Esi.AI/Esi.AI.Core/ModelLoading/PythonInferenceServer.cs'
---

# SGLang Vision Processing

## Current capability

- The Esi.AI Studio SGLang integration shares the local Python process and gRPC bridge with the vLLM route.
- The current `PythonInferenceChatSession` sends text-generation requests only. It does not load image bytes, encode multimodal content, or expose image tensors to SGLang.
- The OpenAI controller rejects image requests for the SGLang backend with `unsupported_request_error`.
- Keep `imageInput` false until a complete, tested SGLang multimodal path is available.

## Change boundary

- Never download remote URLs or pass Base64 data URLs as a text workaround.
- Do not bypass the central Python/gRPC integration with a new HTTP call or ad hoc process.
- A future implementation must specify the bridge media contract, image serialization, model capability detection, request ordering, cancellation, cleanup, and real semantic tests.
- Keep existing SGLang process startup, XPU/CUDA environment setup, readiness checks, streaming, and shutdown unchanged.

## Validation

- Preserve text-only SGLang behavior.
- Reject image input explicitly while unsupported.
- SGLang upstream multimodal support does not imply support in this local Esi.AI Studio bridge; only an end-to-end integration test can enable the capability.
