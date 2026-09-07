---
applyTo: "**/*.cs,**/*.csproj,**/*.razor,**/*.razor.css,.vscode/**"
description: "C# Dev Kit debugging, dynamic launch configurations, and Hot Reload for Esi.AI Studio"
---

# Esi.AI Studio mit C# Dev Kit

- Verwende für Esi.AI Studio den Microsoft C# Dev Kit Workflow. C# Dev Kit ermittelt das Projekt und erstellt die Debugkonfiguration dynamisch im Speicher.
- Erzeuge oder verlange für den normalen C#-Dev-Kit-Start keine `.vscode/launch.json` und keine `.vscode/tasks.json`. Diese Dateien sind nur für ausdrücklich benötigte manuelle VS-Code-Debugkonfigurationen vorgesehen.
- Starte das Serverprojekt über den C#-Dev-Kit-Befehl `csdevkit.debug.projectDebugLaunch` beziehungsweise über **Start New Instance** im Solution Explorer. Verwende `csdevkit.debug.noDebugProjectLaunch` nur für einen Lauf ohne Debugger.
- Verwende `csdevkit.debug.selectStartupProject`, wenn das Startprojekt nicht eindeutig ist.
- Verwende `csdevkit.debug.hotReload` für das Anwenden von Änderungen und `csdevkit.debug.showHotReloadPanel` zur Diagnose von Hot Reload.
- `launchSettings.json` bleibt zulässig und relevant für ASP.NET-/Blazor-Profile, `applicationUrl`, Development-Umgebung und die Microsoft-`inspectUri`. Es ersetzt keine C#-Dev-Kit-Dynamikkonfiguration.
- `UseWebAssemblyDebugging()` bleibt in der Development-Pipeline erforderlich, wenn Blazor-WebAssembly-Breakpoints über den Blazor-Debugproxy unterstützt werden sollen.
- Prüfe vor dem Start, dass kein verwaister Studio-Prozess Port `7010` belegt. Führe keine zweite Studio-Instanz parallel aus.
- Hot Reload wird zuerst für unterstützte Änderungen verwendet. Bei C#-Debugsessions ist `csharp.experimental.debug.hotReload` erforderlich; `csharp.debug.hotReloadOnSave` aktiviert das Anwenden beim Speichern.
- Für ASP.NET Core auf Linux/macOS sind C#-Hot-Reload-Änderungen insbesondere auf `.cs`-Dateien beschränkt. Razor-, CSS- und Markup-Änderungen können zusätzlich über den Blazor-/`dotnet watch`-Mechanismus unterstützt werden und müssen im laufenden Browser geprüft werden.
- Ein Neustart ist nur erforderlich, wenn die Änderung vom aktiven Hot-Reload-Mechanismus nicht unterstützt wird oder Projektdateien, Pakete, Runtime-Verträge, native Bibliotheken oder Buildmetadaten betroffen sind.
- Für agentengesteuerte C#-Dev-Kit-Aktionen verwende die EsiMCP-Werkzeuge `csharp_devkit_list_commands` und `csharp_devkit_execute_command`, um die freigegebenen Commands zu prüfen und gezielt auszuführen.
- Für Terminalaktionen verwende ausschließlich `vscode_terminal_list_commands` und `vscode_terminal_execute_command` mit den `terminal.*`-Command-IDs.
- `csharp_devkit_execute_command` darf ausschließlich die gelisteten C#-Dev-Kit-Commands einschließlich `csdevkit.debug.active.session`, `csdevkit.debug.stop` und `csdevkit.debug.restart` erhalten. Der alte EsiMCP-Debug-Lifecycle wird nicht verwendet.
- Für den normalen Start wird `csdevkit.debug.projectDebugLaunch` beziehungsweise **Start New Instance** verwendet. Für Hot Reload wird `csdevkit.debug.hotReload` verwendet; `csdevkit.debug.showHotReloadPanel` dient nur der Diagnose. Beliebige eigene `coreclr`-Konfigurationen in `launch.json` werden nicht vorausgesetzt.
