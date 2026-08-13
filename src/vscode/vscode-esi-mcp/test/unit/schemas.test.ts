import { describe, it, expect } from "vitest";
import {
  terminalCreateSchema,
  terminalExecuteSchema,
  terminalReadOutputSchema,
  terminalListSchema,
  terminalCloseSchema,
  terminalSendInputSchema,
  debugStartSchema,
  debugVariableValuesSchema,
  debugEvaluateSchema,
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
  it("rejects unknown debug fields", () => {
    expect(debugStartSchema.safeParse({ workingDirectory: ".", extra: true }).success).toBe(false);
  });

  it("accepts launch, test, and configuration selectors", () => {
    expect(debugStartSchema.safeParse({ workingDirectory: ".", fileFullPath: "src/test.cs", testName: "Test.One", configurationName: "Esi.Web .NET Server" }).success).toBe(true);
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