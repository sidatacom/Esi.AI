# EsiMCP

[![npm version](https://img.shields.io/npm/v/vscode-esi-mcp.svg)](https://npmjs.org/package/vscode-esi-mcp)

A local HTTP MCP server hosted by the VS Code extension. It exposes visible terminal sessions, native VS Code debug operations, and a proxy to the configured Microsoft Access MCP server.

## Requirements

- VS Code 1.99 or later
- Node.js 20 or later
- Windows, Microsoft Access, and a compatible .NET runtime for upstream Access COM/DAO operations

The Studio lifecycle is covered in [EsiMCP Debug Lifecycle](../../../docs/projects/Esi.AI/development/esimcp-debug-lifecycle.md). The native debug facade and its current scenarios are described in [EsiMCP VS Code Debug](../../../docs/projects/Esi.AI/development/esimcp-vscode-debug.md).

## Configure MCP (provided by extension)

EsiMCP registers its workspace-local HTTP endpoint with VS Code through the MCP server definition provider API. It does not need an entry in `.vscode/mcp.json`.

By default, each VS Code window gets a distinct OS-assigned port; set `esimcp.serverPort` only when that workspace needs a fixed port. When `ESIMCP_SECRET` is configured, clients must send its bearer token.

## Tool Families

| EsiMCP MCP protocol tool IDs                                        | Purpose                                                                                           |
| ------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------- |
| `vscode_terminal_list_commands`, `vscode_terminal_execute_command` | Discover and operate visible VS Code terminal sessions.                                           |
| `vscode_debug_list_commands`, `vscode_debug_execute_command`       | Discover and execute allowlisted `debug.*` operations through public VS Code Debug and Task APIs. |
| `msaccess_list_commands`, `msaccess_execute_command`               | Discover and execute Access operations through EsiMCP's configured upstream proxy.                |

The identifiers in the first column are tool IDs registered by EsiMCP on its MCP endpoint. A VS Code tool bridge may expose callable wrapper aliases such as `mcp_esimcp_msaccess_list_commands` and `mcp_esimcp_msaccess_execute_command`; these bridge-specific aliases are not EsiMCP protocol tool IDs. Callers must use the EsiMCP connection and must never connect to or start the configured upstream stdio implementation directly.

For every family, list its command catalog before invoking an unfamiliar command. The returned argument schemas are authoritative. EsiMCP does not expose C# Dev Kit commands.

## Debug Lifecycle

Use `debug.launchProject` with a `.csproj` path to stop active sessions, build, resolve the output, and start it with `coreclr`. It uses the project's `Properties/launchSettings.json` profile for application URLs and environment variables; set `launchProfile` to select a named profile, or omit it to use the first `commandName: "Project"` profile. Use `debug.launchFile` only when a named `.vscode/launch.json` configuration is explicitly required; configure `projectFile` or a process-based `preLaunchTask` so EsiMCP can capture the build output. The public debug catalog has no generic `debug.start` command. Build and debug startup failures return a result code and logfile path.

Use `debug.hotReload` with `mode: "watch"` and a `.csproj` path for live .NET Hot Reload. It stops active debug sessions and starts a visible `dotnet watch` task; the initial build happens once, supported edits are applied live, and unsupported edits trigger an SDK-managed restart. Use `mode: "stopWatch"` to end it. This workflow runs the app under `dotnet watch`, not under the VS Code debugger. `mode: "rebuild"` remains an explicit full build plus debugger relaunch when a code change requires it.

For a C# debug adapter, the standalone MIT-licensed `ms-dotnettools.csharp` extension may provide `coreclr`; EsiMCP owns project/configuration selection and does not depend on C# Dev Kit.

## Terminal Usage

`terminal.run` creates a visible terminal or reuses an idle session matching the requested `agentId` and, when supplied, `cwd`. For long-running commands, use `waitForCompletion: false`, keep the returned session ID, and poll `terminal.read`. A wait timeout leaves the process active; inspect the same session before retrying. Use `terminal.input` only after reading an interactive prompt.

EsiMCP's blocked-command and allowed-directory settings are guardrails, not a sandbox. Review commands and paths carefully; do not run destructive operations without explicit authorization.

## Microsoft Access

Call the EsiMCP protocol tool `msaccess_list_commands` first and pass the selected upstream tool name as `commandId` to `msaccess_execute_command`, using the returned schema. Where a VS Code bridge is the caller, use its `mcp_esimcp_msaccess_list_commands` and `mcp_esimcp_msaccess_execute_command` callable aliases; those aliases are not protocol tool IDs and are not upstream `commandId` values. MCP resources and prompts use their own methods on the EsiMCP connection; they are not tool command IDs. EsiMCP starts its configured upstream stdio process lazily and owns its lifecycle. Callers must never connect to or start that process directly. The process requires Windows, Microsoft Access, and compatible .NET for COM/DAO operations. Confirm the target database before mutations, and inspect its state before retrying a timed-out write.

## Configuration

The `esimcp.msAccess*` settings below are EsiMCP-owned configuration for its internal upstream process; they do not define a separate caller connection.

| Setting                                   |                  Default | Description                                                     |
| ----------------------------------------- | -----------------------: | --------------------------------------------------------------- |
| `esimcp.serverPort`                       |                      `0` | Local MCP HTTP port; `0` assigns a unique port for this VS Code workspace. |
| `esimcp.bindHost`                         |       `127.0.0.1`, `::1` | Bind addresses; loopback is recommended.                        |
| `esimcp.blockedCommands`                  | destructive-pattern list | Commands rejected by the terminal wrapper.                      |
| `esimcp.allowedDirectories`               |                     `[]` | Optional working-directory allowlist; empty means unrestricted. |
| `esimcp.defaultTimeoutMs`                 |                  `30000` | Default terminal command timeout.                               |
| `esimcp.maxConcurrentSessions`            |                     `10` | Maximum active terminal sessions.                               |
| `esimcp.maxOutputLines`                   |                  `10000` | Maximum buffered output lines per session.                      |
| `esimcp.debugReadyString`                 |          `Now ready on:` | Marker observed in terminal or Debug Console output.            |
| `esimcp.debugHostReadinessTimeoutSeconds` |                     `60` | Debug host readiness timeout.                                   |
| `esimcp.debugHostReadinessUrl`            |                    empty | Optional host URL probed during readiness checks.               |
| `esimcp.msAccessServerCommand`            |                 `dotnet` | Executable used to start the Access MCP server.                 |
| `esimcp.msAccessServerArguments`          |                     `[]` | Explicit arguments; replaces default `dotnet run` arguments.    |
| `esimcp.msAccessServerProject`            |     bundled project path | Project used by the default command.                            |
| `esimcp.msAccessServerWorkingDirectory`   | first workspace folder | Access server working directory.                                |
| `esimcp.msAccessDatabasePath`             |                    empty | Optional path passed as `ACCESS_DATABASE_PATH`.                 |
| `esimcp.msAccessTimeoutMs`                |                 `120000` | Access MCP request timeout.                                     |

## Test And Install

From this directory:

```bash
npm test
npm run test:integration
npm run version:check
npm run build
npm run package
code --install-extension vscode-esi-mcp-<version>.vsix --force
```

The Extension Host integration suite uses a native VS Code Node debug configuration. Unit coverage includes project/configuration launch ordering, output logs, failure codes, schemas, readiness, and the `dotnet watch` task lifecycle. After installing a newly versioned VSIX, reload the VS Code window before validating the registered extension.

## License

MIT
