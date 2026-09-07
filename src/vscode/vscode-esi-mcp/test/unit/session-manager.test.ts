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
  includeAllTerminals: false,
  debugHostReadinessTimeoutSeconds: 60,
  onDidCloseTerminal: vi.fn(() => ({ dispose: vi.fn() })),
  onDidStartTerminalShellExecution: vi.fn(() => ({ dispose: vi.fn() })),
  onDidEndTerminalShellExecution: vi.fn(() => ({ dispose: vi.fn() })),
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
    onDidCloseTerminal: mockState.onDidCloseTerminal,
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
          : defaultValue,
    }),
  },
}));

import { SessionManager } from "../../src/terminal/session-manager.js";

describe("SessionManager terminal recovery", () => {
  beforeEach(() => {
    mockState.terminals.length = 0;
    mockState.includeAllTerminals = false;
    mockState.debugHostReadinessTimeoutSeconds = 60;
    mockState.debugEventListeners.length = 0;
    vi.clearAllMocks();
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

  it("resolves when the configured ready string appears in terminal output", async () => {
    const manager = new SessionManager();
    const waiter = manager.waitForTerminalOutput("ready", 1000);
    const listener = mockState.onDidStartTerminalShellExecution.mock.calls[0]?.[0] as ((event: {
      execution: { read: () => AsyncIterable<string> };
    }) => Promise<void>);

    await listener({
      execution: {
        async *read() {
          yield "service is ";
          yield "ready now";
        },
      },
    });

    await expect(waiter.promise).resolves.toBeUndefined();
    manager.dispose();
  });

  it("uses the managed terminal buffer instead of competing for its output stream", async () => {
    mockState.includeAllTerminals = true;
    const terminal = new MockTerminal("MCP: web");
    mockState.terminals.push(terminal);

    const manager = new SessionManager();
    const waiter = manager.waitForTerminalOutput("Now ready on:", 1000);
    const listener = mockState.onDidStartTerminalShellExecution.mock.calls[0]?.[0] as (event: {
      terminal: MockTerminal;
      execution: { read: () => AsyncIterable<string> };
    }) => Promise<void>;

    await listener({
      terminal,
      execution: {
        async *read() {
          yield "Now ready on: https://localhost:5012";
        },
      },
    });

    await expect(waiter.promise).resolves.toBeUndefined();
    manager.dispose();
  });

  it("keeps the readiness latch after a successful wait", async () => {
    const terminal = new MockTerminal("dotnet: Esi.AI.Studio");
    mockState.terminals.push(terminal);
    const manager = new SessionManager();
    const listener = mockState.onDidStartTerminalShellExecution.mock.calls[0]?.[0] as ((event: {
      terminal: MockTerminal;
      execution: { read: () => AsyncIterable<string> };
    }) => Promise<void>);

    await listener({
      terminal,
      execution: {
        async *read() {
          yield "Now ready on: https://localhost:5012";
        },
      },
    });

    await expect(manager.waitForDebugHostReadiness()).resolves.toBe(true);
    await expect(manager.waitForDebugHostReadiness()).resolves.toBe(true);
    manager.dispose();
  });

  it("finds readiness emitted before the check in an active dotnet terminal", async () => {
    const activeTerminal = new MockTerminal("dotnet: Esi.AI.Studio");
    const exitedTerminal = new MockTerminal("dotnet: old", undefined, { code: 0 });
    mockState.terminals.push(activeTerminal, exitedTerminal);
    const manager = new SessionManager();
    const listener = mockState.onDidStartTerminalShellExecution.mock.calls[0]?.[0] as (event: {
      terminal: MockTerminal;
      execution: { read: () => AsyncIterable<string> };
    }) => Promise<void>;

    await listener({
      terminal: activeTerminal,
      execution: {
        async *read() {
          yield "Now ready on: https://localhost:7010";
        },
      },
    });
    await listener({
      terminal: exitedTerminal,
      execution: {
        async *read() {
          yield "Now ready on: https://localhost:9999";
        },
      },
    });

    await expect(manager.waitForDebugHostReadiness()).resolves.toBe(true);
    manager.dispose();
  });

  it("clears readiness when the terminal that reported readiness exits", async () => {
    const terminal = new MockTerminal("dotnet: Esi.AI.Studio");
    mockState.terminals.push(terminal);
    const manager = new SessionManager();
    const listener = mockState.onDidStartTerminalShellExecution.mock.calls[0]?.[0] as (event: {
      terminal: MockTerminal;
      execution: { read: () => AsyncIterable<string> };
    }) => Promise<void>;

    await listener({
      terminal,
      execution: {
        async *read() {
          yield "Now ready on: https://localhost:7010";
        },
      },
    });
    await expect(manager.waitForDebugHostReadiness()).resolves.toBe(true);

    terminal.exitStatus = { code: 0 };
    mockState.debugHostReadinessTimeoutSeconds = 0.001;
    await expect(manager.waitForDebugHostReadiness()).resolves.toBe(false);
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

  it("keeps the readiness latch available after a timeout", async () => {
    mockState.debugHostReadinessTimeoutSeconds = 0.001;
    const terminal = new MockTerminal("dotnet: Esi.AI.Studio");
    mockState.terminals.push(terminal);
    const manager = new SessionManager();

    await expect(manager.waitForDebugHostReadiness()).resolves.toBe(false);

    const listener = mockState.onDidStartTerminalShellExecution.mock.calls[0]?.[0] as ((event: {
      terminal: MockTerminal;
      execution: { read: () => AsyncIterable<string> };
    }) => Promise<void>);
    await listener({
      terminal,
      execution: {
        async *read() {
          yield "Now ready on: https://localhost:5012";
        },
      },
    });

    await expect(manager.waitForDebugHostReadiness()).resolves.toBe(true);
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

});