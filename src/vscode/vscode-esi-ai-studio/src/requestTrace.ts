export interface ProviderRequestSummary {
  model?: string;
  streaming: boolean;
  messageCount: number;
  messageRoles: string[];
  imageCount: number;
  toolCount: number;
}

export function summarizeOpenAiRequest(value: unknown): ProviderRequestSummary {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return {
      streaming: false,
      messageCount: 0,
      messageRoles: [],
      imageCount: 0,
      toolCount: 0,
    };
  }

  const request = value as Record<string, unknown>;
  const messages = Array.isArray(request.messages) ? request.messages : [];
  const messageRoles: string[] = [];
  let imageCount = 0;

  for (const message of messages) {
    if (
      typeof message !== "object" ||
      message === null ||
      Array.isArray(message)
    ) {
      continue;
    }

    const messageRecord = message as Record<string, unknown>;
    if (typeof messageRecord.role === "string") {
      messageRoles.push(messageRecord.role);
    }

    if (!Array.isArray(messageRecord.content)) {
      continue;
    }

    for (const part of messageRecord.content) {
      if (
        typeof part === "object" &&
        part !== null &&
        !Array.isArray(part) &&
        (part as Record<string, unknown>).type === "image_url"
      ) {
        imageCount++;
      }
    }
  }

  return {
    ...(typeof request.model === "string" ? { model: request.model } : {}),
    streaming: request.stream === true,
    messageCount: messages.length,
    messageRoles,
    imageCount,
    toolCount: Array.isArray(request.tools) ? request.tools.length : 0,
  };
}
