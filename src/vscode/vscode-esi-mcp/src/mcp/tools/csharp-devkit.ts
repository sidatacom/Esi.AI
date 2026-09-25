import * as vscode from "vscode";
import { AsyncLocalStorage } from "node:async_hooks";
import { randomUUID } from "node:crypto";
import * as fs from "node:fs/promises";
import * as path from "node:path";
import { z } from "zod";
import type { DebugManager } from "../../debug/manager.js";
import type { SessionManager } from "../../terminal/session-manager.js";
import type { McpToolResponse } from "../../types/index.js";
import { csharpDevKitArgumentsSchema, csharpDevKitCommandSchema, csharpDevKitEmptySchema, csharpDevKitInteractionResponseSchema, csharpDevKitInteractionStatusSchema, csharpDevKitListCommandsSchema, csharpDevKitNoArgumentsSchema, csharpDevKitProjectLaunchArgumentsSchema, csharpDevKitReadinessArgumentsSchema, csharpDevKitRestartArgumentsSchema, toJsonSchema } from "./schemas.js";
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

type CSharpDevKitInteractionKind = "message" | "quickPick" | "inputBox" | "openDialog" | "saveDialog" | "workspaceFolderPick";
type CSharpDevKitInteraction = {
  id: string;
  kind: CSharpDevKitInteractionKind;
  message?: string;
  choices?: Array<{ index: number; label: string; description?: string; detail?: string }>;
  options?: Record<string, unknown>;
};
type CSharpDevKitExecution = {
  id: string;
  commandId: string;
  status: "running" | "waitingForAgent" | "completed" | "failed";
  operationFinished: boolean;
  version: number;
  interaction?: CSharpDevKitInteraction;
  resolveInteraction?: (response: unknown) => void;
  mapResponse?: (response: unknown) => unknown;
  response?: McpToolResponse;
  error?: string;
  listeners: Set<() => void>;
};

const commandExecutionContext = new AsyncLocalStorage<CSharpDevKitExecution>();
const commandExecutions = new Map<string, CSharpDevKitExecution>();
let activeCommandExecution: CSharpDevKitExecution | undefined;
let lastCommandExecutionId: string | undefined;

function notifyExecutionChanged(execution: CSharpDevKitExecution): void {
  execution.version += 1;
  for (const listener of execution.listeners) listener();
}

function waitForExecutionChange(execution: CSharpDevKitExecution, waitMs: number, version = execution.version): Promise<void> {
  if (execution.status !== "running" || waitMs === 0 || execution.version !== version) return Promise.resolve();
  return new Promise((resolve) => {
    const finish = () => {
      clearTimeout(timeout);
      execution.listeners.delete(onChange);
      resolve();
    };
    const onChange = () => {
      if (execution.version !== version) finish();
    };
    const timeout = setTimeout(finish, waitMs);
    execution.listeners.add(onChange);
    if (execution.version !== version) finish();
  });
}

function displayValue(value: unknown): string {
  if (typeof value === "string") return value;
  if (value && typeof value === "object" && "value" in value && typeof value.value === "string") return value.value;
  try {
    return JSON.stringify(value);
  } catch {
    return String(value);
  }
}

function interactionChoices(items: unknown[]): CSharpDevKitInteraction["choices"] {
  return items.map((item, index) => {
    const value = item && typeof item === "object" ? item as Record<string, unknown> : {};
    const labelValue = typeof item === "string" ? item : value.label ?? value.title ?? value.name ?? item;
    return {
      index,
      label: displayValue(labelValue),
      ...(typeof value.description === "string" ? { description: value.description } : {}),
      ...(typeof value.detail === "string" ? { detail: value.detail } : {}),
    };
  });
}

function interactionOptions(value: unknown, keys: string[]): Record<string, unknown> {
  if (!value || typeof value !== "object") return {};
  const source = value as Record<string, unknown>;
  return Object.fromEntries(keys.filter((key) => source[key] !== undefined).map((key) => [key, source[key]]));
}

function responseRecord(response: unknown): Record<string, unknown> {
  return response && typeof response === "object" ? response as Record<string, unknown> : {};
}

function responseIndexes(response: unknown, choices: unknown[]): number[] {
  const value = responseRecord(response);
  const indexes = Array.isArray(value.selectedIndexes)
    ? value.selectedIndexes
    : typeof value.selectedIndex === "number" ? [value.selectedIndex]
      : typeof response === "number" ? [response] : [];
  if (indexes.some((index) => !Number.isInteger(index) || (index as number) < 0 || (index as number) >= choices.length)) {
    throw new Error("The response contains an invalid choice index");
  }
  return indexes as number[];
}

