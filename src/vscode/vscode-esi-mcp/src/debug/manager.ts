import * as vscode from "vscode";
import { appendFile, unlink } from "node:fs/promises";
import { basename, dirname, isAbsolute, relative, resolve, sep } from "node:path";
import { createDebugLogPath, runLoggedCommand, writeDebugLog, type CommandResult } from "./command-runner.js";
import { parse as parseJsonc } from "jsonc-parser/lib/esm/main.js";
import { appendDebugOutput, beginDebugOutput, finishDebugOutput, log } from "../utils/logger.js";

const MAX_VARIABLES = 100;
const SECRET_NAME = /(password|passwd|secret|token|api[_-]?key|connectionstring|authorization|credential|private[_-]?key)/i;
const SECRET_VALUE = /(bearer\s+[A-Za-z0-9._~+/=-]+|(?:api[_-]?key|password|secret|token)\s*[:=]\s*\S+|eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+)/i;

type Scope = "local" | "global" | "all";
type DapVariable = { name: string; value?: string; evaluateName?: string; variablesReference?: number };
type DapResponse = { scopes?: Array<{ name?: string; variablesReference?: number }>; variables?: DapVariable[]; threads?: Array<{ id: number }>; stackFrames?: Array<{ id: number }>; result?: unknown; value?: unknown; body?: { exceptionId?: string; description?: string; breakMode?: string } };
type DapBreakpoint = { id?: number; verified?: boolean; line?: number; column?: number; message?: string };
type DebugStackItemDetails = { source?: { uri?: vscode.Uri }; range?: { start?: { line?: number } } };
export type DebugEvent = {
  id: number;
  type: "paused" | "continued" | "terminated";
  sessionId: string;
  sessionName: string;
  timestamp: string;
  reason?: "exception" | "breakpoint" | "pause" | "unknown";
  exceptionType?: string;
  exceptionMessage?: string;
  fileFullPath?: string;
  line?: number;
};
type DebugEventListener = (event: DebugEvent) => void;
type DebugOutputListener = (session: vscode.DebugSession, output: string) => void;
type BreakpointStatus = {
  fileFullPath: string;
  line: number;
  condition?: string;
  logMessage?: string;
  sessionId: string | null;
  bound: boolean;
  verified: boolean | null;
  adapterLine: number | null;
  message: string | null;
};

export class DebugManager {
  private readonly debugDisposables: vscode.Disposable[] = [];
  private readonly debugAdapterTrackers = new Map<string, vscode.DebugAdapterTracker>();
  private readonly debugSessions = new Map<string, vscode.DebugSession>();
  private readonly debugSessionStartListeners = new Set<(session: vscode.DebugSession) => void>();
  private readonly debugEvents: DebugEvent[] = [];
  private readonly eventWaiters: Array<{ type?: DebugEvent["type"]; sessionId?: string; afterId: number; consume: boolean; resolve: (event: DebugEvent | null) => void; timer: ReturnType<typeof setTimeout> }> = [];
  private readonly eventListeners = new Set<DebugEventListener>();
  private readonly debugOutputListeners = new Set<DebugOutputListener>();
  private readonly debugSessionPaused = new Map<string, boolean>();
  private watchExecution: vscode.TaskExecution | undefined;
  private watchTask: vscode.Task | undefined;
  private watchProjectFile: string | undefined;
  private watchExitCode: number | undefined;
  private watchStartInProgress = false;
  private readonly watchEndWaiters = new Map<vscode.TaskExecution, Set<(result: { completed: boolean; exitCode?: number }) => void>>();
  private nextEventId = 1;
  private pausedStateKey: string | null = null;
  private debugStartInProgress = false;

  constructor() {
    this.debugDisposables.push(vscode.tasks.registerTaskProvider("esiMcpDotnetWatch", {
      provideTasks: () => this.watchTask ? [this.watchTask] : [],
      resolveTask: () => this.watchTask,
    }));
    this.debugDisposables.push(vscode.debug.onDidChangeActiveStackItem(() => { void this.observeDebugState(); }));
    this.debugDisposables.push(vscode.debug.onDidTerminateDebugSession((session) => {
      this.finishDebugSession(session);
    }));
    this.debugDisposables.push(vscode.tasks.onDidEndTaskProcess(({ execution, exitCode }) => {
      if (execution !== this.watchExecution && execution.task !== this.watchTask) return;
      this.watchExitCode = exitCode;
      this.watchExecution = undefined;
      this.watchTask = undefined;
      this.watchProjectFile = undefined;
      log(`dotnet watch task ended: exitCode=${exitCode ?? "unknown"}`);
      for (const waiter of this.watchEndWaiters.get(execution) ?? []) waiter({ completed: true, exitCode });
      this.watchEndWaiters.delete(execution);
    }));
    this.debugDisposables.push(vscode.debug.registerDebugAdapterTrackerFactory("*", {
      createDebugAdapterTracker: (session) => {
        log(`Debug adapter tracker attached: session=${session.id}, name=${session.name}`);
        const tracker: vscode.DebugAdapterTracker = {
          onDidSendMessage: (message) => {
            if (message.type !== "event") return;
            if (message.event === "terminated") {
              this.finishDebugSession(session);
              return;
            }
            if (message.event === "continued") {
              this.debugSessionPaused.set(session.id, false);
              if (this.pausedStateKey?.startsWith(`${session.id}:`)) this.pausedStateKey = null;
              this.publishDebugEvent({ type: "continued", sessionId: session.id, sessionName: session.name, timestamp: new Date().toISOString() });
              return;
            }
            if (message.event === "stopped") {
              const body = message.body as { reason?: string; description?: string } | undefined;
              this.debugSessionPaused.set(session.id, true);
              const reason = body?.reason === "exception" || body?.reason === "breakpoint" || body?.reason === "pause" ? body.reason : "unknown";
              this.publishDebugEvent({
                type: "paused",
                sessionId: session.id,
                sessionName: session.name,
                timestamp: new Date().toISOString(),
                reason,
                ...(body?.reason === "exception" && body.description ? { exceptionMessage: this.redact(body.description) as string } : {}),
              });
              return;
            }
            if (message.event !== "output") return;
            const output = message.body?.output;
            if (typeof output !== "string") return;
            for (const listener of this.debugOutputListeners) listener(session, output);
          },
          onWillStopSession: () => this.finishDebugSession(session),
          onExit: () => this.finishDebugSession(session),
        };
        this.debugSessions.set(session.id, session);
        this.debugAdapterTrackers.set(session.id, tracker);
        for (const listener of this.debugSessionStartListeners) listener(session);
        return tracker;
      },
    }));
  }

