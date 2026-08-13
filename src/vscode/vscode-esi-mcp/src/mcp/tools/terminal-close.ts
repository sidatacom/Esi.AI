import type { SessionManager } from "../../terminal/session-manager.js";
import type { McpToolResponse } from "../../types/index.js";
import { terminalCloseSchema } from "./schemas.js";

export async function handleTerminalClose(
  params: unknown,
  sessionManager: SessionManager,
): Promise<McpToolResponse> {
  const input = terminalCloseSchema.parse(params);

  const closed = sessionManager.closeSession(input.sessionId);

  if (!closed) {
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

  return {
    content: [
      {
        type: "text",
        text: `Session closed: ${input.sessionId}`,
      },
    ],
  };
}
