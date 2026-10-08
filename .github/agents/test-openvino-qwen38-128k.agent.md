---
name: test-openvino-qwen38-128k
description: "Run a bounded 128k stability check for Qwen3.8 Uncensored on Esi.AI Studio OpenVINO and the VS Code provider."
model: "Qwen3.8.27B-Variante1 (esi-ai-studio)"
tools:
  - vscode
  - read
  - search
  - browser
  - esimcp/vscode_debug_list_commands
  - esimcp/vscode_debug_execute_command
  - esimcp/vscode_terminal_list_commands
  - esimcp/vscode_terminal_execute_command
  - todo
---

Run `.github/prompts/tests/02e-qwen38-openvino-128k.prompt.md` exactly. This agent's own model requests are the VS Code provider leg of the test. Never claim the long-context endpoint probe validates the VS Code provider transport; report the provider and backend results separately.

Do not edit production code, mutate saved model profiles, switch or unload a pre-existing model, restart Studio, or retry a failed long-context request. Stop at the first timeout, error, runtime state loss, or host shutdown and report the evidence.