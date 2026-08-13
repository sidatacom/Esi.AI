import * as vscode from "vscode";
import { initLogger, log, disposeLogger } from "./utils/logger.js";
import { SessionManager } from "./terminal/session-manager.js";
import { DebugManager } from "./debug/manager.js";
import { createConfiguredMcpRequestHandler, startMcpHttpServer, type McpHttpServer } from "./mcp-http-server.js";
import { normalizeBindHosts, normalizePort } from "./config.js";

let mcpHttpServer: McpHttpServer | undefined;
let sessionManager: SessionManager | undefined;
let debugManager: DebugManager | undefined;
let statusBarItem: vscode.StatusBarItem | undefined;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
  initLogger();
  log("EsiMCP extension activating...");
  sessionManager = new SessionManager();
  debugManager = new DebugManager();
  statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
  statusBarItem.text = "$(terminal) EsiMCP: 0 sessions";
  statusBarItem.tooltip = "EsiMCP terminal and debug controls";
  statusBarItem.show();
  context.subscriptions.push(statusBarItem);
  sessionManager.onSessionsChanged(() => { if (statusBarItem && sessionManager) statusBarItem.text = `$(terminal) EsiMCP: ${sessionManager.getActiveSessionCount()} sessions`; });

  const config = vscode.workspace.getConfiguration("esimcp");
  const port = normalizePort(config.get("serverPort", 3002));
  mcpHttpServer = await startMcpHttpServer({
    port,
    bindHost: normalizeBindHosts(config.get("bindHost", ["127.0.0.1", "::1"])),
    timeoutInSeconds: config.get<number>("timeoutInSeconds", 30),
    requestHandler: createConfiguredMcpRequestHandler(sessionManager, debugManager),
  });
  context.subscriptions.push({ dispose: () => { void deactivate(); } });
  log(`EsiMCP direct HTTP server listening on port ${port}`);
}

export async function deactivate(): Promise<void> {
  const server = mcpHttpServer;
  mcpHttpServer = undefined;
  await server?.close();
  sessionManager?.dispose();
  sessionManager = undefined;
  debugManager = undefined;
  disposeLogger();
}