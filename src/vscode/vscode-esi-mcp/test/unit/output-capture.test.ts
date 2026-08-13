import { describe, it, expect } from "vitest";
import {
  createOutputBuffer,
  appendToBuffer,
  readFromBuffer,
  resetReadCursor,
  getBufferLineCount,
} from "../../src/terminal/output-capture.js";

describe("OutputBuffer", () => {
  describe("createOutputBuffer", () => {
    it("should create an empty buffer with default max lines", () => {
      const buffer = createOutputBuffer();
      expect(buffer.lines).toEqual([]);
      expect(buffer.totalLinesReceived).toBe(0);
      expect(buffer.lastReadIndex).toBe(0);
      expect(buffer.maxLines).toBe(10000);
    });

    it("should create a buffer with custom max lines", () => {
      const buffer = createOutputBuffer(100);
      expect(buffer.maxLines).toBe(100);
    });
  });

  describe("appendToBuffer", () => {
    it("should append a single line", () => {
      const buffer = createOutputBuffer();
      appendToBuffer(buffer, "hello");
      expect(buffer.lines).toEqual(["hello"]);
      expect(buffer.totalLinesReceived).toBe(1);
    });

    it("should split multiline text", () => {
      const buffer = createOutputBuffer();
      appendToBuffer(buffer, "line1\nline2\nline3");
      expect(buffer.lines).toEqual(["line1", "line2", "line3"]);
      expect(buffer.totalLinesReceived).toBe(3);
    });

    it("should respect circular buffer limit", () => {
      const buffer = createOutputBuffer(3);
      appendToBuffer(buffer, "a\nb\nc\nd\ne");
      // Only last 3 lines should remain
      expect(buffer.lines.length).toBe(3);
      expect(buffer.lines).toEqual(["c", "d", "e"]);
    });

    it("should track total lines received", () => {
      const buffer = createOutputBuffer(3);
      appendToBuffer(buffer, "a\nb\nc\nd\ne");
      expect(buffer.totalLinesReceived).toBe(5);
    });
  });

  describe("readFromBuffer", () => {
    it("should read all lines with default offset", () => {
      const buffer = createOutputBuffer();
      appendToBuffer(buffer, "line1\nline2\nline3");

      const result = readFromBuffer(buffer);
      expect(result.lines).toEqual(["line1", "line2", "line3"]);
      expect(result.readFrom).toBe(0);
      expect(result.readCount).toBe(3);
      expect(result.remaining).toBe(0);
      expect(result.isComplete).toBe(true);
    });

    it("should support incremental reads", () => {
      const buffer = createOutputBuffer();
      appendToBuffer(buffer, "a\nb\nc\nd\ne");

      // First read: get first 2 lines
      const r1 = readFromBuffer(buffer, 0, 2);
      expect(r1.lines).toEqual(["a", "b"]);
      expect(r1.remaining).toBe(3);

      // Second read: get next lines from cursor
      const r2 = readFromBuffer(buffer, 0, 2);
      expect(r2.lines).toEqual(["c", "d"]);
      expect(r2.remaining).toBe(1);

      // Third read: get remaining
      const r3 = readFromBuffer(buffer, 0, 10);
      expect(r3.lines).toEqual(["e"]);
      expect(r3.isComplete).toBe(true);
    });

    it("should support tail mode with negative offset", () => {
      const buffer = createOutputBuffer();
      appendToBuffer(buffer, "a\nb\nc\nd\ne");

      const result = readFromBuffer(buffer, -3, 500);
      expect(result.lines).toEqual(["c", "d", "e"]);
    });

    it("should support absolute offset", () => {
      const buffer = createOutputBuffer();
      appendToBuffer(buffer, "a\nb\nc\nd\ne");

      const result = readFromBuffer(buffer, 2, 2);
      expect(result.lines).toEqual(["c", "d"]);
      expect(result.readFrom).toBe(2);
    });

    it("should return empty when buffer is empty", () => {
      const buffer = createOutputBuffer();
      const result = readFromBuffer(buffer);
      expect(result.lines).toEqual([]);
      expect(result.isComplete).toBe(true);
    });
  });

  describe("resetReadCursor", () => {
    it("should reset cursor to beginning", () => {
      const buffer = createOutputBuffer();
      appendToBuffer(buffer, "a\nb\nc");
      readFromBuffer(buffer, 0, 2); // advance cursor

      resetReadCursor(buffer);
      expect(buffer.lastReadIndex).toBe(0);

      const result = readFromBuffer(buffer);
      expect(result.lines).toEqual(["a", "b", "c"]);
    });
  });

  describe("getBufferLineCount", () => {
    it("should return current line count", () => {
      const buffer = createOutputBuffer();
      expect(getBufferLineCount(buffer)).toBe(0);

      appendToBuffer(buffer, "a\nb");
      expect(getBufferLineCount(buffer)).toBe(2);
    });
  });
});
