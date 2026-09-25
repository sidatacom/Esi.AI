import { describe, expect, it, vi } from "vitest";

vi.mock("../../src/utils/logger.js", () => ({ log: vi.fn(), logError: vi.fn() }));
vi.mock("vscode", () => ({
  extensions: {
    getExtension: vi.fn(() => ({ isActive: true, packageJSON: { version: "3.20.199", contributes: { commands: [] } } })),
  },
  commands: { getCommands: vi.fn(async () => []) },
}));
import { createMcpRequestHandler } from "../../src/mcp/server.js";
import type { SessionManager } from "../../src/terminal/session-manager.js";
import type { DebugManager } from "../../src/debug/manager.js";

describe("EsiMCP tool catalog", () => {
  it("exposes namespaced Terminal, C# Dev Kit, and Access tools without standalone Debug tools", async () => {
    const handler = createMcpRequestHandler({} as SessionManager, {} as DebugManager);
    const result = await handler("tools/list") as { tools: Array<{ name: string; description: string }> };
    const names = result.tools.map((tool) => tool.name);

    expect(names).toEqual([
      "vscode_terminal_list_commands",
      "vscode_terminal_execute_command",
      "csharp_devkit_list_commands",
      "csharp_devkit_execute_command",
      "csharp_devkit_get_interaction_status",
      "csharp_devkit_respond_to_interaction",
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
      name: "csharp_devkit_execute_command",
      arguments: { commandId: "csdevkit.debug.check.host.readyness", arguments: [] },
    }) as { content: Array<{ text: string }>; isError?: boolean; errorCode?: string };

    expect(result.isError).toBe(true);
    expect(result.errorCode).toBe("DEBUG_SESSION_EXCEPTION");
    expect(JSON.parse(result.content[0].text)).toEqual({
      errorCode: "DEBUG_SESSION_EXCEPTION",
      error: "Debug session exception: startup failed",
    });
  });

  it("preserves the existing C# Dev Kit restart behavior when no session is active", async () => {
    const restartDebugging = vi.fn().mockResolvedValue(false);
    const debugManager = {
      restartDebugging,
    } as unknown as DebugManager;
    const handler = createMcpRequestHandler({} as SessionManager, debugManager);

    const result = await handler("tools/call", {
      name: "csharp_devkit_execute_command",
      arguments: { commandId: "csdevkit.debug.restart", arguments: [{ rebuildTaskName: "build" }] },
    }) as { content: Array<{ text: string }>; isError?: boolean };

    expect(result.isError).toBeUndefined();
    expect(JSON.parse(result.content[0].text)).toEqual({
      restarted: false,
      message: "No active debug session",
    });
    expect(restartDebugging).toHaveBeenCalledWith("build");
  });

  it("lists command syntax for every virtual C# Dev Kit command", async () => {
    const handler = createMcpRequestHandler({} as SessionManager, {} as DebugManager);

    const result = await handler("tools/call", {
      name: "csharp_devkit_list_commands",
      arguments: {},
    }) as { content: Array<{ text: string }> };

    const payload = JSON.parse(result.content[0].text) as { commands: Array<{ command: string; invocationSyntax: string }> };
    expect(payload.commands.map((command) => command.command)).toContain("csdevkit.debug.stop");
    expect(payload.commands.map((command) => command.command)).toContain("csdevkit.debug.restart");
    expect(payload.commands.map((command) => command.command)).not.toContain("csdevkit.debug.start");
    expect(payload.commands.every((command) => command.invocationSyntax.length > 0)).toBe(true);
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