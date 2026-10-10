---
name: vscode-debug
description: "MANDATORY for EsiMCP VS Code debugging: use when starting, stopping, restarting, inspecting, or validating a debug session; configuring a debug launch; working with Hot Reload, breakpoints, DAP events, Esi.AI Studio, port 7010, Blazor WebAssembly debugging, or browser checks after a debug launch."
---

# VS Code Debug

Use this workflow for EsiMCP debug-session lifecycle operations. EsiMCP owns launch orchestration and uses the public VS Code Debug and Task APIs. Do not use C# Dev Kit commands, APIs, brokers, dynamic project models, or UI workflows.

## Allowed EsiMCP Tools

- `vscode_debug_list_commands`: discover registered EsiMCP debug operations and their JSON schemas.
- `vscode_debug_execute_command`: invoke one allowlisted `debug.*` operation with an object in `arguments`.
- Use the separate VS Code terminal tools only for visible terminal work; load the `vscode-terminal` skill for that workflow.

The live catalog returned by `vscode_debug_list_commands` is the source of truth. Read it before using an unfamiliar operation. Do not call arbitrary VS Code command IDs.

## Session Lifecycle

1. Query `debug.active.session` before starting. Do not create a second session over an active one.
2. Use `debug.launchProject` as the standard project start with a workspace-relative or absolute `.csproj` path; it uses the selected `Properties/launchSettings.json` profile, optionally selected with `launchProfile`. Use `debug.launchFile` only for an explicitly required Studio-specific named VS Code configuration. Both stop all active VS Code debug sessions before building; select `workspaceFolder` for multi-root workspaces.
3. `debug.launchProject` returns a build or debug result code and the corresponding logfile path on failure. The explicit compatibility path `debug.launchFile` requires a `projectFile` or a process-based `preLaunchTask` so EsiMCP can build and capture output. The selected launch configuration must use `request: "launch"`.
4. Both launch paths use `vscode.debug.startDebugging`, wait for the matching VS Code session-start event, and return the started session ID. Verify with `debug.active.session` when the workflow needs independent confirmation.
5. For Esi.AI Studio, wait for `debug.check.host.readyness` before browser checks. The development endpoint is `http://localhost:7010`; a failed browser request is not proof that startup completed.
6. For live code updates without C# Dev Kit, use `debug.hotReload` with `mode: "watch"` and a `.csproj` `projectFile`. This stops debug sessions and starts a visible `dotnet watch` task. Do not treat the task-start event as application readiness: `dotnet watch` must finish its initial build before the host starts. Wait for the app's readiness signal or health endpoint before editing or validating it. Once running, it applies supported edits live and restarts only when an edit cannot be applied. This is a runtime watch workflow, not a debugger-attached Edit and Continue session. Use `mode: "stopWatch"` to stop it. Use `mode: "rebuild"` only when an explicit full build and debugger relaunch is required; a failed build does not relaunch.
7. Stop a standalone session with `debug.stop`. Use `debug.restart` only for a session already started from its saved configuration; an optional `rebuildTaskName` must exactly match a task in `tasks.json`.
8. Before an independent Studio build or test, follow the Studio Build Gate below.

## Studio Build Gate

This gate is mandatory before every separate build, test, or rebuild of `Esi.AI.Studio` or `Esi.AI.Studio.Client`, including client-only, Razor, CSS, and Static Web Assets changes.

1. Immediately before the build/test command, query `debug.active.session` through EsiMCP. A previous query or an apparently free port does not satisfy this check.
2. If a session is active, identify whether it belongs to Esi.AI Studio. Stop an active Studio session with `debug.stop`, then query `debug.active.session` again.
3. Verify separately that no Studio process owns port `7010`.
4. Proceed only when no Studio debug session remains active and port `7010` is free. A verified unrelated session does not need to be stopped. If session ownership, termination, or port release is uncertain, do not build or test; resolve that block first.
5. Repeat the gate immediately before every separate build/test invocation. The only exception is an edit applied within the same active `debug.launchProject` session without starting a separate build/test command.

The standalone MIT-licensed `ms-dotnettools.csharp` extension may provide the `coreclr` debug adapter. EsiMCP must not rely on C# Dev Kit for project discovery or launch. Check the selected project profile in `Properties/launchSettings.json` for application URL, environment, and Blazor `inspectUri`; inspect a named VS Code launch configuration only when the explicit Studio compatibility path is used. Ensure `UseWebAssemblyDebugging()` is enabled in Development when client-side breakpoints are required.

## Debug Case And Test Matrix

Automated unit coverage:

