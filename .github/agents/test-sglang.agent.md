---
name: "Test SGLang"
description: "Runs a narrow end-to-end chat smoke test against a loaded SGLang model in Esi.AI Studio."
model: "Qwen2.5-0.5B-Instruct (esi-ai-studio)"
tools: [vscode, execute, read, agent, GitHub.vscode-pull-request-github/issue_fetch, GitHub.vscode-pull-request-github/labels_fetch, GitHub.vscode-pull-request-github/notification_fetch, GitHub.vscode-pull-request-github/doSearch, GitHub.vscode-pull-request-github/activePullRequest, GitHub.vscode-pull-request-github/pullRequestStatusChecks, GitHub.vscode-pull-request-github/openPullRequest, GitHub.vscode-pull-request-github/create_pull_request, GitHub.vscode-pull-request-github/resolveReviewThread, ms-python.python/getPythonEnvironmentInfo, ms-python.python/getPythonExecutableCommand, ms-python.python/installPythonPackage, ms-python.python/configurePythonEnvironment, edit, search, web, 'esimcp/csharp_devkit_list_commands', 'esimcp/csharp_devkit_execute_command', 'esimcp/vscode_terminal_list_commands', 'esimcp/vscode_terminal_execute_command', 'microsoft.fluentui.aspnetcore.mcpserver/*', 'microsoftdocs/mcp/*', browser, 'pylance-mcp-server/*', todo]
---

Test the currently loaded SGLang model through the Esi.AI Studio OpenAI-compatible chat path.
Use the existing `/v1/models` and `/v1/chat/completions` contracts.

Constraints:
- Do not edit production code.
- Use a short deterministic prompt and verify a non-empty response.
- Report the exact model id, backend, HTTP status, finish reason, and any error.
- Do not print credentials or full prompts unnecessarily.
