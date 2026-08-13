import * as vscode from "vscode";
import type {
  TerminalSessionConfig,
  TerminalSessionInfo,
  OutputBuffer,
  CommandExecution,
} from "../types/index.js";
import {
  createOutputBuffer,
  appendToBuffer,
  readFromBuffer,
  getBufferLineCount,
} from "./output-capture.js";
import { generateSessionId, generateCommandId } from "../utils/id-generator.js";
import { log } from "../utils/logger.js";

export class TerminalSession {
  readonly sessionId: string;
  readonly name: string;
  readonly cwd: string;
  readonly agentId?: string;
  readonly createdAt: number;

  private terminal: vscode.Terminal;
  private outputBuffer: OutputBuffer;
  private commandHistory: CommandExecution[] = [];
  private currentCommand: CommandExecution | null = null;
  private isActive = true;
  private lastCommandAt?: number;
  private shellReady: Promise<void>;
  private readonly timing: { completionPollIntervalMs: number; completionSettleMs: number };

  constructor(
    config: TerminalSessionConfig,
    maxOutputLines: number,
    timing: { completionPollIntervalMs: number; completionSettleMs: number },
    existingTerminal?: vscode.Terminal,
  ) {
    this.sessionId = generateSessionId();
    this.name = config.name;
    this.cwd =
      config.cwd || vscode.workspace.workspaceFolders?.[0]?.uri.fsPath || process.cwd();
    this.agentId = config.agentId;
    this.createdAt = Date.now();
    this.timing = timing;
    this.outputBuffer = createOutputBuffer(maxOutputLines);

    if (existingTerminal) {
      this.terminal = existingTerminal;
    } else {
      const terminalOptions: vscode.TerminalOptions = {
        name: `MCP: ${config.name}`,
        cwd: this.cwd,
        env: config.env,
      };

      if (config.shell) {
        terminalOptions.shellPath = config.shell;
      }

      this.terminal = vscode.window.createTerminal(terminalOptions);
      this.terminal.show(true);
    }

    // Wait 2 seconds for shell to fully initialize
    this.shellReady = new Promise<void>((resolve) => {
      resolve();
    });

    this.setupShellIntegrationCapture();

    log(`Session ${this.sessionId} created: ${config.name} (cwd: ${this.cwd})`);
  }

  private setupShellIntegrationCapture(): void {
    if (vscode.window.onDidEndTerminalShellExecution) {
      vscode.window.onDidEndTerminalShellExecution((event) => {
          if (event.terminal !== this.terminal) return;

          if (this.currentCommand) {
            this.currentCommand.completedAt = Date.now();
            this.currentCommand.exitCode = event.exitCode;
            this.currentCommand.outputEndLine = this.outputBuffer.lines.length;
            this.commandHistory.push(this.currentCommand);
            this.currentCommand = null;
          }
          // Update lastCommandAt so the idle reaper doesn't kill the session immediately
          this.lastCommandAt = Date.now();

          log(
            `Shell execution ended in session ${this.sessionId} with exit code: ${event.exitCode}`,
          );
      });
    }
  }

  appendOutput(chunk: string): void {
    appendToBuffer(this.outputBuffer, chunk);
  }

