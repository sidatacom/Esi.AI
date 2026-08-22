import * as vscode from "vscode";
import { TerminalSession } from "./session.js";
import type {
  TerminalSessionConfig,
  TerminalSessionInfo,
  SecurityConfig,
} from "../types/index.js";
import { log, logError } from "../utils/logger.js";

export interface TerminalOutputWaiter {
  promise: Promise<void>;
  cancel(): void;
}

interface OutputWaiterState {
  expectedText: string;
  recentOutput: string;
  resolve: () => void;
  reject: (error: Error) => void;
  timer: ReturnType<typeof setTimeout>;
  settled: boolean;
}

export class SessionManager {
  private sessions = new Map<string, TerminalSession>();
  private outputWaiters = new Set<OutputWaiterState>();
  private debugHostReady = false;
  private debugHostReadyRecentOutput = "";
  private terminalOutputDisposable: vscode.Disposable | null = null;
  private onSessionsChangedEmitter = new vscode.EventEmitter<void>();
  readonly onSessionsChanged = this.onSessionsChangedEmitter.event;
  private idleReaperInterval: ReturnType<typeof setInterval> | null = null;

  constructor() {
    // Idle reaper disabled - user closes sessions manually
    this.recoverExistingSessions();
    this.captureTerminalOutput();

    // Listen for terminals being closed externally
    vscode.window.onDidCloseTerminal((terminal) => {
      for (const [id, session] of this.sessions) {
        if (session.getTerminal() === terminal) {
          log(`Terminal closed externally for session ${id}`);
          session.dispose();
          this.sessions.delete(id);
          this.onSessionsChangedEmitter.fire();
          break;
        }
      }
    });
  }

  private captureTerminalOutput(): void {
    if (!vscode.window.onDidStartTerminalShellExecution) return;

    this.terminalOutputDisposable = vscode.window.onDidStartTerminalShellExecution(async (event) => {
      try {
        for await (const chunk of event.execution.read()) {
          this.findByTerminal(event.terminal)?.appendOutput(chunk);
          this.updateDebugHostReadiness(chunk);
          this.notifyOutputWaiters(chunk);
        }
      } catch (error) {
        logError("Error reading terminal output while waiting for the debug ready string", error);
        this.rejectOutputWaiters(error instanceof Error ? error : new Error(String(error)));
      }
    });
  }

  private updateDebugHostReadiness(chunk: string): void {
    const readyString = this.getDebugReadyString();
    if (!readyString) return;

    const output = `${this.debugHostReadyRecentOutput}${chunk}`;
    if (output.includes(readyString)) {
      this.debugHostReady = true;
    }
    this.debugHostReadyRecentOutput = output.slice(-(readyString.length - 1));
  }

  private notifyOutputWaiters(chunk: string): void {
    for (const waiter of this.outputWaiters) {
      const output = `${waiter.recentOutput}${chunk}`;
      if (output.includes(waiter.expectedText)) {
        this.finishOutputWaiter(waiter);
        continue;
      }
      waiter.recentOutput = output.slice(-(waiter.expectedText.length - 1));
    }
  }

  private finishOutputWaiter(waiter: OutputWaiterState, error?: Error): void {
    if (waiter.settled) return;
    waiter.settled = true;
    clearTimeout(waiter.timer);
    this.outputWaiters.delete(waiter);
    error ? waiter.reject(error) : waiter.resolve();
  }

  private rejectOutputWaiters(error: Error): void {
    for (const waiter of [...this.outputWaiters]) this.finishOutputWaiter(waiter, error);
  }

  private recoverExistingSessions(): void {
    const config = this.getConfig();
    const maxOutputLines = config.maxOutputLines;
    const maxSessions = config.maxConcurrentSessions;

    for (const terminal of vscode.window.terminals) {
      if (
        (!config.includeAllTerminals && !terminal.name.startsWith("MCP:")) ||
        this.sessions.size >= maxSessions
      ) {
        continue;
      }

      const name = terminal.name.slice("MCP:".length).trim() || terminal.name;
      const creationOptions = (
        terminal as vscode.Terminal & {
          creationOptions?: vscode.TerminalOptions;
        }
      ).creationOptions;
      const cwd =
        typeof creationOptions?.cwd === "string"
          ? creationOptions.cwd
          : creationOptions?.cwd && "fsPath" in creationOptions.cwd
            ? creationOptions.cwd.fsPath
            : vscode.workspace.workspaceFolders?.[0]?.uri.fsPath || process.cwd();

      const session = new TerminalSession({ name, cwd }, maxOutputLines, {
        completionPollIntervalMs: config.completionPollIntervalMs,
        completionSettleMs: config.completionSettleMs,
      }, terminal);
      this.sessions.set(session.sessionId, session);
      log(`Recovered session ${session.sessionId}: ${name} (cwd: ${cwd})`);
    }

    if (this.sessions.size > 0) {
      this.onSessionsChangedEmitter.fire();
    }
  }

