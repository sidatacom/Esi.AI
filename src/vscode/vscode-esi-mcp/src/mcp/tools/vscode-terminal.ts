import type { ZodType } from "zod";
import type { SessionManager } from "../../terminal/session-manager.js";
import type { McpToolResponse } from "../../types/index.js";
import { terminalCreateSchema, terminalExecuteSchema, terminalListSchema, terminalReadOutputSchema, terminalCloseSchema, terminalRunSchema, terminalSendInputSchema, toJsonSchema, vscodeCommandSchema, debugEmptySchema } from "./schemas.js";
import { handleTerminalCreate } from "./terminal-create.js";
import { handleTerminalExecute } from "./terminal-execute.js";
import { handleTerminalList } from "./terminal-list.js";
import { handleTerminalReadOutput } from "./terminal-read-output.js";
import { handleTerminalClose } from "./terminal-close.js";
import { handleTerminalRun } from "./terminal-run.js";
import { handleTerminalSendInput } from "./terminal-send-input.js";

type TerminalCommandDefinition = {
  command: string;
  title: string;
  description: string;
  schema: ZodType;
  handler: (params: unknown, sessionManager: SessionManager) => Promise<McpToolResponse>;
};

export interface VscodeTerminalToolDefinition {
  name: string;
  description: string;
  schema: ZodType;
  handler: (params: unknown, sessionManager: SessionManager) => Promise<McpToolResponse>;
}

const text = (value: unknown): McpToolResponse => ({ content: [{ type: "text", text: JSON.stringify(value) }] });

const TERMINAL_COMMANDS: TerminalCommandDefinition[] = [
  { command: "terminal.run", title: "Run Terminal Command", description: "Create or reuse a visible terminal session and execute a command", schema: terminalRunSchema, handler: handleTerminalRun },
  { command: "terminal.create", title: "Create Terminal", description: "Create a visible terminal session", schema: terminalCreateSchema, handler: handleTerminalCreate },
  { command: "terminal.execute", title: "Execute Terminal Command", description: "Execute a command in an existing terminal session", schema: terminalExecuteSchema, handler: handleTerminalExecute },
  { command: "terminal.read", title: "Read Terminal Output", description: "Read buffered output from a terminal session", schema: terminalReadOutputSchema, handler: handleTerminalReadOutput },
  { command: "terminal.list", title: "List Terminals", description: "List active terminal sessions", schema: terminalListSchema, handler: handleTerminalList },
  { command: "terminal.close", title: "Close Terminal", description: "Close a terminal session", schema: terminalCloseSchema, handler: handleTerminalClose },
  { command: "terminal.input", title: "Send Terminal Input", description: "Send input to a terminal session", schema: terminalSendInputSchema, handler: handleTerminalSendInput },
];

async function listCommands(): Promise<McpToolResponse> {
  return text({
    commands: TERMINAL_COMMANDS.map(({ command, title, description, schema }) => ({ command, title, description, argumentsSchema: toJsonSchema(schema), registered: true })),
  });
}

async function executeCommand(params: unknown, sessionManager: SessionManager): Promise<McpToolResponse> {
  const input = vscodeCommandSchema.parse(params);
  const command = TERMINAL_COMMANDS.find((item) => item.command === input.commandId);
  if (!command) {
    throw new Error(`Command '${input.commandId}' is not allowed by the EsiMCP VS Code terminal wrapper`);
  }

  return command.handler(input.arguments ?? {}, sessionManager);
}

export const VSCODE_TERMINAL_TOOLS: VscodeTerminalToolDefinition[] = [
  {
    name: "vscode_terminal_list_commands",
    description: "EsiMCP VS Code Terminal: list the available terminal commands",
    schema: debugEmptySchema,
    handler: async () => listCommands(),
  },
  {
    name: "vscode_terminal_execute_command",
    description: "EsiMCP VS Code Terminal: execute one available terminal command",
    schema: vscodeCommandSchema,
    handler: executeCommand,
  },
];
