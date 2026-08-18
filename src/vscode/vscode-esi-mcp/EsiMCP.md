# Project: EsiMCP

MCP server that runs commands in visible VS Code terminal tabs.
EsiMCP is one direct HTTP MCP server per VS Code workspace, hosted by the VS Code extension at `http://127.0.0.1:<configured esimcp.serverPort>/mcp`. Its terminal bindings are `mcp_esimcp_terminal_list`, `mcp_esimcp_terminal_read`, `mcp_esimcp_terminal_input`, `mcp_esimcp_terminal_exec`, `mcp_esimcp_terminal_run`, `mcp_esimcp_terminal_create`, and `mcp_esimcp_terminal_close`; its DebugMCP controls are split between launch ownership and host-readiness observation. Use distinct ports for separate workspaces and configure the bearer token when `ESIMCP_SECRET` is enabled.

## Esi.Web Debug Readiness

For a new Esi.Web launch, the orchestrator must dispatch `debug_start` and
`debug_check_host_readyness` in the same parallel tool-call batch:

1. The backend owns `debug_start`, which starts the configured VS Code debug session and waits for the debugger to attach.
2. The frontend owns `debug_check_host_readyness` and must invoke it immediately, without waiting for `debug_start` to return.
3. `debug_check_host_readyness` is a blocking call. It reads live shell execution output from VS Code terminals and remains open until readiness or its timeout is returned. It does not read terminal scrollback.
4. `{ "ready": true }` means the configured readiness string was observed. The default string is `Now ready on:`.
5. `{ "ready": false }` means only that the readiness timeout expired.
6. `Canceled: Canceled` means the MCP call was externally canceled. It is not a timeout and must stop the workflow; do not continue to browser actions.

Do not perform browser validation, call `debug_stop`, or delegate another action while
`debug_check_host_readyness` is pending. Start the readiness call before or during host
startup so it can observe live shell output.

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

Prefer the EsiMCP MCP tools (`mcp_esimcp_terminal_run`, `mcp_esimcp_terminal_exec`, `mcp_esimcp_terminal_read`, and related tools) over other command execution facilities. EsiMCP runs commands in visible VS Code terminal tabs where the user can see output in real time.

For commands that may take longer than 30 seconds or produce large output, use pull mode:

1. Call `mcp_esimcp_terminal_run` with `waitForCompletion: false`.
2. Call `mcp_esimcp_terminal_read` with `offset: -10` to check progress.
3. Repeat until the command has finished.
4. Report the final result.

## Terminal Cleanup

At the end of every workflow, enumerate sessions and read their latest output before closing anything. Protect active commands, Esi.Web and Esi.Terminal hosts, active debugger sessions, the canonical terminal, and unrelated user terminals. Close only explicitly identified completed, stale, or hidden sessions when no command is still running. If the state is ambiguous, leave the session open and report it. Re-enumerate after a permitted close and report preserved sessions, closed sessions, and blockers.
