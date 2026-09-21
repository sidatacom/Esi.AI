---
name: esi-ai-studio-test-llama-vulkan
description: Test LLamaSharp with the Vulkan device variant.
---

Test `/backends` with backend `LLama` and device/runtime variant `Vulkan`.

Prerequisite: the approved Qwen2.5 Instruct GGUF artifact from `02a-reference-model.prompt.md`, a working Vulkan driver, and a detected Vulkan device.

Checks:
1. Open the LLama tab and select the reference GGUF model.
2. Select `Vulkan` as the backend/runtime and verify Vulkan devices and routing controls are shown.
3. Verify explicit device weights are available and persist after leaving and returning to the tab.
4. Verify requirements status identifies the Vulkan driver/device correctly.
5. Load the model; verify pending, loading, ready, and failure states with bounded waiting.
6. Send one deterministic short chat request and verify a non-empty response.
7. Verify the loaded status identifies LLama/Vulkan and appears on Overview and Provider.
8. Unload only this test load and verify all dependent views reconcile.

A missing Vulkan driver/device or reference artifact is BLOCKED. CUDA, SYCL, OpenVINO, vLLM, SGLang, and dotLLM are out of scope for this prompt. Report exact device id, model path, status transitions, response result, and cleanup.
