import * as vscode from "vscode";
import { initLogger, log, disposeLogger } from "./utils/logger.js";
import { SessionManager } from "./terminal/session-manager.js";
import { DebugManager } from "./debug/manager.js";
import { createConfiguredMcpRequestHandler, startMcpHttpServer, type McpHttpServer } from "./mcp-http-server.js";
import { normalizeBindHosts, normalizePort } from "./config.js";
import { MsAccessClient } from "./mcp/msaccess-client.js";

let mcpHttpServer: McpHttpServer | undefined;
let sessionManager: SessionManager | undefined;
let debugManager: DebugManager | undefined;
let msAccessClient: MsAccessClient | undefined;
let statusBarItem: vscode.StatusBarItem | undefined;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
  initLogger();
  log("EsiMCP extension activating...");
  sessionManager = new SessionManager();
  debugManager = new DebugManager();
  msAccessClient = new MsAccessClient();
  sessionManager.attachDebugManager(debugManager);
  statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
  statusBarItem.text = "$(plug) EsiMCP: Starting";
  statusBarItem.tooltip = "Starting the workspace-local EsiMCP server.";
  statusBarItem.show();
  context.subscriptions.push(statusBarItem);

  const config = vscode.workspace.getConfiguration("esimcp");
  const port = normalizePort(config.get("serverPort", 0));
  mcpHttpServer = await startMcpHttpServer({
    port,
    bindHost: normalizeBindHosts(config.get("bindHost", ["127.0.0.1", "::1"])),
    timeoutInSeconds: config.get<number>("timeoutInSeconds", 30),
    requestHandler: createConfiguredMcpRequestHandler(sessionManager, debugManager, msAccessClient),
  });
  const workspaceName = vscode.workspace.name ?? vscode.workspace.workspaceFolders?.[0]?.name ?? "Workspace";
  statusBarItem.text = `$(plug) EsiMCP: ${workspaceName}`;
  statusBarItem.tooltip = `EsiMCP server for workspace "${workspaceName}" is listening on port ${mcpHttpServer.port}.`;
  context.subscriptions.push(vscode.lm.registerMcpServerDefinitionProvider("vscode-esi-mcp.server", {
    provideMcpServerDefinitions: () => {
      if (!mcpHttpServer) return [];
      return [new vscode.McpHttpServerDefinition(
        "EsiMCP",
        vscode.Uri.parse(`http://127.0.0.1:${mcpHttpServer.port}/mcp`),
        {},
        context.extension.packageJSON.version,
      )];
    },
  }));
  context.subscriptions.push({ dispose: () => { void deactivate(); } });
  log(`EsiMCP direct HTTP server listening on port ${mcpHttpServer.port}`);
}

export async function deactivate(): Promise<void> {
  const server = mcpHttpServer;
  mcpHttpServer = undefined;
  await server?.close();
  sessionManager?.dispose();
  sessionManager = undefined;
  debugManager?.dispose();
  debugManager = undefined;
  msAccessClient?.dispose();
  msAccessClient = undefined;
  disposeLogger();
}