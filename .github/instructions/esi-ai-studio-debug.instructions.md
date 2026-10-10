---
applyTo: '**/Esi.AI.Studio/**/*.cs,**/Esi.AI.Studio.Client/**/*,**/Esi.AI.Studio/**/*.razor,**/Esi.AI.Studio/**/*.razor.css'
description: 'Esi.AI Studio debugging and lifecycle constraints using EsiMCP and public VS Code APIs'
---

# Esi.AI Studio Debugging

- Before starting, stopping, restarting, testing, or browser-checking Studio, load and follow `.github/skills/vscode-debug/SKILL.md`.
- EsiMCP owns project/profile selection and launch orchestration. Use `debug.launchProject` as the standard Studio project start with its `.csproj`; the selected `Properties/launchSettings.json` profile supplies URL and environment, with optional `launchProfile` selection. Use `debug.launchFile` only for an explicitly required Studio-specific named VS Code configuration. Both paths stop active debug sessions, build with captured output, and then use public VS Code Debug APIs; never invoke C# Dev Kit commands, command IDs, brokers, UI, or project models.
- The standalone `ms-dotnettools.csharp` extension may provide the `coreclr` debug adapter, but project resolution and launch configuration remain owned by EsiMCP.
- For live updates without C# Dev Kit, use `debug.hotReload` with `mode: "watch"` and the Studio `.csproj`. It stops debugger sessions and runs the host under `dotnet watch`, which applies supported edits live and restarts only when required by an unsupported edit. This does not retain a VS Code debugger session. Use explicit `mode: "rebuild"` only when a debugger relaunch is necessary; then verify host readiness and the changed behavior.
- For client-side breakpoints, preserve the Microsoft `inspectUri` in `Properties/launchSettings.json` and enable `UseWebAssemblyDebugging()` in Development.
- Before a fresh Studio start, verify no stale Studio process owns port `7010`; after launch, verify the actual debug session and host readiness before browser checks.
