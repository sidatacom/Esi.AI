# Oktober 2026 – Sitzungszusammenfassung

Zusammenfassung von 73 Sitzungsprotokollen aus `docs/history/2026/10/`
(01.–16.10.2026; Protokolle an 10 Tagen).

---

## 01.10.2026 – Backend-Profile, Studio-Funktionen und Host-Stabilität

### Backend-Profile, Varianten und Laufzeittests
- Für fünf Backendfamilien wurden `Referenz-Test`-Profile angelegt; LLama, OpenVINO und dotLLM bestanden einzelne Lade-/Generierungstests, während vLLM und SGLang an fehlenden Runtime-Voraussetzungen scheiterten.
- Die Variantauflösung wurde für gespeicherte Profile vereinheitlicht; Regressionstests deckten Variantenkonflikte und SGLang-Routing ab.
- Spätere Chatversuche lieferten keinen vollständigen Erfolg: LLama blieb ohne Antwort, OpenVINO verlor die Datenverbindung, und vLLM war durch fehlende CUDA-/Python-Voraussetzungen blockiert.
- Quellen: `20261001-111116.md`, `20261001-132709.md`, `20261001-140736.md`

### Modellkatalog, Runtime-Logs und Download-Metadaten
- Die native `ggml_vulkan`-Geräteausgabe wurde aus dem Debuglog in einen eigenen Runtime-Log-Tab unter Settings verlagert.
- Ein OpenVINO-Referenzmodell wurde auf ein passendes IR-Repository zurückgestellt; die lokale Suche erkennt nun zusätzlich `openvino_model.xml`.
- Nach Hugging-Face-Downloads lädt die Models-Seite die serverseitige Local-Model-Sammlung neu, damit HF-Referenz, Backend-Kompatibilität und Provider-Metadaten erhalten bleiben.
- Ein fehlender SGLang-Pythonpfad wurde auf Hostebene repariert; die verbleibende Snap-Pfad-Abhängigkeit ist dokumentiert.
- Quellen: `20261001-113343.md`, `20261001-113402.md`, `20261001-120000.md`, `20261001-131005.md`

### Neue Funktionen, Git und Flow-Designer
- Eine QLoRA-Trainingsseite samt neuem `Esi.AI.PyTorch`-Assembly wurde umgesetzt; 7 fokussierte Tests und UI-/Buildprüfungen bestanden, ein echter XPU-Trainingslauf blieb wegen fehlender Python-Abhängigkeiten ungetestet.
- Der Strata-Submoduleintrag wurde entfernt, während das ungetrackte Verzeichnis erhalten blieb; ein separater Git-Divergenzfall wurde konfliktfrei mit `origin/main` zusammengeführt.
- Beim Flow-Designer wurden Elsa-Portdarstellung, Canvas-Größe und kompakte Toolbar verbessert; Browsermessungen bestätigten die Karten im Desktop-Canvas und null Außenpadding auf Mobilgeräten.
- Quellen: `20261001-122055.md`, `20261001-123549.md`, `20261001-133645.md`, `20261001-135442.md`

### Freeze-Diagnose und OpenVINO-Umgebung
- Vorhandene Systemmessungen belegten keinen RAM-OOM als Ursache der Freezes; zwei Kernel-Crashmeldungen zeigten stattdessen einen .NET-Segfault in `libopenvino.so` bei einem OpenVINO-Modell.
- OpenVINO Core und GenAI wurden auf verifizierte 2026.4-Pakete angehoben und Studio erfolgreich gebaut; ein Modelllauf zur Bestätigung der Freeze-Behebung wurde bewusst nicht durchgeführt.
- `systemd-oomd` lief bereits, EarlyOOM und Swappiness wurden nicht geändert; die erforderliche Host-Privilegienfreigabe lag nicht vor.
- Quellen: `20261001-154624.md`, `20261001-182637.md`

---

## 02.10.2026 – Flow-Layout, Referenzmodellprüfung und Backend-Runtime

### Flow-Designer und Backends-Oberfläche
- Die Flow-Karten wurden vor der Elsa-Initialisierung auf 220 px gesetzt und mit 275 px Abstand positioniert; drei Karten passten mit sichtbaren Kanten in den gemessenen Canvas.
- Die mobile Flow-Shell behielt null Außenpadding; der separate Flow-Kopf wurde zugunsten einer kompakten Toolbar entfernt.
- Für das Backends-Konfigurationspanel wurde die umgebende Workspace-Breitenbegrenzung als wahrscheinliche Ursache der zu schmalen Darstellung eingegrenzt.
- Quellen: `20261002-103708.md`, `20261002-120558.md`

