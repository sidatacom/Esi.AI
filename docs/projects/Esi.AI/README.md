# Esi.AI Projekte

Diese Dokumentation beschreibt die aktuellen Projekte unter `src/Esi.AI`.

## Projektuebersicht

- [Esi.AI.Studio](Esi.AI.Studio/README.md): ASP.NET Core Blazor Web App und Server-Host.
- [Esi.AI.Studio.Client](Esi.AI.Studio.Client/README.md): interaktiver Blazor-Client.
- [Esi.AI.Core](Esi.AI.Core/README.md): Core- und Runtime-Integrationen.
- [OpenAI-kompatible WebAPI und VS-Code-Provider](OpenAI-Compatible-WebAPI-and-Model-Provider.md)
- [Studio Layer-Design](studio-layers-design.md): Zielbild fuer Backend-, Flow- und Request-Ebene.
- [Backend-Dokumentation](backends/): Backend-Pakete, Runtime Gallery, Tool Calling und Python/gRPC.
- [Entwicklungsanleitungen](development/): Studio-Debugging und EsiMCP-Lifecycle.
- [Modelle](models/): Referenzmodelle und Smoke-Tests.
- [Benchmarks](benchmarks/README.md): Messergebnisse und Benchmarknotizen.

## Solution und Voraussetzungen

Die aktuelle Studio-Solution liegt unter `src/Esi.AI/Esi.AI.Studio.sln` und enthaelt `Esi.AI.Studio`, `Esi.AI.Studio.Client`, `Esi.AI.Workflow`, `Esi.AI.Core` und `Esi.AI.Models`. Backend- und Testprojekte liegen ebenfalls unter `src/Esi.AI`, sind aber nicht Teil dieser Solution. Fuer den Studio-Build wird das im Repository konfigurierte .NET SDK verwendet; konkrete Build- und Debug-Ablaufe stehen in [Studio-Entwicklung](development/studio-development.md).
