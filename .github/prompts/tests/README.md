# Esi.AI Studio browser test prompts

These prompts define a repeatable browser test plan for the Studio navigation. The numeric order follows `NavMenu.razor` and is the execution order used by `00-run-all.prompt.md`.

## Prompt order

1. Overview: shell status and dashboard refresh.
2. Backends: support matrix and shared lifecycle orchestration.
3. Reference model: prepare one Qwen2.5 Instruct model family in backend-specific formats.
4. LLama Vulkan: load, chat, and unload on Vulkan.
5. LLama CUDA: load, chat, and unload on CUDA 12.
6. LLama SYCL: load, chat, and unload on SYCL 16.
7. OpenVINO: load, chat, and unload on the configured accelerator/device.
8. vLLM CUDA: load, chat, and unload on NVIDIA CUDA.
9. vLLM XPU: load, chat, and unload on Intel XPU.
10. SGLang and dotLLM are deferred and excluded from the current result.
11. Models: local library, Hugging Face search, filters, downloads, and deletion safeguards.
12. Web API: configuration persistence, validation, models endpoint, and bounded chat smoke test.
13. Chats: chat creation, model selection, message flow, separation, and cleanup.
14. Provider: provider status, model list, trace entries, and error visibility.
15. Settings: persistence, validation, and restoration.
16. Auth required: unauthenticated/authenticated navigation and account flows.
17. Navigation shell: route transitions, refresh, history, brand link, and Not Found.

Run `00-run-all.prompt.md` for the complete plan. Individual prompts are intentionally standalone so a failed or blocked page or backend variant can be rerun without repeating the entire suite.

## Result rules

- `PASS`: the behavior was executed and the expected result was observed.
- `FAIL`: the behavior was executed and contradicted the expected result, crashed, hung, or caused data loss.
- `BLOCKED`: a required runtime, model, network permission, auth fixture, or disposable identity was unavailable.
- Never report `PASS` for a skipped or assumed check.
- A backend result must include its exact device variant; a generic backend PASS is invalid.
- The reference model is one logical model family, not one physical file: GGUF, OpenVINO IR, and Transformers/Safetensors are required packaging variants.
- SGLang and dotLLM remain `DEFERRED` until their dedicated prompts are added.
- Redact secrets and clean up disposable artifacts after every run.