  dispose(): void {
    this.watchExecution?.terminate();
    this.watchExecution = undefined;
    this.watchTask = undefined;
    this.watchProjectFile = undefined;
    this.watchEndWaiters.clear();
    this.debugDisposables.forEach((disposable) => disposable.dispose());
    this.debugEvents.length = 0;
    for (const waiter of this.eventWaiters.splice(0)) {
      clearTimeout(waiter.timer);
      waiter.resolve(null);
    }
    this.eventListeners.clear();
    this.debugOutputListeners.clear();
    this.debugSessions.clear();
    this.debugAdapterTrackers.clear();
  }

  onDebugEvent(listener: DebugEventListener): vscode.Disposable {
    this.eventListeners.add(listener);
    return { dispose: () => this.eventListeners.delete(listener) };
  }

  onDebugOutput(listener: DebugOutputListener): vscode.Disposable {
    this.debugOutputListeners.add(listener);
    return { dispose: () => this.debugOutputListeners.delete(listener) };
  }

  async waitForDebugEvent(timeoutMs: number, type?: DebugEvent["type"]): Promise<DebugEvent | null> {
    await this.observeDebugState();
    return this.waitForDebugEventAfter(timeoutMs, type, undefined, 0);
  }

  private async waitForDebugEventAfter(timeoutMs: number, type: DebugEvent["type"] | undefined, sessionId: string | undefined, afterId: number, consume = true): Promise<DebugEvent | null> {
    const matches = (event: DebugEvent) => (!type || event.type === type)
      && (!sessionId || event.sessionId === sessionId)
      && event.id > afterId;
    const queuedIndex = this.debugEvents.findIndex(matches);
    if (queuedIndex >= 0) return consume ? this.debugEvents.splice(queuedIndex, 1)[0] : this.debugEvents[queuedIndex];

    return new Promise((resolve) => {
      const timer = setTimeout(() => {
        const index = this.eventWaiters.findIndex((waiter) => waiter.timer === timer);
        if (index >= 0) this.eventWaiters.splice(index, 1);
        resolve(null);
      }, timeoutMs);
      this.eventWaiters.push({ type, sessionId, afterId, consume, resolve, timer });
    });
  }

  getActiveSessionId(): string | null {
    return this.readActiveSession()?.id ?? null;
  }

  hasDebugAdapterTracker(sessionId: string): boolean {
    return this.debugAdapterTrackers.has(sessionId);
  }

  getSetting(setting: string): { setting: string; value: unknown } {
    const separator = setting.lastIndexOf(".");
    const section = setting.slice(0, separator);
    const key = setting.slice(separator + 1);
    const value = vscode.workspace.getConfiguration(section).get<unknown>(key);
    return { setting, value: this.redact(value, setting) };
  }

  async startDebugging(input: {
    workspaceFolder?: string;
    configuration?: vscode.DebugConfiguration;
    configurationName?: string;
  }): Promise<{ started: boolean; sessionId: string | null; session?: vscode.DebugSession }> {
    if (this.watchStartInProgress) return { started: false, sessionId: null };
    if (this.watchExecution) await this.stopWatch();
    if (this.debugStartInProgress || this.readActiveSession()) return { started: false, sessionId: null };
    const configuration = input.configuration ?? input.configurationName;
    if (!configuration) throw new Error("A VS Code debug configuration or configurationName is required");
    const configurationName = typeof configuration === "string" ? configuration : configuration.name;
    if (!configurationName) throw new Error("The VS Code debug configuration must have a name");
    const workspaceFolder = this.resolveWorkspaceFolder(input.workspaceFolder);

    this.debugStartInProgress = true;
    const startedSession = this.waitForStartedSession(configurationName);
    try {
      const started = await vscode.debug.startDebugging(workspaceFolder, configuration);
      if (!started) {
        startedSession.cancel();
        return { started: false, sessionId: null };
      }
      const session = await startedSession.promise;
      return { started: true, sessionId: session.id, session };
    } catch (error) {
      startedSession.cancel();
      throw error;
    } finally {
      this.debugStartInProgress = false;
    }
  }

  async launchProject(input: {
    projectFile: string;
    workspaceFolder?: string;
    targetFramework?: string;
    configuration?: string;
  }): Promise<Record<string, unknown>> {
    const folder = this.resolveWorkspaceFolder(input.workspaceFolder);
    if (!folder) throw new Error("A workspace folder is required to launch a project");
    const projectFile = this.resolveProjectFile(input.projectFile, folder.uri.fsPath);

    await this.stopWatch();
    await this.stopAllDebugging();
    const buildLogPath = createDebugLogPath("build");
    const buildArguments = ["build", projectFile, "--configuration", input.configuration ?? "Debug", "--nologo"];
    if (input.targetFramework) buildArguments.push("--framework", input.targetFramework);
    const build = await this.runLaunchBuild(`Building ${basename(projectFile)}`, "dotnet", buildArguments, dirname(projectFile), buildLogPath);
    log(`Project build completed: project=${projectFile}, exitCode=${build.exitCode}, log=${build.logPath}`);
    if (build.exitCode !== 0) {
      return { success: false, stage: "build", resultCode: build.exitCode, buildLogPath: build.logPath };
    }

    const targetArguments = [
      "msbuild", projectFile, "-nologo", "-getProperty:TargetPath",
      `-property:Configuration=${input.configuration ?? "Debug"}`,
    ];
    if (input.targetFramework) targetArguments.push(`-property:TargetFramework=${input.targetFramework}`);
    const target = await runLoggedCommand("dotnet", targetArguments, dirname(projectFile), createDebugLogPath("build"));
    await appendFile(build.logPath, `\n[TargetPath lookup]\n${target.stdout}${target.stderr ? `\n[stderr]\n${target.stderr}` : ""}`, "utf8");
    await unlink(target.logPath).catch(() => undefined);
    if (target.exitCode !== 0) {
      return { success: false, stage: "build", resultCode: target.exitCode, buildLogPath: build.logPath };
    }

    let targetPath: string;
    try {
      const jsonStart = target.stdout.indexOf("{");
      const resolvedTargetPath = jsonStart >= 0
        ? (JSON.parse(target.stdout.slice(jsonStart)) as { Properties?: { TargetPath?: string } }).Properties?.TargetPath
        : target.stdout.trim().split(/\r?\n/).filter(Boolean).at(-1)?.trim();
      if (!resolvedTargetPath) throw new Error("MSBuild returned an empty TargetPath property; specify targetFramework for multi-target projects");
      targetPath = isAbsolute(resolvedTargetPath) ? resolvedTargetPath : resolve(dirname(projectFile), resolvedTargetPath);
    } catch (error) {
      await appendFile(build.logPath, `\n[TargetPath error]\n${error instanceof Error ? error.message : String(error)}\n`, "utf8");
      return { success: false, stage: "build", resultCode: 1, buildLogPath: build.logPath };
    }

    const debugLogPath = createDebugLogPath("debug");
    const debugConfiguration: vscode.DebugConfiguration = {
      name: `EsiMCP: ${basename(projectFile, ".csproj")}`,
      type: "coreclr",
      request: "launch",
      console: "internalConsole",
      program: targetPath,
      cwd: dirname(projectFile),
    };
    try {
      const result = await this.startDebugging({ workspaceFolder: folder?.uri.fsPath, configuration: debugConfiguration });
      if (!result.started) {
        await writeDebugLog(debugLogPath, "VS Code declined to start the coreclr debug configuration");
        return { success: false, stage: "debug", resultCode: 1, debugLogPath, buildLogPath: build.logPath };
      }
      return { success: true, started: true, stage: "debug", sessionId: result.sessionId, session: result.session, buildLogPath: build.logPath };
    } catch (error) {
      await writeDebugLog(debugLogPath, error instanceof Error ? error.stack ?? error.message : String(error));
      return { success: false, stage: "debug", resultCode: 1, debugLogPath, buildLogPath: build.logPath };
    }
  }

