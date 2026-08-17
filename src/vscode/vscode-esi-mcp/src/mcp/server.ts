  handler: (params: unknown, sessionManager: SessionManager, debugManager?: DebugManager) => Promise<McpToolResponse>;
import { zodToJsonSchema } from "zod-to-json-schema";
import { log, logError } from "../utils/logger.js";
import type { SessionManager } from "../terminal/session-manager.js";
import type { DebugManager } from "../debug/manager.js";
import type { McpToolResponse } from "../types/index.js";
import {
  terminalCreateSchema,
  terminalExecuteSchema,
  terminalReadOutputSchema,
  terminalListSchema,
  terminalCloseSchema,
  terminalSendInputSchema,
  terminalRunSchema,
} from "./tools/schemas.js";
import { handleTerminalCreate } from "./tools/terminal-create.js";
import { handleTerminalExecute } from "./tools/terminal-execute.js";
import { handleTerminalRun } from "./tools/terminal-run.js";
import { handleTerminalReadOutput } from "./tools/terminal-read-output.js";
import { handleTerminalList } from "./tools/terminal-list.js";
import { handleTerminalClose } from "./tools/terminal-close.js";
import { handleTerminalSendInput } from "./tools/terminal-send-input.js";
import { DEBUG_TOOLS } from "./tools/debug.js";

interface ToolDefinition {
  name: string;
  description: string;
  inputSchema: Record<string, unknown>;
  handler: (
    params: unknown,
    sessionManager: SessionManager,
    debugManager?: DebugManager,
  ) => Promise<McpToolResponse>;
}

function toJsonSchema(schema: import("zod").ZodType): Record<string, unknown> {
  return zodToJsonSchema(schema, { target: "openApi3" }) as Record<string, unknown>;
}

const TOOLS: ToolDefinition[] = [
  {
    name: "terminal_run",
    description:
      "EsiMCP Terminal: create a new terminal and execute a command in one step. Combines create + exec for convenience.",
    inputSchema: toJsonSchema(terminalRunSchema),
    handler: handleTerminalRun,
  },
  {
    name: "terminal_create",
    description:
      "EsiMCP Terminal: create a new visible terminal session in VSCode. Returns a sessionId for subsequent operations.",
    inputSchema: toJsonSchema(terminalCreateSchema),
    handler: handleTerminalCreate,
  },
  {
    name: "terminal_exec",
    description:
      "EsiMCP Terminal: execute a command in an existing terminal session and capture the output. The command runs visibly in the VSCode terminal tab.",
    inputSchema: toJsonSchema(terminalExecuteSchema),
    handler: handleTerminalExecute,
  },
  {
    name: "terminal_read",
    description:
      "EsiMCP Terminal: read output from a terminal session with pagination support. Use offset and lines parameters for incremental reads.",
    inputSchema: toJsonSchema(terminalReadOutputSchema),
    handler: handleTerminalReadOutput,
  },
  {
    name: "terminal_list",
    description:
      "EsiMCP Terminal: list all active terminal sessions. Optionally filter by agentId for subagent isolation.",
    inputSchema: toJsonSchema(terminalListSchema),
    handler: handleTerminalList,
  },
  {
    name: "terminal_close",
    description:
      "EsiMCP Terminal: close a terminal session and its associated VSCode terminal tab.",
    inputSchema: toJsonSchema(terminalCloseSchema),
    handler: handleTerminalClose,
  },
  {
    name: "terminal_input",
    description:
      "EsiMCP Terminal: send text input to an interactive terminal session. Useful for answering prompts or interacting with REPLs.",
    inputSchema: toJsonSchema(terminalSendInputSchema),
    handler: handleTerminalSendInput,
  },
];

TOOLS.push(...DEBUG_TOOLS.map((tool) => ({
  name: tool.name,
  description: tool.description,
  inputSchema: toJsonSchema(tool.schema),
  handler: async (params: unknown, sessionManager: SessionManager, debugManager?: DebugManager) => {
    if (!debugManager) throw new Error("Debug manager is unavailable");
    return tool.handler(params, debugManager, sessionManager);
  },
})));

export const TOOL_DEFINITIONS = TOOLS;

/**
 * Creates a handler function that processes MCP JSON-RPC requests
 * served by the direct local HTTP transport.
 */
export function createMcpRequestHandler(
  sessionManager: SessionManager,
  debugManager?: DebugManager,
): (method: string, params?: unknown) => Promise<unknown> {
  return async (method: string, params?: unknown): Promise<unknown> => {
    log(`MCP request: ${method}`);

    // Handle MCP initialization
    if (method === "initialize") {
      return {
        protocolVersion: "2024-11-05",
        capabilities: {
          tools: {},
        },
        serverInfo: {
          name: "EsiMCP",
          version: "1.0.6",
        },
      };
    }

    // Handle tools/list
    if (method === "tools/list") {
      return {
        tools: TOOLS.map((tool) => ({
          name: tool.name,
          description: tool.description,
          inputSchema: tool.inputSchema,
        })),
      };
    }

    // Handle tools/call
    if (method === "tools/call") {
      const { name, arguments: args } =
        params as { name: string; arguments?: unknown } || {};

      const tool = TOOLS.find((t) => t.name === name);
      if (!tool) {
        return {
          content: [
            { type: "text", text: `Unknown tool: ${name}` },
          ],
          isError: true,
        };
      }

      try {
        const result = await tool.handler(args, sessionManager, debugManager);
        return result;
      } catch (err) {
        const errorMsg = err instanceof Error ? err.message : String(err);
        logError(`Tool ${name} failed`, err);
        return {
          content: [{ type: "text", text: `Error: ${errorMsg}` }],
          isError: true,
        };
      }
    }

    // Handle notifications (no response needed)
    if (method === "notifications/initialized") {
      log("Client initialized");
      return {};
    }

    log(`Unknown method: ${method}`);
    return {
      error: { code: -32601, message: `Method not found: ${method}` },
    };
  };
}
