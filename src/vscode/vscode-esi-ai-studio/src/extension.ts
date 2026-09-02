import * as vscode from "vscode";
import { EsiAiStudioConfigurationViewProvider } from "./configurationView.js";
import { EsiAiStudioProvider } from "./provider.js";

export async function activate(context: vscode.ExtensionContext): Promise<void> {
  const provider = new EsiAiStudioProvider(context);
  const configurationView = new EsiAiStudioConfigurationViewProvider(provider);
  const languageModelProvider = vscode.lm.registerLanguageModelChatProvider("esi-ai-studio", provider);
  provider.startAutomaticRefresh();
  context.subscriptions.push(
    provider,
    vscode.window.registerWebviewViewProvider(EsiAiStudioConfigurationViewProvider.viewId, configurationView),
    languageModelProvider,
    vscode.commands.registerCommand("esiAiStudio.refreshModels", async () => {
      try {
        const modelCount = await provider.refresh();
        void vscode.window.showInformationMessage(`Modellliste aktualisiert: ${modelCount} Modell${modelCount === 1 ? "" : "e"}.`);
      } catch (error) {
        void vscode.window.showErrorMessage(error instanceof Error ? error.message : String(error));
      }
    }),
    vscode.commands.registerCommand("esiAiStudio.inspectModels", async () => {
      try {
        const modelCount = await provider.inspectRegisteredModels();
        void vscode.window.showInformationMessage(`VS Code registriert ${modelCount} Esi.AI Studio Modell${modelCount === 1 ? "" : "e"}.`);
      } catch (error) {
        void vscode.window.showErrorMessage(error instanceof Error ? error.message : String(error));
      }
    }),
    vscode.commands.registerCommand("esiAiStudio.configureApiKey", () => provider.configureApiKey()),
    vscode.workspace.onDidChangeConfiguration((event) => {
      if (event.affectsConfiguration("esiAiStudio")) {
        void provider.refresh().catch(() => undefined);
      }
    }),
  );

  try {
    await provider.refresh();
  } catch {
  }
}

export function deactivate(): void {}