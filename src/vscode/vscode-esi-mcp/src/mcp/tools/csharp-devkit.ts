import * as vscode from "vscode";
import * as fs from "node:fs/promises";
import * as path from "node:path";
import { z } from "zod";
import type { DebugManager } from "../../debug/manager.js";
import type { SessionManager } from "../../terminal/session-manager.js";
import type { McpToolResponse } from "../../types/index.js";
import { csharpDevKitArgumentsSchema, csharpDevKitCommandSchema, csharpDevKitEmptySchema, csharpDevKitListCommandsSchema, csharpDevKitNoArgumentsSchema, csharpDevKitProjectLaunchArgumentsSchema, csharpDevKitReadinessArgumentsSchema, csharpDevKitRestartArgumentsSchema, toJsonSchema } from "./schemas.js";
import { DEBUG_TOOLS, handleActiveDebugSession, handleDebugHostReadiness, handleRestartDebugSession, handleStopDebugSession } from "./debug.js";

const CSHARP_DEV_KIT_EXTENSION_ID = "ms-dotnettools.csdevkit";
const DEBUG_LAUNCH_COMMANDS = new Set([
  "csdevkit.debug.fileLaunch",
  "csdevkit.debug.projectDebugLaunch",
]);
let debugLaunchInProgress = false;
const ADDITIONAL_CSHARP_DEV_KIT_COMMANDS: CSharpDevKitCommand[] = [
  {
    command: "csdevkit.debug.active.session",
    title: "Active Debug Session",
    keyboardShortcuts: [],
    menuContexts: [],
    registered: false,
    argumentsSchema: toJsonSchema(csharpDevKitNoArgumentsSchema),
  },
  {
    command: "csdevkit.debug.check.host.readyness",
    title: "Check Debug Host Readiness",
    keyboardShortcuts: [],
    menuContexts: [],
    registered: false,
    argumentsSchema: toJsonSchema(csharpDevKitReadinessArgumentsSchema),
  },
  {
    command: "csdevkit.debug.output.diagnostics",
    title: "Debug Console Diagnostics",
    keyboardShortcuts: [],
    menuContexts: [],
    registered: false,
    argumentsSchema: toJsonSchema(csharpDevKitReadinessArgumentsSchema),
  },
  {
    command: "csdevkit.debug.stop",
    title: "Stop Debugging",
    keyboardShortcuts: [],
    menuContexts: [],
    registered: false,
    argumentsSchema: toJsonSchema(csharpDevKitNoArgumentsSchema),
  },
  {
    command: "csdevkit.debug.restart",
    title: "Restart Debugging",
    keyboardShortcuts: [],
    menuContexts: [],
    registered: false,
    argumentsSchema: toJsonSchema(csharpDevKitRestartArgumentsSchema),
  },
];

type CSharpDevKitCommand = {
  command: string;
  title: string;
  keyboardShortcuts: string[];
  menuContexts: string[];
  registered: boolean;
  argumentsSchema: Record<string, unknown>;
  argumentsExample?: unknown[];
};

const EXISTING_DEBUG_DEVKIT_COMMANDS = new Set([
  "csdevkit.debug.active.session",
  "csdevkit.debug.check.host.readyness",
  "csdevkit.debug.restart",
  "csdevkit.debug.stop",
]);
const DEBUG_DEVKIT_COMMANDS: CSharpDevKitCommand[] = DEBUG_TOOLS
  .filter((tool) => !EXISTING_DEBUG_DEVKIT_COMMANDS.has(`csdevkit.${tool.name}`))
  .map((tool) => ({
    command: `csdevkit.${tool.name}`,
    title: tool.name.split(".").map((part) => part.charAt(0).toUpperCase() + part.slice(1)).join(" "),
    keyboardShortcuts: [],
    menuContexts: [],
    registered: false,
    argumentsSchema: toJsonSchema(tool.schema === csharpDevKitEmptySchema
      ? csharpDevKitNoArgumentsSchema
      : z.array(tool.schema).length(1)),
  }));
