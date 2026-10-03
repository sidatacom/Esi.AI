---
name: msaccess
description: "MANDATORY for EsiMCP-integrated Microsoft Access operations: use when discovering or executing Access tools, reading Access resources/prompts, configuring EsiMCP's upstream process, or troubleshooting Access database connectivity."
---

# Microsoft Access through EsiMCP

Use the EsiMCP connection for all Access operations. The configured upstream `MS-Access-mcp` stdio process is an internal implementation detail managed by EsiMCP; do not invoke a separate Access MCP server directly.

## Tool Workflow

1. Call the VS Code tool-bridge alias `mcp_esimcp_msaccess_list_commands` first; it invokes EsiMCP's MCP protocol tool ID `msaccess_list_commands`. Treat the returned upstream tool names, descriptions, and JSON schemas as authoritative; the catalog can vary by server version.
2. Call the VS Code tool-bridge alias `mcp_esimcp_msaccess_execute_command`, corresponding to EsiMCP's MCP protocol tool ID `msaccess_execute_command`, with the exact upstream `commandId` and optional object-valued JSON `arguments` from that schema. `commandId` names an upstream operation, not an EsiMCP protocol tool ID or a callable VS Code tool. Do not invent tool names or argument fields.
3. For resources and prompts, use the matching MCP protocol methods (`resources/list`, `resources/templates/list`, `resources/read`, `prompts/list`, or `prompts/get`) on the same EsiMCP connection when the client exposes them. They are not Access tools and do not require a separate server connection.
4. Check the response for an MCP error before relying on a mutation or query result. For a long request, respect the configured request timeout rather than retrying a write blindly.

## Database Safety

- Treat the active database path and selected database as critical context. Confirm the intended database before queries that mutate schema/data, run VBA/macros, execute DoCmd actions, or affect security/printing.
- Read schemas, table metadata, and relevant records before making a change. Prefer the narrowest operation that satisfies the request.
- Do not delete, overwrite, bulk-update, migrate, or execute code against a database without explicit user authorization. When authorized, describe the target and scope before executing and verify the resulting state afterward.
- Never assume a failed/timed-out write did not take effect. Inspect the database state before deciding whether to retry.

## Server And Platform Requirements

- EsiMCP starts the internal stdio process lazily on the first Access tool or protocol request. By default it runs the bundled project with `dotnet run --project <project> --no-launch-profile`; explicit server arguments replace the defaults. Do not start this process separately.
- EsiMCP's internal upstream process requires Windows, Microsoft Access, and a compatible .NET runtime for COM/DAO operations. A Linux workspace can author or inspect configuration, but cannot validate Access COM/DAO execution locally.
- Relevant VS Code settings for EsiMCP's upstream process are `esimcp.msAccessServerCommand`, `esimcp.msAccessServerArguments`, `esimcp.msAccessServerProject`, `esimcp.msAccessServerWorkingDirectory`, `esimcp.msAccessDatabasePath`, and `esimcp.msAccessTimeoutMs` (default 120000 ms).
- `esimcp.msAccessDatabasePath`, when set, is passed to the server as `ACCESS_DATABASE_PATH`. Do not print credentials or sensitive database contents in logs or summaries.
- If startup fails, check the configured executable, project path, working directory, Windows/Access/.NET prerequisites, and server stderr before retrying.
