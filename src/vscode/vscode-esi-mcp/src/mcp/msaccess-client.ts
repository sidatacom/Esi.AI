import { spawn, type ChildProcessWithoutNullStreams } from "node:child_process";
import * as path from "node:path";
import * as readline from "node:readline";
import * as vscode from "vscode";
import { log, logError } from "../utils/logger.js";

type JsonRpcResponse = { id?: number; result?: unknown; error?: { code?: number; message?: string } };
type PendingRequest = { resolve: (result: unknown) => void; reject: (error: Error) => void; timer: NodeJS.Timeout };

export class MsAccessClient {
  private process?: ChildProcessWithoutNullStreams;
  private initialization?: Promise<void>;
  private nextRequestId = 1;
  private readonly pending = new Map<number, PendingRequest>();

  async call(method: string, params: Record<string, unknown> = {}): Promise<unknown> {
    const process = this.ensureProcess();
    if (method !== "initialize") await this.ensureInitialized(process);
    return this.sendRequest(process, method, params);
  }

  private async ensureInitialized(process: ChildProcessWithoutNullStreams): Promise<void> {
    if (this.initialization) return this.initialization;
    this.initialization = this.sendRequest(process, "initialize", {
      protocolVersion: "2024-11-05",
      capabilities: {},
      clientInfo: { name: "EsiMCP", version: "1.0.32" },
    }).then(() => undefined).finally(() => { this.initialization = undefined; });
    return this.initialization;
  }

  private sendRequest(process: ChildProcessWithoutNullStreams, method: string, params: Record<string, unknown>): Promise<unknown> {
    const id = this.nextRequestId++;
    const timeoutMs = vscode.workspace.getConfiguration("esimcp").get<number>("msAccessTimeoutMs", 120000);
    const request = JSON.stringify({ jsonrpc: "2.0", id, method, params }) + "\n";

    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`Microsoft Access MCP request '${method}' timed out after ${timeoutMs} ms`));
      }, timeoutMs);
      this.pending.set(id, { resolve, reject, timer });
      process.stdin.write(request, (error) => {
        if (!error) return;
        clearTimeout(timer);
        this.pending.delete(id);
        reject(error);
      });
    });
  }

  dispose(): void {
    for (const pending of this.pending.values()) {
      clearTimeout(pending.timer);
      pending.reject(new Error("Microsoft Access MCP client was disposed"));
    }
    this.pending.clear();
    this.initialization = undefined;
    this.process?.kill();
    this.process = undefined;
    this.initialization = undefined;
  }

  private ensureProcess(): ChildProcessWithoutNullStreams {
    if (this.process && !this.process.killed) return this.process;
    const configuration = vscode.workspace.getConfiguration("esimcp");
    const command = configuration.get<string>("msAccessServerCommand", "dotnet");
    const configuredArguments = configuration.get<string[]>("msAccessServerArguments", []);
    const projectPath = configuration.get<string>("msAccessServerProject", path.join("origins", "brickly26", "MS-Access-mcp", "MS.Access.MCP.Official", "MS.Access.MCP.Official.csproj"));
    const args = configuredArguments.length > 0 ? configuredArguments : ["run", "--project", projectPath, "--no-launch-profile"];
    const cwd = configuration.get<string>("msAccessServerWorkingDirectory", vscode.workspace.workspaceFolders?.[0]?.uri.fsPath);
    const databasePath = configuration.get<string>("msAccessDatabasePath", "");
    const environment = databasePath ? { ...process.env, ACCESS_DATABASE_PATH: databasePath } : process.env;

    log(`Starting Microsoft Access MCP server: ${command} ${args.join(" ")}`);
    const child = spawn(command, args, { cwd: cwd || undefined, env: environment, stdio: ["pipe", "pipe", "pipe"] });
    this.process = child;
    readline.createInterface({ input: child.stdout }).on("line", (line) => this.handleResponse(line));
    child.stderr.on("data", (chunk) => log(`Microsoft Access MCP stderr: ${String(chunk).trim()}`));
    child.on("error", (error) => this.handleProcessFailure(error));
    child.on("exit", (code, signal) => this.handleProcessFailure(new Error(`Microsoft Access MCP server exited (code=${code}, signal=${signal})`)));
    return child;
  }

  private handleResponse(line: string): void {
    let response: JsonRpcResponse;
    try { response = JSON.parse(line) as JsonRpcResponse; }
    catch { log(`Ignoring non-JSON Microsoft Access MCP output: ${line}`); return; }
    if (typeof response.id !== "number") return;
    const pending = this.pending.get(response.id);
    if (!pending) return;
    clearTimeout(pending.timer);
    this.pending.delete(response.id);
    if (response.error) {
      pending.reject(new Error(response.error.message ?? `Microsoft Access MCP error ${response.error.code ?? "unknown"}`));
      return;
    }
    pending.resolve(response.result);
  }

  private handleProcessFailure(error: Error): void {
    if (!this.process) return;
    logError("Microsoft Access MCP server failed", error);
    this.process = undefined;
    for (const pending of this.pending.values()) {
      clearTimeout(pending.timer);
      pending.reject(error);
    }
    this.pending.clear();
  }
}