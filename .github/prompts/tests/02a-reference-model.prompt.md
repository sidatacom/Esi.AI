---
name: esi-ai-studio-test-reference-model
description: Prepare and validate the common backend reference model family.
---

Prepare the reference artifacts before running any active backend variant.

Canonical rule:
- This prompt validates each saved Reference profile as an independent functional smoke test. It does not certify artifacts for cross-backend performance comparison.
- Each backend may use its own model artifact, format, checkpoint, variant, revision, and quantization. Differences in those artifacts do not block load/chat/unload testing.
- Record the exact profile ID, model path, and artifact format actually tested. Do not describe the artifacts as a common reference family or compare their performance unless their provenance is independently verified.
- Do not silently substitute an artifact during a test. Use the model path saved in the Reference profile and report that exact artifact.

Required artifact matrix:

| Consumer | Example format | Profile-specific setting |
|---|---|---|
| LLama Vulkan/CUDA/SYCL | GGUF | `ESI_LLAMA_MODEL_PATH` or the saved profile path |
| OpenVINO | OpenVINO IR | `ESI_OPENVINO_MODEL_PATH` or the saved profile path |
| vLLM CUDA/XPU | Transformers/Safetensors | `ESI_VLLM_REFERENCE_MODEL` or the saved profile path |

Checks:
1. Verify the exact artifact path saved in each profile exists, is readable, and is accepted by that backend.
2. Run the existing profile without silently substituting a different model or downloading an artifact.
3. A missing path, incompatible format, unavailable runtime, or failed load/chat/unload is BLOCKED or FAIL for that backend; it does not block independent tests of other profiles.
4. Record the profile ID, backend variant, device route, model path, artifact format, and load/chat/unload outcome. Record checkpoint, revision, quantization, conversion details, and hashes when available, but missing provenance does not block a functional smoke test.
5. Do not claim cross-backend artifact equivalence or performance comparability without verified provenance. SGLang and dotLLM artifacts are not required for this plan and remain DEFERRED.

Result is PASS only when every active backend's own Reference profile loads, returns a non-empty response, and unloads successfully. Each result applies only to the exact artifact tested.
