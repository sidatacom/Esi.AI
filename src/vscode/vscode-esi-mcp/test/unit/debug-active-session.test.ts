import { beforeEach, describe, expect, it, vi } from "vitest";

const mockState = vi.hoisted(() => ({
  activeDebugSession: undefined as { id: string; name: string } | undefined,
  terminationListeners: [] as Array<(session: { id: string; name: string }) => void>,
  startListeners: [] as Array<(session: { id: string; name: string }) => void>,
  taskEndListeners: [] as Array<(event: { execution: { task: { name: string } }; exitCode?: number }) => void>,
  tasks: [] as Array<{ name: string }>,
  trackerFactory: undefined as { createDebugAdapterTracker: (session: unknown) => { onDidSendMessage: (message: { type: string; event?: string; body?: { output?: unknown } }) => void } } | undefined,
  resolveStart: undefined as ((started?: boolean) => void) | undefined,
}));

vi.mock("vscode", () => ({
  debug: {
    get activeDebugSession() { return mockState.activeDebugSession; },
    onDidChangeActiveStackItem: () => ({ dispose: vi.fn() }),
    onDidTerminateDebugSession: (listener: (session: { id: string; name: string }) => void) => {
      mockState.terminationListeners.push(listener);
      return { dispose: () => { const index = mockState.terminationListeners.indexOf(listener); if (index >= 0) mockState.terminationListeners.splice(index, 1); } };
    },
    onDidStartDebugSession: (listener: (session: { id: string; name: string }) => void) => {
      mockState.startListeners.push(listener);
      return { dispose: () => { const index = mockState.startListeners.indexOf(listener); if (index >= 0) mockState.startListeners.splice(index, 1); } };
    },
    registerDebugAdapterTrackerFactory: (_filter: string, factory: typeof mockState.trackerFactory) => {
      mockState.trackerFactory = factory ?? undefined;
      return { dispose: vi.fn() };
    },
    stopDebugging: vi.fn(async (session: { id: string; name: string }) => {
      mockState.activeDebugSession = undefined;
      mockState.terminationListeners.slice().forEach((listener) => listener(session));
    }),
    startDebugging: vi.fn(() => new Promise<boolean>((resolve) => {
      mockState.resolveStart = (started = true) => {
        if (started) {
          const session = { id: "session-started", name: "Esi.Web .NET Server" };
          mockState.activeDebugSession = session;
          mockState.startListeners.slice().forEach((listener) => listener(session));
        }
        resolve(started);
      };
    })),
  },
  tasks: {
    fetchTasks: vi.fn(async () => mockState.tasks),
    executeTask: vi.fn(async (task: { name: string }) => {
      queueMicrotask(() => mockState.taskEndListeners.slice().forEach((listener) => listener({ execution: { task }, exitCode: 0 })));
      return { task };
    }),
    onDidEndTaskProcess: (listener: (event: { execution: { task: { name: string } }; exitCode?: number }) => void) => {
      mockState.taskEndListeners.push(listener);
      return { dispose: () => { const index = mockState.taskEndListeners.indexOf(listener); if (index >= 0) mockState.taskEndListeners.splice(index, 1); } };
    },
    onDidEndTask: () => ({ dispose: vi.fn() }),
  },
  workspace: {
    getWorkspaceFolder: () => ({}),
    getConfiguration: () => ({ get: () => undefined }),
  },
  Uri: { file: (filePath: string) => ({ fsPath: filePath }) },
  commands: { executeCommand: vi.fn() },
}));

import { DebugManager } from "../../src/debug/manager.js";