const VIRTUAL_DEBUG_COMMANDS = new Set([
  ...ADDITIONAL_CSHARP_DEV_KIT_COMMANDS,
  ...DEBUG_DEVKIT_COMMANDS,
].map((command) => command.command));

type CSharpDevKitPackageJson = {
  version?: string;
  contributes?: {
    commands?: Array<{ command?: unknown; title?: unknown }>;
    keybindings?: Array<{ command?: unknown; key?: unknown }>;
    menus?: Record<string, Array<{ command?: unknown }>>;
  };
};

export interface CSharpDevKitToolDefinition {
  name: string;
  description: string;
  schema: import("zod").ZodType;
  handler: (params: unknown, debugManager?: DebugManager, sessionManager?: SessionManager) => Promise<McpToolResponse>;
}

const text = (value: unknown): McpToolResponse => ({
  content: [{ type: "text", text: JSON.stringify(value ?? null) }],
});

function getCSharpDevKitExtension(): vscode.Extension<CSharpDevKitPackageJson> {
  const extension = vscode.extensions.getExtension(CSHARP_DEV_KIT_EXTENSION_ID);
  if (!extension) {
    throw new Error(`C# Dev Kit extension '${CSHARP_DEV_KIT_EXTENSION_ID}' is not installed`);
  }
  return extension;
}

async function getLocalizedTitles(extension: vscode.Extension<CSharpDevKitPackageJson>): Promise<Record<string, string>> {
  try {
    const source = await fs.readFile(path.join(extension.extensionPath, "package.nls.json"), "utf8");
    return JSON.parse(source) as Record<string, string>;
  } catch {
    return {};
  }
}

async function getCSharpDevKitCommands(): Promise<CSharpDevKitCommand[]> {
  const extension = getCSharpDevKitExtension();
  const contributes = extension.packageJSON.contributes;
  const manifestCommands: Array<{ command?: unknown; title?: unknown }> = contributes?.commands ?? [];
  const registeredCommands = new Set(await vscode.commands.getCommands(true));
  const localizedTitles = await getLocalizedTitles(extension);
  const keyboardShortcuts = new Map<string, string[]>();
  const keybindings: Array<{ command?: unknown; key?: unknown }> = contributes?.keybindings ?? [];
  for (const item of keybindings) {
    if (typeof item.command !== "string" || typeof item.key !== "string") continue;
    keyboardShortcuts.set(item.command, [...(keyboardShortcuts.get(item.command) ?? []), item.key]);
  }
  const menuContexts = new Map<string, string[]>();
  const menus: Record<string, Array<{ command?: unknown }>> = contributes?.menus ?? {};
  for (const [menu, items] of Object.entries(menus)) {
    for (const item of items) {
      if (typeof item.command !== "string") continue;
      menuContexts.set(item.command, [...(menuContexts.get(item.command) ?? []), menu]);
    }
  }

  const declaredCommands = manifestCommands
    .filter((item): item is { command: string; title?: unknown } => typeof item.command === "string")
    .map((item) => ({
      command: item.command,
      title: typeof item.title === "string" && item.title.startsWith("%")
        ? localizedTitles[item.title.slice(1, -1)] ?? item.command
        : typeof item.title === "string" ? item.title : item.command,
      keyboardShortcuts: keyboardShortcuts.get(item.command) ?? [],
      menuContexts: menuContexts.get(item.command) ?? [],
      registered: registeredCommands.has(item.command),
      argumentsSchema: DEBUG_LAUNCH_COMMANDS.has(item.command)
        ? item.command === "csdevkit.debug.projectDebugLaunch"
          ? toJsonSchema(csharpDevKitProjectLaunchArgumentsSchema)
          : toJsonSchema(z.array(z.object({ scheme: z.literal("file"), fsPath: z.string().min(1) }).passthrough()).length(1))
        : toJsonSchema(csharpDevKitArgumentsSchema),
      ...(item.command === "csdevkit.debug.projectDebugLaunch"
        ? { argumentsExample: [{ path: "/absolute/path/to/Project.csproj" }] }
        : {}),
    }));

  return [...declaredCommands, ...ADDITIONAL_CSHARP_DEV_KIT_COMMANDS, ...DEBUG_DEVKIT_COMMANDS];
}

