---
name: esi-ai-studio-test-qwen38-openvino-128k
description: Test provider streaming and bounded long-context stability for Qwen3.8 Uncensored on OpenVINO at 128k.
---

Run a focused stability check for the local Qwen3.8 Uncensored OpenVINO model. Do not run the full browser suite.

## Preconditions

- Studio is ready at `http://127.0.0.1:7010` and the OpenVINO runtime reports the Intel GPU route ready.
- The application catalog maps model `Qwen3.8-27B-Uncensored-int4-awq-g128-ov` to the saved configuration `Qwen3.8-27B-Variante1`, with backend `OpenVINO` and the matching local model path.
- The loaded model is that exact configuration. If another model is already loaded, do not replace it; report `BLOCKED`.
- Workspace setting `esiAiStudio.maxInputTokens` is `131072`. Refresh provider models and confirm the selected agent model is from vendor `esi-ai-studio`.
- Before loading the model, require at least 16 GiB available host memory. After load, require at least 8 GiB available and no increasing swap use; otherwise report `BLOCKED` and do not send long-context probes.
- Do not save, assign, or update model configuration while selecting the test model.

## Checks

1. Record Studio session ID, host readiness, GPU device, model path, configuration ID, and loaded status. Confirm the model config supports at least 131072 input tokens.
2. Verify the VS Code provider leg through this agent's own model request. In `/tmp/esi-ai-studio-provider.jsonl`, correlate only the new request ID and confirm `POST /chat/completions`, HTTP 200, `text/event-stream`, a terminal `[DONE]`, non-empty output, and usage metadata. Never print prompt contents, credentials, or full trace payloads.
3. Run one short baseline completion through the local OpenAI-compatible endpoint and confirm that the exact loaded model responds and remains loaded.
4. Using synthetic, non-sensitive input and a single visible EsiMCP terminal session, escalate through one 32768-token request and then one 65536-token request. Stream each response, request at most 8 output tokens, and use `usage.prompt_tokens` as the authority. After each response, verify Studio status responds within 10 seconds and the model remains loaded before proceeding.
5. Only if both staged requests pass and the memory gate still passes, run one 125000-131072-token request through `POST /v1/chat/completions` using the configuration ID as the model. Do not run a second 128k request in the same session. Record prompt tokens, completion tokens, tokens/sec, and wall time without logging prompt or response content.
6. Stop immediately on any error, timeout, missing `[DONE]`, health-check delay, host shutdown, or lost model state. Do not automatically retry, cancel a still-running request by starting another, or restart Studio. Verify provider status/trace and model state after the run.

The long-context endpoint probes validate OpenVINO/backend stability, not the VS Code provider's transport. Report the provider smoke and backend probes as separate results. Never report a 128k provider `PASS` unless the provider trace itself contains a successful request with at least 125000 prompt tokens.

## Result

Return `PASS`, `FAIL`, or `BLOCKED` separately for the provider leg and the OpenVINO 128k leg. Include exact model/configuration IDs, device, memory gate result, observed token counts, request completion evidence, and cleanup state. State explicitly if the agent's provider request stayed below 125000 tokens.