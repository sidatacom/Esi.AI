# VS Code Extension as MCP Server

## Direct HTTP Extension Model

EsiMCP runs entirely in the VS Code extension host. On activation, `extension.ts` starts a loopback HTTP server for the current workspace. The server exposes the MCP endpoint at `/mcp` and invokes the terminal and debug handlers directly, so those handlers retain access to the VS Code APIs.

The extension contributes a VS Code MCP server definition provider. It returns the active endpoint URL for that extension host:

```text
http://127.0.0.1:<assigned-port>/mcp
```

By default, the OS assigns a distinct port to each VS Code window/workspace. The extension also binds the IPv6 loopback host on the same port. `esimcp.serverPort` can specify a fixed port when needed. An optional bearer token is checked through the `ESIMCP_SECRET` environment variable.

## Streamable HTTP Session Model

The server uses the MCP SDK `StreamableHTTPServerTransport`:

1. A client sends an initialization `POST` to `/mcp` with JSON content.
2. The server creates the MCP server and transport, connects them, and returns an `Mcp-Session-Id`.
3. The client sends later `POST`, `GET`, and `DELETE` requests with that session header.
4. The extension maps JSON-RPC requests directly to the registered terminal and debug tools.
5. Closing the session removes it from the workspace server and closes its MCP server.

## Client Configuration

VS Code discovers EsiMCP dynamically through the contributed definition provider. Do not add a static EsiMCP URL to `.vscode/mcp.json`; it would not track OS-assigned ports and could connect a window to another workspace's server.

## Existing MCP Servers in the Environment
1. **desktop-commander** (v0.2.38) - Command execution, files
2. **playwright-mcp** - Browser automation
3. **context7** - Contextual documentation
4. **chrome-devtools-mcp** (v0.20.2) - DevTools
