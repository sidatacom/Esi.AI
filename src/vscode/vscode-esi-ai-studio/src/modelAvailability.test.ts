import { describe, expect, it } from "vitest";
import { isModelAvailable } from "./modelAvailability.js";

describe("isModelAvailable", () => {
  it("includes loaded models even when auto-launch is disabled", () => {
    expect(isModelAvailable({ loaded: true, autoLaunch: false })).toBe(true);
  });

  it("includes unloaded models when auto-launch is enabled", () => {
    expect(isModelAvailable({ loaded: false, autoLaunch: true })).toBe(true);
  });

  it("excludes unloaded models when auto-launch is disabled", () => {
    expect(isModelAvailable({ loaded: false, autoLaunch: false })).toBe(false);
  });
});
