import { describe, expect, it } from "vitest";
import { summarizeOpenAiRequest } from "./requestTrace.js";

describe("summarizeOpenAiRequest", () => {
  it("keeps request metadata while excluding message and tool contents", () => {
    const summary = summarizeOpenAiRequest({
      model: "vision-model",
      stream: true,
      messages: [
        {
          role: "user",
          content: [
            { type: "text", text: "private prompt" },
            { type: "image_url", image_url: { url: "private image" } },
          ],
        },
        { role: "assistant", content: "private response" },
      ],
      tools: [
        {
          type: "function",
          function: {
            name: "lookup",
            description: "private description",
            arguments: "private arguments",
          },
        },
      ],
    });

    expect(summary).toEqual({
      model: "vision-model",
      streaming: true,
      messageCount: 2,
      messageRoles: ["user", "assistant"],
      imageCount: 1,
      toolCount: 1,
    });
    const serialized = JSON.stringify(summary);
    expect(serialized).not.toContain("private");
    expect(serialized).not.toContain("image_url");
  });

  it("returns an empty summary for non-object input", () => {
    expect(summarizeOpenAiRequest(null)).toEqual({
      streaming: false,
      messageCount: 0,
      messageRoles: [],
      imageCount: 0,
      toolCount: 0,
    });
  });
});
