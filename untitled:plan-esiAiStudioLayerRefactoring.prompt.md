## Plan: Esi.AI.Studio in fünf Schichten trennen

Esi.AI.Studio besitzt bereits erkennbare Verantwortungsbereiche, aber keine klare, erzwingbare Trennung nach den fünf Zielschichten. Die stärksten Verstöße sind die Rückwärtsabhängigkeit `Esi.AI.Studio -> Esi.AI.Studio.Client`, die Bündelung mehrerer Schichten in `DataService` und `ModelLibraryService`, das Backend-String-Routing im API-Controller und das Fehlen einer expliziten Serving-/Scheduler-Abstraktion. Der Refactor soll inkrementell erfolgen, die bestehende eine OpenAI-kompatible API, den zentralen `DataHub` und die bestehenden SignalR-CRUD-Namen erhalten bzw. kontrolliert normalisieren.

**Befund**
- Layer 1, Modellformat und Quantisierung: teilweise vorhanden. `ReferenceModelFormat` und `ModelBackendCompatibility` liegen in `Esi.AI.Models`; die eigentliche Datei-/Verzeichnis-Erkennung, Hugging-Face-Synchronisierung, Download-Warteschlange und Metadatenpersistenz liegen gemeinsam in `ModelLibraryService`. Quantisierung wird überwiegend von den nativen Backends interpretiert, ist aber kein explizites, transportneutrales Modellmerkmal.
- Layer 2, Runtime und Compute: funktional vorhanden in `Esi.AI.Core` mit `LlamaModelLoader`, `OpenVinoModelLoader`, `PythonInferenceServer` und `DotLlmInProcessRuntime`. `ModelRuntime` bündelt Registrierung, Laden, Entladen, Statusaggregation, Pending-Status, Provisionierung und Session-Fabrikation.
- Layer 3, Serving und Scheduler: nur teilweise getrennt. `OpenAiCompatibleController` routet per Backend-String und erzeugt Sessions direkt; `DataService` enthält einen zweiten Generation-/Streaming-Pfad. Es gibt Locks für einzelne Loader und eine Download-Queue, aber keinen einheitlichen Inference-Scheduler oder Admission-/Concurrency-Vertrag.
- Layer 4, Daemon und Lifecycle: vorhanden, aber vermischt. `BackendRequirementMonitor` diagnostiziert und publiziert zugleich; `BackendPrerequisiteProvisioner` wird aus `ModelRuntime.LoadAsync` heraus aufgerufen; `ModelLibraryService` besitzt einen Hintergrund-Worker; Watchdog und Prozessisolation sind separat und für Linux bewusst implementiert.
- Layer 5, Workbench: die Blazor-Seiten nutzen grundsätzlich `IDataService` und SignalR. Der Server referenziert jedoch das Client-Projekt, weil `IDataService` und `OpenVinoDiagnosticsDto` dort liegen. `DataHub` injiziert zusätzlich konkrete Serverdienste und enthält lokale Autorisierungs-/Dispatchlogik. Mehrere Seiten verwalten dieselben Runtime-/Download-Zustände über eigene Eventhandler und lokale Collections.
- Positiv und beizubehalten: genau ein `OpenAiCompatibleController`, zentraler Hub `/hubs/data`, DTOs in `Esi.AI.Models`, serverseitiges `DataService` implementiert bereits `IDataService`, bestehende SignalR-CRUD-Ereignisse und die Watchdog-Startregeln.

**Schritte**

### 1. Baseline und Zielverträge
1. Aktive Studio-Debugsession und Watchdog-Zustand prüfen; vor jedem Build/Test die projektweiten Startregeln beachten.
2. Bestehende Tests und Builds als Baseline ausführen und die Ergebnisse dokumentieren.
3. Eine kurze Architekturentscheidung dokumentieren: Abhängigkeiten zeigen ausschließlich nach unten; Layer 1 enthält Format-/Kompatibilitätsregeln, Layer 2 konkrete Engines, Layer 3 Serving/Scheduling, Layer 4 Host-/Provisionierungs-/Lifecycle-Orchestrierung, Layer 5 UI/Transportadapter.
4. Neue transportneutrale Vertragsgrenze planen: ein kleines `Esi.AI.Studio.Contracts`-Projekt für `IDataService` und nicht-DTO-Contracts; alle DTOs wie `OpenVinoDiagnosticsDto` bleiben bzw. wandern nach `Esi.AI.Models`.

