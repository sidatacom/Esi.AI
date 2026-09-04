import * as vscode from "vscode";
import { appendFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

const apiKeySecret = "esiAiStudio.apiKey";
const defaultTraceFilePath = join(tmpdir(), "esi-ai-studio-provider.jsonl");
const loggingEnabledStateKey = "loggingEnabled";
const loggingPathStateKey = "loggingPath";
const defaultMaxInputTokens = 32768;
const defaultMaxOutputTokens = 32768;

interface OpenAiModel {
  id?: unknown;
  name?: unknown;
  capabilities?: unknown;
  loaded?: unknown;
}

interface OpenAiModelsResponse {
  data?: OpenAiModel[];
}

interface OpenAiStreamChunk {
  error?: {
    message?: unknown;
  };
  usage?: OpenAiUsage;
  choices?: Array<{
    delta?: {
      content?: unknown;
      tool_calls?: OpenAiToolCallDelta[];
    };
  }>;
}

interface OpenAiCompletionResponse {
  usage?: OpenAiUsage;
  choices?: Array<{
    message?: {
      content?: unknown;
      tool_calls?: OpenAiToolCall[];
    };
  }>;
}

interface OpenAiUsage {
  prompt_tokens?: unknown;
  completion_tokens?: unknown;
  total_tokens?: unknown;
  tokens_per_second?: unknown;
}

interface OpenAiToolCall {
  id?: unknown;
  type?: unknown;
  function?: {
    name?: unknown;
    arguments?: unknown;
  };
}

interface OpenAiToolCallDelta extends OpenAiToolCall {
  index?: unknown;
}

interface EsiModel {
  id: string;
  name: string;
  tooltip: string;
  detail: string;
  family: string;
  version: string;
  maxInputTokens: number;
  maxOutputTokens: number;
  capabilities: vscode.LanguageModelChatCapabilities;
}

interface ModelOptions {
  [name: string]: unknown;
}

interface OpenAiMessage {
  role: "user" | "assistant" | "tool";
  content: string | OpenAiContentPart[] | null;
  tool_calls?: Array<{
    id: string;
    type: "function";
    function: {
      name: string;
      arguments: string;
    };
  }>;
  tool_call_id?: string;
}

interface OpenAiContentPart {
  type: "text" | "image_url";
  text?: string;
  image_url?: { url: string };
}

export class EsiAiStudioProvider implements vscode.LanguageModelChatProvider<EsiModel>, vscode.Disposable {
  private readonly changeEmitter = new vscode.EventEmitter<void>();
  private readonly output = vscode.window.createOutputChannel("Esi.AI Studio Models");
  private models: EsiModel[] = [];
  private backendModelIds = new Map<string, string>();
  private refreshPromise?: Promise<number>;
  private retryTimer?: ReturnType<typeof setInterval>;
  private traceSequence = 0;

  public readonly onDidChangeLanguageModelChatInformation = this.changeEmitter.event;

  public constructor(private readonly context: vscode.ExtensionContext) {}

  public provideLanguageModelChatInformation(
    _options: vscode.PrepareLanguageModelChatModelOptions,
    token: vscode.CancellationToken,
  ): Thenable<EsiModel[]> {
    if (this.models.length > 0) {
      this.output.appendLine(`Provider requested ${this.models.length} cached models (silent=${_options.silent}).`);
      return Promise.resolve(this.models);
    }

    return this.refresh(token)
      .then(() => {
        this.output.appendLine(`Provider returned ${this.models.length} models (silent=${_options.silent}).`);
        return this.models;
      })
      .catch(() => this.models);
  }

  public async provideLanguageModelChatResponse(
    model: EsiModel,
    messages: readonly vscode.LanguageModelChatRequestMessage[],
    options: vscode.ProvideLanguageModelChatResponseOptions,
    progress: vscode.Progress<vscode.LanguageModelResponsePart>,
    token: vscode.CancellationToken,
  ): Promise<void> {
    const supportsImages = model.capabilities.imageInput === true;
    const supportsTools = model.capabilities.toolCalling === true || typeof model.capabilities.toolCalling === "number";
    if (!supportsTools && options.tools && options.tools.length > 0) {
      throw new Error("The selected Esi.AI Studio model does not support tool calling.");
    }
    const toolOptimizationResponse = this.getToolOptimizationResponse(messages);
    if (toolOptimizationResponse !== undefined) {
      progress.report(new vscode.LanguageModelTextPart(toolOptimizationResponse));
      return;
    }
    const maxTokens = this.isToolOptimizationRequest(messages) ? 512 : model.maxOutputTokens;
    const reasoningEffort = getReasoningEffort(options.modelOptions) ?? getConfiguredReasoningEffort();
    const requestBody = {
      model: this.backendModelIds.get(model.id) ?? model.id,
      messages: messages.map((message) => this.toOpenAiMessage(message, supportsImages)),
      max_tokens: maxTokens,
      reasoning_effort: reasoningEffort,
      top_p: 0.9,
      tools: supportsTools ? options.tools?.map((tool) => ({
        type: "function",
        function: {
          name: tool.name,
          description: tool.description,
          parameters: tool.inputSchema ?? {},
        },
      })) : undefined,
      tool_choice: supportsTools && options.tools && options.tools.length > 0
        ? options.toolMode === vscode.LanguageModelChatToolMode.Required ? "required" : "auto"
        : undefined,
      stream_options: { include_usage: true },
      stream: true,
    };
    let response: Response;
    try {
      response = await this.request("/chat/completions", requestBody, token);
    } catch (error) {
      if (!isModelNotLoadedError(error)) {
        throw error;
      }

      await this.refresh(token);
      const activeModelId = this.getOnlyBackendModelId();
      if (!activeModelId || activeModelId === requestBody.model) {
        throw error;
      }

      response = await this.request("/chat/completions", { ...requestBody, model: activeModelId }, token);
    }

    if (!response.body) {
      throw new Error("Esi.AI Studio returned an empty response body.");
    }

    if (!response.headers.get("content-type")?.includes("text/event-stream")) {
      const completion = (await response.json()) as OpenAiCompletionResponse;
      this.trace("completion", { mode: "json", payload: completion });
      const content = completion.choices?.[0]?.message?.content;
      if (typeof content === "string" && content.length > 0) {
        const textFilter = new VisibleResponseTextFilter();
        const visibleContent = textFilter.push(content) + textFilter.finish();
        if (visibleContent.length > 0) {
          progress.report(new vscode.LanguageModelTextPart(visibleContent));
        }
      }
      this.reportToolCalls(completion.choices?.[0]?.message?.tool_calls, progress);
      this.reportUsage(completion.usage, progress);
      return;
    }

    const toolCalls = new Map<number, Partial<OpenAiToolCall>>();
    const textFilter = new VisibleResponseTextFilter();
    let usage: OpenAiUsage | undefined;
    let loggedSseChunks = 0;
    try {
      for await (const data of readServerSentEvents(response.body, token)) {
        if (loggedSseChunks < 200) {
          const traceData = data.length > 4096 ? `${data.slice(0, 4096)}...[truncated]` : data;
          this.trace("sse", { data: traceData });
          loggedSseChunks++;
        } else if (loggedSseChunks === 200) {
          this.trace("sse", { data: "[further SSE chunks omitted]" });
          loggedSseChunks++;
        }
        if (data === "[DONE]") {
          const finalText = textFilter.finish();
          if (finalText.length > 0) {
            progress.report(new vscode.LanguageModelTextPart(finalText));
          }
          this.reportToolCalls([...toolCalls.values()], progress);
          this.reportUsage(usage, progress);
          return;
        }

        const chunk = JSON.parse(data) as OpenAiStreamChunk;
        if (typeof chunk.error?.message === "string" && chunk.error.message.length > 0) {
          throw new Error(`Esi.AI Studio request failed: ${chunk.error.message}`);
        }
        usage = chunk.usage ?? usage;
        const content = chunk.choices?.[0]?.delta?.content;
        if (typeof content === "string" && content.length > 0) {
          const visibleContent = textFilter.push(content);
          if (visibleContent.length > 0) {
            progress.report(new vscode.LanguageModelTextPart(visibleContent));
          }
        }
        for (const toolCall of chunk.choices?.[0]?.delta?.tool_calls ?? []) {
          const index = typeof toolCall.index === "number" ? toolCall.index : 0;
          const existing = toolCalls.get(index) ?? {};
          toolCalls.set(index, {
            id: toolCall.id ?? existing.id,
            type: toolCall.type ?? existing.type,
            function: {
              name: appendValue(existing.function?.name, toolCall.function?.name),
              arguments: appendValue(existing.function?.arguments, toolCall.function?.arguments),
            },
          });
        }
      }
    } catch (error) {
      if (token.isCancellationRequested) {
        throw new vscode.CancellationError();
      }

      const message = error instanceof Error ? error.message : String(error);
      this.output.appendLine(`SSE stream failed: ${message}`);
      throw new Error(`Esi.AI Studio SSE stream terminated before completion: ${message}`, { cause: error });
    }

    throw new Error("Esi.AI Studio SSE stream ended before the [DONE] marker.");
  }

  private reportUsage(usage: OpenAiUsage | undefined, progress: vscode.Progress<vscode.LanguageModelResponsePart>): void {
    const promptTokens = toFiniteNumber(usage?.prompt_tokens);
    const completionTokens = toFiniteNumber(usage?.completion_tokens);
    const totalTokens = toFiniteNumber(usage?.total_tokens);
    const tokensPerSecond = toFiniteNumber(usage?.tokens_per_second);
    if (promptTokens === undefined && completionTokens === undefined && totalTokens === undefined && tokensPerSecond === undefined) {
      return;
    }

    const promptText = promptTokens === undefined ? "n/a" : String(Math.round(promptTokens));
    const tokenText = completionTokens === undefined ? "n/a" : String(Math.round(completionTokens));
    const throughputText = tokensPerSecond === undefined ? "n/a" : tokensPerSecond.toFixed(1);
    progress.report(new vscode.LanguageModelTextPart(`\n\n---\n*Esi.AI Studio: ${promptText} prompt + ${tokenText} completion Tokens | ${throughputText} Tok/s*`));
  }

  public provideTokenCount(
    _model: EsiModel,
    text: string | vscode.LanguageModelChatRequestMessage,
    _token: vscode.CancellationToken,
  ): Thenable<number> {
    const value = typeof text === "string" ? text : this.messageText(text);
    return Promise.resolve(Math.ceil(value.length / 4));
  }

  public async refresh(token?: vscode.CancellationToken): Promise<number> {
    if (this.refreshPromise) {
      return this.refreshPromise;
    }

    const refreshPromise = this.loadModels(token);
    this.refreshPromise = refreshPromise;

    try {
      return await refreshPromise;
    } catch (error) {
      this.startAutomaticRefresh();
      throw error;
    } finally {
      if (this.refreshPromise === refreshPromise) {
        this.refreshPromise = undefined;
      }
    }
  }

  public startAutomaticRefresh(): void {
    if (this.retryTimer) {
      return;
    }

    this.retryTimer = setInterval(() => {
      void this.refresh().catch(() => undefined);
    }, 5000);
  }

  private async loadModels(token?: vscode.CancellationToken): Promise<number> {
    try {
      const backendModelIds = new Map<string, string>();
      const discoveredModels = await this.listModels(token ?? new vscode.CancellationTokenSource().token);
      const loadedModels = discoveredModels.filter((model) => model.loaded === true);
      const models = (loadedModels.length > 0 ? loadedModels : discoveredModels).map((model, index) =>
        this.toModel(model, index, backendModelIds),
      );
      const changed = models.length !== this.models.length || models.some((model, index) => !sameModel(model, this.models[index]));
      this.models = models;
      this.backendModelIds = backendModelIds;
      this.output.appendLine(`Model refresh succeeded: ${models.length} models.`);
      if (changed) {
        this.changeEmitter.fire();
      }
      return models.length;
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      this.output.appendLine(`Model refresh failed: ${message}`);
      throw error;
    }
  }

  public async testConnection(): Promise<number> {
    const models = await this.listModels(new vscode.CancellationTokenSource().token);
    return models.length;
  }

  public async inspectRegisteredModels(): Promise<number> {
    const models = await vscode.lm.selectChatModels({ vendor: "esi-ai-studio" });
    this.output.appendLine(`VS Code registered ${models.length} Esi.AI Studio chat models.`);
    return models.length;
  }

  public async configureApiKey(): Promise<void> {
    const apiKey = await vscode.window.showInputBox({
      ignoreFocusOut: true,
      password: true,
      prompt: "Enter the Esi.AI Studio API key. Leave empty to remove it.",
    });
    if (apiKey === undefined) {
      return;
    }

    if (apiKey.trim().length === 0) {
      await this.context.secrets.delete(apiKeySecret);
      vscode.window.showInformationMessage("Esi.AI Studio API key removed.");
      return;
    }

    await this.context.secrets.store(apiKeySecret, apiKey.trim());
    vscode.window.showInformationMessage("Esi.AI Studio API key stored securely.");
  }

  public dispose(): void {
    if (this.retryTimer) {
      clearInterval(this.retryTimer);
      this.retryTimer = undefined;
    }
    this.changeEmitter.dispose();
    this.output.dispose();
  }

  public getLoggingEnabled(): boolean {
    const configuration = vscode.workspace.getConfiguration("esiAiStudio");
    return configuration.inspect<boolean>(loggingEnabledStateKey) ? configuration.get<boolean>(loggingEnabledStateKey, true) : this.context.globalState.get(loggingEnabledStateKey, true);
  }

  public getLoggingPath(): string {
    const configuration = vscode.workspace.getConfiguration("esiAiStudio");
    return configuration.inspect<string>(loggingPathStateKey) ? configuration.get<string>(loggingPathStateKey, "") : this.context.globalState.get(loggingPathStateKey, "");
  }

  public async updateLoggingSetting(key: typeof loggingEnabledStateKey | typeof loggingPathStateKey, value: boolean | string): Promise<void> {
    const configuration = vscode.workspace.getConfiguration("esiAiStudio");
    if (!configuration.inspect(key)) {
      await this.context.globalState.update(key, value);
      return;
    }

    try {
      await configuration.update(key, value, vscode.ConfigurationTarget.Global);
    } catch (error) {
      if (!(error instanceof Error) || !error.message.includes("is not a registered configuration")) {
        throw error;
      }
      await this.context.globalState.update(key, value);
    }
  }

  private async listModels(token?: vscode.CancellationToken): Promise<OpenAiModel[]> {
    const response = await this.request("/models", undefined, token);
    const payload = (await response.json()) as OpenAiModelsResponse;
    return Array.isArray(payload.data) ? payload.data.filter((model) => typeof model.id === "string") : [];
  }

  private getOnlyBackendModelId(): string | undefined {
    return this.backendModelIds.size === 1 ? this.backendModelIds.values().next().value : undefined;
  }

  private async request(path: string, body?: object, token?: vscode.CancellationToken): Promise<Response> {
    const configuration = vscode.workspace.getConfiguration("esiAiStudio");
    const baseUrl = normalizeBaseUrl(configuration.get<string>("baseUrl", "http://127.0.0.1:7010/v1"));
    const controller = new AbortController();
    let timedOut = false;
    const timeout = setTimeout(
      () => {
        timedOut = true;
        controller.abort();
      },
      configuration.get<number>("requestTimeoutMs", 120000),
    );
    const cancellation = token?.onCancellationRequested(() => controller.abort());
    const requestId = ++this.traceSequence;

    try {
      const headers = new Headers({ Accept: body ? "text/event-stream" : "application/json" });
      if (body) {
        headers.set("Content-Type", "application/json");
      }

      const apiKey = await this.getApiKey();
      if (apiKey) {
        headers.set("Authorization", `Bearer ${apiKey}`);
      }

      const url = `${baseUrl}${path}`;
      this.trace("request", {
        requestId,
        method: body ? "POST" : "GET",
        url,
        headers: ["Accept", ...(body ? ["Content-Type"] : []), ...(apiKey ? ["Authorization: Bearer <redacted>"] : [])],
        body: body ?? null,
        curl: body ? this.toCurlCommand(url, body, Boolean(apiKey)) : null,
      });
      const response = await fetch(url, {
        method: body ? "POST" : "GET",
        headers,
        body: body ? JSON.stringify(body) : undefined,
        signal: controller.signal,
      });
      this.trace("response", {
        requestId,
        status: response.status,
        contentType: response.headers.get("content-type"),
      });
      if (!response.ok) {
        const detail = await response.text();
        this.trace("error", { requestId, status: response.status, detail });
        throw new Error(`Esi.AI Studio returned HTTP ${response.status}: ${detail || response.statusText}`);
      }

      return response;
    } catch (error) {
      if (token?.isCancellationRequested) {
        throw new vscode.CancellationError();
      }

      if (timedOut) {
        throw new Error(`Zeitüberschreitung beim Verbinden mit Esi.AI Studio unter ${baseUrl}.`);
      }

      if (error instanceof Error && (error.name === "AbortError" || error.name === "TypeError")) {
        const cause = error.cause instanceof Error ? `; Ursache: ${error.cause.message}` : "";
        this.output.appendLine(`Fetch ${path} fehlgeschlagen (${error.name}): ${error.message}${cause}`);
        throw new Error(`Esi.AI Studio ist unter ${baseUrl} nicht erreichbar. Läuft der Host?`, { cause: error });
      }

      throw error;
    } finally {
      clearTimeout(timeout);
      cancellation?.dispose();
    }
  }

  private trace(event: string, details: Record<string, unknown>): void {
    if (!this.getLoggingEnabled()) {
      return;
    }

    const configuredPath = this.getLoggingPath().trim();
    const traceFilePath = configuredPath || defaultTraceFilePath;
    const entry = JSON.stringify({ timestamp: new Date().toISOString(), event, ...details });
    try {
      appendFileSync(traceFilePath, `${entry}\n`, "utf8");
    } catch {
      // Request tracing must never change provider behavior.
    }
    this.output.appendLine(`[trace] ${entry}`);
  }

  private toCurlCommand(url: string, body: object, hasApiKey: boolean): string {
    const headers = [
      `-H ${shellQuote("Accept: text/event-stream")}`,
      `-H ${shellQuote("Content-Type: application/json")}`,
      ...(hasApiKey ? [`-H ${shellQuote("Authorization: Bearer <redacted>")}`] : []),
    ];
    return ["curl -N", shellQuote(url), ...headers, "--data-raw", shellQuote(JSON.stringify(body))].join(" ");
  }

  private async getApiKey(): Promise<string | undefined> {
    const storedKey = await this.context.secrets.get(apiKeySecret);
    return storedKey || process.env.ESI_AI_STUDIO_API_KEY;
  }

  private toModel(model: OpenAiModel, index: number, backendModelIds: Map<string, string>): EsiModel {
    const backendId = String(model.id);
    const baseId = typeof model.name === "string" && model.name.length > 0 ? model.name : `model-${index + 1}`;
    let id = baseId;
    let suffix = 2;
    while (backendModelIds.has(id)) {
      id = `${baseId}-${suffix}`;
      suffix += 1;
    }
    backendModelIds.set(id, backendId);
    const configuration = vscode.workspace.getConfiguration("esiAiStudio");
    return {
      id,
      name: typeof model.name === "string" && model.name.length > 0 ? model.name : backendId,
      tooltip: `Esi.AI Studio model ${backendId}`,
      detail: "Esi.AI Studio",
      family: "esi-ai-studio",
      version: "1",
      maxInputTokens: configuration.get<number>("maxInputTokens", defaultMaxInputTokens),
      maxOutputTokens: configuration.get<number>("maxOutputTokens", defaultMaxOutputTokens),
      capabilities: toLanguageModelCapabilities(model.capabilities),
    };
  }

  private toOpenAiMessage(message: vscode.LanguageModelChatRequestMessage, supportsImages: boolean): OpenAiMessage {
    const toolResult = message.content.find((part): part is vscode.LanguageModelToolResultPart => part instanceof vscode.LanguageModelToolResultPart);
    if (toolResult) {
      return {
        role: "tool",
        content: this.toOpenAiContent(toolResult.content, supportsImages),
        tool_call_id: toolResult.callId,
      };
    }

    const toolCalls = message.content
      .filter((part): part is vscode.LanguageModelToolCallPart => part instanceof vscode.LanguageModelToolCallPart)
      .map((toolCall) => ({
        id: toolCall.callId,
        type: "function" as const,
        function: { name: toolCall.name, arguments: JSON.stringify(toolCall.input) },
      }));
    const contentParts = message.content.filter((part) => !(part instanceof vscode.LanguageModelToolCallPart));
    return {
      role: message.role === vscode.LanguageModelChatMessageRole.Assistant || toolCalls.length > 0 ? "assistant" : "user",
      content: contentParts.length > 0 ? this.toOpenAiContent(contentParts, supportsImages) : null,
      tool_calls: toolCalls.length > 0 ? toolCalls : undefined,
    };
  }

  private toOpenAiContent(parts: readonly unknown[], supportsImages: boolean): string | OpenAiContentPart[] {
    const content: OpenAiContentPart[] = [];
    for (const part of parts) {
      if (part instanceof vscode.LanguageModelTextPart) {
        content.push({ type: "text", text: part.value });
        continue;
      }
      if (part instanceof vscode.LanguageModelDataPart) {
        if (!supportsImages || !part.mimeType.startsWith("image/")) {
          throw new Error("The selected Esi.AI Studio model does not support this input content type.");
        }
        content.push({
          type: "image_url",
          image_url: { url: `data:${part.mimeType};base64,${Buffer.from(part.data).toString("base64")}` },
        });
        continue;
      }
      throw new Error("The selected Esi.AI Studio model does not support this input content type.");
    }
    return content.length === 1 && content[0].type === "text" ? content[0].text ?? "" : content;
  }

  private reportToolCalls(toolCalls: Iterable<OpenAiToolCall | Partial<OpenAiToolCall>> | undefined, progress: vscode.Progress<vscode.LanguageModelResponsePart>): void {
    if (!toolCalls) {
      return;
    }

    for (const toolCall of toolCalls) {
      const id = typeof toolCall.id === "string" ? toolCall.id : undefined;
      const name = typeof toolCall.function?.name === "string" ? toolCall.function.name : undefined;
      if (!id || !name) {
        continue;
      }

      const argumentsText = typeof toolCall.function?.arguments === "string" ? toolCall.function.arguments : "{}";
      progress.report(new vscode.LanguageModelToolCallPart(id, name, parseToolInput(argumentsText)));
    }
  }

  private messageText(message: vscode.LanguageModelChatRequestMessage): string {
    return message.content
      .filter((part): part is vscode.LanguageModelTextPart => part instanceof vscode.LanguageModelTextPart)
      .map((part) => part.value)
      .join("");
  }

  private isToolOptimizationRequest(messages: readonly vscode.LanguageModelChatRequestMessage[]): boolean {
    const prompt = messages.map((message) => this.messageText(message)).join("\n");
    return prompt.includes("You will be given ") && prompt.includes("groups of tools.") && prompt.includes("For each group, provide a name and summary");
  }

  private getToolOptimizationResponse(messages: readonly vscode.LanguageModelChatRequestMessage[]): string | undefined {
    const prompt = messages.map((message) => this.messageText(message)).join("\n");
    if (!this.isToolOptimizationRequest(messages)) {
      return undefined;
    }

    const groups = [...prompt.matchAll(/<group index="(\d+)">([\s\S]*?)<\/group>/g)];
    const response = groups.map((group) => {
      const groupIndex = Number(group[1]);
      const toolNames = [...group[2].matchAll(/<tool name="([^"]+)">/g)].map((tool) => tool[1]);
      const groupName = `tool_group_${groupIndex}`;
      const summary = toolNames.length > 0
        ? `Related tools for ${toolNames.join(", ")}.`
        : "Related tools grouped by capability.";
      return { groupIndex, groupName, summary };
    });

    return JSON.stringify(response);
  }
}

function getReasoningEffort(modelOptions: Readonly<ModelOptions> | undefined): string | undefined {
  const value = modelOptions?.reasoning_effort ?? modelOptions?.reasoningEffort;
  return normalizeReasoningEffort(value);
}

function getConfiguredReasoningEffort(): string {
  const configuration = vscode.workspace.getConfiguration("esiAiStudio");
  return normalizeReasoningEffort(configuration.get<string>("reasoningEffort", "none")) ?? "none";
}

function normalizeReasoningEffort(value: unknown): string | undefined {
  if (typeof value !== "string") {
    return undefined;
  }

  const normalized = value.trim().toLowerCase();
  return ["none", "low", "medium", "high", "xhigh", "max"].includes(normalized) ? normalized : undefined;
}

function sameModel(left: EsiModel, right: EsiModel | undefined): boolean {
  return (
    right !== undefined &&
    left.id === right.id &&
    left.name === right.name &&
    left.family === right.family &&
    left.version === right.version &&
    left.maxInputTokens === right.maxInputTokens &&
    left.maxOutputTokens === right.maxOutputTokens &&
    JSON.stringify(left.capabilities) === JSON.stringify(right.capabilities)
  );
}

function shellQuote(value: string): string {
  return `'${value.replace(/'/g, `'\\''`)}'`;
}

function normalizeBaseUrl(value: string): string {
  return value.replace(/\/+$/, "");
}

function isModelNotLoadedError(error: unknown): boolean {
  return error instanceof Error && error.message.includes("HTTP 503") && error.message.includes("not loaded");
}

function toLanguageModelCapabilities(value: unknown): vscode.LanguageModelChatCapabilities {
  if (!isRecord(value)) {
    return {};
  }

  return {
    imageInput: value.imageInput === true,
    toolCalling: value.toolCalling === true || typeof value.toolCalling === "number" ? value.toolCalling : false,
  };
}

function appendValue(existing: unknown, next: unknown): string {
  return `${typeof existing === "string" ? existing : ""}${typeof next === "string" ? next : ""}`;
}

function toFiniteNumber(value: unknown): number | undefined {
  return typeof value === "number" && Number.isFinite(value) ? value : undefined;
}

function parseToolInput(value: string): object {
  try {
    const parsed: unknown = JSON.parse(value);
    return isRecord(parsed) ? parsed : {};
  } catch {
    return {};
  }
}

class VisibleResponseTextFilter {
  private static readonly markers = ["<think>", "</think>"];
  private buffered = "";
  private inThinkingBlock = false;

  public push(value: string): string {
    this.buffered += value;
    let visible = "";

    while (this.buffered.length > 0) {
      if (this.inThinkingBlock) {
        const closingIndex = this.buffered.indexOf("</think>");
        if (closingIndex < 0) {
          this.buffered = this.retainPossibleMarkerSuffix(this.buffered, "</think>");
          break;
        }

        this.buffered = this.buffered.slice(closingIndex + "</think>".length);
        this.inThinkingBlock = false;
        continue;
      }

      const openingIndex = this.buffered.indexOf("<think>");
      const closingIndex = this.buffered.indexOf("</think>");
      const markerIndex = this.firstMarkerIndex(openingIndex, closingIndex);
      if (markerIndex >= 0) {
        visible += this.buffered.slice(0, markerIndex);
        if (openingIndex >= 0 && openingIndex === markerIndex) {
          this.buffered = this.buffered.slice(markerIndex + "<think>".length);
          this.inThinkingBlock = true;
        } else {
          this.buffered = this.buffered.slice(markerIndex + "</think>".length);
        }
        continue;
      }

      const safeLength = this.buffered.length - Math.max(
        ...VisibleResponseTextFilter.markers.map((marker) => this.possibleMarkerSuffixLength(this.buffered, marker)),
      );
      visible += this.buffered.slice(0, safeLength);
      this.buffered = this.buffered.slice(safeLength);
      break;
    }

    return visible;
  }

  public finish(): string {
    const visible = this.inThinkingBlock ? "" : this.buffered;
    this.buffered = "";
    return visible;
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

  private retainPossibleMarkerSuffix(value: string, marker: string): string {
    const suffixLength = this.possibleMarkerSuffixLength(value, marker);
    return suffixLength > 0 ? value.slice(-suffixLength) : "";
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

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

async function* readServerSentEvents(
  body: ReadableStream<Uint8Array>,
  token: vscode.CancellationToken,
): AsyncGenerator<string> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  try {
    while (true) {
      if (token.isCancellationRequested) {
        throw new vscode.CancellationError();
      }
      const { done, value } = await reader.read();
      buffer += decoder.decode(value, { stream: !done });

      const events = buffer.split(/\r?\n\r?\n/);
      buffer = events.pop() ?? "";
      for (const event of events) {
        const data = event
          .split(/\r?\n/)
          .filter((line) => line.startsWith("data:"))
          .map((line) => line.slice(5).trimStart())
          .join("\n");
        if (data) {
          yield data;
        }
      }

      if (done) {
        break;
      }
    }
  } finally {
    reader.releaseLock();
  }
}