---
name: vscode-terminal
description: "MANDATORY for EsiMCP visible terminal work: use when running commands, creating/reusing terminal sessions, reading output, sending interactive input, or closing sessions through vscode_terminal tools."
---

# VS Code Terminal

Use EsiMCP's visible VS Code terminals for workspace commands so output and session state remain visible to the user. Do not substitute shell tools when the requested workflow depends on the EsiMCP session, agent isolation, or incremental output.

## Discover And Execute

1. Call `vscode_terminal_list_commands` before using an unfamiliar operation. Its live schemas are authoritative.
2. Call `vscode_terminal_execute_command` with a listed `commandId` and an `arguments` object. Allowed IDs are `terminal.run`, `terminal.create`, `terminal.execute`, `terminal.read`, `terminal.list`, `terminal.close`, and `terminal.input`.
3. For ordinary work, prefer `terminal.run`; it creates a visible session or reuses an idle one. Reuse matches the requested `agentId` and, when supplied, `cwd`; it does not promise to reconcile different environment or shell settings.
4. Set `agentId` when parallel agent tasks must be isolated. List sessions with the same `agentId` to keep follow-up operations scoped.

## Long-Running And Interactive Work

- For builds, tests, installs, and other long-running commands, set `waitForCompletion: false`, retain the returned session ID, and read incremental output with `terminal.read` until the command finishes.
- A command timeout does not close or cancel the process. Read the same session to inspect progress or the eventual exit result; do not launch a duplicate command just because a wait timed out.
- Use `terminal.input` only after reading a prompt from that session. Send one response per prompt and read again before answering another.
- Use `terminal.execute` to run a follow-up command in a known session. Use `terminal.close` only when the session is no longer needed and the running operation may safely be terminated.
- `terminal.read` supports a negative offset for reading from the end of the output buffer; use a bounded number of lines.

## Safety And Scope

- EsiMCP applies configured blocked-command patterns and optional allowed-directory checks. These are guardrails, not a sandbox; review the command, working directory, and affected files before execution.
- Do not execute destructive database, filesystem, deployment, or system commands without explicit user authorization. Do not work around a blocked command by splitting or re-encoding it.
- Keep commands within the requested repository and project scope. Do not terminate unrelated user terminals or processes.
- When an operation is tied to VS Code debugging or Studio lifecycle, load and follow `vscode-debug` as well; terminal access does not replace the debug lifecycle rules.
