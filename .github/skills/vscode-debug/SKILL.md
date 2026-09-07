---
name: vscode-debug
description: "Use when starting, stopping, restarting, or validating a VS Code or .NET debug session, including Esi.AI Studio, Blazor Interactive Auto, WebAssembly debugging, Hot Reload, debug ports, and browser checks."
---

# VS Code Debug

Use this workflow for every debug-session lifecycle operation.

## Allowed EsiMCP tools

Use only `csharp_devkit_list_commands` and `csharp_devkit_execute_command` for C# Dev Kit and debug lifecycle operations. Use only `vscode_terminal_list_commands` and `vscode_terminal_execute_command` for visible VS Code terminal operations. Do not use legacy `mcp_esimcp_debug_*`, `mcp_esimcp_terminal_*`, or `mcp_esimcp_csharp_devkit_*` names.

Esi.AI Studio is a .NET 10 Blazor Web App. Start it through the C# Dev Kit Run and Debug configuration shown by VS Code: `C#: Esi.AI.Studio [Default Configuration]`. Select or verify that configuration in the Run and Debug configuration picker before invoking the dynamic project launch (`csdevkit.debug.projectDebugLaunch` / **Start New Instance**). Do not require `.vscode/launch.json` or `.vscode/tasks.json` for the normal workflow.

## Session lifecycle

1. Inspect the active debug session before starting anything. Reuse it when it already matches the requested configuration.
2. If a debug session is active and a fresh session is required, stop the existing session first with `csharp_devkit_execute_command` using `commandId: "csdevkit.debug.stop"`. Never start a second session on top of an existing one.
3. Confirm that the previous session has stopped and that its application process or debug port is no longer owned by the old session.
4. In the Run and Debug configuration picker, select `C#: Esi.AI.Studio [Default Configuration]`; do not launch with an undefined, stale, or unrelated target.
5. Start exactly one server debug session with `csdevkit.debug.projectDebugLaunch` and the Studio server project selected.
6. Wait for the debugger to attach, verify an active debug-session ID with `commandId: "csdevkit.debug.active.session"`, invoke `csharp_devkit_execute_command` using `commandId: "csdevkit.debug.check.host.readyness"`, and verify the shared browser page at `http://localhost:7010`.
7. Reuse the existing shared browser page when possible, reload the target route, and verify the behavior that motivated the debug session.
8. For WebAssembly breakpoints, verify the `inspectUri` in the server launch profile and use the `blazorwasm` attach configuration only when an explicit browser attach is needed.
9. At the end of the task, leave the single intended session running only when further interactive verification is expected; otherwise stop it cleanly.

When the active session only needs to be refreshed, execute `csdevkit.debug.restart` with `csharp_devkit_execute_command`, then execute `csdevkit.debug.active.session` and `csdevkit.debug.check.host.readyness`, reselect `C#: Esi.AI.Studio [Default Configuration]` in Run and Debug when required, and verify the active session and host readiness after the restart.

### Change routing: Hot Reload versus restart

- For `.razor`, `.razor.css`, CSS, markup, and other client/UI changes, keep the active Studio debug session running and invoke `csdevkit.debug.hotReload` through `csharp_devkit_execute_command`. Verify the result in the existing browser page without a page reload when possible.
- Do not stop the session, run a separate build, or restart only because a UI change was made. Use `csdevkit.debug.showHotReloadPanel` only when Hot Reload diagnostics are needed; the panel contents are not returned by EsiMCP.
- Use `csdevkit.debug.restart` only when Hot Reload reports that the change cannot be applied, or when the change affects server/project files, dependencies, startup configuration, or another runtime boundary that requires recompilation.
- Before a required separate build or test, stop the active Studio debug session first and verify that port `7010` is free. For UI-only changes, Hot Reload takes precedence over a build.

When an agent must invoke C# Dev Kit programmatically through the EsiMCP server, use `csharp_devkit_list_commands` and `csharp_devkit_execute_command` for C# Dev Kit commands, including the virtual `active.session`, `check.host.readyness`, `stop`, and `restart` commands.