function getInvocationSyntax(command: CSharpDevKitCommand): string {
  if (command.command === "csdevkit.debug.fileLaunch") {
    return `Call csharp_devkit_execute_command with commandId "${command.command}" and arguments containing the file URI object for the target project, for example [{"scheme":"file","fsPath":"<absolute Esi.Web .csproj path>"}]. The C# Dev Kit resolves the project launch settings, including its configured port.`;
  }
  if (command.command === "csdevkit.debug.projectDebugLaunch") {
    return `Call csharp_devkit_execute_command with commandId "${command.command}" and arguments containing one project context object with its absolute path, for example [{"path":"<absolute .csproj path>"}]. The C# Dev Kit converts it to a VS Code file URI.`;
  }
  if (VIRTUAL_DEBUG_COMMANDS.has(command.command)) {
    return command.argumentsSchema.maxItems === 0
      ? `Call csharp_devkit_execute_command with commandId "${command.command}" and arguments: [].`
      : `Call csharp_devkit_execute_command with commandId "${command.command}" and arguments: [<one object matching this command's argumentsSchema>].`;
  }
  return `Call csharp_devkit_execute_command with commandId "${command.command}" and arguments: [<positional arguments required by this command>]. The Dev Kit manifest does not publish a command-specific arguments schema; omit arguments or use [] when no arguments are required.`;
}

async function listCommands(params: unknown): Promise<McpToolResponse> {
  const input = csharpDevKitListCommandsSchema.parse(params ?? {});
  const extension = getCSharpDevKitExtension();
  const commands = await getCSharpDevKitCommands();
  const matchingCommands = input.commandId
    ? commands.filter((command) => command.command === input.commandId)
    : commands;
  if (input.commandId && matchingCommands.length === 0) {
    throw new Error(`Command '${input.commandId}' is not declared by the C# Dev Kit or EsiMCP`);
  }

  return text({
    extensionId: CSHARP_DEV_KIT_EXTENSION_ID,
    version: extension.packageJSON.version,
    active: extension.isActive,
    commands: matchingCommands.map((command) => ({ ...command, invocationSyntax: getInvocationSyntax(command) })),
  });
}

async function findProjectForActiveEditor(): Promise<vscode.Uri | undefined> {
  const activeDocument = vscode.window.activeTextEditor?.document.uri;
  if (!activeDocument || activeDocument.scheme !== "file") return undefined;

  const workspaceRoots = (vscode.workspace.workspaceFolders ?? []).map((folder) => folder.uri.fsPath);
  let directory = path.dirname(activeDocument.fsPath);
  while (workspaceRoots.some((root) => directory === root || directory.startsWith(`${root}${path.sep}`))) {
    const projectFiles = (await fs.readdir(directory, { withFileTypes: true }))
      .filter((entry) => entry.isFile() && entry.name.endsWith(".csproj"))
      .map((entry) => path.join(directory, entry.name));
    if (projectFiles.length === 1) return vscode.Uri.file(projectFiles[0]);

    const parent = path.dirname(directory);
    if (parent === directory) break;
    directory = parent;
  }

  return undefined;
}

async function resolveProjectLaunchArguments(argumentsValue: unknown[] | undefined): Promise<unknown[]> {
  if (argumentsValue && argumentsValue.length > 0) return csharpDevKitProjectLaunchArgumentsSchema.parse(argumentsValue);

  const projectUri = await findProjectForActiveEditor();
  if (projectUri) return [{ path: projectUri.fsPath }];

  const projects = await vscode.workspace.findFiles("**/*.csproj", "**/{bin,obj,node_modules}/**");
  if (projects.length === 1) return [{ path: projects[0].fsPath }];

  throw new Error("C# Dev Kit project launch requires a project context; select a project in Solution Explorer or pass { path: \"<absolute .csproj path>\" } as the first argument");
}

