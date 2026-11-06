const summarizeOpenTag = "<summarize>";
const requestOpenTag = "<request>";
const requestCloseTag = "</request>";
const responseOpenTag = "<response>";
const responseCloseTag = "</response>";
const summarizeCloseTag = "</summarize>";

type Mode = "detecting" | "request" | "response" | "summarize-close" | "plain";

export class VisibleResponseTextFilter {
  private mode: Mode = "detecting";
  private buffered = "";
  private thinkingBuffer = "";
  private inThinkingBlock = false;
  private responseStarted = false;

  public push(value: string): string {
    if (this.mode === "plain") {
      return this.filterThinking(value);
    }

    this.buffered += value;
    return this.process();
  }

  public finish(): string {
    let visible = "";
    if (this.mode === "detecting" || this.mode === "request") {
      visible = this.filterThinking(this.buffered);
    } else if (this.mode === "response") {
      const suffixLength = this.possibleMarkerSuffixLength(
        this.buffered,
        responseCloseTag,
      );
      const response = this.buffered.slice(
        0,
        this.buffered.length - suffixLength,
      );
      visible = this.filterResponse(response, true);
    }

    this.buffered = "";
    return visible + this.finishThinking();
  }

  private process(): string {
    let visible = "";
    while (true) {
      if (this.mode === "detecting") {
        const status = this.tagStatus(this.buffered, summarizeOpenTag);
        if (status === "waiting") {
          return visible;
        }
        if (status === "invalid") {
          this.mode = "plain";
          return visible + this.filterThinking(this.takeBuffered());
        }

        this.mode = "request";
        continue;
      }

      if (this.mode === "request") {
        const afterSummarize = this.afterTag(this.buffered, summarizeOpenTag);
        const requestStatus = this.tagStatus(afterSummarize, requestOpenTag);
        if (requestStatus === "waiting") {
          return visible;
        }
        if (requestStatus === "invalid") {
          this.mode = "plain";
          return visible + this.filterThinking(this.takeBuffered());
        }

        const requestBody = this.afterTag(afterSummarize, requestOpenTag);
        const requestEnd = requestBody.indexOf(requestCloseTag);
        if (requestEnd < 0) {
          return visible;
        }

        const afterRequest = requestBody.slice(
          requestEnd + requestCloseTag.length,
        );
        const responseStatus = this.tagStatus(afterRequest, responseOpenTag);
        if (responseStatus === "waiting") {
          return visible;
        }
        if (responseStatus === "invalid") {
          this.mode = "plain";
          return visible + this.filterThinking(this.takeBuffered());
        }

        this.buffered = this.afterTag(afterRequest, responseOpenTag);
        this.mode = "response";
        continue;
      }

      if (this.mode === "response") {
        const responseEnd = this.buffered.indexOf(responseCloseTag);
        if (responseEnd >= 0) {
          visible += this.filterResponse(
            this.buffered.slice(0, responseEnd),
            true,
          );
          this.buffered = this.buffered.slice(
            responseEnd + responseCloseTag.length,
          );
          this.mode = "summarize-close";
          continue;
        }

        const markerSuffixLength = this.possibleMarkerSuffixLength(
          this.buffered,
          responseCloseTag,
        );
        const safeLength = this.buffered.length - markerSuffixLength;
        const safeText = this.buffered.slice(0, safeLength);
        const trailingWhitespace = safeText.match(/\s*$/)?.[0].length ?? 0;
        const visibleLength = safeText.length - trailingWhitespace;
        visible += this.filterResponse(safeText.slice(0, visibleLength), false);
        this.buffered = this.buffered.slice(visibleLength);
        return visible;
      }

      if (this.mode === "summarize-close") {
        const summarizeEnd = this.buffered.indexOf(summarizeCloseTag);
        if (summarizeEnd >= 0) {
          const trailingText = this.buffered.slice(
            summarizeEnd + summarizeCloseTag.length,
          );
          this.buffered = "";
          this.mode = "plain";
          visible += this.filterThinking(trailingText);
          return visible;
        }

        const suffixLength = this.possibleMarkerSuffixLength(
          this.buffered,
          summarizeCloseTag,
        );
        this.buffered = this.buffered.slice(
          this.buffered.length - suffixLength,
        );
        return visible;
      }

      return visible + this.filterThinking(this.takeBuffered());
    }
  }