### Backend- und OpenVINO-Validierung
- Die veröffentlichten Default-Flow-Routen wurden als Ursache eines vor der Inferenz scheiternden Chats gefunden und repariert; fokussierte Routingtests bestanden 11/11.
- OpenVINO mit dem 1.5B-IR-Modell lieferte im Studio einen vollständigen Chat; das separate 0.5B-IR-Modell scheiterte weiterhin mit nativer `Unknown error (-17)`.
- Der 0.5B-Fehler wurde auf einen stateless exportierten IR-Zustand zurückgeführt; ein stateful Export desselben Checkpoints bestand begrenzte Streamingtests, während das bestehende Profil unverändert blieb.
- Für SYCL wurden globale Library-Pfad-/Level-Zero-Overrides entfernt und Worker-Diagnostik verbessert; spätere Prüfungen bestätigten Geräteerkennung und Laden, aber keinen Chat.
- Quellen: `20261002-114006.md`, `20261002-123634.md`, `20261002-125306.md`

### Backendumfang und verbleibende Grenzen
- SGLang und dotLLM wurden aus den aktiven Studio-Routen und auswählbaren Oberflächen entfernt; ihre Enumwerte und Legacy-Datensätze blieben zur Persistenzkompatibilität erhalten.
- Vulkan-, CUDA-12-, OpenVINO- und vLLM-Pfade wurden mit unterschiedlichen Lade-/Chatresultaten geprüft; nach einem SYCL-Prozessabbruch wurden weitere riskante Modellstarts zunächst gestoppt.
- Die fokussierten Backend-Tests bestanden, während reale Modell-/Hardwarepfade und bestehende OpenVINO-/Hostfehler als offene Arbeit festgehalten wurden.
- Quellen: `20261002-142150.md`

---

## 03.10.2026 – Backend-Matrix, vLLM-XPU und Speicherbedarf

### Backend-Routen und vLLM-XPU
- Das Backend-Testprotokoll erfasste LLama Vulkan/CUDA/SYCL, OpenVINO sowie vLLM CUDA/XPU mit klarer Trennung zwischen geladenem Modell und erfolgreichem Chat.
- Ein vLLM-XPU-Konfigurationsfehler wurde auf Profilhydration, explizites `xpu:0`-Routing und ungewollt aktivierte MTP-Tokens zurückgeführt; Profileinstellungen wurden erhalten.
- Die XPU-Antwortqualität wurde isoliert: Transformers auf CPU und Intel XPU antwortete korrekt, während vLLM-XPU NaN-Logits und wiederholte `lock`-Tokens lieferte.
- Durch Deaktivieren des einzelnen fehlerhaften `silu_and_mul`-Custom-Ops und Entfernen eines nicht unterstützten `device`-Arguments bestand vLLM-XPU anschließend direkte, WebAPI- und Browserprüfungen; CUDA12 wurde später ebenfalls mit passender Umgebung live geprüft.
- Quellen: `20261003-125923.md`, `20261003-173453.md`

### Backendumfang und Speicheranalyse
- SGLang und dotLLM wurden aus den aktiven Studio-Backends entfernt; Legacy-Enumwerte und vorhandene Profile blieben erhalten.
- Die Root-Partition war nahezu voll; die größten Verbraucher waren Esi.AI-/LM-Studio-Modellbestände, Python-Umgebungen, Paket-Caches und das Git-Repository.
- Eine Models-Clean-Funktion mit Vorschau, erneuter Kandidatenprüfung und Schutz konfigurierter Modelle wurde ergänzt; eine bestätigte Löschung gab 22,46 GiB frei.
- Eine anschließende Inventur zeigte, dass der NVMe-Datenträger nahezu vollständig belegt, aber nicht ganz voll war; es wurden keine weiteren Dateien ohne Zustimmung gelöscht.
- Quellen: `20261003-102542.md`, `20261003-150046.md`, `20261003-221358.md`, `20261003-222138.md`, `20261003-222339.md`

---

## 04.10.2026 – OpenVINO-Streaming und Kontextgrenzen

