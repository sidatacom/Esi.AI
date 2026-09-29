# Copilot Instructions

## Verbindliches Skill-Routing

- Bei EsiMCP-Debugoperationen (Start/Stop/Restart, Debugzustand, Breakpoints, Host-Readiness, Hot Reload oder anschließender Browserprüfung) muss vor dem ersten Toolaufruf `.github/skills/vscode-debug/SKILL.md` vollständig gelesen und befolgt werden. Verwende ausschließlich `vscode_debug_list_commands` und `vscode_debug_execute_command` für Debugoperationen.
- Bei EsiMCP-Terminaloperationen muss vor dem ersten Toolaufruf `.github/skills/vscode-terminal/SKILL.md` vollständig gelesen und befolgt werden. Verwende ausschließlich `vscode_terminal_list_commands` und `vscode_terminal_execute_command` für EsiMCP-Terminals.
- Bei EsiMCP-Microsoft-Access-Operationen muss vor dem ersten Toolaufruf `.github/skills/msaccess/SKILL.md` vollständig gelesen und befolgt werden. Verwende ausschließlich `msaccess_list_commands` und `msaccess_execute_command` für Access-Tools; Ressourcen und Prompts bleiben eigene MCP-Methoden.
- Vor jedem Starten, Stoppen, Neustarten oder Prüfen von `Esi.AI.Studio`, jeder Debugsession, jedem Hot-Reload-Lauf und jeder anschließenden Browserprüfung muss der Skill `vscode-debug` geladen und vollständig befolgt werden.
- Das gilt auch dann, wenn der Benutzer nur indirekt von Starten, Testen, Browserprüfung, Port `7010`, Debugging oder Hot Reload spricht.
- Vor dem ersten Lifecycle-Toolaufruf muss `.github/skills/vscode-debug/SKILL.md` gelesen werden. Für EsiMCP-Lifecycle-Aktionen sind ausschließlich `vscode_debug_list_commands` und `vscode_debug_execute_command` sowie `vscode_terminal_list_commands` und `vscode_terminal_execute_command` zu verwenden.
- C# Dev Kit ist keine Abhängigkeit und darf in EsiMCP weder integriert noch über MCP-Tools, Command-IDs, Broker, UI oder dynamische Projektkonfigurationen verwendet werden. Die MIT-lizenzierte Standalone-Erweiterung `ms-dotnettools.csharp` darf als Debug-Adapter verwendet werden; EsiMCP startet Sessions ausschließlich über öffentliche VS Code APIs.
- Debug-Projektstarts laufen über EsiMCP `debug.launchProject` (Projektpfad) oder `debug.launchFile` (Name aus `.vscode/launch.json`). Beide stoppen aktive Debugsessions vor dem Build und starten anschließend über öffentliche VS-Code-APIs. Für Live-Updates ohne C# Dev Kit `debug.hotReload` mit `mode: "watch"` und einem `.csproj` verwenden: `dotnet watch` wendet unterstützte Edits live an und startet nur bei nicht unterstützten Änderungen neu. Diese Watch-Session ist nicht an den VS-Code-Debugger angehängt. `mode: "rebuild"` bleibt der ausdrückliche vollständige Build mit Debugger-Neustart.
- Bei Konflikten zwischen einer allgemeinen Vorgehensweise und `vscode-debug` hat `vscode-debug` für Debug-, Start-, Restart-, Hot-Reload- und Browser-Lifecycle Vorrang.

## Root-Cause-Regel

- Probleme werden an ihrer Ursache gelöst, nicht durch symptomatische Patches, Workarounds oder zusätzliche Fallbacks.
- Vor jeder Änderung muss der konkrete kontrollierende Codepfad identifiziert werden: Wer erzeugt den fehlerhaften Zustand, wer mutiert ihn und welche Abstraktion besitzt den Vertrag?
- Eine Änderung ist erst ausreichend, wenn sie den ursprünglichen Fehler reproduzierbar verhindert und der betroffene Ablauf getestet wurde. Eine lediglich sichtbare Änderung oder ein unterdrückter Fehler gilt nicht als Lösung.
- Bei widersprüchlichem Zustand müssen die Zustandsquelle und die Synchronisationsgrenze korrigiert werden; doppelte lokale Sonderlogik darf nicht als Ersatz für eine konsistente Quelle der Wahrheit eingeführt werden.
- Keine stillen Fallbacks, keine alternativen Startwege und kein "funktioniert irgendwie"-Verhalten, wenn der eigentliche Pfad fehlerhaft ist. Wenn eine Annahme nicht belegt ist, muss sie durch den nächstliegenden Test oder eine gezielte Diagnose falsifiziert werden.
- Nach der Ursachenänderung sind die ursprüngliche Fehlersituation, angrenzende Zustandsübergänge und die relevante Regression gezielt zu validieren.

## Esi.AI Studio Startregel

