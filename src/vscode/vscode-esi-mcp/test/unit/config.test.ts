import { describe, expect, it } from "vitest";
import { normalizeBindHosts, normalizePort } from "../../src/config.js";

describe("configuration normalization", () => {
  it("normalizes valid ports and falls back for invalid values", () => {
    expect(normalizePort("4321")).toBe(4321);
    expect(normalizePort(0)).toBe(3002);
    expect(normalizePort(70000)).toBe(3002);
    expect(normalizePort("invalid")).toBe(3002);
  });

  it("keeps only supported loopback bind hosts", () => {
    expect(normalizeBindHosts(["127.0.0.1", " 127.0.0.1 ", "::1"])).toEqual(["127.0.0.1", "::1"]);
    expect(normalizeBindHosts(undefined)).toEqual(["127.0.0.1", "::1"]);
    expect(() => normalizeBindHosts(["0.0.0.0"])).toThrow(/loopback hosts/);
  });
});