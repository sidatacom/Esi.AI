---
name: "Test vLLM"
description: "Loads the saved exact vLLM profile and runs a narrow end-to-end chat smoke test in Esi.AI Studio."
model: "Qwen2.5-0.5B-Instruct (esi-ai-studio)"
tools: [vscode, execute, read, agent, GitHub.vscode-pull-request-github/issue_fetch, GitHub.vscode-pull-request-github/labels_fetch, GitHub.vscode-pull-request-github/notification_fetch, GitHub.vscode-pull-request-github/doSearch, GitHub.vscode-pull-request-github/activePullRequest, GitHub.vscode-pull-request-github/pullRequestStatusChecks, GitHub.vscode-pull-request-github/openPullRequest, GitHub.vscode-pull-request-github/create_pull_request, GitHub.vscode-pull-request-github/resolveReviewThread, ms-python.python/getPythonEnvironmentInfo, ms-python.python/getPythonExecutableCommand, ms-python.python/installPythonPackage, ms-python.python/configurePythonEnvironment, edit, search, web, 'esimcp/csharp_devkit_list_commands', 'esimcp/csharp_devkit_execute_command', 'esimcp/vscode_terminal_list_commands', 'esimcp/vscode_terminal_execute_command', 'microsoft.fluentui.aspnetcore.mcpserver/*', 'microsoftdocs/mcp/*', browser, 'pylance-mcp-server/*', todo]
---

Test the saved exact Qwen3.8 vLLM profile through the Esi.AI Studio application and OpenAI-compatible chat paths.

Before sending chat:
- Read `GET http://127.0.0.1:7010/v1/application/models/catalog`.
- Select the configuration named `Qwen3.8-27B-B70-Article-Exact` and its matching model entry.
- Send both returned IDs to `POST http://127.0.0.1:7010/v1/application/models/load` as `{"modelId":"...","configurationId":"..."}`.
- Verify the load response reports backend `vLLM` and the application model status is loaded. Do not substitute a direct vLLM port-8000 request for this step.
- Only then use the resulting Studio model through `GET /v1/models` and `POST /v1/chat/completions`.

The expected persisted settings are: GPTQ, float16, max model length 131072, GPU memory utilization 88, FP8 KV cache, MTP4, max sequences 64, max batched tokens 8192, prefix caching disabled, XPU graph disabled, BF16 MTP draft enabled, and Intel Arc Pro B70 selected.

Constraints:
- Do not edit production code.
- Use a short deterministic prompt and verify a non-empty response.
- Report the exact model id, backend, HTTP status, finish reason, and any error.
- Report the selected model/configuration IDs and whether the full saved configuration load succeeded before chat.
- Active structured tools must be reported as unsupported if the direct vLLM engine path rejects them; do not treat that as a successful tool test.
- Do not print credentials or full prompts unnecessarily.
