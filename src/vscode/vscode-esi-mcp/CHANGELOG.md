# Changelog

All notable changes to this project will be documented in this file.

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
