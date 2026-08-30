# Esi.AI Studio Models for VS Code

This extension exposes the models currently loaded by Esi.AI Studio as native VS Code language models.

## Setup

1. Start Esi.AI Studio and load a model.
2. Install this extension in VS Code.
3. Select the model from the VS Code Chat model picker.

The default API URL is `http://127.0.0.1:7010/v1`. Change `esiAiStudio.baseUrl` when Studio runs elsewhere.

The provider uses the OpenAI-compatible endpoints `GET /models` and `POST /chat/completions` with streaming enabled.

For an authenticated deployment, run **Esi AI Studio: Configure API Key** from the Command Palette. The key is stored in VS Code SecretStorage. The `ESI_AI_STUDIO_API_KEY` environment variable is used as a fallback.

## Development

```bash
npm install
npm run build
```

Press `F5` in this folder to launch an Extension Development Host.