import { beforeEach, describe, expect, it, vi } from "vitest";

const mockState = vi.hoisted(() => ({
  activeDebugSession: undefined as { id: string; name: string } | undefined,
  terminationListeners: [] as Array<(session: { id: string; name: string }) => void>,
}));

vi.mock("vscode", () => ({
  debug: {
    get activeDebugSession() { return mockState.activeDebugSession; },
    onDidChangeActiveStackItem: () => ({ dispose: vi.fn() }),
    onDidTerminateDebugSession: (listener: (session: { id: string; name: string }) => void) => {
      mockState.terminationListeners.push(listener);
      return { dispose: () => { const index = mockState.terminationListeners.indexOf(listener); if (index >= 0) mockState.terminationListeners.splice(index, 1); } };
    },
    stopDebugging: vi.fn(async (session: { id: string; name: string }) => {
      mockState.activeDebugSession = undefined;
      mockState.terminationListeners.slice().forEach((listener) => listener(session));
    }),
    startDebugging: vi.fn(),
  },
  workspace: {
    getWorkspaceFolder: () => ({}),
  },
  Uri: { file: (filePath: string) => ({ fsPath: filePath }) },
  commands: { executeCommand: vi.fn() },
}));

import { DebugManager } from "../../src/debug/manager.js";

describe("DebugManager active session", () => {
  beforeEach(() => {
    mockState.activeDebugSession = undefined;
    mockState.terminationListeners.length = 0;
  });

  it("returns the active session ID", () => {
    mockState.activeDebugSession = { id: "session-123", name: "Esi.Web .NET Server" };

    expect(new DebugManager().getActiveSessionId()).toBe("session-123");
  });

  it("returns null when no session is active", () => {
    expect(new DebugManager().getActiveSessionId()).toBeNull();
  });

  it("waits for the requested session to terminate", async () => {
    const session = { id: "session-123", name: "Esi.Web .NET Server" };
    mockState.activeDebugSession = session;

    await new DebugManager().stopDebugging();

    expect(mockState.activeDebugSession).toBeUndefined();
  });
});