function captureInteraction(
  execution: CSharpDevKitExecution,
  interaction: Omit<CSharpDevKitInteraction, "id">,
  mapResponse: (response: unknown) => unknown,
): Promise<unknown> {
  if (execution.interaction) return Promise.reject(new Error("The C# Dev Kit command opened overlapping interactions"));
  return new Promise((resolve) => {
    execution.interaction = { ...interaction, id: randomUUID() };
    execution.status = "waitingForAgent";
    execution.mapResponse = mapResponse;
    execution.resolveInteraction = resolve;
    notifyExecutionChanged(execution);
  });
}

function createCapturedInputControl(execution: CSharpDevKitExecution, control: object, kind: "quickPick" | "inputBox"): object {
  const handlers = new Map<string, Set<(value?: unknown) => void>>();
  const dispatch = (event: string, value?: unknown) => {
    for (const handler of handlers.get(event) ?? []) handler(value);
  };

  return new Proxy(control, {
    get(target, property) {
      if (typeof property !== "string") return Reflect.get(target, property, target);
      if (property.startsWith("onDid")) {
        return (handler: (value?: unknown) => void) => {
          const eventHandlers = handlers.get(property) ?? new Set();
          eventHandlers.add(handler);
          handlers.set(property, eventHandlers);
          return { dispose: () => eventHandlers.delete(handler) };
        };
      }
      if (property === "show") {
        return () => {
          if (kind === "quickPick") {
            const items = Reflect.get(target, "items", target) as unknown[];
            const canPickMany = Reflect.get(target, "canSelectMany", target) === true;
            const prompt = captureInteraction(execution, {
              kind,
              choices: interactionChoices(Array.isArray(items) ? items : []),
              options: interactionOptions(target, ["title", "placeholder", "prompt", "canSelectMany", "value"]),
            }, (response) => {
              const choices = Array.isArray(items) ? items : [];
              const indexes = responseIndexes(response, choices);
              return indexes.length === 0 ? undefined : canPickMany ? indexes.map((index) => choices[index]) : choices[indexes[0]];
            });
            void prompt.then((selected) => commandExecutionContext.run(execution, () => {
              const selectedItems = selected === undefined ? [] : Array.isArray(selected) ? selected : [selected];
              Reflect.set(target, "selectedItems", selectedItems, target);
              Reflect.set(target, "activeItems", selectedItems, target);
              dispatch("onDidChangeActive", selectedItems);
              dispatch("onDidChangeSelection", selectedItems);
              dispatch(selectedItems.length > 0 ? "onDidAccept" : "onDidHide");
            }));
            return;
          }

          const prompt = captureInteraction(execution, {
            kind,
            options: interactionOptions(target, ["title", "prompt", "placeholder", "value", "password", "ignoreFocusOut"]),
          }, (response) => typeof response === "string" ? response : responseRecord(response).value ?? undefined);
          void prompt.then((value) => commandExecutionContext.run(execution, () => {
            if (typeof value !== "string") {
              dispatch("onDidHide");
              return;
            }
            Reflect.set(target, "value", value, target);
            dispatch("onDidChangeValue", value);
            dispatch("onDidAccept");
          }));
        };
      }
      if (property === "hide") return () => dispatch("onDidHide");
      const value = Reflect.get(target, property, target);
      return typeof value === "function" ? value.bind(target) : value;
    },
    set(target, property, value) {
      return Reflect.set(target, property, value, target);
    },
  });
}

function executionSnapshot(execution: CSharpDevKitExecution): Record<string, unknown> {
  const payload: Record<string, unknown> = {
    executionId: execution.id,
    commandId: execution.commandId,
    status: execution.status,
    ...(execution.interaction ? { interaction: execution.interaction } : {}),
  };
  if (execution.status === "completed" && execution.response) {
    const content = execution.response.content.find((item) => item.type === "text");
    if (content?.type === "text") {
      try {
        payload.result = JSON.parse(content.text);
      } catch {
        payload.result = content.text;
      }
    }
  }
  if (execution.error) payload.error = execution.error;
  return payload;
}

function findExecution(executionId?: string): CSharpDevKitExecution {
  const id = executionId ?? activeCommandExecution?.id ?? lastCommandExecutionId;
  const execution = id ? commandExecutions.get(id) : undefined;
  if (!execution) throw new Error(`C# Dev Kit command execution '${id ?? ""}' was not found`);
  return execution;
}