  private getConfig(): SecurityConfig {
    const config = vscode.workspace.getConfiguration("esimcp");
    return {
      blockedCommands: config.get<string[]>("blockedCommands", [
        "rm -rf /",
        "mkfs",
        "dd if=",
        ":(){ :|:& };:",
      ]),
      allowedDirectories: config.get<string[]>("allowedDirectories", []),
      defaultTimeoutMs: config.get<number>("defaultTimeoutMs", 30000),
      maxConcurrentSessions: config.get<number>("maxConcurrentSessions", 10),
      includeAllTerminals: config.get<boolean>("includeAllTerminals", false),
      maxOutputLines: config.get<number>("maxOutputLines", 10000),
      idleTimeoutMs: config.get<number>("idleTimeoutMs", 300000),
      terminalStartupDelayMs: config.get<number>("terminalStartupDelayMs", 500),
      idleReaperIntervalMs: config.get<number>("idleReaperIntervalMs", 60000),
      completionPollIntervalMs: config.get<number>("terminalPollIntervalMs", 1000),
      completionSettleMs: config.get<number>("terminalCompletionSettleMs", 2000),
    };
  }
  
  getDebugReadyString(): string {
    return vscode.workspace.getConfiguration("esimcp").get<string>("debugReadyString", "Now ready on:").trim();
  }

  getDebugReadyTimeoutMs(): number {
    const timeoutMs = vscode.workspace.getConfiguration("esimcp").get<number>("debugReadyTimeoutMs", 120000);
    return typeof timeoutMs === "number" && Number.isFinite(timeoutMs) && timeoutMs > 0 ? timeoutMs : 120000;
  }

  getDebugHostReadinessTimeoutMs(): number {
    const timeoutSeconds = vscode.workspace
      .getConfiguration("esimcp")
      .get<number>("debugHostReadinessTimeoutSeconds", 60);
    return typeof timeoutSeconds === "number" && Number.isFinite(timeoutSeconds) && timeoutSeconds > 0
      ? timeoutSeconds * 1000
      : 60000;
  }

  resetDebugHostReadiness(): void {
    this.debugHostReady = false;
    this.debugHostReadyRecentOutput = "";
  }

  async waitForDebugHostReadiness(): Promise<boolean> {
    const timeoutMs = this.getDebugHostReadinessTimeoutMs();
    const startedAt = Date.now();

    while (Date.now() - startedAt < timeoutMs) {
      if (this.debugHostReady) return true;
      await new Promise((resolve) => setTimeout(resolve, Math.min(1000, timeoutMs - (Date.now() - startedAt))));
    }

    return this.debugHostReady;
  }

  waitForTerminalOutput(expectedText: string, timeoutMs: number): TerminalOutputWaiter {
    const normalizedText = expectedText.trim();
    if (!normalizedText) throw new Error("esimcp.debugReadyString must not be empty");
    if (!vscode.window.onDidStartTerminalShellExecution) {
      throw new Error("VS Code terminal shell integration is unavailable; cannot wait for the debug ready string");
    }

    let waiter: OutputWaiterState;
    const promise = new Promise<void>((resolve, reject) => {
      waiter = {
        expectedText: normalizedText,
        recentOutput: "",
        resolve,
        reject,
        timer: setTimeout(() => this.finishOutputWaiter(waiter, new Error(`Timed out after ${timeoutMs}ms waiting for debug ready string '${normalizedText}'`)), timeoutMs),
        settled: false,
      };
      this.outputWaiters.add(waiter);
    });

    return { promise, cancel: () => this.finishOutputWaiter(waiter!) };
  }

