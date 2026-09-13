# Copilot Instructions

## Root-Cause-Regel

- Probleme werden an ihrer Ursache gelöst, nicht durch symptomatische Patches, Workarounds oder zusätzliche Fallbacks.
- Vor jeder Änderung muss der konkrete kontrollierende Codepfad identifiziert werden: Wer erzeugt den fehlerhaften Zustand, wer mutiert ihn und welche Abstraktion besitzt den Vertrag?
- Eine Änderung ist erst ausreichend, wenn sie den ursprünglichen Fehler reproduzierbar verhindert und der betroffene Ablauf getestet wurde. Eine lediglich sichtbare Änderung oder ein unterdrückter Fehler gilt nicht als Lösung.
- Bei widersprüchlichem Zustand müssen die Zustandsquelle und die Synchronisationsgrenze korrigiert werden; doppelte lokale Sonderlogik darf nicht als Ersatz für eine konsistente Quelle der Wahrheit eingeführt werden.
- Keine stillen Fallbacks, keine alternativen Startwege und kein "funktioniert irgendwie"-Verhalten, wenn der eigentliche Pfad fehlerhaft ist. Wenn eine Annahme nicht belegt ist, muss sie durch den nächstliegenden Test oder eine gezielte Diagnose falsifiziert werden.
- Nach der Ursachenänderung sind die ursprüngliche Fehlersituation, angrenzende Zustandsübergänge und die relevante Regression gezielt zu validieren.

## Esi.AI Studio Startregel

- Starte `Esi.AI.Studio` mit C# Dev Kit über `csdevkit.debug.projectDebugLaunch` beziehungsweise **Start New Instance** im Solution Explorer.
- C# Dev Kit verwendet dynamische, speicherinterne Debugkonfigurationen. Für den normalen Start dürfen keine `.vscode/launch.json` oder `.vscode/tasks.json` vorausgesetzt oder neu erzeugt werden.
- Verwende `csdevkit.debug.hotReload` für Hot Reload und `csdevkit.debug.showHotReloadPanel` zur Diagnose.
- Für den Klartext der Debug-Console verwende bei aktiver Session `csdevkit.debug.output.diagnostics` über EsiMCP. Die Antwort enthält `output` und `lastLine`; bei der Frage nach der letzten Zeile ist `lastLine` maßgeblich. `csdevkit.debug.showHotReloadPanel` öffnet nur das VS-Code-Panel und ersetzt diese strukturierte Diagnose nicht.
- Für clientseitige Breakpoints muss `Properties/launchSettings.json` die Microsoft-`inspectUri` enthalten und die Anwendung muss in Development `UseWebAssemblyDebugging()` aktivieren.
- Prüfe vor jedem Start, dass kein alter Studio-Prozess den Port `7010` belegt. Beende verwaiste projektbezogene Prozesse kontrolliert, bevor eine neue Debugsession gestartet wird.
- Ein separater Watchdog, eine PID-Datei und eine Startblockade im Anwendungscode gehören nicht zum Blazor-Debugging und dürfen nicht eingeführt werden.

## Esi.AI Studio Hot Reload

- Bei einer laufenden Studio-Debugsession ist für reine UI-Änderungen zuerst Hot Reload zu verwenden.
- Nach jeder Änderung an `.razor`, `.razor.css`, CSS oder Markup ist bei einer aktiven Studio-Debugsession die unmittelbar nächste Validierungsaktion `csdevkit.debug.hotReload`; ein separater Build, `get_errors`, Testlauf oder bloßes `git diff` darf davor nicht als Ersatz ausgeführt werden.
- Als Hot-Reload-fähige UI-Änderungen gelten insbesondere Änderungen an `.razor`, `.razor.css`, CSS, Markup und anderem Client-Code, sofern VS Code und die laufende Anwendung die Änderung übernehmen können.
- Für solche Änderungen darf die Debugsession nicht nur wegen einer anschließenden Browserprüfung oder eines unnötigen separaten Builds gestoppt werden. Die laufende Session bleibt aktiv, und die Änderung wird direkt im Browser validiert.
- Ein kontrollierter Debug-Restart oder ein Stop vor einem Build ist erst erforderlich, wenn Hot Reload die Änderung nicht anwenden kann, ein vollständiger Build ausdrücklich nötig ist oder die Änderung Server-/Projektdateien betrifft, die einen Neustart verlangen.
- Nach einem Hot-Reload-Lauf sind Host-Readiness, Browserzustand und gegebenenfalls die betroffene Route zu prüfen. Danach darf die Session für weitere Arbeit aktiv bleiben.

## Esi.AI Studio Build- und Debug-Lebenszyklus

- Vor jedem Build, Rebuild oder Test des Studio-Projekts muss die aktive VS-Code-Debugsession geprüft werden.
- Vor jedem separaten Compile-, Build-, Rebuild- oder Test-Befehl muss eine laufende Studio-Debugsession kontrolliert gestoppt werden. Kein solcher Befehl darf gestartet werden, solange die Debugsession noch läuft.
- Nach dem Stoppen muss geprüft werden, dass kein alter Studio-Prozess den Port `7010` belegt. Erst danach darf der separate Compile-, Build-, Rebuild- oder Test-Befehl gestartet werden.
- Für reine `.razor`-, `.razor.css`-, CSS- oder Markup-Änderungen gilt ausschließlich die Hot-Reload-Regel oben; dafür darf die Debugsession aktiv bleiben und es darf kein unnötiger separater Build gestartet werden.
- Wenn die Debugsession weiter benötigt wird, ist stattdessen ein kontrollierter Debug-Restart zu verwenden; dieser führt den notwendigen Rebuild aus. Danach muss die Host-Readiness erneut geprüft werden.
- Für Änderungen am Studio gilt daher: aktive Debugsession prüfen, zunächst Hot Reload versuchen, anschließend die laufende UI validieren und nur bei Bedarf kontrolliert stoppen oder per Debug-Restart neu bauen. Nach einem Neustart muss die Host-Readiness erneut geprüft werden.

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

