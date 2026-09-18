# Project: EsiMCP

MCP server that runs commands in visible VS Code terminal tabs.
EsiMCP is one direct HTTP MCP server per VS Code workspace, hosted by the VS Code extension at `http://127.0.0.1:<configured esimcp.serverPort>/mcp`. Terminal and debug operations use `mcp_esimcp_vscode_terminal_list_commands`, `mcp_esimcp_vscode_terminal_execute_command`, `mcp_esimcp_vscode_debug_list_commands`, and `mcp_esimcp_vscode_debug_execute_command`; their command IDs use dot notation such as `terminal.list` and `debug.active.session`. Use distinct ports for separate workspaces and configure the bearer token when `ESIMCP_SECRET` is enabled.

The terminal and debug list tools return an `argumentsSchema` for every command, including required fields, types, defaults, and descriptions. C# Dev Kit list results return the positional array shape accepted by the wrapper; the installed extension does not expose more specific argument metadata for its declared commands.

## Esi.Web Debug Readiness

For a new Esi.Web launch, the orchestrator must dispatch `vscode_debug_execute_command`
with `debug.start` and `debug.check.host.readyness` in the same parallel tool-call batch:

1. The backend owns `debug.start`, which starts the configured VS Code debug session and waits for the debugger to attach.
2. The frontend owns `debug.check.host.readyness` and must invoke it immediately, without waiting for `debug.start` to return.
3. `debug.check.host.readyness` is a blocking call. It reads live shell execution output from VS Code terminals when available and can probe the configured `esimcp.debugHostReadinessUrl`; it does not read terminal scrollback.
4. `{ "ready": true }` means the configured readiness string or the configured host endpoint was observed. The default string is `Now ready on:`.
5. `{ "ready": false }` means only that the readiness timeout expired.
6. `Canceled: Canceled` means the MCP call was externally canceled. It is not a timeout and must stop the workflow; do not continue to browser actions.

For an integrated-terminal debugger whose output is not exposed through shell integration,
set `esimcp.debugHostReadinessUrl` to the host URL, for example `https://localhost:5012`.

Do not perform browser validation, call `debug.stop`, or delegate another action while
`debug.check.host.readyness` is pending. Start the readiness call before or during host
startup so it can observe live shell output.

## C# Dev Kit Commands

EsiMCP exposes the installed Microsoft C# Dev Kit as a separate command area:

- `csharp_devkit_list_commands` returns manifest commands with their IDs, resolved titles, keyboard shortcuts, menu contexts, and current registration state.
- `csharp_devkit_execute_command` invokes one declared `csdevkit.*` command and optionally forwards JSON arguments.

Command execution is allowlisted against `ms-dotnettools.csdevkit/package.json`; arbitrary VS Code commands are rejected.

## Release Process

When publishing a new version, follow these steps in order:

### 1. Update version

```bash
# In package.json, bump the version
# e.g., "version": "0.1.6" -> "version": "0.1.7"
```

### 2. Update CHANGELOG.md

Add a new entry at the top with the new version and date:

```markdown
## [0.1.7] - YYYY-MM-DD

### Added
- ...

### Fixed
- ...
```

### 3. Update README.md

Replace the "Latest Changes" section with the new version's changes. Keep only the latest version in README; full history lives in CHANGELOG.md.

### 4. Build and publish

```bash
# Build
npm run build

# Publish to npm
npm publish --access public

# Package VSIX
npx vsce package --allow-missing-repository

# Install locally for testing
cp dist/extension.js ~/.vscode/extensions/sidatacom.vscode-esi-mcp-<version>/dist/
```

### 5. Upload to the VS Code Marketplace

1. Go to https://marketplace.visualstudio.com/manage/publishers/sidatacom
2. Select EsiMCP and upload the generated VSIX.

### 6. Commit and push

```bash
git add -A
git commit -m "v0.1.7: <summary>"
git push
```

## Extension Cache Workaround

VS Code aggressively caches extensions. When developing locally:

```bash
# Quick update after modifying source
npm run build
cp dist/extension.js ~/.vscode/extensions/sidatacom.vscode-esi-mcp-<version>/dist/extension.js
# Then run "Developer: Reload Window"
```

If reload does not pick up changes, close and reopen VS Code completely.

## Terminal Execution

Prefer `mcp_esimcp_vscode_terminal_execute_command` with the `terminal.*` command IDs over other command execution facilities. EsiMCP runs commands in visible VS Code terminal tabs where the user can see output in real time.

For commands that may take longer than 30 seconds or produce large output, use pull mode:

1. Call `mcp_esimcp_vscode_terminal_execute_command` with `commandId: "terminal.run"` and `arguments.waitForCompletion: false`.
2. Call `mcp_esimcp_vscode_terminal_execute_command` with `commandId: "terminal.read"` and `arguments.offset: -10` to check progress.
3. Repeat until the command has finished.
4. Report the final result.

## Terminal Cleanup

At the end of every workflow, enumerate sessions and read their latest output before closing anything. Protect active commands, Esi.Web and Esi.Terminal hosts, active debugger sessions, the canonical terminal, and unrelated user terminals. Close only explicitly identified completed, stale, or hidden sessions when no command is still running. If the state is ambiguous, leave the session open and report it. Re-enumerate after a permitted close and report preserved sessions, closed sessions, and blockers.