### OpenVINO-Generierung und Modellunterschiede
- Zero-Temperatur-Anfragen nutzen nun Greedy-Generierung; ein fokussierter OpenVINO-Test und ein echter 1.5B-Studio-Chat bestanden.
- Das 0.5B-IR-Modell scheiterte weiterhin unabhängig von Streaming mit `Unknown error (-17)`; der Fehler blieb getrennt von der erfolgreichen 1.5B-Prüfung.
- Ein früherer Fehler beim Chat-Routing wurde eingegrenzt, als Debugger-Ausnahmen den scheinbaren Stillstand verursachten; ein nicht zugeordneter `SchemeNotFoundException` blieb weiter untersuchungsbedürftig.
- Quellen: `20261004-111356.md`

### Lange OpenVINO-Prefills
- Mit PagedAttention und u4-KV-Cache wurde ein vollständiger Lauf mit 118.052 tatsächlichen Prompt-Tokens gemessen; usage, Stop-Grund und `[DONE]` waren vorhanden.
- Ein erwarteter Schritt auf 119.052 Tokens endete ohne Abschluss und korrelierte mit Xe-`VM worker error: -105`; ein Wiederholungslauf bei 118.052 Tokens blieb ebenfalls ohne Abschluss.
- Die gemessenen Läufe belegen 118.052 als erfolgreiche Größe, aber keine exakte maximale Kontextgrenze; Ursachen wie Speicherallokation oder Treiberverhalten blieben offen.
- Quellen: `20261004-150943.md`, `20261004-153300.md`, `20261004-164800.md`

---

## 05.10.2026 – OpenVINO-Abbruch und Inferenz-Lebenszyklus

### Streaming-Abbruch und verbleibende Unsicherheit
- Die lange OpenVINO-Anfrage wurde nach nur initialen SSE-Chunks abgebrochen; für die Wiederholung lagen keine Xe-Fehlerbelege vor, daher wurde der frühere `-105`-Fehler nicht als deren Ursache ausgegeben.
- Die native Generierungs-API nimmt keinen Cancellation-Token entgegen; die Streaming-Callbacks signalisierten Abbruch erst, wenn die native Prefill-Phase zurückkehrte.
- Cancellation wurde durch den OpenVINO-Chatpfad weitergereicht; ein anschließender Watch-Modus-Test zeigte nach Client-Abbruch wieder responsive Statusendpunkte und eine erfolgreiche kurze Inferenz.
- Im Debugger pauste eine erwartete `OperationCanceledException` den gesamten Host; das Entfernen des redundanten Worker-Throws verhinderte diese Pause, aber ein weiterer Debugger-Lauf zeigte weiterhin einen Inferenz-Lock-Stillstand.
- Quellen: `20261005-113510.md`, `20261005-121602.md`, `20261005-121741.md`

---

## 06.10.2026 – Qwen3.8-Leistung und OpenVINO-Langkontext

### Benchmark-Auswahl und Ressourcen
- vLLM-XPU wurde als erste lokale Optimierungskandidatin für Qwen3.8 bewertet; vorhandene Kurzprompt-Decodierwerte wurden ausdrücklich nicht als 265k-/Langkontextleistung ausgegeben.
- Das Modell hat eine native Kontextgrenze von 262.144 Tokens; 265k liegt darüber, und vollständige Langkontextleistung war noch nicht gemessen.
- Der Versuch, LM Studio zu deinstallieren, wurde mangels sudo-Autorisierung blockiert; Konfiguration und Modelle blieben erhalten.
- Quellen: `20261006-123156.md`, `20261006-124936.md`

### OpenVINO-Routing und Prefill
- Das generische GPU-Routing wurde auf das erkannte B70-Gerät `GPU.1` aufgelöst; kurze nicht-streamende und SSE-Anfragen wurden abgeschlossen.
- 32.820 Prompt-Tokens bestanden mit PagedAttention und u4-KV-Cache; größere Anfragen erzeugten Xe-VM-/`EINVAL`-Fehler, ohne vollständige SSE-Antwort.
- Eine 118.052-Token-Anfrage wurde mit vollständiger Nutzungsmeldung und `[DONE]` abgeschlossen; das anschließende 119.052-Token-Inkrement blieb unvollständig und löste erneut `-105` aus.
- Eine Wiederholung bei 118.052 Tokens blieb stecken, diesmal ohne korrespondierenden Xe-Fehler im erfassten Zeitfenster; die genaue obere Grenze und Ursache blieben offen.
- Quellen: `20261006-140255.md`, `20261006-164849.md`, `20261006-171504.md`, `20261006-172200.md`, `20261006-175756.md`, `20261006-185139.md`, `20261006-191704.md`

