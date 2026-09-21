import { describe, expect, it, vi } from "vitest";

const { readdir, showErrorMessage, windowApi } = vi.hoisted(() => {
  const showErrorMessage = vi.fn();
  return { readdir: vi.fn(), showErrorMessage, windowApi: { activeTextEditor: undefined, showErrorMessage } };
});

vi.mock("node:fs/promises", () => ({ readdir }));

const extension = {
  isActive: true,
  packageJSON: {
    version: "3.20.199",
    contributes: {
      commands: [
        { command: "csdevkit.buildSolution", title: "Build Solution" },
        { command: "csdevkit.debug.projectDebugLaunch", title: "Launch Project" },
        { command: "csdevkit.debug.hotReload", title: "Hot Reload" },
        { command: "csdevkit.debug.showHotReloadPanel", title: "Show Hot Reload output" },
        { command: "csdevkit.debug.selectStartupProject", title: "Select C# Startup Project" },
      ],
      keybindings: [{ command: "csdevkit.debug.hotReload", key: "Ctrl+Shift+Enter" }],
      menus: { commandPalette: [{ command: "csdevkit.buildSolution" }], "debug/toolBar": [{ command: "csdevkit.debug.projectDebugLaunch" }] },
    },
  },
  activate: vi.fn(async () => undefined),
};

vi.mock("vscode", () => ({
  extensions: { getExtension: vi.fn(() => extension) },
  commands: {
    getCommands: vi.fn(async () => [
      "csdevkit.buildSolution",
      "csdevkit.debug.projectDebugLaunch",
      "csdevkit.debug.hotReload",
      "csdevkit.debug.showHotReloadPanel",
      "csdevkit.debug.selectStartupProject",
    ]),
    executeCommand: vi.fn(async (command: string, ...argumentsValue: unknown[]) => {
      if (command === "csdevkit.debug.hotReload") await windowApi.showErrorMessage("Hot Reload build failed");
      return { command, argumentsValue };
    }),
  },
  Uri: { file: (filePath: string) => ({ scheme: "file", fsPath: filePath }) },
  workspace: {
    workspaceFolders: [{ uri: { fsPath: "/workspace" } }],
    findFiles: vi.fn(async () => []),
  },
  window: windowApi,
}));

import { CSHARP_DEVKIT_TOOLS } from "../../src/mcp/tools/csharp-devkit.js";

