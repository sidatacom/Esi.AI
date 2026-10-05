# September 2026 – Sitzungszusammenfassung

Zusammenfassung von 309 Sitzungsprotokollen aus `docs/history/` (01.–30.09.2026, 20 Sitzungstage). Die Tagesarchive enthalten die vollständigen, unveränderten Quelldateien; diese liegen gesammelt unter `docs/history/2026/09/_source/`.

Quelldateien werden pro Thema platzsparend als `YYYYMMDD-{HHMMSS,...}.md` angegeben.

---

## 01.09.2026 – VS-Code-Modellprovider und Chat-Wiederherstellung

### Provider-Aktivierung, Modell-Picker und betreuter Host
- Die Esi.AI-Studio-Modelle fehlten oder waren im VS-Code-Picker nicht auswählbar. Provider-Metadaten, Registrierung, Modell-Refresh und Fehlermeldungen wurden wiederholt geprüft und angepasst.
- Versionierte Provider-Builds und Installationen ergänzten unter anderem Capability-Metadaten und automatische Wiederholungen fehlgeschlagener Modellabfragen.
- Der OpenVINO/Qwen-Pfad über die Studio-Streaming-API wurde erfolgreich demonstriert; andere Prüfungen blieben wegen nicht erreichbarem Host, ausstehendem Reload oder unvollständiger Validierung offen.
- Ein unbeaufsichtigter Studio-Start und wiederholte Rechner-Freezes führten zu Diagnose- und Lifecycle-Arbeit. Der überwachte Start wurde mit Watchdog-PID-Prüfung abgesichert.

Quelldateien: `20260901-{165727,171824,174901,180812,181350,182513,182555,182718,183114,191522,201014,202149,202317,205324,212345}.md`

## 02.09.2026 – Provider-Faehigkeiten, OpenVINO-Chat und Backend-API

### Capability-Vertrag, Chat und Runtime-Verhalten
- Tool Calling, Vision, Agent Mode und Thinking wurden entlang von Studio-Modellmetadaten, `/v1/models` und VS-Code-Provider untersucht; Capability-Angaben sollten nur tatsaechlich unterstuetzte Pfade versprechen.
- OpenVINO-VLM-Textchat und SSE-Streaming wurden wiederhergestellt und mit Qwen live geprueft. Ein fehlendes geladenes Modell erklaerte einen 503; der anschliessende Chat lieferte `[DONE]`.
- Der OpenAI-kompatible API-/Provider-Pfad wurde backendneutral erweitert, einschliesslich Token- und Durchsatzangaben. Lokale Credits wurden mangels Billing-/Accounting-Domaene ausdruecklich nicht behauptet.
- SGLang-XPU-Start-/OOM-Fehler, lesbare Backend-Fehlerausgabe, Hugging-Face-Hardware-/VRAM-Filter, Filter-Reset und Models-Seitenaktionen wurden bearbeitet; mehrere Builds, fokussierte Tests und Browserpruefungen bestanden.

Quelldateien: `20260902-{060951,114207,120049,120408,120835,121412,122636,123107,124154,125113,142652,143216,143959,144906,150037,155115,164227,170937,175858,183808,191156,191911,194440,200211,200740,201741,202243,203012}.md`

## 03.09.2026 – Hugging-Face-Filter und strukturierte Copilot-Integration

### Modellkatalog, Provider-Tool-Calls und Statusereignisse
- Hugging-Face-Filterverhalten, Tags, Backendkompatibilitaet und Modellgroesse wurden geklaert; ein Hardwareprofil mit konfigurierbarem Kontextfenster filterte Suchergebnisse nach geschaetztem VRAM-Bedarf.
- Die VS-Code-Provider-/Copilot-Integration erhielt konfigurierbare Tokenlimits und strukturierte Tool-Call-Verarbeitung. OpenVINO/Qwen-Toolaufrufe wurden end-to-end validiert; mehrere Sessions dokumentieren weiterhin haengende Tool-Optimierung oder SSE-Abbrueche als offene bzw. erneut untersuchte Probleme.
- Backend-Modellstatus wurde auf SignalR-Create/Update/Delete-Ereignisse ausgerichtet, damit die Backends-Seite Ladefortschritt ohne Seitenreload zeigt.
- Die August-Historie wurde als thematische Zusammenfassung mit 80 tatsaechlich gefundenen Quelldateien archiviert; einige andere Tagesprotokolle blieben als laufende Untersuchungen markiert.

