import { beforeEach, describe, expect, it, vi } from "vitest";

class MockTerminal {
  readonly name: string;
  readonly creationOptions: { cwd?: string };
  dispose = vi.fn();
  sendText = vi.fn();
  show = vi.fn();

  constructor(name: string, cwd?: string) {
    this.name = name;
    this.creationOptions = { cwd };
  }
}

const mockState = vi.hoisted(() => ({
  terminals: [] as Array<MockTerminal>,
  includeAllTerminals: false,
  onDidCloseTerminal: vi.fn(() => ({ dispose: vi.fn() })),
  onDidStartTerminalShellExecution: vi.fn(() => ({ dispose: vi.fn() })),
  onDidEndTerminalShellExecution: vi.fn(() => ({ dispose: vi.fn() })),
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
          : defaultValue,
    }),
  },
}));

import { SessionManager } from "../../src/terminal/session-manager.js";

describe("SessionManager terminal recovery", () => {
  beforeEach(() => {
    mockState.terminals.length = 0;
    mockState.includeAllTerminals = false;
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
});