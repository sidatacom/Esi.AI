import type { SessionManager } from "../../terminal/session-manager.js";
import type { McpToolResponse } from "../../types/index.js";
import { terminalReadOutputSchema } from "./schemas.js";

export async function handleTerminalReadOutput(
  params: unknown,
  sessionManager: SessionManager,
): Promise<McpToolResponse> {
  const input = terminalReadOutputSchema.parse(params);

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

  const result = session.readOutput(input.offset, input.lines);

  const cleanOutput = result.lines
    .join("\n")
    .replace(/\x1b\[[0-9;]*[a-zA-Z]/g, "")
    .replace(/\x1b\][^\x07]*\x07/g, "")
    .replace(/\r\n/g, "\n")
    .replace(/\r/g, "")
    .trim();

  const status = [
    `lines: ${result.readFrom}-${result.readFrom + result.readCount}/${result.totalLines}`,
    `remaining: ${result.remaining}`,
    input.sessionId,
  ];

  const text = `${cleanOutput}\n\n[${status.join(" | ")}]`;

  return {
    content: [
      {
        type: "text",
        text,
      },
    ],
  };
}