Quelldateien: `20260903-{084044,102258,103332,103705,110230,110517,111546,125049,125955,133135,133519,133753,133923,134848,141451,142920,145134,151920,170105,172546,173503,175219,175942,183251,185225,190353,190735,193127,193535,195815}.md`

## 04.09.2026 – Multimodale WebAPI und LLama-Runtimes

### Bildverarbeitung, Backend-Pakete und Architekturpruefung
- OpenAI-kompatible Bild/Text-Anfragen wurden fuer OpenVINO und LLamaSharp bearbeitet. Lokale Bilddaten wurden dekodiert und als Tensoren getestet; ueberwachte OpenVINO-Bildinferenz bestand. Remote-URLs wurden nicht abgerufen.
- Die Grenzen blieben explizit: vLLM/SGLang-GRPC-Bruecken und dotLLM waren text-only; echte Vision-Unterstuetzung dort erforderte weitere Runtime-/Protokollarbeit. Einzelne OpenVINO-`-17`-Fehler und Modellpaketprobleme wurden nicht als generell geloest dargestellt.
- CUDA-12-/SYCL-16-Runtime-Installation, lokaler Paketpfad und ein LLamaSharp-SYCL-Buildskript wurden vorbereitet. Der lokale Fork baute, waehrend Release/CI und echte Arc-Hardwarevalidierung teilweise noch ausstanden.
- API-, Repository- und Refactoring-Dokumentation wurde aktualisiert. Die Fuenf-Layer-Architekturpruefung bewertete mehrere Extraktions- und UI-State-Schritte weiterhin als offen.

Quelldateien: `20260904-{110453,113127,120513,121044,123613,130139,131406,133649,135205,140330,143042,144310,145854,151910,155151,170335,173131,174127,174630,175957,181223,182955,184053,192722,193159,194232,195429,200820,201311,204008,205027,205443,205826,210505,211028,211354,214206,221946,222634}.md`

## 05.09.2026 – Backend-UI, Runtime-CRUD und Tool-Aufrufe

### UI- und API-Konsolidierung
- Dashboard-Charts, Backend-Layout und Requirements-Anzeige wurden angepasst; Scrollen und kleinere UI-Aenderungen wurden mit Hot Reload geprueft und entsprechende Entwicklungsregeln dokumentiert.
- Der geladene-Modell-Ablauf publiziert Pending-/Ergebniszustand. Eine SignalR-Publikationsstoerung wurde von der nativen OpenVINO-Ladeoperation entkoppelt; ein echtes Modell wurde anschliessend erfolgreich geladen.
- Die OpenAI-kompatible Middleware vereinheitlichte Message-, Bild- und strukturierte Tool-Call-Verarbeitung fuer die vorhandenen Adapter, ohne willkuerliche Tool-Anzahl oder Provider-only-Textkuerzung.
- OpenVINO `-17` und SSE-Fehler blieben wiederkehrende Untersuchungsgegenstaende; Validierungen unterschieden erfolgreich getestete Pfade von nicht gestarteten Live-Tests.

Quelldateien: `20260905-{095644,110900,131247,131855,135855,140302,141811,142155,142424,145450,151407,160531,182019,185735,191744,192040,193229,200913,204849,214032,220940,221730,222909,224110}.md`

## 06.09.2026 – Copilot-Agent, Modell-Lifecycle und Debug-Werkzeuge

### API-Konfigurationen, Tool-Calls und Laufzeitsteuerung
- Der OpenAI-Pfad wechselte zu gespeicherten Modellkonfigurationen mit `AutoLaunch`; Profilnamen und Einstellungen wurden editierbar gemacht und die EF-Migration mit fokussierten Tests gebaut.
- Pending-Modellzustand, Entladen und Requests waehrend eines Loads wurden weiterentwickelt. Ein echter Multi-Tool-Agentlauf ueber OpenVINO bestand; spaetere Logs grenzten verbleibende Fehler auf native OpenVINO-Generierung nach erfolgreichem Laden ein.
- Hot-Reload-/Watch-, Readiness- und EsiMCP-Debug-Lifecycle wurden mehrfach repariert und live geprueft. EsiMCP 1.0.28 bestand den dokumentierten virtuellen Start/Readiness/Restart/Stop-Ablauf; einzelne Folgeaenderungen warteten noch auf Extension-Host-Reload oder weitere Tests.
- Provider-Unterstuetzung fuer `stateful_marker` wurde versioniert gebaut und installiert. Weitere Browser- und Debug-Checks deckten UI-/Statusfehler auf und fuehrten zu gezielten Korrekturen.

