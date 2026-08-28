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
- `Components/`: Layout, Routing, Seiten und Identity-Komponenten.
- `Data/`: `ApplicationDbContext`, `ApplicationUser` und Migrationen.
- `wwwroot/`: statische Web-Ressourcen.

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

## Aktueller Stand

Das Projekt basiert auf dem Blazor-Template. Fachliche Studio-Seiten und die spaetere LLama-Integration sind noch nicht umgesetzt.
