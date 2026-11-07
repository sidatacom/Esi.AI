interface MessageContentPart {
  type: "image_url" | "text";
  text?: string;
  image_url?: { url: string };
}

type MessageContent = string | MessageContentPart[] | null;

const usageFooterMarker = "\n\n---\n*Esi.AI Studio:";

export class UsageFooterTextFilter {
  private buffered = "";
  private footerFound = false;

  public push(value: string): string {
    if (this.footerFound) {
      return "";
    }

    this.buffered += value;
    const footerIndex = this.buffered.indexOf(usageFooterMarker);
    if (footerIndex >= 0) {
      const visible = this.buffered.slice(0, footerIndex);
      this.buffered = "";
      this.footerFound = true;
      return visible;
    }

    const suffixLength = this.possibleMarkerSuffixLength(this.buffered);
    const visible = this.buffered.slice(0, this.buffered.length - suffixLength);
    this.buffered = this.buffered.slice(visible.length);
    return visible;
  }

  public finish(): string {
    const visible = this.footerFound ? "" : this.buffered;
    this.buffered = "";
    return visible;
  }

  private possibleMarkerSuffixLength(value: string): number {
    const maximumLength = Math.min(value.length, usageFooterMarker.length - 1);
    for (let length = maximumLength; length > 0; length -= 1) {
      if (value.endsWith(usageFooterMarker.slice(0, length))) {
        return length;
      }
    }
    return 0;
  }
}

export function stripUsageFooter(value: string): string {
  const footerIndex = value.indexOf(usageFooterMarker);
  return footerIndex >= 0 ? value.slice(0, footerIndex) : value;
}

export function stripUsageFooterFromContent(
  content: MessageContent,
): MessageContent {
  if (typeof content === "string") {
    return stripUsageFooter(content);
  }
  if (!Array.isArray(content)) {
    return content;
  }

  let footerFound = false;
  const filteredContent = content.flatMap((part) => {
    if (footerFound) {
      return [];
    }
    if (part.type !== "text" || typeof part.text !== "string") {
      return [part];
    }

    const text = stripUsageFooter(part.text);
    footerFound = text.length !== part.text.length;
    return text.length > 0 ? [{ ...part, text }] : [];
  });

  return filteredContent.length > 0 ? filteredContent : null;
}