Quelldateien: `20260906-{093420,094207,101803,104153,111134,111203,111739,113925,114411,114506,130326,132345,135105,135621,141947,142632,142737,144106,151749,152232,153135,154657,162012,162401,163016,164104,164811,170818,171411,172320,173000,174324,175317,180331,193628,202250,202825,203114,203507,204234,204334,210647,210744,210955,211532,212006,213406,213742,214000,214155,214503,222639,222801,224027,225046,225346,230709,231941,233429,234008}.md`

## 07.09.2026 – OpenVINO-Abbruch und Host-Stabilitaet

### Load-Steuerung, Diagnostik und offene Thinking-Ausgabe
- Fuer OpenVINO-Loads kam ein expliziter Abbruchpfad hinzu; Cancellation wird an sicheren Synchronisationsgrenzen beachtet, nicht durch gewaltsames Beenden nativer Konstruktoren.
- Doppelte OpenVINO-Core-Erzeugung wurde teilweise auf getrennte DI-Loader/Gates zurueckgefuehrt; eine gemeinsame native Core-Instanz erforderte weiterhin eine API-Erweiterung ausserhalb des vorhandenen Bindings.
- Freeze-Schutz wurde auf Kernel-/systemd-Ebene untersucht und eingerichtet bzw. als Host-Konfiguration dokumentiert; eine riskante absichtliche Freeze-Probe wurde vermieden.
- Ein Buildfehler durch veraltete leere gRPC-Generatordateien wurde per Clean und Neugenerierung behoben. Thinking-Tags blieben offen, da der verwendete VLM-Prompt den Chat-Template-Kontext umging.

Quelldateien: `20260907-{000747,003639,004252,090159,092059,104207,182142}.md`

## 10.09.2026 – OpenVINO-Lifecycle und Hardwarediagnostik

### Native Operations-Gate und Admission Checks
- OpenVINO Load, Unload und Shutdown wurden ueber ein gemeinsames Prozess-Gate serialisiert; Diagnostik vermeidet ungeschuetzte native Core-Erzeugung.
- Fehlende VRAM-Telemetrie unter `/sys` wurde als Warnung behandelt statt jedes Modell vor dem nativen Load abzulehnen; konkrete native Fehler bleiben an die UI durchgereicht.
- Build und fokussierte Diagnostics-/Gate-Tests bestanden. Die erfolgreiche Stabilisierung wurde nicht mit einer Garantie gegen Kernel-/Treiber-Freezes gleichgesetzt.

Quelldatei: `20260910-{000000}.md`

## 11.09.2026 – Geraeteerkennung, SYCL und Statusmodelle

### Vereinheitlichte Runtime- und Device-Vertraege
- Runtime-Verzeichnisse, neutrale GPU-IDs und Device-Status wurden backenduebergreifend vereinheitlicht; das UI unterscheidet fehlgeschlagene Loads (`Clear`) von geladenen Modellen (`Unload`).
- SYCL-Abhaengigkeiten und Level-Zero-Laufzeit wurden nachverfolgt, der Installer-Pfad und rekursive Bibliothekskopien repariert. Anschliessend waren Runtime-Anforderungen `Ready` und ein Qwen-GGUF-Load auf Arc/SYCL wurde am API-Pfad bestaetigt.
- Backend-/Runtime-Paketsettings wanderten in SQLite; Build- und Testlaeufe bestanden, einschliesslich fokussierter Installer-Tests. Ein separates Studio-Chatproblem wurde als nicht erreichbarer Host ohne belegten Codefehler eingeordnet.
- Der Qwen3.8-B70-Leistungsartikel wurde eingeordnet: die berichteten 84,56 tok/s waren ein enger Einzelrequest-Benchmark, kein allgemeiner Servingwert.

Quelldateien: `20260911-{000000,102659,102829,125438,133812,141542,150001,153216,161855,164600,184043,190809,210042}.md`

## 12.09.2026 – Studio-Ausnahme-Diagnose

### Win32Exception im laufenden Host
- Wiederkehrende `System.ComponentModel.Win32Exception`-Meldungen auf Port 7010 sollten lokalisiert und behoben werden.
- Das Protokoll endet mit abgeschlossener Initialisierung, enthaelt aber keinen belegten Ursachenbefund oder Validierungserfolg; der Fehler bleibt in dieser Quelle ungeklaert.

