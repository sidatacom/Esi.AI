# Changelog

All notable changes to this project will be documented in this file.

## [2.0.2] - 2026-09-25

### Fixed
- Probe the configured host URL for an already-running server
- Finish readiness immediately when no launch or active session remains
- Stop waiting when a launch is canceled or its session/terminal ends

## [2.0.1] - 2026-09-25

### Fixed
- Keep host readiness latched per debug session until its terminal or session ends

## [2.0.0] - 2026-09-24

### Removed
- **Breaking:** Remove the standalone `vscode_debug_*` tools; use `csharp_devkit_*` with `csdevkit.debug.*` command IDs instead.

## [1.0.37] - 2026-09-21

### Fixed
- End debug host readiness immediately when startup exceptions appear in Debug Console output

## [1.0.36] - 2026-09-18

### Fixed
- Probe the configured debug host when integrated-terminal output is unavailable

## [1.0.35] - 2026-09-18

### Fixed
- Accept the runtime debug session name returned by VS Code

## [1.0.34] - 2026-09-18

### Fixed
- Return the debug session ID from the VS Code debug start command

## [1.0.33] - 2026-09-18

### Fixed
- Accept readiness detected from an active dotnet terminal while a debug session is selected

## [1.0.32] - 2026-09-17

### Added
- Publish the current EsiMCP Access tools, resources, prompts, and documentation

## [1.0.31] - 2026-09-17

### Added
- Wrap the Microsoft Access MCP submodule through namespaced list and execute commands

## [1.0.30] - 2026-09-13

### Added
- Return buffered Debug Console output and its last non-empty line from diagnostics

## [1.0.29] - 2026-09-06

### Fixed
- Require a new VS Code debug session ID after restart instead of accepting recycled IDs
- Support an optional named `tasks.json` rebuild task between stopping and starting the debug session

## [1.0.28] - 2026-09-06

### Fixed
- Complete debug restarts when VS Code reuses the existing session ID
- Add detailed restart lifecycle diagnostics

## [1.0.27] - 2026-09-06

### Added
- Expose buffered Debug Console output for readiness diagnostics

## [1.0.26] - 2026-09-06

### Fixed
- Match debug-host readiness strings split across Debug Console output chunks

## [1.0.25] - 2026-09-06

### Fixed
- Log debug-host readiness state transitions for diagnostics

## [1.0.24] - 2026-09-06

### Fixed
- Store debug host readiness independently for each VS Code debug session

## [1.0.23] - 2026-09-06

### Fixed
- Capture readiness from Debug Adapter Protocol output and reset it when the debug session ends

## [1.0.22] - 2026-09-06

### Fixed
- Capture Studio readiness from C# Dev Kit Debug Console DAP output

## [1.0.21] - 2026-09-06

### Fixed
- Detect readiness already emitted by active `dotnet:` terminals before the readiness command is invoked
- Ignore readiness output from exited terminals

## [1.0.20] - 2026-09-06

### Added
- Add virtual C# Dev Kit commands for debug-session readiness, restart, and stop
- Route C# Dev Kit lifecycle operations through the shared debug handlers

## [1.0.19] - 2026-09-06

### Added
- Include per-command argument schemas in Debug, Terminal, and C# Dev Kit command listings

## [1.0.18] - 2026-09-06

### Changed
- Restrict C# Dev Kit wrapper commands to project debug, no-debug launch, Hot Reload, Hot Reload diagnostics, and startup-project selection

## [1.0.17] - 2026-09-06

### Added
- Add allowlist-based EsiMCP tools for listing and executing Microsoft C# Dev Kit commands

## [1.0.16] - 2026-09-06

### Fixed
- Complete `debug_restart` after the old debug session terminates and the replacement session starts

## [1.0.15] - 2026-09-06

### Fixed
- Read the active debug session directly from VS Code for every debug tool operation

## [1.0.14] - 2026-09-06

### Fixed
- Return immediately from `debug_restart` when no active debug session exists

## [1.0.13] - 2026-09-02

### Fixed
- Abort debug host readiness waiting when the active debug session raises an exception, returning `DEBUG_SESSION_EXCEPTION`

## [1.0.12] - 2026-08-22

### Fixed
- Keep the debug host readiness latch until the next debug lifecycle reset

## [1.0.11] - 2026-08-19

### Fixed
- Prevent concurrent `debug_start` calls from starting more than one VS Code debug session

## [1.0.10] - 2026-08-18

### Fixed
- Reset the host readiness latch before `debug_start` and after `debug_stop`

## [1.0.9] - 2026-08-18

### Added
- `debug_check_host_readyness` MCP tool with a global terminal readiness latch
- Configurable `esimcp.debugHostReadinessTimeoutSeconds` setting with a 60-second default

## [1.0.8] - 2026-08-18

### Fixed
- Use a single `debug_start` and `debug_stop` tool pair
- Wait for the exact debug session termination when stopping

### Added
- `debug_wait_for_event` MCP tool for debugger pause, exception, continue, and termination events
- DAP exception details including exception type, message, source file, and line when available

### Fixed
- Preserve debugger events until the agent explicitly waits for them

## [1.0.6] - 2026-08-17

### Added
- Configurable `esimcp.debugConfigurationName` default for `debug_start`

### Fixed
- Use the configured launch name when no explicit `configurationName` is supplied
- Keep the automatic source-file debug configuration as the final fallback

## [1.0.5] - 2026-08-17

### Fixed
- Wait for the VS Code debug session before reporting `debug_start` success
- Remove unreliable terminal output readiness from debugger startup

## [1.0.4] - 2026-08-15

### Added
- `debug_settings` tool for reading active VS Code workspace settings

## [1.0.1] - 2026-08-13

### Fixed
- Consistent package, server, and MCP handshake version reporting
- Correct README configuration key names and defaults

## [0.1.6] - 2026-03-19 18:18 PDT

### Added
- Direct per-workspace HTTP MCP architecture using Streamable HTTP, with no routing or socket bridge
- Screenshots in README for marketplace (run, exec, permission dialog)
- Custom terminal tab names with date format (e.g., `MCP: EsiMCP-26-03-19-17-30`)
- `name` parameter in `run` tool for custom terminal names
- Unique HTTP endpoint per workspace to prevent conflicts between multiple VSCode instances
- Large output handling documentation
- Development workflow docs for extension cache workaround

### Fixed
- Clean output format for all tools (`run`, `exec`, `read`, `list`, `close`, `input`) — no more raw JSON responses
- `waitForCompletion: false` not working (`z.coerce.boolean()` converted string `"false"` to `true`)
- Idle reaper killing sessions with running commands — reaper disabled, user closes sessions manually

## [0.1.5] - 2026-03-18 14:50 PDT

### Added
- npm publish with `bin` entry for `npx vscode-esi-mcp` support
- Published to VSCode Marketplace and MCP Registry

## [0.1.3] - 2026-03-18 11:00 PDT

### Added
- `run` tool combining create + exec in one step
- Session reuse: `run` finds idle sessions before creating new ones
- Busy session detection: won't reuse sessions with running commands

### Fixed
- First-command timing fix with shell initialization delay

## [0.1.0] - 2026-03-18 10:00 PDT

### Added
- Initial release
- Tools: `create`, `exec`, `read`, `input`, `list`, `close`
- Shell Integration API for output capture and exit code detection
- Circular output buffer with pagination support
- Subagent isolation with `agentId`
- Command blocklist security
- Direct HTTP MCP endpoint for local clients
