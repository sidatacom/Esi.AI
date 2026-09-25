---
name: vscode-debug
description: "MANDATORY for Esi.AI Studio and any C# Dev Kit lifecycle task: use when starting, stopping, restarting, or validating Esi.AI Studio; using Start New Instance, csdevkit.debug.projectDebugLaunch, C# Dev Kit, Run and Debug, Hot Reload, port 7010, Blazor Interactive Auto, WebAssembly debugging, localhost browser checks, or testing the Studio UI in a browser. Load this skill before the first lifecycle or browser tool call."
---

# VS Code Debug

Use this workflow for every debug-session lifecycle operation.

## Allowed EsiMCP tools

Use only `csharp_devkit_list_commands` and `csharp_devkit_execute_command` for C# Dev Kit and debug lifecycle operations. Use only `vscode_terminal_list_commands` and `vscode_terminal_execute_command` for visible VS Code terminal operations. Do not use legacy `mcp_esimcp_debug_*`, `mcp_esimcp_terminal_*`, or `mcp_esimcp_csharp_devkit_*` names.

Esi.AI Studio is a .NET 10 Blazor Web App. For automated starts, invoke `csdevkit.debug.projectDebugLaunch` with the explicit Studio project context as its first positional argument. Through EsiMCP, pass a context object containing `path`; the installed C# Dev Kit converts it to a VS Code file URI. This is the programmatic project-launch action; it does not require a pre-existing `launch.json` or a previously selected in-memory debug configuration. For a manual start, right-click the Studio project in Solution Explorer and choose **Start New Instance**. Alternatively, use **Debug: Select and Start Debugging** or **Show all automatic debug configurations** in the Debug view to create/select a dynamic C# configuration before pressing F5. Microsoft documents these UI paths at [C# debugging in VS Code](https://code.visualstudio.com/docs/csharp/debugging).

Do not confuse `csdevkit.debug.selectStartupProject` with selecting a Run and Debug configuration: it only selects the startup project. EsiMCP passes JSON arguments to the C# Dev Kit command unchanged. Pass an array containing a command-context object with `path` set to the absolute `.csproj` path. In multi-project workspaces, always pass it explicitly; without arguments EsiMCP can only resolve a project from the active editor or from a workspace containing exactly one project.

## Session lifecycle

1. Inspect the active debug session before starting anything. Reuse it when it already matches the requested configuration.
2. If a debug session is active and a fresh session is required, stop the existing session first with `csharp_devkit_execute_command` using `commandId: "csdevkit.debug.stop"`. Never start a second session on top of an existing one.
3. Confirm that the previous session has stopped and that its application process or debug port is no longer owned by the old session.
4. Before every debug start or restart, build `src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj` successfully. Do not launch or restart the debugger when the build exits non-zero; report the build diagnostics and fix or request resolution first.
5. Start exactly one server debug session with `csdevkit.debug.projectDebugLaunch` and the explicit Studio project context below, or use **Start New Instance** in Solution Explorer.
6. Do not treat command dispatch as a running session. Verify a non-null ID with `csdevkit.debug.active.session`; if it is `null`, inspect the C# Dev Kit/Debug Console output and fix the selected project or VS Code launch context before retrying.
7. Once an active session exists, call `csdevkit.debug.check.host.readyness` and verify the shared browser page at `http://localhost:7010`.
8. Reuse the existing shared browser page when possible, reload the target route, and verify the behavior that motivated the debug session.
9. For WebAssembly breakpoints, verify the `inspectUri` in the server launch profile and use the `blazorwasm` attach configuration only when an explicit browser attach is needed.
10. At the end of the task, leave the single intended session running only when further interactive verification is expected; otherwise stop it cleanly.

When the active session only needs to be refreshed, execute `csdevkit.debug.restart` with `csharp_devkit_execute_command`, then execute `csdevkit.debug.active.session` and `csdevkit.debug.check.host.readyness`, reselect `C#: Esi.AI.Studio [Default Configuration]` in Run and Debug when required, and verify the active session and host readiness after the restart.

### Change routing: Hot Reload versus restart

- For `.razor`, `.razor.css`, CSS, markup, and other client/UI changes, keep the active Studio debug session running and invoke `csdevkit.debug.hotReload` through `csharp_devkit_execute_command`. Verify the result in the existing browser page without a page reload when possible.
- After such a UI edit, `csdevkit.debug.hotReload` is the agent's immediate next validation action. Do not run a separate build, test, `get_errors`, or diff-only check first; those do not replace Hot Reload while the Studio debug session is active.
- Do not stop the session, run a separate build, or restart only because a UI change was made. Use `csdevkit.debug.showHotReloadPanel` only to open the VS Code panel interactively; its panel text is not returned by EsiMCP.
- After Hot Reload, a restart, or a failed browser check, use `csdevkit.debug.output.diagnostics` with the active session ID to read the buffered Debug Console output. Inspect `lastLine` first for the latest error or readiness state, and inspect `output` when surrounding context is needed. Do not claim that the Debug Console is unreadable when this command returns a session object.
- Use `csdevkit.debug.restart` only when Hot Reload reports that the change cannot be applied, or when the change affects server/project files, dependencies, startup configuration, or another runtime boundary that requires recompilation.
- Before a required separate build or test, stop the active Studio debug session first and verify that port `7010` is free. For UI-only changes, Hot Reload takes precedence over a build.

