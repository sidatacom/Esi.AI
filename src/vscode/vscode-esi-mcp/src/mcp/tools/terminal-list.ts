import type { SessionManager } from "../../terminal/session-manager.js";
import type { McpToolResponse } from "../../types/index.js";
import { terminalListSchema } from "./schemas.js";

export async function handleTerminalList(
  params: unknown,
  sessionManager: SessionManager,
): Promise<McpToolResponse> {
  const input = terminalListSchema.parse(params ?? {});

  const sessions = sessionManager.listSessions(input.agentId);

  if (sessions.length === 0) {
    return {
      content: [{ type: "text", text: "No active sessions." }],
    };
  }

  const lines = sessions.map((s) => {
    const age = Math.round((Date.now() - s.createdAt) / 1000);
    const parts = [s.sessionId, s.name, `cwd: ${s.cwd}`, `${age}s old`, `${s.outputLineCount} lines`];
    if (s.agentId) parts.push(`agent: ${s.agentId}`);
    return parts.join(" | ");
  });

  return {
    content: [
      {
        type: "text",
        text: `${sessions.length} active session(s):\n${lines.join("\n")}`,
      },
    ],
  };
}
