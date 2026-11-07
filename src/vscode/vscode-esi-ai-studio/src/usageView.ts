import * as vscode from "vscode";

export interface EsiAiStudioUsage {
  modelId: string;
  modelName: string;
  completedAt: number;
  durationMs: number;
  promptTokens?: number;
  completionTokens?: number;
  totalTokens?: number;
  decodeTokensPerSecond?: number;
  requestId: number;
  inferenceId?: string;
}

class UsageTreeItem extends vscode.TreeItem {
  public constructor(
    label: string,
    collapsibleState: vscode.TreeItemCollapsibleState,
    public readonly usage?: EsiAiStudioUsage,
    description?: string,
  ) {
    super(label, collapsibleState);
    this.description = description;
  }
}

export class EsiAiStudioUsageView
  implements vscode.TreeDataProvider<UsageTreeItem>, vscode.Disposable
{
  public static readonly viewId = "esiAiStudio.usage";

  private readonly changeEmitter = new vscode.EventEmitter<
    UsageTreeItem | undefined | null | void
  >();
  private readonly statusBarItem = vscode.window.createStatusBarItem(
    vscode.StatusBarAlignment.Right,
    90,
  );
  private readonly usageHistory: EsiAiStudioUsage[] = [];

  public readonly onDidChangeTreeData = this.changeEmitter.event;

  public constructor() {
    this.statusBarItem.command = "esiAiStudio.showUsage";
    this.statusBarItem.tooltip = "Open Esi.AI Studio usage details";
  }

  public getTreeItem(element: UsageTreeItem): vscode.TreeItem {
    return element;
  }

  public getChildren(element?: UsageTreeItem): UsageTreeItem[] {
    if (element?.usage) {
      return this.getUsageDetails(element.usage);
    }
    if (element) {
      return [];
    }

    if (this.usageHistory.length === 0) {
      return [
        new UsageTreeItem(
          "Noch keine abgeschlossene Esi.AI-Antwort",
          vscode.TreeItemCollapsibleState.None,
        ),
      ];
    }

    return this.usageHistory.map((usage, index) => {
      const label =
        index === 0
          ? `Letzte Antwort · ${usage.modelName}`
          : `${this.formatTime(usage.completedAt)} · ${usage.modelName}`;
      const description = `${this.formatTokens(usage.promptTokens)} in · ${this.formatTokens(usage.completionTokens)} out · ${this.formatRate(usage.decodeTokensPerSecond)} Tok/s`;
      return new UsageTreeItem(
        label,
        index === 0
          ? vscode.TreeItemCollapsibleState.Expanded
          : vscode.TreeItemCollapsibleState.Collapsed,
        usage,
        description,
      );
    });
  }

  public recordUsage(usage: EsiAiStudioUsage): void {
    this.usageHistory.unshift(usage);
    this.usageHistory.length = Math.min(this.usageHistory.length, 20);
    this.statusBarItem.text = `$(pulse) Esi.AI · ${this.formatTokens(usage.completionTokens)} out · ${this.formatRate(usage.decodeTokensPerSecond)} Tok/s`;
    this.statusBarItem.tooltip = `${usage.modelName}\n${this.formatTokens(usage.promptTokens)} input, ${this.formatTokens(usage.completionTokens)} output tokens\nOutput tokens pro Sekunde (Backend): ${this.formatRate(usage.decodeTokensPerSecond)} Tok/s`;
    this.statusBarItem.show();
    this.changeEmitter.fire(undefined);
  }

  public dispose(): void {
    this.changeEmitter.dispose();
    this.statusBarItem.dispose();
  }

  private getUsageDetails(usage: EsiAiStudioUsage): UsageTreeItem[] {
    const endToEndRate =
      usage.totalTokens !== undefined && usage.durationMs > 0
        ? (usage.totalTokens * 1000) / usage.durationMs
        : undefined;

    return [
      this.createDetail("Modell", usage.modelName),
      this.createDetail(
        "Abgeschlossen",
        this.formatDateTime(usage.completedAt),
      ),
      this.createDetail("Input tokens", this.formatTokens(usage.promptTokens)),
      this.createDetail(
        "Output tokens",
        this.formatTokens(usage.completionTokens),
      ),
      this.createDetail("Total tokens", this.formatTokens(usage.totalTokens)),
      this.createDetail(
        "Output tokens pro Sekunde (Backend)",
        `${this.formatRate(usage.decodeTokensPerSecond)} Tok/s`,
      ),
      this.createDetail(
        "Tokens pro Sekunde (Client, Ende-zu-Ende)",
        `${this.formatRate(endToEndRate)} Tok/s`,
      ),
      this.createDetail(
        "Requestdauer",
        `${(usage.durationMs / 1000).toFixed(2)} s`,
      ),
      this.createDetail(
        "Copilot-Credits",
        "Nicht vom Esi.AI-Provider/API bereitgestellt",
      ),
      this.createDetail("Request-ID", String(usage.requestId)),
      this.createDetail("Inference-ID", usage.inferenceId ?? "n/a"),
      this.createDetail("Model-ID", usage.modelId),
    ];
  }

  private createDetail(label: string, value: string): UsageTreeItem {
    const item = new UsageTreeItem(
      label,
      vscode.TreeItemCollapsibleState.None,
      undefined,
      value,
    );
    item.tooltip = value;
    return item;
  }

  private formatTokens(value: number | undefined): string {
    return value === undefined ? "n/a" : Math.round(value).toLocaleString();
  }

  private formatRate(value: number | undefined): string {
    return value === undefined || !Number.isFinite(value)
      ? "n/a"
      : value.toFixed(1);
  }

  private formatTime(value: number): string {
    return new Date(value).toLocaleTimeString();
  }

  private formatDateTime(value: number): string {
    return new Date(value).toLocaleString();
  }
}
