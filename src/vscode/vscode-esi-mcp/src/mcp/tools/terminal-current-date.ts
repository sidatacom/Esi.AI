import type { McpToolResponse } from "../../types/index.js";
import { terminalCurrentDateSchema } from "./schemas.js";

export async function handleTerminalCurrentDate(params: unknown): Promise<McpToolResponse> {
  terminalCurrentDateSchema.parse(params);

  const now = new Date();
  const pad = (value: number): string => String(value).padStart(2, "0");

  return {
    content: [{
      type: "text",
      text: JSON.stringify({
        isoUtc: now.toISOString(),
        localDate: `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`,
        localTime: `${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}`,
        timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
        unixMilliseconds: now.getTime(),
      }),
    }],
  };
}