- Active-session lookup returns its ID or `null` when no session exists.
- Project launch stops every active VS Code session before build, builds before launch, resolves MSBuild `TargetPath`, and returns the build exit code/log path without starting the debugger after failure.
- Named launch-file selection parses JSONC, rejects missing/duplicate/attach configurations, captures supported build-task output, and removes EsiMCP-only build metadata before debugger launch.
- Debug-start refusal or exceptions return a debug result code and logfile path; host readiness binds only after a matching session starts.
- `debug.hotReload` starts/stops the tracked `dotnet watch` task and reports explicit rebuild results. Watch applies supported edits without a rebuild and restarts only for unsupported edits; rebuild stops before building and relaunches only after build success.
- For a real `dotnet watch` integration check, wait for the initial build and app readiness first, then change only an existing C# method body in an ASP.NET Core Debug fixture. Verify the endpoint reflects the edit while the app PID remains unchanged. Do not count an unsupported/rude edit as a live-apply case; non-interactive watch may restart the app for such edits. ASP.NET Core Hot Reload and C# supported-edit references: [ASP.NET Core Hot Reload](https://learn.microsoft.com/de-de/aspnet/core/test/hot-reload?view=aspnetcore-10.0) and [supported C# code changes](https://learn.microsoft.com/de-de/visualstudio/debugger/supported-code-changes-csharp?view=visualstudio).
- A launch passes the explicit VS Code configuration/workspace and returns the session observed through `onDidStartDebugSession`.
- An existing session prevents overlapping start; a VS Code launch refusal returns `started: false` and removes the pending start listener.
- Restart waits for old-session termination, optionally runs the exact named build task, then observes a new session; a reused session ID is rejected, and no active session is a no-op.
- Readiness binds terminal output captured before or after the debug session event to the correct session only.
- Readiness detects split and ANSI-decorated markers, remains latched after buffer trimming, and is isolated across parent/child sessions.
- Startup exception, failed host probe, canceled launch, terminated session, closed terminal, and no-host/no-launch paths return the appropriate not-ready/error result.
- Debug Console output is bounded and DAP output is forwarded to readiness tracking.
- MCP tool catalog exposes the terminal, debug, and Access wrappers, excludes C# Dev Kit tools, and dispatches only allowlisted debug command IDs.

Manual or integration scenarios when changing launch/runtime behavior:

- Single-folder and multi-root launch selection; reject a folder outside the current workspace.
- A start event for a different configuration must not satisfy the requested launch; verify timeout and listener cleanup.
- Stop while the session is running or paused; verify termination and that the process releases its port.
- For restart with a rebuild task, verify task failure prevents relaunch and successful task completion precedes the new session.
- Studio startup: verify active session ID, configured readiness marker or readiness URL, then the intended browser route. Resume a paused process before browser checks.
- Server breakpoint binds to the intended source line; WebAssembly client breakpoints require the configured `inspectUri` and Development debug proxy.
- `dotnet watch` is the supported live-update path without C# Dev Kit. Do not describe it as debugger-attached Edit and Continue; reserve explicit rebuild/relaunch for changes that require it.
- A passing task-start event alone does not prove Watch is usable. Require the initial build-success output followed by the host readiness signal before applying the supported method-body edit and checking value/PID.

When adding a launch or lifecycle branch, add a focused unit test for its success path and rejection/cancellation boundary. Add integration coverage only when the behavior depends on actual VS Code event ordering, the debug adapter, task execution, or a running host.

## Breakpoints And Inspection

- Use `debug.add.breakpoint` or `debug.add.logpoint` with an absolute source path and one-based line number. Check adapter binding in the response.
- Use `debug.list.breakpoints`, `debug.remove.breakpoint`, and `debug.clear.all.breakpoints` to manage breakpoints.
- Inspect only a paused frame. Use `debug.list.variable.names` before requesting specific names with `debug.get.variables.values`.
- Use `debug.evaluate.expression` for a bounded expression in the paused frame. Do not use it for unrelated side effects.
- Use `debug.wait.for.event` for paused, continued, or terminated events. Resume a paused server before browser or HTTP validation.
- Use stepping and pause/continue operations only when the session state permits them.

## Studio And Browser Validation

- Before a Studio start, check the active debug session and port `7010`; clean up only a stale process that belongs to this Studio workspace.
- After launch, verify the returned session ID and host readiness before opening or reloading a browser route.
- Verify the route and behavior that motivated the session. An existing `/flow` error is not evidence about unrelated debug or MCP changes.
- Stop the session at the end unless interactive follow-up is expected.
