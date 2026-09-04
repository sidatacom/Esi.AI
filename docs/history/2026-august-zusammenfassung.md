# August 2026 – Session-Zusammenfassung

Zusammenfassung von 64 Sitzungsprotokollen aus `docs/history/` (28.–31.08.2026).
Die Protokolle wurden thematisch pro Tag gruppiert; die zugehörigen Quelldateien
stehen am Ende jedes Themas.

---

## 28.08.2026 – OpenVINO/Qwen3.8-Integration, Trennung LLama/OpenVINO, UI-Grundausstattung

### OpenVINO Qwen3.8 IR: Download, Runtime und Pipeline
- OpenVINO-Toolkit (2026.4.0.dev20260714) und GenAI (2026.4.0.0.dev20260814)
  für Ubuntu 26 heruntergeladen und per SHA-256 verifiziert.
- Qwen3.8-27B als multimodales IR-Modell identifiziert → `VLMPipeline` erforderlich.
- `OpenVinoModelLoader` respektiert `OPENVINO_RUNTIME_DIR` und wählt automatisch
  `VLMPipeline`, wenn das IR-Verzeichnis eine Vision-Komponente enthält.
- Integrationstest mit `GPU.1` erfolgreich (38.8 s); 12 Core-Tests bestanden.
- Studio-Build erfolgreich; Chat über die Backends-UI mit `Backend: OpenVINO`
  und `Model: Qwen3` persistiert und zurückgegeben.
- Dateinamen: `20260828-210357.md`, `20260828-211925.md`, `20260828-continue-openvino.md`,
  `20260828-download-qwen-ir.md`, `20260828-qwen-model-check.md`

### Trennung LLama/OpenVINO und SignalR-only-Architektur
- „Load model“ in Backends lieferte `failed`; Ursache behoben.
- Saubere Trennung zwischen LLama (nur `.gguf`) und OpenVINO (eigene
  Load-/Settings-Verträge, neue `OpenVinoSettings`-EF-Tabelle) in UI,
  Service-Routing und Persistenz.
- Browser-Aktionen laufen ausschließlich über `IDataService`/`SignalRDataService`
  und `DataHub`; keine `EditForm`-/Submit-Handler mehr.
- Core-Tests: 16 bestanden, 2 optionale Hardware-/Modelltests übersprungen.
- Dateinamen: `20260828-213632.md`, `20260828-223019.md`

### UI-Grundausstattung
- Scoped-CSS-Hintergrundbilder für die fehlenden Nav-Icons (Backends, Models,
  WebApi); Chat-Icon-SVG auf `fill: white` gesetzt.
- Chat-Metadaten zeigen zusätzlich Tokenzahl und Token/s aus der nativen
  OpenVINO-Generierung.
- Dateinamen: `20260828-212407.md`, `20260828-213151.md`

### GPU-Persistenz und Logging-Infrastruktur
- Vulkan- und OpenVINO-GPU-Auswahl (Device-Enablement/Priorität) werden über den
  bestehenden Save-Pfad mit dem aktiven Modellprofil persistiert.
- Einführung einer eigenen `Copilot-Processing/`-Logdatei pro Session; später am
  29.08. nach `docs/history/` verschoben.
- Dateinamen: `20260828-history.md`, `20260828-model-log.md`, `20260828-220000.md`

---

## 29.08.2026 – Runtime-Fassade, Backend-Isolation, Hugging Face, vLLM/SGLang

### Modell-Status und gemeinsame Runtime-Fassade
- Nach Navigation Backends → Chats → Backends wurde das geladene Modell nicht
  mehr angezeigt; `InitializeAsync` aktualisierte nur LLama-Status. OpenVINO-Status
  wird jetzt beim Seitenstart synchronisiert.
- Gemeinsamer „ACTIVE RUNTIMES“-Bereich über den Backend-Tabs; `ModelLoadStatus`,
  `LoadedModelStatus`, `VulkanDeviceStatus` in `Esi.AI.Models`.
