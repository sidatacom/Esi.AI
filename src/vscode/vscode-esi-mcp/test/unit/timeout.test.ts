import { describe, it, expect } from "vitest";
import { withTimeout, clampTimeout } from "../../src/security/timeout.js";

describe("timeout utilities", () => {
  describe("withTimeout", () => {
    it("should resolve when operation completes before timeout", async () => {
      const result = await withTimeout(
        Promise.resolve("done"),
        5000,
      );
      expect(result.timedOut).toBe(false);
      expect(result.result).toBe("done");
    });

    it("should timeout when operation takes too long", async () => {
      const slowOp = new Promise<string>((resolve) =>
        setTimeout(() => resolve("done"), 5000),
      );
      const result = await withTimeout(slowOp, 50);
      expect(result.timedOut).toBe(true);
      expect(result.result).toBeUndefined();
    });

    it("should call onTimeout callback", async () => {
      let called = false;
      const slowOp = new Promise<string>((resolve) =>
        setTimeout(() => resolve("done"), 5000),
      );
      await withTimeout(slowOp, 50, () => {
        called = true;
      });
      expect(called).toBe(true);
    });
  });

  describe("clampTimeout", () => {
    it("should return default when undefined", () => {
      expect(clampTimeout(undefined, 30000)).toBe(30000);
    });

    it("should clamp below minimum", () => {
      expect(clampTimeout(500, 30000)).toBe(1000);
    });

    it("should clamp above maximum", () => {
      expect(clampTimeout(999999, 30000)).toBe(300000);
    });

    it("should pass through valid values", () => {
      expect(clampTimeout(15000, 30000)).toBe(15000);
    });

    it("should support custom max", () => {
      expect(clampTimeout(200000, 30000, 120000)).toBe(120000);
    });
  });
});
