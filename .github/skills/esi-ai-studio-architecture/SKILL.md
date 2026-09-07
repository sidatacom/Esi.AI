---
name: esi-ai-studio-architecture
description: 'Use when exploring, reviewing, refactoring, or debugging Esi.AI Studio architecture, SignalR state, model lifecycle, inference routing, or streaming.'
---

# Esi.AI Studio Architecture Skill

Use this skill to keep architecture work aligned with the current Esi.AI Studio boundaries.

## Ownership Map

- `Esi.AI.Studio` owns the server host, `DataService`, `DataHub`, the single `OpenAiCompatibleController`, and `ModelRuntime` integration.
- `Esi.AI.Studio.Client` owns browser pages and client-side service abstractions. Pages should use `IDataService` and state services instead of direct hub or server dependencies.
- `Esi.AI.Models` owns shared request, response, status, and SignalR contract DTOs.
- `ModelRuntime` owns LLama, OpenVINO, Python, and dotLLM runtime resources.
- `InferenceService` owns normalized SignalR chat generation. `OpenAiCompatibleBackendMiddleware` owns normalized OpenAI-compatible generation for streaming and non-streaming requests.

## Investigation Workflow

1. Start from the reported behavior, owning symbol, or failing request.
2. Follow the nearest code path that directly computes state or invokes inference.
3. Check the server source of truth and the corresponding SignalR event or HTTP contract.
4. Separate request validation, user cancellation, runtime failure, and transport failure before changing behavior.
5. Prefer a focused test or a clean fresh-process reproduction that can disconfirm the hypothesis.
6. Make the smallest change at the owning boundary and validate it before exploring adjacent code.

## Lifecycle Rules

- Client-visible collections use explicit `<Entity>_Create`, `<Entity>_Read`, `<Entity>_Update`, and `<Entity>_Delete` operations.
- Model status is server-owned. Full initialization uses `<Entity>_Read`; active operations reconcile from pushed updates rather than polling.
- Any backend inference failure is fatal: unload every backend runtime, publish cleared model state, and request host shutdown once. Preserve normal request validation and caller cancellation.
- Do not add fixed tool counts, truncation, description stripping, schema rewrites, provider-specific hacks, watchdogs, PID files, or startup blockades to hide native failures.

## Review Hotspots

- Direct component event subscriptions can create stale UI or disposal issues; verify subscription and render scheduling together.
- Channel-based streaming can obscure the original generation exception; preserve exception propagation and completion semantics.
- Runtime status can become stale after native failure; compare API state, SignalR state, and runtime ownership.
- When a native request fails, invalidate evidence from later requests in the same process. Reproduce again after a clean model load before assigning a schema or tool-count root cause.

## Validation

- Use only projects under `src/` in the `Esi.*` namespace for builds and tests.
- Check the active Studio debug session before build or test operations.
- Start model loads and other potentially long-running commands asynchronously from the beginning; use synchronous commands only for short checks.
- Validate the narrowest changed slice first, then run the relevant Studio or Core project tests.