  async launchConfiguration(input: { configurationName: string; workspaceFolder?: string; projectFile?: string; targetFramework?: string; configuration?: string }): Promise<Record<string, unknown> & { started: boolean; session?: vscode.DebugSession }> {
    const folder = this.resolveWorkspaceFolder(input.workspaceFolder);
    if (!folder) throw new Error("A workspace folder is required to read launch.json");
    await this.stopWatch();
    const launchFile = vscode.Uri.joinPath(folder.uri, ".vscode", "launch.json");
    const source = Buffer.from(await vscode.workspace.fs.readFile(launchFile)).toString("utf8");
    const launchJson = parseJsonc(source) as { configurations?: vscode.DebugConfiguration[] };
    const matches = (launchJson.configurations ?? []).filter((configuration) => configuration.name === input.configurationName);
    if (matches.length !== 1) throw new Error(matches.length ? `Multiple launch configurations are named '${input.configurationName}'` : `Launch configuration '${input.configurationName}' was not found`);
    const configuration = matches[0];
    if (configuration.request !== "launch") throw new Error(`Launch configuration '${input.configurationName}' must use request 'launch'`);

    await this.stopAllDebugging();
    const buildLogPath = createDebugLogPath("build");
    const projectFileInput = input.projectFile ?? (typeof configuration.projectFile === "string" ? configuration.projectFile : undefined);
    if (projectFileInput) {
      const projectFile = this.resolveProjectFile(projectFileInput, folder.uri.fsPath);
      const buildArguments = ["build", projectFile, "--configuration", input.configuration ?? "Debug", "--nologo"];
      if (input.targetFramework) buildArguments.push("--framework", input.targetFramework);
      const build = await this.runLaunchBuild(`Building ${basename(projectFile)}`, "dotnet", buildArguments, dirname(projectFile), buildLogPath);
      if (build.exitCode !== 0) return { success: false, started: false, stage: "build", resultCode: build.exitCode, buildLogPath };
    } else if (typeof configuration.preLaunchTask === "string" && configuration.preLaunchTask.length > 0) {
      const task = (await vscode.tasks.fetchTasks()).find((candidate) => candidate.name === configuration.preLaunchTask);
      const execution = task?.execution as vscode.ProcessExecution | undefined;
      if (!task || (task.definition as { type?: string }).type !== "process" || !execution || typeof execution.process !== "string") {
        await writeDebugLog(buildLogPath, `Build task '${configuration.preLaunchTask}' must be a process task to capture its output safely`);
        return { success: false, started: false, stage: "build", resultCode: 2, buildLogPath };
      }
      const workspacePath = folder.uri.fsPath;
      const expand = (value: string) => value.replaceAll("${workspaceFolder}", workspacePath).replaceAll("${workspaceFolderBasename}", basename(workspacePath));
      const command = expand(execution.process);
      const args = (execution.args ?? []).map(expand);
      if (/\$\{[^}]+\}/.test(command) || args.some((argument) => /\$\{[^}]+\}/.test(argument))) {
        await writeDebugLog(buildLogPath, `Build task '${configuration.preLaunchTask}' uses unresolved VS Code variables`);
        return { success: false, started: false, stage: "build", resultCode: 2, buildLogPath };
      }
      const options = execution.options;
      const cwd = typeof options?.cwd === "string" ? resolve(workspacePath, expand(options.cwd)) : workspacePath;
      const environment = { ...process.env, ...(options?.env ?? {}) };
      const build = await this.runLaunchBuild(`Running build task '${configuration.preLaunchTask}'`, command, args, cwd, buildLogPath, environment);
      if (build.exitCode !== 0) return { success: false, started: false, stage: "build", resultCode: build.exitCode, buildLogPath };
    } else {
      await writeDebugLog(buildLogPath, "The launch configuration must define projectFile or a process-based preLaunchTask so EsiMCP can build and capture output");
      return { success: false, started: false, stage: "build", resultCode: 2, buildLogPath };
    }

    const debugLogPath = createDebugLogPath("debug");
    const { preLaunchTask: _preLaunchTask, projectFile: _projectFile, ...debugConfiguration } = configuration;
    debugConfiguration.console ??= "internalConsole";
    try {
      const result = await this.startDebugging({ workspaceFolder: folder.uri.fsPath, configuration: debugConfiguration });
      if (!result.started) {
        await writeDebugLog(debugLogPath, "VS Code declined to start the selected launch.json configuration");
        return { success: false, started: false, stage: "debug", resultCode: 1, debugLogPath };
      }
      return { success: true, started: true, stage: "debug", sessionId: result.sessionId, session: result.session, buildLogPath };
    } catch (error) {
      await writeDebugLog(debugLogPath, error instanceof Error ? error.stack ?? error.message : String(error));
      return { success: false, started: false, stage: "debug", resultCode: 1, debugLogPath };
    }
  }

  async hotReload(input: {
    mode: "watch" | "stopWatch" | "rebuild";
    projectFile?: string;
    configurationName?: string;
    workspaceFolder?: string;
    targetFramework?: string;
    configuration?: string;
  }): Promise<Record<string, unknown>> {
    if (input.mode === "watch") return this.startDotnetWatch(input);
    if (input.mode === "stopWatch") return this.stopWatch();

    if (input.projectFile) {
      return this.launchProject({
        projectFile: input.projectFile,
        workspaceFolder: input.workspaceFolder,
        targetFramework: input.targetFramework,
        configuration: input.configuration,
      });
    }
    if (input.configurationName) {
      return this.launchConfiguration({
        configurationName: input.configurationName,
        workspaceFolder: input.workspaceFolder,
        projectFile: input.projectFile,
        targetFramework: input.targetFramework,
        configuration: input.configuration,
      });
    }
    return { success: false, stage: "build", resultCode: 2, reason: "A projectFile or configurationName is required for a full rebuild" };
  }

  private async runLaunchBuild(label: string, command: string, args: string[], cwd: string, logPath: string, env?: NodeJS.ProcessEnv): Promise<CommandResult> {
    beginDebugOutput(label);
    try {
      const result = await runLoggedCommand(command, args, cwd, logPath, env, (_stream, chunk) => appendDebugOutput(chunk));
      finishDebugOutput(label, result.exitCode);
      return result;
    } catch (error) {
      appendDebugOutput(`${error instanceof Error ? error.message : String(error)}\n`);
      finishDebugOutput(label, 1);
      throw error;
    }
  }

  private async startDotnetWatch(input: {
    projectFile?: string;
    workspaceFolder?: string;
    targetFramework?: string;
    configuration?: string;
  }): Promise<Record<string, unknown>> {
    if (!input.projectFile) return { success: false, stage: "hotReload", resultCode: 2, reason: "projectFile is required to start dotnet watch" };
    if (this.watchStartInProgress) return { success: false, stage: "hotReload", resultCode: 2, reason: "A dotnet watch task is already starting" };

    const folder = this.resolveWorkspaceFolder(input.workspaceFolder);
    if (!folder) throw new Error("A workspace folder is required to start dotnet watch");
    const projectFile = this.resolveProjectFile(input.projectFile, folder.uri.fsPath);
    if (this.watchExecution && this.watchProjectFile === projectFile) {
      return { success: true, started: false, alreadyRunning: true, stage: "hotReload", mode: "watch", projectFile, taskName: this.watchTask?.name };
    }

    this.watchStartInProgress = true;
    try {
      if (this.watchExecution) {
        const stopped = await this.stopWatch();
        if (!stopped.success) return stopped;
      }
      await this.stopAllDebugging();

      const configuration = input.configuration ?? "Debug";
      const definition: vscode.TaskDefinition = {
        type: "esiMcpDotnetWatch",
        projectFile,
        configuration,
        ...(input.targetFramework ? { targetFramework: input.targetFramework } : {}),
      };
      const argumentsList = ["watch", "--non-interactive", "run", "--project", projectFile, "--configuration", configuration];
      if (input.targetFramework) argumentsList.push("--framework", input.targetFramework);
      const task = new vscode.Task(
        definition,
        folder,
        `EsiMCP dotnet watch: ${basename(projectFile, ".csproj")}`,
        "EsiMCP",
        new vscode.ProcessExecution("dotnet", argumentsList, {
          cwd: dirname(projectFile),
          env: { DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER: "1" },
        }),
      );
      task.presentationOptions = {
        reveal: vscode.TaskRevealKind.Always,
        panel: vscode.TaskPanelKind.Dedicated,
        showReuseMessage: false,
      };
      this.watchTask = task;
      this.watchProjectFile = projectFile;
      this.watchExitCode = undefined;
      const execution = await this.waitForTaskStart(task);
      if (this.watchTask !== task) {
        return { success: false, stage: "hotReload", resultCode: this.watchExitCode ?? 1, reason: "dotnet watch exited before its task was registered" };
      }
      this.watchExecution = execution;
      return {
        success: true,
        started: true,
        stage: "hotReload",
        mode: "watch",
        projectFile,
        taskName: task.name,
        initialBuildOccurs: true,
        hotReloadRequested: true,
        restartOnUnsupportedEdits: true,
      };
    } catch (error) {
      this.watchTask = undefined;
      this.watchProjectFile = undefined;
      return { success: false, stage: "hotReload", resultCode: 1, reason: error instanceof Error ? error.message : String(error) };
    } finally {
      this.watchStartInProgress = false;
    }
  }

  private async stopWatch(): Promise<Record<string, unknown>> {
    const execution = this.watchExecution;
    if (!execution) return { success: true, stopped: false, stage: "hotReload", mode: "stopWatch" };

    const ended = new Promise<{ completed: boolean; exitCode?: number }>((resolve) => {
      const waiters = this.watchEndWaiters.get(execution) ?? new Set();
      const waiter = (result: { completed: boolean; exitCode?: number }) => {
        clearTimeout(timer);
        waiters.delete(waiter);
        resolve(result);
      };
      const timer = setTimeout(() => waiter({ completed: false }), 10000);
      waiters.add(waiter);
      this.watchEndWaiters.set(execution, waiters);
    });
    execution.terminate();
    const result = await ended;
    if (!result.completed) return { success: false, stopped: false, stage: "hotReload", resultCode: 2, reason: "Timed out waiting for dotnet watch to stop" };
    return { success: true, stopped: true, stage: "hotReload", mode: "stopWatch", exitCode: result.exitCode };
  }

  private waitForTaskStart(task: vscode.Task): Promise<vscode.TaskExecution> {
    return new Promise((resolve, reject) => {
      let settled = false;
      let failed = false;
      let taskExecution: vscode.TaskExecution | undefined;
      const matchesTask = (execution: vscode.TaskExecution) => execution.task === task
        || (execution.task.name === task.name && execution.task.source === task.source);
      const cleanup = () => {
        clearTimeout(timer);
        startListener.dispose();
        endListener.dispose();
      };
      const fail = (error: Error) => {
        if (settled) return;
        settled = true;
        failed = true;
        cleanup();
        taskExecution?.terminate();
        reject(error);
      };
      const startListener = vscode.tasks.onDidStartTask(({ execution }) => {
        if (settled || !matchesTask(execution)) return;
        settled = true;
        cleanup();
        resolve(execution);
      });
      const endListener = vscode.tasks.onDidEndTaskProcess(({ execution, exitCode }) => {
        if (!matchesTask(execution)) return;
        fail(new Error(`dotnet watch exited before starting (exit code ${exitCode})`));
      });
      const timer = setTimeout(() => fail(new Error("Timed out waiting for the dotnet watch task to start")), 15000);
      void vscode.tasks.executeTask(task).then((execution) => {
        taskExecution = execution;
        if (failed) execution.terminate();
      }, (error: unknown) => {
        fail(error instanceof Error ? error : new Error(String(error)));
      });
    });
  }

  private resolveProjectFile(projectFile: string, workspacePath: string): string {
    const resolvedProjectFile = resolve(workspacePath, projectFile);
    if (!resolvedProjectFile.toLowerCase().endsWith(".csproj")) throw new Error("projectFile must point to a .csproj file");
    const projectRelativePath = relative(workspacePath, resolvedProjectFile);
    if (projectRelativePath === ".." || projectRelativePath.startsWith(`..${sep}`) || isAbsolute(projectRelativePath)) {
      throw new Error("projectFile must be inside the selected workspace folder");
    }
    return resolvedProjectFile;
  }

  private async observeDebugState(): Promise<void> {
    const session = vscode.debug.activeDebugSession;
    const stackItem = vscode.debug.activeStackItem;
    if (!session || !stackItem) {
      if (this.pausedStateKey) {
        const [sessionId] = this.pausedStateKey.split(":", 1);
        if (this.debugSessionPaused.get(sessionId) !== false) {
          this.debugSessionPaused.set(sessionId, false);
          this.publishDebugEvent({ type: "continued", sessionId, sessionName: session?.name ?? "", timestamp: new Date().toISOString() });
        }
        this.pausedStateKey = null;
      }
      return;
    }

    const frameId = "frameId" in stackItem && typeof stackItem.frameId === "number" ? stackItem.frameId : "unknown";
    const stateKey = `${session.id}:${frameId}`;
    if (stateKey === this.pausedStateKey) return;
    this.pausedStateKey = stateKey;
    if (this.debugSessionPaused.has(session.id)) return;
    this.debugSessionPaused.set(session.id, true);

    const details = stackItem as unknown as DebugStackItemDetails;
    const exception = await this.getExceptionInfo(session, stackItem);
    this.publishDebugEvent({
      type: "paused",
      sessionId: session.id,
      sessionName: session.name,
      timestamp: new Date().toISOString(),
      reason: exception ? "exception" : "unknown",
      exceptionType: exception?.exceptionType,
      exceptionMessage: exception?.exceptionMessage,
      fileFullPath: details.source?.uri?.fsPath,
      line: typeof details.range?.start?.line === "number" ? details.range.start.line + 1 : undefined,
    });
  }

  private async getExceptionInfo(session: vscode.DebugSession, stackItem: vscode.DebugStackFrame | vscode.DebugThread): Promise<{ exceptionType?: string; exceptionMessage?: string } | undefined> {
    if (!("threadId" in stackItem) || typeof stackItem.threadId !== "number") return undefined;
    try {
      const response = await this.dapRequest(session, "exceptionInfo", { threadId: stackItem.threadId });
      const body = (response.body ?? response) as { exceptionId?: unknown; description?: unknown };
      if (typeof body.exceptionId !== "string" && typeof body.description !== "string") return undefined;
      return {
        exceptionType: typeof body.exceptionId === "string" ? body.exceptionId : undefined,
        exceptionMessage: typeof body.description === "string" ? this.redact(body.description) as string : undefined,
      };
    } catch {
      return undefined;
    }
  }

  private publishDebugEvent(event: Omit<DebugEvent, "id">): void {
    const published = { ...event, id: this.nextEventId++ };
    this.debugEvents.push(published);
    while (this.debugEvents.length > 100) this.debugEvents.shift();
    const waiterIndex = this.eventWaiters.findIndex((waiter) => (!waiter.type || waiter.type === published.type)
      && (!waiter.sessionId || waiter.sessionId === published.sessionId)
      && published.id > waiter.afterId);
    if (waiterIndex >= 0) {
      const waiter = this.eventWaiters.splice(waiterIndex, 1)[0];
      clearTimeout(waiter.timer);
      if (waiter.consume) this.debugEvents.splice(this.debugEvents.indexOf(published), 1);
      waiter.resolve(published);
    }
    for (const listener of this.eventListeners) listener(published);
  }

  private finishDebugSession(session: vscode.DebugSession): void {
    if (!this.debugSessions.has(session.id)) return;
    if (this.pausedStateKey?.startsWith(`${session.id}:`)) this.pausedStateKey = null;
    this.debugSessionPaused.delete(session.id);
    this.debugSessions.delete(session.id);
    this.debugAdapterTrackers.delete(session.id);
    this.publishDebugEvent({ type: "terminated", sessionId: session.id, sessionName: session.name, timestamp: new Date().toISOString() });
  }

  async stopDebugging(): Promise<void> {
    const session = this.requireSession();
    log(`Stopping debug session: session=${session.id}, name=${session.name}`);
    const termination = this.waitForSessionTermination(session);
    try {
      await vscode.debug.stopDebugging(session);
      await termination.promise;
    } catch (error) {
      termination.cancel();
      throw error;
    }
  }

  async stopAllDebugging(): Promise<void> {
    const sessions = new Map(this.debugSessions);
    for (const session of vscode.debug.sessions ?? []) sessions.set(session.id, session);
    const activeSession = this.readActiveSession();
    if (activeSession) sessions.set(activeSession.id, activeSession);
    const terminations = [...sessions.values()].map((session) => {
      log(`Stopping debug session before launch: session=${session.id}, name=${session.name}`);
      return this.waitForSessionTermination(session);
    });
    try {
      await vscode.debug.stopDebugging();
      await Promise.all(terminations.map((termination) => termination.promise));
    } catch (error) {
      terminations.forEach((termination) => termination.cancel());
      throw error;
    }
  }

  async stepOver(): Promise<void> { await this.step("next"); }
  async stepInto(): Promise<void> { await this.step("stepIn"); }
  async stepOut(): Promise<void> { await this.step("stepOut"); }

  async continueExecution(): Promise<void> {
    const session = this.requirePausedSession();
    const threadId = await this.getActiveThreadId(session);
    const afterId = this.nextEventId - 1;
    const [, continued] = await Promise.all([
      this.dapRequest(session, "continue", { threadId }),
      this.waitForDebugEventAfter(this.getTimeoutMs("debugStateTimeoutMs", 30000), "continued", session.id, afterId, false),
    ]);
    if (!continued) throw new Error("Timed out waiting for the debug adapter to continue the session");
  }

  async pauseExecution(): Promise<void> {
    const session = this.requireSession();
    if (this.debugSessionPaused.get(session.id) === true || (!this.debugSessionPaused.has(session.id) && vscode.debug.activeStackItem)) {
      throw new Error("Debug session is already paused");
    }
    const threads = (await this.dapRequest(session, "threads", {})).threads ?? [];
    const threadId = threads[0]?.id;
    if (typeof threadId !== "number") throw new Error("Could not determine a debug thread to pause");
    const afterId = this.nextEventId - 1;
    const [, paused] = await Promise.all([
      this.dapRequest(session, "pause", { threadId }),
      this.waitForDebugEventAfter(this.getTimeoutMs("debugStateTimeoutMs", 30000), "paused", session.id, afterId, false),
    ]);
    if (!paused) throw new Error("Timed out waiting for the debug adapter to pause the session");
    await this.waitForState(() => vscode.debug.activeDebugSession === session && !!vscode.debug.activeStackItem, "debugger to expose the paused stack");
  }

  async restartDebugging(rebuildTaskName?: string): Promise<boolean> {
    const session = this.readActiveSession();
    if (!session) return false;
    log(`Restarting debug session: session=${session.id}, name=${session.name}`);
    const termination = this.waitForSessionTermination(session);
    try {
      await vscode.debug.stopDebugging(session);
      await termination.promise;
      log(`Debug session stopped for restart: session=${session.id}, name=${session.name}`);

      if (rebuildTaskName?.trim()) {
        await this.runTaskByName(rebuildTaskName.trim());
      }

      const start = this.waitForNewSession(session);
      const started = await Promise.race([
        start.promise.then(() => true),
        vscode.debug.startDebugging(session.workspaceFolder, session.configuration),
      ]);
      if (!started) {
        start.cancel();
        throw new Error(`VS Code did not start debug session '${session.name}'`);
      }
      await start.promise;
      log(`Debug session restart completed: previousSession=${session.id}, currentSession=${this.getActiveSessionId() ?? "none"}, name=${session.name}`);
    } catch (error) {
      log(`Debug session restart failed: session=${session.id}, name=${session.name}, error=${error instanceof Error ? error.message : String(error)}`);
      termination.cancel();
      throw error;
    }
    return true;
  }

  async addBreakpoint(fileFullPath: string, line: number, condition?: string, logMessage?: string): Promise<BreakpointStatus> {
    const document = await this.openSourceDocument(fileFullPath, line);
    const location = new vscode.Location(document.uri, new vscode.Position(line - 1, 0));
    const breakpoint = new vscode.SourceBreakpoint(location, true, condition, undefined, logMessage);
    vscode.debug.addBreakpoints([breakpoint]);
    return this.getBreakpointStatus(breakpoint);
  }

  async removeBreakpoint(fileFullPath: string, line: number): Promise<void> {
    const document = await this.openSourceDocument(fileFullPath, line);
    const uri = document.uri;
    const matches = vscode.debug.breakpoints.filter((breakpoint) => breakpoint instanceof vscode.SourceBreakpoint && breakpoint.location.uri.toString() === uri.toString() && breakpoint.location.range.start.line === line - 1);
    if (matches.length === 0) throw new Error(`No breakpoint exists at ${fileFullPath}:${line}`);
    vscode.debug.removeBreakpoints(matches);
  }

  clearAllBreakpoints(): void { vscode.debug.removeBreakpoints(vscode.debug.breakpoints); }

  async listBreakpoints(): Promise<BreakpointStatus[]> {
    const sourceBreakpoints = vscode.debug.breakpoints.filter((breakpoint): breakpoint is vscode.SourceBreakpoint => breakpoint instanceof vscode.SourceBreakpoint);
    return Promise.all(sourceBreakpoints.map((breakpoint) => this.getBreakpointStatus(breakpoint)));
  }

  async listVariableNames(scope: Scope): Promise<string[]> {
    const variables = await this.getScopedVariables(scope);
    return variables.map((variable) => variable.name).slice(0, MAX_VARIABLES);
  }

  async getVariablesValues(variableNames: string[], scope: Scope): Promise<Record<string, unknown>> {
    const session = this.requirePausedSession();
    const variables = await this.getScopedVariables(scope);
    const requested = new Set(variableNames);
    const values: Record<string, unknown> = {};
    for (const variable of variables) {
      if (!requested.has(variable.name)) continue;
      if (this.isSecretName(variable.name)) {
        values[variable.name] = "[REDACTED]";
        continue;
      }
      let value: unknown = variable.value;
      if (variable.evaluateName) {
        try {
          const response = await this.dapRequest(session, "evaluate", { expression: variable.evaluateName, frameId: this.requireFrameId(), context: "watch" });
          value = response?.result ?? response?.value ?? value;
        } catch {
          value = variable.value;
        }
      }
      values[variable.name] = this.redact(value, variable.name);
    }
    return values;
  }

  async evaluateExpression(expression: string): Promise<unknown> {
    const session = this.requirePausedSession();
    const response = await this.dapRequest(session, "evaluate", { expression, frameId: this.requireFrameId(), context: "repl" });
    return this.redact(response?.result ?? response?.value ?? response, expression);
  }

  private async getScopedVariables(scope: Scope): Promise<DapVariable[]> {
    const session = this.requirePausedSession();
    const response = await this.dapRequest(session, "scopes", { frameId: this.requireFrameId() });
    const scopes = (response?.scopes ?? []).filter((item) => this.matchesScope(item.name ?? "", scope));
    if (scopes.length === 0) throw new Error(`No '${scope}' debug scope is available in the paused frame`);
    const variables: DapVariable[] = [];
    for (const item of scopes.slice(0, scope === "all" ? 10 : 2)) {
      if (!item.variablesReference) continue;
      const result = await this.dapRequest(session, "variables", { variablesReference: item.variablesReference });
      variables.push(...(result?.variables ?? []).slice(0, MAX_VARIABLES));
    }
    return [...new Map(variables.map((variable) => [variable.name, variable])).values()].slice(0, MAX_VARIABLES);
  }

  private matchesScope(name: string, scope: Scope): boolean {
    if (scope === "all") return true;
    const normalized = name.toLowerCase();
    return scope === "local" ? normalized.includes("local") || normalized.includes("argument") : normalized.includes("global") || normalized.includes("static");
  }

  private async openSourceDocument(fileFullPath: string, line: number): Promise<vscode.TextDocument> {
    const document = await vscode.workspace.openTextDocument(vscode.Uri.file(fileFullPath));
    if (line < 1 || line > document.lineCount) throw new Error(`Line ${line} is outside ${fileFullPath}; document has ${document.lineCount} lines`);
    return document;
  }

  private async getBreakpointStatus(breakpoint: vscode.SourceBreakpoint): Promise<BreakpointStatus> {
    const session = this.getDebugSession();
    let protocolBreakpoint: DapBreakpoint | undefined;
    if (session) {
      const deadline = Date.now() + Math.min(this.getTimeoutMs("debugAdapterTimeoutMs", 30000), 5000);
      do {
        protocolBreakpoint = await session.getDebugProtocolBreakpoint(breakpoint) as DapBreakpoint | undefined;
        if (protocolBreakpoint) break;
        await new Promise((resolve) => setTimeout(resolve, 100));
      } while (Date.now() < deadline);
    }

    return {
      fileFullPath: breakpoint.location.uri.fsPath,
      line: breakpoint.location.range.start.line + 1,
      condition: breakpoint.condition,
      logMessage: breakpoint.logMessage,
      sessionId: session?.id ?? null,
      bound: protocolBreakpoint !== undefined,
      verified: protocolBreakpoint?.verified ?? null,
      adapterLine: protocolBreakpoint?.line ?? null,
      message: protocolBreakpoint?.message ?? null,
    };
  }

  private requireSession(): vscode.DebugSession {
    const session = this.getDebugSession();
    if (!session) throw new Error("No active debug session");
    return session;
  }

  private requirePausedSession(): vscode.DebugSession {
    const session = this.requireSession();
    if (this.debugSessionPaused.get(session.id) === false || (!this.debugSessionPaused.has(session.id) && !vscode.debug.activeStackItem)) {
      throw new Error("Debug session is not paused at a stack frame");
    }
    return session;
  }

  private requireFrameId(): number {
    const stackItem = vscode.debug.activeStackItem;
    if (!stackItem || !("frameId" in stackItem) || typeof stackItem.frameId !== "number") throw new Error("No paused debug stack frame");
    return stackItem.frameId;
  }

  private async step(command: string): Promise<void> {
    const session = this.requirePausedSession();
    const threadId = await this.getActiveThreadId(session);
    const afterId = this.nextEventId - 1;
    const [, continued] = await Promise.all([
      this.dapRequest(session, command, { threadId }),
      this.waitForDebugEventAfter(this.getTimeoutMs("debugStateTimeoutMs", 30000), "continued", session.id, afterId, false),
    ]);
    if (!continued) throw new Error("Timed out waiting for the debug adapter to resume for stepping");
    const paused = await this.waitForDebugEventAfter(this.getTimeoutMs("debugStateTimeoutMs", 30000), "paused", session.id, continued.id, false);
    if (!paused) throw new Error("Timed out waiting for the debug adapter to pause after stepping");
    if (vscode.debug.activeDebugSession !== session) throw new Error("Debug session stopped while stepping");
    await this.waitForState(() => vscode.debug.activeDebugSession === session && !!vscode.debug.activeStackItem, "debugger to pause after stepping");
  }

  private async waitForState(predicate: () => boolean, description: string): Promise<void> {
    if (predicate()) return;
    const timeoutMs = this.getTimeoutMs("debugStateTimeoutMs", 30000);
    await new Promise<void>((resolve, reject) => {
      const disposables = [vscode.debug.onDidChangeActiveDebugSession(check), vscode.debug.onDidChangeActiveStackItem(check)];
      const timer = setTimeout(() => finish(new Error(`Timed out after ${timeoutMs}ms waiting for ${description}`)), timeoutMs);
      const finish = (error?: Error) => { clearTimeout(timer); disposables.forEach((disposable) => disposable.dispose()); error ? reject(error) : resolve(); };
      function check(): void { if (predicate()) finish(); }
    });
  }

  private waitForSessionTermination(session: vscode.DebugSession): { promise: Promise<void>; cancel(): void } {
    this.debugSessions.set(session.id, session);
    let settled = false;
    let timer: ReturnType<typeof setTimeout>;
    let resolvePromise: () => void;
    let rejectPromise: (error: Error) => void;
    let disposable: vscode.Disposable;
    const finish = (error?: Error) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      disposable.dispose();
      error ? rejectPromise(error) : resolvePromise();
    };
    disposable = this.onDebugEvent((event) => {
      if (event.type === "terminated" && event.sessionId === session.id) finish();
    });
    const promise = new Promise<void>((resolve, reject) => {
      resolvePromise = resolve;
      rejectPromise = reject;
      timer = setTimeout(() => finish(new Error(`Timed out after ${this.getTimeoutMs("debugStateTimeoutMs", 30000)}ms waiting for debug session to stop`)), this.getTimeoutMs("debugStateTimeoutMs", 30000));
    });

    return { promise, cancel: () => finish(new Error("Waiting for debug session termination was cancelled")) };
  }

  private waitForNewSession(session: vscode.DebugSession): { promise: Promise<void>; cancel(error?: Error): void } {
    let settled = false;
    let timer: ReturnType<typeof setTimeout>;
    let resolvePromise: () => void;
    let rejectPromise: (error: Error) => void;
    const disposables: vscode.Disposable[] = [];
    const finish = (error?: Error) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      disposables.forEach((disposable) => disposable.dispose());
      error ? rejectPromise(error) : resolvePromise();
    };
    const normalizeName = (name: string) => name.split(" « ", 1)[0].trim();
    const previousName = normalizeName(session.name);
    const matches = (candidate: vscode.DebugSession) => {
      const candidateName = normalizeName(candidate.name);
      return candidateName === previousName
        || candidateName.startsWith(`${previousName} `)
        || previousName.startsWith(`${candidateName} `);
    };
    const promise = new Promise<void>((resolve, reject) => {
      resolvePromise = resolve;
      rejectPromise = reject;
      timer = setTimeout(() => {
        const error = new Error(`Timed out after ${this.getTimeoutMs("debugStateTimeoutMs", 30000)}ms waiting for debug session restart`);
        log(`Debug session restart timed out waiting for a new session ID: previousSession=${session.id}, name=${session.name}`);
        finish(error);
      }, this.getTimeoutMs("debugStateTimeoutMs", 30000));
    });

    const onStarted = (startedSession: vscode.DebugSession) => {
      log(`Observed debug session during restart: previous=${session.id}/${session.name}, current=${startedSession.id}/${startedSession.name}`);
      if (matches(startedSession) && startedSession.id !== session.id) {
        log(`Debug session started during restart: previousSession=${session.id}, currentSession=${startedSession.id}, name=${startedSession.name}`);
        finish();
      } else if (matches(startedSession)) {
        log(`Rejected recycled debug session ID during restart: session=${startedSession.id}, name=${startedSession.name}`);
        finish(new Error(`VS Code recycled debug session ID '${startedSession.id}' during restart`));
      }
    };
    this.debugSessionStartListeners.add(onStarted);
    disposables.push({ dispose: () => this.debugSessionStartListeners.delete(onStarted) });
    disposables.push(vscode.debug.onDidStartDebugSession(onStarted));

    return { promise, cancel: (error = new Error("Waiting for debug session restart was cancelled")) => finish(error) };
  }

  private async runTaskByName(taskName: string): Promise<void> {
    const task = (await vscode.tasks.fetchTasks()).find((candidate) => candidate.name === taskName);
    if (!task) throw new Error(`Task '${taskName}' was not found in tasks.json`);

    log(`Running rebuild task before debug restart: task=${taskName}`);
    let settled = false;
    let resolvePromise: () => void;
    let rejectPromise: (error: Error) => void;
    const finish = (error?: Error) => {
      if (settled) return;
      settled = true;
      disposables.forEach((disposable) => disposable.dispose());
      error ? rejectPromise(error) : resolvePromise();
    };
    const disposables = [
      vscode.tasks.onDidEndTaskProcess((event) => {
        if (event.execution.task.name === task.name) {
          if (event.exitCode && event.exitCode !== 0) finish(new Error(`Rebuild task '${taskName}' exited with code ${event.exitCode}`));
          else finish();
        }
      }),
      vscode.tasks.onDidEndTask((event) => {
        if (event.execution.task.name === task.name) finish();
      }),
    ];
    const completed = new Promise<void>((resolve, reject) => { resolvePromise = resolve; rejectPromise = reject; });
    try {
      await vscode.tasks.executeTask(task);
      await completed;
      log(`Rebuild task completed before debug restart: task=${taskName}`);
    } finally {
      disposables.forEach((disposable) => disposable.dispose());
    }
  }

  private async dapRequest(session: vscode.DebugSession, command: string, args: unknown): Promise<DapResponse> {
    const timeoutMs = this.getTimeoutMs("debugAdapterTimeoutMs", 30000);
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error(`Debug adapter timed out after ${timeoutMs}ms during '${command}'`)), timeoutMs);
      session.customRequest(command, args).then((result) => { clearTimeout(timer); resolve(result as DapResponse); }, (error) => { clearTimeout(timer); reject(error); });
    });
  }

  private async getActiveThreadId(session: vscode.DebugSession): Promise<number> {
    const activeStackItem = vscode.debug.activeStackItem as unknown as { frameId?: number; threadId?: number } | undefined;
    if (typeof activeStackItem?.threadId === "number") return activeStackItem.threadId;

    const threads = (await this.dapRequest(session, "threads", {})).threads ?? [];
    if (typeof activeStackItem?.frameId === "number") {
      for (const thread of threads) {
        const stackTrace = await this.dapRequest(session, "stackTrace", { threadId: thread.id, startFrame: 0, levels: 100 });
        if (stackTrace.stackFrames?.some((frame) => frame.id === activeStackItem.frameId)) return thread.id;
      }
    }
    if (threads.length === 1) return threads[0].id;
    throw new Error("Could not determine the active debug thread");
  }

  private getTimeoutMs(settingName: string, fallback: number): number {
    const value = vscode.workspace.getConfiguration("esimcp").get<number>(settingName);
    return typeof value === "number" && Number.isFinite(value) && value > 0 ? value : fallback;
  }

  private resolveWorkspaceFolder(folderPath?: string): vscode.WorkspaceFolder | undefined {
    const folders = vscode.workspace.workspaceFolders ?? [];
    if (folderPath) {
      const folder = vscode.workspace.getWorkspaceFolder(vscode.Uri.file(folderPath));
      if (!folder) throw new Error(`Workspace folder '${folderPath}' is not part of the current workspace`);
      return folder;
    }
    if (folders.length > 1) throw new Error("workspaceFolder is required when the current workspace has multiple folders");
    return folders[0];
  }

  private waitForStartedSession(configurationName: string): { promise: Promise<vscode.DebugSession>; cancel(): void } {
    let settled = false;
    let timer: ReturnType<typeof setTimeout>;
    let resolvePromise: (session: vscode.DebugSession) => void;
    let rejectPromise: (error: Error) => void;
    let listener: vscode.Disposable;
    const finish = (error?: Error, session?: vscode.DebugSession) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      listener.dispose();
      if (error) rejectPromise(error);
      else if (session) resolvePromise(session);
    };
    const promise = new Promise<vscode.DebugSession>((resolve, reject) => {
      resolvePromise = resolve;
      rejectPromise = reject;
      timer = setTimeout(() => finish(new Error(`Timed out after ${this.getTimeoutMs("debugStateTimeoutMs", 30000)}ms waiting for debug session '${configurationName}'`)), this.getTimeoutMs("debugStateTimeoutMs", 30000));
    });
    void promise.catch(() => undefined);
    listener = vscode.debug.onDidStartDebugSession((session) => {
      if (session.name === configurationName || session.name.startsWith(`${configurationName} `)) finish(undefined, session);
    });
    return { promise, cancel: () => finish(new Error("Debug session startup was cancelled")) };
  }

  private getDebugSession(configurationName?: string): vscode.DebugSession | undefined {
    const activeSession = this.readActiveSession();
    if (!configurationName) return activeSession;
    if (!activeSession) return undefined;
    return activeSession.name === configurationName || activeSession.name.startsWith(`${configurationName} `)
      ? activeSession
      : undefined;
  }

  private readActiveSession(): vscode.DebugSession | undefined {
    const activeSession = vscode.debug.activeDebugSession;
    if (activeSession) return activeSession;
    const vscodeSessions = vscode.debug.sessions;
    if (vscodeSessions?.length) return vscodeSessions[vscodeSessions.length - 1];
    const trackedSessions = Array.from(this.debugSessions.values());
    return trackedSessions[trackedSessions.length - 1];
  }

  private isSecretName(name: string): boolean { return SECRET_NAME.test(name); }

  private redact(value: unknown, key?: string): unknown {
    if (key && this.isSecretName(key)) return "[REDACTED]";
    if (typeof value === "string") return SECRET_VALUE.test(value) ? "[REDACTED]" : value;
    if (Array.isArray(value)) return value.map((item) => this.redact(item));
    if (value && typeof value === "object") return Object.fromEntries(Object.entries(value).map(([name, item]) => [name, this.redact(item, name)]));
    return value;
  }
}