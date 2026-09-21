---
name: esi-ai-studio-test-vllm-xpu
description: Test vLLM with the Intel XPU device variant.
---

Test `/backends` with backend `vLLM`, Intel XPU Python runtime, and `Intel / XPU` device route.

Prerequisite: the approved Qwen2.5 Instruct Transformers/Safetensors reference id from `02a-reference-model.prompt.md`, the Intel XPU vLLM Python environment, Intel XPU runtime/driver, and an approved `xpu:0` or `xpu:1` route.

Checks:
1. Open the vLLM tab and select the reference model id.
2. Select the Intel XPU Python executable/profile and verify the XPU device route is shown.
3. Verify Intel/XPU requirements status and Python runtime diagnostics are actionable.
4. Verify CUDA routes cannot be mixed into this worker configuration.
5. Start the model; verify queued, starting, ready, and failure states with bounded waiting.
6. Send one deterministic short chat request through the running vLLM worker and verify a non-empty response.
7. Verify the loaded status identifies vLLM/XPU on Overview and Provider.
8. Stop/unload only this test worker and verify no process or loaded model remains.

A missing Intel XPU runtime/device, Python environment, or model id is BLOCKED. Report executable, device, port, model id, status transitions, response result, and cleanup. SGLang is out of scope.
