import * as vscode from "vscode";

let outputChannel: vscode.OutputChannel | undefined;
let debugOutputChannel: vscode.OutputChannel | undefined;

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

export function beginDebugOutput(message: string): void {
  debugOutputChannel ??= vscode.window.createOutputChannel("EsiMCP Debug");
  debugOutputChannel.appendLine(`\n[${new Date().toISOString()}] ${message}`);
  debugOutputChannel.show(true);
}

export function appendDebugOutput(chunk: string): void {
  debugOutputChannel?.append(chunk);
}

export function finishDebugOutput(message: string, exitCode: number): void {
  debugOutputChannel?.appendLine(`\n[${new Date().toISOString()}] ${message}: exit code ${exitCode}`);
}

export function disposeLogger(): void {
  outputChannel?.dispose();
  debugOutputChannel?.dispose();
  outputChannel = undefined;
  debugOutputChannel = undefined;
}
