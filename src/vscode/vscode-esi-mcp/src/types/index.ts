// --- Session Types ---

export interface TerminalSessionConfig {
  name: string;
  cwd?: string;
  env?: Record<string, string>;
  shell?: string;
  agentId?: string;
}

export interface TerminalSessionInfo {
  sessionId: string;
  name: string;
  cwd: string;
  agentId?: string;
  isActive: boolean;
  createdAt: number;
  lastCommandAt?: number;
  outputLineCount: number;
}

export interface OutputBuffer {
  lines: string[];
  totalLinesReceived: number;
  lastReadIndex: number;
  maxLines: number;
}

export interface CommandExecution {
  commandId: string;
  command: string;
  startedAt: number;
  completedAt?: number;
  exitCode?: number;
  timedOut: boolean;
  outputStartLine: number;
  outputEndLine?: number;
}

export interface ExecuteResult {
  sessionId: string;
  commandId: string;
  output: string;
  exitCode: number | null;
  timedOut: boolean;
  durationMs: number;
}

export interface ReadOutputResult {
  sessionId: string;
  lines: string[];
  totalLines: number;
  readFrom: number;
  readCount: number;
  remaining: number;
  isComplete: boolean;
}

// --- MCP Tool Input Types ---

export interface TerminalCreateInput {
  name: string;
  cwd?: string;
  env?: Record<string, string>;
  shell?: string;
  agentId?: string;
}

export interface TerminalExecuteInput {
  sessionId: string;
  command: string;
  timeoutMs?: number;
  waitForCompletion?: boolean;
}

export interface TerminalReadOutputInput {
  sessionId: string;
  offset?: number;
  lines?: number;
}

export interface TerminalListInput {
  agentId?: string;
}

export interface TerminalCloseInput {
  sessionId: string;
}

export interface TerminalSendInputInput {
  sessionId: string;
  input: string;
  pressEnter?: boolean;
}

// --- Security Types ---

export interface SecurityConfig {
  allowedCommands?: string[];
  blockedCommands: string[];
  allowedDirectories: string[];
  defaultTimeoutMs: number;
  maxConcurrentSessions: number;
  includeAllTerminals: boolean;
  maxOutputLines: number;
  idleTimeoutMs: number;
}

// --- MCP Response Helpers ---

export interface McpToolResponse {
  content: Array<{ type: "text"; text: string }>;
  isError?: boolean;
}

