import type { SessionManager } from "../../terminal/session-manager.js";
import type { McpToolResponse } from "../../types/index.js";
import { terminalRunSchema } from "./schemas.js";

export async function handleTerminalRun(
  params: unknown,
  sessionManager: SessionManager,
): Promise<McpToolResponse> {
  const input = terminalRunSchema.parse(params);

  // Try to reuse an existing session matching cwd and agentId that is not busy
  let sessionId: string | undefined;
  let isNewSession = false;
  const existing = sessionManager.listSessions(input.agentId);
  for (const s of existing) {
    if (!s.isActive || (input.cwd && s.cwd !== input.cwd)) continue;
    const session = sessionManager.getSession(s.sessionId);
    if (session && !session.isBusy) {
      sessionId = s.sessionId;
      break;
    }
  }

  // Create new session only if no compatible one exists
  if (!sessionId) {
    const sessionInfo = sessionManager.createSession({
      name: input.name ?? (() => {
        const d = new Date();
        const pad = (n: number) => String(n).padStart(2, "0");
        return `EsiMCP-${pad(d.getFullYear() % 100)}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}-${pad(d.getHours())}-${pad(d.getMinutes())}`;
      })(),
      cwd: input.cwd,
      env: input.env,
      shell: input.shell,
      agentId: input.agentId,
    });
    sessionId = sessionInfo.sessionId;
    isNewSession = true;
  }

  if (isNewSession) {
    return new Promise<McpToolResponse>((resolve) => {
      setTimeout(async () => {
        const result = await executeCommand(sessionId!, input, sessionManager);
        resolve(result);
      }, sessionManager.getTerminalStartupDelayMs());
    });
  }

  return executeCommand(sessionId, input, sessionManager);
}

async function executeCommand(
  sessionId: string,
  input: { command: string; timeoutMs?: number; waitForCompletion?: boolean },
  sessionManager: SessionManager,
): Promise<McpToolResponse> {
  const session = sessionManager.getSession(sessionId);
  if (!session) {
    return {
      content: [{ type: "text", text: "Error: Failed to get terminal session." }],
      isError: true,
    };
  }

  const validation = sessionManager.validateCommand(input.command);
  if (!validation.valid) {
    return {
      content: [{ type: "text", text: `Command blocked: ${validation.reason}` }],
      isError: true,
    };
  }

  const timeoutMs = input.timeoutMs ?? sessionManager.getDefaultTimeout();
  const waitForCompletion = input.waitForCompletion ?? true;

  const result = await session.execute(input.command, timeoutMs, waitForCompletion);

  let cleanOutput = result.output
    .replace(/\x1b\[[0-9;]*[a-zA-Z]/g, "")
    .replace(/\x1b\][^\x07]*\x07/g, "")
    .replace(/\r\n/g, "\n")
    .replace(/\r/g, "")
    .trim();

  const lines = cleanOutput.split("\n");
  if (lines.length > 0 && lines[0].trim() === input.command.trim()) {
    lines.shift();
    cleanOutput = lines.join("\n").trim();
  }

  const statusParts = [`exit: ${result.exitCode ?? "n/a"}`, `${result.durationMs}ms`, sessionId];
  let text = `$ ${input.command}\n${cleanOutput}\n\n[${statusParts.join(" | ")}]`;

  if (result.timedOut) {
    text += `\n[TIMED OUT after ${timeoutMs}ms - session still active, use read to get more output]`;
  }

  return {
    content: [{ type: "text", text }],
    isError: result.exitCode !== null && result.exitCode !== 0,
  };
}
