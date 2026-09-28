# EsiMCP Project Guidance

EsiMCP is a per-workspace local HTTP MCP server hosted inside the VS Code extension host. Use the three dedicated skills before operating a tool family:

- `vscode_debug` -> `.github/skills/vscode-debug/SKILL.md`
- `vscode_terminal` -> `.github/skills/vscode-terminal/SKILL.md`
- `msaccess` -> `.github/skills/msaccess/SKILL.md`

The only EsiMCP tool families are terminal, native VS Code debug, and Microsoft Access. Do not add or invoke C# Dev Kit integrations. Discover each family through its `*_list_commands` tool; use the returned schemas and invoke operations only through the matching `*_execute_command` tool.

## Debug Test Scenarios

- No active session and active-session ID lookup.
- Explicit launch configuration and named configuration; reject overlapping starts and handle VS Code launch refusal.
- Project launch: stop all active sessions before build, resolve MSBuild `TargetPath`, launch with `coreclr`, and return build/debug result codes plus logfile paths on failure.
- Named `launch.json` configuration: parse JSONC, require a unique `launch` request, build through `projectFile` or a capturable process task, and prevent a second pre-launch build.
- Live code updates use a tracked `dotnet watch` task; supported edits apply without restarting, while unsupported edits are restarted by the .NET SDK. Explicit full rebuild stops before building and does not relaunch after failure.
- Match the actual `onDidStartDebugSession` event to the requested configuration; clean up on cancellation, mismatch, and timeout.
- Stop a running or paused session and verify termination.
- Restart the same configuration; verify optional task completion occurs between stop and start, failed tasks prevent restart, and the new session ID differs.
- Readiness from output captured before/after session binding, split/ANSI output, configured URL, startup exception, canceled launch, process/session/terminal termination, and no active launch.
- Paused-frame variable listing, bounded value requests, expression evaluation/redaction, breakpoint/logpoint binding and removal, step/continue/pause events.
- Studio readiness and browser-route check only after session confirmation; WebAssembly client breakpoints require `inspectUri` and Development debug proxy.

Unit tests cover isolated state transitions and failure boundaries. The VS Code Extension Host suite validates actual event ordering, debug adapter behavior, task execution, and MCP dispatch. Do not count a runner exit code alone as success when no integration success report is emitted.

## Validation And Release

From `src/vscode/vscode-esi-mcp`, run `npm test`, `npm run test:integration`, `npm run version:check`, `npm run build`, then `npm run package`. Increment the patch version and keep the package, lockfile, server metadata, and latest changelog entry consistent before building. Install the newly versioned VSIX with `code --install-extension <versioned-vsix> --force`, then reload the VS Code window and verify the installed extension.

For a Studio lifecycle task, load `vscode-debug` before any launch/stop/browser operation. Use `debug.launchProject` or `debug.launchFile` and verify session ID plus host readiness before browser checks. The standalone `ms-dotnettools.csharp` extension may provide the `coreclr` adapter; EsiMCP owns launch orchestration. For non-debugged live updates use `debug.hotReload` with `mode: "watch"`; it starts the project under `dotnet watch`. Use `mode: "rebuild"` only when an explicit debugger relaunch is required.

Terminal work should use visible EsiMCP sessions and asynchronous output reads for long-running commands. Access mutations require explicit authorization and confirmation of the target database; Access COM/DAO execution requires Windows, Microsoft Access, and compatible .NET.
