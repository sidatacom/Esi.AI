# Aufgabe: LiteLLM schrittweise nach Blazor/.NET portieren

Analysiere das bestehende Repository `Esi.AI` sowie die relevante LiteLLM-Architektur und entwickle eine modulare .NET-Implementierung der zentralen LiteLLM-Funktionalität.

## Ziel

Baue eine erweiterbare Blazor-/ASP.NET-Core-Lösung, die eine OpenAI-kompatible API bereitstellt und verschiedene LLM-Provider über ein einheitliches Provider-Adapter-Muster anspricht.

Die Implementierung soll zunächst auf die wichtigsten Kernfunktionen und fünf Provider begrenzt werden. Eine vollständige Portierung aller LiteLLM-Provider ist ausdrücklich nicht erforderlich.

## Technische Rahmenbedingungen

- Zielplattform: die im Repository bereits konfigurierte .NET-Version verwenden
- UI: Blazor
- Backend: ASP.NET Core
- JSON: `System.Text.Json`
- HTTP: `IHttpClientFactory`
- Dependency Injection für alle Provider und Services
- Streaming zunächst über Server-Sent Events (SSE)
- OpenAI-kompatible Request- und Response-Formate
- Redis für verteiltes Spend-Tracking, Caching und Rate-Limiting vorbereiten
- Alle neuen Namespaces müssen unter `Esi.AI.Llm` liegen
- Bestehende Funktionalität und uncommitted Änderungen nicht überschreiben oder zurücksetzen
- Bestehende Projektstruktur und Konventionen im Repository berücksichtigen

## Zielarchitektur

Strukturiere die Lösung modular und trenne mindestens folgende Verantwortlichkeiten:

### 1. Kernmodelle

Implementiere gemeinsame Modelle für:

- Chat-Completion-Requests
- Chat-Nachrichten
- Model-Responses
- Streaming-Chunks
- Token-Usage
- Finish-Reasons
- Modell- und Deployment-Konfiguration
- Routing-Konfiguration
- Budget- und Rate-Limit-Konfiguration
- Provider-Fehler

Die Modelle sollen möglichst kompatibel mit dem OpenAI-API-Format sein.

### 2. Provider-Abstraktionen

Definiere ein einheitliches Interface, beispielsweise:

- `IChatCompletionProvider`
- `IStreamingChatCompletionProvider`

Das Interface soll mindestens unterstützen:

- synchrones Chat-Completion-Verhalten
- Streaming
- Provider- und Modellidentifikation
- Konfiguration von API-Key und Endpoint
- strukturierte Fehlerbehandlung
- CancellationToken-Unterstützung

### 3. Provider-Implementierungen

Implementiere die Provider in dieser Reihenfolge:

1. OpenAI als Referenzimplementierung
2. Anthropic mit Streaming-Unterstützung
3. Google Gemini
4. Azure OpenAI
5. Ollama für lokale Entwicklung

Verwende für jeden Provider einen eigenen Adapter. Provider-spezifische Details dürfen nicht in den gemeinsamen Kernmodellen oder im Router verteilt werden.

### 4. Provider-Auswahl

Implementiere eine Provider-Factory oder Registry, die anhand der Modell- und Deployment-Konfiguration den passenden Provider auswählt.

Die Auswahl soll ermöglichen:

- mehrere Provider für dasselbe Modell
- konfigurierbare API-Keys und Endpoints
- einfache Erweiterung um weitere Provider
- Validierung fehlender oder ungültiger Konfiguration
- testbare Provider-Auflösung ohne echte externe API-Aufrufe

### 5. Router

Implementiere einen zentralen Router mit Deployment-Management.

Unterstütze zunächst diese Routing-Strategien:

- Round Robin
- Lowest Latency
- Lowest Cost
- Least Busy

Der Router soll:

- verfügbare Deployments verwalten
- fehlerhafte Deployments temporär deaktivieren
- Retries kontrolliert durchführen
- CancellationTokens weiterreichen
- Routing-Metriken erfassen
- Provider- und Modellkonfigurationen berücksichtigen

### 6. Kostenberechnung

Implementiere einen Cost Calculator für:

- Input-Token
- Output-Token
- unterschiedliche Modelle
- unterschiedliche Provider
- unbekannte Modelle mit klar definiertem Fallback-Verhalten