  private startIdleReaper(): void {
    this.idleReaperInterval = setInterval(() => {
      const config = this.getConfig();
      if (config.idleTimeoutMs <= 0) return;

      for (const [id, session] of this.sessions) {
        if (session.isBusy) continue; // Don't reap sessions with running commands
        if (session.isIdle(config.idleTimeoutMs)) {
          log(`Reaping idle session ${id}`);
          session.dispose();
          this.sessions.delete(id);
          this.onSessionsChangedEmitter.fire();
        }
      }
    }, this.getConfig().idleReaperIntervalMs);
  }

  /**
   * Create a new terminal session.
   */
  createSession(config: TerminalSessionConfig): TerminalSessionInfo {
    const secConfig = this.getConfig();

    // Check concurrent session limit
    if (this.sessions.size >= secConfig.maxConcurrentSessions) {
      throw new Error(
        `Maximum concurrent sessions (${secConfig.maxConcurrentSessions}) reached. Close existing sessions first.`,
      );
    }

    // Validate working directory
    if (config.cwd && secConfig.allowedDirectories.length > 0) {
      const path = require("path");
      const resolvedCwd = path.resolve(config.cwd);
      const isAllowed = secConfig.allowedDirectories.some((dir) =>
        resolvedCwd.startsWith(path.resolve(dir)),
      );
      if (!isAllowed) {
        throw new Error(
          `Working directory "${config.cwd}" is not in the allowed directories list.`,
        );
      }
    }

    const session = new TerminalSession(config, secConfig.maxOutputLines, {
      completionPollIntervalMs: secConfig.completionPollIntervalMs,
      completionSettleMs: secConfig.completionSettleMs,
    });
    this.sessions.set(session.sessionId, session);
    this.onSessionsChangedEmitter.fire();

    return session.getInfo();
  }

  /**
   * Get a session by ID.
   */
  getSession(sessionId: string): TerminalSession | undefined {
    return this.sessions.get(sessionId);
  }

  /**
   * List all active sessions, optionally filtered by agentId.
   */
  listSessions(agentId?: string): TerminalSessionInfo[] {
    const sessions: TerminalSessionInfo[] = [];
    for (const session of this.sessions.values()) {
      const info = session.getInfo();
      if (agentId === undefined || info.agentId === agentId) {
        sessions.push(info);
      }
    }
    return sessions;
  }

  /**
   * Close and remove a session.
   */
  closeSession(sessionId: string): boolean {
    const session = this.sessions.get(sessionId);
    if (!session) return false;

    session.dispose();
    this.sessions.delete(sessionId);
    this.onSessionsChangedEmitter.fire();
    return true;
  }

  /**
   * Validate a command against the security blocklist.
   */
  validateCommand(command: string): { valid: boolean; reason?: string } {
    const config = this.getConfig();

    for (const blocked of config.blockedCommands) {
      if (command.includes(blocked)) {
        return {
          valid: false,
          reason: `Command contains blocked pattern: "${blocked}"`,
        };
      }
    }

    return { valid: true };
  }

  /**
   * Get default timeout from config.
   */
  getDefaultTimeout(): number {
    return this.getConfig().defaultTimeoutMs;
  }

  /**
   * Get the delay before sending the first command to a newly created terminal.
   */
  getTerminalStartupDelayMs(): number {
    return this.getConfig().terminalStartupDelayMs;
  }

  /**
   * Get the number of active sessions.
   */
  getActiveSessionCount(): number {
    return this.sessions.size;
  }

  /**
   * Find a session by its VSCode terminal instance.
   */
  findByTerminal(terminal: vscode.Terminal): TerminalSession | undefined {
    for (const session of this.sessions.values()) {
      if (session.getTerminal() === terminal) {
        return session;
      }
    }
    return undefined;
  }

  /**
   * Dispose all sessions and cleanup.
   */
  dispose(): void {
    if (this.idleReaperInterval) {
      clearInterval(this.idleReaperInterval);
      this.idleReaperInterval = null;
    }

    for (const [id, session] of this.sessions) {
      session.dispose();
    }
    this.sessions.clear();
    this.terminalOutputDisposable?.dispose();
    this.terminalOutputDisposable = null;
    this.rejectOutputWaiters(new Error("SessionManager disposed"));
    this.onSessionsChangedEmitter.dispose();

    log("SessionManager disposed");
  }
}