### 2. Abhängigkeit Client/Server auflösen
1. `IDataService` aus `Esi.AI.Studio.Client` in `Esi.AI.Studio.Contracts` verschieben, ohne die bestehenden Methoden und die zentrale SignalR-Kommunikation zunächst umzubenennen.
2. `OpenVinoDiagnosticsDto`, `OpenVinoDeviceDto`, `OpenVinoDiagnosticCheckDto` und `OpenVinoSolveResultDto` nach `Esi.AI.Models` verschieben.
3. Server und Client auf `Esi.AI.Studio.Contracts` bzw. `Esi.AI.Models` umstellen; die Projektabhängigkeit `Esi.AI.Studio -> Esi.AI.Studio.Client` entfernen.
4. `SignalRDataService` bleibt der Client-Transportadapter; `DataService` bleibt die serverseitige Implementierung. Die Eventinterfaces `IModelDownloadEvents`, `IModelRuntimeEvents` und `IBackendRequirementEvents` bleiben clientseitig oder werden erst dann in Contracts verschoben, wenn ein zweiter Client sie benötigt.
5. Nach dem Umbau kompilieren und die Interface-/DI-Registrierung sowie die generierten SignalR-Aufrufe prüfen.

### 3. Layer 1 isolieren: Format, Kompatibilität und Katalog
1. Aus `ModelLibraryService.ScanLocalModelsAsync` einen reinen `ILocalModelScanner`/`LocalModelScanner` extrahieren. Dieser darf nur Dateisystem/YAML lesen und `LocalModelInfo` inklusive `ReferenceModelFormat` liefern.
2. Format-Erkennung und Backend-Kompatibilität trennen: einen reinen `IModelFormatDetector` für Pfad-/Dateisignaturen und einen reinen Resolver für `ReferenceModelFormat -> ConfigurationBackend[]`; `ModelBackendCompatibility` darf keine Dateisystem- oder Transportabhängigkeit bekommen.
3. Quantisierungsinformationen als optionales, transportneutrales Modellmerkmal definieren, soweit sie zuverlässig aus GGUF-/Repository-Metadaten ableitbar sind. Keine eigene Quantisierungsarithmetik im Studio nachbauen; die Berechnung bleibt in LLamaSharp/OpenVINO/dotLLM.
4. `HuggingFace`-HTTP und Metadatenmapping in einen eigenen `IHuggingFaceCatalog`-Adapter verschieben.
5. Download-Queue, Persistenz und Fortschritt in einen `IModelDownloadManager` bzw. eine getrennte Download-Orchestrierung verschieben. SignalR-Publishing über einen Publisher-Contract statt über `IHubContext` im Katalog-/Downloadkern anbinden.
6. `DataService` nur als Application-Fassade für LocalModel-, Download- und Konfigurationsfälle belassen; EF-Mapping und Katalog-/Downloadlogik aus der monolithischen Klasse entfernen.

### 4. Layer 2 isolieren: Runtime-Adapter und typisiertes Backend-Routing
1. Einen gemeinsamen `IBackendRuntime`-/`IModelRuntimeAdapter`-Vertrag in `Esi.AI.Core` definieren: Backendkennung, unterstützte Formate, Laden, Entladen, Status, Session-/Generation-Zugriff und Shutdown.
2. Llama, OpenVINO, vLLM/SGLang und dotLLM jeweils als Adapter registrieren. Backend-spezifische Optionen bleiben typisiert und werden im jeweiligen Adapter in native Optionen übersetzt.
3. `ModelRuntime` auf Registry-/Coordinator-Aufgaben reduzieren: Adapter auswählen, Status aus einer gemeinsamen Sicht liefern und Lifecycle-Aufrufe delegieren. Die vier öffentlichen `LoadAsync`-Varianten dürfen während der Migration als Kompatibilitätsfassade bestehen bleiben, sollen intern aber über die Registry laufen.
4. Provisionierung aus dem Runtime-Kern herauslösen: `BackendPrerequisiteProvisioner` wird als explizites Preflight im Lifecycle-/Application-Service ausgeführt; ein Runtime-Adapter lädt erst nach erfolgreicher Vorbereitung.
5. Die Backend-String-Switches entfernen oder auf die Transportgrenze beschränken. Intern wird `ConfigurationBackend` bzw. ein typisierter Backend-Descriptor verwendet; Aliasnamen wie `Vulkan`, `CPU`, `OpenVINO`, `vLLM` und `SGLang` werden an einer einzigen Mappingstelle normalisiert.
6. Tests für Adapterauswahl, Formatkompatibilität, Fehlerweitergabe, Shutdown und Statusaggregation ergänzen.

