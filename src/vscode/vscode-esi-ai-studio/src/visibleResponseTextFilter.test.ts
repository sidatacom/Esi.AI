import { describe, expect, it } from "vitest";
import { VisibleResponseTextFilter } from "./visibleResponseTextFilter.js";

describe("VisibleResponseTextFilter", () => {
  it("extracts the response from a streamed summarize envelope", () => {
    const filter = new VisibleResponseTextFilter();
    const chunks = [
      "<sum",
      "marize>\n<request>\nDescribe the screenshot.\n</request>\n<res",
      "ponse>\nA model is selected.",
      "\n</response>\n</summarize>",
    ];

    const visible =
      chunks.map((chunk) => filter.push(chunk)).join("") + filter.finish();

    expect(visible).toBe("A model is selected.");
  });

  it("preserves markup that does not match the summarize envelope", () => {
    const filter = new VisibleResponseTextFilter();
    const output = "<summarize><note>keep this XML</note></summarize>";

    expect(filter.push(output) + filter.finish()).toBe(output);
  });

  it("continues hiding think blocks in ordinary streamed responses", () => {
    const filter = new VisibleResponseTextFilter();

    const visible =
      filter.push("Visible <thi") +
      filter.push("nk>private reasoning</think> text") +
      filter.finish();

    expect(visible).toBe("Visible  text");
  });
});
