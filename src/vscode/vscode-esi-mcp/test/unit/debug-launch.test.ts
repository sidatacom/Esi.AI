import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const mockState = vi.hoisted(() => ({
  activeDebugSession: undefined as { id: string; name: string } | undefined,
  sessions: [] as Array<Record<string, any>>,
  startListeners: [] as Array<(session: Record<string, any>) => void>,
  terminationListeners: [] as Array<(session: Record<string, any>) => void>,
  workspaceFolder: { uri: { fsPath: "/workspace" } },
  launchJson: "",
  logDirectory: "",
  logNumber: 0,
  debugOutput: [] as string[],
  commandResults: [] as Array<{ exitCode: number; stdout: string; stderr: string }>,
  operations: [] as string[],
  tasks: [] as Array<Record<string, any>>,
  executedTasks: [] as Array<Record<string, any>>,
  taskStartListeners: [] as Array<(event: Record<string, any>) => void>,
  taskEndListeners: [] as Array<(event: Record<string, any>) => void>,
}));

vi.mock("vscode", () => ({
  debug: {
    get activeDebugSession() { return mockState.activeDebugSession; },
    get sessions() { return mockState.sessions; },
    onDidChangeActiveStackItem: () => ({ dispose: vi.fn() }),
    onDidTerminateDebugSession: (listener: (session: Record<string, any>) => void) => {
      mockState.terminationListeners.push(listener);
      return { dispose: () => { const index = mockState.terminationListeners.indexOf(listener); if (index >= 0) mockState.terminationListeners.splice(index, 1); } };
    },
    onDidStartDebugSession: (listener: (session: Record<string, any>) => void) => {
      mockState.startListeners.push(listener);
      return { dispose: () => { const index = mockState.startListeners.indexOf(listener); if (index >= 0) mockState.startListeners.splice(index, 1); } };
    },
    registerDebugAdapterTrackerFactory: () => ({ dispose: vi.fn() }),
    stopDebugging: vi.fn(async (session?: Record<string, any>) => {
      const stoppedSessions = session ? [session] : [...mockState.sessions];
      mockState.operations.push("stop");
      if (session) mockState.sessions = mockState.sessions.filter((candidate) => candidate.id !== session.id);
      else mockState.sessions = [];
      if (!session || mockState.activeDebugSession?.id === session.id) mockState.activeDebugSession = undefined;
      stoppedSessions.forEach((stoppedSession) => mockState.terminationListeners.slice().forEach((listener) => listener(stoppedSession)));
    }),
    startDebugging: vi.fn(async (_folder: unknown, configuration: Record<string, any>) => {
      mockState.operations.push("start");
      const session = { id: "session-new", name: configuration.name, configuration, workspaceFolder: mockState.workspaceFolder };
      mockState.sessions.push(session);
      mockState.activeDebugSession = session;
      mockState.startListeners.slice().forEach((listener) => listener(session));
      return true;
    }),
  },
  tasks: {
    fetchTasks: vi.fn(async () => mockState.tasks),
    registerTaskProvider: vi.fn(() => ({ dispose: vi.fn() })),
    executeTask: vi.fn(async (task: Record<string, any>) => {
      const execution = {
        task,
        terminate: vi.fn(() => {
          mockState.operations.push("task:terminate");
          mockState.taskEndListeners.slice().forEach((listener) => listener({ execution, exitCode: 0 }));
        }),
      };
      mockState.executedTasks.push(execution);
      mockState.operations.push("task:start");
      mockState.taskStartListeners.slice().forEach((listener) => listener({ execution }));
      return execution;
    }),
    onDidStartTask: (listener: (event: Record<string, any>) => void) => {
      mockState.taskStartListeners.push(listener);
      return { dispose: () => { const index = mockState.taskStartListeners.indexOf(listener); if (index >= 0) mockState.taskStartListeners.splice(index, 1); } };
    },
    onDidEndTaskProcess: (listener: (event: Record<string, any>) => void) => {
      mockState.taskEndListeners.push(listener);
      return { dispose: () => { const index = mockState.taskEndListeners.indexOf(listener); if (index >= 0) mockState.taskEndListeners.splice(index, 1); } };
    },
  },
  Task: class {
    constructor(definition: unknown, scope: unknown, name: string, source: string, execution: unknown) {
      Object.assign(this, { definition, scope, name, source, execution });
    }
  },
  ProcessExecution: class {
    constructor(process: string, args: string[], options: unknown) {
      Object.assign(this, { process, args, options });
    }
  },
  TaskRevealKind: { Always: 1 },
  TaskPanelKind: { Dedicated: 2 },
  workspace: {
    workspaceFolders: [mockState.workspaceFolder],
    getWorkspaceFolder: () => mockState.workspaceFolder,
    getConfiguration: () => ({ get: () => undefined }),
    fs: { readFile: vi.fn(async () => Buffer.from(mockState.launchJson, "utf8")) },
  },
  Uri: {
    file: (filePath: string) => ({ fsPath: filePath }),
    joinPath: (_base: unknown, ...parts: string[]) => ({ fsPath: join("/workspace", ...parts) }),
  },
}));