- Starte das Studio über `debug.launchProject` mit dem Projektpfad oder `debug.launchFile` mit dem Namen der expliziten VS-Code-Debugkonfiguration. Beide warten auf den passenden `onDidStartDebugSession`-Event; verwende keine implizite Auswahl im Debug-UI.
- Build- und Debugstartfehler müssen einen Result-Code und den Pfad zum jeweiligen Logfile liefern. Ein MCP-Aufruf allein ist kein Startnachweis.
- Ein akzeptierter MCP-Aufruf ist kein Startnachweis. Prüfe danach die aktive Session über `debug.active.session`, warte auf den passenden `onDidStartDebugSession`-Event und prüfe anschließend Host-Readiness.
- Die Standalone-Erweiterung `ms-dotnettools.csharp` darf den `coreclr`-Debug-Adapter bereitstellen. C#-Projektauflösung und Startkonfiguration besitzt EsiMCP; sie dürfen nicht an Dev-Kit-Projektmodelle delegiert werden.
- Für clientseitige Breakpoints muss `Properties/launchSettings.json` die Microsoft-`inspectUri` enthalten und die Anwendung muss in Development `UseWebAssemblyDebugging()` aktivieren.
- Prüfe vor jedem Start, dass kein alter Studio-Prozess den Port `7010` belegt. Beende verwaiste projektbezogene Prozesse kontrolliert, bevor eine neue Debugsession gestartet wird.
- Für clientseitige Breakpoints muss `Properties/launchSettings.json` die Microsoft-`inspectUri` enthalten und die Anwendung muss in Development `UseWebAssemblyDebugging()` aktivieren.
- Prüfe vor jedem Start, dass kein alter Studio-Prozess den Port `7010` belegt. Beende verwaiste projektbezogene Prozesse kontrolliert, bevor eine neue Debugsession gestartet wird.
- Ein separater Watchdog, eine PID-Datei und eine Startblockade im Anwendungscode gehören nicht zum Blazor-Debugging und dürfen nicht eingeführt werden.

## Esi.AI Studio Hot Reload

- `debug.hotReload` mit `mode: "watch"` startet den ausgewählten `.csproj` unter `dotnet watch`; der SDK-eigene Hot-Reload-Mechanismus wendet unterstützte Edits live an und startet die App nur bei nicht unterstützten Änderungen neu. `mode: "stopWatch"` beendet den von EsiMCP gestarteten Task.
- Watch beendet aktive Debugsessions, startet aber keine Debugsession für den Watch-Prozess. Für einen angehängten Debugger ist ein expliziter `debug.launchProject`-/`debug.launchFile`-Start erforderlich.
- Für `mode: "rebuild"` muss genau ein `projectFile` oder `configurationName` angegeben werden. EsiMCP beendet zuerst Watcher und Debugsessions und startet nur nach erfolgreichem Build erneut.
- Nach erfolgreichem Neustart Host-Readiness und die betroffene Browserroute prüfen.

## Esi.AI Studio Build- und Debug-Lebenszyklus

- Vor jedem separaten Build/Test von `Esi.AI.Studio` oder `Esi.AI.Studio.Client` muss die Build-Schranke aus `.github/skills/vscode-debug/SKILL.md` vollständig ausgeführt werden, einschließlich unmittelbar vorheriger Sessionabfrage und bestätigter Portfreigabe.
- Für UI-only Änderungen zuerst `debug.hotReload` mit `mode: "watch"` verwenden, wenn eine Debugger-freie Watch-Session genügt. `mode: "rebuild"` nur für Änderungen, die einen vollständigen Debugger-Neustart erfordern, und anschließend Host-Readiness prüfen.

## Long-Running Commands

- Starte potenziell lang laufende Vorgänge von Anfang an asynchron im Hintergrund. Das gilt insbesondere für Modell-Ladevorgänge, Debug-Sessions, Server, Watcher sowie Builds und Tests, die voraussichtlich länger als einen kurzen Check laufen.
- Verwende synchrone Ausführung nur für kurze, begrenzte Prüfungen. Lasse einen synchron gestarteten Vorgang nicht erst nach einem Timeout in den Hintergrund verschieben.
- Lies den Output eines Hintergrundvorgangs erst nach Abschluss oder einer expliziten Benachrichtigung; starte keinen zweiten parallelen Vorgang auf derselben Ressource.

## Build- und Testumfang

- Kompiliere und teste ausschließlich Projekte unter `src/` im `Esi.*`-Namespace.
- Alles unter `origins/` dient nur als Vorlage bzw. Referenz und wird nicht als Teil des Esi.AI-Builds oder der Esi.AI-Tests behandelt.

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

## VS Code Provider Release Rules

- Every behavioral change to `src/vscode/vscode-esi-ai-studio` must increment the extension patch version in `package.json`; never ship a changed provider bundle under the previous version.
- Keep `package-lock.json`, the generated `dist/extension.js`, and the packaged VSIX synchronized with that new version before installing or validating the extension.
- Install the newly versioned VSIX through `scripts/install.sh` or the documented `npm run install:local` workflow. Do not treat a same-version reinstall as sufficient validation.
- After every provider change, perform that versioned build and installation as part of the task; do not leave installation or reinstallation as a manual user step.
- After installing a provider update, reload the VS Code window or restart the Extension Host before checking registered models or capabilities.
- Validate the complete capability path for changed model flags: Studio `/v1/models` JSON, provider mapping, installed bundle, and VS Code model registration. Preserve existing backend mappings, Hugging Face IDs, synchronization, and all previously supported capabilities.

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

