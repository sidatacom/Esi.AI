# Copilot Instructions

## Application Architecture

- The application exposes exactly one HTTP API controller: `OpenAiCompatibleController`.
- Do not add additional API controllers, Minimal API endpoints, or ad hoc HTTP endpoints for application features unless explicitly requested.
- The controller is reserved for the OpenAI-compatible API contract. Keep browser application functionality out of it.
- All other client-to-server application communication goes through the central SignalR hub `DataHub` at `/hubs/data`.
- The client accesses server functionality through `IDataService` and its `SignalRDataService` implementation. Add new operations to this service and the corresponding `DataHub` method instead of introducing direct HTTP calls.
- Hub methods should delegate application work to the existing server-side services, especially `DataService`, rather than duplicating business logic in the hub.
- Blazor pages and components should depend on the client-side service abstraction, not directly on `HubConnection`, controllers, or server implementations.
- All DTOs, including request, response, status, and SignalR contract types, belong in `Esi.AI.Models`.
- Do not define application DTOs in the Web project, Client project, Hub, controller, or service layer.
- Keep DTOs free of transport-specific behavior so the same types can be used by the controller boundary and SignalR contracts.