vi.mock("../../src/utils/logger.js", () => ({
  log: vi.fn(),
  logError: vi.fn(),
  beginDebugOutput: (label: string) => mockState.debugOutput.push(`begin:${label}`),
  appendDebugOutput: (chunk: string) => mockState.debugOutput.push(`output:${chunk}`),
  finishDebugOutput: (label: string, exitCode: number) => mockState.debugOutput.push(`finish:${label}:${exitCode}`),
}));
vi.mock("../../src/debug/command-runner.js", () => ({
  createDebugLogPath: (stage: string) => join(mockState.logDirectory, `${stage}-${mockState.logNumber++}.log`),
  runLoggedCommand: vi.fn(async (_command: string, args: string[], _cwd: string, logPath: string, _env: NodeJS.ProcessEnv | undefined, onOutput?: (stream: "stdout" | "stderr", chunk: string) => void) => {
    mockState.operations.push(`run:${args[0]}`);
    const result = mockState.commandResults.shift() ?? { exitCode: 0, stdout: "", stderr: "" };
    if (result.stdout) onOutput?.("stdout", result.stdout);
    if (result.stderr) onOutput?.("stderr", result.stderr);
    await writeFile(logPath, `${result.stdout}${result.stderr}`, "utf8");
    return { ...result, logPath };
  }),
  writeDebugLog: vi.fn(async (logPath: string, message: string) => writeFile(logPath, message, "utf8")),
}));

import * as vscode from "vscode";
import { DebugManager } from "../../src/debug/manager.js";

let temporaryDirectory: string;

beforeEach(async () => {
  temporaryDirectory = await mkdtemp(join(tmpdir(), "esi-mcp-launch-test-"));
  mockState.activeDebugSession = undefined;
  mockState.sessions.length = 0;
  mockState.startListeners.length = 0;
  mockState.terminationListeners.length = 0;
  mockState.launchJson = "";
  mockState.logDirectory = temporaryDirectory;
  mockState.logNumber = 0;
  mockState.debugOutput.length = 0;
  mockState.commandResults.length = 0;
  mockState.operations.length = 0;
  mockState.tasks.length = 0;
  mockState.executedTasks.length = 0;
  mockState.taskStartListeners.length = 0;
  mockState.taskEndListeners.length = 0;
  vi.mocked(vscode.debug.startDebugging).mockClear();
  vi.mocked(vscode.debug.stopDebugging).mockClear();
});

afterEach(async () => {
  await rm(temporaryDirectory, { recursive: true, force: true });
});

