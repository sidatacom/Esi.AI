import { describe, expect, it } from "vitest";
import { resolveModelTokenLimits } from "./modelTokenLimits.js";

describe("resolveModelTokenLimits", () => {
  it("uses backend limits while reserving output space in the model context", () => {
    expect(
      resolveModelTokenLimits({
        context_length: 131072,
        max_input_tokens: 131072,
        max_output_tokens: 65536,
      }),
    ).toEqual({ maxInputTokens: 65536, maxOutputTokens: 65536 });
  });

  it("uses API context length instead of provider-specific input settings", () => {
    expect(resolveModelTokenLimits({ context_length: 8192 })).toEqual({
      maxInputTokens: 4096,
      maxOutputTokens: 4096,
    });
  });

  it("falls back to provider defaults when the API omits token metadata", () => {
    expect(resolveModelTokenLimits({})).toEqual({
      maxInputTokens: 32768,
      maxOutputTokens: 32768,
    });
  });

  it("ignores invalid token metadata", () => {
    expect(
      resolveModelTokenLimits({
        context_length: -1,
        max_input_tokens: "131072",
        max_output_tokens: 0,
      }),
    ).toEqual({ maxInputTokens: 32768, maxOutputTokens: 32768 });
  });
});
