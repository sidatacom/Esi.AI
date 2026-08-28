# Esi.AI.LLama

## Zweck

`Esi.AI.LLama` ist eine .NET Class Library fuer die kuenftige LLamaSharp-Anbindung im Esi.AI-System. Das Projekt bildet die Integrationsgrenze fuer lokale LLM-Inferenz und soll spaeter vom Studio Host verwendet werden.

## Technologie

- SDK: `Microsoft.NET.Sdk`
- Target Framework: `net10.0`
- Projektpfad: `src/Esi.AI/Esi.AI.LLama`
- Solution: `src/Esi.AI/Esi.AI.Studio.sln`
- Vorgesehener Root-Namespace: `Esi.AI.Llm`

## LLamaSharp-Quelle

Das Projekt referenziert die Core-Bibliothek direkt aus dem lokalen Fork:

```text
../../../origins/sidatacom/LLamaSharp/LLama/LLamaSharp.csproj
```

Der Fork liegt unter `origins/sidatacom/LLamaSharp` und verwendet aktuell die Version `0.28.0` im Quellprojekt. Die direkte ProjectReference ermoeglicht Aenderungen am Fork und spaetere Pull Requests an `SciSharp/LLamaSharp`.

## Build

```bash
dotnet restore src/Esi.AI/Esi.AI.LLama/Esi.AI.LLama.csproj
dotnet build src/Esi.AI/Esi.AI.LLama/Esi.AI.LLama.csproj
```

Beim Build wird aus dem Fork das kompatible `net8.0` Target verwendet. LLamaSharp kann dabei native Laufzeitdateien vorbereiten; ein konkretes CPU-, CUDA- oder Vulkan-Backend ist noch nicht als Esi.AI-Abhaengigkeit festgelegt.

## Aktueller Stand

Das Projekt enthaelt noch die generierte Platzhalterklasse. Die explizite Namespace-Deklaration der Platzhalterklasse verwendet derzeit `Esi.AI.LLama`; neue Esi.AI-Typen muessen gemaess Repository-Konvention unter `Esi.AI.Llm` angelegt werden.

## Naechste Integrationsschritte

1. LLamaSharp-Konfiguration und Modellpfade kapseln.
2. Ein Esi.AI-eigenes Interface fuer Inferenz definieren.
3. Einen passenden nativen Backend-Pfad fuer die Zielumgebung festlegen.
4. Die Registrierung im Studio Host ueber Dependency Injection ergaenzen.
