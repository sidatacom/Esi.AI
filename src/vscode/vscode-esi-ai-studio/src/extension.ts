import * as vscode from "vscode";
import { EsiAiStudioProvider } from "./provider.js";

export function activate(context: vscode.ExtensionContext): void {
  const provider = new EsiAiStudioProvider(context);
  context.subscriptions.push(
    provider,
    vscode.lm.registerLanguageModelChatProvider("esi-ai-studio", provider),
    vscode.commands.registerCommand("esiAiStudio.refreshModels", () => provider.refresh()),
    vscode.commands.registerCommand("esiAiStudio.configureApiKey", () => provider.configureApiKey()),
    vscode.workspace.onDidChangeConfiguration((event) => {
      if (event.affectsConfiguration("esiAiStudio")) {
        void provider.refresh();
      }
    }),
  );
}

export function deactivate(): void {}