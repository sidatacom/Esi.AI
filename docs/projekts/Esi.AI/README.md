# Esi.AI Projekte

Diese Dokumentation beschreibt die aktuellen Projekte unter `src/Esi.AI`.

## Projekte

- [Esi.AI.Studio](Esi.AI.Studio/README.md): ASP.NET Core Blazor Web App und Host fuer Identity, SQLite und interaktive Komponenten.
- [Esi.AI.Studio.Client](Esi.AI.Studio.Client/README.md): Blazor WebAssembly Client fuer die Auto-Interaktivitaet.
- [Esi.AI.LLama](Esi.AI.LLama/README.md): Class Library fuer die lokale LLamaSharp-Anbindung.

## Solution

Alle drei Projekte sind aktuell in `src/Esi.AI/Esi.AI.Studio.sln` eingetragen.

## Gemeinsame Voraussetzungen

- .NET SDK 10.0
- Restore mit `dotnet restore`
- Build mit `dotnet build`

Der LLamaSharp-Code wird lokal aus `origins/sidatacom/LLamaSharp` referenziert. Native Backend-Pakete und produktive Modellkonfiguration sind noch nicht festgelegt.