- `ModelLoader` koordiniert typisierte LLama-/OpenVINO-Ladevorgänge; Enum-basiertes
  Unload über eindeutigen Hub-Namen.
- `ModelLoader` → `ModelRuntime` (IHostedService + IDisposable): initialisiert
  OpenVINO in `StartAsync`, entlädt beide Runtimes in `StopAsync`.
  Loader-Konstruktion in den Singleton verlagert.
- Dateinamen: `20260829-084945.md`, `20260829-085131.md`, `20260829-090022.md`,
  `20260829-091523.md`

### Prozess-Isolation und Watchdog
- Isolation von LLama/OpenVINO aus dem Studio-Prozess; Debug-Session-Watchdog für
  den kompletten Studio-Prozess (Skript, VS-Code-JSON-Diagnostik, Start/Stop).
- Dateinamen: `20260829-100621.md`

### Logging-Infrastruktur und Seitentitel
- `Copilot-Processing` → `docs/history` verschoben; Copilot-Processing-Regel
  aktualisiert (kein `Copilot-Processing`-Referenz mehr).
- `MainLayout.razor` ist die einzige Quelle für den Seitentitel (Header +
  Browser-Tab); doppelte `<PageTitle>`-Elemente aus den sechs Studio-Pages entfernt.
- Dateinamen: `20260829-090352.md`, `20260829-090838.md`, `20260829-092755.md`

### vLLM/SGLang als isolierte Python-Backends
- vLLM und SGLang als Python-Inferenz-Server-Backends implementiert: C# startet
  `Python/inference_server.py` als isolierten Kindprozess, wählt Engine, wartet auf
  `/v1/models`, stoppt den Prozessbaum, Chat über OpenAI-kompatible API.
- `/backends` zeigt Engine, Modell-ID/Pfad, Python-Executable, Port, Kontextlänge,
  Tensor-Parallelismus, GPU-Speicherauslastung und Start/Stop-Steuerung.
- Qwen3.8-Flash-Next (Qwen4ExpForConditionalGeneration, MoE 125B/6B, 51B n-gram
  Embedding, 262.144 Kontext) recherchiert/dokumentiert – für vLLM/SGLang/TokenSpeed,
  nicht für GGUF/OpenVINO-Pfade.
- Dateinamen: `20260829-102037.md`, `20260829-185115.md`, `20260829-185647.md`

### Hugging Face: REST-Vertrag, Download-Queue, Filter
- HF-REST-Vertrag gegen offizielle API validiert (HTTP-Client statt curl),
  Revision-Pinning und Path-Safety geprüft.
- Download-Queue: ein Aggregat pro Modell, parallele Aggregate und parallele
  Dateien/Shards; Aggregat- und Per-File-Fortschritt über SignalR in der UI.
- Vollständiger HF-Filterbereich (Libraries, Apps) auf der Models-Seite;
  transportneutrales Filter-Contract in `Esi.AI.Models`, durch IDataService/
  SignalRDataService/DataHub/ModelLibraryService geführt; Blazor-Runtime-Fehler
  nach Tab-Auswahl behoben.
- `ModelDownloadOption.SizeInBytes` mit binärer Größe im Label (z. B. „12 Dateien
  · 175.3 GiB“); Download-Queue wird beim SignalR-Connect gepusht und im HF-Tab
  oberhalb des Runtime-Status angezeigt.
- Microsoft-Prüfung: `InteractiveAuto` garantiert keine dauerhafte Serververbindung
  für Push; bestehender `DataHub` wird als Push-Kanal wiederverwendet.
- Dateinamen: `20260829-111119.md`, `20260829-113041.md`, `20260829-124657.md`,
  `20260829-125341.md`, `20260829-130935.md`, `20260829-154016.md`, `20260829-155929.md`

