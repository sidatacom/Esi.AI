---
name: esi-ai-studio-test-vllm-cuda
description: Test vLLM with the NVIDIA CUDA device variant.
---

Test `/backends` with backend `vLLM`, Python runtime, and `NVIDIA / CUDA` device route.

Prerequisite: the approved Qwen2.5 Instruct Transformers/Safetensors reference id from `02a-reference-model.prompt.md`, a working vLLM Python environment, NVIDIA CUDA driver/runtime, and `cuda:0` or the approved CUDA route.

Checks:
1. Open the vLLM tab and select the reference model id.
2. Select the NVIDIA/CUDA Python executable/profile and verify device `cuda:0` is shown.
3. Verify CUDA requirements status and Python runtime diagnostics are actionable.
4. Verify XPU routes cannot be mixed into this worker configuration.
5. Start the model; verify queued, starting, ready, and failure states with bounded waiting.
6. Send one deterministic short chat request through the running vLLM worker and verify a non-empty response.
7. Verify the loaded status identifies vLLM/CUDA on Overview and Provider.
8. Stop/unload only this test worker and verify no process or loaded model remains.

A missing Python environment, CUDA device, model id, or runtime is BLOCKED. Report executable, device, port, model id, status transitions, response result, and cleanup. SGLang is out of scope.
