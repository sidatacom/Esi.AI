# EsiMCP

[![npm version](https://img.shields.io/npm/v/vscode-esi-mcp.svg)](https://npmjs.org/package/vscode-esi-mcp)

Direct local HTTP MCP server for visible VS Code terminals, debugging, C# Dev Kit commands, and Microsoft Access MCP operations.

## Key Features

- **Visible Terminals**: Commands run in real VSCode terminal tabs, not hidden processes. You see everything in real time.
- **Session Reuse**: The `run` tool automatically reuses idle sessions, creating new terminals only when needed.
- **Long-Running Support**: Fire-and-forget execution with `waitForCompletion: false`, then poll output incrementally with `read`.
- **Subagent Isolation**: Tag sessions with `agentId` to keep parallel agent workloads separated.
- **Debug Lifecycle**: Start, inspect, stop, restart, and wait for readiness or debug events through allowlisted commands.
- **Microsoft Access**: Proxy the upstream `MS-Access-mcp` server with 360 tools, 13 resources, 9 resource templates, and 9 prompts.

## Requirements

- VS Code 1.93+ (for Shell Integration API)
- Node.js 20+

The complete Esi.AI Studio debug lifecycle, including the virtual readiness,
restart, and stop commands, is documented in
[`docs/esimcp-debug-lifecycle.md`](../../../docs/esimcp-debug-lifecycle.md).

## How It Works

EsiMCP is implemented as one direct HTTP MCP server per VS Code workspace inside the VS Code extension host. The extension starts the server on the configured loopback port and dispatches MCP requests directly to the terminal and debug tools.

## Getting Started

Add to your `.vscode/mcp.json`:

```json
{
  "servers": {
    "EsiMCP": {
      "type": "http",
      "url": "http://127.0.0.1:<configured esimcp.serverPort>/mcp",
      "headers": {
        "Authorization": "Bearer ${env:ESIMCP_SECRET}"
      }
    }
  }
}
```

After installation, ask Copilot to run `ls -la` in the terminal.

## Tools

### VS Code Terminal Commands

| Tool | Description |
|------|-------------|
| `vscode_terminal_list_commands` | List the available terminal commands. |
| `vscode_terminal_execute_command` | Execute a terminal command by `commandId` and `arguments`. |

Available command IDs: `terminal.run`, `terminal.create`, `terminal.execute`, `terminal.read`, `terminal.list`, `terminal.close`, and `terminal.input`.
Each entry returned by `vscode_terminal_list_commands` also includes `argumentsSchema` with required fields, types, defaults, and descriptions.

### VS Code Debug Commands

| Tool | Description |
|------|-------------|
| `vscode_debug_list_commands` | List the available debug commands. |
| `vscode_debug_execute_command` | Execute a debug command by `commandId` and `arguments`. |

Available command IDs include `debug.start`, `debug.active.session`, `debug.stop`, `debug.restart`, and `debug.check.host.readyness`.

`debug.restart` stops the active session and starts it again with a newly observed VS Code session ID. It accepts an optional `rebuildTaskName` that must exactly match a task from `tasks.json`, for example `{ "rebuildTaskName": "build" }`.
Each entry returned by `vscode_debug_list_commands` also includes `argumentsSchema` with required fields, types, defaults, and descriptions.

### C# Dev Kit and Microsoft Access

| Tool | Description |
|------|-------------|
| `csharp_devkit_list_commands` | List the commands declared by the installed Microsoft C# Dev Kit, including ID, title, keyboard shortcuts, menu contexts, and registration status. |
| `csharp_devkit_execute_command` | Execute one command from the installed C# Dev Kit command manifest. The `commandId` must be declared by `ms-dotnettools.csdevkit`; optional arguments are forwarded to VS Code. |
| `msaccess_list_commands` | List the tools exposed by the configured `MS-Access-mcp` stdio server, including the upstream Access schemas. |
| `msaccess_execute_command` | Execute one upstream Access tool by `commandId`, forwarding its JSON arguments. |

The C# Dev Kit tools expose only commands declared by the installed extension. Arbitrary VS Code command IDs are rejected.

The Access wrapper also forwards the upstream MCP `resources/list`, `resources/templates/list`, `resources/read`, `prompts/list`, and `prompts/get` methods. Use the upstream list responses as the source of truth for exact schemas and arguments. These are MCP resource and prompt methods, not additional `msaccess_execute_command` IDs.

The Access server provides:

- 360 tools covering database lifecycle, tables, fields, queries, relationships, forms, reports, DAO recordsets, VBA, macros, metadata, DoCmd operations, security, printing, controls, dependencies, and pyodbc compatibility.
- 13 static resources and 9 URI templates for schema, table data, controls, query SQL, VBA code, properties, indexes, and relationships.
- 9 prompt templates for schema analysis, query optimization, debugging, normalization, data dictionaries, migration, performance, security, and index optimization.

The upstream server requires Windows, Microsoft Access, and a compatible .NET runtime for COM/DAO operations. The EsiMCP submodule is located at `origins/brickly26/MS-Access-mcp`.
The C# Dev Kit list includes `argumentsSchema` as a positional array. The extension manifest does not publish command-specific parameter metadata for declared commands, so those entries are intentionally generic; the virtual active-session, stop, and restart commands accept no arguments.

For a new Esi.Web launch, dispatch `vscode_debug_execute_command` with `debug.start` and
`debug.check.host.readyness` in the same parallel tool-call batch. The backend owns
`debug.start`; the frontend must invoke `debug.check.host.readyness` immediately and keep
the blocking call open until it returns.
The readiness tool reads live shell execution output, not terminal scrollback. A result of
`{ "ready": true }` confirms the readiness string was observed. `{ "ready": false }` means
only that the timeout expired. `Canceled: Canceled` is external cancellation, not a timeout;
stop the workflow and do not continue to browser actions.

## Usage Patterns

### Simple Command

The `run` tool handles everything — creates a terminal if needed, executes, and returns clean output:

```
> Run npm test
```

```
$ npm test
PASS src/utils.test.ts (3 tests)
PASS src/index.test.ts (5 tests)

[exit: 0 | 1243ms | session-abc123]
```

### Long-Running Process

For builds, deployments, or any command that takes a while:

pm run build` without waiting, then check progress
```
> Start `npm run build` without waiting, then check progress
```

The agent will:
1. Call `run` with `waitForCompletion: false` — returns immediately
2. Call `read` with `offset: -10` to check the last 10 lines
3. Repeat until the process completes

### Interactive Commands

For commands that need user input:

```
> Run npm init and answer the prompts
```

pm init`
The agent will:
1. Call `run` with `npm init`
2. Call `read` to see the prompt
3. Call `input` to send the answer

### Parallel Agents

Subagents can work in isolated terminals using `agentId`:

```
> Have one agent run tests while another runs the linter
```

Each subagent gets its own terminal tagged with its `agentId`, preventing output from mixing.

## Configuration

The extension reads configuration from VS Code settings under `esimcp.*`. Use distinct `esimcp.serverPort` values for separate workspaces. When `ESIMCP_SECRET` is configured, clients must send the matching bearer token:

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `esimcp.maxConcurrentSessions` | number | 10 | Maximum concurrent terminal sessions |
| `esimcp.defaultTimeoutMs` | number | 30000 | Default command timeout in ms |
| `esimcp.maxOutputLines` | number | 10000 | Max lines kept in output buffer per session |
| `esimcp.idleTimeoutMs` | number | 300000 | Close idle sessions after this many ms (0 = disabled) |
| `esimcp.blockedCommands` | string[] | `["rm -rf /"]` | Commands that will be rejected |
| `esimcp.debugConfigurationName` | string | empty | Default VS Code launch configuration used by `debug.start` |
| `esimcp.debugReadyString` | string | `Now ready on:` | Text observed in live VS Code shell output by `debug.check.host.readyness` |
| `esimcp.debugHostReadinessTimeoutSeconds` | number | 60 | Timeout for `debug.check.host.readyness` in seconds |
| `esimcp.msAccessServerCommand` | string | `dotnet` | Executable used to start the Access MCP server |
| `esimcp.msAccessServerArguments` | string[] | `[]` | Explicit server arguments; replaces the default `dotnet run` arguments when set |
| `esimcp.msAccessServerProject` | string | `origins/brickly26/MS-Access-mcp/MS.Access.MCP.Official/MS.Access.MCP.Official.csproj` | Access MCP project used by the default command |
| `esimcp.msAccessServerWorkingDirectory` | string | workspace root | Working directory for the Access MCP process |
| `esimcp.msAccessDatabasePath` | string | empty | Optional path passed through `ACCESS_DATABASE_PATH` |
| `esimcp.msAccessTimeoutMs` | number | 120000 | Timeout for Access MCP requests in milliseconds |

For debugging, an explicit `configurationName` supplied to `debug.start` takes precedence over this setting. If neither is supplied, `testName` is used when present; otherwise EsiMCP creates a debug configuration from `fileFullPath`. `debug.start` reports debugger attachment; use `debug.check.host.readyness` separately for host readiness.

Use `debug.wait.for.event` to wait for debugger state changes. A paused exception event includes DAP-provided exception details when the adapter supports `exceptionInfo`; the agent must resume a paused host before starting browser or HTTP validation.

Use `debug.stop` to stop the active debug session.

## Recommended: Set as Preferred Tool

Copilot agents may have built-in command execution tools. Prefer the EsiMCP tools so command output remains visible in VS Code and associated with the correct terminal session.

Use the following guidance in the project's Copilot instructions:

```markdown
## Terminal Execution

Prefer `mcp_esimcp_vscode_terminal_execute_command` with the `terminal.*` command IDs over other command execution tools.
EsiMCP runs commands in visible VSCode terminal tabs where the user can see output in real time.
Use another command tool only for simple, non-interactive operations when EsiMCP is unavailable.

For commands that may take longer than 30 seconds or produce large amounts of output (builds, test suites,
deployments, installs), use the pull mode pattern:
1. Call `mcp_esimcp_vscode_terminal_execute_command` with `commandId: "terminal.run"` and `arguments.waitForCompletion: false` to launch the command without blocking.
2. Call `mcp_esimcp_vscode_terminal_execute_command` with `commandId: "terminal.read"` and `arguments.offset: -10` to check the last 10 lines of output.
3. Repeat step 2 until you see the command has finished (look for exit messages, prompts, or "Done").
4. Report the final result to the user.

This prevents conversation timeouts and lets the user watch progress in the terminal in real time.
```

**Why this matters:**

| | Built-in Bash | EsiMCP MCP |
|---|---|---|
| Output visibility | Embedded in chat, hard to scroll | Visible in VSCode terminal tab |
| Real-time feedback | User sees nothing until command finishes | User watches output live |
| Long-running commands | Blocks the conversation until timeout | Fire-and-forget + polling |
| Session state | Each command is isolated | Persistent sessions with history |
| Interactive commands | Not supported | Send input to prompts/REPLs |

## Development: Updating the Extension

VSCode aggressively caches extensions in memory. When developing locally, `code --install-extension` and even "Developer: Reload Window" may **not** reload your changes. Use this workflow:

### Quick update (no restart needed)

After modifying source files, build and copy directly into the installed extension directory:

```bash
cd /path/to/vscode-esi-mcp
npm run build
cp dist/extension.js ~/.vscode/extensions/sidatacom.vscode-esi-mcp-<version>/dist/extension.js
```

Then run **"Developer: Reload Window"** (`Ctrl+Shift+P`).

### Full reinstall (when quick update doesn't work)

If VSCode still uses old code:

```bash
# 1. Uninstall and remove all copies
code --uninstall-extension sidatacom.vscode-esi-mcp
rm -rf ~/.vscode/extensions/sidatacom.vscode-esi-mcp-*

# 2. Check for ghost entries with old publisher names
# Look in ~/.vscode/extensions/extensions.json for stale entries
# Remove any stale entries with old publisher IDs

# 3. Close VSCode completely (not just reload)

# 4. Rebuild and install
npm run build
npx vsce package --allow-missing-repository
code --install-extension vscode-esi-mcp-<version>.vsix --force

# 5. Open VSCode
```

### Verify the correct version is loaded

```bash
# Check which extension directories exist
ls ~/.vscode/extensions/ | grep esi-mcp

# Verify your changes are in the installed extension
grep "YOUR_UNIQUE_STRING" ~/.vscode/extensions/sidatacom.vscode-esi-mcp-*/dist/extension.js

# Compare checksums
md5sum dist/extension.js ~/.vscode/extensions/sidatacom.vscode-esi-mcp-*/dist/extension.js
```

## Large Output Handling

When `read` returns output that exceeds the MCP client's token limit, the system automatically saves the full output to a temporary JSON file and returns the file path in the error message.

To extract the relevant content:

```bash
# Get the last 50 lines (most relevant for status)
tail -50 /path/to/saved/file.txt

# Or parse the JSON to extract the text content
python3 -c "import json; data=json.load(open('/path/to/file.txt')); print(data[0]['text'][-2000:])"
```

The file format is JSON: `[{"type": "text", "text": "..."}]`

This commonly happens with commands that produce heavy TUI output (progress bars, ANSI escape codes). Use smaller `offset` values (e.g., `offset: -20` instead of `offset: -100`) to reduce the captured output size.

## How It Works

1. The VS Code extension activates and registers each workspace window with the local EsiMCP HTTP server
2. The direct HTTP server exposes the MCP endpoint at `http://127.0.0.1:<configured esimcp.serverPort>/mcp` and routes requests to the owning window
3. Commands execute in real VS Code terminals using the Shell Integration API
4. Output is stored in circular buffers with pagination support for efficient reading

## Latest Changes (1.0.31)

- Added the `MS-Access-mcp` submodule and configurable stdio bridge
- Added `msaccess_list_commands` and `msaccess_execute_command`
- Forwarded Access MCP resources and prompt templates
- Advertised `tools`, `resources`, and `prompts` capabilities during initialization
- Added Access server and database path configuration

See [CHANGELOG.md](CHANGELOG.md) for full history.

## License

MIT
