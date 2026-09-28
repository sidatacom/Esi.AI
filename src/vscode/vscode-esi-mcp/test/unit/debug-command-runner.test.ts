import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import { runLoggedCommand } from "../../src/debug/command-runner.js";

const temporaryDirectories: string[] = [];

afterEach(async () => {
  await Promise.all(temporaryDirectories.splice(0).map((directory) => rm(directory, { recursive: true, force: true })));
});

describe("runLoggedCommand", () => {
  it("captures successful stdout and stderr in the requested log file", async () => {
    const directory = await mkdtemp(join(tmpdir(), "esi-mcp-debug-test-"));
    temporaryDirectories.push(directory);
    const logPath = join(directory, "build.log");
    const streamed: Record<"stdout" | "stderr", string[]> = { stdout: [], stderr: [] };

    const result = await runLoggedCommand(process.execPath, ["-e", "process.stdout.write('built'); process.stderr.write('warning');"], process.cwd(), logPath, undefined, (stream, chunk) => streamed[stream].push(chunk));

    expect(result.exitCode).toBe(0);
    expect(result.stdout).toBe("built");
    expect(result.stderr).toBe("warning");
    expect(streamed.stdout.join("")).toBe("built");
    expect(streamed.stderr.join("")).toBe("warning");
    expect(await readFile(logPath, "utf8")).toContain("built\n[stderr]\nwarning");
  });

  it("returns the command exit code and keeps failure output in the log", async () => {
    const directory = await mkdtemp(join(tmpdir(), "esi-mcp-debug-test-"));
    temporaryDirectories.push(directory);
    const logPath = join(directory, "build.log");

    const result = await runLoggedCommand(process.execPath, ["-e", "process.stderr.write('build failed'); process.exit(7);"], process.cwd(), logPath);

    expect(result.exitCode).toBe(7);
    expect(await readFile(logPath, "utf8")).toContain("build failed");
  });
});