Quelldatei: `20260912-{141843}.md`

## 13.09.2026 – vLLM XPU, BF16-MTP und EsiMCP

### Qwen3.8-Benchmarks und Debug-Integration
- Der BF16-MTP-Pfad fuer vLLM XPU wurde in der Python-Bruecke verdrahtet und ein EngineCore-Fehler behoben; echte Chatgenerierung gelang. Ein neuer Durchsatzwert blieb aus, weil der abschliessende Graph-Compile in einem Lauf nicht Health erreichte.
- Eine direkte vLLM-Anfrage lieferte HTTP 200; der Esi.AI-Studio-Providerpfad blieb zeitweise wegen nicht laufendem Studio bzw. fehlgeschlagenem Debug-Launch unvalidiert.
- EsiMCP erhielt Startfehler-/Readiness-Diagnostik, Debug-Console-Ausgabe und dazu passende Dokumentation; EsiMCP 1.0.31 wurde mit 94/94 Suite-Tests gebaut und installiert.
- OpenVINO-Filter- und Agent-Toolpfade wurden ergaenzt; Provider-/Reasoning-Konfigurationen wurden weiter untersucht.

Quelldateien: `20260913-{000000,115039,131247,131602,141548,200740,202823,212250,223048,224436}.md`

## 14.09.2026 – XPU-Durchsatz und Geraeterouten

### Messmethodik, Optimierung und Statuskorrekturen
- Der Vergleich mit 80+ tok/s wurde als ungueltig eingeordnet, solange MTP4 und identische Decode-only-Bedingungen nicht belegt waren; rund 55 tok/s galt als ehrliche Zwischenbasis.
- Eine `Optimize interactive`-Option speicherte vLLM-/SGLang-Einstellungen fuer Einzelrequest, XPU-Graph und BF16-MTP. Ein A/B-Lauf mit gleichzeitig gestarteten grossen Engines erschoepfte XPU-Speicher und wurde nicht als Messwert verwendet.
- Eine statische RTX-4070-Route wurde entfernt; parallele geladene/pending Modelle wurden als ein Runtime-Eintrag zusammengefuehrt.

Quelldateien: `20260914-{121628,130209,131658,134156,140635,141500,142000}.md`

## 17.09.2026 – XPU-Benchmark und Hugging-Face-Token

### Performance-Messung und Modelldownload
- Ein korrigierter Qwen3.8-27B vLLM-XPU-Graphlauf mit MTP4/BF16 ergab 46,862 tok/s Median ueber drei Laeufe mit je 132 Completion-Tokens; das Ziel aus dem Artikel wurde nicht erreicht.
- Der Hugging-Face-Token wird vor Suche, Repository-Aufloesung und Download aus aktuellen lokalen Einstellungen gelesen, statt beim Singleton-Start eingefroren zu werden.
- Git-Synchronisierung und Konfliktloesung liessen den Autostash bewusst bestehen; ein Submodul-/Branchzustand blieb in einem Protokoll noch nicht vollstaendig aufgeloest.

Quelldateien: `20260917-{000000,120000,153940,154247,155800}.md`

## 18.09.2026 – Page-State-Refactoring

### SignalR- und Komponentenstatus
- Seitenzustand wurde auf je ein Base-State-Objekt mit verschachtelten Bereichs-States und wiederverwendbarem `ActiveModelsState` ausgerichtet.
- Studio-Build, Start und Browser-Smokes fuer Backends, Chats und Settings bestanden. Der volle Testlauf blieb teils haengend bzw. an einem fehlerhaften erwarteten SGLang-Fehlerstatus blockiert.
- Das Protokoll beansprucht daher keinen vollstaendig durchgelaufenen Gesamttestlauf.

Quelldateien: `20260918-{091515,104934,174530}.md`

## 21.09.2026 – Workflow-Routing und Studio-Testplan

### Flow-Seite, persistierte Definitionen und Eingangs-Routing
- Flow-Definitionen wurden in SQLite gespeichert und ueber IDataService/DataHub verwaltet; ein Routing-Service waehlt Chat-, Vision- oder Tool-Workflows fuer OpenAI-Requests und liefert Workflow-Version-Header.
- Editor-/Designer-Integration und Routing wurden schrittweise erweitert; Optimajet-Lizenz und Release-Rechte blieben pruefpflichtig. Spaetere Browser-/Build-Checks bestaetigten Flow-Rendering und erwartete Chat/Tool/Vision-Routen.
- Der vollstaendige Browser-Testplan wurde wegen eines Sicherheitsbefunds gestoppt; weitere Tests waren ohne passende Testmodelle oder Wegwerf-Authentifizierungsdaten blockiert und wurden nicht als PASS gewertet.
- EsiMCP Readiness wurde so erweitert, dass erkannte Startausnahmen sofort als fehlgeschlagene Bereitschaft gemeldet werden.

