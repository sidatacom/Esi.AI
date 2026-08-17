import * as crypto from "node:crypto";
import * as http from "node:http";
import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StreamableHTTPServerTransport } from "@modelcontextprotocol/sdk/server/streamableHttp.js";
import { CallToolRequestSchema, InitializeRequestSchema, ListToolsRequestSchema } from "@modelcontextprotocol/sdk/types.js";
import { isAllowedHttpRequest } from "./http-security.js";
import { normalizeBindHosts, normalizePort } from "./config.js";
import { createMcpRequestHandler } from "./mcp/server.js";
import type { SessionManager } from "./terminal/session-manager.js";
import type { DebugManager } from "./debug/manager.js";

type RequestHandler = (method: string, params?: unknown) => Promise<unknown>;
type ClientSession = {
  server: Server;
  transport: StreamableHTTPServerTransport;
  closePromise?: Promise<void>;
  serverClosePromise?: Promise<void>;
};
const MAX_BODY_BYTES = 1_000_000;
const MCP_PATH = "/mcp";

export interface McpHttpServerOptions {
  port?: number;
  bindHost?: string | string[];
  timeoutInSeconds?: number;
  requestHandler?: RequestHandler;
}

export interface McpHttpServer {
  readonly servers: http.Server[];
  close(): Promise<void>;
}

async function readBody(request: http.IncomingMessage): Promise<unknown> {
  let size = 0;
  const chunks: Buffer[] = [];
  for await (const chunk of request) {
    const buffer = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk);
    size += buffer.length;
    if (size > MAX_BODY_BYTES) throw new Error("Request body exceeds 1 MB");
    chunks.push(buffer);
  }
  return JSON.parse(Buffer.concat(chunks).toString("utf8") || "{}");
}

function createMcpSdkServer(requestHandler: RequestHandler): Server {
  const server = new Server({ name: "EsiMCP", version: "1.0.7" }, { capabilities: { tools: {} } });
  server.setRequestHandler(InitializeRequestSchema, (request) => requestHandler("initialize", request.params) as never);
  server.setRequestHandler(ListToolsRequestSchema, () => requestHandler("tools/list") as never);
  server.setRequestHandler(CallToolRequestSchema, (request) => requestHandler("tools/call", request.params) as never);
  return server;
}

function respondError(response: http.ServerResponse, status: number, message: string): void {
  if (response.destroyed || response.writableEnded) return;
  if (response.headersSent) { response.end(); return; }
  response.writeHead(status, { "content-type": "application/json" });
  response.end(JSON.stringify({ error: message }));
}

export async function startMcpHttpServer(options: McpHttpServerOptions = {}): Promise<McpHttpServer> {
  const port = normalizePort(options.port);
  const hosts = normalizeBindHosts(options.bindHost);
  const sessions = new Map<string, ClientSession>();
  let initializationPromise: Promise<ClientSession> | undefined;
  const requestHandler = options.requestHandler ?? (() => Promise.reject(new Error("MCP request handler is unavailable")));
  const handleRequest = async (request: http.IncomingMessage, response: http.ServerResponse): Promise<void> => {
    if (request.url?.split("?", 1)[0] !== MCP_PATH) { respondError(response, 404, "Not found"); return; }
    if (!isAllowedHttpRequest(request)) { respondError(response, 403, "Forbidden"); return; }
    if (!request.method || !["POST", "GET", "DELETE"].includes(request.method)) { respondError(response, 405, "Method not allowed"); return; }
    const header = request.headers["mcp-session-id"];
    const sessionId = typeof header === "string" ? header : undefined;
    let session = sessionId ? sessions.get(sessionId) : undefined;
    if (sessionId && !session) { respondError(response, 404, "Unknown MCP session"); return; }
    if (!sessionId && request.method !== "POST") { respondError(response, 400, "Mcp-Session-Id header is required"); return; }
    if (request.method === "POST" && !String(request.headers["content-type"] ?? "").toLowerCase().startsWith("application/json")) {
      respondError(response, 415, "Content-Type must be application/json");
      return;
    }
    if (!session) {
      initializationPromise ??= (async () => {
        const server = createMcpSdkServer(requestHandler);
        const transport = new StreamableHTTPServerTransport({ sessionIdGenerator: () => crypto.randomUUID() });
        const createdSession: ClientSession = { server, transport };
        const closeServer = (): Promise<void> => createdSession.serverClosePromise ??= Promise.resolve().then(() => server.close());
        transport.onclose = () => {
          if (transport.sessionId) sessions.delete(transport.sessionId);
          void closeServer();
        };
        try {
          await server.connect(transport);
          return createdSession;
        } catch (error) {
          await closeServer();
          throw error;
        }
      })().finally(() => { initializationPromise = undefined; });
      session = await initializationPromise;
    }
    const body = request.method === "POST" ? await readBody(request) : undefined;
    await session.transport.handleRequest(request, response, body);
    if (session.transport.sessionId) sessions.set(session.transport.sessionId, session);
  };
  const servers = hosts.map((host) => {
    const server = http.createServer((request, response) => { void handleRequest(request, response).catch((error) => respondError(response, 400, error instanceof Error ? error.message : String(error))); });
    server.timeout = 0;
    return server;
  });
  await Promise.all(servers.map((server, index) => new Promise<void>((resolve, reject) => {
    const onError = (error: Error) => { server.off("listening", onListening); reject(error); };
    const onListening = () => { server.off("error", onError); resolve(); };
    server.once("error", onError);
    server.once("listening", onListening);
    server.listen(port, hosts[index]);
  })));
  let closePromise: Promise<void> | undefined;
  return {
    servers,
    close: () => closePromise ??= (async () => {
      await Promise.all([...sessions.values()].map(async (session) => {
        if (!session.closePromise) {
          session.closePromise = (async () => {
            if (session.transport.sessionId) sessions.delete(session.transport.sessionId);
            try { await session.transport.close(); }
            finally { session.serverClosePromise ??= session.server.close(); await session.serverClosePromise; }
          })();
        }
        await session.closePromise;
      }));
      await Promise.all(servers.map((server) => new Promise<void>((resolve) => {
        if (!server.listening) { resolve(); return; }
        server.close(() => resolve());
      })));
    })(),
  };
}

export function createConfiguredMcpRequestHandler(sessionManager: SessionManager, debugManager: DebugManager): RequestHandler {
  return createMcpRequestHandler(sessionManager, debugManager);
}
