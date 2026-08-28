# Esi.AI.Studio.Client

## Zweck

`Esi.AI.Studio.Client` ist das browserseitige Blazor WebAssembly Projekt. Es enthaelt die Komponenten, Seiten und Layouts, die im Interactive-WebAssembly-Modus des Studio Hosts geladen werden.

## Technologie

- SDK: `Microsoft.NET.Sdk.BlazorWebAssembly`
- Target Framework: `net10.0`
- Solution: `src/Esi.AI/Esi.AI.Studio.sln`
- Launch Settings: werden vom Host verwaltet (`NoDefaultLaunchSettingsFile=true`)

## Wichtige Bereiche

- `Pages/`: routbare Seiten und Template-Seiten.
- `Layout/`: gemeinsame Client-Layouts.
- `_Imports.razor`: gemeinsame Razor-Namespaces.
- `wwwroot/`: statische Client-Ressourcen.

## Abhaengigkeiten

- `Microsoft.AspNetCore.Components.WebAssembly` `10.0.11`.
- `Microsoft.AspNetCore.Components.WebAssembly.Authentication` `10.0.11`.

Der Client wird vom Projekt `Esi.AI.Studio` als zusaetzliche Assembly fuer den WebAssembly-Render-Modus eingebunden.

## Start und Build

Der Client wird normalerweise ueber den Studio Host gestartet:

```bash
dotnet build src/Esi.AI/Esi.AI.Studio.Client/Esi.AI.Studio.Client.csproj
dotnet run --project src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj
```

## Aktueller Stand

Die vorhandenen Seiten stammen aus dem Blazor-Template. Die fachlichen Studio-Funktionen werden spaeter ergaenzt.
