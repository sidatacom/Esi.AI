# VS Code Extension as MCP Server

## Direct HTTP Extension Model

EsiMCP runs entirely in the VS Code extension host. On activation, `extension.ts` reads `esimcp.serverPort` and starts a loopback HTTP server for the current workspace. The server exposes the MCP endpoint at `/mcp` and invokes the terminal and debug handlers directly, so those handlers retain access to the VS Code APIs.

External MCP clients connect directly to the configured loopback URL:

```text
http://127.0.0.1:<configured esimcp.serverPort>/mcp
```

The default port is `3002`; the extension also binds the IPv6 loopback host when configured. Separate VS Code workspaces must use distinct ports. An optional bearer token is checked through the `ESIMCP_SECRET` environment variable.

## Streamable HTTP Session Model

The server uses the MCP SDK `StreamableHTTPServerTransport`:

1. A client sends an initialization `POST` to `/mcp` with JSON content.
2. The server creates the MCP server and transport, connects them, and returns an `Mcp-Session-Id`.
3. The client sends later `POST`, `GET`, and `DELETE` requests with that session header.
4. The extension maps JSON-RPC requests directly to the registered terminal and debug tools.
5. Closing the session removes it from the workspace server and closes its MCP server.

## Client Configuration

Configure the VS Code/Copilot client with the direct HTTP URL and the port selected in the VS Code workspace settings. The configuration is stored in `.vscode/mcp.json`.

## Existing MCP Servers in the Environment
1. **desktop-commander** (v0.2.38) - Command execution, files
2. **playwright-mcp** - Browser automation
3. **context7** - Contextual documentation
4. **chrome-devtools-mcp** (v0.20.2) - DevTools
