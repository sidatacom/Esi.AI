# Esi.AI.Core

## Zweck

`Esi.AI.Core` ist die zentrale .NET-Bibliothek fuer Modellruntime und Inferenzintegration. Sie enthaelt unter anderem `ModelRuntime`, konkrete LLamaSharp-, OpenVINO- und dotLLM-Lader sowie die Python/gRPC-Bruecke fuer vLLM und SGLang. Der Studio-Host orchestriert diese Laufzeiten ueber seine Services.

## Technologie

- SDK: `Microsoft.NET.Sdk`
- Target Framework: `net10.0`
- Projektpfad: `src/Esi.AI/Esi.AI.Core`
- Solution: `src/Esi.AI/Esi.AI.Studio.sln`
- Root-Namespace: `Esi.AI.Core`

## LLamaSharp-Quelle

Das Projekt referenziert die Core-Bibliothek direkt aus dem lokalen Fork:

```text
../../../origins/sidatacom/LLamaSharp/LLama/LLamaSharp.csproj
```

Der Fork liegt unter `origins/sidatacom/LLamaSharp` und verwendet aktuell die Version `0.28.0` im Quellprojekt. Die direkte ProjectReference ermoeglicht Aenderungen am Fork und spaetere Pull Requests an `SciSharp/LLamaSharp`.

## Build

```bash
dotnet restore src/Esi.AI/Esi.AI.Core/Esi.AI.Core.csproj
dotnet build src/Esi.AI/Esi.AI.Core/Esi.AI.Core.csproj
```

Beim Build wird aus dem Fork das kompatible `net8.0` Target verwendet. Zusaetzliche Backend-Projekte unter `src/Esi.AI` kapseln bereits Teile der LLama-Runtime separat; die vollstaendige Host-Integration und Migration der Legacy-Lader ist noch nicht abgeschlossen.

## Aktueller Stand

Das Projekt enthaelt die bestehenden Laufzeitadapter und Teile der bisherigen Runtime-Orchestrierung. Die schrittweise Extraktion in `Esi.AI.Backend.*` ist im [Backend-Migrationsstand](../backends/backend-assemblies.md) beschrieben.

## Naechste Integrationsschritte

1. Verbleibende Runtime-Implementierungen mit gezielten Lifecycle- und Generierungstests aus Core herausloesen.
2. Die Backend-Auswahl des Studio-Hosts schrittweise auf den gemeinsamen `IBackendRuntime`-Vertrag migrieren.
3. Engine-spezifische Referenzen aus Core entfernen, sobald alle benoetigten Backend-Module integriert sind.