  async execute(
    command: string,
    timeoutMs: number,
    waitForCompletion: boolean,
  ): Promise<{
    commandId: string;
    output: string;
    exitCode: number | null;
    timedOut: boolean;
    durationMs: number;
  }> {
    log(`Waiting for shell ready in session ${this.sessionId}...`);
    await this.shellReady;
    log(`Shell ready, executing command in session ${this.sessionId}`);

    const commandId = generateCommandId();
    const startedAt = Date.now();
    this.lastCommandAt = startedAt;

    this.currentCommand = {
      commandId,
      command,
      startedAt,
      timedOut: false,
      outputStartLine: this.outputBuffer.lines.length,
    };

    const outputStartIndex = this.outputBuffer.lines.length;

    this.terminal.sendText(command, true);

    if (!waitForCompletion) {
      return {
        commandId,
        output: "(command sent, not waiting for completion)",
        exitCode: null,
        timedOut: false,
        durationMs: Date.now() - startedAt,
      };
    }

    return new Promise((resolve) => {
      let resolved = false;

      const timeoutHandle = setTimeout(() => {
        if (resolved) return;
        resolved = true;

        if (this.currentCommand) {
          this.currentCommand.timedOut = true;
          this.currentCommand.completedAt = Date.now();
          this.currentCommand.outputEndLine = this.outputBuffer.lines.length;
          this.commandHistory.push(this.currentCommand);
          this.currentCommand = null;
        }

        const output = this.outputBuffer.lines
          .slice(outputStartIndex)
          .join("\n");

        resolve({
          commandId,
          output,
          exitCode: null,
          timedOut: true,
          durationMs: Date.now() - startedAt,
        });
      }, timeoutMs);

      if (vscode.window.onDidEndTerminalShellExecution) {
        const disposable = vscode.window.onDidEndTerminalShellExecution(
          (event) => {
            if (event.terminal !== this.terminal || resolved) return;

            resolved = true;
            clearTimeout(timeoutHandle);
            disposable.dispose();

            const output = this.outputBuffer.lines
              .slice(outputStartIndex)
              .join("\n");

            resolve({
              commandId,
              output,
              exitCode: event.exitCode ?? null,
              timedOut: false,
              durationMs: Date.now() - startedAt,
            });
          },
        );
      } else {
        const pollInterval = setInterval(() => {
          if (resolved) {
            clearInterval(pollInterval);
            return;
          }
          const currentLines = this.outputBuffer.lines.length;
          setTimeout(() => {
            if (resolved) return;
            if (this.outputBuffer.lines.length === currentLines) {
              resolved = true;
              clearTimeout(timeoutHandle);
              clearInterval(pollInterval);

              const output = this.outputBuffer.lines
                .slice(outputStartIndex)
                .join("\n");

              resolve({
                commandId,
                output,
                exitCode: null,
                timedOut: false,
                durationMs: Date.now() - startedAt,
              });
            }
          }, this.timing.completionSettleMs);
        }, this.timing.completionPollIntervalMs);
      }
    });
  }

  sendInput(input: string, pressEnter: boolean): void {
    this.terminal.sendText(input, pressEnter);
    this.lastCommandAt = Date.now();
    log(
      `Input sent to session ${this.sessionId}: ${input.slice(0, 50)}${input.length > 50 ? "..." : ""}`,
    );
  }

  readOutput(
    offset: number = 0,
    maxLines: number = 500,
  ): {
    lines: string[];
    readFrom: number;
    readCount: number;
    remaining: number;
    totalLines: number;
    isComplete: boolean;
  } {
    return readFromBuffer(this.outputBuffer, offset, maxLines);
  }

  peekOutput(maxLines: number = 500): string[] {
    return this.outputBuffer.lines.slice(-maxLines);
  }

  get isBusy(): boolean {
    return this.currentCommand !== null;
  }

  getInfo(): TerminalSessionInfo {
    return {
      sessionId: this.sessionId,
      name: this.name,
      cwd: this.cwd,
      agentId: this.agentId,
      isActive: this.isActive,
      createdAt: this.createdAt,
      lastCommandAt: this.lastCommandAt,
      outputLineCount: getBufferLineCount(this.outputBuffer),
    };
  }

  getTerminal(): vscode.Terminal {
    return this.terminal;
  }

  isIdle(idleThresholdMs: number): boolean {
    if (idleThresholdMs <= 0) return false;
    const lastActivity = this.lastCommandAt ?? this.createdAt;
    return Date.now() - lastActivity > idleThresholdMs;
  }

  dispose(): void {
    this.isActive = false;
    this.terminal.dispose();
    log(`Session ${this.sessionId} disposed`);
  }
}