async function executeHotReloadSilently(command: string, argumentsValue: unknown[]): Promise<unknown> {
  const windowApi = vscode.window as unknown as Record<string, unknown>;
  const messageMethods = ["showErrorMessage", "showWarningMessage", "showInformationMessage"] as const;
  const suppressedMessages: Array<{ method: string; message: string }> = [];
  const originalMethods = new Map<string, unknown>();

  for (const method of messageMethods) {
    const original = windowApi[method];
    if (typeof original !== "function") continue;
    originalMethods.set(method, original);
    windowApi[method] = (message: unknown) => {
      suppressedMessages.push({ method, message: String(message) });
      return Promise.resolve(undefined);
    };
  }

  try {
    const result = await vscode.commands.executeCommand(command, ...argumentsValue);
    return { result, suppressedMessages };
  } finally {
    for (const [method, original] of originalMethods) windowApi[method] = original;
  }
}

async function executeDebugLaunchCommand(
  command: string,
  resolveArguments: () => Promise<unknown[]>,
  sessionManager?: SessionManager,
): Promise<McpToolResponse> {
  if (debugLaunchInProgress || vscode.debug.activeDebugSession) {
    return text({ commandId: command, started: false, sessionId: null });
  }

  debugLaunchInProgress = true;
  let startListener: vscode.Disposable | undefined;
  let readinessPrepared = false;
  let readinessBound = false;

  try {
    const argumentsValue = await resolveArguments();
    sessionManager?.prepareDebugHostReadiness();
    readinessPrepared = sessionManager !== undefined;
    let startedSession: vscode.DebugSession | undefined;
    let resolveStartedSession: (session: vscode.DebugSession) => void = () => undefined;
    const startedSessionPromise = new Promise<vscode.DebugSession>((resolve) => {
      resolveStartedSession = resolve;
    });
    startListener = vscode.debug.onDidStartDebugSession((session) => {
      startedSession = session;
      resolveStartedSession(session);
    });
    const result = await vscode.commands.executeCommand(command, ...argumentsValue);
    if (result === false) return text({ commandId: command, result, started: false, sessionId: null });

    const activeSession = vscode.debug.activeDebugSession;
    let session = startedSession ?? activeSession;
    if (!session) {
      const configuredTimeout = vscode.workspace.getConfiguration("esimcp").get<number>("debugStateTimeoutMs", 30000);
      const timeoutMs = typeof configuredTimeout === "number" && Number.isFinite(configuredTimeout) && configuredTimeout > 0
        ? configuredTimeout
        : 30000;
      let timeout: ReturnType<typeof setTimeout> | undefined;
      try {
        session = await Promise.race([
          startedSessionPromise,
          new Promise<never>((_, reject) => {
            timeout = setTimeout(() => reject(new Error(`Timed out after ${timeoutMs}ms waiting for C# Dev Kit command '${command}' to start a debug session`)), timeoutMs);
          }),
        ]);
      } finally {
        if (timeout) clearTimeout(timeout);
      }
    }

    sessionManager?.bindDebugHostReadiness(session);
    readinessBound = true;
    return text({ commandId: command, result, started: true, sessionId: session.id });
  } finally {
    startListener?.dispose();
    if (readinessPrepared && !readinessBound) sessionManager?.cancelPendingDebugHostReadiness();
    debugLaunchInProgress = false;
  }
}

