import { execFile } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import { randomUUID } from "node:crypto";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";

const MAX_BUFFER_BYTES = 10 * 1024 * 1024;
const COMMAND_TIMEOUT_MS = 10 * 60 * 1000;

export type CommandResult = {
  exitCode: number;
  stdout: string;
  stderr: string;
  logPath: string;
};

type CommandOutputStream = "stdout" | "stderr";
type CommandOutputListener = (stream: CommandOutputStream, chunk: string) => void;

export function createDebugLogPath(stage: "build" | "debug"): string {
  return join(tmpdir(), "esi-mcp", "debug", `${Date.now()}-${randomUUID()}-${stage}.log`);
}

function executeFile(command: string, args: string[], cwd: string, env: NodeJS.ProcessEnv | undefined, onOutput?: CommandOutputListener): Promise<{ stdout: string; stderr: string }> {
  return new Promise((resolve, reject) => {
    const child = execFile(command, args, {
      cwd,
      ...(env ? { env } : {}),
      encoding: "utf8",
      timeout: COMMAND_TIMEOUT_MS,
      maxBuffer: MAX_BUFFER_BYTES,
    }, (error, stdout, stderr) => {
      const result = { stdout: String(stdout), stderr: String(stderr) };
      if (error) {
        Object.assign(error, result);
        reject(error);
        return;
      }
      resolve(result);
    });

    child.stdout?.on("data", (chunk: Buffer | string) => onOutput?.("stdout", String(chunk)));
    child.stderr?.on("data", (chunk: Buffer | string) => onOutput?.("stderr", String(chunk)));
  });
}

export async function runLoggedCommand(command: string, args: string[], cwd: string, logPath: string, env?: NodeJS.ProcessEnv, onOutput?: CommandOutputListener): Promise<CommandResult> {
  await mkdir(dirname(logPath), { recursive: true });
  try {
    const { stdout, stderr } = await executeFile(command, args, cwd, env, onOutput);
    await writeFile(logPath, `${stdout}${stderr ? `\n[stderr]\n${stderr}` : ""}`, "utf8");
    return { exitCode: 0, stdout, stderr, logPath };
  } catch (error) {
    const processError = error as Error & { code?: number | string; stdout?: string; stderr?: string };
    const stdout = processError.stdout ?? "";
    const stderr = processError.stderr ?? "";
    const detail = processError.message ? `\n[error]\n${processError.message}` : "";
    await writeFile(logPath, `${stdout}${stderr ? `\n[stderr]\n${stderr}` : ""}${detail}`, "utf8");
    return {
      exitCode: typeof processError.code === "number" ? processError.code : 1,
      stdout,
      stderr,
      logPath,
    };
  }
}

export async function writeDebugLog(logPath: string, message: string): Promise<void> {
  await mkdir(dirname(logPath), { recursive: true });
  await writeFile(logPath, `${message}\n`, "utf8");
}