### Abbruchverhalten und Folgetests
- Das Abbruchverhalten wurde als mögliche Ursache verifiziert: eine native Generierung konnte nach Client-Abbruch weiterlaufen und den Runtime-Lock halten.
- Ein debuggerfreier Watch-Lauf erholte sich nach einem kurzen Client-Abbruch und schloss eine anschließende Ein-Token-Inferenz erfolgreich ab.
- Bei erneutem Debuggerbetrieb stoppte eine behandelte Cancellation-Ausnahme den gesamten Host; nach Korrektur blieb eine lange native Generierung jedoch gesperrt, sodass keine vollständige Langkontextlösung behauptet wurde.
- Quellen: `20261006-192442.md`

---

## 07.10.2026 – Benchmark-Oberfläche, Cancellation und Laufzeit-Gates

### Benchmark-UI und persistierte Runs
- Die Benchmark-Funktion wurde in eine Studio-Seite mit Profil-/Backend-Auswahl und Streamingläufen über die vorhandene SignalR-Servicearchitektur verlagert.
- BenchmarkRuns speichern inkrementelle Messungen und einen unveränderlichen Profil-/Modell-Snapshot; History lässt sich unabhängig vom späteren Profilzustand filtern und löschen.
- Ein Server-Prerender-Fehler wurde auf eine fehlende `IBenchmarkRunEvents`-Registrierung zurückgeführt; nach dem Adapter- und DI-Fix wurde `/benchmark` erfolgreich angezeigt.
- Quellen: `20261007-125208.md`, `20261007-130000.md`

### Profile und Qwen3.8-Testläufe
- Die fünf nicht-Qwen3.8-Profile mit dem exakten Namen `Benchmark` wurden über die bestehende CRUD-Oberfläche gelöscht; fünf Qwen3.8-Profile blieben erhalten.
- Ein dokumentierter SYCL-128k-Lauf wurde zunächst durch ein zu kurzes Client-Timeout abgeschnitten; die Timeoutbudgetierung wurde angepasst und ein späterer Lauf mit 128.011 Prompt- und 128 Completion-Tokens vollständig beendet.
- Beim anschließenden CUDA12-Load traten nacheinander ein LLamaSharp-Prozesskonflikt und fehlende `libcudart`-/`libcublas`-Bibliotheken auf; kein CUDA12-Probe wurde gesendet.
- Vulkan scheiterte beim Laden, OpenVINO wurde bei starker RAM-/Swap-Belastung gestoppt und vLLM-XPU blieb beim Start hängen; der gesamte Backendlauf bestand daher nicht.
- Quellen: `20261007-130001.md`, `20261007-150858.md`

### Cancellation-Diagnose und Backend-Testprojekt
- Status- und Cancellationpfade wurden weiter instrumentiert; Änderungen verbesserten den Umgang mit abgebrochenen Model-List-Anfragen und machten LLama-Offload-Zähler aussagekräftiger.
- Ein Live-128k-SYCL-Run erhöhte den Swapverbrauch stark und wurde ohne Benchmarkrequest gestoppt; der Anomaliewert `assignedLayerCount=0` stammte aus einer Discovery-Platzhalterquelle und belegte keine fehlende Offload-Zuweisung.
- Der gemeinsame Qwen3.8-Artefakt-Gate blieb blockiert: Checkpoint-, Revision- und Quantisierungsprovenienz sowie die Kompatibilität aller Runtimes waren nicht belegt.
- XPU- und CUDA12-Python-Prüfungen fanden getrennte Plugin-/Paketprobleme; keine Runtime-Reparatur oder Modelländerung wurde vorgenommen.
- Quellen: `20261007-092041.md`, `20261007-151000.md`, `20261007-171921.md`, `20261007-174355.md`

---

## 08.10.2026 – EsiMCP-Verbindung, Modellpfade und Benchmark-Projekt

### EsiMCP pro VS-Code-Fenster und Git
- EsiMCP wechselte von einer ungültigen statischen MCP-URL zu einer dynamischen VS-Code-MCP-Definition pro Extension Host; Port 0 erlaubt unabhängige Instanzen.
- Die Statusleiste zeigt nach erfolgreichem Listenerstart Workspace und Port statt einer nicht aussagekräftigen Terminalanzahl; fokussierte Tests, Paketierung und Installation wurden durchgeführt.
- Die Tool-Bridge wurde als workspace-lokaler Streamable-HTTP-MCP-Server erklärt; ein separater LLamaSharp-Repositoryfehler wurde durch Entfernen defekter Refs und Wiederherstellen des gepinnten llama.cpp-Commits behoben.
- Quellen: `20261008-131351.md`, `20261008-145304.md`, `20261008-150128.md`