async function executeCommand(params: unknown, debugManager?: DebugManager, sessionManager?: SessionManager): Promise<McpToolResponse> {
  const input = csharpDevKitCommandSchema.parse(params);
  const command = (await getCSharpDevKitCommands()).find((item) => item.command === input.commandId);
  if (!command) {
    throw new Error(`Command '${input.commandId}' is not declared by the C# Dev Kit or exposed as an EsiMCP virtual command`);
  }

  if (input.commandId === "csdevkit.debug.active.session") {
    if (!debugManager) throw new Error("Debug manager is unavailable");
    return handleActiveDebugSession({}, debugManager);
  }
  if (input.commandId === "csdevkit.debug.check.host.readyness") {
    if (!debugManager || !sessionManager) throw new Error("Debug or session manager is unavailable");
    const argumentsValue = csharpDevKitReadinessArgumentsSchema.parse(input.arguments ?? []);
    return handleDebugHostReadiness(argumentsValue[0], debugManager, sessionManager);
  }
  if (input.commandId === "csdevkit.debug.output.diagnostics") {
    if (!debugManager || !sessionManager) throw new Error("Debug or session manager is unavailable");
    const argumentsValue = csharpDevKitReadinessArgumentsSchema.parse(input.arguments ?? []);
    const sessionId = argumentsValue[0]?.sessionId ?? debugManager.getActiveSessionId();
    if (!sessionId) return text(null);
    return text(sessionManager.getDebugConsoleDiagnostics(sessionId));
  }
  if (input.commandId === "csdevkit.debug.stop") {
    if (!debugManager || !sessionManager) throw new Error("Debug or session manager is unavailable");
    return handleStopDebugSession({}, debugManager, sessionManager);
  }
  if (input.commandId === "csdevkit.debug.restart") {
    if (!debugManager) throw new Error("Debug manager is unavailable");
    const argumentsValue = csharpDevKitRestartArgumentsSchema.parse(input.arguments ?? []);
    return handleRestartDebugSession(argumentsValue[0], debugManager);
  }

  const debugCommand = DEBUG_TOOLS.find((item) => input.commandId === `csdevkit.${item.name}`);
  if (debugCommand) {
    if (!debugManager || !sessionManager) throw new Error("Debug or session manager is unavailable");
    const argumentsValue = input.arguments ?? [];
    if (debugCommand.schema === csharpDevKitEmptySchema) {
      if (argumentsValue.length > 0) throw new Error(`Command '${input.commandId}' does not accept arguments`);
      return debugCommand.handler({}, debugManager, sessionManager);
    }
    if (argumentsValue.length !== 1) throw new Error(`Command '${input.commandId}' requires one object argument`);
    return debugCommand.handler(argumentsValue[0], debugManager, sessionManager);
  }

  const extension = getCSharpDevKitExtension();
  if (!extension.isActive) await extension.activate();
  if (!(await vscode.commands.getCommands(true)).includes(command.command)) {
    throw new Error(`C# Dev Kit command '${command.command}' is not registered in the current workspace`);
  }

  if (DEBUG_LAUNCH_COMMANDS.has(command.command)) {
    const resolveArguments = input.commandId === "csdevkit.debug.projectDebugLaunch"
      ? () => resolveProjectLaunchArguments(input.arguments)
      : async () => input.arguments ?? [];
    return executeDebugLaunchCommand(command.command, resolveArguments, sessionManager);
  }

  const argumentsValue = input.arguments ?? [];
  const result = input.commandId === "csdevkit.debug.hotReload"
    ? await executeHotReloadSilently(command.command, argumentsValue)
    : await vscode.commands.executeCommand(command.command, ...argumentsValue);
  return text({ commandId: command.command, result });
}

export const CSHARP_DEVKIT_TOOLS: CSharpDevKitToolDefinition[] = [
  {
    name: "csharp_devkit_list_commands",
    description: "EsiMCP C# Dev Kit: list all commands declared by the installed extension, or query one command's invocation syntax with commandId",
    schema: csharpDevKitListCommandsSchema,
    handler: async (params) => listCommands(params),
  },
  {
    name: "csharp_devkit_execute_command",
    description: "EsiMCP C# Dev Kit: execute one command declared by the installed Microsoft C# Dev Kit extension",
    schema: csharpDevKitCommandSchema,
    handler: executeCommand,
  },
];