### Gemeinsame Backend-Modell-/Konfigurations-Integration
- `BackendModel` mit `ConfigurationBackend` und optionaler Profilzuordnung;
  `GetBackendModelsAsync` über SignalR; gemeinsames Modell-Katalog- und
  Konfigurations-Toolbar für jeden Backend-Tab.
- Python- und dotLLM-Profilpersistenz korrigiert (nicht mehr als LLama-Settings);
  vLLM/SGLang-fähige Backend-Filterung.
- Ein Referenzmodell pro Backend (5 verifizierte Einträge) für Backend-Tests.
- Dateinamen: `20260829-120926.md`, `20260829-122354.md`, `20260829-124500.md`

### Backend-Voraussetzungen (Requirements)
- `python3` bereitet `~/.venvs/esi-ai-vllm` bzw. `esi-ai-sglang` beim ersten
  Backend-Load vor; explizite Python-Executables werden nur validiert.
- Alle Backends durchlaufen eine gemeinsame Voraussetzungs-Grenze; native Backends
  melden gebündelte Runtime, Python-Backends isolierte Umgebungen.
- Gemeinsames Diagnose-Contract und SignalR-Pfad; Python-Backends explizit
  vorbereitbar, optionale Intel-XPU-Hardware informativ (blockiert nicht CUDA).
- Dateinamen: `20260829-201320.md`, `20260829-202422.md`, `20260829-202952.md`,
  `20260829-204511.md`

---

## 30.08.2026 – GPU-Matrix, CRUD-Konvention, Generalisierung, Streaming, VS Code Provider

### GPU-Kompatibilitätsmatrix und Backend-Performance
- Anforderungen-Button in jeder `Supported`-Matrix-Zelle (Vendor aus Spalte,
  Backend aus Zeile); AMD-Zelle ergänzt; Button nur bei unvollständigen
  Anforderungen sichtbar (alle Backends, nicht nur LLama).
- Matrix eingeklappt, wenn alle Checks ok sind.
- `/backends` war langsam: Requirement-Checks blockierten die Initialisierung;
  HTTP 500 durch fehlende `IBackendRequirementEvents` beim Server-Prerender behoben
  (serverseitige no-op-Registrierung).
- Dateinamen: `20260830-134641.md`, `20260830-135656.md`, `20260830-144333.md`,
  `20260830-145204.md`, `20260830-192529.md`

### Intel Arc Pro B70 / XPU (vLLM, SGLang)
- SGLang mit offiziellem XPU-PyTorch-Stack; XPU-Runtime-Sichtbarkeit mit
  Arc-only-Routing und deaktiviertem CUDA; SGLang-Lauf auf Arc Pro B70 wegen
  fehlendem XPU-Runtime-Support fehlgeschlagen (Blocker dokumentiert).
- vLLM 0.28.0 CUDA/XPU-Plugin-Konflikt auf Host mit RTX 4070 + Arc Pro B70
  untersucht; isoliertes CUDA/XPU-Routing, Import-Checks und XPU-Kompatibilitäts-
  Hooks ergänzt; UHD 630 ausgeschlossen.
- Dateinamen: `20260830-122501.md`, `20260830-130954.md`

### OpenVINO-Load-Diagnosen
- „Load model“ → `failed` und leeres UI-Log untersucht; OpenVINO-Load-/Log-Pfad
  nachverfolgt (Load-Handler, `DataService.LoadModelAsync`, `OpenVinoModelLoader`).
- „OpenVINO Core“-Logmeldung erschien dreifach → doppelte Emission an der
  Callback-Registrierung entfernt.
- Nach Debug-Restart blieb die geladenen-Modelle-Karte bei `NO MODEL LOADED`;
  read-only Analyse von Button-Handler, SignalR-Pfad und Tests.
- Dateinamen: `20260830-145916.md`, `20260830-152214.md`, `20260830-153344.md`

### Hugging Face-Queue und Download-Abbruch
- HF-Aktion wird deaktiviert, solange der HF-ID in der Queue ist (Queue-Predicate).
- „Abbrechen“-Button repariert: wiederhergestellte Downloads exponieren keine
  unvollendete Wait-Task mehr; Regressionstest für Abbruch nach Restore.
