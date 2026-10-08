import { beforeEach, describe, expect, it, vi } from "vitest";

const state = vi.hoisted(() => ({
  statusBarItem: undefined as { text: string; tooltip: string; show: ReturnType<typeof vi.fn> } | undefined,
  onSessionsChanged: vi.fn(),
}));

vi.mock("vscode", () => ({
  StatusBarAlignment: { Left: 1 },
  window: {
    createStatusBarItem: vi.fn(() => {
      const statusBarItem = { text: "", tooltip: "", show: vi.fn() };
      state.statusBarItem = statusBarItem;
      return statusBarItem;
    }),
  },
  workspace: {
    name: "Test Workspace",
    workspaceFolders: [{ name: "Folder Fallback" }],
    getConfiguration: vi.fn(() => ({ get: (_name: string, defaultValue: unknown) => defaultValue })),
  },
  lm: { registerMcpServerDefinitionProvider: vi.fn(() => ({ dispose: vi.fn() })) },
}));
vi.mock("../../src/utils/logger.js", () => ({ initLogger: vi.fn(), log: vi.fn(), disposeLogger: vi.fn() }));
vi.mock("../../src/terminal/session-manager.js", () => ({
  SessionManager: class {
    onSessionsChanged = state.onSessionsChanged;
    attachDebugManager = vi.fn();
    dispose = vi.fn();
  },
}));
vi.mock("../../src/debug/manager.js", () => ({ DebugManager: class { dispose = vi.fn(); } }));
vi.mock("../../src/mcp/msaccess-client.js", () => ({ MsAccessClient: class { dispose = vi.fn(); } }));
vi.mock("../../src/mcp-http-server.js", () => ({
  startMcpHttpServer: vi.fn(async () => ({ port: 45678, close: vi.fn() })),
  createConfiguredMcpRequestHandler: vi.fn(),
}));

import { activate } from "../../src/extension.js";

describe("EsiMCP extension status bar", () => {
  beforeEach(() => {
    state.statusBarItem = undefined;
    state.onSessionsChanged.mockClear();
  });

  it("shows the connected workspace name after the local HTTP server is listening", async () => {
    const subscriptions: Array<{ dispose?: () => void }> = [];

    await activate({
      subscriptions,
      extension: { packageJSON: { version: "2.0.19" } },
    } as never);

    expect(state.statusBarItem?.text).toBe("$(plug) EsiMCP: Test Workspace");
    expect(state.statusBarItem?.tooltip).toContain('workspace "Test Workspace" is listening on port 45678');
    expect(state.onSessionsChanged).not.toHaveBeenCalled();
  });
});
