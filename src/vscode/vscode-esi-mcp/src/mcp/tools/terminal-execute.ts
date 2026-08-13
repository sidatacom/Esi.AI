import type { SessionManager } from "../../terminal/session-manager.js";
import type { McpToolResponse } from "../../types/index.js";
import { terminalExecuteSchema } from "./schemas.js";

export async function handleTerminalExecute(
  params: unknown,
  sessionManager: SessionManager,
): Promise<McpToolResponse> {
  const input = terminalExecuteSchema.parse(params);

  const session = sessionManager.getSession(input.sessionId);
  if (!session) {
    return {
      content: [
        {
          type: "text",
          text: `Error: Session "${input.sessionId}" not found. Use terminal_list to see active sessions.`,
        },
      ],
      isError: true,
    };
  }

  // Validate command against blocklist
  const validation = sessionManager.validateCommand(input.command);
  if (!validation.valid) {
    return {
      content: [
        {
          type: "text",
          text: `Command blocked: ${validation.reason}`,
        },
      ],
      isError: true,
    };
  }

  const timeoutMs = input.timeoutMs ?? sessionManager.getDefaultTimeout();
  const waitForCompletion = input.waitForCompletion ?? true;

  const result = await session.execute(
    input.command,
    timeoutMs,
    waitForCompletion,
  );

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

  const statusParts = [`exit: ${result.exitCode ?? "n/a"}`, `${result.durationMs}ms`, input.sessionId];
  let text = `$ ${input.command}\n${cleanOutput}\n\n[${statusParts.join(" | ")}]`;

  if (result.timedOut) {
    text += `\n[TIMED OUT after ${timeoutMs}ms - session still active, use read to get more output]`;
  }

  return {
    content: [
      {
        type: "text",
        text,
      },
    ],
    isError: result.exitCode !== null && result.exitCode !== 0,
  };
}