- Dateinamen: `20260830-153037.md`, `20260830-154735.md`

### Chat- und Runtime-UI
- Chat-Modellname vollständig statt abgeschnitten; Chats löschbar (SignalR-Architektur
  + Integrationstest).
- `LoadedModelStatus.IsLoading`: `ModelRuntime` verfolgt in-progress-Loads
  (LLama/OpenVINO/vLLM/SGLang/dotLLM) in der gemeinsamen `LoadedModels`-Sammlung;
  Backends zeigt Pendende mit Pfad/Runtime, unterscheidet aktive/ladende Zähler,
  separates Tab-Loading-Log entfernt.
- Runtime-Load-Ausgabe wird nach erfolgreichem Load ausgeblendet.
- Dateinamen: `20260830-163149.md`, `20260830-164057.md`, `20260830-161928.md`

### SignalR-Collection-CRUD-Konvention
- `LoadedModel` folgt `<Entity>_<Operation>`: `ModelRuntime` erstellt Pending-Item
  vor dem Load, publisht Update bei Status-Snapshot-Änderung, Update bei Erfolg,
  Delete bei fehlgeschlagenem/abgebrochenem Load; `LoadedModel_Read` als
  serverseitige Source-of-Truth beim vollständigen Reload; Backends subscribt statt
  zu poll; SignalR-Parallel-Invocations-Workaround entfernt.
- Standardisierung aller client-sichtbaren Collection-Verträge
  (`Chat`, `ModelDownload`, `ModelConfigurationProfile`, `LocalModel`, `LlamaModel`,
  `BackendModel`, `ModelDirectory`) über IDataService/DataService/DataHub/
  SignalRDataService/Seiten/Events/Tests; doppelte Chat-Overloads entfernt;
  `ModelDownload`-Notifications in Create/Update/Delete aufgeteilt; Regel in
  `.github/copilot-instructions.md` dokumentiert.
- Dateinamen: `20260830-164733.md`, `20260830-170134.md`

### Generalisierung von Modell-Settings/-Sammlungen
- LLama-spezifische Persistenz-Entity/DbSet auf backend-neutrale Namen umbenannt;
  `ModelConfigurationProfiles` → `ModelConfigurations` mit datenerhaltender EF-Migration.
- `LlamaSettings`/`OpenVinoSettings` → `ModelSettings`; `LlamaModel_*` → `Model_*`
  (in Arbeit/fortgeführt).
- Build- und Warning-Cleanup über alle `Esi.AI.*`-Projekte; Browser-Test Backend
  und Chat (ModelRuntime ist IHostedService, Hub wartete bis LoadAsync endet;
  parallele Hub-Aufrufe über MaximumParallelInvocationsPerClient=8).
- Dateinamen: `20260830-162254.md`, `20260830-172051.md`, `20260830-173148.md`,
  `20260830-173729.md`, `20260830-174743.md`, `20260830-175936.md`, `20260830-180654.md`

### Chat-Streaming über SignalR
- End-to-End-Response-Streaming über Chat-Contract, SignalR-Hub/Client und
  LLama/Python/dotLLM/OpenVINO-Adapter; Client rendert Deltas sofort und übernimmt
  den persistierten Chat bei Abschluss.
- Active-Runtime-Liste bleibt nach Load → Chat → Backends stabil; nach Reload
  via `LocalModel.Name` (Runtime-Key bleibt voller `ModelPath`).
- Qwen `<think>…</think>` in standard-eingeklapptem erweiterbarem Block; Parser-
  Edge-Case für schließendes Tag ohne öffnendes Tag in `Chats.razor` behoben.
- Langer OpenVINO-Prefill (> 30 s) vor erstem Callback → Watchdog-Toleranz erhöht;
  read-only Analyse: Code kann ein Delta vor native-Abschluss forwarden, keine
  Quelländerung gerechtfertigt.
