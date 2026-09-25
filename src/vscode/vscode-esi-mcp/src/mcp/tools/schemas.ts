import { z } from "zod";
import { zodToJsonSchema } from "zod-to-json-schema";

export function toJsonSchema(schema: z.ZodType): Record<string, unknown> {
  return zodToJsonSchema(schema, { target: "openApi3" }) as Record<string, unknown>;
}

// z.coerce.boolean() converts "false" string to true (truthy).
// This preprocessor handles string "false"/"true" correctly.
const coerceBoolean = z.preprocess(
  (val) => {
    if (typeof val === "string") return val.toLowerCase() === "true";
    return val;
  },
  z.boolean(),
);

export const terminalCreateSchema = z.object({
  name: z.string().min(1).describe("Display name for the terminal tab"),
  cwd: z.string().optional().describe("Working directory for the terminal"),
  env: z
    .record(z.string())
    .optional()
    .describe("Additional environment variables"),
  shell: z
    .string()
    .optional()
    .describe("Override shell (e.g., /bin/zsh, /bin/bash)"),
  agentId: z
    .string()
    .optional()
    .describe("Identifier for the owning agent/subagent"),
});

export const terminalExecuteSchema = z.object({
  sessionId: z.string().min(1).describe("Session ID of the target terminal"),
  command: z.string().min(1).describe("Command to execute"),
  timeoutMs: z.coerce
    .number()
    .min(1000)
    .max(300000)
    .optional()
    .describe("Timeout in milliseconds (workspace setting is used when omitted, max: 300000)"),
  waitForCompletion: coerceBoolean
    .optional()
    .default(true)
    .describe("Wait for command to complete before returning (default: true)"),
});

export const terminalReadOutputSchema = z.object({
  sessionId: z.string().min(1).describe("Session ID of the target terminal"),
  offset: z.coerce
    .number()
    .optional()
    .default(0)
    .describe("Line offset (0 = from last read cursor, negative = tail)"),
  lines: z.coerce
    .number()
    .min(1)
    .max(5000)
    .optional()
    .default(500)
    .describe("Maximum lines to return (default: 500, max: 5000)"),
});

export const terminalListSchema = z.object({
  agentId: z
    .string()
    .optional()
    .describe("Filter sessions by agent ID (omit for all sessions)"),
});

export const terminalCloseSchema = z.object({
  sessionId: z.string().min(1).describe("Session ID of the terminal to close"),
});

export const terminalRunSchema = z.object({
  command: z.string().min(1).describe("Command to execute"),
  name: z
    .string()
    .optional()
    .describe("Display name for the terminal tab (auto-generated if omitted)"),
  cwd: z.string().optional().describe("Working directory for the terminal"),
  env: z
    .record(z.string())
    .optional()
    .describe("Additional environment variables"),
  shell: z
    .string()
    .optional()
    .describe("Override shell (e.g., /bin/zsh, /bin/bash)"),
  agentId: z
    .string()
    .optional()
    .describe("Identifier for the owning agent/subagent"),
  timeoutMs: z.coerce
    .number()
    .min(1000)
    .max(300000)
    .optional()
    .describe("Timeout in milliseconds (workspace setting is used when omitted, max: 300000)"),
  waitForCompletion: coerceBoolean
    .optional()
    .default(true)
    .describe("Wait for command to complete before returning (default: true)"),
});

export const terminalSendInputSchema = z.object({
  sessionId: z.string().min(1).describe("Session ID of the target terminal"),
  input: z.string().describe("Text input to send to the terminal"),
  pressEnter: z.coerce
    .boolean()
    .optional()
    .default(true)
    .describe("Whether to press Enter after the input (default: true)"),
});

const debugScopeSchema = z.enum(["local", "global", "all"]).default("local");
const variableNameSchema = z.string().min(1).max(128).refine((name) => !name.includes("*") && name.toLowerCase() !== "all", { message: "Wildcard and all-variable requests are not allowed" });
export const debugEmptySchema = z.object({}).strict();
export const debugWaitForEventSchema = z.object({ timeoutMs: z.coerce.number().int().min(100).max(120000).optional().default(30000), type: z.enum(["paused", "continued", "terminated"]).optional() }).strict();
export const debugBreakpointSchema = z.object({ fileFullPath: z.string().min(1), line: z.number().int().min(1), condition: z.string().max(1000).optional() }).strict();
export const debugLogpointSchema = debugBreakpointSchema.extend({ logMessage: z.string().min(1).max(2000) }).strict();
export const debugVariablesSchema = z.object({ scope: debugScopeSchema }).strict();
export const debugVariableValuesSchema = z.object({ variableNames: z.array(variableNameSchema).min(1).max(20), scope: debugScopeSchema }).strict();
export const debugEvaluateSchema = z.object({ expression: z.string().min(1).max(500).refine((expression) => !expression.includes("*"), { message: "Wildcard expressions are not allowed" }) }).strict();
export const debugSettingsSchema = z.object({ setting: z.string().min(3).max(128).refine((setting) => setting.includes(".") && !/[\\*]/.test(setting), { message: "A fully qualified setting name without wildcards is required" }) }).strict();
export const debugRestartSchema = z.object({ rebuildTaskName: z.string().min(1).optional().describe("Optional exact task name from tasks.json to run after stopping and before restarting") }).strict();
export const debugCheckHostReadinessSchema = z.object({ sessionId: z.string().min(1).optional() }).strict();
export const csharpDevKitEmptySchema = debugEmptySchema;
export const csharpDevKitListCommandsSchema = z.object({
  commandId: z.string().min(1).max(256).optional().describe("Optional command ID to return only that command and its invocation syntax"),
}).strict();
export const csharpDevKitArgumentsSchema = z.array(z.unknown()).max(20).describe("Optional positional arguments forwarded to the C# Dev Kit command; the extension does not publish command-specific argument metadata");
export const csharpDevKitProjectLaunchArgumentsSchema = z.array(z.object({
  path: z.string().min(1).describe("Absolute path to the project .csproj file"),
}).strict()).length(1).describe("Pass one C# Dev Kit command-context object with a path property; C# Dev Kit converts it to a VS Code file URI");
export const csharpDevKitReadinessArgumentsSchema = z.array(z.object({ sessionId: z.string().min(1).optional() }).strict()).max(1).describe("Optional active debug session ID; when omitted, EsiMCP reads vscode.debug.activeDebugSession at call time");
export const csharpDevKitRestartArgumentsSchema = z.array(debugRestartSchema).max(1).describe("Optional restart settings; rebuildTaskName must match a task name from tasks.json");
export const csharpDevKitNoArgumentsSchema = z.array(z.unknown()).max(0).describe("This virtual command does not accept arguments");
export const csharpDevKitInteractionStatusSchema = z.object({
  executionId: z.string().min(1).optional(),
  waitMs: z.number().int().min(0).max(30000).optional().default(1000),
}).strict();
export const csharpDevKitInteractionResponseSchema = z.object({
  executionId: z.string().min(1),
  interactionId: z.string().min(1),
  response: z.unknown().optional(),
  waitMs: z.number().int().min(0).max(30000).optional().default(1000),
}).strict();
export const csharpDevKitCommandSchema = z.object({
  commandId: z.string().min(1).max(256),
  arguments: z.array(z.unknown()).max(20).optional(),
}).strict();
export const vscodeCommandSchema = z.object({
  commandId: z.string().min(1).max(256),
  arguments: z.record(z.unknown()).optional(),
}).strict();