  private filterResponse(value: string, isFinal: boolean): string {
    if (!this.responseStarted) {
      value = value.trimStart();
      if (value.length > 0) {
        this.responseStarted = true;
      }
    }
    return this.filterThinking(isFinal ? value.trimEnd() : value);
  }

  private filterThinking(value: string): string {
    this.thinkingBuffer += value;
    let visible = "";

    while (this.thinkingBuffer.length > 0) {
      if (this.inThinkingBlock) {
        const closingIndex = this.thinkingBuffer.indexOf("</think>");
        if (closingIndex < 0) {
          this.thinkingBuffer = this.retainPossibleMarkerSuffix(
            this.thinkingBuffer,
            "</think>",
          );
          break;
        }

        this.thinkingBuffer = this.thinkingBuffer.slice(
          closingIndex + "</think>".length,
        );
        this.inThinkingBlock = false;
        continue;
      }

      const openingIndex = this.thinkingBuffer.indexOf("<think>");
      const closingIndex = this.thinkingBuffer.indexOf("</think>");
      const markerIndex = this.firstMarkerIndex(openingIndex, closingIndex);
      if (markerIndex >= 0) {
        visible += this.thinkingBuffer.slice(0, markerIndex);
        if (openingIndex >= 0 && openingIndex === markerIndex) {
          this.thinkingBuffer = this.thinkingBuffer.slice(
            markerIndex + "<think>".length,
          );
          this.inThinkingBlock = true;
        } else {
          this.thinkingBuffer = this.thinkingBuffer.slice(
            markerIndex + "</think>".length,
          );
        }
        continue;
      }

      const safeLength =
        this.thinkingBuffer.length -
        Math.max(
          this.possibleMarkerSuffixLength(this.thinkingBuffer, "<think>"),
          this.possibleMarkerSuffixLength(this.thinkingBuffer, "</think>"),
        );
      visible += this.thinkingBuffer.slice(0, safeLength);
      this.thinkingBuffer = this.thinkingBuffer.slice(safeLength);
      break;
    }

    return visible;
  }

  private finishThinking(): string {
    const visible = this.inThinkingBlock ? "" : this.thinkingBuffer;
    this.thinkingBuffer = "";
    return visible;
  }

  private tagStatus(
    value: string,
    tag: string,
  ): "waiting" | "matched" | "invalid" {
    const candidate = value.trimStart();
    if (tag.startsWith(candidate)) {
      return "waiting";
    }
    return candidate.startsWith(tag) ? "matched" : "invalid";
  }

  private afterTag(value: string, tag: string): string {
    return value.trimStart().slice(tag.length);
  }

  private takeBuffered(): string {
    const value = this.buffered;
    this.buffered = "";
    return value;
  }

  private retainPossibleMarkerSuffix(value: string, marker: string): string {
    const suffixLength = this.possibleMarkerSuffixLength(value, marker);
    return suffixLength > 0 ? value.slice(-suffixLength) : "";
  }

  private firstMarkerIndex(openingIndex: number, closingIndex: number): number {
    if (openingIndex < 0) {
      return closingIndex;
    }
    if (closingIndex < 0) {
      return openingIndex;
    }
    return Math.min(openingIndex, closingIndex);
  }

  private possibleMarkerSuffixLength(value: string, marker: string): number {
    const maximumLength = Math.min(value.length, marker.length - 1);
    for (let length = maximumLength; length > 0; length -= 1) {
      if (value.endsWith(marker.slice(0, length))) {
        return length;
      }
    }
    return 0;
  }
}
