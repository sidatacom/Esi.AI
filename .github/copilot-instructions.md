# Copilot Instructions

## Application Architecture

- The application exposes exactly one HTTP API controller: `OpenAiCompatibleController`.
- Do not add additional API controllers, Minimal API endpoints, or ad hoc HTTP endpoints for application features unless explicitly requested.
- The controller is reserved for the OpenAI-compatible API contract. Keep browser application functionality out of it.
- All other client-to-server application communication goes through the central SignalR hub `DataHub` at `/hubs/data`.
- The client accesses server functionality through `IDataService` and its `SignalRDataService` implementation. Add new operations to this service and the corresponding `DataHub` method instead of introducing direct HTTP calls.
- Do not introduce application forms or form-submit handlers for client-to-server operations. Use the central `IDataService`/`SignalRDataService` path and the corresponding `DataHub` method for all such actions.
- Hub methods should delegate application work to the existing server-side services, especially `DataService`, rather than duplicating business logic in the hub.
- Blazor pages and components should depend on the client-side service abstraction, not directly on `HubConnection`, controllers, or server implementations.
- All DTOs, including request, response, status, and SignalR contract types, belong in `Esi.AI.Models`.
- Do not define application DTOs in the Web project, Client project, Hub, controller, or service layer.
- Keep DTOs free of transport-specific behavior so the same types can be used by the controller boundary and SignalR contracts.

## SignalR Collection CRUD

- Every server-owned entity that maintains a client-visible collection must expose explicit CRUD operations named `<Entity>_Create`, `<Entity>_Read`, `<Entity>_Update`, and `<Entity>_Delete`. This applies to existing entities when they are changed as well as to new entities.
- Use the same entity-based CRUD names in `IDataService` and `DataService`; client wrappers may add the `Async` suffix but must preserve the `<Entity>_<Operation>` stem. Do not expose generic verbs such as `CreateChatAsync`, `GetModelsAsync`, or `SaveProfileAsync` for collection entities.
- Use the exact operation names on `DataHub` and in SignalR event names. Client-side C# wrappers may add the `Async` suffix, for example `LoadedModel_ReadAsync`, but must invoke the exact hub operation `LoadedModel_Read`.
- `<Entity>_Create` must publish the new collection item or collection snapshot as soon as creation begins. For long-running work, create the pending item before starting the operation.
- `<Entity>_Update` must be pushed by the server when the entity state changes. The browser must subscribe to the SignalR update and must not poll the hub while the originating operation is running.
- `<Entity>_Read` must return the current collection from the server-owned source of truth and must be used during full page initialization/reload.
- `<Entity>_Delete` must be published when an item is removed, cancelled, or fails to complete. The client must reconcile its local collection from the received contract.
- `IDataService` owns application orchestration, `DataHub` delegates to `IDataService`, and a server-side publisher adapts collection CRUD changes to SignalR. Do not put collection business logic in the hub or directly in a Blazor component.
- Collection DTOs and SignalR payloads belong in `Esi.AI.Models` and must remain transport-independent.

