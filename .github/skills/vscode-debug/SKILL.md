---
name: vscode-debug
description: "Use when starting, stopping, restarting, or validating a VS Code or .NET debug session, including Esi.AI Studio, launch configurations, watchdogs, debug ports, and browser checks."
---

# VS Code Debug

Use this workflow for every debug-session lifecycle operation.

Esi.AI Studio may only be started through the `Esi.AI Studio (Server Debug)` launch configuration or the `build-and-watch-esi-ai-studio` task chain. The `start-esi-ai-studio-watchdog` task must be active before the Studio process starts; never launch the Studio binary or `dotnet run` directly. Studio startup validates the watchdog PID marker supplied by that launch configuration and exits when it is absent or invalid.

## Session lifecycle

1. Inspect the active debug session before starting anything. Reuse it when it already matches the requested configuration.
2. If a debug session is active and a fresh session is required, stop the existing session first. Never start a second session on top of an existing one.
3. Confirm that the previous session has stopped and that its application process or debug port is no longer owned by the old session.
4. Check whether the project watchdog is already running. Do not start a duplicate watchdog.
5. Start exactly one debug session with the requested launch configuration.
6. Wait for the debugger to attach and verify the configured host readiness signal before browser checks.
7. Reuse the existing shared browser page when possible, reload the target route, and verify the behavior that motivated the debug session.
8. At the end of the task, leave the single intended session running only when further interactive verification is expected; otherwise stop it cleanly.

When the active session only needs to be refreshed, use the available `mcp_esimcp_debug_restart` operation instead of starting a new session manually. Verify the active session and host readiness after the restart.

When terminals or tasks are stale, duplicated, or inconsistent with the active debug state, first stop all project-related terminal and task processes. Then verify that no old debug session or process still owns the application port, and only after that start exactly one new debug session. Do not leave old build, watchdog, or Studio terminals running beside the replacement session.

## Esi.AI Studio defaults

- Launch configuration: `Esi.AI Studio (Server Debug)`
- Development port: `7010`
- Application watchdog task: `start-esi-ai-studio-watchdog`
- Build task: `build-esi-ai-studio`
- Combined build and watchdog task: `build-and-watch-esi-ai-studio`

## Recovery rules

- If the port is unavailable after stopping, inspect the owning process and terminate only the process belonging to the stale Studio session.
- If the host is not ready, inspect the debug/task output before starting another session.
- A browser connection failure is not evidence that a second debug session is needed; first verify the host and task state.
