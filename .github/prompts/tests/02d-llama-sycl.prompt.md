---
name: esi-ai-studio-test-llama-sycl
description: Test LLamaSharp with the Intel SYCL 16 device variant.
---

Test `/backends` with backend `LLama` and device/runtime variant `SYCL 16`.

Prerequisite: the approved Qwen2.5 Instruct GGUF artifact from `02a-reference-model.prompt.md`, Intel oneAPI SYCL/Level Zero runtime, and a detected Intel SYCL device.

Checks:
1. Open the LLama tab and select the reference GGUF model.
2. Select `SYCL 16`; verify SYCL device routing and the selected native device are shown.
3. Verify SYCL device selection persists after leaving and returning to the tab.
4. Verify requirements status identifies the Intel SYCL/Level Zero prerequisite accurately.
5. Load the model; verify pending, loading, ready, and failure states with bounded waiting.
6. Send one deterministic short chat request and verify a non-empty response.
7. Verify the loaded status identifies LLama/SYCL and appears on Overview and Provider.
8. Unload only this test load and verify all dependent views reconcile.

A missing SYCL runtime/device or reference artifact is BLOCKED. Vulkan, CUDA, OpenVINO, vLLM, SGLang, and dotLLM are out of scope. Report exact device id, model path, status transitions, response result, and cleanup.