describe("DebugManager active session", () => {
  beforeEach(() => {
    mockState.activeDebugSession = undefined;
    mockState.terminationListeners.length = 0;
    mockState.startListeners.length = 0;
    mockState.taskEndListeners.length = 0;
    mockState.tasks.length = 0;
    mockState.trackerFactory = undefined;
    mockState.resolveStart = undefined;
  });

  it("returns the active session ID", () => {
    mockState.activeDebugSession = { id: "session-123", name: "Esi.Web .NET Server" };

    expect(new DebugManager().getActiveSessionId()).toBe("session-123");
  });

  it("returns null when no session is active", () => {
    expect(new DebugManager().getActiveSessionId()).toBeNull();
  });

  it("forwards DAP output events to readiness listeners", () => {
    const manager = new DebugManager();
    const outputListener = vi.fn();
    manager.onDebugOutput(outputListener);
    const tracker = mockState.trackerFactory?.createDebugAdapterTracker({ id: "session-123", name: "Esi.AI Studio" });

    tracker?.onDidSendMessage({ type: "event", event: "output", body: { output: "Now ready on: http://localhost:7010" } });

    expect(outputListener).toHaveBeenCalledWith(
      { id: "session-123", name: "Esi.AI Studio" },
      "Now ready on: http://localhost:7010",
    );
    manager.dispose();
  });

  it("rejects a start when a debug session is already active", async () => {
    mockState.activeDebugSession = { id: "session-123", name: "Esi.Web .NET Server" };

    await expect(new DebugManager().startDebugging({ workingDirectory: "C:/workspace", configurationName: "Esi.Web .NET Server" })).resolves.toEqual({ started: false, sessionId: null });
  });

  it("waits for the requested session to terminate", async () => {
    const session = { id: "session-123", name: "Esi.Web .NET Server" };
    mockState.activeDebugSession = session;

    await new DebugManager().stopDebugging();

    expect(mockState.activeDebugSession).toBeUndefined();
  });

  it("completes restart after the old session terminates and a new one starts", async () => {
    const oldSession = { id: "session-old", name: "Esi.Web .NET Server", configuration: {}, workspaceFolder: {} };
    const newSession = { id: "session-new", name: "Esi.Web .NET Server" };
    mockState.activeDebugSession = oldSession;
    const vscode = await import("vscode");
    vi.mocked(vscode.debug.startDebugging).mockImplementationOnce(async () => {
      mockState.activeDebugSession = newSession;
      mockState.startListeners.slice().forEach((listener) => listener(newSession));
      return true;
    });

    await expect(new DebugManager().restartDebugging()).resolves.toBe(true);
    expect(vscode.debug.stopDebugging).toHaveBeenCalledWith(oldSession);
    expect(vscode.debug.startDebugging).toHaveBeenCalledWith(oldSession.workspaceFolder, oldSession.configuration);
    expect(mockState.activeDebugSession).toBe(newSession);
  });

  it("rejects restart when VS Code reuses the session ID", async () => {
    const session = { id: "session-reused", name: "Esi.Web .NET Server", configuration: {}, workspaceFolder: {} };
    mockState.activeDebugSession = session;
    const vscode = await import("vscode");
    vi.mocked(vscode.debug.startDebugging).mockImplementationOnce(async () => {
      mockState.activeDebugSession = session;
      mockState.startListeners.slice().forEach((listener) => listener(session));
      return true;
    });

    await expect(new DebugManager().restartDebugging()).rejects.toThrow("recycled debug session ID");
    expect(mockState.activeDebugSession).toBe(session);
  });

  it("runs the named rebuild task between stop and a new session start", async () => {
    const oldSession = { id: "session-old", name: "Esi.Web .NET Server", configuration: {}, workspaceFolder: {} };
    const newSession = { id: "session-new", name: "Esi.Web .NET Server" };
    mockState.activeDebugSession = oldSession;
    mockState.tasks.push({ name: "build" });
    const vscode = await import("vscode");
    vi.mocked(vscode.debug.startDebugging).mockImplementationOnce(async () => {
      mockState.activeDebugSession = newSession;
      mockState.startListeners.slice().forEach((listener) => listener(newSession));
      return true;
    });

    await expect(new DebugManager().restartDebugging("build")).resolves.toBe(true);
    expect(vscode.tasks.executeTask).toHaveBeenCalledWith(mockState.tasks[0]);
    expect(vscode.debug.startDebugging).toHaveBeenCalledWith(oldSession.workspaceFolder, oldSession.configuration);
  });

  it("rejects a second start while the first start is in progress", async () => {
    const manager = new DebugManager();
    const input = { workingDirectory: "C:/workspace", configurationName: "Esi.Web .NET Server" };
    const onAcceptedStart = vi.fn();
    const onStarted = vi.fn();
    const onEnd = vi.fn();
    const firstStart = manager.startDebugging(input, { onAcceptedStart, onStarted, onEnd });

    await Promise.resolve();
    const secondOnAcceptedStart = vi.fn();
    const secondOnEnd = vi.fn();
    await expect(manager.startDebugging(input, { onAcceptedStart: secondOnAcceptedStart, onEnd: secondOnEnd })).resolves.toEqual({ started: false, sessionId: null });
    expect(onAcceptedStart).toHaveBeenCalledOnce();
    expect(secondOnAcceptedStart).not.toHaveBeenCalled();
    expect(secondOnEnd).not.toHaveBeenCalled();

    mockState.resolveStart?.();
    await expect(firstStart).resolves.toEqual({ started: true, sessionId: "session-started" });
    expect(onEnd).toHaveBeenCalledOnce();
    expect(onStarted).toHaveBeenCalledWith({ id: "session-started", name: "Esi.Web .NET Server" });
  });

  it("returns a started session when VS Code uses the runtime terminal name", async () => {
    const vscode = await import("vscode");
    vi.mocked(vscode.debug.startDebugging).mockImplementationOnce(async () => {
      const session = { id: "session-runtime-name", name: "Esi.Web.dll" };
      mockState.activeDebugSession = session;
      mockState.startListeners.slice().forEach((listener) => listener(session));
      return true;
    });

    await expect(new DebugManager().startDebugging({
      workingDirectory: "C:/workspace",
      configurationName: "Esi.Web .NET Server",
    })).resolves.toEqual({ started: true, sessionId: "session-runtime-name" });
  });

  it("runs the lifecycle end callback when an accepted start fails", async () => {
    const manager = new DebugManager();
    const input = { workingDirectory: "C:/workspace", testName: "Esi.Web startup" };
    const onAcceptedStart = vi.fn();
    const onEnd = vi.fn();
    const vscode = await import("vscode");
    vi.mocked(vscode.commands.executeCommand).mockRejectedValueOnce(new Error("startup failed"));
    const start = manager.startDebugging(input, { onAcceptedStart, onEnd });

    await expect(start).rejects.toThrow("startup failed");
    expect(onAcceptedStart).toHaveBeenCalledOnce();
    expect(onEnd).toHaveBeenCalledOnce();
  });
});