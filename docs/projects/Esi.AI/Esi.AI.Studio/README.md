# Esi.AI.Studio

## Zweck

`Esi.AI.Studio` ist der ASP.NET Core Host der Blazor Web App. Er stellt die serverseitige Laufzeit, Identity, Datenbankzugriff und die Endpunkte fuer die Client- und Identity-Komponenten bereit.

## Technologie

- SDK: `Microsoft.NET.Sdk.Web`
- Target Framework: `net10.0`
- Blazor Render-Modi: Interactive Server und Interactive WebAssembly
- Authentifizierung: ASP.NET Core Identity mit Einzelkonten
- Datenbank: SQLite ueber `Data/app.db`
- Solution: `src/Esi.AI/Esi.AI.Studio.sln`

## Wichtige Bereiche

- `Program.cs`: Registrierung von Razor Components, Render-Modi, Authentication und Identity.
- `Components/`: Host-Layout, Routing und Identity-Komponenten.
- `Data/`: `ApplicationDbContext`, `ApplicationUser` und Migrationen.
- `wwwroot/`: statische Web-Ressourcen.
- `Controllers/OpenAiCompatibleController.cs`: OpenAI-kompatible Inference-API.
- `Hubs/DataHub.cs` und `DataService`: SignalR-Anwendungsoperationen und Orchestrierung.

## Abhaengigkeiten

- `Esi.AI.Studio.Client` als Projekt-Referenz.
- ASP.NET Core Components WebAssembly Server `10.0.11`.
- ASP.NET Core Identity EntityFrameworkCore `10.0.11`.
- Entity Framework Core SQLite `10.0.11`.

## Start und Build

```bash
dotnet restore src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj
dotnet build src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj
dotnet run --project src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj
```

Die SQLite-Verbindungszeichenfolge steht in `appsettings.json`. In der Entwicklungsumgebung aktiviert die App den Migrations-Endpunkt.

## VS Code, Debugging und Hot Reload

Der verbindliche Entwicklungsablauf mit `dotnet watch`, CoreCLR-Server-Debugging,
WebAssembly-Attach und dem Linux-Polling-Watcher ist in
[Esi.AI Studio: Debugging und Hot Reload](../development/studio-development.md)
dokumentiert.

## Aktueller Stand

Studio hostet die interaktive Client-Anwendung, Identity und die OpenAI-kompatible API. Fachliche Seiten wie Backends, Models, Chats, Flow, Provider, Settings und WebAPI liegen im Clientprojekt. Backend-Runtime-Migration und Flow-Ausfuehrung sind teilweise beziehungsweise noch nicht vollstaendig integriert; Details stehen in den jeweiligen Architektur- und Backend-Dokumenten.
