import { z, type ZodType } from "zod";
import type { McpToolResponse } from "../../types/index.js";
import { debugEmptySchema, toJsonSchema } from "./schemas.js";
import { MsAccessClient } from "../msaccess-client.js";

type MsAccessTool = { name: string; description: string; schema: ZodType; handler: (params: unknown, client: MsAccessClient) => Promise<McpToolResponse> };
const text = (value: unknown): McpToolResponse => ({ content: [{ type: "text", text: JSON.stringify(value ?? null) }] });
const commandSchema = z.object({ commandId: z.string().min(1).max(256), arguments: z.record(z.unknown()).optional() }).strict();

async function listCommands(_params: unknown, client: MsAccessClient): Promise<McpToolResponse> { return text(await client.call("tools/list")); }
async function executeCommand(params: unknown, client: MsAccessClient): Promise<McpToolResponse> {
  const input = commandSchema.parse(params);
  return text(await client.call("tools/call", { name: input.commandId, arguments: input.arguments ?? {} }));
}

export const MSACCESS_TOOLS: MsAccessTool[] = [
  { name: "msaccess_list_commands", description: "EsiMCP Microsoft Access: list tools exposed by the configured MS-Access-mcp server", schema: debugEmptySchema, handler: listCommands },
  { name: "msaccess_execute_command", description: "EsiMCP Microsoft Access: execute one tool exposed by the configured MS-Access-mcp server", schema: commandSchema, handler: executeCommand },
];

export const MSACCESS_COMMAND_SCHEMA = toJsonSchema(commandSchema);