import { describe, it, expect } from "vitest";
import {
  terminalCreateSchema,
  terminalExecuteSchema,
  terminalReadOutputSchema,
  terminalListSchema,
  terminalCloseSchema,
  terminalSendInputSchema,
  debugWaitForEventSchema,
  debugVariableValuesSchema,
  debugEvaluateSchema,
  debugLaunchProjectSchema,
  debugLaunchFileSchema,
  debugHotReloadSchema,
} from "../../src/mcp/tools/schemas.js";

describe("Zod Schemas", () => {
  describe("terminalCreateSchema", () => {
    it("should accept valid input with name only", () => {
      const result = terminalCreateSchema.safeParse({ name: "test" });
      expect(result.success).toBe(true);
    });

    it("should accept full input", () => {
      const result = terminalCreateSchema.safeParse({
        name: "test",
        cwd: "/tmp",
        env: { FOO: "bar" },
        shell: "/bin/zsh",
        agentId: "agent-1",
      });
      expect(result.success).toBe(true);
    });

    it("should reject empty name", () => {
      const result = terminalCreateSchema.safeParse({ name: "" });
      expect(result.success).toBe(false);
    });

    it("should reject missing name", () => {
      const result = terminalCreateSchema.safeParse({});
      expect(result.success).toBe(false);
    });
  });

  describe("terminalExecuteSchema", () => {
    it("should accept valid input", () => {
      const result = terminalExecuteSchema.safeParse({
        sessionId: "session-abc",
        command: "ls -la",
      });
      expect(result.success).toBe(true);
      if (result.success) {
        expect(result.data.timeoutMs).toBeUndefined();
        expect(result.data.waitForCompletion).toBe(true);
      }
    });

    it("should enforce timeout bounds", () => {
      const tooLow = terminalExecuteSchema.safeParse({
        sessionId: "s1",
        command: "ls",
        timeoutMs: 100,
      });
      expect(tooLow.success).toBe(false);

      const tooHigh = terminalExecuteSchema.safeParse({
        sessionId: "s1",
        command: "ls",
        timeoutMs: 999999,
      });
      expect(tooHigh.success).toBe(false);
    });

    it("should reject empty command", () => {
      const result = terminalExecuteSchema.safeParse({
        sessionId: "s1",
        command: "",
      });
      expect(result.success).toBe(false);
    });
  });

  describe("terminalReadOutputSchema", () => {
    it("should apply defaults", () => {
      const result = terminalReadOutputSchema.safeParse({
        sessionId: "s1",
      });
      expect(result.success).toBe(true);
      if (result.success) {
        expect(result.data.offset).toBe(0);
        expect(result.data.lines).toBe(500);
      }
    });

    it("should enforce max lines", () => {
      const result = terminalReadOutputSchema.safeParse({
        sessionId: "s1",
        lines: 10000,
      });
      expect(result.success).toBe(false);
    });
  });

  describe("terminalListSchema", () => {
    it("should accept empty input", () => {
      const result = terminalListSchema.safeParse({});
      expect(result.success).toBe(true);
    });

    it("should accept agentId filter", () => {
      const result = terminalListSchema.safeParse({ agentId: "agent-1" });
      expect(result.success).toBe(true);
    });
  });

  describe("terminalCloseSchema", () => {
    it("should require sessionId", () => {
      expect(terminalCloseSchema.safeParse({}).success).toBe(false);
      expect(
        terminalCloseSchema.safeParse({ sessionId: "s1" }).success,
      ).toBe(true);
    });
  });

  describe("terminalSendInputSchema", () => {
    it("should accept valid input", () => {
      const result = terminalSendInputSchema.safeParse({
        sessionId: "s1",
        input: "yes",
      });
      expect(result.success).toBe(true);
      if (result.success) {
        expect(result.data.pressEnter).toBe(true);
      }
    });

    it("should allow empty input (e.g., just pressing Enter)", () => {
      const result = terminalSendInputSchema.safeParse({
        sessionId: "s1",
        input: "",
      });
      expect(result.success).toBe(true);
    });
  });
});

describe("Debug Schemas", () => {
  it("requires a project file and applies the Debug build default", () => {
    expect(debugLaunchProjectSchema.safeParse({}).success).toBe(false);
    expect(debugLaunchProjectSchema.parse({ projectFile: "src/App.csproj" }).configuration).toBe("Debug");
  });

  it("requires a named launch.json configuration", () => {
    expect(debugLaunchFileSchema.safeParse({}).success).toBe(false);
    expect(debugLaunchFileSchema.safeParse({ configurationName: "App" }).success).toBe(true);
  });

  it("requires a project for watch and exactly one launch target for rebuild", () => {
    expect(debugHotReloadSchema.parse({ mode: "stopWatch" }).mode).toBe("stopWatch");
    expect(debugHotReloadSchema.safeParse({}).success).toBe(false);
    expect(debugHotReloadSchema.parse({ mode: "watch", projectFile: "App.csproj" }).mode).toBe("watch");
    expect(debugHotReloadSchema.safeParse({ mode: "watch", configurationName: "App" }).success).toBe(false);
    expect(debugHotReloadSchema.safeParse({ mode: "stopWatch", projectFile: "App.csproj" }).success).toBe(false);
    expect(debugHotReloadSchema.safeParse({ mode: "rebuild" }).success).toBe(false);
    expect(debugHotReloadSchema.safeParse({ mode: "rebuild", projectFile: "App.csproj" }).success).toBe(true);
    expect(debugHotReloadSchema.safeParse({ mode: "rebuild", projectFile: "App.csproj", configurationName: "App" }).success).toBe(false);
  });

  it("accepts filtered debugger event waits and applies the timeout default", () => {
    const result = debugWaitForEventSchema.safeParse({ type: "paused" });
    expect(result.success).toBe(true);
    if (result.success) expect(result.data.timeoutMs).toBe(30000);
  });

  it("bounds requested variables and rejects wildcard dumps", () => {
    expect(debugVariableValuesSchema.safeParse({ variableNames: ["*"] }).success).toBe(false);
    expect(debugVariableValuesSchema.safeParse({ variableNames: Array.from({ length: 21 }, (_, index) => `value${index}`) }).success).toBe(false);
    expect(debugVariableValuesSchema.safeParse({ variableNames: ["result"], scope: "all" }).success).toBe(true);
  });

  it("rejects wildcard expressions", () => {
    expect(debugEvaluateSchema.safeParse({ expression: "*" }).success).toBe(false);
  });

});