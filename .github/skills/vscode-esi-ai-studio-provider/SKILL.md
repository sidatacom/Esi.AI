---
name: vscode-esi-ai-studio-provider
description: "Use when diagnosing or changing the Esi.AI Studio VS Code language-model provider, provider API calls, OpenVINO WebAPI chat, tool optimization hangs, SSE streaming, model registration, request logging, or /tmp/esi-ai-studio-provider.jsonl."
---

# Esi.AI Studio VS Code Provider

Use this skill for the provider in `src/vscode/vscode-esi-ai-studio` and its OpenAI-compatible Studio API integration.

## First diagnostic step

Read `/tmp/esi-ai-studio-provider.jsonl` before changing code. The provider logs JSONL events named `request`, `response`, `error`, and `sse` when `esiAiStudio.loggingEnabled` is enabled. A custom path may be configured through `esiAiStudio.loggingPath`.

Correlate entries by `requestId` and check, in order:

1. `GET /v1/models` returns the expected loaded model.
2. `POST /v1/chat/completions` uses the backend model id, `stream: true`, and the intended `messages`.
3. Tool-enabled requests contain `tools` and the expected `tool_choice`.
4. The response is `text/event-stream` and produces content or `tool_calls` chunks.
5. The stream ends with `[DONE]`; a missing terminator explains a chat that remains busy after receiving text.
6. `error` entries and repeated `GET /models` requests are correlated with the same attempt.

Do not print prompt contents or credentials unnecessarily. Authorization is expected to be redacted in provider traces.

## Provider path

- Provider implementation: `src/vscode/vscode-esi-ai-studio/src/provider.ts`
- Generated bundle: `src/vscode/vscode-esi-ai-studio/dist/extension.js`
- Extension metadata: `src/vscode/vscode-esi-ai-studio/package.json`
- Install workflow: `src/vscode/vscode-esi-ai-studio/scripts/install.sh`
- Studio API controller: `src/Esi.AI/Esi.AI.Studio/Controllers/OpenAiCompatibleController.cs`

The provider maps VS Code language-model messages and tools to `/v1/chat/completions`, parses SSE chunks, reports `LanguageModelToolCallPart` values, and reports usage after `[DONE]`.

## Change and validation rules

For provider behavior changes:

- Increment the extension patch version in `package.json`.
- Keep `package-lock.json`, `dist/extension.js`, and the VSIX synchronized.
- Build and install the newly versioned VSIX using the repository workflow.
- Reload VS Code or restart the Extension Host before validating model registration.
- Validate the complete path: Studio `/v1/models`, provider mapping, installed bundle, and VS Code model registration.

For Studio changes, follow the watchdog and debug lifecycle in `.github/copilot-instructions.md`. Use the monitored VS Code debug session and do not run an unmonitored Studio process.
