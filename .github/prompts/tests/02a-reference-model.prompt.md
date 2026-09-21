---
name: esi-ai-studio-test-reference-model
description: Prepare and validate the common backend reference model family.
---

Prepare the reference artifacts before running any active backend variant.

Canonical rule:
- Use one logical Qwen2.5 Instruct reference-model family and the same revision where the backend supports it.
- The physical artifact is backend-specific: GGUF for LLama, OpenVINO IR for OpenVINO, and Transformers/Safetensors for vLLM.
- Do not silently substitute the existing SmolLM2 or Qwen 1.5B artifact and call it the common reference model. Record any migration gap as BLOCKED.

Required artifact matrix:

| Consumer | Required format | Required identity | Required setting |
|---|---|---|---|
| LLama Vulkan/CUDA/SYCL | GGUF | Qwen2.5 Instruct GGUF, pinned revision | `ESI_LLAMA_MODEL_PATH` |
| OpenVINO | OpenVINO IR | Qwen2.5 Instruct INT4, pinned revision | `ESI_OPENVINO_MODEL_PATH` |
| vLLM CUDA/XPU | Transformers/Safetensors | Qwen2.5 Instruct, pinned revision | `ESI_VLLM_REFERENCE_MODEL` |

Checks:
1. Verify each artifact exists, is readable, and has the expected format.
2. Verify all artifacts identify the same Qwen2.5 Instruct family and record revision/hash where available.
3. Verify the GGUF path is accepted by the local model scanner and the Transformers id is resolvable by the configured Python environment.
4. Verify no download occurs without explicit approval; missing artifacts are BLOCKED.
5. Record model path/id, format, revision, size, and source without exposing credentials.

Result is PASS only when every active backend row has its required artifact. SGLang and dotLLM artifacts are not required for this plan and remain DEFERRED.
