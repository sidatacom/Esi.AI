---
name: esi-ai-studio-benchmark-qwen38-all-backends
description: Create or verify Benchmark profiles and run one sequential 128k Copilot-style Qwen3.8 request per ready Esi.AI Studio backend.
---

Run the standardized 128k Qwen3.8 agent-style benchmark sequentially across every backend variant listed in the benchmark guide. Do not run the full browser suite.

The request is a single-turn coding-agent-shaped performance probe. It does not exercise Copilot tool calling, repository inspection, or code edits; the long context is deterministic synthetic padding.

## Preconditions

1. Read `benchmarks/qwen38-backend-benchmark.md`. Its table is authoritative for registered variants, compatible Qwen3.8-27B artifacts, routes, saved profiles, and readiness. The model family is common; use each backend's documented artifact rather than substituting one backend's artifact for another.
2. Use the existing Studio at `http://127.0.0.1:7010` or `ESI_STUDIO_URL`. Confirm it is already ready before interacting with it. Do not start another Studio instance. If it is unavailable, report `BLOCKED` and stop.
3. In Studio, inspect every registered backend variant in the guide. For each, reuse a saved configuration named exactly `Benchmark` only if it already selects that variant's documented Qwen3.8-27B artifact and route. Otherwise create and save a separate configuration named exactly `Benchmark` for that backend, with `AutoLaunch=false`, context 131072 where supported, and the documented backend-specific load options. Do not overwrite, rename, or delete any existing configuration. Record the exact Studio configuration ID from Studio/API state; never derive an ID from a display name.
4. Create/verify configurations before benchmarking. Do not install dependencies, repair runtimes, or work around a failed Studio configuration dialog. If configuration creation fails, mark that backend `BLOCKED` and continue only with other variants whose configurations can be verified.
5. A backend may be benchmarked only when the guide explicitly marks its runtime ready. Mark other variants `BLOCKED` without loading them or attempting repair/install. Do not treat a saved profile alone as runtime readiness.
6. Before each eligible backend, require at least 16 GiB available RAM. If available RAM is below 4 GiB, swap is growing, or runtime health is degraded, mark that backend `BLOCKED` and stop the remaining run sequence. Run one backend at a time; load its exact Benchmark configuration, verify the loaded configuration ID and route, run the probe, unload it, and confirm Studio remains ready and resources are safe before proceeding.
7. Use the repository's EsiMCP terminal workflow for the runner. Read and follow `.github/skills/vscode-terminal/SKILL.md` before any EsiMCP terminal operation. Do not use an alternative shell.

## Execution

Use the existing dependency-free runner with its fixed prompt and sampling parameters. For every runtime-ready backend with a verified loaded Benchmark configuration, run exactly one request without warm-up:

```sh
node benchmarks/qwen38-benchmark.mjs --model <EXACT_CONFIGURATION_ID> --repeat-count 127850 --warmups 0 --runs 1
```

Replace the ID with the exact ID confirmed for the loaded profile, not its display name. Finish one backend fully, including unload and health/memory checks, before starting the next. Stop the remaining sequence on an error, timeout, missing terminal SSE usage event, loss of Studio/model readiness, or unsafe memory pressure. Do not blindly retry. The runner's `usage.prompt_tokens` is authoritative; a measurement is complete only when `completionLengthMatchesLimit=true` and `fits131072Context=true`.

## Report

Report one row per registered backend variant with status `PASS`, `FAIL`, `BLOCKED`, or `NOT RUN`, including exact configuration ID (if available), backend variant, route, artifact, configuration verification, runtime readiness, Studio readiness, and stop reason where applicable.

For each completed probe, include prompt/completion tokens, TTFT, client decode rate, wall time, finish reason, and available RAM/swap before and after. Overall status is `PASS` only if every guide-listed backend is runtime-ready and every probe completes with both required flags true; otherwise report `FAIL` for a failed probe or `BLOCKED` for prerequisites/readiness that prevent full coverage. Mark later variants `NOT RUN` after a stop condition. Do not include the prompt body, generated response text, secrets, or unrelated trace payloads.
