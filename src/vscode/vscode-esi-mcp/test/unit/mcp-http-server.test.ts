import * as http from "node:http";
import { describe, expect, it, vi } from "vitest";
vi.mock("../../src/mcp/server.js", () => ({ createMcpRequestHandler: vi.fn() }));
import { startMcpHttpServer } from "../../src/mcp-http-server.js";

function request(port: number, method: string, body?: unknown, headers: Record<string, string> = {}): Promise<{ status: number; headers: http.IncomingHttpHeaders; body: string }> {
  return new Promise((resolve, reject) => {
    const request = http.request({ hostname: "127.0.0.1", port, path: "/mcp", method, headers: { accept: "application/json, text/event-stream", ...(body === undefined ? {} : { "content-type": "application/json", "content-length": Buffer.byteLength(JSON.stringify(body)) }), ...headers } }, (response) => {
      const chunks: Buffer[] = [];
      response.on("data", (chunk) => chunks.push(Buffer.from(chunk)));
      response.on("end", () => resolve({ status: response.statusCode ?? 0, headers: response.headers, body: Buffer.concat(chunks).toString("utf8") }));
    });
    request.on("error", reject);
    if (body !== undefined) request.write(JSON.stringify(body));
    request.end();
  });
}

describe("direct MCP HTTP server", () => {
  it("initializes, reuses sessions, and rejects invalid session requests", async () => {
    const port = 31000 + Math.floor(Math.random() * 1000);
    const server = await startMcpHttpServer({ port, bindHost: "127.0.0.1", requestHandler: async (method) => {
      if (method === "initialize") return { protocolVersion: "2025-03-26", capabilities: { tools: {} }, serverInfo: { name: "test", version: "1" } };
      if (method === "tools/list") return { tools: [] };
      throw new Error(`Unexpected method: ${method}`);
    } });
    try {
      expect(server.servers[0].timeout).toBe(0);
      const missingSessionGet = await request(port, "GET");
      expect(missingSessionGet.status).toBe(400);
      const unknownSession = await request(port, "POST", { jsonrpc: "2.0", id: 1, method: "initialize", params: {} }, { "mcp-session-id": "unknown" });
      expect(unknownSession.status).toBe(404);
      const wrongContentType = await request(port, "POST", { jsonrpc: "2.0", id: 1, method: "initialize", params: {} }, { "content-type": "text/plain" });
      expect(wrongContentType.status).toBe(415);
      const initialize = await request(port, "POST", { jsonrpc: "2.0", id: 1, method: "initialize", params: { protocolVersion: "2025-03-26", capabilities: {}, clientInfo: { name: "test", version: "1" } } });
      expect(initialize.status).toBe(200);
      const sessionId = initialize.headers["mcp-session-id"];
      expect(typeof sessionId).toBe("string");
      const tools = await request(port, "POST", { jsonrpc: "2.0", id: 2, method: "tools/list", params: {} }, { "mcp-session-id": sessionId as string });
      expect(tools.status).toBe(200);
      expect(JSON.parse(tools.body.split("data: ")[1].trim()).result.tools).toEqual([]);
    } finally {
      await Promise.all([server.close(), server.close()]);
    }
  });

  it("allows independent server instances to initialize simultaneously", async () => {
    const firstPort = 32000 + Math.floor(Math.random() * 2000);
    let secondPort = 32000 + Math.floor(Math.random() * 2000);
    while (secondPort === firstPort) secondPort = 32000 + Math.floor(Math.random() * 2000);
    const requestHandler = async (method: string) => {
      if (method === "initialize") return { protocolVersion: "2025-03-26", capabilities: { tools: {} }, serverInfo: { name: "test", version: "1" } };
      throw new Error("Unexpected method: " + method);
    };
    const servers = await Promise.all([
      startMcpHttpServer({ port: firstPort, bindHost: "127.0.0.1", requestHandler }),
      startMcpHttpServer({ port: secondPort, bindHost: "127.0.0.1", requestHandler }),
    ]);
    try {
      const initializeRequest = { jsonrpc: "2.0", id: 1, method: "initialize", params: { protocolVersion: "2025-03-26", capabilities: {}, clientInfo: { name: "test", version: "1" } } };
      const [firstInitialize, secondInitialize] = await Promise.all([request(firstPort, "POST", initializeRequest), request(secondPort, "POST", initializeRequest)]);
      expect(firstInitialize.status).toBe(200);
      expect(secondInitialize.status).toBe(200);
      expect(typeof firstInitialize.headers["mcp-session-id"]).toBe("string");
      expect(typeof secondInitialize.headers["mcp-session-id"]).toBe("string");
    } finally {
      await Promise.all(servers.map((server) => server.close()));
    }
  });
});
