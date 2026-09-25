import { beforeEach, describe, expect, it, vi } from "vitest";

const { debugState, readdir, showErrorMessage, windowApi } = vi.hoisted(() => {
  const showErrorMessage = vi.fn();
  return {
    debugState: {
      activeDebugSession: undefined as { id: string; name: string } | undefined,
      startListeners: [] as Array<(session: { id: string; name: string }) => void>,
    },
    readdir: vi.fn(),
    showErrorMessage,
    windowApi: { activeTextEditor: undefined, showErrorMessage },
  };
});

vi.mock("node:fs/promises", () => ({ readdir }));

const extension = {
  isActive: true,
  packageJSON: {
    version: "3.20.199",
    contributes: {
      commands: [
        { command: "csdevkit.addExistingProject", title: "Add Existing Project..." },
        { command: "csdevkit.buildSolution", title: "Build Solution" },
        { command: "csdevkit.debug.fileLaunch", title: "Debug project associated with this file" },
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
      "csdevkit.addExistingProject",
      "csdevkit.buildSolution",
      "csdevkit.debug.fileLaunch",
      "csdevkit.debug.projectDebugLaunch",
      "csdevkit.debug.hotReload",
      "csdevkit.debug.showHotReloadPanel",
      "csdevkit.debug.selectStartupProject",
    ]),
    executeCommand: vi.fn(async (command: string, ...argumentsValue: unknown[]) => {
      if (command === "csdevkit.debug.hotReload") await windowApi.showErrorMessage("Hot Reload build failed");
      if (command === "csdevkit.debug.fileLaunch" || command === "csdevkit.debug.projectDebugLaunch") {
        const session = { id: `session-${command.split(".").at(-1)}`, name: "Esi.Web" };
        debugState.activeDebugSession = session;
        debugState.startListeners.forEach((listener) => listener(session));
      }
      return { command, argumentsValue };
    }),
  },
  debug: {
    get activeDebugSession() { return debugState.activeDebugSession; },
    onDidStartDebugSession: vi.fn((listener: (session: { id: string; name: string }) => void) => {
      debugState.startListeners.push(listener);
      return { dispose: () => {
        const index = debugState.startListeners.indexOf(listener);
        if (index >= 0) debugState.startListeners.splice(index, 1);
      } };
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
  beforeEach(() => {
    debugState.activeDebugSession = undefined;
    debugState.startListeners.length = 0;
  });

  it("lists commands from the installed C# Dev Kit manifest", async () => {
    const result = await CSHARP_DEVKIT_TOOLS[0].handler({});
    const payload = JSON.parse(result.content[0].text);

    expect(payload.extensionId).toBe("ms-dotnettools.csdevkit");
  expect(payload.commands.map(({ argumentsSchema: _argumentsSchema, argumentsExample: _argumentsExample, invocationSyntax: _invocationSyntax, ...command }) => command)).toEqual([
      { command: "csdevkit.addExistingProject", title: "Add Existing Project...", keyboardShortcuts: [], menuContexts: [], registered: true },
      { command: "csdevkit.buildSolution", title: "Build Solution", keyboardShortcuts: [], menuContexts: ["commandPalette"], registered: true },
      { command: "csdevkit.debug.fileLaunch", title: "Debug project associated with this file", keyboardShortcuts: [], menuContexts: [], registered: true },
      { command: "csdevkit.debug.projectDebugLaunch", title: "Launch Project", keyboardShortcuts: [], menuContexts: ["debug/toolBar"], registered: true },
      { command: "csdevkit.debug.hotReload", title: "Hot Reload", keyboardShortcuts: ["Ctrl+Shift+Enter"], menuContexts: [], registered: true },
      { command: "csdevkit.debug.showHotReloadPanel", title: "Show Hot Reload output", keyboardShortcuts: [], menuContexts: [], registered: true },
      { command: "csdevkit.debug.selectStartupProject", title: "Select C# Startup Project", keyboardShortcuts: [], menuContexts: [], registered: true },
      { command: "csdevkit.debug.active.session", title: "Active Debug Session", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.check.host.readyness", title: "Check Debug Host Readiness", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.output.diagnostics", title: "Debug Console Diagnostics", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.stop", title: "Stop Debugging", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.restart", title: "Restart Debugging", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.settings", title: "Debug Settings", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.wait.for.event", title: "Debug Wait For Event", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.step.over", title: "Debug Step Over", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.step.into", title: "Debug Step Into", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.step.out", title: "Debug Step Out", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.continue", title: "Debug Continue", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.pause", title: "Debug Pause", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.add.breakpoint", title: "Debug Add Breakpoint", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.add.logpoint", title: "Debug Add Logpoint", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.remove.breakpoint", title: "Debug Remove Breakpoint", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.clear.all.breakpoints", title: "Debug Clear All Breakpoints", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.list.breakpoints", title: "Debug List Breakpoints", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.list.variable.names", title: "Debug List Variable Names", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.get.variables.values", title: "Debug Get Variables Values", keyboardShortcuts: [], menuContexts: [], registered: false },
      { command: "csdevkit.debug.evaluate.expression", title: "Debug Evaluate Expression", keyboardShortcuts: [], menuContexts: [], registered: false },
    ]);
    expect(payload.commands.map((command: { command: string }) => command.command)).toContain("csdevkit.addExistingProject");
    expect(payload.commands.map((command: { command: string }) => command.command)).not.toContain("csdevkit.debug.start");
    expect(payload.commands.find((command: { command: string }) => command.command === "csdevkit.debug.fileLaunch").invocationSyntax).toContain("absolute Esi.Web .csproj path");
    expect(payload.commands.every((command: { invocationSyntax: string }) => command.invocationSyntax.length > 0)).toBe(true);
    expect(payload.commands.filter((command: { registered: boolean; command: string }) => command.registered && !["csdevkit.debug.fileLaunch", "csdevkit.debug.projectDebugLaunch"].includes(command.command)).every((command: { argumentsSchema: { type: string; maxItems: number } }) => command.argumentsSchema.type === "array" && command.argumentsSchema.maxItems === 20)).toBe(true);
    const fileLaunchCommand = payload.commands.find((command: { command: string }) => command.command === "csdevkit.debug.fileLaunch");
    expect(fileLaunchCommand.argumentsSchema).toMatchObject({ type: "array", minItems: 1, maxItems: 1 });
    expect(fileLaunchCommand.argumentsSchema.items.properties).toHaveProperty("fsPath");
    const launchCommand = payload.commands.find((command: { command: string }) => command.command === "csdevkit.debug.projectDebugLaunch") as { argumentsSchema: Record<string, unknown>; argumentsExample: unknown[] };
    expect(launchCommand.argumentsSchema).toMatchObject({
      type: "array",
      minItems: 1,
      maxItems: 1,
      items: {
        type: "object",
        required: ["path"],
        properties: { path: { type: "string" } },
      },
    });
    expect(launchCommand.argumentsExample).toEqual([{ path: "/absolute/path/to/Project.csproj" }]);
    expect(payload.commands.filter((command: { command: string }) => ["csdevkit.debug.step.over", "csdevkit.debug.step.into", "csdevkit.debug.step.out", "csdevkit.debug.continue", "csdevkit.debug.pause", "csdevkit.debug.clear.all.breakpoints", "csdevkit.debug.list.breakpoints"].includes(command.command)).every((command: { argumentsSchema: { type: string; maxItems: number } }) => command.argumentsSchema.type === "array" && command.argumentsSchema.maxItems === 0)).toBe(true);
    const readinessCommand = payload.commands.find((command: { command: string }) => command.command === "csdevkit.debug.check.host.readyness") as { argumentsSchema: { type: string; maxItems: number } };
    expect(readinessCommand.argumentsSchema).toMatchObject({ type: "array", maxItems: 1 });
  });

  it("executes projectDebugLaunch with its URI argument and transfers the old start lifecycle", async () => {
    const projectContext = { path: "/workspace/Esi.AI.Studio.csproj" };
    const prepareDebugHostReadiness = vi.fn();
    const bindDebugHostReadiness = vi.fn();
    const cancelPendingDebugHostReadiness = vi.fn();

    const result = await CSHARP_DEVKIT_TOOLS[1].handler(
      { commandId: "csdevkit.debug.projectDebugLaunch", arguments: [projectContext] },
      undefined,
      { prepareDebugHostReadiness, bindDebugHostReadiness, cancelPendingDebugHostReadiness } as never,
    );
    const payload = JSON.parse(result.content[0].text);

    expect(payload.commandId).toBe("csdevkit.debug.projectDebugLaunch");
    expect(payload.result.argumentsValue).toEqual([projectContext]);
    expect(payload).toMatchObject({ started: true, sessionId: "session-projectDebugLaunch" });
    expect(prepareDebugHostReadiness).toHaveBeenCalledOnce();
    expect(bindDebugHostReadiness).toHaveBeenCalledWith(debugState.activeDebugSession);
    expect(cancelPendingDebugHostReadiness).not.toHaveBeenCalled();
  });

  it("rejects URI-shaped project launch arguments", async () => {
    await expect(CSHARP_DEVKIT_TOOLS[1].handler({
      commandId: "csdevkit.debug.projectDebugLaunch",
      arguments: [{ scheme: "file", fsPath: "/workspace/Esi.AI.Studio.csproj" }],
    })).rejects.toThrow();
  });

  it("queries one command's invocation syntax", async () => {
    const result = await CSHARP_DEVKIT_TOOLS[0].handler({ commandId: "csdevkit.debug.fileLaunch" });
    const payload = JSON.parse(result.content[0].text);

    expect(payload.commands).toHaveLength(1);
    expect(payload.commands[0].command).toBe("csdevkit.debug.fileLaunch");
    expect(payload.commands[0].invocationSyntax).toContain("arguments");
    expect(payload.commands[0].invocationSyntax).toContain("absolute Esi.Web .csproj path");
  });

  it("executes fileLaunch with its URI argument and transfers the old start lifecycle", async () => {
    const fileUri = { scheme: "file", fsPath: "C:\\workspace\\Esi.Web.csproj" };
    const prepareDebugHostReadiness = vi.fn();
    const bindDebugHostReadiness = vi.fn();
    const cancelPendingDebugHostReadiness = vi.fn();

    const result = await CSHARP_DEVKIT_TOOLS[1].handler(
      { commandId: "csdevkit.debug.fileLaunch", arguments: [fileUri] },
      undefined,
      { prepareDebugHostReadiness, bindDebugHostReadiness, cancelPendingDebugHostReadiness } as never,
    );

    const payload = JSON.parse(result.content[0].text);
    expect(payload.commandId).toBe("csdevkit.debug.fileLaunch");
    expect(payload.result.argumentsValue).toEqual([fileUri]);
    expect(payload).toMatchObject({ started: true, sessionId: "session-fileLaunch" });
    expect(prepareDebugHostReadiness).toHaveBeenCalledOnce();
    expect(bindDebugHostReadiness).toHaveBeenCalledWith(debugState.activeDebugSession);
    expect(cancelPendingDebugHostReadiness).not.toHaveBeenCalled();
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

    expect(payload.result.argumentsValue).toEqual([{ path: "/workspace/src/Esi.AI.Studio/Esi.AI.Studio.csproj" }]);
  });

  it("rejects commands outside the installed C# Dev Kit manifest", async () => {
    await expect(CSHARP_DEVKIT_TOOLS[1].handler({ commandId: "csdevkit.notDeclared" })).rejects.toThrow(
      "is not declared by the C# Dev Kit",
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

  it("executes migrated debugger commands through the C# Dev Kit namespace", async () => {
    const stepOver = vi.fn().mockResolvedValue(undefined);
    await CSHARP_DEVKIT_TOOLS[1].handler(
      { commandId: "csdevkit.debug.step.over", arguments: [] },
      { stepOver } as never,
      {} as never,
    );

    expect(stepOver).toHaveBeenCalledOnce();
  });
});