- Dateinamen: `20260830-191532.md`, `20260830-193501.md`, `20260830-194946.md`,
  `20260830-201616.md`, `20260830-211223.md`, `20260830-220954.md`, `20260830-223926.md`

### VS Code Model-Provider und WebAPI
- Eigenständiger VS-Code-Model-Provider `src/vscode/vscode-esi-ai-studio`
  registriert Studio-Modelle für die VS-Code-Language-Model-API (SSE + JSON);
  Build, TS-Diagnosen und Paketumfang geprüft; Live-Chat gegen Port 7010 offen.
- WebAPI für VS-Code-Zugang: API-DTOs in `Esi.AI.Models`, OpenAI-kompatible
  Response-Verträge, Streaming für jeden geladenen Backend über Controller;
  fokussierte Tests für Models/Validation/JSON/SSE.
- `.gitignore` schließt Node.js-Dependency-Verzeichnisse auf beliebiger Tiefe aus.
- Dateinamen: `20260830-211613.md`, `20260830-212305.md`, `20260831-073410.md`

### OpenVINO-CLR-Fehler
- Studio-Prozess endete mit `Fatal error. Internal CLR error. (0x80131506)` nach
  HF-401/416 während eines Hintergrund-Downloads (offen/fortgeführt).
- Dateinamen: `20260830-112554.md`

---

## 31.08.2026 – VS Code Provider-Installation

### Installation des VS Code Model-Providers
- Installationsroutine und Installations-Prompt für `vscode-esi-ai-studio` nach
  dem bestehenden EsiMCP-Muster; wiederholbarer lokaler VSIX-Installations-Workflow
  und Prompt, der ein Agent durch Installation und Verifikation führt.
- Dateinamen: `20260831-080623.md`

---

## Gesamtergebnis August

- **OpenVINO/Qwen3.8** vollständig integriert: Ubuntu-26-Runtime, automatische
  `VLMPipeline`-Auswahl, GPU-1-Integrationstest, Chat mit `Backend: OpenVINO`.
- **Backend-Landschaft ausgeweitet**: LLama und OpenVINO sauber getrennt; vLLM und
  SGLang als isolierte Python-Backends über OpenAI-kompatible API; Intel Arc Pro B70
  XPU-Routen untersucht (SGLang durch fehlenden XPU-Runtime-Support blockiert).
- **Architektur konsolidiert**: `ModelLoader` → `ModelRuntime` (IHostedService/
  IDisposable); gemeinsame `ACTIVE RUNTIMES`-Fassade; `ModelSettings`/`Model`
  generalisiert; vollständige `<Entity>_<Operation>`-CRUD-Konvention über
  IDataService/DataService/DataHub/SignalR/Tests eingeführt und dokumentiert.
- **Hugging Face** ausgebaut: REST-Vertrag validiert, parallele Download-Queue mit
  Aggregat-/Shard-Fortschritt, vollständiger Filterbereich, Queue-Sync nach Reload.
- **Chat-Streaming** end-to-end über SignalR für alle Backends; `<think>`-Block
  als eingeklappter Bereich; Chat-Delete und vollständige Modellnamen in der UI.
- **VS Code-Integration**: eigener Model-Provider (`vscode-esi-ai-studio`) und
  OpenAI-kompatible WebAPI für VS-Code-Zugang; Installations-Workflow angelegt.
- **Robustheit**: Debug-Session-Watchdog, GPU-Kompatibilitätsmatrix mit
  Requirements-Dialog, Performance- und Prerender-Fixes, CLR-Fehler bei HF-401/416
  identifiziert.
- **Offen/fortgeführt**: SGLang auf Arc Pro B70 (XPU-Runtime), OpenVINO-Streaming
  bei langem Prefill, CLR-Fehler-Handling bei HF-Fehlstatus, Generalisierung der
  `ModelSettings`/`Model`-Verträge.