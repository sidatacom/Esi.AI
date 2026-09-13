  handler: (params: unknown, sessionManager: SessionManager, debugManager?: DebugManager) => Promise<McpToolResponse>;
import { log, logError } from "../utils/logger.js";
import type { SessionManager } from "../terminal/session-manager.js";
import type { DebugManager } from "../debug/manager.js";
import type { McpToolResponse } from "../types/index.js";
import { VSCODE_DEBUG_TOOLS } from "./tools/vscode-debug.js";
import { VSCODE_TERMINAL_TOOLS } from "./tools/vscode-terminal.js";
import { CSHARP_DEVKIT_TOOLS } from "./tools/csharp-devkit.js";
import { toJsonSchema } from "./tools/schemas.js";

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

const TOOLS: ToolDefinition[] = [];

TOOLS.push(...VSCODE_TERMINAL_TOOLS.map((tool) => ({
  name: tool.name,
  description: tool.description,
  inputSchema: toJsonSchema(tool.schema),
  handler: async (params: unknown, sessionManager: SessionManager, _debugManager?: DebugManager) => tool.handler(params, sessionManager),
})));

TOOLS.push(...VSCODE_DEBUG_TOOLS.map((tool) => ({
  name: tool.name,
  description: tool.description,
  inputSchema: toJsonSchema(tool.schema),
  handler: async (params: unknown, sessionManager: SessionManager, debugManager?: DebugManager) => {
    if (!debugManager) throw new Error("Debug manager is unavailable");
    return tool.handler(params, debugManager, sessionManager);
  },
})));

TOOLS.push(...CSHARP_DEVKIT_TOOLS.map((tool) => ({
  name: tool.name,
  description: tool.description,
  inputSchema: toJsonSchema(tool.schema),
  handler: async (params: unknown, sessionManager: SessionManager, debugManager?: DebugManager) => tool.handler(params, debugManager, sessionManager),
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
          version: "1.0.30",
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
        const errorCode = typeof err === "object" && err !== null && "code" in err && typeof err.code === "string" ? err.code : undefined;
        logError(`Tool ${name} failed`, err);
        return {
          content: [{ type: "text", text: errorCode ? JSON.stringify({ errorCode, error: errorMsg }) : `Error: ${errorMsg}` }],
          isError: true,
          ...(errorCode ? { errorCode } : {}),
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
