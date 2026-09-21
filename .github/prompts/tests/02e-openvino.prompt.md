---
name: esi-ai-studio-test-openvino
description: Test OpenVINO with the configured accelerator/device variant.
---

Test `/backends` with backend `OpenVINO` and the detected configured device.

Prerequisite: the approved Qwen2.5 Instruct OpenVINO IR artifact from `02a-reference-model.prompt.md` and a compatible OpenVINO device. Record whether the selected device is CPU, GPU, NPU, or another supported route.

Checks:
1. Open the OpenVINO tab and select the reference OpenVINO IR model.
2. Run OpenVINO diagnostics and verify device, vendor, driver, compatibility, and readiness are visible.
3. Verify device enable/disable controls, priority/weight controls, configuration profile selection, and assignment behavior.
4. Verify model loading is disabled when diagnostics report an incompatible or unavailable device.
5. With a ready device, load the model and verify pending, loading, ready, and failure states with bounded waiting.
6. Send one deterministic short chat request and verify a non-empty response.
7. Verify the loaded status identifies OpenVINO and the selected device on Overview and Provider.
8. Unload only this test load and verify all dependent views reconcile.

A missing IR artifact or compatible device is BLOCKED. Report exact device, diagnostics result, model path, status transitions, response result, and cleanup.
