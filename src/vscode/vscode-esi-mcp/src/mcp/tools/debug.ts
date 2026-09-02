import type { ZodType } from "zod";
import type { McpToolResponse } from "../../types/index.js";
import type { DebugManager } from "../../debug/manager.js";
import type { SessionManager } from "../../terminal/session-manager.js";
import {
  debugBreakpointSchema, debugEmptySchema, debugEvaluateSchema, debugLogpointSchema, debugStartSchema,
  debugSettingsSchema, debugVariableValuesSchema, debugVariablesSchema, debugWaitForEventSchema,
  debugCheckHostReadinessSchema,
} from "./schemas.js";

export interface DebugToolDefinition {
  name: string;
  description: string;
  schema: ZodType;
  handler: (params: unknown, manager: DebugManager, sessionManager: SessionManager) => Promise<McpToolResponse>;
}

const text = (value: unknown): McpToolResponse => ({ content: [{ type: "text", text: JSON.stringify(value) }] });
const empty = debugEmptySchema;
const stopDebugSession = async (_: unknown, manager: DebugManager, sessionManager: SessionManager): Promise<McpToolResponse> => {
  try {
    await manager.stopDebugging();
    return text({ stopped: true });
  } finally {
    sessionManager.resetDebugHostReadiness();
  }
};

export const DEBUG_TOOLS: DebugToolDefinition[] = [
  { name: "debug_active_session", description: "EsiMCP Debug: return the ID of the active VS Code debug session", schema: empty, handler: async (_, manager) => text(manager.getActiveSessionId()) },
  { name: "debug_settings", description: "EsiMCP Debug: read a setting from the active VS Code workspace configuration", schema: debugSettingsSchema, handler: async (params, manager) => { const input = debugSettingsSchema.parse(params); return text(manager.getSetting(input.setting)); } },
  { name: "debug_start", description: "EsiMCP Debug: start a VS Code debug session and wait for the debugger to attach", schema: debugStartSchema, handler: async (params, manager, sessionManager) => {
    const started = await manager.startDebugging(debugStartSchema.parse(params), {
      onAcceptedStart: () => sessionManager.resetDebugHostReadiness(),
      onEnd: () => sessionManager.resetDebugHostReadiness(),
    });
    return text(started);
  } },
  { name: "debug_check_host_readyness", description: "EsiMCP Debug: wait for the configured readiness string in any VS Code terminal and abort if the debug session raises an exception", schema: debugCheckHostReadinessSchema, handler: async (_, manager, sessionManager) => text({ ready: await sessionManager.waitForDebugHostReadiness(manager) }) },
  { name: "debug_wait_for_event", description: "EsiMCP Debug: wait for a debugger pause, exception, continue, or termination event", schema: debugWaitForEventSchema, handler: async (params, manager) => { const input = debugWaitForEventSchema.parse(params); return text(await manager.waitForDebugEvent(input.timeoutMs, input.type)); } },
  { name: "debug_stop", description: "EsiMCP Debug: stop the active debug session", schema: empty, handler: stopDebugSession },
  { name: "debug_step_over", description: "EsiMCP Debug: step over the current statement", schema: empty, handler: async (_, manager) => { await manager.stepOver(); return text({ stepped: true }); } },
  { name: "debug_step_into", description: "EsiMCP Debug: step into the current statement", schema: empty, handler: async (_, manager) => { await manager.stepInto(); return text({ stepped: true }); } },
  { name: "debug_step_out", description: "EsiMCP Debug: step out of the current function", schema: empty, handler: async (_, manager) => { await manager.stepOut(); return text({ stepped: true }); } },
  { name: "debug_continue", description: "EsiMCP Debug: continue the active debug session", schema: empty, handler: async (_, manager) => { await manager.continueExecution(); return text({ continued: true }); } },
  { name: "debug_pause", description: "EsiMCP Debug: pause the active debug session", schema: empty, handler: async (_, manager) => { await manager.pauseExecution(); return text({ paused: true }); } },
  { name: "debug_restart", description: "EsiMCP Debug: restart the active debug session", schema: empty, handler: async (_, manager) => { await manager.restartDebugging(); return text({ restarted: true }); } },
  { name: "debug_add_breakpoint", description: "EsiMCP Debug: add a source breakpoint and report adapter binding", schema: debugBreakpointSchema, handler: async (params, manager) => { const input = debugBreakpointSchema.parse(params); return text(await manager.addBreakpoint(input.fileFullPath, input.line, input.condition)); } },
  { name: "debug_add_logpoint", description: "EsiMCP Debug: add a source logpoint and report adapter binding", schema: debugLogpointSchema, handler: async (params, manager) => { const input = debugLogpointSchema.parse(params); return text(await manager.addBreakpoint(input.fileFullPath, input.line, input.condition, input.logMessage)); } },
  { name: "debug_remove_breakpoint", description: "EsiMCP Debug: remove a source breakpoint", schema: debugBreakpointSchema.omit({ condition: true }), handler: async (params, manager) => { const input = debugBreakpointSchema.omit({ condition: true }).parse(params); await manager.removeBreakpoint(input.fileFullPath, input.line); return text({ removed: true }); } },
  { name: "debug_clear_all_breakpoints", description: "EsiMCP Debug: remove all breakpoints", schema: empty, handler: async (_, manager) => { manager.clearAllBreakpoints(); return text({ cleared: true }); } },
  { name: "debug_list_breakpoints", description: "EsiMCP Debug: list source breakpoints", schema: empty, handler: async (_, manager) => text(manager.listBreakpoints()) },
  { name: "debug_list_variable_names", description: "EsiMCP Debug: list bounded names from a paused debug scope", schema: debugVariablesSchema, handler: async (params, manager) => text(await manager.listVariableNames(debugVariablesSchema.parse(params).scope)) },
  { name: "debug_get_variables_values", description: "EsiMCP Debug: read explicitly requested variables from a paused debug scope", schema: debugVariableValuesSchema, handler: async (params, manager) => { const input = debugVariableValuesSchema.parse(params); return text(await manager.getVariablesValues(input.variableNames, input.scope)); } },
  { name: "debug_evaluate_expression", description: "EsiMCP Debug: evaluate one bounded expression in the paused frame", schema: debugEvaluateSchema, handler: async (params, manager) => text(await manager.evaluateExpression(debugEvaluateSchema.parse(params).expression)) },
];
