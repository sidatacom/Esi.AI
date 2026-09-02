import { randomBytes } from "node:crypto";
import { tmpdir } from "node:os";
import { join } from "node:path";
import * as vscode from "vscode";
import { EsiAiStudioProvider } from "./provider.js";

const defaultTraceFilePath = join(tmpdir(), "esi-ai-studio-provider.jsonl");

export class EsiAiStudioConfigurationViewProvider implements vscode.WebviewViewProvider {
  public static readonly viewId = "esiAiStudio.configuration";
  private view?: vscode.WebviewView;

  public constructor(private readonly provider: EsiAiStudioProvider) {}

  public resolveWebviewView(webviewView: vscode.WebviewView): void {
    this.view = webviewView;
    webviewView.webview.options = { enableScripts: true };
    webviewView.webview.html = this.getHtml();
    webviewView.webview.onDidReceiveMessage((message: { command?: string; value?: unknown }) => this.handleMessage(message));
    webviewView.onDidDispose(() => {
      if (this.view === webviewView) {
        this.view = undefined;
      }
    });
  }

  private async handleMessage(message: { command?: string; value?: unknown }): Promise<void> {
    try {
      switch (message.command) {
        case "saveBaseUrl":
          await this.saveSetting("baseUrl", message.value, "Base URL gespeichert.");
          return;
        case "saveTimeout":
          await this.saveSetting("requestTimeoutMs", message.value, "Timeout gespeichert.");
          return;
        case "saveLoggingEnabled":
          if (typeof message.value !== "boolean") {
            await this.postStatus("Bitte den Logging-Schalter verwenden.", true);
            return;
          }
          await this.provider.updateLoggingSetting("loggingEnabled", message.value);
          await this.postStatus(message.value ? "Request-Logging aktiviert." : "Request-Logging deaktiviert.");
          return;
        case "saveLoggingPath":
          await this.saveSetting("loggingPath", message.value, "Logpfad gespeichert.", true);
          return;
        case "refresh":
          await this.postStatus("Modellliste wird aktualisiert.");
          await this.provider.refresh();
          await this.postStatus("Modellliste aktualisiert.");
          return;
        case "testConnection":
          await this.postStatus("Verbindung wird geprüft.");
          const modelCount = await this.provider.testConnection();
          await this.postStatus(`Verbindung erfolgreich. ${modelCount} Modell${modelCount === 1 ? "" : "e"} gefunden.`);
          return;
        case "configureApiKey":
          await vscode.commands.executeCommand("esiAiStudio.configureApiKey");
          await this.postStatus("API-Key-Dialog geöffnet.");
          return;
        default:
          return;
        }
    } catch (error) {
      await this.postStatus(error instanceof Error ? error.message : String(error), true);
    }
  }

  private async saveSetting(key: string, value: unknown, successMessage: string, allowEmpty = false): Promise<void> {
    if (typeof value !== "string" || (!allowEmpty && value.trim().length === 0)) {
      await this.postStatus("Bitte einen Wert eingeben.", true);
      return;
    }

    const configuration = vscode.workspace.getConfiguration("esiAiStudio");
    const settingValue = key === "requestTimeoutMs" ? Number(value) : value.trim();
    if (typeof settingValue === "number" && (!Number.isFinite(settingValue) || settingValue < 1000)) {
      await this.postStatus("Der Timeout muss mindestens 1000 ms betragen.", true);
      return;
    }

    try {
      await configuration.update(key, settingValue, vscode.ConfigurationTarget.Global);
      await this.provider.refresh();
      await this.postStatus(successMessage);
    } catch (error) {
      await this.postStatus(error instanceof Error ? error.message : String(error), true);
    }
  }

  private async postStatus(message: string, isError = false): Promise<void> {
    const view = this.view;
    if (view) {
      await view.webview.postMessage({ message, isError });
      return;
    }

    if (isError) {
      void vscode.window.showErrorMessage(message);
    } else {
      void vscode.window.showInformationMessage(message);
    }
  }

