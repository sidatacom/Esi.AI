# Project: EsiMCP

MCP server that runs commands in visible VS Code terminal tabs.
EsiMCP is one direct HTTP MCP server per VS Code workspace, hosted by the VS Code extension at `http://127.0.0.1:<configured esimcp.serverPort>/mcp`. Terminal operations use `mcp_esimcp_vscode_terminal_list_commands` and `mcp_esimcp_vscode_terminal_execute_command`; C# Dev Kit and DebugManager operations use `mcp_esimcp_csharp_devkit_list_commands` and `mcp_esimcp_csharp_devkit_execute_command`. Terminal command IDs use `terminal.*`; DebugManager command IDs use `csdevkit.debug.*`. Use distinct ports for separate workspaces and configure the bearer token when `ESIMCP_SECRET` is enabled.

The terminal list tool returns an `argumentsSchema` for every command, including required fields, types, defaults, and descriptions. C# Dev Kit list results expose every command declared by the installed extension and EsiMCP virtual debug commands. Each entry includes `invocationSyntax`; pass an optional `commandId` to retrieve one command's syntax. The installed extension does not expose command-specific argument metadata for manifest commands. Debug commands use `csdevkit.debug.*`; pass one parameter object inside `arguments` when required, or `arguments: []` when no parameters are required.

## Esi.Web Debug Readiness

For a new Esi.Web launch, the orchestrator must dispatch `csharp_devkit_execute_command`
with `commandId: "csdevkit.debug.fileLaunch"` and `arguments: [{ "scheme": "file", "fsPath": "<absolute Esi.Web .csproj path>" }]`, together with `commandId: "csdevkit.debug.check.host.readyness"` and `arguments: []` in the same parallel tool-call batch:

1. The frontend owns both calls. `fileLaunch` starts the project through C# Dev Kit and resolves its launch URL and port from the project's launch settings.
2. EsiMCP prepares readiness tracking before invoking `fileLaunch`, then binds readiness to the started VS Code debug session and returns its session ID.
3. The frontend invokes `csdevkit.debug.check.host.readyness` immediately and keeps the blocking call open until it returns.
4. `csdevkit.debug.check.host.readyness` is a blocking call. It reads live shell execution output from VS Code terminals when available and can probe the configured `esimcp.debugHostReadinessUrl`; it does not read terminal scrollback.
5. `{ "ready": true }` means the configured readiness string or the configured host endpoint was observed. The default string is `Now ready on:`.
6. `{ "ready": false }` means there is no active or pending launch, the host probe failed, startup failed, the session or terminal ended, or the timeout expired.
7. `Canceled: Canceled` means the MCP call was externally canceled. It is not a timeout and must stop the workflow; do not continue to browser actions.

For an integrated-terminal debugger whose output is not exposed through shell integration,
set `esimcp.debugHostReadinessUrl` to the host URL, for example `https://localhost:5012`.

Do not perform browser validation, call `csdevkit.debug.stop`, or delegate another action while
`csdevkit.debug.check.host.readyness` is pending. Start the readiness call before or during host
startup so it can observe live shell output.

## C# Dev Kit Commands

EsiMCP exposes the installed Microsoft C# Dev Kit as a separate command area:

- `csharp_devkit_list_commands` returns every command declared by the installed manifest with its ID, resolved title, keyboard shortcuts, menu contexts, registration state, and invocation syntax, plus virtual DebugManager commands under `csdevkit.debug.*`. An optional `commandId` filters the result to one command.
- `csharp_devkit_execute_command` invokes one manifest command or virtual DebugManager command. Parameters are positional: pass one object in the `arguments` array when required, or `[]` for commands without parameters.

Manifest command execution is restricted to command IDs declared by `ms-dotnettools.csdevkit/package.json`; virtual DebugManager commands use an explicit EsiMCP allowlist. Arbitrary VS Code commands are rejected.

### Launching a C# Dev Kit Project

For a manual C# start, Microsoft documents **Start New Instance** from the project context menu in Solution Explorer, and **Debug: Select and Start Debugging** / **Show all automatic debug configurations** for creating and choosing dynamic configurations in the Debug view. See [C# debugging in VS Code](https://code.visualstudio.com/docs/csharp/debugging). A `launch.json` is not required for the normal C# Dev Kit flow.

For EsiMCP automation, invoke the declared `csdevkit.debug.projectDebugLaunch` command with a project command-context object as its first positional argument:

```json
{
	"commandId": "csdevkit.debug.projectDebugLaunch",
	"arguments": [
		{
			"path": "/absolute/path/to/Project.csproj"
		}
	]
}
```

The wrapper forwards supplied arguments unchanged. The C# Dev Kit accepts this command context and converts its `path` value to a VS Code file URI. If omitted, EsiMCP tries the active editor's containing project, then a workspace with exactly one `.csproj`; multi-project workspaces must pass the project explicitly. `csdevkit.debug.selectStartupProject` selects a startup project but does not create/select a Run and Debug configuration. After launch, verify the session with `csdevkit.debug.active.session` and host readiness with `csdevkit.debug.check.host.readyness`; command dispatch alone is not evidence that the process started. A 2026-09-25 test dispatched the command with the context above but produced no session or port listener, so this automated launch path remains unverified in that workspace; recover through **Start New Instance** or **Debug: Select and Start Debugging** rather than treating the dispatch as a successful start.

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
