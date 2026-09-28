# EsiMCP Debug Lifecycle

This guide covers native EsiMCP project/configuration launch, rebuild, readiness, and stop behavior. EsiMCP uses public VS Code APIs; the standalone `ms-dotnettools.csharp` extension may supply the `coreclr` adapter. C# Dev Kit is not required.

## Prerequisites

- Open the project in the VS Code workspace.
- Install the EsiMCP extension and .NET SDK.
- Install the standalone C# extension when using the `coreclr` adapter.
- For Studio, keep port `7010` available and use the correct Development launch settings.

Discover the current command schemas with `vscode_debug_list_commands`. Execute lifecycle operations through `vscode_debug_execute_command` with object-valued `arguments`.

## Launch a Project

`debug.launchProject` accepts a `.csproj` path and optional workspace folder, target framework, and MSBuild configuration. The project must be inside the selected workspace folder. EsiMCP stops all active VS Code debug sessions and their debugger-owned launch processes, runs `dotnet build`, resolves the MSBuild `TargetPath`, and launches it through `coreclr`.

```json
{
  "commandId": "debug.launchProject",
  "arguments": {
    "projectFile": "src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj",
    "workspaceFolder": "/home/llm/Git/Esi.AI",
    "configuration": "Debug"
  }
}
```

Build failures return `stage: "build"`, the process exit `resultCode`, and `buildLogPath`. Debug startup refusal/errors return `stage: "debug"`, `resultCode`, and `debugLogPath`. Logs are stored in the OS temporary directory under `esi-mcp/debug/`. A successful launch returns a session ID and the build log path.

## Launch a Configuration

`debug.launchFile` selects a unique configuration by name from `.vscode/launch.json`. The configuration must use `request: "launch"` and provide either a workspace-relative `projectFile` or a `preLaunchTask` defined as a VS Code `process` task. EsiMCP captures process-task output, removes `preLaunchTask` before calling the debugger to avoid running the build twice, and does not execute shell tasks or unresolved task variables.

```json
{
  "commandId": "debug.launchFile",
  "arguments": {
    "configurationName": "Esi.AI Studio",
    "workspaceFolder": "/home/llm/Git/Esi.AI"
  }
}
```

Build and debugger startup failures use the same structured result/log-path contract described above. A named configuration must be unique; attach configurations are not accepted by this launch operation.

## Hot Reload And Rebuild

For live code updates without C# Dev Kit, start a tracked .NET SDK watch task with `mode: "watch"`. It stops active debug sessions, starts the project under `dotnet watch`, applies supported edits live, and restarts the app only when an edit cannot be applied. This runs without an attached VS Code debugger; use the normal launch flow when breakpoints are required.

```json
{
  "commandId": "debug.hotReload",
  "arguments": {
    "mode": "watch",
    "projectFile": "src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj",
    "workspaceFolder": "/home/llm/Git/Esi.AI"
  }
}
```

Stop the tracked task with `mode: "stopWatch"`. Starting the same project twice reuses the existing task; starting another project stops the previous task first.

Use `mode: "rebuild"` only when a full build and debugger relaunch is explicitly needed. Pass exactly one project or configuration target. EsiMCP stops Watch and debug sessions before the build and starts a new debug session only after a successful build:

```json
{
  "commandId": "debug.hotReload",
  "arguments": {
    "mode": "rebuild",
    "projectFile": "src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj",
    "workspaceFolder": "/home/llm/Git/Esi.AI"
  }
}
```

Stopping a session terminates the host owned by that debugger launch. EsiMCP does not scan for or kill arbitrary detached processes; verify port ownership separately before a fresh Studio start and stop only a process whose project ownership is established.

## Studio Readiness And Stop

After launch, inspect `debug.active.session`, then call `debug.check.host.readyness`. Do not infer readiness from the accepted launch result alone. Verify the actual Studio route only after readiness succeeds.

Use `debug.stop` for the active debug session. Then verify that the session is gone and that port `7010` is no longer held by the Studio process. Before independent builds/tests of Studio, stop its debug session first.