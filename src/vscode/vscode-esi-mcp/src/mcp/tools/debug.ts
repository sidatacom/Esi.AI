import type { ZodType } from "zod";
import type { McpToolResponse } from "../../types/index.js";
import type { DebugManager } from "../../debug/manager.js";
import type { SessionManager } from "../../terminal/session-manager.js";
import {
  debugBreakpointSchema, debugEmptySchema, debugEvaluateSchema, debugLogpointSchema, debugStartSchema,
  debugSettingsSchema, debugVariableValuesSchema, debugVariablesSchema, debugWaitForEventSchema,
  debugCheckHostReadinessSchema, debugRestartSchema,
} from "./schemas.js";

export interface DebugToolDefinition {
  name: string;
  description: string;
  schema: ZodType;
  handler: (params: unknown, manager: DebugManager, sessionManager: SessionManager) => Promise<McpToolResponse>;
}

const text = (value: unknown): McpToolResponse => ({ content: [{ type: "text", text: JSON.stringify(value) }] });
const empty = debugEmptySchema;
export const handleActiveDebugSession = async (_: unknown, manager: DebugManager): Promise<McpToolResponse> => text(manager.getActiveSessionId());
export const handleDebugHostReadiness = async (params: unknown, manager: DebugManager, sessionManager: SessionManager): Promise<McpToolResponse> => {
  const input = debugCheckHostReadinessSchema.parse(params ?? {});
  const sessionId = input.sessionId ?? manager.getActiveSessionId() ?? undefined;
  return text({ ready: await sessionManager.waitForDebugHostReadiness(manager, sessionId) });
};
export const handleStopDebugSession = async (_: unknown, manager: DebugManager, sessionManager: SessionManager): Promise<McpToolResponse> => {
  try {
    await manager.stopDebugging();
    return text({ stopped: true });
  } finally {
    sessionManager.resetDebugHostReadiness();
  }
};
export const handleRestartDebugSession = async (params: unknown, manager: DebugManager): Promise<McpToolResponse> => {
  const input = debugRestartSchema.parse(params ?? {});
  const restarted = await manager.restartDebugging(input.rebuildTaskName);
  return text(restarted ? { restarted: true } : { restarted: false, message: "No active debug session" });
};

export const DEBUG_TOOLS: DebugToolDefinition[] = [
  { name: "debug.active.session", description: "EsiMCP Debug: return the ID of the active VS Code debug session", schema: empty, handler: handleActiveDebugSession },
  { name: "debug.settings", description: "EsiMCP Debug: read a setting from the active VS Code workspace configuration", schema: debugSettingsSchema, handler: async (params, manager) => { const input = debugSettingsSchema.parse(params); return text(manager.getSetting(input.setting)); } },
  { name: "debug.start", description: "EsiMCP Debug: start a VS Code debug session and wait for the debugger to attach", schema: debugStartSchema, handler: async (params, manager, sessionManager) => {
    const started = await manager.startDebugging(debugStartSchema.parse(params), {
      onAcceptedStart: () => sessionManager.resetDebugHostReadiness(),
      onEnd: () => sessionManager.resetDebugHostReadiness(),
    });
    return text(started);
  } },
  { name: "debug.check.host.readyness", description: "EsiMCP Debug: check the configured readiness string in active dotnet: terminals and abort if the debug session raises an exception", schema: debugCheckHostReadinessSchema, handler: handleDebugHostReadiness },
  { name: "debug.wait.for.event", description: "EsiMCP Debug: wait for a debugger pause, exception, continue, or termination event", schema: debugWaitForEventSchema, handler: async (params, manager) => { const input = debugWaitForEventSchema.parse(params); return text(await manager.waitForDebugEvent(input.timeoutMs, input.type)); } },
  { name: "debug.stop", description: "EsiMCP Debug: stop the active debug session", schema: empty, handler: handleStopDebugSession },
  { name: "debug.step.over", description: "EsiMCP Debug: step over the current statement", schema: empty, handler: async (_, manager) => { await manager.stepOver(); return text({ stepped: true }); } },
  { name: "debug.step.into", description: "EsiMCP Debug: step into the current statement", schema: empty, handler: async (_, manager) => { await manager.stepInto(); return text({ stepped: true }); } },
  { name: "debug.step.out", description: "EsiMCP Debug: step out of the current function", schema: empty, handler: async (_, manager) => { await manager.stepOut(); return text({ stepped: true }); } },
  { name: "debug.continue", description: "EsiMCP Debug: continue the active debug session", schema: empty, handler: async (_, manager) => { await manager.continueExecution(); return text({ continued: true }); } },
  { name: "debug.pause", description: "EsiMCP Debug: pause the active debug session", schema: empty, handler: async (_, manager) => { await manager.pauseExecution(); return text({ paused: true }); } },
  { name: "debug.restart", description: "EsiMCP Debug: stop and restart the active debug session, optionally running a named tasks.json task between them", schema: debugRestartSchema, handler: handleRestartDebugSession },
  { name: "debug.add.breakpoint", description: "EsiMCP Debug: add a source breakpoint and report adapter binding", schema: debugBreakpointSchema, handler: async (params, manager) => { const input = debugBreakpointSchema.parse(params); return text(await manager.addBreakpoint(input.fileFullPath, input.line, input.condition)); } },
  { name: "debug.add.logpoint", description: "EsiMCP Debug: add a source logpoint and report adapter binding", schema: debugLogpointSchema, handler: async (params, manager) => { const input = debugLogpointSchema.parse(params); return text(await manager.addBreakpoint(input.fileFullPath, input.line, input.condition, input.logMessage)); } },
  { name: "debug.remove.breakpoint", description: "EsiMCP Debug: remove a source breakpoint", schema: debugBreakpointSchema.omit({ condition: true }), handler: async (params, manager) => { const input = debugBreakpointSchema.omit({ condition: true }).parse(params); await manager.removeBreakpoint(input.fileFullPath, input.line); return text({ removed: true }); } },
  { name: "debug.clear.all.breakpoints", description: "EsiMCP Debug: remove all breakpoints", schema: empty, handler: async (_, manager) => { manager.clearAllBreakpoints(); return text({ cleared: true }); } },
  { name: "debug.list.breakpoints", description: "EsiMCP Debug: list source breakpoints", schema: empty, handler: async (_, manager) => text(manager.listBreakpoints()) },
  { name: "debug.list.variable.names", description: "EsiMCP Debug: list bounded names from a paused debug scope", schema: debugVariablesSchema, handler: async (params, manager) => text(await manager.listVariableNames(debugVariablesSchema.parse(params).scope)) },
  { name: "debug.get.variables.values", description: "EsiMCP Debug: read explicitly requested variables from a paused debug scope", schema: debugVariableValuesSchema, handler: async (params, manager) => { const input = debugVariableValuesSchema.parse(params); return text(await manager.getVariablesValues(input.variableNames, input.scope)); } },
  { name: "debug.evaluate.expression", description: "EsiMCP Debug: evaluate one bounded expression in the paused frame", schema: debugEvaluateSchema, handler: async (params, manager) => text(await manager.evaluateExpression(debugEvaluateSchema.parse(params).expression)) },
];
