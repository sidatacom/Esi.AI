import { beforeEach, describe, expect, it, vi } from "vitest";
import type { DebugEvent } from "../../src/debug/manager.js";

class MockTerminal {
  readonly name: string;
  readonly creationOptions: { cwd?: string };
  exitStatus: unknown;
  dispose = vi.fn();
  sendText = vi.fn();
  show = vi.fn();

  constructor(name: string, cwd?: string, exitStatus?: unknown) {
    this.name = name;
    this.creationOptions = { cwd };
    this.exitStatus = exitStatus;
  }
}

const mockState = vi.hoisted(() => ({
  terminals: [] as Array<MockTerminal>,
  activeTerminal: null as MockTerminal | null,
  includeAllTerminals: false,
  debugHostReadinessTimeoutSeconds: 60,
  debugHostReadinessUrl: "",
  onDidCloseTerminal: vi.fn(() => ({ dispose: vi.fn() })),
  onDidOpenTerminal: vi.fn(() => ({ dispose: vi.fn() })),
  onDidStartTerminalShellExecution: vi.fn(() => ({ dispose: vi.fn() })),
  shellEndListeners: [] as Array<(event: { terminal: unknown; exitCode?: number }) => void>,
  onDidEndTerminalShellExecution: vi.fn((listener: (event: { terminal: unknown; exitCode?: number }) => void) => {
    mockState.shellEndListeners.push(listener);
    return { dispose: () => {
      const index = mockState.shellEndListeners.indexOf(listener);
      if (index >= 0) mockState.shellEndListeners.splice(index, 1);
    } };
  }),
  debugEventListeners: [] as Array<(event: DebugEvent) => void>,
}));

vi.mock("vscode", () => ({
  EventEmitter: class {
    event = vi.fn();
    fire = vi.fn();
    dispose = vi.fn();
  },
  window: {
    get terminals() {
      return mockState.terminals;
    },
    get activeTerminal() {
      return mockState.activeTerminal;
    },
    onDidCloseTerminal: mockState.onDidCloseTerminal,
    onDidOpenTerminal: mockState.onDidOpenTerminal,
    onDidStartTerminalShellExecution: mockState.onDidStartTerminalShellExecution,
    onDidEndTerminalShellExecution: mockState.onDidEndTerminalShellExecution,
    createTerminal: vi.fn(),
  },
  workspace: {
    workspaceFolders: [{ uri: { fsPath: "C:\\workspace" } }],
    getConfiguration: () => ({
      get: (key: string, defaultValue: unknown) =>
        key === "includeAllTerminals"
          ? mockState.includeAllTerminals
          : key === "debugHostReadinessTimeoutSeconds"
            ? mockState.debugHostReadinessTimeoutSeconds
            : key === "debugHostReadinessUrl"
              ? mockState.debugHostReadinessUrl
          : defaultValue,
    }),
  },
}));

import { SessionManager } from "../../src/terminal/session-manager.js";
import { TerminalSession } from "../../src/terminal/session.js";

