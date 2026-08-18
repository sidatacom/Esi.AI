import { describe, expect, it, vi } from "vitest";

vi.mock("../../src/utils/logger.js", () => ({ log: vi.fn(), logError: vi.fn() }));
import { createMcpRequestHandler } from "../../src/mcp/server.js";
import type { SessionManager } from "../../src/terminal/session-manager.js";
import type { DebugManager } from "../../src/debug/manager.js";

describe("EsiMCP tool catalog", () => {
  it("exposes namespaced Terminal and Debug tools", async () => {
    const handler = createMcpRequestHandler({} as SessionManager, {} as DebugManager);
    const result = await handler("tools/list") as { tools: Array<{ name: string; description: string }> };
    const names = result.tools.map((tool) => tool.name);

    expect(names).toEqual([
      "terminal_run",
      "terminal_create",
      "terminal_exec",
      "terminal_read",
      "terminal_list",
      "terminal_close",
      "terminal_input",
      "debug_active_session",
      "debug_settings",
      "debug_start",
      "debug_check_host_readyness",
      "debug_wait_for_event",
      "debug_stop",
      "debug_step_over",
      "debug_step_into",
      "debug_step_out",
      "debug_continue",
      "debug_pause",
      "debug_restart",
      "debug_add_breakpoint",
      "debug_add_logpoint",
      "debug_remove_breakpoint",
      "debug_clear_all_breakpoints",
      "debug_list_breakpoints",
      "debug_list_variable_names",
      "debug_get_variables_values",
      "debug_evaluate_expression",
    ]);
    expect(result.tools.every((tool) => tool.description.startsWith("EsiMCP "))).toBe(true);
  });
});