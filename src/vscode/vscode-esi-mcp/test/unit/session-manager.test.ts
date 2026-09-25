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
  onDidCloseTerminal: vi.fn((listener: (terminal: MockTerminal) => void) => {
    mockState.terminalCloseListeners.push(listener);
    return { dispose: vi.fn() };
  }),
  onDidOpenTerminal: vi.fn(() => ({ dispose: vi.fn() })),
  onDidStartTerminalShellExecution: vi.fn(() => ({ dispose: vi.fn() })),
  onDidEndTerminalShellExecution: vi.fn(() => ({ dispose: vi.fn() })),
  debugEventListeners: [] as Array<(event: DebugEvent) => void>,
  terminalCloseListeners: [] as Array<(terminal: MockTerminal) => void>,
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

describe("SessionManager terminal recovery", () => {
  beforeEach(() => {
    vi.unstubAllGlobals();
    mockState.terminals.length = 0;
    mockState.activeTerminal = null;
    mockState.includeAllTerminals = false;
    mockState.debugHostReadinessTimeoutSeconds = 60;
    mockState.debugHostReadinessUrl = "";
    mockState.debugEventListeners.length = 0;
    mockState.terminalCloseListeners.length = 0;
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

  it("binds terminal readiness to the debug session created by debug.start", async () => {
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

  it("removes readiness when the associated terminal closes", async () => {
    const manager = new SessionManager();
    const terminal = new MockTerminal("Esi.Web.dll");
    const onOpenTerminal = mockState.onDidOpenTerminal.mock.calls[0]?.[0] as (terminal: MockTerminal) => void;
    const onShellExecution = mockState.onDidStartTerminalShellExecution.mock.calls[0]?.[0] as (event: {
      terminal: MockTerminal;
      execution: { read: () => AsyncIterable<string> };
    }) => Promise<void>;
    const readinessBySession = (manager as unknown as {
      debugReadinessBySession: Map<string, { ready: boolean }>;
    }).debugReadinessBySession;

    manager.prepareDebugHostReadiness();
    onOpenTerminal(terminal);
    manager.bindDebugHostReadiness({ id: "session-closed-terminal", name: "Esi.Web .NET Server" } as never);
    await onShellExecution({
      terminal,
      execution: {
        async *read() {
          yield "Now ready on: https://localhost:5012";
        },
      },
    });
    expect(readinessBySession.get("session-closed-terminal")?.ready).toBe(true);

    mockState.terminalCloseListeners[0]?.(terminal);

    expect(readinessBySession.has("session-closed-terminal")).toBe(false);
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

  it("returns ready when the configured host responds without an active debug session", async () => {
    mockState.debugHostReadinessUrl = "https://localhost:5012";
    mockState.debugHostReadinessTimeoutSeconds = 0.001;
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      body: { cancel: vi.fn().mockResolvedValue(undefined) },
    });
    vi.stubGlobal("fetch", fetchMock);
    const manager = new SessionManager();
    const debugManager = {
      getActiveSessionId: () => null,
      onDebugEvent: vi.fn(() => ({ dispose: vi.fn() })),
    };

    await expect(manager.waitForDebugHostReadiness(debugManager)).resolves.toBe(true);
    expect(fetchMock).toHaveBeenCalledWith("https://localhost:5012", expect.objectContaining({ method: "GET" }));
    manager.dispose();
  });

  it("returns false without waiting when no host URL, session, or launch exists", async () => {
    mockState.debugHostReadinessTimeoutSeconds = 60;
    const manager = new SessionManager();
    const debugManager = {
      getActiveSessionId: () => null,
      onDebugEvent: vi.fn(() => ({ dispose: vi.fn() })),
    };
    let settled = false;
    const readiness = manager.waitForDebugHostReadiness(debugManager).then((ready) => {
      settled = true;
      return ready;
    });

    await Promise.resolve();

    expect(settled).toBe(true);
    await expect(readiness).resolves.toBe(false);
    manager.dispose();
  });

  it("waits while a launch is pending and returns false when the launch is canceled", async () => {
    mockState.debugHostReadinessTimeoutSeconds = 0.001;
    const manager = new SessionManager();
    const debugManager = {
      getActiveSessionId: () => null,
      onDebugEvent: vi.fn((listener: (event: DebugEvent) => void) => {
        mockState.debugEventListeners.push(listener);
        return { dispose: vi.fn() };
      }),
    };
    manager.prepareDebugHostReadiness();
    const readiness = manager.waitForDebugHostReadiness(debugManager);

    manager.cancelPendingDebugHostReadiness();

    await expect(readiness).resolves.toBe(false);
    manager.dispose();
  });

  it("returns false when the launched session terminates before the first readiness poll", async () => {
    let activeSessionId: string | null = null;
    const debugManager = {
      getActiveSessionId: () => activeSessionId,
      onDebugEvent: vi.fn((listener: (event: DebugEvent) => void) => {
        mockState.debugEventListeners.push(listener);
        return { dispose: vi.fn() };
      }),
      onDebugOutput: vi.fn(() => ({ dispose: vi.fn() })),
    };
    const manager = new SessionManager();
    manager.attachDebugManager(debugManager);
    manager.prepareDebugHostReadiness();
    const readiness = manager.waitForDebugHostReadiness(debugManager);
    activeSessionId = "session-fast";
    manager.bindDebugHostReadiness({ id: "session-fast", name: "Esi.Web .NET Server" } as never);
    activeSessionId = null;

    const terminatedEvent: DebugEvent = {
      id: 1,
      type: "terminated",
      sessionId: "session-fast",
      sessionName: "Esi.Web .NET Server",
      timestamp: new Date().toISOString(),
    };
    for (const listener of [...mockState.debugEventListeners]) listener(terminatedEvent);

    await expect(readiness).resolves.toBe(false);
    manager.dispose();
  });

  it("aborts a hanging configured-host probe on a debug exception", async () => {
    mockState.debugHostReadinessUrl = "https://localhost:5012";
    const fetchMock = vi.fn(() => new Promise(() => undefined));
    vi.stubGlobal("fetch", fetchMock);
    const debugManager = {
      getActiveSessionId: () => "session-1",
      onDebugEvent: vi.fn((listener: (event: DebugEvent) => void) => {
        mockState.debugEventListeners.push(listener);
        return { dispose: vi.fn() };
      }),
    };
    const manager = new SessionManager();
    const readiness = manager.waitForDebugHostReadiness(debugManager, "session-1");

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

    await expect(readiness).rejects.toMatchObject({ code: "DEBUG_SESSION_EXCEPTION" });
    expect(fetchMock).toHaveBeenCalledOnce();
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

  it("keeps readiness true after later output evicts the ready string from the bounded buffer", async () => {
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

    outputListener?.({ id: "session-1", name: "Esi.Web .NET Server" }, "Now ready on: https://localhost:5012");
    outputListener?.({ id: "session-1", name: "Esi.Web .NET Server" }, "x".repeat(64 * 1024));

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

    manager.prepareDebugHostReadiness();
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
    const readinessBySession = (manager as unknown as {
      debugReadinessBySession: Map<string, unknown>;
    }).debugReadinessBySession;
    expect(readinessBySession.has("session-a")).toBe(false);
    expect(readinessBySession.has("session-b")).toBe(true);
    await expect(manager.waitForDebugHostReadiness(debugManager, "session-b")).resolves.toBe(true);
    manager.dispose();
  });

  it("does not bind one terminal's readiness to another debug session", async () => {
    let activeSessionId: string | null = "session-a";
    let outputListener: ((session: { id: string; name: string }, output: string) => void) | undefined;
    const debugManager = {
      getActiveSessionId: () => activeSessionId,
      onDebugEvent: vi.fn(() => ({ dispose: vi.fn() })),
      onDebugOutput: vi.fn((listener: (session: { id: string; name: string }, output: string) => void) => {
        outputListener = listener;
        return { dispose: vi.fn() };
      }),
    };
    const manager = new SessionManager();
    manager.attachDebugManager(debugManager);
    outputListener?.({ id: "session-a", name: "Esi.Web .NET Server" }, "Starting A");
    outputListener?.({ id: "session-b", name: "Esi.Web .NET Server" }, "Starting B");

    const terminal = new MockTerminal("Esi.Web.dll");
    const onOpenTerminal = mockState.onDidOpenTerminal.mock.calls[0]?.[0] as (terminal: MockTerminal) => void;
    const onShellExecution = mockState.onDidStartTerminalShellExecution.mock.calls[0]?.[0] as (event: {
      terminal: MockTerminal;
      execution: { read: () => AsyncIterable<string> };
    }) => Promise<void>;
    manager.prepareDebugHostReadiness();
    onOpenTerminal(terminal);
    manager.bindDebugHostReadiness({ id: "session-a", name: "Esi.Web .NET Server" } as never);
    await onShellExecution({
      terminal,
      execution: {
        async *read() {
          yield "Now ready on: https://localhost:5012";
        },
      },
    });

    mockState.debugHostReadinessTimeoutSeconds = 0.001;
    await expect(manager.waitForDebugHostReadiness(debugManager, "session-a")).resolves.toBe(true);
    await expect(manager.waitForDebugHostReadiness(debugManager, "session-b")).resolves.toBe(false);
    activeSessionId = null;
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