async function getInteractionStatus(params: unknown): Promise<McpToolResponse> {
  const input = csharpDevKitInteractionStatusSchema.parse(params ?? {});
  const execution = findExecution(input.executionId);
  await waitForExecutionChange(execution, input.waitMs);
  return text(executionSnapshot(execution));
}

async function respondToInteraction(params: unknown): Promise<McpToolResponse> {
  const input = csharpDevKitInteractionResponseSchema.parse(params);
  const execution = findExecution(input.executionId);
  if (execution.status !== "waitingForAgent" || execution.interaction?.id !== input.interactionId || !execution.resolveInteraction || !execution.mapResponse) {
    throw new Error(`Interaction '${input.interactionId}' is not pending for execution '${input.executionId}'`);
  }

  const version = execution.version;
  const response = execution.mapResponse(input.response);
  execution.status = execution.operationFinished ? (execution.error ? "failed" : "completed") : "running";
  execution.interaction = undefined;
  execution.resolveInteraction(response);
  execution.resolveInteraction = undefined;
  execution.mapResponse = undefined;
  if (execution.operationFinished) {
    notifyExecutionChanged(execution);
    if (activeCommandExecution === execution) activeCommandExecution = undefined;
  }
  await waitForExecutionChange(execution, input.waitMs, version);

  if (execution.status === "completed" && execution.response) return execution.response;
  return text(executionSnapshot(execution));
}

