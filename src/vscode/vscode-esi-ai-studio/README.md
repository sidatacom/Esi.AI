# Esi.AI Studio Models for VS Code

This extension exposes the local models detected by Esi.AI Studio as native VS Code language models. A model must be loaded in Studio before it can answer chat requests.

## Setup

1. Start Esi.AI Studio and load a model.
2. Install this extension in VS Code.
3. Select the model from the VS Code Chat model picker.

The default API URL is `http://127.0.0.1:7010/v1`. Change `esiAiStudio.baseUrl` when Studio runs elsewhere.

The provider uses the OpenAI-compatible endpoints `GET /models` and `POST /chat/completions` with streaming enabled.

The extension adds an **Esi.AI Studio** icon to the VS Code activity bar. Its **Provider** view lets you configure the base URL, request timeout, request logging, and log path, test the connection, refresh models, and open the secure API-key dialog. Request traces are JSONL and include a copyable curl replay command; Authorization values are never logged. The provider is also listed in VS Code as **Esi.AI Studio**. If the icon does not appear after installation, run **Developer: Reload Window**.

## Local installation

From the repository root, run:

```bash
cd src/vscode/vscode-esi-ai-studio
npm install
npm run install:local
```

The routine builds the extension, creates a versioned VSIX, removes the previous local installation, installs the new VSIX with the `code` CLI, and verifies the extension identifier. Set `CODE_BIN=code-insiders` when using VS Code Insiders.

After installation, reload VS Code, start Esi.AI Studio on port `7010`, and load a model. The model appears in the VS Code Chat model picker; **Manage Models** can be used to hide or pin it. If the list is not current, run **Esi AI Studio: Refresh Models** from the Command Palette.

For an authenticated deployment, run **Esi AI Studio: Configure API Key** from the Command Palette. The key is stored in VS Code SecretStorage. The `ESI_AI_STUDIO_API_KEY` environment variable is used as a fallback.

Request logging is enabled by default for compatibility with earlier diagnostic builds. Disable it with `esiAiStudio.loggingEnabled` or the **Provider** view. Configure `esiAiStudio.loggingPath` with an absolute path; an empty value uses `os.tmpdir()/esi-ai-studio-provider.jsonl`.

Configure `esiAiStudio.reasoningEffort` with `none`, `low`, `medium`, `high`, `xhigh`, or `max`. A reasoning option supplied by VS Code for an individual request takes precedence over this setting.

## Development

```bash
npm install
npm run build
```

Press `F5` in this folder to launch an Extension Development Host.