### 5. Layer 3 isolieren: Serving, Chat und Scheduler
1. Einen `IChatCompletionService`/`IInferenceService` als gemeinsame Application-Fassade für OpenAI-Chat-Completions und Studio-Chats einführen. Er übernimmt Modellselektion, typed Backend-Routing, Optionsmapping, Tool-/Multimodalvalidierung und Generation.
2. Einen expliziten `IInferenceScheduler` ergänzen. Zunächst reicht ein klarer Admission-/Concurrency-Vertrag mit per-Backend bzw. per-Modell Gates, Cancellation und Load/Unload-Koordination; Continuous Batching wird nicht vorgetäuscht, solange kein Backend es liefert.
3. Die vier direkten Session-Fabrikationpfade aus `OpenAiCompatibleController` und `DataService.GenerateChatWithStatsAsync` in die Serving-/Runtime-Abstraktion verschieben. Dadurch existiert nur noch ein Generationpfad.
4. `Chat_UpdateStreamAsync` in einen testbaren Chat-Orchestrator mit getrennten Schritten für Generation, Delta-Weitergabe, Cancellation und Persistenz zerlegen. Das aktuelle `Task.Factory.StartNew`-/Channel-Verhalten muss dabei auf Fehlerabschluss, Abbruch und Ressourcenfreigabe geprüft werden.
5. `OpenAiCompatibleController` auf HTTP-Vertragsarbeit reduzieren: Requestvalidierung/Mapping, SSE-Response-Serialisierung und HTTP-Fehlercodes. Es bleiben genau die vorhandenen `/v1/models`- und `/v1/chat/completions`-Routen; kein neuer Controller oder Endpoint.
6. OpenAI-Controllertests auf einen Fake-Serving-Service umstellen und separat Backend-Routing, SSE-Abschluss/Heartbeat, Tool-Calls, Cancellation und "kein Modell geladen" testen.

### 6. Layer 4 isolieren: Lifecycle, Provisionierung und Publikation
1. Einen `ModelLifecycleCoordinator` bzw. `LoadedModelStateStore` einführen, der `Idle`, `Loading`, `Loaded`, `Unloading`, `Failed` und `Cancelled` explizit modelliert, konkurrierende Load-/Unload-Aufträge koordiniert und die autoritative `LoadedModel`-Collection führt.
2. Pending-, Update- und Delete-Publikation aus `ModelRuntime` in diese Lifecycle-Komponente verschieben. Die bestehende Regel bleibt erhalten: Pending-Item beim Start publizieren, Updates bei Zustandsänderung, Delete bei Entfernen/Fehler/Abbruch.
3. `BackendRequirementMonitor` in einen reinen `BackendDiagnosticsCollector` und einen SignalR-/Event-Publisher trennen. Der BackgroundService bleibt der Taktgeber; Diagnostik darf unabhängig und unit-testbar sein.
4. `DataHub` auf einen dünnen Transportadapter reduzieren. Alle Model-, Download-, Chat-, Diagnose- und Lifecycleaktionen delegieren an den Application-Service/IDataService; konkrete `ModelLibraryService`, Installer und Monitor werden nicht mehr direkt injiziert. Die Loopback-Sperre für Treiberinstallation bleibt erhalten, wird aber in eine testbare Policy bzw. einen dedizierten Application-Service verschoben.
5. Initialzustand bei `OnConnectedAsync` und die SignalR-CRUD-Verträge explizit testen. Fehlende Entity-Operationen werden dabei nur für tatsächlich client-sichtbare Collections ergänzt; bestehende Namen werden nicht ohne Übergang geändert.
6. Watchdog/`StudioProcessIsolation` in diesem Refactor nicht portieren oder funktional umdeuten. Eine plattformneutrale Prozessabstraktion ist ein separater späterer Scope, solange Esi.AI.Studio Linux/Watchdog voraussetzt.

