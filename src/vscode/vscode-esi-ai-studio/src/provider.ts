import * as vscode from "vscode";

const apiKeySecret = "esiAiStudio.apiKey";

interface OpenAiModel {
  id?: unknown;
}

interface OpenAiModelsResponse {
  data?: OpenAiModel[];
}

interface OpenAiStreamChunk {
  choices?: Array<{
    delta?: {
      content?: unknown;
    };
  }>;
}

interface OpenAiCompletionResponse {
  choices?: Array<{
    message?: {
      content?: unknown;
    };
  }>;
}

interface EsiModel {
  id: string;
  name: string;
  family: string;
  version: string;
  maxInputTokens: number;
  maxOutputTokens: number;
}

interface OpenAiMessage {
  role: "user" | "assistant";
  content: string;
}

export class EsiAiStudioProvider implements vscode.LanguageModelChatProvider<EsiModel>, vscode.Disposable {
  private readonly changeEmitter = new vscode.EventEmitter<void>();
  private readonly output = vscode.window.createOutputChannel("Esi.AI Studio Models");

  public readonly onDidChangeLanguageModelChatInformation = this.changeEmitter.event;

  public constructor(private readonly context: vscode.ExtensionContext) {}

  public async provideLanguageModelChatInformation(
    _options: vscode.PrepareLanguageModelChatModelOptions,
    token: vscode.CancellationToken,
  ): Promise<EsiModel[]> {
    const models = await this.listModels(token);
    return models.map((model) => this.toModel(model));
  }

  public async provideLanguageModelChatResponse(
    model: EsiModel,
    messages: readonly vscode.LanguageModelChatRequestMessage[],
    _options: vscode.ProvideLanguageModelChatResponseOptions,
    progress: vscode.Progress<vscode.LanguageModelResponsePart>,
    token: vscode.CancellationToken,
  ): Promise<void> {
    const response = await this.request(
      "/chat/completions",
      {
        model: model.id,
        messages: messages.map((message) => this.toOpenAiMessage(message)),
        stream: true,
      },
      token,
    );

    if (!response.body) {
      throw new Error("Esi.AI Studio returned an empty response body.");
    }

    if (!response.headers.get("content-type")?.includes("text/event-stream")) {
      const completion = (await response.json()) as OpenAiCompletionResponse;
      const content = completion.choices?.[0]?.message?.content;
      if (typeof content === "string" && content.length > 0) {
        progress.report(new vscode.LanguageModelTextPart(content));
      }
      return;
    }

    for await (const data of readServerSentEvents(response.body, token)) {
      if (data === "[DONE]") {
        return;
      }

      const chunk = JSON.parse(data) as OpenAiStreamChunk;
      const content = chunk.choices?.[0]?.delta?.content;
      if (typeof content === "string" && content.length > 0) {
        progress.report(new vscode.LanguageModelTextPart(content));
      }
    }
  }

  public provideTokenCount(
    _model: EsiModel,
    text: string | vscode.LanguageModelChatRequestMessage,
    _token: vscode.CancellationToken,
  ): Thenable<number> {
    const value = typeof text === "string" ? text : this.messageText(text);
    return Promise.resolve(Math.ceil(value.length / 4));
  }

  public async refresh(token?: vscode.CancellationToken): Promise<void> {
    try {
      await this.listModels(token ?? new vscode.CancellationTokenSource().token);
      this.changeEmitter.fire();
    } catch (error) {
      this.output.appendLine(`Model refresh failed: ${error instanceof Error ? error.message : String(error)}`);
    }
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
    this.changeEmitter.dispose();
    this.output.dispose();
  }

  private async listModels(token?: vscode.CancellationToken): Promise<OpenAiModel[]> {
    const response = await this.request("/models", undefined, token);
    const payload = (await response.json()) as OpenAiModelsResponse;
    return Array.isArray(payload.data) ? payload.data.filter((model) => typeof model.id === "string") : [];
  }

  private async request(path: string, body?: object, token?: vscode.CancellationToken): Promise<Response> {
    const configuration = vscode.workspace.getConfiguration("esiAiStudio");
    const baseUrl = normalizeBaseUrl(configuration.get<string>("baseUrl", "http://127.0.0.1:7010/v1"));
    const controller = new AbortController();
    const timeout = setTimeout(
      () => controller.abort(),
      configuration.get<number>("requestTimeoutMs", 120000),
    );
    const cancellation = token?.onCancellationRequested(() => controller.abort());

    try {
      const headers = new Headers({ Accept: body ? "text/event-stream" : "application/json" });
      if (body) {
        headers.set("Content-Type", "application/json");
      }

      const apiKey = await this.getApiKey();
      if (apiKey) {
        headers.set("Authorization", `Bearer ${apiKey}`);
      }

      const response = await fetch(`${baseUrl}${path}`, {
        method: body ? "POST" : "GET",
        headers,
        body: body ? JSON.stringify(body) : undefined,
        signal: controller.signal,
      });
      if (!response.ok) {
        const detail = await response.text();
        throw new Error(`Esi.AI Studio returned HTTP ${response.status}: ${detail || response.statusText}`);
      }

      return response;
    } finally {
      clearTimeout(timeout);
      cancellation?.dispose();
    }
  }

  private async getApiKey(): Promise<string | undefined> {
    const storedKey = await this.context.secrets.get(apiKeySecret);
    return storedKey || process.env.ESI_AI_STUDIO_API_KEY;
  }

  private toModel(model: OpenAiModel): EsiModel {
    const id = String(model.id);
    const configuration = vscode.workspace.getConfiguration("esiAiStudio");
    return {
      id,
      name: id,
      family: "esi-ai-studio",
      version: "1",
      maxInputTokens: configuration.get<number>("maxInputTokens", 32768),
      maxOutputTokens: configuration.get<number>("maxOutputTokens", 4096),
    };
  }

  private toOpenAiMessage(message: vscode.LanguageModelChatRequestMessage): OpenAiMessage {
    return {
      role: message.role === vscode.LanguageModelChatMessageRole.Assistant ? "assistant" : "user",
      content: this.messageText(message),
    };
  }

  private messageText(message: vscode.LanguageModelChatRequestMessage): string {
    return message.content
      .filter((part): part is vscode.LanguageModelTextPart => part instanceof vscode.LanguageModelTextPart)
      .map((part) => part.value)
      .join("");
  }
}

function normalizeBaseUrl(value: string): string {
  return value.replace(/\/+$/, "");
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