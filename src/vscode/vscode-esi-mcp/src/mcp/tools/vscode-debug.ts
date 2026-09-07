import type { ZodType } from "zod";
import type { DebugManager } from "../../debug/manager.js";
import type { SessionManager } from "../../terminal/session-manager.js";
import type { McpToolResponse } from "../../types/index.js";
import { debugEmptySchema, toJsonSchema, vscodeCommandSchema } from "./schemas.js";
import { DEBUG_TOOLS, type DebugToolDefinition } from "./debug.js";

export interface VscodeDebugToolDefinition {
  name: string;
  description: string;
  schema: ZodType;
  handler: (params: unknown, manager: DebugManager, sessionManager: SessionManager) => Promise<McpToolResponse>;
}

const text = (value: unknown): McpToolResponse => ({ content: [{ type: "text", text: JSON.stringify(value) }] });

async function listCommands(): Promise<McpToolResponse> {
  return text({
    commands: DEBUG_TOOLS.map(({ name, description, schema }) => ({
      command: name,
      title: name.split(".").slice(1).map((part) => part.charAt(0).toUpperCase() + part.slice(1)).join(" "),
      description,
      argumentsSchema: toJsonSchema(schema),
      registered: true,
    })),
  });
}

async function executeCommand(params: unknown, manager: DebugManager, sessionManager: SessionManager): Promise<McpToolResponse> {
  const input = vscodeCommandSchema.parse(params);
  const command = DEBUG_TOOLS.find((item: DebugToolDefinition) => item.name === input.commandId);
  if (!command) {
    throw new Error(`Command '${input.commandId}' is not allowed by the EsiMCP VS Code debug wrapper`);
  }

  return command.handler(input.arguments ?? {}, manager, sessionManager);
}

export const VSCODE_DEBUG_TOOLS: VscodeDebugToolDefinition[] = [
  {
    name: "vscode_debug_list_commands",
    description: "EsiMCP VS Code Debug: list the available debug commands",
    schema: debugEmptySchema,
    handler: async () => listCommands(),
  },
  {
    name: "vscode_debug_execute_command",
    description: "EsiMCP VS Code Debug: execute one available debug command",
    schema: vscodeCommandSchema,
    handler: executeCommand,
  },
];
