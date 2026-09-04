---
name: Esi.AI Studio dotLLM Vision
description: "Guidance for dotLLM image handling in Esi.AI Studio and the current text-only in-process integration."
applyTo: 'src/Esi.AI/Esi.AI.Core/ModelLoading/DotLlmInProcessRuntime.cs'
---

# dotLLM Vision Processing

## Current capability

- The Esi.AI Studio dotLLM integration loads GGUF weights in-process and generates text with `TextGenerator`.
- `DotLlmInProcessChatSession` maps role and text content into the dotLLM chat template. It does not consume `ChatImage` or `ChatMessageContentPart.ImageIndex`.
- The controller must reject image requests for dotLLM with `unsupported_request_error`.
- Do not advertise `imageInput` for dotLLM or pretend that a text-only template understands an image marker.

## Change boundary

- Keep dotLLM text generation, tokenizer selection, chat templates, CPU-only execution, and resource disposal unchanged when working on unrelated API or vision code.
- A future image feature requires a proven dotLLM multimodal runtime/API, an explicit image contract, template support, capability discovery, resource ownership, and semantic tests.
- Do not route dotLLM images through OpenVINO, LLamaSharp, or an untracked external service.
- Do not silently drop image parts from a request.

## Validation

- Text-only dotLLM requests remain supported.
- Image requests fail clearly before generation.
- A future capability flag may be enabled only after a real image request is processed by the dotLLM runtime and the returned answer is semantically verified.