### 7. Layer 5 stabilisieren: Workbench-State und UI-Grenze
1. `Models.razor`, `Backends.razor` und `Provider.razor` auf einen gemeinsamen clientseitigen Runtime-/Download-State-Container bzw. scoped Store umstellen, der SignalR-Events reconciled und bei Reconnect einmalig `*_Read` ausführt.
2. Lokale Dictionaries und doppelte Statusmodelle der Seiten reduzieren; Seiten senden Commands über `IDataService` und rendern aus dem Store. Während eines laufenden Load-/Download-Vorgangs wird nicht gepollt.
3. Eventabonnements zentral verwalten und beim Disposal zuverlässig entfernen. Reconnect, verpasste Events und vollständige Seiteninitialisierung müssen denselben Read-/Reconcile-Pfad verwenden.
4. Die UI-spezifische `SignalRDataService`-Implementierung bleibt ausschließlich Client-Transport; keine Servertypen, `HubConnection` oder Controller in Razor-Seiten.
5. Blazor-Komponententests bzw. service-level Tests für Reconnect-Reconciliation, Load-Statusübergänge, Download-Updates und Disposal ergänzen.

### 8. Abschluss, Bereinigung und Dokumentation
1. Alte direkte Methoden, String-Routing, doppelte Generation und überflüssige Projekt-Usings erst löschen, wenn alle Aufrufer auf die neuen Abstraktionen migriert sind.
2. Abhängigkeitsrichtung anhand der `.csproj`-Referenzen und eines Architekturtests prüfen: `Client -> Contracts/Models`, `Studio -> Contracts/Core/Models`, `Core -> Models`; keine Referenz von Server/Core auf Client.
3. XML-Dokumentation der neuen öffentlichen Contracts und Adapter gemäß C#-Regeln ergänzen.
4. Eine Architektur-Dokumentation unter `docs/` aktualisieren: Verantwortlichkeit je Layer, erlaubte Abhängigkeiten, SignalR-CRUD-/Read-Reconcile-Regeln und Backend-Erweiterungspunkt.

**Parallele Arbeit**
- Nach Schritt 2 können Format-/Katalogzerlegung und Runtime-Adapterdefinition parallel vorbereitet werden.
- Serving/Scheduler hängt von den Runtime-Verträgen ab.
- Lifecycle-Publisher und UI-State hängen von den finalen Status-/Eventverträgen ab.
- Tests für reine Formatresolver, Backend-Mapping und Diagnosecollector können jeweils parallel zu ihrer Implementierung entstehen.

**Relevante Dateien**
- `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj` — Client-Rückwärtsreferenz entfernen, Contracts-Referenz ergänzen.
- `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Studio.Client/Esi.AI.Studio.Client.csproj` — Contracts-Referenz ergänzen.
- `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Studio.Client/Services/IDataService.cs` — nach Contracts verschieben; Client-Eventverträge separat halten.
- `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Studio.Client/Services/SignalRDataService.cs` — einziger Client-Transportadapter; Hub-Namen und Reconnect-Verhalten erhalten.
- `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Models/LibraryContracts.cs` — reine Format-/Kompatibilitätsregeln und neue transportneutrale Metadaten.
- `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Studio/Services/ModelLibraryService.cs` — in Scanner, Hugging-Face-Katalog, Downloadmanager und Publisher-Orchestrierung zerlegen.
- `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Core/ModelLoading/ModelRuntime.cs` — auf Adapterregistry und Lifecyclekoordination reduzieren.
- `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Core/ModelLoading/LlamaModelLoader.cs`, `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Core/ModelLoading/OpenVinoModelLoader.cs`, `/home/llm/Git/Esi.AI/Esi.AI.Core/ModelLoading/PythonInferenceServer.cs`, `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Core/ModelLoading/DotLlmInProcessRuntime.cs` — konkrete Layer-2-Adapter.
- `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Core/ModelLoading/BackendPrerequisiteProvisioner.cs` — expliziter Preflight, nicht implizite Runtime-Loading-Nebenwirkung.
- `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Studio/Services/DataService.cs` — Application-Fassade; Katalog, Runtime, Serving, Persistenz und Diagnostik aus der monolithischen Klasse lösen.
- `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Studio/Controllers/OpenAiCompatibleController.cs` — auf API-Vertrag und SSE reduzieren; Backend-Generation delegieren.
- `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Studio/Hubs/DataHub.cs` — dünner SignalR-Adapter ohne konkrete Businessdienste oder lokale Dispatchlogik.
- `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Studio/Services/BackendRequirementMonitor.cs` — Collector und Publisher trennen.
- `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Studio.Client/Pages/Models.razor`, `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Studio.Client/Pages/Backends.razor`, `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Studio.Client/Pages/Provider.razor` — gemeinsamen State-/Event-Reconcilepfad verwenden.
- `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Studio.Tests/OpenAiCompatibleControllerTests.cs` und `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Studio.Tests/BackendCatalogIntegrationTests.cs` — vorhandene Testbasis erweitern.
- `/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Core.Tests/` — Runtime-, Resolver-, Scheduler- und Lifecycletests ergänzen.
- `/home/llm/Git/Esi.AI/docs/` — Zielarchitektur und Erweiterungspunkte dokumentieren.

