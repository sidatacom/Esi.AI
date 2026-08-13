# MCP SDK - Patterns and Architecture

## Base Protocol
- JSON-RPC 2.0 as message format
- 3-component architecture: **Server** (exposes capabilities), **Client** (consumes), **Host** (manages communication - VS Code/Copilot)

## Package @modelcontextprotocol/sdk
- Core: `@modelcontextprotocol/server` and `@modelcontextprotocol/client`
- Transports: `@modelcontextprotocol/node` (HTTP), Express, Hono
- Requires `zod` as peer dependency for schema validation
- Setup with ES Modules (`"type": "module"`)

## 3 Protocol Primitives
1. **Tools** - Executable functions the LLM can invoke with structured parameters
2. **Resources** - Data sources (databases, APIs, filesystem)
3. **Prompts** - Predefined templates for interactions

## Request Structure (JSON-RPC 2.0)
```json
{
  "jsonrpc": "2.0",
  "id": "<unique-id>",
  "method": "tools/call",
  "params": {
    "name": "<tool-name>",
    "arguments": {}
  }
}
```

## Tool Definition
Each tool requires:
- `name` - Unique identifier
- `title` - Human-readable name
- `description` - Functionality explanation
- `inputSchema` - JSON Schema (defined with Zod)
- Handler function - Executes logic and returns results

## Implemented Transport: Streamable HTTP

EsiMCP uses the MCP SDK's `StreamableHTTPServerTransport` over a direct local HTTP endpoint. Each VS Code workspace runs its own extension-host server on the configured loopback port, with MCP requests posted to `/mcp`. The server is not a separate stdio process and does not use an IPC bridge.

## Request and Session Lifecycle
1. **Initialization** - The client sends an `initialize` JSON-RPC request as an HTTP `POST` with `Content-Type: application/json`. The server creates an MCP SDK server and transport, performs the capability/version exchange, and returns an `Mcp-Session-Id`.
2. **Operation** - Subsequent `POST`, `GET`, and `DELETE` requests include `Mcp-Session-Id`. The transport handles JSON-RPC tool listing and calls for that session, while the extension-host request handler accesses the visible terminal and debug APIs.
3. **Termination** - The client closes the session with `DELETE`, or the transport closes it when the server shuts down. The extension removes the session and closes the associated MCP SDK server.
