---
name: msaccess
description: "MANDATORY for EsiMCP Microsoft Access operations: use when discovering or executing MS-Access-mcp tools, reading Access resources/prompts, configuring the Access MCP server, or troubleshooting Access database connectivity."
---

# Microsoft Access MCP

EsiMCP proxies the configured upstream `MS-Access-mcp` stdio server. Use it only through the EsiMCP Access tools or the separately exposed MCP resource/prompt methods.

## Tool Workflow

1. Call `msaccess_list_commands` first. Treat the returned upstream tool names, descriptions, and JSON schemas as authoritative; the catalog can vary by server version.
2. Call `msaccess_execute_command` with the exact upstream `commandId` and JSON `arguments` from that schema. Do not invent tool names or argument fields.
3. For resources and prompts, use the matching MCP methods (`resources/list`, `resources/templates/list`, `resources/read`, `prompts/list`, or `prompts/get`). They are not `msaccess_execute_command` command IDs.
4. Check the response for an MCP error before relying on a mutation or query result. For a long request, respect the configured request timeout rather than retrying a write blindly.

## Database Safety

- Treat the active database path and selected database as critical context. Confirm the intended database before queries that mutate schema/data, run VBA/macros, execute DoCmd actions, or affect security/printing.
- Read schemas, table metadata, and relevant records before making a change. Prefer the narrowest operation that satisfies the request.
- Do not delete, overwrite, bulk-update, migrate, or execute code against a database without explicit user authorization. When authorized, describe the target and scope before executing and verify the resulting state afterward.
- Never assume a failed/timed-out write did not take effect. Inspect the database state before deciding whether to retry.

## Server And Platform Requirements

- The client starts the stdio server lazily on the first Access call. By default it runs the bundled project with `dotnet run --project <project> --no-launch-profile`; explicit server arguments replace the defaults.
- The upstream server requires Windows, Microsoft Access, and a compatible .NET runtime for COM/DAO operations. A Linux workspace can author or inspect configuration, but cannot validate Access COM/DAO execution locally.
- Relevant VS Code settings are `esimcp.msAccessServerCommand`, `esimcp.msAccessServerArguments`, `esimcp.msAccessServerProject`, `esimcp.msAccessServerWorkingDirectory`, `esimcp.msAccessDatabasePath`, and `esimcp.msAccessTimeoutMs` (default 120000 ms).
- `esimcp.msAccessDatabasePath`, when set, is passed to the server as `ACCESS_DATABASE_PATH`. Do not print credentials or sensitive database contents in logs or summaries.
- If startup fails, check the configured executable, project path, working directory, Windows/Access/.NET prerequisites, and server stderr before retrying.
