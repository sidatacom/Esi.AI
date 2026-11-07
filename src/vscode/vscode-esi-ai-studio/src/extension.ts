import * as vscode from "vscode";
import { EsiAiStudioConfigurationViewProvider } from "./configurationView.js";
import { EsiAiStudioProvider } from "./provider.js";
import { EsiAiStudioUsageView } from "./usageView.js";

export async function activate(
  context: vscode.ExtensionContext,
): Promise<void> {
  const usageView = new EsiAiStudioUsageView();
  const provider = new EsiAiStudioProvider(context, (usage) =>
    usageView.recordUsage(usage),
  );
  const configurationView = new EsiAiStudioConfigurationViewProvider(provider);
  const languageModelProvider = vscode.lm.registerLanguageModelChatProvider(
    "esi-ai-studio",
    provider,
  );
  context.subscriptions.push(
    provider,
    usageView,
    vscode.window.registerWebviewViewProvider(
      EsiAiStudioConfigurationViewProvider.viewId,
      configurationView,
    ),
    vscode.window.registerTreeDataProvider(
      EsiAiStudioUsageView.viewId,
      usageView,
    ),
    languageModelProvider,
    vscode.commands.registerCommand("esiAiStudio.refreshModels", async () => {
      try {
        const modelCount = await provider.refresh();
        void vscode.window.showInformationMessage(
          `Modellliste aktualisiert: ${modelCount} Modell${modelCount === 1 ? "" : "e"}.`,
        );
      } catch (error) {
        void vscode.window.showErrorMessage(
          error instanceof Error ? error.message : String(error),
        );
      }
    }),
    vscode.commands.registerCommand("esiAiStudio.inspectModels", async () => {
      try {
        const modelCount = await provider.inspectRegisteredModels();
        void vscode.window.showInformationMessage(
          `VS Code registriert ${modelCount} Esi.AI Studio Modell${modelCount === 1 ? "" : "e"}.`,
        );
      } catch (error) {
        void vscode.window.showErrorMessage(
          error instanceof Error ? error.message : String(error),
        );
      }
    }),
    vscode.commands.registerCommand("esiAiStudio.configureApiKey", () =>
      provider.configureApiKey(),
    ),
    vscode.commands.registerCommand("esiAiStudio.showUsage", () =>
      vscode.commands.executeCommand("workbench.view.extension.esiAiStudio"),
    ),
    vscode.workspace.onDidChangeConfiguration((event) => {
      if (event.affectsConfiguration("esiAiStudio")) {
        void provider.refresh().catch(() => undefined);
      }
    }),
  );

  try {
    await provider.refresh();
  } catch {}
}

export function deactivate(): void {}
