import type { OutputBuffer } from "../types/index.js";

const DEFAULT_MAX_LINES = 10000;

export function createOutputBuffer(maxLines?: number): OutputBuffer {
  return {
    lines: [],
    totalLinesReceived: 0,
    lastReadIndex: 0,
    maxLines: maxLines ?? DEFAULT_MAX_LINES,
  };
}

/**
 * Append raw text to the output buffer.
 * Splits by newlines and maintains circular buffer semantics.
 */
export function appendToBuffer(buffer: OutputBuffer, data: string): void {
  const newLines = data.split("\n");

  for (const line of newLines) {
    if (buffer.lines.length >= buffer.maxLines) {
      // Circular: shift oldest line out
      buffer.lines.shift();
      // Adjust lastReadIndex if it was pointing to removed lines
      if (buffer.lastReadIndex > 0) {
        buffer.lastReadIndex--;
      }
    }
    buffer.lines.push(line);
    buffer.totalLinesReceived++;
  }
}

/**
 * Read lines from the buffer starting from a given offset.
 *
 * @param offset - 0 means from lastReadIndex (incremental), negative means tail
 * @param maxLines - Maximum number of lines to return
 * @returns Object with the read lines and metadata
 */
export function readFromBuffer(
  buffer: OutputBuffer,
  offset: number = 0,
  maxLines: number = 500,
): {
  lines: string[];
  readFrom: number;
  readCount: number;
  remaining: number;
  totalLines: number;
  isComplete: boolean;
} {
  const totalLines = buffer.lines.length;

  let startIndex: number;
  if (offset < 0) {
    // Tail mode: read last N lines
    startIndex = Math.max(0, totalLines + offset);
  } else if (offset === 0) {
    // Incremental mode: read from last cursor
    startIndex = buffer.lastReadIndex;
  } else {
    // Absolute offset
    startIndex = Math.min(offset, totalLines);
  }

  const endIndex = Math.min(startIndex + maxLines, totalLines);
  const lines = buffer.lines.slice(startIndex, endIndex);

  // Update read cursor
  buffer.lastReadIndex = endIndex;

  const remaining = totalLines - endIndex;

  return {
    lines,
    readFrom: startIndex,
    readCount: lines.length,
    remaining,
    totalLines,
    isComplete: remaining === 0,
  };
}

/**
 * Reset the read cursor to the beginning.
 */
export function resetReadCursor(buffer: OutputBuffer): void {
  buffer.lastReadIndex = 0;
}

/**
 * Get the total number of lines currently in the buffer.
 */
export function getBufferLineCount(buffer: OutputBuffer): number {
  return buffer.lines.length;
}