### Benchmark- und Modellabläufe
- Benchmark-Prompts und Skillanweisungen wurden für sequenzielle, readiness-geprüfte Läufe präzisiert; Model-free Tests sollten klar von Live-Messungen getrennt bleiben.
- Ein Modellverzeichnis mit rund 124 GiB wurde nach `/home/llm/models` verschoben und die Esi.AI-Datenbankpfade angepasst; Quelle und Ziel lagen auf derselben Root-Partition, daher entstand kein freier Speicherplatz.
- Die gemeinsame Backend-Testplanung dokumentierte Artefaktprovenienz als Gate; GGUF, OpenVINO IR und GPTQ-Kandidaten hatten unterschiedliche Formate und keine belegte identische Quell-/Quantisierungsherkunft.
- Quellen: `20261008-153142.md`, `20261008-153625.md`, `20261008-163206.md`, `20261008-164236.md`, `20261008-165355.md`, `20261008-171240.md`

### Benchmark in Studio
- Die interaktive Benchmark-Seite wurde in Studio eingeführt, liest Profile und Laufzeitstatus über `IDataService`/SignalR und startet oder entlädt keine Modelle automatisch.
- Persistierte Benchmarkläufe halten Messwerte und unveränderliche Profil-/Modell-Snapshots fest; die Studio-Lösung wurde gebaut, die Migration aber nicht auf eine Datenbank angewendet.
- Quellen: `20261008-185845.md`

---

## 09.10.2026 – Benchmark-Validierung und Workflow-Prozessfehler

### Benchmark-UI, Profile und Dokumentation
- Die Benchmark-Seite erhielt einen fehlenden Server-Prerender-Eventadapter; `/benchmark` wurde danach im Studio erfolgreich gerendert.
- Fünf `Benchmark`-Profile ohne `Qwen3.8` im Namen wurden entfernt, während alle fünf namensgleichen Qwen3.8-Profile und alle Benchmark-History-Einträge erhalten blieben.
- Skill und Projektplan wurden überarbeitet, um Readiness, identische Artefakte, Herkunftsprovenienz sowie die getrennte Auswertung von Unit-Tests und Live-Benchmarks festzuhalten.
- Quellen: `20261009-100149.md`, `20261009-105732.md`, `20261009-111825.md`

### Live-Benchmark und Backend-Grenzen
- Ein SYCL-Lauf beendete 128.011 Prompt- und 128 Completion-Tokens mit `finish_reason=stop` und `[DONE]`; die CLI-Messzeile fehlte, daher stammen Zeitdaten aus dem Provider-Trace.
- Beim CUDA12-Ladeversuch wurden zunächst prozessweite LLamaSharp-Konfiguration und anschließend fehlende CUDA-Runtimebibliotheken festgestellt; kein Benchmarkrequest wurde gesendet.
- Vulkan scheiterte beim Laden, OpenVINO wurde bei starkem Swapwachstum gestoppt und vLLM-XPU blieb vor Inferenz beim Pluginimport hängen; die All-Backend-Anforderung blieb unerfüllt.
- Spätere Benchmarkläufe waren zusätzlich durch nicht verfügbare Studio-Sessions, Artefaktprovenienz, unvollständige Profile und Swap-Gates blockiert; keine fehlgeschlagenen Anfrage wurde blind wiederholt.
- Quellen: `20261009-112544.md`, `20261009-202822.md`, `20261009-202849.md`

### Gemeinsames Backend-Testprojekt
- Das Projekt hielt den identischen Qwen3.8-Artefakt-/Quantisierungsnachweis als zwingendes Gate fest; fehlende Modelle unter alten Pfaden und getrennte GGUF-/IR-/GPTQ-Artefakte verhinderten eine gemeinsame Freigabe.
- Ein CUDA12-Python-3.12-Kandidat wurde mit vLLM, Torch, gRPC, Protobuf und RTX-Gerät geprüft; das GGUF-Plugin wurde durch den Installationsschutz blockiert und nicht über einen alternativen Weg umgangen.
- Der XPU-Pfad importierte zwar die verwaltete Runtime, doch GGUF-Unterstützung für XPU war nicht belegt; vorhandene Profile und Modellartefakte blieben unverändert.
- Quellen: `20261009-140834.md`, `20261009-143544.md`, `20261009-193639.md`, `20261009-193923.md`

