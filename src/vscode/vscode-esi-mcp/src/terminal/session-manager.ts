import * as vscode from "vscode";
import stripAnsi from "strip-ansi";
import { TerminalSession } from "./session.js";
import type { DebugEvent, DebugManager } from "../debug/manager.js";
import type {
  TerminalSessionConfig,
  TerminalSessionInfo,
  SecurityConfig,
} from "../types/index.js";
import { log, logError } from "../utils/logger.js";

export const DEBUG_SESSION_EXCEPTION_ERROR_CODE = "DEBUG_SESSION_EXCEPTION";
const MAX_DEBUG_CONSOLE_BUFFER_CHARACTERS = 64 * 1024;
const ESI_WEB_DEBUG_TERMINAL_NAME = "Esi.Web.dll";

export class DebugSessionExceptionError extends Error {
  readonly code = DEBUG_SESSION_EXCEPTION_ERROR_CODE;
  readonly event: DebugEvent;

  constructor(event: DebugEvent) {
    super(event.exceptionMessage ? `Debug session exception: ${event.exceptionMessage}` : "Debug session exception occurred");
    this.name = "DebugSessionExceptionError";
    this.event = event;
  }
}

interface DebugReadinessState {
  sessionId: string;
  terminal: vscode.Terminal | null;
  ready: boolean;
  failed: boolean;
  buffer: string;
}

type DebugReadinessSource = Pick<DebugManager, "getActiveSessionId" | "onDebugEvent"> & Partial<Pick<DebugManager, "onDebugOutput">>;

export class SessionManager {
  private sessions = new Map<string, TerminalSession>();
  private debugReadinessBySession = new Map<string, DebugReadinessState>();
  private debugConsoleOutputBySession = new Map<string, string>();
  private debugEventDisposable: vscode.Disposable | null = null;
  private debugOutputDisposable: vscode.Disposable | null = null;
  private terminalOutputDisposable: vscode.Disposable | null = null;
  private debugLaunchInProgress = false;
  private debugLaunchTerminals = new Set<vscode.Terminal>();
  private debugReadinessByTerminal = new Map<vscode.Terminal, { ready: boolean; failed: boolean; buffer: string }>();
  private onSessionsChangedEmitter = new vscode.EventEmitter<void>();
  readonly onSessionsChanged = this.onSessionsChangedEmitter.event;
  private idleReaperInterval: ReturnType<typeof setInterval> | null = null;

