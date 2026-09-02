---
name: install-esi-ai-studio
description: Build, install, and verify the Esi.AI Studio VS Code language-model provider.
argument-hint: Optional VS Code CLI name, for example code-insiders.
---

Install the Esi.AI Studio language-model provider into the current VS Code installation.

1. Work from the repository root at `/home/llm/Git/Esi.AI`.
2. Confirm that Node.js, npm, and the VS Code CLI are available. Use the argument as `CODE_BIN` when one is provided; otherwise use `code`.
3. Run `npm install` in `src/vscode/vscode-esi-ai-studio` when dependencies are missing.
4. Run `CODE_BIN=<cli> npm run install:local` in `src/vscode/vscode-esi-ai-studio`.
5. Verify that `sidatacom.vscode-esi-ai-studio` appears in `<cli> --list-extensions`.
6. Tell the user to reload VS Code, start Esi.AI Studio, load a model, and run `Esi AI Studio: Refresh Models`.
7. Verify the provider by checking that the loaded model appears in the VS Code Chat model picker. If Studio is not running or no model is loaded, report that prerequisite instead of claiming the provider is ready.

Do not install or modify `vscode-esi-mcp`; this prompt is only for the Esi.AI Studio language-model provider. Do not claim success unless the VS Code extension list confirms the exact extension identifier.