If `csdevkit.debug.projectDebugLaunch` fails with a missing URI/scheme or an undefined launch target, treat that as a broken VS Code/C# Dev Kit context. Inspect and repair the active Run and Debug configuration and startup project, then retry the same C# Dev Kit command. Never substitute `dotnet run`, a manually created launch configuration, or another server process as a workaround.

### Verified EsiMCP launch invocation

The verified EsiMCP call uses `csharp_devkit_execute_command` with the command ID `csdevkit.debug.projectDebugLaunch` and a serialized VS Code file URI object as its first argument. The URI string alone is not sufficient:

```json
{
	"commandId": "csdevkit.debug.projectDebugLaunch",
	"arguments": [
		{
			"scheme": "file",
			"authority": "",
			"path": "/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj",
			"query": "",
			"fragment": "",
			"fsPath": "/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj"
		}
	]
}
```

This invocation created the active Studio debug session. Verify success through the active debug-session ID and the shared browser page at `http://localhost:7010`; a command response without an active session or a reachable browser page is not a successful launch. For Hot Reload, use `csdevkit.debug.hotReload` against that active session and `csdevkit.debug.showHotReloadPanel` only to inspect Hot Reload diagnostics.

### Verified EsiMCP stop invocation

Call `csharp_devkit_execute_command` with the virtual C# Dev Kit stop command:

```json
{
	"commandId": "csdevkit.debug.stop",
	"arguments": []
}
```

The verified result is `{"stopped":true}`. Confirm the stop by checking that the active debug-session ID is `null` and that no process is listening on port `7010`.

### Restart, readiness, and diagnostics sequence

C# Dev Kit 3.20.199 now exposes these lifecycle and diagnostic commands through the EsiMCP C# Dev Kit bridge. They are virtual commands in the extension command list (`registered: false`) and must be invoked through `csharp_devkit_execute_command`:

- `csdevkit.debug.active.session`
- `csdevkit.debug.check.host.readyness`
- `csdevkit.debug.output.diagnostics`
- `csdevkit.debug.stop`
- `csdevkit.debug.restart`

1. Check the active debug-session ID.
2. Start with `csdevkit.debug.projectDebugLaunch` and the complete Studio project file URI object shown above.
3. Confirm the active debug-session ID with `csdevkit.debug.active.session`.
4. Call `csharp_devkit_execute_command` with `{ "commandId": "csdevkit.debug.check.host.readyness", "arguments": [] }`.
5. For a refresh, call `{ "commandId": "csdevkit.debug.restart", "arguments": [] }`, then query the new active session and run the readiness command again.
6. Confirm a reachable browser page before browser checks.

For structured diagnostics, call `{ "commandId": "csdevkit.debug.output.diagnostics", "arguments": [{ "sessionId": "<active-session-id>" }] }`. The verified response included `bufferedCharacters: 15337` and `readinessStringSeen: true`. The readiness command returned `{ "ready": true }`; these are the C# Dev Kit bridge checks, distinct from the legacy EsiMCP host-readiness helper.

### Verified Razor Hot Reload check

For a concrete Hot Reload test, change a visible value in `Backends.razor`, call `csdevkit.debug.hotReload`, then call `csdevkit.debug.showHotReloadPanel`. The panel command is a UI command and does not return its panel text through EsiMCP. Verify the result in the connected browser DOM without reloading the page. In the verified test, changing the `Loaded models` heading from `1rem` to `1.1rem` produced a computed browser font size of `17.6px` without a page reload.

When terminals or tasks are stale, duplicated, or inconsistent with the active debug state, first stop all project-related terminal and task processes. Then verify that no old debug session or process still owns the application port, and only after that start exactly one new debug session. Do not leave old build or Studio terminals running beside the replacement session.

## Esi.AI Studio defaults

- C# Dev Kit project command: `csdevkit.debug.projectDebugLaunch`
- C# Dev Kit Hot Reload command: `csdevkit.debug.hotReload`
- Development port: `7010`
- WebAssembly debug proxy: `/_framework/debug/ws-proxy`
- Development URL: `http://localhost:7010`

## Recovery rules

- If the port is unavailable after stopping, inspect the owning process and terminate only the process belonging to the stale Studio session.
- If the host is not ready, inspect the debug/task output before starting another session.
- A browser connection failure is not evidence that a second debug session is needed; first verify the host and task state.
