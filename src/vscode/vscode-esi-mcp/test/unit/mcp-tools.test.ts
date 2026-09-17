import { describe, expect, it, vi } from "vitest";

vi.mock("../../src/utils/logger.js", () => ({ log: vi.fn(), logError: vi.fn() }));
vi.mock("vscode", () => ({
  extensions: { getExtension: vi.fn(() => undefined) },
  commands: { getCommands: vi.fn(async () => []) },
}));
import { createMcpRequestHandler } from "../../src/mcp/server.js";
import type { SessionManager } from "../../src/terminal/session-manager.js";
import type { DebugManager } from "../../src/debug/manager.js";

describe("EsiMCP tool catalog", () => {
  it("exposes namespaced Terminal and Debug tools", async () => {
    const handler = createMcpRequestHandler({} as SessionManager, {} as DebugManager);
    const result = await handler("tools/list") as { tools: Array<{ name: string; description: string }> };
    const names = result.tools.map((tool) => tool.name);

    expect(names).toEqual([
      "vscode_terminal_list_commands",
      "vscode_terminal_execute_command",
      "vscode_debug_list_commands",
      "vscode_debug_execute_command",
      "csharp_devkit_list_commands",
      "csharp_devkit_execute_command",
      "msaccess_list_commands",
      "msaccess_execute_command",
    ]);
    expect(result.tools.every((tool) => tool.description.startsWith("EsiMCP "))).toBe(true);
  });

  it("returns the debug exception error code when readiness is aborted", async () => {
    const readinessError = Object.assign(new Error("Debug session exception: startup failed"), { code: "DEBUG_SESSION_EXCEPTION" });
    const sessionManager = { waitForDebugHostReadiness: vi.fn().mockRejectedValue(readinessError) } as unknown as SessionManager;
    const handler = createMcpRequestHandler(sessionManager, { getActiveSessionId: vi.fn(() => "session-123") } as unknown as DebugManager);

    const result = await handler("tools/call", {
      name: "vscode_debug_execute_command",
      arguments: { commandId: "debug.check.host.readyness", arguments: {} },
    }) as { content: Array<{ text: string }>; isError?: boolean; errorCode?: string };

    expect(result.isError).toBe(true);
    expect(result.errorCode).toBe("DEBUG_SESSION_EXCEPTION");
    expect(JSON.parse(result.content[0].text)).toEqual({
      errorCode: "DEBUG_SESSION_EXCEPTION",
      error: "Debug session exception: startup failed",
    });
  });

  it("returns immediately when debug.restart has no active session", async () => {
    const restartDebugging = vi.fn().mockResolvedValue(false);
    const debugManager = {
      restartDebugging,
    } as unknown as DebugManager;
    const handler = createMcpRequestHandler({} as SessionManager, debugManager);

    const result = await handler("tools/call", {
      name: "vscode_debug_execute_command",
      arguments: { commandId: "debug.restart", arguments: { rebuildTaskName: "build" } },
    }) as { content: Array<{ text: string }>; isError?: boolean };

    expect(result.isError).toBeUndefined();
    expect(JSON.parse(result.content[0].text)).toEqual({
      restarted: false,
      message: "No active debug session",
    });
    expect(restartDebugging).toHaveBeenCalledWith("build");
  });

  it("lists the legacy debug commands behind the VS Code debug command tool", async () => {
    const handler = createMcpRequestHandler({} as SessionManager, {} as DebugManager);

    const result = await handler("tools/call", {
      name: "vscode_debug_list_commands",
      arguments: {},
    }) as { content: Array<{ text: string }> };

    const payload = JSON.parse(result.content[0].text) as { commands: Array<{ command: string; argumentsSchema: { properties?: Record<string, unknown>; required?: string[] } }> };
    expect(payload.commands.map((command) => command.command)).toContain("debug.stop");
    expect(payload.commands.map((command) => command.command)).toContain("debug.restart");
    const startCommand = payload.commands.find((command) => command.command === "debug.start");
    expect(startCommand?.argumentsSchema.required).toEqual(["workingDirectory"]);
    expect(startCommand?.argumentsSchema.properties).toHaveProperty("fileFullPath");
  });

  it("lists the legacy terminal commands behind the VS Code terminal command tool", async () => {
    const handler = createMcpRequestHandler({} as SessionManager, {} as DebugManager);

    const result = await handler("tools/call", {
      name: "vscode_terminal_list_commands",
      arguments: {},
    }) as { content: Array<{ text: string }> };

    const payload = JSON.parse(result.content[0].text) as { commands: Array<{ command: string; argumentsSchema: { properties?: Record<string, unknown>; required?: string[] } }> };
    expect(payload.commands.map((command) => command.command)).toEqual([
      "terminal.run",
      "terminal.create",
      "terminal.execute",
      "terminal.read",
      "terminal.list",
      "terminal.close",
      "terminal.input",
    ]);
    const runCommand = payload.commands.find((command) => command.command === "terminal.run");
    expect(runCommand?.argumentsSchema.required).toEqual(["command"]);
    expect(runCommand?.argumentsSchema.properties).toHaveProperty("waitForCompletion");
  });
});