When an agent must invoke C# Dev Kit programmatically through the EsiMCP server, use `csharp_devkit_list_commands` and `csharp_devkit_execute_command` for C# Dev Kit commands, including the virtual `active.session`, `check.host.readyness`, `stop`, and `restart` commands.

If `csdevkit.debug.projectDebugLaunch` reports a missing URI/scheme, verify the EsiMCP argument is an array containing `{ "path": "<absolute .csproj path>" }`; the installed C# Dev Kit converts this context to a VS Code URI. If the command is dispatched but `csdevkit.debug.active.session` remains `null`, the launch has failed: inspect C# Dev Kit output and use **Start New Instance** on the Studio project or **Debug: Select and Start Debugging** to resolve the dynamic launch context. Do not repeatedly dispatch the command, create a launch file, or substitute `dotnet run`/another server process. The automated command was dispatched with this context in the 2026-09-25 verification attempt, but no session or listener appeared; automated launch is not yet verified in this workspace.

### EsiMCP Project Launch Invocation

The EsiMCP wrapper forwards the command's positional arguments unchanged. Pass a command-context object with `path`; do not pass a URI-shaped JSON object with only `scheme` and `fsPath` because it is not a VS Code `Uri` instance:

```json
{
	"commandId": "csdevkit.debug.projectDebugLaunch",
	"arguments": [
		{
			"path": "/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj"
		}
	]
}
```

The JSON shape above is the EsiMCP wrapper's argument contract, not proof that a launch succeeded. The command ID is exposed by the installed C# Dev Kit manifest; Microsoft's public documentation describes the UI launch paths rather than this internal command payload. The wrapper can infer an omitted project argument from the active editor or from a workspace with exactly one `.csproj`; this workspace has multiple projects, so pass the Studio project explicitly. Verify success through the active debug-session ID and the shared browser page at `http://localhost:7010`; a command response without an active session is not a successful launch. For Hot Reload, use `csdevkit.debug.hotReload` against that active session and `csdevkit.debug.showHotReloadPanel` only to inspect Hot Reload diagnostics.

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

C# Dev Kit 3.20.207 exposes these lifecycle and diagnostic commands through the EsiMCP C# Dev Kit bridge. They are virtual commands in the extension command list (`registered: false`) and must be invoked through `csharp_devkit_execute_command`:

- `csdevkit.debug.active.session`
- `csdevkit.debug.check.host.readyness`
- `csdevkit.debug.output.diagnostics`
- `csdevkit.debug.stop`
- `csdevkit.debug.restart`

1. Check the active debug-session ID.
2. Start with `csdevkit.debug.projectDebugLaunch` and the Studio project context shown above.
3. Confirm the active debug-session ID with `csdevkit.debug.active.session`.
4. Call `csharp_devkit_execute_command` with `{ "commandId": "csdevkit.debug.check.host.readyness", "arguments": [] }`.
5. For a refresh, call `{ "commandId": "csdevkit.debug.restart", "arguments": [] }`, then query the new active session and run the readiness command again.
6. Confirm a reachable browser page before browser checks.

For structured diagnostics, call `{ "commandId": "csdevkit.debug.output.diagnostics", "arguments": [{ "sessionId": "<active-session-id>" }] }`. The response includes `sessionId`, `bufferedCharacters`, `readinessStringSeen`, `output`, and `lastLine`. Use `lastLine` as the concise answer when the user asks for the last Debug Console line; use `output` to investigate the surrounding messages. If no session is active, the command returns `null`, so first call `csdevkit.debug.active.session` and do not infer a console failure from `null`. The readiness command returned `{ "ready": true }`; these are the C# Dev Kit bridge checks, distinct from the legacy EsiMCP host-readiness helper.

### Troubleshooting a missing launch session

The EsiMCP wrapper does not construct VS Code `Uri` instances for explicit arguments. Pass the `{ "path": "<absolute .csproj path>" }` command context so C# Dev Kit can create the URI internally. If that command returns without an active session, the launch is still unsuccessful; use **Start New Instance** on `Esi.AI.Studio` or **Debug: Select and Start Debugging** to choose a dynamic configuration. Do not replace the C# Dev Kit launch with `dotnet run`; after a successful start, verify both the active session ID and `{ "ready": true }`.

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
