# Flow Integration Plan

## Goal
Add a Flow page to Esi.AI Studio and route incoming chat messages/requests through Optimajet WorkflowEngine.NET before backend execution. Include the upstream engine under `origins/` and reference it as a project.

## Completed
- [x] Added Optimajet WorkflowEngine.NET as a shallow Git submodule at `origins/optimajet/WorkflowEngine.NET` (commit `a2dbda0`, version 22.0.0).
- [x] Added a Studio server `ProjectReference` to Optimajet's .NET 10 SQLite provider project.
- [x] Added a hosted Optimajet runtime using the existing Studio SQLite connection, per-request workflow instances, cancellation, and cleanup.
- [x] Routed Studio chat and OpenAI-compatible chat requests through one `DataService` orchestration path before the existing inference/backend dispatch.
- [x] Seeded published chat, vision, and tool-routing definitions when Studio initializes.
- [x] Replaced the Elsa canvas on `/flow` with a SignalR-backed editor for selecting existing backend profiles and built-in routes.
- [x] Added Optimajet execution, request-classification, published-flow, API-routing, and fail-closed tests.
- [x] Fixed the pre-existing `EsiAiWorkflowDesigner.razor` compile error that blocked the initial Studio build.
- [x] Built the Studio project and passed the full Studio test suite: 90 passed, 1 skipped. The focused routing suite passes 9/9.
- [x] Updated the session history at `docs/history/20260925-190021.md`.

## Limitations / Follow-up
- The C# Dev Kit launch command returned `running`, but `csdevkit.debug.active.session` stayed `null`, port `7010` remained free, and structured diagnostics returned `null`. Per lifecycle instructions, no blind second launch was attempted; browser verification is therefore blocked in this session.
- The build still reports three existing `NU1608` Roslyn package-version warnings.
- Optimajet's repository includes an EULA rather than an OSI open-source license. Review its trial, developer, source-use, and redistribution terms before sharing or releasing this integration. The upstream origin remains a submodule; no designer assets or source were copied into Esi.AI.

## Constraints
- Keep browser-to-server application operations on the existing `IDataService`/SignalR/`DataHub` path; retain the single OpenAI-compatible controller contract.
- Keep shared DTOs in `Esi.AI.Models` and backend inference ownership in existing server services.
- Preserve existing user changes, and build/test only projects under `src/` in the `Esi.*` namespace.
- Do not add any Designer HTTP route; the Flow UI persists through the existing SignalR path.
