# Esi.AI Studio Layer-Design

## Ziel

Studio soll vom Runtime-Betrieb bis zur einzelnen Anfrage eine nachvollziehbare Kette zeigen:

```text
Backend  >  Flow  >  Request
Runtime     Orchestration   Ausfuehrung und Diagnose
```

Das ist eine Navigations- und Verantwortungsstruktur. Zur Laufzeit fliesst eine Anfrage in Gegenrichtung: Request -> Flow -> Backend -> Antwort. Ein Flow waehlt nicht nur ein Modell, sondern macht nachvollziehbar, welche Schritte eine Anfrage durchlaeuft und warum.

## Die drei Ebenen

### Backend

**Aufgabe:** Inference-Runtimes, Modelle, Hardware und deren Faehigkeiten betreiben.

Die Backend-Ebene beantwortet: *Was kann gerade ausgefuehrt werden?* Sie zeigt geladene und verfuegbare Modelle, Runtime-/Device-Status, Kapazitaet, Modellfaehigkeiten sowie Fehler und Voraussetzungen. Routing-Regeln gehoeren nicht in Backend-Konfigurationen; Backend liefert Faehigkeiten und Zustand, Flow trifft die Auswahl.

### Flow

**Aufgabe:** Anfrageverarbeitung als versionierten, testbaren Ablauf modellieren.

Flow ist der visuelle Arbeitsbereich. Eine Flow-Definition verbindet Eingang, Sicherheitspruefungen, Kontext, Agentenlogik, Werkzeuge, Transformation und Antwort. Die Aktivitaetenpalette sollte diese Kategorien anbieten:

| Gruppe | Aktivitaeten | Verantwortung |
| --- | --- | --- |
| Eingang und Steuerung | Request Trigger, Bedingungen, Switch, Fan-out | Anfrageform erkennen und Pfade waehlen |
| Routing | Modell-/Backend-Auswahl, Faehigkeitspruefung, Fallback | Passendes Modell anhand von Aufgabe, Faehigkeit und Laufzeitstatus waehlen |
| Sicherheit | Eingabe-/Ausgabepruefung, Policy, PII-Filter, Freigabe | Risiken und Berechtigungen vor Werkzeugen oder Ausgabe kontrollieren |
| Kontext | RAG Retrieval, Quellen zusammenfuehren, Kontextbudget | Relevante, mandantengerechte Belege beschaffen und begrenzen |
| Agent | Agent Loop, Planner, Tool-Auswahl, Handoff | Modellschritte und begrenzte Werkzeugaufrufe koordinieren |
| Integrationen | MCP-Server/Tools, lokale Tools | Werkzeuge finden, autorisieren und aufrufen |
| Transformation | Prompt-/Nachrichten-Mapping, JSON-Schema, Normalisierung | Ein- und Ausgabeformate kontrolliert abbilden |
| Zuverlaessigkeit | Retry, Timeout, Rate-/Tokenbudget, Circuit Breaker | Laufzeit und Ressourcen begrenzen, Fehler gezielt behandeln |
| Ergebnis | Antwort, Streaming, Quellen/Zitate | Antwortvertrag und Quellen an den Request zurueckgeben |

**Wichtige Modellierung:** MCP ist primaer eine Integrations- und Tool-Quelle, nicht pauschal ein eigener Verarbeitungsschritt. MCP-Server und ihre freigegebenen Tools werden in einer zentralen Integrationsverwaltung gepflegt; ein Flow-Agent erhaelt nur die explizit ausgewaehlten Tools. RAG ist als abrufbare Kontextquelle modelliert. Transform sollte getrennte Ein- und Ausgabe-Mapping-Schritte erlauben. Ein Agent ist die kontrollierte Schleife, die Modell, Instruktionen, Kontext und erlaubte Tools zusammenspielt.

### Request

**Aufgabe:** Konkrete Ausfuehrungen pruefen und erklaeren.

Request ist zugleich Playground und Betriebsdiagnose, keine zweite Workflow-Konfiguration. Eine Anfrage kann mit Beispielpayload gegen einen ausgewaehlten Flow getestet werden. Die Ausfuehrungsansicht zeigt Request-ID, Flow-Version, gewaehltes Backend/Modell, Schritt-Timeline, Dauer, Tokenverbrauch, Tool-Aufrufe, RAG-Quellen, Retries und Fehler. Geheime Werte und sensible Inhalte muessen standardmaessig maskiert sein; Payload-Aufbewahrung und Redaction sind explizit konfigurierbar.

## Agent-Bereich

Ein moderner Agent-Editor sollte die Kontrollgrenzen sichtbar machen, statt einen undurchsichtigen Chatbot-Knoten anzubieten. Pro Agent werden konfiguriert:

- **Instructions:** System-/Rollenanweisung mit Vorschau der zusammengesetzten Nachrichten.
- **Model policy:** bevorzugtes Modell, Faehigkeitsanforderungen, Routing und erlaubte Fallbacks.
- **Tools:** explizite Tool-Allowlist aus MCP und Studio-eigenen Integrationen; Argument-Schema und Berechtigungen je Tool.
- **Context:** verbundene RAG-Quellen, Filter, Top-k, Zitierpflicht und Kontextbudget.
- **State and memory:** bewusst getrennte Laufzeitvariablen, Gespraechszustand und persistente Erinnerung; Speicherumfang und Mandantenscope sind sichtbar.
- **Limits:** maximale Agent-Schritte, Tool-Aufrufe, Laufzeit und Tokens; Abbruchverhalten und Retry-Policy.
- **Approval:** menschliche Freigabe fuer ausgewaehlte Tools oder Aktionen mit Vorschau der Argumente.
- **Evaluation:** Testfaelle mit erwarteten Quellen, Tool-Auswahl, Schema und Erfolgsbedingungen.

