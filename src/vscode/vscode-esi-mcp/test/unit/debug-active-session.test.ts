import { beforeEach, describe, expect, it, vi } from "vitest";

const mockState = vi.hoisted(() => ({
  activeDebugSession: undefined as { id: string; name: string } | undefined,
}));

vi.mock("vscode", () => ({
  debug: {
    get activeDebugSession() { return mockState.activeDebugSession; },
    onDidChangeActiveStackItem: () => ({ dispose: vi.fn() }),
    onDidTerminateDebugSession: () => ({ dispose: vi.fn() }),
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
  });

  it("returns the active session ID", () => {
    mockState.activeDebugSession = { id: "session-123", name: "Esi.Web .NET Server" };

    expect(new DebugManager().getActiveSessionId()).toBe("session-123");
  });

  it("returns null when no session is active", () => {
    expect(new DebugManager().getActiveSessionId()).toBeNull();
  });
});