---
applyTo: '**/Esi.AI.Studio/**/*.cs,**/Esi.AI.Studio.Client/**/*'
description: 'Verified Esi.AI Studio architecture and lifecycle constraints'
---

# Esi.AI Studio Architecture

- Treat `Esi.AI.Studio` as the server host and `Esi.AI.Studio.Client` as the Interactive Auto client.
- Keep the application HTTP surface limited to `OpenAiCompatibleController`. Do not add feature-specific controllers, Minimal API endpoints, or direct browser HTTP calls.
- Route browser-to-server application operations through `IDataService`, `SignalRDataService`, and the central `DataHub` at `/hubs/data`.
- Keep application DTOs and SignalR contracts in `Esi.AI.Models`. DTOs must remain transport-independent.
- Keep business orchestration in `DataService` and runtime ownership in `ModelRuntime`; hub methods and components should delegate rather than duplicate that logic.
- For client-visible collections, preserve the explicit `<Entity>_Create`, `<Entity>_Read`, `<Entity>_Update`, and `<Entity>_Delete` lifecycle. The server owns the source of truth and publishes state changes through SignalR.
- Treat `LoadedModel_Create`, `LoadedModel_Read`, `LoadedModel_Update`, and `LoadedModel_Delete` as the model collection lifecycle contract.
- Keep inference failure handling backend-independent. Backend generation failures must use the central fatal lifecycle coordinator, which unloads all runtimes before requesting host shutdown. Do not mask native failures with tool truncation, fixed tool limits, schema rewriting, or provider-only workarounds.
- Before changing a shared path, identify the owning abstraction, the server source of truth, the SignalR contract, and the narrowest behavior test.
- Consider direct event subscriptions and channel-based streaming orchestration as risk areas. Verify their current implementation before refactoring and add focused lifecycle tests for changes.
- Do not reintroduce watchdogs, PID files, startup gates, or process-isolation workarounds for Blazor debugging. Use the normal VS Code C# debug lifecycle.
- Start potentially long-running model loads, debug sessions, servers, watchers, builds, and tests asynchronously from the beginning. Use synchronous execution only for short bounded checks.
