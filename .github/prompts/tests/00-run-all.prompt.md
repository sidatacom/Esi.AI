---
name: run-test-all
description: Execute the complete Esi.AI Studio browser test plan in navigation order.
---

Run the following prompts in order and do not skip a prompt silently:
1. `01-overview.prompt.md`
2. `02-backends.prompt.md`
3. `02a-reference-model.prompt.md`
4. `02b-llama-vulkan.prompt.md`
5. `02c-llama-cuda.prompt.md`
6. `02d-llama-sycl.prompt.md`
7. `02e-openvino.prompt.md`
8. `02f-vllm-cuda.prompt.md`
9. `02g-vllm-xpu.prompt.md`
10. `03-models.prompt.md`
11. `04-webapi.prompt.md`
12. `05-chats.prompt.md`
13. `06-provider.prompt.md`
14. `07-settings.prompt.md`
15. `08-auth.prompt.md`
16. `09-navigation.prompt.md`

Execution rules:
- Start only after confirming the Studio host is ready and the browser URL is reachable.
- Use the shared browser session and follow navigation links where the individual prompt requires it.
- Execute independent checks in the listed order; preserve state needed by later prompts.
- Use only disposable test data and approved local models/downloads.
- Mark a check `BLOCKED` when its prerequisite is absent. Never convert BLOCKED to PASS by assumption.
- Stop and report immediately on data loss, a security issue, an unhandled exception, or a hang.
- Redact credentials, tokens, cookies, request bodies containing secrets, and personal data.
- Clean up every artifact created by the test run.

Final report format:

# Esi.AI Studio browser test report
Date/time: <timestamp>
Base URL: <url>
Host readiness: PASS/FAIL

| Navigation | Prompt | PASS | FAIL | BLOCKED | Notes |
|---|---|---:|---:|---:|---|
| Overview | 01 | 0 | 0 | 0 | |
| Reference model | 02a | 0 | 0 | 0 | |
| LLama Vulkan | 02b | 0 | 0 | 0 | |
| LLama CUDA 12 | 02c | 0 | 0 | 0 | |
| LLama SYCL 16 | 02d | 0 | 0 | 0 | |
| OpenVINO | 02e | 0 | 0 | 0 | |
| vLLM CUDA | 02f | 0 | 0 | 0 | |
| vLLM XPU | 02g | 0 | 0 | 0 | |
| SGLang | deferred | - | - | - | Test later |
| dotLLM | deferred | - | - | - | Test later |
| Models | 03 | 0 | 0 | 0 | |
| Web API | 04 | 0 | 0 | 0 | |
| Chats | 05 | 0 | 0 | 0 | |
| Provider | 06 | 0 | 0 | 0 | |
| Settings | 07 | 0 | 0 | 0 | |
| Auth required | 08 | 0 | 0 | 0 | |
| Navigation shell | 09 | 0 | 0 | 0 | |

Overall result: PASS only when no check is FAIL and every BLOCKED item is explicitly explained.
Include failing route, exact visible error, reproduction steps, severity, and recommended next diagnostic. Do not claim that untested functionality passed.
