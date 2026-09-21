---
name: esi-ai-studio-test-llama-cuda
description: Test LLamaSharp with the NVIDIA CUDA 12 device variant.
---

Test `/backends` with backend `LLama` and device/runtime variant `CUDA 12`.

Prerequisite: the approved Qwen2.5 Instruct GGUF artifact from `02a-reference-model.prompt.md`, a working NVIDIA CUDA 12 runtime/driver, and a detected CUDA device.

Checks:
1. Open the LLama tab and select the reference GGUF model.
2. Select `CUDA 12`; verify CUDA device routing and the selected native device are shown.
3. Verify the CUDA setting persists after leaving and returning to the tab.
4. Verify requirements status identifies the NVIDIA/CUDA prerequisite accurately.
5. Load the model; verify pending, loading, ready, and failure states with bounded waiting.
6. Send one deterministic short chat request and verify a non-empty response.
7. Verify the loaded status identifies LLama/CUDA and appears on Overview and Provider.
8. Unload only this test load and verify all dependent views reconcile.

A missing CUDA runtime/device or reference artifact is BLOCKED. Vulkan, SYCL, OpenVINO, vLLM, SGLang, and dotLLM are out of scope. Report exact device id, model path, status transitions, response result, and cleanup.