Quelldateien: `20260921-{082800,104414,105056,110808,120111,120611,125141,144323,160000,175737}.md`

## 25.09.2026 – Backend-Module und Flow-Integration

### Runtime-Abstraktion, Paketaufteilung und Workflows
- `Esi.AI.Backend.Abstractions` und `IBackendRuntime` etablierten einen engine-neutralen Vertrag. Llama Vulkan/CUDA12 sowie vLLM XPU/CUDA12 wurden als getrennte Module begonnen; sechs vorhandene Module wurden in Studio registriert und ueber stabile Variant-IDs aufgeloest.
- Fokussierte Backendtests und Core-/Studio-Testlaeufe bestanden; die vollstaendige Extraktion weiterer Loader blieb als Migration offen.
- Dokumentation wurde unter `docs/projects/` organisiert. Ein Elsa-/Optimajet-Flow-Editor wurde mit Esi.AI-eigener Persistenz und Routing verbunden; Lizenz- und Browser-/Integrationsfragen blieben teilweise offen.
- EsiMCP-Popup-Interzeption wurde ueber oeffentliche VS-Code-APIs getestet; nicht alle proprietaeren oder Webview-Popups sind damit abfangbar.

Quelldateien: `20260925-{000000,093520,093645,094159,095033,095902,101130,101738,102146,103539,103540,103545,131947,141638,152739,183247,183925,190021}.md`

## 26.09.2026 – Launch-Adapter und Hot Reload

### Flow-Livecheck, Debug-Lifecycle und proprietaere Referenzen
- EsiMCPs Projektlaunch wandelte den Projektpfad korrekt in eine VS-Code-URI um; Unit- und Extension-Host-Lifecycletests bestanden. Nach einem Route-Parserfix bestand auch die Flow-Browserpruefung und die Studio-Suite mit 91 Tests und einem Skip.
- Debug-Ausgaben wurden in Debug Console gelenkt und Popup-Reduktion verbessert. Referenz-VSIX-Dateien wurden als lokale Rechercheartefakte behandelt, nicht als zu veroeffentlichende Projektabhaengigkeit.
- Fuer Hot Reload wurde `dotnet watch` als getrennte Alternative untersucht. Task-Registrierung und Builds bestanden, aber der in-task Method-Body-Edit wurde trotz erfolgreicher Fixture-Readiness nicht uebernommen; der End-to-End-Paket-/Installationsnachweis blieb deshalb offen.
- Weitere Sessions hielten einen EsiMCP-2.0.7-Lifecycle-Lauf, RDP-Konfiguration und einen nicht geloesten Watcher-Test getrennt fest.

Quelldateien: `20260926-{095822,124351,132845,163310,182852,190000,190700,202914,203324,213514,213843,214016}.md`

## 28.09.2026 – Backend-Tabs und UI-Gruppierung

### Tab-Struktur und Vorarbeiten
- Die Backend-Toggles wurden in Llama-, vLLM- und weitere Backendgruppen aufgeteilt; eine umfassendere einheitliche Backend-Tab-Oberflaeche wurde im Browser fuer acht Tabs auf Renderfehler geprueft.
- Ein ungueltig benanntes, versehentlich getracktes Git-Artefakt wurde entfernt; die Aenderung muss noch committed/pushed werden, bevor sie anderen Rechnern hilft.
- Ein Flow-Lifecycle-Follow-up traf bei einem Startversuch auf `spawn dotnet ENOENT`; die konkrete PATH-/Arbeitsverzeichnisursache blieb in diesem Protokoll offen.

Quelldateien: `20260928-{000000,085905,091231,092806,135648,135756,183136,185041}.md`

## 29.09.2026 – Hardware-Erkennung und Device-Routing