Die Preisdefinitionen sollen nicht fest im Code verteilt sein, sondern über Konfiguration oder ein erweiterbares Preisverzeichnis geladen werden.

### 7. Gateway

Stelle ASP.NET-Core-Endpunkte bereit, die mit bestehenden OpenAI-kompatiblen Clients verwendet werden können.

Mindestens erforderlich:

- `POST /v1/chat/completions`
- optional `GET /v1/models`
- synchrones Antwortformat
- SSE-Streaming für `stream: true`
- standardisierte Fehlerantworten
- API-Key- beziehungsweise Authentifizierungs-Erweiterung vorbereiten
- Cancellation bei Client-Abbruch korrekt behandeln

### 8. Redis-Integration

Bereite eine Redis-basierte Infrastruktur vor für:

- verteiltes Spend-Tracking
- Rate-Limiting
- Deployment- und Routing-Metriken
- Multi-Instance- beziehungsweise Multi-Pod-Koordination

Die Redis-Abhängigkeit soll abstrahiert werden, sodass lokale Tests ohne laufende Redis-Instanz möglich sind.

### 9. Blazor-Verwaltungsoberfläche

Erstelle eine einfache, funktionale Blazor-Verwaltungsoberfläche für:

- Provider- und Deployment-Übersicht
- Modellkonfiguration
- Routing-Strategie
- Budgetstatus
- Request- und Latenzmetriken
- Provider- beziehungsweise Deployment-Status

Die Oberfläche soll sich an den bestehenden UI-Konventionen des Projekts orientieren.

## Vorgehensweise

Arbeite schrittweise und überprüfbar:

1. Repository und bestehende Projektstruktur analysieren
2. Aktuelle .NET-Version und vorhandene Projekte ermitteln
3. Bestehende Architektur und Namenskonventionen berücksichtigen
4. Fehlende Projekte oder Module mit möglichst kleinen Änderungen ergänzen
5. Zuerst Kernmodelle und Abstraktionen implementieren
6. Danach den OpenAI-Provider als Referenz implementieren
7. Anschließend Router und OpenAI-kompatibles Gateway integrieren
8. Danach weitere Provider einzeln ergänzen
9. Redis und Blazor-Verwaltung als separate Module integrieren
10. Nach jedem größeren Schritt fokussierte Tests und Builds ausführen

## Tests

Erstelle fokussierte Tests für:

- Serialisierung und Deserialisierung der Kernmodelle
- Provider-Factory
- Provider-Konfigurationsvalidierung
- OpenAI-Provider mit Mock-HTTP-Server
- Streaming-Verarbeitung
- Router-Strategien
- Retry- und Fehlerverhalten
- Kostenberechnung
- Rate-Limiting und Budgetgrenzen
- OpenAI-kompatible Gateway-Antworten

Externe Provider dürfen in Unit-Tests nicht direkt aufgerufen werden. Verwende Mock-Handler oder Mock-Server.

## Akzeptanzkriterien

Die Implementierung gilt als erfolgreich, wenn:

- das Projekt kompiliert
- die bestehenden Tests weiterhin erfolgreich sind
- ein OpenAI-kompatibler Chat-Completion-Request verarbeitet werden kann
- synchrone Antworten funktionieren
- SSE-Streaming funktioniert
- der OpenAI-Provider als Referenz vollständig getestet ist
- Provider über eine gemeinsame Abstraktion austauschbar sind
- Router-Strategien testbar sind
- Kosten und Token-Usage verarbeitet werden
- Redis optional eingebunden werden kann
- die Architektur ohne größere Änderungen um weitere Provider erweitert werden kann
- keine neuen Root-Namespaces außerhalb von `Esi.AI.Llm` eingeführt werden

## Ergebnis

Liefere:

1. Die notwendigen Codeänderungen im Repository
2. Eine kurze Zusammenfassung der implementierten Architektur
3. Eine Liste der hinzugefügten oder geänderten Projekte und Dateien
4. Die ausgeführten Build- und Testbefehle mit Ergebnissen
5. Offene Punkte und bewusste Einschränkungen
6. Einen Vorschlag für die nächsten sinnvollen Implementierungsschritte

Beginne mit einer kurzen Bestandsaufnahme des Repositories und implementiere anschließend die kleinste funktionsfähige vertikale Scheibe: Kernmodelle, Provider-Abstraktion, OpenAI-Provider, Router und den OpenAI-kompatiblen Chat-Completion-Endpunkt.