describe("EsiMCP C# Dev Kit tools", () => {
  it("lists commands from the installed C# Dev Kit manifest", async () => {
    const result = await CSHARP_DEVKIT_TOOLS[0].handler({});
    const payload = JSON.parse(result.content[0].text);

    expect(payload.extensionId).toBe("ms-dotnettools.csdevkit");
    expect(payload.commands.map(({ argumentsSchema: _argumentsSchema, ...command }) => command)).toEqual([
      { command: "csdevkit.debug.projectDebugLaunch", title: "Launch Project", keyboardShortcuts: [], menuContexts: ["debug/toolBar"], registered: true },
      { command: "csdevkit.debug.hotReload", title: "Hot Reload", keyboardShortcuts: ["Ctrl+Shift+Enter"], menuContexts: [], registered: true },
      { command: "csdevkit.debug.showHotReloadPanel", title: "Show Hot Reload output", keyboardShortcuts: [], menuContexts: [], registered: true },
      { command: "csdevkit.debug.selectStartupProject", title: "Select C# Startup Project", keyboardShortcuts: [], menuContexts: [], registered: true },
      { command: "csdevkit.debug.active.session", title: "Active Debug Session", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.check.host.readyness", title: "Check Debug Host Readiness", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.output.diagnostics", title: "Debug Console Diagnostics", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.stop", title: "Stop Debugging", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.restart", title: "Restart Debugging", keyboardShortcuts: [], menuContexts: [], registered: false },
    ]);
    expect(payload.commands.filter((command: { registered: boolean }) => command.registered).every((command: { argumentsSchema: { type: string; maxItems: number } }) => command.argumentsSchema.type === "array" && command.argumentsSchema.maxItems === 20)).toBe(true);
    expect(payload.commands.filter((command: { registered: boolean; command: string }) => !command.registered && !["csdevkit.debug.check.host.readyness", "csdevkit.debug.output.diagnostics", "csdevkit.debug.restart"].includes(command.command)).every((command: { argumentsSchema: { type: string; maxItems: number } }) => command.argumentsSchema.type === "array" && command.argumentsSchema.maxItems === 0)).toBe(true);
    const readinessCommand = payload.commands.find((command: { command: string }) => command.command === "csdevkit.debug.check.host.readyness") as { argumentsSchema: { type: string; maxItems: number } };
    expect(readinessCommand.argumentsSchema).toMatchObject({ type: "array", maxItems: 1 });
  });

  it("executes an allowlisted command declared by the C# Dev Kit manifest", async () => {
    const projectUri = { scheme: "file", fsPath: "/workspace/Esi.AI.Studio.csproj" };
    const result = await CSHARP_DEVKIT_TOOLS[1].handler({ commandId: "csdevkit.debug.projectDebugLaunch", arguments: [projectUri] });
    const payload = JSON.parse(result.content[0].text);

    expect(payload.commandId).toBe("csdevkit.debug.projectDebugLaunch");
    expect(payload.result.argumentsValue).toEqual([projectUri]);
  });

  it("suppresses C# Dev Kit notifications during EsiMCP hot reload", async () => {
    const result = await CSHARP_DEVKIT_TOOLS[1].handler({ commandId: "csdevkit.debug.hotReload", arguments: [] });
    const payload = JSON.parse(result.content[0].text);

    expect(payload.result.suppressedMessages).toEqual([
      { method: "showErrorMessage", message: "Hot Reload build failed" },
    ]);
  });

  it("resolves the project from the active editor when launch arguments are omitted", async () => {
    const vscode = await import("vscode");
    readdir.mockResolvedValueOnce([
      { name: "Esi.AI.Studio.csproj", isFile: () => true } as never,
    ]);
    (vscode.window as { activeTextEditor?: unknown }).activeTextEditor = {
      document: { uri: { scheme: "file", fsPath: "/workspace/src/Esi.AI.Studio/Program.cs" } },
    };

    const result = await CSHARP_DEVKIT_TOOLS[1].handler({ commandId: "csdevkit.debug.projectDebugLaunch" });
    const payload = JSON.parse(result.content[0].text);

    expect(payload.result.argumentsValue).toEqual([{ scheme: "file", fsPath: "/workspace/src/Esi.AI.Studio/Esi.AI.Studio.csproj" }]);
  });

  it("rejects commands outside the EsiMCP allowlist", async () => {
    await expect(CSHARP_DEVKIT_TOOLS[1].handler({ commandId: "csdevkit.buildSolution" })).rejects.toThrow(
      "is not allowed by the EsiMCP C# Dev Kit wrapper",
    );
  });

  it("uses the shared debug handler for the virtual active-session command", async () => {
    const getActiveSessionId = vi.fn(() => "session-123");
    const result = await CSHARP_DEVKIT_TOOLS[1].handler(
      { commandId: "csdevkit.debug.active.session", arguments: [] },
      { getActiveSessionId } as never,
    );
    const payload = JSON.parse(result.content[0].text);

    expect(payload).toBe("session-123");
    expect(getActiveSessionId).toHaveBeenCalledOnce();
  });

  it("uses the shared readiness handler for the virtual readiness command", async () => {
    const waitForDebugHostReadiness = vi.fn().mockResolvedValue(true);
    const result = await CSHARP_DEVKIT_TOOLS[1].handler(
      { commandId: "csdevkit.debug.check.host.readyness", arguments: [] },
      { getActiveSessionId: vi.fn(() => "session-123") } as never,
      { waitForDebugHostReadiness } as never,
    );

    expect(JSON.parse(result.content[0].text)).toEqual({ ready: true });
    expect(waitForDebugHostReadiness).toHaveBeenCalledOnce();
  });

  it("returns buffered Debug Console diagnostics for the active session", async () => {
    const getDebugConsoleDiagnostics = vi.fn(() => ({ sessionId: "session-123", bufferedCharacters: 256, readinessStringSeen: true, output: "debug output", lastLine: "debug output" }));
    const result = await CSHARP_DEVKIT_TOOLS[1].handler(
      { commandId: "csdevkit.debug.output.diagnostics", arguments: [] },
      { getActiveSessionId: vi.fn(() => "session-123") } as never,
      { getDebugConsoleDiagnostics } as never,
    );

    expect(JSON.parse(result.content[0].text)).toEqual({ sessionId: "session-123", bufferedCharacters: 256, readinessStringSeen: true, output: "debug output", lastLine: "debug output" });
    expect(getDebugConsoleDiagnostics).toHaveBeenCalledWith("session-123");
  });

  it("uses the shared restart handler for the virtual restart command", async () => {
    const restartDebugging = vi.fn().mockResolvedValue(true);
    const result = await CSHARP_DEVKIT_TOOLS[1].handler(
      { commandId: "csdevkit.debug.restart", arguments: [{ rebuildTaskName: "build" }] },
      { restartDebugging } as never,
    );

    expect(JSON.parse(result.content[0].text)).toEqual({ restarted: true });
    expect(restartDebugging).toHaveBeenCalledWith("build");
  });

  it("uses the shared stop handler for the virtual stop command", async () => {
    const stopDebugging = vi.fn().mockResolvedValue(undefined);
    const resetDebugHostReadiness = vi.fn();
    const result = await CSHARP_DEVKIT_TOOLS[1].handler(
      { commandId: "csdevkit.debug.stop", arguments: [] },
      { stopDebugging } as never,
      { resetDebugHostReadiness } as never,
    );

    expect(JSON.parse(result.content[0].text)).toEqual({ stopped: true });
    expect(stopDebugging).toHaveBeenCalledOnce();
    expect(resetDebugHostReadiness).toHaveBeenCalledOnce();
  });
});