**Verifikation**
1. Nach jeder Phase zuerst den kleinsten passenden MSTest-/Projekt-Test ausführen; Testnamen nach `MethodName_Condition_ExpectedResult()` wählen.
2. Nach der Vertragsphase `dotnet build` für Contracts, Client und Studio ausführen und verifizieren, dass Studio keine Client-Projektreferenz mehr besitzt.
3. Nach Layer 1 Tests für GGUF-, OpenVINO-IR- und Transformers-Erkennung, Backend-Kompatibilität, Quantisierungsmetadaten und Katalog-/Download-Reconciliation ausführen.
4. Nach Layer 2/3 Tests für typisierte Backendauswahl, parallele Requests, Load/Unload-Koordination, Cancellation, einheitliche Chat-Generation und OpenAI-SSE ausführen.
5. Nach Layer 4 Tests für alle Lifecyclezustände, Pending/Create/Update/Delete-Publikation, Reconnect-Initialisierung, Diagnosefehler und Loopback-Autorisierung ausführen.
6. Nach Layer 5 Tests für SignalR-Reconnect, Eventverlust plus Read-Reconcile, Store-Disposal und UI-Commandpfade ausführen.
7. Am Ende die gesamte relevante Solution/Test-Suite bauen und testen. Falls eine laufende Studio-Debugsession existiert, sie gemäß den Repository-Regeln kontrolliert stoppen oder einen kontrollierten Debug-Restart verwenden; keine parallelen Builds mit Studio.
8. Für einen manuellen Lauf ausschließlich `build-and-watch-esi-ai-studio` bzw. die überwachte VS-Code-Debugkonfiguration verwenden und danach `stop-esi-ai-studio-watchdog` ausführen.

**Entscheidungen**
- Kein neuer HTTP-Controller, kein Minimal-API-Endpunkt und keine direkten Browser-HTTP-Aufrufe.
- `Esi.AI.Models` bleibt die Heimat aller DTOs und SignalR-/API-Datentypen; Contracts enthalten Interfaces, nicht transportabhängige Geschäftsobjekte.
- Die native Quantisierungsarithmetik bleibt in den Backendengines; Studio modelliert nur erkannte/verfügbare Metadaten.
- Stringnamen werden nur an Transport-/Kompatibilitätsgrenzen akzeptiert und intern sofort zu `ConfigurationBackend` bzw. Backenddeskriptoren normalisiert.
- Bestehende SignalR-CRUD-Ereignisse und Reconnect-/Read-Semantik werden erhalten; Änderungen an Hub-Namen erfolgen nur mit Übergang und Tests.
- Watchdog- und Linux-Prozessisolation bleiben unverändert und sind kein Teil des ersten Refactoring-Slices.
- Keine Persistenz der flüchtigen Runtime-Handles in SQLite; persistiert werden weiterhin Modellmetadaten, Konfigurationen und Chats, während aktive Runtime-Zustände über den Lifecycle-State-Store und SignalR geliefert werden.

**Erfolgskriterium**
Ein neuer Backendadapter soll nur Models-Verträge, einen Core-Adapter, eine Registry-/DI-Registrierung und die erforderlichen typed Options benötigen. UI, `DataHub`, OpenAI-Controller, Katalogscan und Lifecycle-Publisher dürfen dafür nicht geändert werden müssen; diese Änderung zeigt, dass die fünf Verantwortungsbereiche tatsächlich getrennt sind.
