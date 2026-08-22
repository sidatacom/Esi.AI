import { beforeEach, describe, expect, it, vi } from "vitest";

const mockState = vi.hoisted(() => ({
  activeDebugSession: undefined as { id: string; name: string } | undefined,
  terminationListeners: [] as Array<(session: { id: string; name: string }) => void>,
  startListeners: [] as Array<(session: { id: string; name: string }) => void>,
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
    mockState.resolveStart = undefined;
  });

  it("returns the active session ID", () => {
    mockState.activeDebugSession = { id: "session-123", name: "Esi.Web .NET Server" };

    expect(new DebugManager().getActiveSessionId()).toBe("session-123");
  });

  it("returns null when no session is active", () => {
    expect(new DebugManager().getActiveSessionId()).toBeNull();
  });

  it("rejects a start when a debug session is already active", async () => {
    mockState.activeDebugSession = { id: "session-123", name: "Esi.Web .NET Server" };

    await expect(new DebugManager().startDebugging({ workingDirectory: "C:/workspace", configurationName: "Esi.Web .NET Server" })).resolves.toBe(false);
  });

  it("waits for the requested session to terminate", async () => {
    const session = { id: "session-123", name: "Esi.Web .NET Server" };
    mockState.activeDebugSession = session;

    await new DebugManager().stopDebugging();

    expect(mockState.activeDebugSession).toBeUndefined();
  });

  it("rejects a second start while the first start is in progress", async () => {
    const manager = new DebugManager();
    const input = { workingDirectory: "C:/workspace", configurationName: "Esi.Web .NET Server" };
    const onAcceptedStart = vi.fn();
    const onEnd = vi.fn();
    const firstStart = manager.startDebugging(input, { onAcceptedStart, onEnd });

    await Promise.resolve();
    const secondOnAcceptedStart = vi.fn();
    const secondOnEnd = vi.fn();
    await expect(manager.startDebugging(input, { onAcceptedStart: secondOnAcceptedStart, onEnd: secondOnEnd })).resolves.toBe(false);
    expect(onAcceptedStart).toHaveBeenCalledOnce();
    expect(secondOnAcceptedStart).not.toHaveBeenCalled();
    expect(secondOnEnd).not.toHaveBeenCalled();

    mockState.resolveStart?.();
    await expect(firstStart).resolves.toBe(true);
    expect(onEnd).toHaveBeenCalledOnce();
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