  constructor() {
    // Idle reaper disabled - user closes sessions manually
    this.recoverExistingSessions();
    this.captureTerminalOutput();

    // Listen for terminals being closed externally
    vscode.window.onDidCloseTerminal((terminal) => {
      this.debugLaunchTerminals.delete(terminal);
      for (const state of this.debugReadinessBySession.values()) {
        if (state.terminal === terminal) {
          state.terminal = null;
          state.ready = false;
          state.buffer = "";
        }
      }
      this.debugReadinessByTerminal.delete(terminal);
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
    vscode.window.onDidOpenTerminal((terminal) => {
      if (this.debugLaunchInProgress) {
        log(`Debug launch terminal opened: name=${terminal.name}`);
        this.debugLaunchTerminals.add(terminal);
      }
    });
  }

  prepareDebugHostReadiness(): void {
    this.debugLaunchInProgress = true;
    this.debugLaunchTerminals.clear();
    this.debugReadinessByTerminal.clear();
    this.resetDebugHostReadiness();
  }

  bindDebugHostReadiness(session: vscode.DebugSession): void {
    const terminal = [...this.debugLaunchTerminals].find((candidate) => this.isEsiWebDebugTerminal(candidate)) ?? null;
    const existing = this.debugReadinessBySession.get(session.id);
    const terminalState = terminal ? this.debugReadinessByTerminal.get(terminal) : undefined;
    this.debugReadinessBySession.set(session.id, {
      sessionId: session.id,
      terminal,
      ready: existing?.ready === true || terminalState?.ready === true,
      failed: existing?.failed === true || terminalState?.failed === true,
      buffer: `${existing?.buffer ?? ""}${terminalState?.buffer ?? ""}`.slice(-MAX_DEBUG_CONSOLE_BUFFER_CHARACTERS),
    });
    if (terminal) this.debugReadinessByTerminal.delete(terminal);
    this.debugLaunchInProgress = false;
    this.debugLaunchTerminals.clear();
    log(`Bound debug readiness: session=${session.id}, terminal=${terminal?.name ?? "none"}`);
  }

  private isEsiWebDebugTerminal(terminal: vscode.Terminal | null): terminal is vscode.Terminal {
    return terminal?.name === ESI_WEB_DEBUG_TERMINAL_NAME && terminal.exitStatus === undefined;
  }

  cancelPendingDebugHostReadiness(): void {
    this.debugLaunchInProgress = false;
    this.debugLaunchTerminals.clear();
  }

  private captureTerminalOutput(): void {
    if (!vscode.window.onDidStartTerminalShellExecution) return;

    this.terminalOutputDisposable = vscode.window.onDidStartTerminalShellExecution(async (event) => {
      try {
        for await (const chunk of event.execution.read()) {
          this.findByTerminal(event.terminal)?.appendOutput(chunk);
          this.updateDebugHostReadiness(event.terminal, chunk);
        }
      } catch (error) {
        logError("Error reading terminal output", error);
      }
    });
  }

  private updateDebugHostReadiness(terminal: vscode.Terminal | undefined, chunk: string): void {
    const readyString = this.getDebugReadyString();
    if (!readyString || !terminal || !this.isEsiWebDebugTerminal(terminal)) return;
    const normalizedChunk = stripAnsi(chunk);
    log(`Terminal output: terminal=${terminal.name}, length=${chunk.length}, normalizedLength=${normalizedChunk.length}, sessions=${[...this.debugReadinessBySession.keys()].join(",") || "none"}`);
    const pendingState = this.debugReadinessByTerminal.get(terminal) ?? { ready: false, failed: false, buffer: "" };
    pendingState.buffer = `${pendingState.buffer}${normalizedChunk}`.slice(-MAX_DEBUG_CONSOLE_BUFFER_CHARACTERS);
    pendingState.ready = pendingState.buffer.includes(readyString);
    pendingState.failed = pendingState.failed || this.containsStartupFailure(pendingState.buffer);
    this.debugReadinessByTerminal.set(terminal, pendingState);
    log(`Terminal readiness candidate: terminal=${terminal.name}, pendingBufferLength=${pendingState.buffer.length}, matched=${pendingState.ready}, tail=${JSON.stringify(pendingState.buffer.slice(-160))}`);
    for (const state of this.debugReadinessBySession.values()) {
      if (!state.terminal) {
        state.terminal = terminal;
        log(`Late-bound debug readiness terminal: session=${state.sessionId}, terminal=${terminal.name}`);
      }
      if (state.terminal !== terminal) {
        log(`Terminal readiness skipped session=${state.sessionId}: sameTerminal=${state.terminal === terminal}, boundTerminal=${state.terminal?.name ?? "none"}`);
        continue;
      }
      state.buffer = pendingState.buffer;
      state.failed = pendingState.failed;
      if (state.buffer.includes(readyString)) {
        state.ready = true;
        log(`Debug host readiness latched from terminal: session=${state.sessionId}, terminal=${terminal.name}`);
      }
    }
  }

  private updateDebugHostReadinessFromDebugOutput(session: vscode.DebugSession, chunk: string): void {
    const readyString = this.getDebugReadyString();
    if (!readyString) return;

    const normalizedChunk = stripAnsi(chunk);
    const existingState = this.debugReadinessBySession.get(session.id) ?? {
      sessionId: session.id,
      terminal: null,
      ready: false,
      failed: false,
      buffer: "",
    };
    existingState.buffer = `${existingState.buffer}${normalizedChunk}`.slice(-MAX_DEBUG_CONSOLE_BUFFER_CHARACTERS);
    existingState.ready = existingState.buffer.includes(readyString);
    existingState.failed = existingState.failed || this.containsStartupFailure(existingState.buffer);
    this.debugReadinessBySession.set(session.id, existingState);

    const output = `${this.debugConsoleOutputBySession.get(session.id) ?? ""}${normalizedChunk}`;
    this.debugConsoleOutputBySession.set(session.id, output.slice(-MAX_DEBUG_CONSOLE_BUFFER_CHARACTERS));
    if (existingState.ready) {
      for (let currentSession: vscode.DebugSession | undefined = session; currentSession; currentSession = currentSession.parentSession) {
        const state = this.debugReadinessBySession.get(currentSession.id) ?? {
          sessionId: currentSession.id,
          terminal: null,
          ready: false,
          failed: false,
          buffer: "",
        };
        state.buffer = `${state.buffer}${normalizedChunk}`.slice(-MAX_DEBUG_CONSOLE_BUFFER_CHARACTERS);
        state.ready = true;
        state.failed = existingState.failed;
        this.debugReadinessBySession.set(currentSession.id, state);
      }
      log(`Debug host readiness latched from debug output: session=${session.id}`);
    } else if (normalizedChunk.toLowerCase().includes("ready")) {
      log(`Debug readiness candidate did not match configured string: session=${session.id}, readyString=${JSON.stringify(readyString)}`);
    }
  }

  attachDebugManager(debugManager: Pick<DebugManager, "onDebugEvent" | "onDebugOutput">): void {
    log("Attaching debug manager to readiness tracking");
    this.debugEventDisposable?.dispose();
    this.debugOutputDisposable?.dispose();
    this.debugEventDisposable = debugManager.onDebugEvent((event) => {
      if (event.type === "terminated") {
        this.debugReadinessBySession.delete(event.sessionId);
        this.debugConsoleOutputBySession.delete(event.sessionId);
      }
    });
    this.debugOutputDisposable = debugManager.onDebugOutput((session, output) => {
      this.updateDebugHostReadinessFromDebugOutput(session, output);
    });
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

  getDebugHostReadinessTimeoutMs(): number {
    const timeoutSeconds = vscode.workspace
      .getConfiguration("esimcp")
      .get<number>("debugHostReadinessTimeoutSeconds", 60);
    return typeof timeoutSeconds === "number" && Number.isFinite(timeoutSeconds) && timeoutSeconds > 0
      ? timeoutSeconds * 1000
      : 60000;
  }

  resetDebugHostReadiness(sessionId?: string): void {
    log(`Resetting debug host readiness: session=${sessionId ?? "all"}`);
    if (sessionId) {
      this.debugReadinessBySession.delete(sessionId);
      this.debugConsoleOutputBySession.delete(sessionId);
    } else {
      this.debugReadinessBySession.clear();
      this.debugConsoleOutputBySession.clear();
      this.debugReadinessByTerminal.clear();
    }
  }

  async waitForDebugHostReadiness(debugManager?: DebugReadinessSource, requestedSessionId?: string): Promise<boolean> {
    const timeoutMs = this.getDebugHostReadinessTimeoutMs();
    const startedAt = Date.now();
    let sessionId = requestedSessionId;
    log(`Waiting for debug host readiness: requestedSession=${requestedSessionId ?? "none"}, activeSession=${debugManager?.getActiveSessionId() ?? "none"}`);

    return new Promise<boolean>((resolve, reject) => {
      let settled = false;
      let pollTimer: ReturnType<typeof setTimeout> | undefined;
      let timeoutTimer: ReturnType<typeof setTimeout> | undefined;
      let debugEventDisposable: { dispose(): unknown } | undefined;
      const finish = (error?: Error) => {
        if (settled) return;
        settled = true;
        if (pollTimer) clearTimeout(pollTimer);
        if (timeoutTimer) clearTimeout(timeoutTimer);
        debugEventDisposable?.dispose();
        if (error) {
          log(`Readiness check failed: session=${sessionId ?? "none"}, error=${error.message}`);
          reject(error);
          return;
        }
        const ready = this.isDebugHostReady(sessionId);
        log(`Readiness check finished: session=${sessionId ?? "terminal"}, ready=${ready}, elapsedMs=${Date.now() - startedAt}`);
        resolve(ready);
      };
      const checkReadiness = async () => {
        const activeSessionId = debugManager?.getActiveSessionId() ?? undefined;
        if (requestedSessionId) {
          if (debugManager && activeSessionId !== requestedSessionId) {
            log(`Readiness check observed requested session change: session=${requestedSessionId}`);
            finish();
            return;
          }
          sessionId = requestedSessionId;
        } else if (activeSessionId) {
          sessionId = activeSessionId;
        }

        if (sessionId && debugManager && activeSessionId !== sessionId) {
          log(`Readiness check observed session change: session=${sessionId}`);
          finish();
          return;
        }
        if (this.isDebugHostReady(sessionId)) {
          log(`Readiness check observed latch: session=${sessionId ?? "terminal"}`);
          finish();
          return;
        }

        if (this.hasDebugHostStartupFailed(sessionId)) {
          log(`Readiness check observed startup failure: session=${sessionId ?? "none"}`);
          finish();
          return;
        }

        const remainingMs = timeoutMs - (Date.now() - startedAt);
        if (remainingMs <= 0) {
          log(`Readiness check timed out: session=${sessionId ?? "terminal"}, timeoutMs=${timeoutMs}`);
          finish();
          return;
        }

        pollTimer = setTimeout(() => { void checkReadiness(); }, Math.min(1000, remainingMs));
      };

      debugEventDisposable = debugManager?.onDebugEvent((event) => {
        if (event.type === "paused" && event.reason === "exception") finish(new DebugSessionExceptionError(event));
        if (event.type === "terminated" && event.sessionId === sessionId) {
          log(`Readiness check observed session termination: session=${sessionId}`);
          this.debugReadinessBySession.delete(event.sessionId);
          this.debugConsoleOutputBySession.delete(event.sessionId);
          finish();
        }
      });
      timeoutTimer = setTimeout(() => finish(), timeoutMs);
      void checkReadiness();
    });
  }

  private isDebugHostReady(sessionId: string | undefined): boolean {
    return sessionId !== undefined && this.debugReadinessBySession.get(sessionId)?.ready === true;
  }

  private hasDebugHostStartupFailed(sessionId: string | undefined): boolean {
    return sessionId !== undefined && this.debugReadinessBySession.get(sessionId)?.failed === true;
  }

  private containsStartupFailure(output: string): boolean {
    return /(?:LaunchException thrown:|Unhandled exception\s*[:.]|Host terminated unexpectedly|Application startup exception)/i.test(output);
  }

  getDebugConsoleDiagnostics(sessionId: string): { sessionId: string; bufferedCharacters: number; readinessStringSeen: boolean; output: string; lastLine: string } {
    const output = this.debugConsoleOutputBySession.get(sessionId) ?? "";
    const lastLine = output.split(/\r\n|\n|\r/).filter((line) => line.length > 0).at(-1) ?? "";
    return {
      sessionId,
      bufferedCharacters: output.length,
      readinessStringSeen: output.includes(this.getDebugReadyString()),
      output,
      lastLine,
    };
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
    this.debugEventDisposable?.dispose();
    this.debugEventDisposable = null;
    this.debugOutputDisposable?.dispose();
    this.debugOutputDisposable = null;
    this.onSessionsChangedEmitter.dispose();

    log("SessionManager disposed");
  }
}