async function executeWithInteractionCapture(
  commandId: string,
  execute: () => Promise<McpToolResponse>,
): Promise<McpToolResponse> {
  if (activeCommandExecution) {
    throw new Error(`C# Dev Kit command '${activeCommandExecution.commandId}' is still ${activeCommandExecution.status}; handle its interaction before starting another command`);
  }

  const execution: CSharpDevKitExecution = {
    id: randomUUID(),
    commandId,
    status: "running",
    operationFinished: false,
    version: 0,
    listeners: new Set(),
  };
  commandExecutions.set(execution.id, execution);
  activeCommandExecution = execution;
  lastCommandExecutionId = execution.id;

  const windowApi = vscode.window as unknown as Record<string, unknown>;
  const originalMethods = new Map<string, unknown>();
  const patch = (method: string, create: (execution: CSharpDevKitExecution, args: unknown[]) => unknown) => {
    const original = windowApi[method];
    if (typeof original !== "function") return;
    originalMethods.set(method, original);
    windowApi[method] = (...args: unknown[]) => {
      const active = commandExecutionContext.getStore();
      return active ? create(active, args) : original.apply(vscode.window, args);
    };
  };

  const messageMethods = ["showErrorMessage", "showWarningMessage", "showInformationMessage"];
  for (const method of messageMethods) {
    patch(method, (active, args) => {
      const message = displayValue(args[0]);
      const choices = args.slice(1).filter((item) => typeof item === "string" || (item && typeof item === "object" && "title" in item));
      const choiceLabels = interactionChoices(choices);
      const options = args.slice(1).find((item) => item && typeof item === "object" && !("title" in item));
      return captureInteraction(active, {
        kind: "message",
        message,
        choices: choiceLabels,
        options: interactionOptions(options, ["modal", "detail"]),
      }, (response) => {
        if (response === null || response === undefined) return undefined;
        const indexes = responseIndexes(response, choices);
        if (indexes.length > 0) return choices[indexes[0]];
        if (typeof response === "string") return choices.find((choice) => choice === response || (typeof choice === "object" && choice !== null && "title" in choice && choice.title === response));
        return undefined;
      });
    });
  }

  patch("showQuickPick", (active, args) => Promise.resolve(args[0]).then((itemsValue) => {
    const items = Array.isArray(itemsValue) ? itemsValue : [];
    const options = args[1];
    return captureInteraction(active, {
      kind: "quickPick",
      choices: interactionChoices(items),
      options: interactionOptions(options, ["title", "placeHolder", "placeholder", "canPickMany", "matchOnDescription", "matchOnDetail"]),
    }, (response) => {
      const indexes = responseIndexes(response, items);
      return indexes.length === 0 ? undefined : responseRecord(options).canPickMany === true ? indexes.map((index) => items[index]) : items[indexes[0]];
    });
  }));

  patch("showInputBox", (active, args) => captureInteraction(active, {
    kind: "inputBox",
    options: interactionOptions(args[0], ["title", "prompt", "placeHolder", "value", "password", "ignoreFocusOut"]),
  }, (response) => typeof response === "string" ? response : responseRecord(response).value ?? undefined));

  patch("showOpenDialog", (active, args) => captureInteraction(active, {
    kind: "openDialog",
    options: interactionOptions(args[0], ["title", "openLabel", "defaultUri", "canSelectFiles", "canSelectFolders", "canSelectMany", "filters"]),
  }, (response) => {
    const responseValue = responseRecord(response);
    const paths = Array.isArray(responseValue.paths) ? responseValue.paths : Array.isArray(response) ? response : [];
    const fileUris = paths.filter((value): value is string => typeof value === "string").map((filePath) => vscode.Uri.file(filePath));
    return fileUris.length > 0 ? fileUris : undefined;
  }));

  patch("showSaveDialog", (active, args) => captureInteraction(active, {
    kind: "saveDialog",
    options: interactionOptions(args[0], ["title", "saveLabel", "defaultUri", "filters"]),
  }, (response) => {
    const filePath = typeof response === "string" ? response : responseRecord(response).path;
    return typeof filePath === "string" ? vscode.Uri.file(filePath) : undefined;
  }));

  patch("showWorkspaceFolderPick", (active, args) => {
    const folders = vscode.workspace.workspaceFolders ?? [];
    return captureInteraction(active, {
      kind: "workspaceFolderPick",
      choices: interactionChoices(folders.map((folder) => ({ label: folder.name, description: folder.uri.fsPath }))),
      options: interactionOptions(args[0], ["placeHolder", "ignoreFocusOut"]),
    }, (response) => {
      const indexes = responseIndexes(response, folders);
      return indexes.length > 0 ? folders[indexes[0]] : undefined;
    });
  });

  for (const [method, kind] of [["createQuickPick", "quickPick"], ["createInputBox", "inputBox"]] as const) {
    const original = windowApi[method];
    if (typeof original !== "function") continue;
    originalMethods.set(method, original);
    windowApi[method] = (...args: unknown[]) => {
      const active = commandExecutionContext.getStore();
      const control = original.apply(vscode.window, args);
      return active && control && typeof control === "object"
        ? createCapturedInputControl(active, control, kind)
        : control;
    };
  }

  const operation = commandExecutionContext.run(execution, execute);
  void operation.then((response) => {
    execution.response = response;
    execution.operationFinished = true;
    execution.status = execution.interaction ? "waitingForAgent" : "completed";
    notifyExecutionChanged(execution);
  }, (error: unknown) => {
    execution.error = error instanceof Error ? error.message : String(error);
    execution.operationFinished = true;
    execution.status = execution.interaction ? "waitingForAgent" : "failed";
    notifyExecutionChanged(execution);
  }).finally(() => {
    for (const [method, original] of originalMethods) windowApi[method] = original;
    if (activeCommandExecution === execution && !execution.interaction) activeCommandExecution = undefined;
    while (commandExecutions.size > 20) {
      const oldestId = commandExecutions.keys().next().value;
      if (oldestId === undefined) break;
      commandExecutions.delete(oldestId);
    }
  });

  await waitForExecutionChange(execution, 1000);
  if (execution.status === "completed" && execution.response) return execution.response;
  if (execution.status === "failed") throw new Error(execution.error);
  return text(executionSnapshot(execution));
}

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
    return executeWithInteractionCapture(command.command, () => executeDebugLaunchCommand(command.command, resolveArguments, sessionManager));
  }

  const argumentsValue = input.arguments ?? [];
  return executeWithInteractionCapture(command.command, async () => {
    const result = await vscode.commands.executeCommand(command.command, ...argumentsValue);
    return text({ commandId: command.command, result });
  });
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
    description: "EsiMCP C# Dev Kit: execute any command declared by the installed extension. If status is waitingForAgent, inspect the interaction and answer it with csharp_devkit_respond_to_interaction; use csharp_devkit_get_interaction_status while it is running.",
    schema: csharpDevKitCommandSchema,
    handler: executeCommand,
  },
  {
    name: "csharp_devkit_get_interaction_status",
    description: "EsiMCP C# Dev Kit: get or wait for the current state and any captured VS Code popup from a command execution",
    schema: csharpDevKitInteractionStatusSchema,
    handler: getInteractionStatus,
  },
  {
    name: "csharp_devkit_respond_to_interaction",
    description: "EsiMCP C# Dev Kit: submit the agent's selection, text, or file path for a captured popup, then return the next popup or command result",
    schema: csharpDevKitInteractionResponseSchema,
    handler: respondToInteraction,
  },
];