Die Agent-Ausfuehrung endet bei einer finalen Antwort, einem expliziten Handoff, einer Freigabe oder einem sichtbaren Limit-/Fehlerzustand. Kein Tool-Aufruf darf allein durch Modelltext autorisiert werden: Flow-Policy und Tool-Allowlist bleiben massgeblich.

## Editor-Aufbau

Empfohlene Arbeitsflaeche fuer breite Ansichten:

```text
+----------------------+--------------------------------------+----------------------+
| Aktivitaeten         | Flow-Canvas                          | Eigenschaften        |
| Suche und Kategorien | Knoten, Verbindungen, Zoom, Auswahl  | Konfiguration,       |
|                      |                                      | Validierung, Output  |
+----------------------+--------------------------------------+----------------------+
| Run-Konsole: Run | Trace | Issues | Versionen | Compare                        |
+--------------------------------------------------------------------------------+
```

- **Kopfzeile:** Breadcrumb `Flow / Support Agent`, Draft/Published-Badge, Versionsauswahl, Run und Publish.
- **Linke Palette:** Suche und gruppierte Knoten, mit klarer Unterscheidung zwischen Flow-Control, Agent, Data und Integration.
- **Canvas:** gerichtete Ausfuehrung von links nach rechts, minimap, Zoom, Mehrfachauswahl und sichtbare Fehlerkanten.
- **Inspector:** kontextbezogene Formulare, dokumentierte Inputs/Outputs, Berechtigungen und lokale Validierung.
- **Run-Konsole:** Testeingabe und Trace; Knoten erhalten Laufzeitstatus, Fehlerdetails und Laufzeitdaten.
- **Schmale Ansichten:** Canvas bleibt zentral; Palette und Inspector werden als umschaltbare Seitenpanels dargestellt, die Run-Konsole als einklappbarer Bereich. Keine drei dauerhaft zusammengedrueckten Spalten.

Draft, validiert, published und fehlerhaft muessen als unterscheidbare Zustandsanzeige auftreten. Publish zeigt die zu aktivierende Version und benoetigt eine erfolgreiche Validierung. Bearbeiten an einer publizierten Definition erzeugt einen Draft und veraendert laufende Requests nicht rueckwirkend.

## Navigationsmodell

```text
Backend
  Modelle und Runtime
  Hardware und Faehigkeiten
  Runtime-Voraussetzungen

Flow
  Uebersicht und Versionen
  Routing
  Agents
  Knowledge (RAG)
  Integrationen (MCP und Tools)
  Tests und Evaluations

Request
  Playground
  Request-Historie
  Traces und Fehler
```

Routing, Agent, RAG und Transform sind keine gleichartigen globalen Subsysteme: Routing, Agent und Transform sind Flow-Aktivitaeten; RAG ist eine Knowledge-Quelle mit Flow-Aktivitaet; MCP ist eine Integration mit kontrollierter Tool-Auswahl. Sie koennen trotzdem als gefilterte Knotenpalette oder Flow-Unteransichten erreichbar sein.

## Aktueller Stand und Zielbild

Die bestehende Seite `/flow` hat drei fest eingetragene Workflow-Namen und zeigt einen Elsa-Graph-Designer. Definitionen lassen sich ueber Esi.AI `IDataService` speichern und publizieren. Der lokale Elsa-Adapter delegiert Save/Publish an Esi.AI, weist die Elsa-Backend-Ausfuehrung aber als nicht unterstuetzt aus. Damit ist der Designer heute noch kein produktiver Agent-Runtime-Editor.

Das Zielbild oben ist ein Designvorschlag, keine Behauptung, dass Agent Loop, MCP-Ausfuehrung, RAG-Knoten, Freigaben, Evaluations oder Request-Traces bereits im Studio verdrahtet sind. Vor dem Ausbau muessen Ausfuehrungsvertrag, serverseitige Autorisierung, versionierte Aktivierung und Audit-/Trace-Modell festgelegt werden.

## Umsetzungsreihenfolge

1. Flow-Katalog, Definitionen und Draft/Published-Versionen als Esi.AI-eigene Quelle der Wahrheit fertigstellen.
2. Run-/Validate-Vertrag und Trace-Datenmodell festlegen; einen Request gegen einen Flow reproduzierbar ausfuehren.
3. Routing, Budget, Timeout und Fehlerpfade als ausfuehrbare Flow-Aktivitaeten integrieren.
4. Agent-Schleife mit begrenztem Tool-Allowlist-Vertrag und strukturierten Runs ergaenzen.
5. RAG-Quellen, MCP-Tool-Registrierung, Human Approval und Evaluations gezielt integrieren.
6. Erst anschliessend Editor-Palette und Inspector auf die tatsaechlich verfuegbaren Aktivitaeten ausrichten.

Studio-Anwendungsaktionen muessen in den bestehenden IDataService/SignalR-Pfad passen. Der OpenAI-kompatible Controller bleibt die API-Grenze fuer OpenAI-kompatible Requests; Browser-Workflows erhalten keine zusaetzlichen direkten HTTP-Endpunkte.