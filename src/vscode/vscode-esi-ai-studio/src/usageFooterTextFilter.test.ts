import { describe, expect, it } from "vitest";
import {
  stripUsageFooter,
  stripUsageFooterFromContent,
  UsageFooterTextFilter,
} from "./usageFooterTextFilter.js";

describe("UsageFooterTextFilter", () => {
  it("removes a footer split across streamed chunks", () => {
    const filter = new UsageFooterTextFilter();
    const chunks = [
      "Heute ist Freitag.",
      "\n\n-",
      "--\n*Esi.AI ",
      "Studio: 37692 prompt + 51 completion Tokens | 54.2 Tok/s*",
    ];

    const visible =
      chunks.map((chunk) => filter.push(chunk)).join("") + filter.finish();

    expect(visible).toBe("Heute ist Freitag.");
  });

  it("preserves responses without an Esi.AI Studio footer", () => {
    const filter = new UsageFooterTextFilter();

    expect(filter.push("Antwort ohne Footer") + filter.finish()).toBe(
      "Antwort ohne Footer",
    );
  });
});

describe("stripUsageFooter", () => {
  it("removes a prior provider footer from assistant history", () => {
    expect(
      stripUsageFooter(
        "Hallo!\n\n---\n*Esi.AI Studio: 41199 prompt + 9 completion Tokens | 5737.9 Tok/s*",
      ),
    ).toBe("Hallo!");
  });

  it("removes a footer split across assistant text parts", () => {
    expect(
      stripUsageFooterFromContent([
        { type: "text", text: "Hallo!" },
        { type: "text", text: "\n\n---\n*Esi.AI Studio: old usage" },
      ]),
    ).toEqual([{ type: "text", text: "Hallo!" }]);
  });
});
