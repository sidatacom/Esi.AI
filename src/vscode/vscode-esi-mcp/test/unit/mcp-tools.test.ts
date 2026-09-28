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
  it("exposes only the supported EsiMCP tool families", async () => {
    const handler = createMcpRequestHandler({} as SessionManager, {} as DebugManager);
    const result = await handler("tools/list") as { tools: Array<{ name: string; description: string }> };
    const names = result.tools.map((tool) => tool.name);

    expect(names).toEqual([
      "vscode_terminal_list_commands",
      "vscode_terminal_execute_command",
      "vscode_debug_list_commands",
      "vscode_debug_execute_command",
      "msaccess_list_commands",
      "msaccess_execute_command",
    ]);
    expect(result.tools.every((tool) => tool.description.startsWith("EsiMCP "))).toBe(true);
  });

  it("runs the popup-free debug restart through the legacy vscode_debug path", async () => {
    const restartDebugging = vi.fn().mockResolvedValue(true);
    const handler = createMcpRequestHandler({} as SessionManager, { restartDebugging } as unknown as DebugManager);

    const result = await handler("tools/call", {
      name: "vscode_debug_execute_command",
      arguments: { commandId: "debug.restart", arguments: { rebuildTaskName: "build" } },
    }) as { content: Array<{ text: string }>; isError?: boolean };

    expect(result.isError).toBeUndefined();
    expect(JSON.parse(result.content[0].text)).toEqual({ restarted: true });
    expect(restartDebugging).toHaveBeenCalledWith("build");
  });

  it("lists project/file launch and Hot Reload debug operations", async () => {
    const handler = createMcpRequestHandler({} as SessionManager, {} as DebugManager);
    const result = await handler("tools/call", {
      name: "vscode_debug_list_commands",
      arguments: {},
    }) as { content: Array<{ text: string }> };
    const payload = JSON.parse(result.content[0].text) as { commands: Array<{ command: string }> };

    expect(payload.commands.map((command) => command.command)).toContain("debug.launchProject");
    expect(payload.commands.map((command) => command.command)).toContain("debug.launchFile");
    expect(payload.commands.map((command) => command.command)).toContain("debug.hotReload");
  });

  it("returns structured build failures from project launch", async () => {
    const failure = { success: false, stage: "build", resultCode: 1, buildLogPath: "/tmp/build.log" };
    const launchProject = vi.fn().mockResolvedValue(failure);
    const cancelPendingDebugHostReadiness = vi.fn();
    const sessionManager = {
      prepareDebugHostReadiness: vi.fn(),
      cancelPendingDebugHostReadiness,
    } as unknown as SessionManager;
    const handler = createMcpRequestHandler(sessionManager, { launchProject } as unknown as DebugManager);

    const result = await handler("tools/call", {
      name: "vscode_debug_execute_command",
      arguments: { commandId: "debug.launchProject", arguments: { projectFile: "App.csproj" } },
    }) as { content: Array<{ text: string }> };

    expect(JSON.parse(result.content[0].text)).toEqual(failure);
    expect(cancelPendingDebugHostReadiness).toHaveBeenCalledOnce();
    expect(launchProject).toHaveBeenCalledWith({ projectFile: "App.csproj", configuration: "Debug" });
  });

  it("starts an explicit VS Code configuration and binds readiness to its session", async () => {
    const session = { id: "session-started", name: "Esi.AI Studio" };
    const startDebugging = vi.fn().mockResolvedValue({ started: true, sessionId: session.id, session });
    const prepareDebugHostReadiness = vi.fn();
    const bindDebugHostReadiness = vi.fn();
    const sessionManager = { prepareDebugHostReadiness, bindDebugHostReadiness } as unknown as SessionManager;
    const handler = createMcpRequestHandler(sessionManager, { startDebugging } as unknown as DebugManager);
    const configuration = { name: "Esi.AI Studio", type: "coreclr", request: "launch", program: "/workspace/bin/Esi.AI.Studio.dll" };

    const result = await handler("tools/call", {
      name: "vscode_debug_execute_command",
      arguments: { commandId: "debug.start", arguments: { workspaceFolder: "/workspace", configuration } },
    }) as { content: Array<{ text: string }>; isError?: boolean };

    expect(result.isError).toBeUndefined();
    expect(JSON.parse(result.content[0].text)).toEqual({ started: true, sessionId: "session-started" });
    expect(prepareDebugHostReadiness).toHaveBeenCalledOnce();
    expect(bindDebugHostReadiness).toHaveBeenCalledWith(session);
    expect(startDebugging).toHaveBeenCalledWith({ workspaceFolder: "/workspace", configuration });
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

  it("returns immediately when direct debug restart has no active session", async () => {
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