describe("DebugManager launch contracts", () => {
  it("stops existing sessions before building and launches the resolved project output", async () => {
    const existingSession = { id: "session-existing", name: "Existing" };
    mockState.sessions.push(existingSession);
    mockState.activeDebugSession = existingSession;
    mockState.commandResults.push(
      { exitCode: 0, stdout: "Build succeeded", stderr: "" },
      { exitCode: 0, stdout: '{"Properties":{"TargetPath":"/workspace/bin/Debug/App.dll"}}', stderr: "" },
    );

    const result = await new DebugManager().launchProject({ projectFile: "App.csproj" });

    expect(mockState.operations).toEqual(["stop", "run:build", "run:msbuild", "start"]);
    expect(mockState.debugOutput).toEqual([
      "begin:Building App.csproj",
      "output:Build succeeded",
      "finish:Building App.csproj:0",
    ]);
    expect(vscode.debug.startDebugging).toHaveBeenCalledWith(mockState.workspaceFolder, expect.objectContaining({
      type: "coreclr",
      request: "launch",
      console: "internalConsole",
      program: "/workspace/bin/Debug/App.dll",
    }));
    expect(result).toMatchObject({ success: true, started: true, sessionId: "session-new" });
  });

  it("launches the project when MSBuild returns TargetPath as plain text", async () => {
    mockState.commandResults.push(
      { exitCode: 0, stdout: "Build succeeded", stderr: "" },
      { exitCode: 0, stdout: "/workspace/bin/Debug/App.dll\n", stderr: "" },
    );

    const result = await new DebugManager().launchProject({ projectFile: "App.csproj" });

    expect(vscode.debug.startDebugging).toHaveBeenCalledWith(mockState.workspaceFolder, expect.objectContaining({
      program: "/workspace/bin/Debug/App.dll",
    }));
    expect(result).toMatchObject({ success: true, started: true, sessionId: "session-new" });
  });

  it("returns a build result code and logfile path without starting the debugger", async () => {
    mockState.commandResults.push({ exitCode: 9, stdout: "", stderr: "compile error" });

    const result = await new DebugManager().launchProject({ projectFile: "App.csproj" });

    expect(result).toMatchObject({ success: false, stage: "build", resultCode: 9, buildLogPath: join(temporaryDirectory, "build-0.log") });
    expect(vscode.debug.startDebugging).not.toHaveBeenCalled();
  });

  it("returns a debug result code and logfile path when VS Code declines the launch", async () => {
    mockState.commandResults.push(
      { exitCode: 0, stdout: "Build succeeded", stderr: "" },
      { exitCode: 0, stdout: '{"Properties":{"TargetPath":"/workspace/bin/Debug/App.dll"}}', stderr: "" },
    );
    vi.mocked(vscode.debug.startDebugging).mockResolvedValueOnce(false);

    const result = await new DebugManager().launchProject({ projectFile: "App.csproj" });

    expect(result).toMatchObject({
      success: false,
      stage: "debug",
      resultCode: 1,
      debugLogPath: join(temporaryDirectory, "debug-2.log"),
      buildLogPath: join(temporaryDirectory, "build-0.log"),
    });
  });

  it("loads launch.json as JSONC, builds its project, and launches the named configuration", async () => {
    mockState.launchJson = `{
      // launch-file comment
      "configurations": [{
        "name": "Esi App",
        "type": "coreclr",
        "request": "launch",
        "program": "${"${workspaceFolder}"}/bin/Debug/App.dll",
        "projectFile": "App.csproj"
      }]
    }`;
    mockState.commandResults.push({ exitCode: 0, stdout: "Build succeeded", stderr: "" });

    const result = await new DebugManager().launchConfiguration({ configurationName: "Esi App" });

    expect(result).toMatchObject({ success: true, started: true, sessionId: "session-new" });
    expect(vscode.debug.startDebugging).toHaveBeenCalledWith(mockState.workspaceFolder, expect.objectContaining({ console: "internalConsole" }));
    expect(vscode.debug.startDebugging).toHaveBeenCalledWith(mockState.workspaceFolder, expect.not.objectContaining({ projectFile: expect.anything() }));
  });

  it("runs a named process preLaunchTask once and returns its captured build output", async () => {
    mockState.launchJson = `{
      "configurations": [{
        "name": "Esi App",
        "type": "coreclr",
        "request": "launch",
        "program": "${"${workspaceFolder}"}/bin/Debug/App.dll",
        "preLaunchTask": "build",
        "console": "integratedTerminal"
      }]
    }`;
    mockState.tasks.push({
      name: "build",
      definition: { type: "process" },
      execution: {
        process: "dotnet",
        args: ["build", "${workspaceFolder}/App.csproj"],
        options: { cwd: "${workspaceFolder}", env: { CONFIGURATION: "Debug" } },
      },
    });
    mockState.commandResults.push({ exitCode: 0, stdout: "Build succeeded", stderr: "" });

    const result = await new DebugManager().launchConfiguration({ configurationName: "Esi App" });

    expect(result).toMatchObject({ success: true, started: true });
    expect(mockState.debugOutput).toEqual([
      "begin:Running build task 'build'",
      "output:Build succeeded",
      "finish:Running build task 'build':0",
    ]);
    expect(vscode.debug.startDebugging).toHaveBeenCalledWith(mockState.workspaceFolder, expect.objectContaining({ console: "integratedTerminal" }));
    expect(mockState.operations).toEqual(["stop", "run:build", "start"]);
    expect(vscode.debug.startDebugging).toHaveBeenCalledWith(mockState.workspaceFolder, expect.not.objectContaining({ preLaunchTask: expect.anything() }));
  });

  it("starts and stops dotnet watch without issuing a full build", async () => {
    const existingSession = { id: "session-existing", name: "Existing" };
    mockState.sessions.push(existingSession);
    mockState.activeDebugSession = existingSession;
    const manager = new DebugManager();

    const started = await manager.hotReload({ mode: "watch", projectFile: "App.csproj", targetFramework: "net10.0" });

    expect(started).toMatchObject({
      success: true,
      started: true,
      mode: "watch",
      projectFile: "/workspace/App.csproj",
      initialBuildOccurs: true,
      hotReloadRequested: true,
      restartOnUnsupportedEdits: true,
    });
    expect(mockState.operations).toEqual(["stop", "task:start"]);
    const task = mockState.executedTasks[0].task as { definition: { type: string }; name: string; execution: { process: string; args: string[]; options: { cwd: string; env: Record<string, string> } } };
    expect(task.name).toBe("EsiMCP dotnet watch: App");
    expect(task.definition.type).toBe("esiMcpDotnetWatch");
    expect(task.execution).toMatchObject({
      process: "dotnet",
      args: ["watch", "--non-interactive", "run", "--project", "/workspace/App.csproj", "--configuration", "Debug", "--framework", "net10.0"],
      options: { cwd: "/workspace", env: { DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER: "1" } },
    });
    expect(task).toHaveProperty("presentationOptions.reveal", 1);
    expect(vscode.debug.startDebugging).not.toHaveBeenCalled();

    const duplicate = await manager.hotReload({ mode: "watch", projectFile: "App.csproj" });
    expect(duplicate).toMatchObject({ success: true, started: false, alreadyRunning: true });
    expect(mockState.executedTasks).toHaveLength(1);

    const stopped = await manager.hotReload({ mode: "stopWatch" });

    expect(stopped).toMatchObject({ success: true, stopped: true, mode: "stopWatch", exitCode: 0 });
    expect(mockState.operations).toEqual(["stop", "task:start", "task:terminate"]);
  });

  it("stops the watch task before a debugger launch builds its project", async () => {
    const manager = new DebugManager();
    await manager.hotReload({ mode: "watch", projectFile: "App.csproj" });
    mockState.commandResults.push(
      { exitCode: 0, stdout: "Build succeeded", stderr: "" },
      { exitCode: 0, stdout: '{"Properties":{"TargetPath":"/workspace/bin/Debug/App.dll"}}', stderr: "" },
    );

    const result = await manager.launchProject({ projectFile: "App.csproj" });

    expect(result).toMatchObject({ success: true, started: true });
    expect(mockState.operations).toEqual(["stop", "task:start", "task:terminate", "stop", "run:build", "run:msbuild", "start"]);
  });
});
