# Desktop Commander MCP - Implementation Reference

## Overview
Desktop Commander is the most relevant MCP as a reference. It implements command
execution with output capture, session management, and security.

## Exposed Tools
- **Terminal**: `execute_command`, `read_output`, `force_terminate`, `list_sessions`, `list_processes`
- **Filesystem**: `read_file`, `write_file`, `move_file`, `search`
- **Code editing**: `edit_block` (surgical text replacement)

## Security Patterns
- Two execution modes:
  1. **Safe mode** - Executable allowlist without shell interpretation
  2. **Legacy mode** - Command string allowlist/blocklist
- Command validation before execution
- Timeout control
- Working directory restrictions
- Stdin support with timeout protection

## Configuration
```json
{
  "blockedCommands": ["rm -rf /", "mkfs", "dd if="],
  "defaultShell": "/bin/bash",
  "allowedDirectories": ["/home/user/projects"],
  "fileReadLineLimit": 1000,
  "fileWriteLineLimit": 1000
}
```

## Differences with Our Solution
| Aspect | Desktop Commander | Our MCP |
|--------|------------------|---------|
| Visibility | No visible terminal | Visible terminal in VSCode |
| Integration | Independent process | Embedded in VSCode extension |
| Output | Captured text only | Text + visual terminal |
| Subagents | Not designed for this | Native support with agentId |
| Context | Loses visual context | User sees the execution |

## Lessons Applied
1. Circular buffer for output with pagination
2. Dangerous command blocklist by default
3. Configurable timeout with partial return
4. Session management with Map<id, session>
