# Optimajet Flow Integration

## Goal
Add a Flow page to Esi.AI Studio and route incoming chat messages and requests through Optimajet WorkflowEngine.NET before backend execution. Include the upstream engine under `origins/` and reference it as a project.

## Completed
- [x] Added Optimajet WorkflowEngine.NET as a shallow Git submodule at `origins/optimajet/WorkflowEngine.NET` (commit `a2dbda0`, version 22.0.0).
- [x] Added a Studio server `ProjectReference` to Optimajet's .NET 10 SQLite provider project.
- [x] Added a hosted Optimajet runtime using the existing Studio SQLite connection, per-request workflow instances, cancellation, and cleanup.
- [x] Routed Studio chat and OpenAI-compatible chat requests through one `DataService` orchestration path before existing inference/backend dispatch.
- [x] Seeded published chat, vision, and tool-routing definitions when Studio initializes.
- [x] Replaced the Elsa canvas on `/flow` with a SignalR-backed editor for selecting existing backend profiles and built-in routes.
- [x] Shared case-insensitive backend-route parsing between the Flow page and server routing so persisted PascalCase definitions load correctly.
- [x] Added Optimajet execution, request-classification, published-flow, API-routing, and fail-closed tests.
- [x] Fixed the pre-existing `EsiAiWorkflowDesigner.razor` compile error that blocked the Studio build.

## Validation
- Studio build passed on 2026-09-26 with `--no-restore`; three `NU1608` Roslyn dependency-version warnings remain.
- Full `Esi.AI.Studio.Tests` suite passed: 91 passed, 1 skipped, 0 failed. Focused `FlowRoutingServiceTests` passed 10/10, including the PascalCase persisted-definition regression.
- EsiMCP's project-launch adapter was corrected in version 2.0.7: `{ path }` is now converted with `vscode.Uri.file(path)` before invoking `csdevkit.debug.projectDebugLaunch`. This was the cause of the previous accepted-but-timed-out launch; it was not caused by the interaction-status implementation. Unit and Extension Host/MCP lifecycle tests passed, and the versioned extension was installed/reloaded.
- Live launch validation succeeds: EsiMCP returned `completed`, `started: true`, and session ID `0385fc88-ba76-4685-966d-a64b03ef747f`; `active.session` returned the same ID and host readiness returned `{ "ready": true }`.
- Browser verification of `http://localhost:7010/flow` succeeded. The existing `Chat request router` draft rendered with `Loaded local model` selected, confirming persisted PascalCase route properties parse correctly. Existing default workflows remain unpublished drafts in the runtime database; validation did not publish or modify them.

## Architecture And Release Notes
- `FlowDefinition` remains the persisted workflow configuration; `ModelConfiguration` remains the backend allowlist. `DataService` orchestrates both ingress paths; `OpenAiCompatibleController` remains the only API controller and retains its contract.
- The browser does not call the Optimajet Designer HTTP API. The Flow page stores route JSON through the existing SignalR path, and the server translates it into an Optimajet XML process.
- Optimajet's repository includes an EULA rather than an OSI open-source license. Review the trial, developer, source-use, and redistribution terms before sharing or release. The upstream origin remains a submodule; no Designer assets or upstream source were copied into Esi.AI.

## Constraints
- Keep browser-to-server application operations on the existing `IDataService`/SignalR/`DataHub` path; retain the single OpenAI-compatible controller contract.
- Keep shared DTOs in `Esi.AI.Models` and backend inference ownership in existing server services.
- Build and test only projects under `src/` in the `Esi.*` namespace.
- Do not add any Designer HTTP route; the Flow UI persists through the existing SignalR path.
