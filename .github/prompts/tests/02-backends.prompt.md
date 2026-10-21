---
name: esi-ai-studio-test-backends
description: Orchestrate the active Esi.AI Studio backend matrix tests.
---

Test route `/backends` in the running Esi.AI Studio browser session. This prompt is the backend-matrix orchestrator; execute the child prompts in the order listed below.

The browser smoke-test reference family below is the lightweight Qwen2.5 Instruct family. Passing this browser matrix establishes only the behavior of the tested reference artifacts. All artifacts in this browser matrix must derive from the same checkpoint, model variant, and revision and use the same quantization method with the same material parameters. Backend-specific formats are allowed only when conversion provenance demonstrates that these invariants are preserved. Record each artifact's format and SHA-256; hashes need not match between formats.

Active test matrix:

| Backend | Variant | Child prompt | Reference artifact |
|---|---|---|---|
| LLama | Vulkan | `02b-llama-vulkan.prompt.md` | Qwen2.5 Instruct GGUF variant |
| LLama | CUDA 12 | `02c-llama-cuda.prompt.md` | Qwen2.5 Instruct GGUF variant |
| LLama | SYCL 16 | `02d-llama-sycl.prompt.md` | Qwen2.5 Instruct GGUF variant |
| OpenVINO | configured accelerator/device | `02e-openvino.prompt.md` | Qwen2.5 Instruct OpenVINO IR variant |
| vLLM | NVIDIA / CUDA | `02f-vllm-cuda.prompt.md` | Qwen2.5 Instruct Transformers/Safetensors variant |
| vLLM | Intel / XPU | `02g-vllm-xpu.prompt.md` | Qwen2.5 Instruct Transformers/Safetensors variant |

Deferred and excluded from the current pass/fail denominator:
- SGLang: test later.
- dotLLM: test later.

Coordinator checks:
1. Open `Backends` from the main navigation and verify no visible alert or unhandled exception.
2. Verify the backend support matrix exposes LLama Vulkan/CUDA/SYCL, OpenVINO, vLLM CUDA/XPU, and the deferred SGLang/dotLLM entries without hiding their status.
3. Verify the reference-model preparation prompt has produced a provenance-verified artifact for every active row before starting a load. Confirm the shared checkpoint, variant, revision, quantization method/parameters, and each conversion tool/version/options; matching bit width alone is insufficient. Unverified artifacts are `BLOCKED`.
4. Run every active child prompt exactly once. A missing device, driver, runtime, Python environment, or model is `BLOCKED`, not `PASS`.
5. Verify every model loaded by a child prompt appears in Loaded models, Overview, and Provider, then unload only that test model.
6. After all active variants, verify no test model remains loaded and that the deferred variants are recorded as `DEFERRED`.

Never delete user data or download a model from the network without explicit test input. Report each child prompt as PASS, FAIL, or BLOCKED, and report SGLang/dotLLM as DEFERRED. Include the exact variant, device route, reference artifact path/id, visible evidence, load/unload result, and cleanup result.