describe("SessionManager terminal recovery", () => {
  beforeEach(() => {
    mockState.terminals.length = 0;
    mockState.activeTerminal = null;
    mockState.includeAllTerminals = false;
    mockState.debugHostReadinessTimeoutSeconds = 60;
    mockState.debugHostReadinessUrl = "";
    mockState.debugEventListeners.length = 0;
    mockState.shellEndListeners.length = 0;
    vi.clearAllMocks();
  });

  it("resolves waitForCompletion when shell execution ends during sendText", async () => {
    const terminal = new MockTerminal("MCP: execution");
    terminal.sendText.mockImplementation(() => {
      const event = { terminal, exitCode: 0 };
      for (const listener of [...mockState.shellEndListeners]) listener(event);
    });
    const session = new TerminalSession(
      { name: "execution" },
      100,
      { completionPollIntervalMs: 10, completionSettleMs: 5 },
      terminal as never,
    );

    const result = await session.execute("Write-Output complete", 100, true);

    expect(result).toMatchObject({ exitCode: 0, timedOut: false });
    mockState.shellEndListeners.length = 0;
  });

  it("recovers only EsiMCP-owned terminals and closes the adopted terminal", () => {
    const recovered = new MockTerminal("MCP: validation", "C:\\repo");
    const unrelated = new MockTerminal("pwsh");
    mockState.terminals.push(recovered, unrelated);

    const manager = new SessionManager();
    const sessions = manager.listSessions();

    expect(sessions).toHaveLength(1);
    expect(sessions[0].name).toBe("validation");
    expect(sessions[0].cwd).toBe("C:\\repo");
    expect(manager.closeSession(sessions[0].sessionId)).toBe(true);
    expect(recovered.dispose).toHaveBeenCalledOnce();
    expect(unrelated.dispose).not.toHaveBeenCalled();
    manager.dispose();
  });

  it("recovers and closes all visible terminals when enabled", () => {
    mockState.includeAllTerminals = true;
    const recovered = new MockTerminal("MCP: validation", "C:\\repo");
    const unrelated = new MockTerminal("pwsh");
    mockState.terminals.push(recovered, unrelated);

    const manager = new SessionManager();
    const sessions = manager.listSessions();

    expect(sessions).toHaveLength(2);
    expect(sessions.map((session) => session.name)).toEqual([
      "validation",
      "pwsh",
    ]);
    expect(manager.closeSession(sessions[0].sessionId)).toBe(true);
    expect(manager.closeSession(sessions[1].sessionId)).toBe(true);
    expect(recovered.dispose).toHaveBeenCalledOnce();
    expect(unrelated.dispose).toHaveBeenCalledOnce();
    manager.dispose();
  });

  it("keeps normal creation behavior", async () => {
    const vscode = await import("vscode");
    const created = new MockTerminal("MCP: created");
    vi.mocked(vscode.window.createTerminal).mockReturnValue(created as never);

    const manager = new SessionManager();
    manager.createSession({ name: "created" });

    expect(vscode.window.createTerminal).toHaveBeenCalledOnce();
    expect(created.name).toBe("MCP: created");
    manager.dispose();
  });

  it("binds terminal readiness to a C# Dev Kit-launched debug session", async () => {
    const manager = new SessionManager();
    const openedTerminal = new MockTerminal("Esi.Web.dll");
    const openListener = mockState.onDidOpenTerminal.mock.calls[0]?.[0] as ((terminal: MockTerminal) => void);
    const shellListener = mockState.onDidStartTerminalShellExecution.mock.calls[0]?.[0] as (event: {
      terminal: MockTerminal;
      execution: { read: () => AsyncIterable<string> };
    }) => Promise<void>;

    manager.prepareDebugHostReadiness();
    openListener(openedTerminal);
    mockState.terminals.push(openedTerminal);
    manager.bindDebugHostReadiness({ id: "session-started", name: "Esi.Web .NET Server" } as never);

    await shellListener({
      terminal: openedTerminal,
      execution: {
        async *read() {
          yield "Now ready on: https://localhost:5012";
        },
      },
    });

    const debugManager = { getActiveSessionId: () => "session-started", onDebugEvent: vi.fn(() => ({ dispose: vi.fn() })) };
    await expect(manager.waitForDebugHostReadiness(debugManager, "session-started")).resolves.toBe(true);
    await expect(manager.waitForDebugHostReadiness(debugManager, "another-session")).resolves.toBe(false);
    manager.dispose();
  });

  it("inherits terminal readiness already captured before the debug session is bound", async () => {
    const manager = new SessionManager();
    const openedTerminal = new MockTerminal("Esi.Web.dll");
    const openListener = mockState.onDidOpenTerminal.mock.calls[0]?.[0] as ((terminal: MockTerminal) => void);
    const shellListener = mockState.onDidStartTerminalShellExecution.mock.calls[0]?.[0] as (event: {
      terminal: MockTerminal;
      execution: { read: () => AsyncIterable<string> };
    }) => Promise<void>;

    manager.prepareDebugHostReadiness();
    openListener(openedTerminal);
    mockState.terminals.push(openedTerminal);
    await shellListener({
      terminal: openedTerminal,
      execution: {
        async *read() {
          yield "Now ready on: https://localhost:5012";
        },
      },
    });
    manager.bindDebugHostReadiness({ id: "session-started", name: "Esi.Web .NET Server" } as never);

    const debugManager = { getActiveSessionId: () => "session-started", onDebugEvent: vi.fn(() => ({ dispose: vi.fn() })) };
    await expect(manager.waitForDebugHostReadiness(debugManager, "session-started")).resolves.toBe(true);
    manager.dispose();
  });

  it("binds readiness when the debug terminal opens after the debug session", async () => {
    const manager = new SessionManager();
    manager.bindDebugHostReadiness({ id: "session-started", name: "Esi.Web .NET Server" } as never);

    const openedTerminal = new MockTerminal("Esi.Web.dll");
    const shellListener = mockState.onDidStartTerminalShellExecution.mock.calls[0]?.[0] as (event: {
      terminal: MockTerminal;
      execution: { read: () => AsyncIterable<string> };
    }) => Promise<void>;

    await shellListener({
      terminal: openedTerminal,
      execution: {
        async *read() {
          yield "startup output\r\n".repeat(20);
          yield "Now ready on: https://localhost:5012\r\n";
        },
      },
    });

    const debugManager = { getActiveSessionId: () => "session-started", onDebugEvent: vi.fn(() => ({ dispose: vi.fn() })) };
    await expect(manager.waitForDebugHostReadiness(debugManager, "session-started")).resolves.toBe(true);
    manager.dispose();
  });

  it("does not accept readiness from an exited dotnet terminal", async () => {
    mockState.debugHostReadinessTimeoutSeconds = 0.001;
    const exitedTerminal = new MockTerminal("dotnet: old", undefined, { code: 0 });
    mockState.terminals.push(exitedTerminal);
    const manager = new SessionManager();
    const listener = mockState.onDidStartTerminalShellExecution.mock.calls[0]?.[0] as (event: {
      terminal: MockTerminal;
      execution: { read: () => AsyncIterable<string> };
    }) => Promise<void>;

    await listener({
      terminal: exitedTerminal,
      execution: {
        async *read() {
          yield "Now ready on: https://localhost:7010";
        },
      },
    });

    await expect(manager.waitForDebugHostReadiness()).resolves.toBe(false);
    manager.dispose();
  });

  it("uses DAP readiness from a child debug session for its active parent", async () => {
    let outputListener: ((session: { id: string; name: string }, output: string) => void) | undefined;
    const debugManager = {
      getActiveSessionId: () => "session-parent",
      onDebugEvent: vi.fn(() => ({ dispose: vi.fn() })),
      onDebugOutput: vi.fn((listener: (session: { id: string; name: string }, output: string) => void) => {
        outputListener = listener;
        return { dispose: vi.fn() };
      }),
    };
    const manager = new SessionManager();
    manager.attachDebugManager(debugManager);

    outputListener?.({
      id: "session-child",
      name: "Esi.AI Studio .NET",
      parentSession: { id: "session-parent", name: "C#: Esi.AI.Studio" },
    } as never, "Now ready on: http://localhost:7010");

    await expect(manager.waitForDebugHostReadiness(debugManager, "session-parent")).resolves.toBe(true);
    manager.dispose();
  });

  it("matches a readiness string split across DAP output chunks", async () => {
    let outputListener: ((session: { id: string; name: string }, output: string) => void) | undefined;
    const debugManager = {
      getActiveSessionId: () => "session-1",
      onDebugEvent: vi.fn(() => ({ dispose: vi.fn() })),
      onDebugOutput: vi.fn((listener: (session: { id: string; name: string }, output: string) => void) => {
        outputListener = listener;
        return { dispose: vi.fn() };
      }),
    };
    const manager = new SessionManager();
    manager.attachDebugManager(debugManager);

    outputListener?.({ id: "session-1", name: "C#: Esi.AI.Studio" }, "Now ready ");
    outputListener?.({ id: "session-1", name: "C#: Esi.AI.Studio" }, "on: http://localhost:7010");

    await expect(manager.waitForDebugHostReadiness(debugManager, "session-1")).resolves.toBe(true);
    manager.dispose();
  });

  it("matches ANSI-decorated readiness output from the Debug Console", async () => {
    let outputListener: ((session: { id: string; name: string }, output: string) => void) | undefined;
    const debugManager = {
      getActiveSessionId: () => "session-1",
      onDebugEvent: vi.fn(() => ({ dispose: vi.fn() })),
      onDebugOutput: vi.fn((listener: (session: { id: string; name: string }, output: string) => void) => {
        outputListener = listener;
        return { dispose: vi.fn() };
      }),
    };
    const manager = new SessionManager();
    manager.attachDebugManager(debugManager);

    outputListener?.({ id: "session-1", name: "C#: Esi.AI.Studio" }, "\u001B[32mNow ready on:\u001B[0m http://localhost:7010");

    await expect(manager.waitForDebugHostReadiness(debugManager, "session-1")).resolves.toBe(true);
    manager.dispose();
  });

  it("retains bounded diagnostics for Debug Console output", async () => {
    let outputListener: ((session: { id: string; name: string }, output: string) => void) | undefined;
    const manager = new SessionManager();
    manager.attachDebugManager({
      onDebugEvent: vi.fn(() => ({ dispose: vi.fn() })),
      onDebugOutput: vi.fn((listener: (session: { id: string; name: string }, output: string) => void) => {
        outputListener = listener;
        return { dispose: vi.fn() };
      }),
    });

    outputListener?.({ id: "session-1", name: "C#: Esi.AI.Studio" }, "Now ready on: http://localhost:7010");

    expect(manager.getDebugConsoleDiagnostics("session-1")).toEqual({
      sessionId: "session-1",
      bufferedCharacters: 35,
      readinessStringSeen: true,
      output: "Now ready on: http://localhost:7010",
      lastLine: "Now ready on: http://localhost:7010",
    });
    manager.dispose();
  });

  it("stores DAP readiness independently for each debug session", async () => {
    let activeSessionId: string | null = "session-a";
    let outputListener: ((session: { id: string; name: string }, output: string) => void) | undefined;
    const debugManager = {
      getActiveSessionId: () => activeSessionId,
      hasDebugAdapterTracker: () => true,
      onDebugEvent: vi.fn((listener: (event: DebugEvent) => void) => {
        mockState.debugEventListeners.push(listener);
        return { dispose: vi.fn() };
      }),
      onDebugOutput: vi.fn((listener: (session: { id: string; name: string }, output: string) => void) => {
        outputListener = listener;
        return { dispose: vi.fn() };
      }),
    };
    const manager = new SessionManager();
    manager.attachDebugManager(debugManager);

    outputListener?.({ id: "session-a", name: "Esi.AI Studio" }, "Now ready on: http://localhost:7010");
    await expect(manager.waitForDebugHostReadiness(debugManager, "session-a")).resolves.toBe(true);

    activeSessionId = "session-b";
    mockState.debugHostReadinessTimeoutSeconds = 0.001;
    await expect(manager.waitForDebugHostReadiness(debugManager, "session-b")).resolves.toBe(false);

    outputListener?.({ id: "session-b", name: "Esi.AI Studio" }, "Now ready on: http://localhost:7010");
    await expect(manager.waitForDebugHostReadiness(debugManager, "session-b")).resolves.toBe(true);

    mockState.debugEventListeners[0]?.({
      id: 1,
      type: "terminated",
      sessionId: "session-a",
      sessionName: "Esi.AI Studio",
      timestamp: new Date().toISOString(),
    });
    await expect(manager.waitForDebugHostReadiness(debugManager, "session-b")).resolves.toBe(true);
    manager.dispose();
  });

  it("aborts readiness waiting when the debug session raises an exception", async () => {
    const debugEventDisposable = { dispose: vi.fn() };
    const debugManager = {
      getActiveSessionId: () => "session-1",
      onDebugEvent: vi.fn((listener: (event: DebugEvent) => void) => {
        mockState.debugEventListeners.push(listener);
        return debugEventDisposable;
      }),
    };
    const manager = new SessionManager();
    const readiness = manager.waitForDebugHostReadiness(debugManager);

    mockState.debugEventListeners[0]?.({
      id: 1,
      type: "paused",
      sessionId: "session-1",
      sessionName: "Esi.Web .NET Server",
      timestamp: new Date().toISOString(),
      reason: "exception",
      exceptionType: "System.InvalidOperationException",
      exceptionMessage: "startup failed",
    });

    await expect(readiness).rejects.toMatchObject({
      code: "DEBUG_SESSION_EXCEPTION",
      message: "Debug session exception: startup failed",
    });
    expect(debugEventDisposable.dispose).toHaveBeenCalledOnce();
    manager.dispose();
  });

  it("returns not ready when startup fails before a DAP exception event is published", async () => {
    let outputListener: ((session: { id: string; name: string }, output: string) => void) | undefined;
    const debugManager = {
      getActiveSessionId: () => "session-1",
      onDebugEvent: vi.fn(() => ({ dispose: vi.fn() })),
      onDebugOutput: vi.fn((listener: (session: { id: string; name: string }, output: string) => void) => {
        outputListener = listener;
        return { dispose: vi.fn() };
      }),
    };
    const manager = new SessionManager();
    manager.attachDebugManager(debugManager);
    const readiness = manager.waitForDebugHostReadiness(debugManager, "session-1");

    outputListener?.({ id: "session-1", name: "C#: Esi.AI.Studio" }, "LaunchException thrown: 'System.AggregateException' in Microsoft.Extensions.DependencyInjection.dll");

    await expect(readiness).resolves.toBe(false);
    manager.dispose();
  });

});