  private getHtml(): string {
    const configuration = vscode.workspace.getConfiguration("esiAiStudio");
    const baseUrl = escapeHtml(configuration.get<string>("baseUrl", "http://127.0.0.1:7010/v1"));
    const timeout = configuration.get<number>("requestTimeoutMs", 120000).toString();
    const loggingEnabled = this.provider.getLoggingEnabled();
    const loggingPath = escapeHtml(this.provider.getLoggingPath().trim() || defaultTraceFilePath);
    const nonce = randomBytes(16).toString("base64");

    return `<!DOCTYPE html>
<html lang="de">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<style>
  :root { color-scheme: light dark; }
  body { color: var(--vscode-foreground); background: var(--vscode-sideBar-background); font: var(--vscode-font-weight) var(--vscode-font-size)/1.45 var(--vscode-font-family); padding: 10px 12px 18px; }
  h1 { font-size: 18px; line-height: 1.25; margin: 0 0 5px; }
  h2 { color: var(--vscode-descriptionForeground); font-size: 11px; letter-spacing: .08em; margin: 22px 0 9px; text-transform: uppercase; }
  p { color: var(--vscode-descriptionForeground); margin: 0 0 14px; }
  label { display: grid; gap: 5px; margin: 12px 0; }
  label span { font-size: 11px; }
  input { background: var(--vscode-input-background); border: 1px solid var(--vscode-input-border, transparent); color: var(--vscode-input-foreground); box-sizing: border-box; padding: 6px 7px; width: 100%; }
  button { background: var(--vscode-button-background); border: 0; color: var(--vscode-button-foreground); cursor: pointer; padding: 6px 9px; width: 100%; }
  button:hover { background: var(--vscode-button-hoverBackground); }
  button.secondary { background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); }
  .status { border-left: 2px solid var(--vscode-testing-iconPassed); display: none; font-size: 12px; margin: 14px 0; padding: 5px 8px; }
  .status.error { border-color: var(--vscode-testing-iconFailed); }
  .hint { font-size: 12px; }
  .endpoint { color: var(--vscode-textLink-foreground); font-family: var(--vscode-editor-font-family); font-size: 12px; overflow-wrap: anywhere; }
</style>
</head>
<body>
  <h1>Esi.AI Studio</h1>
  <p>Lokaler Model Provider für VS Code Chat.</p>
  <div class="status" id="status" role="status" aria-live="polite"></div>

  <h2>Verbindung</h2>
  <label><span>Base URL</span><input id="base-url" type="url" value="${baseUrl}" spellcheck="false"></label>
  <button id="save-url" type="button">Base URL speichern</button>
  <label><span>Request Timeout (ms)</span><input id="timeout" type="number" min="1000" step="1000" value="${timeout}"></label>
  <button id="save-timeout" type="button">Timeout speichern</button>

  <h2>Logging</h2>
  <label><span><input id="logging-enabled" type="checkbox" ${loggingEnabled ? "checked" : ""}> Request-Logging aktivieren</span></label>
  <label><span>Logpfad</span><input id="logging-path" type="text" value="${loggingPath}" spellcheck="false"></label>
  <button id="save-logging-path" type="button">Logpfad speichern</button>

  <h2>Modelle</h2>
  <p class="endpoint">GET /models</p>
  <button id="test-connection" class="secondary" type="button">Verbindung testen</button>
  <br>
  <button id="refresh" type="button">Modelle aktualisieren</button>

  <h2>Sicherheit</h2>
  <p class="hint">Der API-Key wird ausschließlich im VS-Code SecretStorage verwaltet.</p>
  <button id="api-key" class="secondary" type="button">API-Key konfigurieren</button>

  <script nonce="${nonce}">
    const vscode = acquireVsCodeApi();
    const status = document.getElementById("status");
    const showStatus = (message, isError = false) => {
      status.textContent = message;
      status.classList.toggle("error", isError);
      status.style.display = "block";
    };
    document.getElementById("save-url").addEventListener("click", () => vscode.postMessage({ command: "saveBaseUrl", value: document.getElementById("base-url").value }));
    document.getElementById("save-timeout").addEventListener("click", () => vscode.postMessage({ command: "saveTimeout", value: document.getElementById("timeout").value }));
    document.getElementById("logging-enabled").addEventListener("change", event => vscode.postMessage({ command: "saveLoggingEnabled", value: event.target.checked }));
    document.getElementById("save-logging-path").addEventListener("click", () => vscode.postMessage({ command: "saveLoggingPath", value: document.getElementById("logging-path").value }));
    document.getElementById("test-connection").addEventListener("click", () => { showStatus("Verbindung wird geprüft."); vscode.postMessage({ command: "testConnection" }); });
    document.getElementById("refresh").addEventListener("click", () => { showStatus("Modellliste wird aktualisiert."); vscode.postMessage({ command: "refresh" }); });
    document.getElementById("api-key").addEventListener("click", () => vscode.postMessage({ command: "configureApiKey" }));
    window.addEventListener("message", event => showStatus(event.data.message, event.data.isError));
  </script>
</body>
</html>`;
  }
}

function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, character => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    "\"": "&quot;",
    "'": "&#39;",
  })[character] ?? character);
}