### Backend-spezifische Discovery und persistente Requirements
- CUDA12-, SYCL- und OpenVINO-Routen wurden an die jeweilige Device-Discovery angebunden. Der diskrete Intel-Adapter `0xe223` wurde als dGPU bevorzugt; RTX 4070 wurde fuer CUDA sichtbar, waehrend SYCL nur Intel zeigte.
- VLLM CUDA12 ermittelte die NVIDIA-GPU direkt ueber den Treiber statt ueber LLama/GGML; korrigierte Studio-Referenz und initialer Page-Read liessen den RTX-Eintrag im Browser erscheinen.
- Backend-Requirements wurden pro aktiviertem Backend entkoppelt ausloesbar; EsiMCP-Start-/Readiness-Pfade bestanden teilweise live.
- Ein OpenVINO-Testprojekt blieb durch vorhandene `AvailableDevices`-Compilefehler blockiert; andere Pfade meldeten fehlende Python-Installationen bzw. nicht verfügbare Geraete.

Quelldateien: `20260929-{093305,093919,095941,102334,121500,124828,131514,144637,160500,161500}.md`

## 30.09.2026 – Persistenz, Prerendering und Trainingsrecherche

### Backend-Status, PageState und Nachfolgeoptionen
- Device-Routen wurden variantenspezifisch in `ModelSettings` persistiert; ein Audit zeigte zuvor verlorene vLLM/SGLang/dotLLM-Auswahlen ohne Profil und fuehrte zu Migration und Regressionstests.
- Begrenzte Blazor-`PersistentState`-Snapshots wurden fuer geeignete Seiten eingefuehrt. Secrets, Chattranskripte, Bilder und grosse Trace-/Logdaten blieben ausgeschlossen; SignalR bleibt fuer Navigation, Mutationen und Live-Updates zustaendig.
- Backend-Tabs blieben nach Full-Page-Reload stabil; Client-Build und Browserpruefungen fuer Backends, Models, Chats, Provider und Home bestanden.
- Weitere Sitzungen bewerteten Strata und Unsloth: Training, Inferenz und .NET-Orchestrierung wurden getrennt; Strata kam als geprueftes Submodul hinzu.

Quelldateien: `20260930-{000000,000001,093115,104435,111057,111919,120000,130000}.md`

---

## Gesamtverlauf
- Der VS-Code-Provider entwickelte sich von Modell-Picker-/Aktivierungsfehlern zu einem versionierten Capability-, Tool-Call- und multimodalen API-Pfad. Erfolgreiche End-to-End-Checks sind belegt, waehrend OpenVINO `-17` und einzelne Copilot/SSE-Haenger weiter als konkrete Lifecycle-/Runtime-Probleme untersucht wurden.
- Die API- und Runtime-Zustaende wurden backendneutraler: Modellkonfigurationen, `AutoLaunch`, stabile Backend-Variant-IDs, SignalR-Collection-CRUD und serverseitig geladene Statusquellen wurden ausgebaut.
- Vision wurde fuer OpenVINO und LLamaSharp mit echten lokalen Bilddaten validiert. Python-vLLM/SGLang-Bruecken und dotLLM blieben in den Quellen text-only; weitergehende Bildverarbeitung erforderte zusaetzliche Integration.
- Intel Arc/SYCL wurde von fehlenden Laufzeitabhaengigkeiten bis zu `Ready` und einem echten Modell-Load vorangebracht. Qwen3.8 vLLM XPU erreichte in einem reproduzierbaren Graph/MTP4-Lauf 46,862 tok/s, nicht den publizierten 80+-Wert.
- Runtime- und Hardware-Erkennung wurde von statischen GPU-Routen zu backend- und variantenspezifischen Device-IDs, Discovery und Persistenz weiterentwickelt.
- EsiMCP gewann robustere Launch-, Readiness-, Restart- und Debug-Console-Pfade. Der `dotnet watch` Hot-Reload-Edit blieb in einem End-to-End-Test unbestaetigt.
- Flow wechselte von Definitionen und UI-Prototypen zu persistiertem Chat/Vision/Tool-Routing und Browservalidierung. Optimajets Lizenzbedingungen bleiben vor einer Veroeffentlichung zu pruefen.
- Page-State und InteractiveAuto-Prerendering wurden verbessert; sensitive, private oder unbegrenzte Daten wurden ausdruecklich aus serialisierten Snapshots ausgeschlossen.
- Validierungen waren oft fokussiert und erfolgreich, aber nicht jeder Gesamt- oder Hardwaretest lief: offene Abhaengigkeiten umfassten native OpenVINO-Fehler, vorhandene Compilefehler, fehlende Wegwerf-Credentials/Testmodelle, nicht installierte Python-Umgebungen und externe Lizenz-/Releasepruefungen.
