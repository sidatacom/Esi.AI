import * as vscode from "vscode";

let outputChannel: vscode.OutputChannel | undefined;

export function initLogger(): vscode.OutputChannel {
  if (!outputChannel) {
    outputChannel = vscode.window.createOutputChannel("EsiMCP");
  }
  return outputChannel;
}

export function log(message: string): void {
  const timestamp = new Date().toISOString();
  outputChannel?.appendLine(`[${timestamp}] ${message}`);
}

export function logError(message: string, error?: unknown): void {
  const timestamp = new Date().toISOString();
  const errorStr =
    error instanceof Error ? error.message : JSON.stringify(error);
  outputChannel?.appendLine(`[${timestamp}] ERROR: ${message} - ${errorStr}`);
}

export function disposeLogger(): void {
  outputChannel?.dispose();
  outputChannel = undefined;
}