### Workflow-Scheme und OpenVINO-Folgefehler
- `SchemeNotFoundException` wurde auf das fehlende `WorkflowProcessScheme`-Materialisieren vor dem Prozessstart zurückgeführt; der erste Abrufversuch reichte nicht, die explizite Erzeugung des Prozess-Schemes behob den nachgewiesenen Fehler.
- Ein frischer Studio-Start und ein API-Probe mit unbekanntem Modell bestätigten, dass die Anfrage den Workflow-Router passierte.
- Ein zusätzlich gemeldeter OpenVINO-Bildtensorfehler in `VLMPipeline.GenerateWithHistory` blieb mangels genauer Modell-/Geräte-/Idle-Evidenz offen; dafür wurde kein spekulativer Workaround eingeführt.
- Quellen: `20261009-203357.md`

---

## 16.10.2026 – Workflow-Scheme-Fix und offener OpenVINO-Bildfehler

### Prozess-Scheme und native VLM-Ausnahme
- Für neu angelegte Workflowdefinitionen wird vor `CreateInstanceAsync` nun explizit `CreateNewProcessSchemeAsync` aufgerufen; Build und ein API-Probe bestätigten, dass der gemeldete Scheme-Fehler nicht mehr auftrat.
- Das Studio lief nach dem Fix mit bestätigter Host-Readiness; vorhandene Prozess-/Datenbankdaten wurden nicht blind gelöscht oder migriert.
- Die zusätzliche `OVException UNKNOWN_EXCEPTION (-17)` bei Bildtensoren trat weiterhin als gemeldeter Fehler auf, aber die Evidenz unterschied nicht zwischen Idle-Runtime, Request-Abbruch und modell-/gerätebezogener VLM-Ursache.
- Quelle: `20261016-122108.md`

---

## Gesamtergebnis Oktober

- Studio- und Backendverträge wurden weiter vereinheitlicht: profilbasierte Runtime-Varianten, serverseitige Modellmetadaten, explizite CRUD-Pfade und persistierte Benchmark-Snapshots.
- Die Backend-Landschaft wurde operativ auf LLama, OpenVINO und vLLM fokussiert; Legacywerte blieben erhalten, während SGLang und dotLLM aus aktiven Studio-Funktionen entfernt wurden.
- Ein stateful OpenVINO-IR-Export beseitigte den spezifischen 0.5B-IR-Generierungsfehler; ein 1.5B-OpenVINO-Chat sowie begrenzte 32k-/118k-Prefills bestanden.
- Die OpenVINO-Grenze ist nicht abschließend bestimmt: 119.052 Tokens scheiterten, ein Wiederholungslauf bei 118.052 stockte, und Cancellation während nativer Prefill bleibt ein offener Laufzeit-/Lock-Fall.
- vLLM-XPU-Antwortqualität für Qwen2.5 wurde auf einen Custom-Op-Fehler eingegrenzt und durch selektives Deaktivieren von `silu_and_mul` behoben; CUDA12-Chatpfade bestanden mit korrekt bereitgestellter cu129-Umgebung.
- Qwen3.8-128k-Tests lieferten einen vollständigen SYCL-Lauf mit 128.011 Prompt-Tokens, während CUDA12, Vulkan, OpenVINO und vLLM-XPU durch Laufzeit-, Ressourcen- oder Startfehler blockiert blieben.
- Die gemeinsame Qwen3.8-Artefaktprovenienz für alle Backends blieb unbewiesen; Q4_K_S, OpenVINO-AWQ-IR und GPTQ-INT4 dürfen daher nicht als identischer Benchmark-Artefaktbestand behandelt werden.
- Der Modellbestand wurde auf dieselbe Root-Partition verschoben, wodurch kein Speicherplatz frei wurde; Hostbereinigung und Deinstallation blieben durch Zustimmung/Privilegien beziehungsweise Schutzregeln begrenzt.
- Offen bleiben die präzise OpenVINO-Langkontext-/Cancellation-Ursache, ein belastbarer gemeinsamer Qwen3.8-Artefaktnachweis und vollständige, reproduzierbare Ergebnisse für alle Backendvarianten.
