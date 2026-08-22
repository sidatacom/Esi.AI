import * as vscode from "vscode";
import * as fs from "node:fs/promises";
import * as path from "node:path";

const MAX_VARIABLES = 100;
const SECRET_NAME = /(password|passwd|secret|token|api[_-]?key|connectionstring|authorization|credential|private[_-]?key)/i;
const SECRET_VALUE = /(bearer\s+[A-Za-z0-9._~+/=-]+|(?:api[_-]?key|password|secret|token)\s*[:=]\s*\S+|eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+)/i;

type Scope = "local" | "global" | "all";
type DapVariable = { name: string; value?: string; evaluateName?: string; variablesReference?: number };
type DapResponse = { scopes?: Array<{ name?: string; variablesReference?: number }>; variables?: DapVariable[]; result?: unknown; value?: unknown; body?: { exceptionId?: string; description?: string; breakMode?: string } };
type DapBreakpoint = { id?: number; verified?: boolean; line?: number; column?: number; message?: string };
export type DebugStartLifecycle = { onAcceptedStart?: () => void; onEnd?: () => void };
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
  private readonly debugEvents: DebugEvent[] = [];
  private readonly eventWaiters: Array<{ type?: DebugEvent["type"]; resolve: (event: DebugEvent | null) => void; timer: ReturnType<typeof setTimeout> }> = [];
  private readonly eventListeners = new Set<DebugEventListener>();
  private nextEventId = 1;
  private pausedStateKey: string | null = null;
  private debugStartInProgress = false;

  constructor() {
    this.debugDisposables.push(vscode.debug.onDidChangeActiveStackItem(() => { void this.observeDebugState(); }));
    this.debugDisposables.push(vscode.debug.onDidTerminateDebugSession((session) => {
      if (this.pausedStateKey?.startsWith(`${session.id}:`)) this.pausedStateKey = null;
      this.publishDebugEvent({ type: "terminated", sessionId: session.id, sessionName: session.name, timestamp: new Date().toISOString() });
    }));
  }

  dispose(): void {
    this.debugDisposables.forEach((disposable) => disposable.dispose());
    this.debugEvents.length = 0;
    for (const waiter of this.eventWaiters.splice(0)) {
      clearTimeout(waiter.timer);
      waiter.resolve(null);
    }
    this.eventListeners.clear();
  }

  onDebugEvent(listener: DebugEventListener): vscode.Disposable {
    this.eventListeners.add(listener);
    return { dispose: () => this.eventListeners.delete(listener) };
  }

  async waitForDebugEvent(timeoutMs: number, type?: DebugEvent["type"]): Promise<DebugEvent | null> {
    await this.observeDebugState();
    const queuedIndex = this.debugEvents.findIndex((event) => !type || event.type === type);
    if (queuedIndex >= 0) return this.debugEvents.splice(queuedIndex, 1)[0];

    return new Promise((resolve) => {
      const timer = setTimeout(() => {
        const index = this.eventWaiters.findIndex((waiter) => waiter.timer === timer);
        if (index >= 0) this.eventWaiters.splice(index, 1);
        resolve(null);
      }, timeoutMs);
      this.eventWaiters.push({ type, resolve, timer });
    });
  }

  getActiveSessionId(): string | null {
    return vscode.debug.activeDebugSession?.id ?? null;
  }

  getSetting(setting: string): { setting: string; value: unknown } {
    const separator = setting.lastIndexOf(".");
    const section = setting.slice(0, separator);
    const key = setting.slice(separator + 1);
    const value = vscode.workspace.getConfiguration(section).get<unknown>(key);
    return { setting, value: this.redact(value, setting) };
  }

  async startDebugging(input: { workingDirectory: string; fileFullPath?: string; testName?: string; configurationName?: string }, lifecycle?: DebugStartLifecycle): Promise<boolean> {
    if (this.debugStartInProgress || this.getDebugSession()) return false;
    this.debugStartInProgress = true;

    try {
      lifecycle?.onAcceptedStart?.();
      const folder = vscode.workspace.getWorkspaceFolder(vscode.Uri.file(input.workingDirectory));
      if (input.configurationName?.trim()) {
        const configurationName = input.configurationName.trim();

        const sessionStarted = this.waitForDebugSession(configurationName);
        try {
          const started = await vscode.debug.startDebugging(folder, configurationName);
          if (!started) {
            sessionStarted.cancel();
            return false;
          }

          await sessionStarted.promise;
          return true;
        } catch (error) {
          sessionStarted.cancel();
          throw error;
        }
      }
      if (input.testName?.trim()) {
        await vscode.commands.executeCommand("testing.run", { tests: [input.testName.trim()], debug: true });
        return true;
      }
      const configuredName = vscode.workspace.getConfiguration("esimcp").get<string>("debugConfigurationName", "").trim();
      if (configuredName) {
        const sessionStarted = this.waitForDebugSession(configuredName);
        try {
          const started = await vscode.debug.startDebugging(folder, configuredName);
          if (!started) {
            sessionStarted.cancel();
            return false;
          }

          await sessionStarted.promise;
          return true;
        } catch (error) {
          sessionStarted.cancel();
          throw error;
        }
      }
      if (!input.fileFullPath?.trim()) {
        throw new Error("fileFullPath is required when configurationName and testName are not provided");
      }

      const configuration = await this.createDefaultConfiguration(input.fileFullPath.trim());
      const started = await vscode.debug.startDebugging(folder, configuration);
      if (!started) return false;

      await this.waitForDebugSession(configuration.name);
      return true;
    } finally {
      try {
        lifecycle?.onEnd?.();
      } finally {
        this.debugStartInProgress = false;
      }
    }
  }

  private async observeDebugState(): Promise<void> {
    const session = vscode.debug.activeDebugSession;
    const stackItem = vscode.debug.activeStackItem;
    if (!session || !stackItem) {
      if (this.pausedStateKey) {
        const [sessionId] = this.pausedStateKey.split(":", 1);
        this.publishDebugEvent({ type: "continued", sessionId, sessionName: session?.name ?? "", timestamp: new Date().toISOString() });
        this.pausedStateKey = null;
      }
      return;
    }

    const frameId = "frameId" in stackItem && typeof stackItem.frameId === "number" ? stackItem.frameId : "unknown";
    const stateKey = `${session.id}:${frameId}`;
    if (stateKey === this.pausedStateKey) return;
    this.pausedStateKey = stateKey;

    const source = "source" in stackItem ? stackItem.source : undefined;
    const range = "range" in stackItem ? stackItem.range : undefined;
    const exception = await this.getExceptionInfo(session, stackItem);
    this.publishDebugEvent({
      type: "paused",
      sessionId: session.id,
      sessionName: session.name,
      timestamp: new Date().toISOString(),
      reason: exception ? "exception" : "unknown",
      exceptionType: exception?.exceptionType,
      exceptionMessage: exception?.exceptionMessage,
      fileFullPath: source?.uri.fsPath,
      line: range ? range.start.line + 1 : undefined,
    });
  }

  private async getExceptionInfo(session: vscode.DebugSession, stackItem: vscode.DebugStackFrame | vscode.DebugThread): Promise<{ exceptionType?: string; exceptionMessage?: string } | undefined> {
    if (!("threadId" in stackItem) || typeof stackItem.threadId !== "number") return undefined;
    try {
      const response = await this.dapRequest(session, "exceptionInfo", { threadId: stackItem.threadId });
      const body = response.body ?? response;
      if (!body.exceptionId && !body.description) return undefined;
      return { exceptionType: body.exceptionId, exceptionMessage: typeof body.description === "string" ? this.redact(body.description) as string : undefined };
    } catch {
      return undefined;
    }
  }

  private publishDebugEvent(event: Omit<DebugEvent, "id">): void {
    const published = { ...event, id: this.nextEventId++ };
    this.debugEvents.push(published);
    while (this.debugEvents.length > 100) this.debugEvents.shift();
    const waiterIndex = this.eventWaiters.findIndex((waiter) => !waiter.type || waiter.type === published.type);
    if (waiterIndex >= 0) {
      const waiter = this.eventWaiters.splice(waiterIndex, 1)[0];
      clearTimeout(waiter.timer);
      this.debugEvents.splice(this.debugEvents.indexOf(published), 1);
      waiter.resolve(published);
    }
    for (const listener of this.eventListeners) listener(published);
  }

  async stopDebugging(): Promise<void> {
    const session = this.requireSession();
    const termination = this.waitForSessionTermination(session);
    try {
      await vscode.debug.stopDebugging(session);
      await termination.promise;
    } catch (error) {
      termination.cancel();
      throw error;
    }
  }

  async stepOver(): Promise<void> { await this.step("workbench.action.debug.stepOver"); }
  async stepInto(): Promise<void> { await this.step("workbench.action.debug.stepInto"); }
  async stepOut(): Promise<void> { await this.step("workbench.action.debug.stepOut"); }

  async continueExecution(): Promise<void> {
    this.requirePausedSession();
    const session = this.requireSession();
    await vscode.commands.executeCommand("workbench.action.debug.continue");
    await this.waitForState(() => vscode.debug.activeDebugSession !== session || !vscode.debug.activeStackItem, "debug session to continue or stop");
  }

  async pauseExecution(): Promise<void> {
    const session = this.requireSession();
    if (vscode.debug.activeStackItem) throw new Error("Debug session is already paused");
    await vscode.commands.executeCommand("workbench.action.debug.pause");
    await this.waitForState(() => vscode.debug.activeDebugSession === session && !!vscode.debug.activeStackItem, "debug session to pause");
  }

  async restartDebugging(): Promise<void> {
    const session = this.requireSession();
    await vscode.commands.executeCommand("workbench.action.debug.restart");
    await this.waitForState(() => vscode.debug.activeDebugSession === session || vscode.debug.activeDebugSession !== undefined, "debug session to restart");
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
    if (!vscode.debug.activeStackItem) throw new Error("Debug session is not paused at a stack frame");
    return session;
  }

  private requireFrameId(): number {
    const stackItem = vscode.debug.activeStackItem;
    if (!stackItem || !("frameId" in stackItem) || typeof stackItem.frameId !== "number") throw new Error("No paused debug stack frame");
    return stackItem.frameId;
  }

  private async step(command: string): Promise<void> {
    this.requirePausedSession();
    await vscode.commands.executeCommand(command);
    await this.waitForState(() => !!vscode.debug.activeStackItem, "debugger to reach a paused state");
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
    let settled = false;
    let timer: ReturnType<typeof setTimeout>;
    let resolvePromise: () => void;
    let rejectPromise: (error: Error) => void;
    const disposable = vscode.debug.onDidTerminateDebugSession((terminatedSession) => {
      if (terminatedSession.id === session.id) finish();
    });
    const finish = (error?: Error) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      disposable.dispose();
      error ? rejectPromise(error) : resolvePromise();
    };
    const promise = new Promise<void>((resolve, reject) => {
      resolvePromise = resolve;
      rejectPromise = reject;
      timer = setTimeout(() => finish(new Error(`Timed out after ${this.getTimeoutMs("debugStateTimeoutMs", 30000)}ms waiting for debug session to stop`)), this.getTimeoutMs("debugStateTimeoutMs", 30000));
    });

    return { promise, cancel: () => finish(new Error("Waiting for debug session termination was cancelled")) };
  }

  private waitForDebugSession(configurationName: string): { promise: Promise<void>; cancel(): void } {
    const existingSession = this.getDebugSession(configurationName);
    if (existingSession) return { promise: Promise.resolve(), cancel: () => undefined };

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
    const matches = (session: vscode.DebugSession) => session.name === configurationName || session.name.startsWith(`${configurationName} `);
    const promise = new Promise<void>((resolve, reject) => {
      resolvePromise = resolve;
      rejectPromise = reject;
      timer = setTimeout(() => finish(new Error(`Timed out after ${this.getTimeoutMs("debugStateTimeoutMs", 30000)}ms waiting for debug session '${configurationName}'`)), this.getTimeoutMs("debugStateTimeoutMs", 30000));
    });

    disposables.push(vscode.debug.onDidStartDebugSession((session) => {
      if (matches(session)) finish();
    }));
    disposables.push(vscode.debug.onDidTerminateDebugSession((session) => {
      if (matches(session)) finish(new Error(`Debug session '${configurationName}' terminated before it became ready`));
    }));

    return { promise, cancel: () => finish(new Error("Debug session startup was cancelled")) };
  }

  private async dapRequest(session: vscode.DebugSession, command: string, args: unknown): Promise<DapResponse> {
    const timeoutMs = this.getTimeoutMs("debugAdapterTimeoutMs", 30000);
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error(`Debug adapter timed out after ${timeoutMs}ms during '${command}'`)), timeoutMs);
      session.customRequest(command, args).then((result) => { clearTimeout(timer); resolve(result as DapResponse); }, (error) => { clearTimeout(timer); reject(error); });
    });
  }

  private getTimeoutMs(settingName: string, fallback: number): number {
    const value = vscode.workspace.getConfiguration("esimcp").get<number>(settingName);
    return typeof value === "number" && Number.isFinite(value) && value > 0 ? value : fallback;
  }

  private async createDefaultConfiguration(fileFullPath: string): Promise<vscode.DebugConfiguration> {
    const extension = path.extname(fileFullPath).toLowerCase();
    if (extension !== ".cs" && extension !== ".csproj") {
      return {
        type: this.detectDebuggerType(extension),
        request: "launch",
        name: "EsiMCP Launch",
        program: fileFullPath,
      };
    }

    const projectPath = await this.findNearestProject(fileFullPath);
    if (!projectPath) {
      throw new Error(`Could not locate a .csproj for ${fileFullPath}`);
    }

    const assemblyPath = await this.findBuiltAssembly(projectPath);
    if (!assemblyPath) {
      throw new Error(`Could not find a built assembly for ${path.basename(projectPath)}. Run dotnet build first.`);
    }

    return {
      type: "coreclr",
      request: "launch",
      name: "EsiMCP .NET Launch",
      program: assemblyPath,
      cwd: path.dirname(projectPath),
      stopAtEntry: false,
    };
  }

  private async findNearestProject(fileFullPath: string): Promise<string | null> {
    const resolvedPath = path.resolve(fileFullPath);
    let directory = path.extname(resolvedPath).toLowerCase() === ".csproj"
      ? path.dirname(resolvedPath)
      : (await fs.stat(resolvedPath)).isDirectory() ? resolvedPath : path.dirname(resolvedPath);

    while (true) {
      const entries = await fs.readdir(directory, { withFileTypes: true });
      const project = entries.find((entry) => entry.isFile() && entry.name.toLowerCase().endsWith(".csproj"));
      if (project) return path.join(directory, project.name);

      const parent = path.dirname(directory);
      if (parent === directory) return null;
      directory = parent;
    }
  }

  private async findBuiltAssembly(projectPath: string): Promise<string | null> {
    const projectName = path.basename(projectPath, path.extname(projectPath));
    const binPath = path.join(path.dirname(projectPath), "bin");
    const configurations = ["Debug", "Release"];

    for (const configuration of configurations) {
      const configurationPath = path.join(binPath, configuration);
      let targetFrameworks: string[];
      try {
        targetFrameworks = (await fs.readdir(configurationPath, { withFileTypes: true }))
          .filter((entry) => entry.isDirectory())
          .map((entry) => entry.name);
      } catch {
        continue;
      }

      for (const targetFramework of targetFrameworks) {
        const assemblyPath = path.join(configurationPath, targetFramework, `${projectName}.dll`);
        try {
          await fs.access(assemblyPath);
          return assemblyPath;
        } catch {
          continue;
        }
      }
    }

    return null;
  }

  private detectDebuggerType(extension: string): string {
    const debuggerTypes: Record<string, string> = {
      ".py": "debugpy",
      ".js": "pwa-node",
      ".ts": "pwa-node",
      ".java": "java",
      ".cpp": "cppdbg",
      ".cc": "cppdbg",
      ".c": "cppdbg",
      ".go": "go",
      ".rs": "lldb",
      ".php": "php",
      ".rb": "ruby",
    };
    return debuggerTypes[extension] ?? "pwa-node";
  }

  private getDebugSession(configurationName?: string): vscode.DebugSession | undefined {
    const activeSession = vscode.debug.activeDebugSession;
    if (!configurationName) return activeSession;
    if (!activeSession) return undefined;
    return activeSession.name === configurationName || activeSession.name.startsWith(`${configurationName} `)
      ? activeSession
      : undefined;
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