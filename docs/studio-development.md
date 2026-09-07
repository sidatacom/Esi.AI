# Esi.AI Studio: Debugging und Hot Reload

Diese Anleitung beschreibt den verbindlichen lokalen Entwicklungsablauf fuer `Esi.AI Studio` unter VS Code.

Der vollstaendige Live-Ablauf fuer die virtuellen EsiMCP-Commands `readyness`,
`restart` und `stop` steht in [EsiMCP Debug-Lifecycle](esimcp-debug-lifecycle.md).

## Voraussetzungen

- .NET SDK 10.0
- Workspace `Esi.AI` in VS Code
- freier Port `7010`
- C# Dev Kit und CoreCLR-Debugger
- fuer Client-Breakpoints: eine Development-Konfiguration mit Blazor-
  `inspectUri` und aktiviertem `UseWebAssemblyDebugging()`

Der normale C#-Dev-Kit-Workflow benoetigt keine `.vscode/launch.json` und keine
`.vscode/tasks.json`. C# Dev Kit erstellt die Debugkonfiguration dynamisch im
Speicher und ermittelt das Startprojekt aus dem Solution-/Projektmodell.

## Server starten

1. Pruefe, dass kein alter Studio-Prozess Port `7010` belegt.
2. Waehle im Solution Explorer das Serverprojekt `Esi.AI.Studio`.
3. Starte **Start New Instance** oder den C#-Dev-Kit-Befehl
  `csdevkit.debug.projectDebugLaunch`.

C# Dev Kit baut das Projektmodell selbst und startet die dynamische Debugsession.
Fuer einen Lauf ohne Debugger steht `csdevkit.debug.noDebugProjectLaunch` zur
Verfuegung. Fuer den Watch-/Blazor-Workflow kann C# Dev Kit den Projektstart mit
Hot Reload uebernehmen; ein manuell definierter `preLaunchTask` ist nicht
erforderlich.

Wenn fuer eine konkrete Blazor-UI-Aenderung `dotnet watch` verwendet wird, lautet
der effektive Watch-Befehl:

```bash
dotnet watch --non-interactive run --no-launch-profile
```

Studio ist anschliessend unter <http://localhost:7010> erreichbar.

## ASP.NET- und Blazor-Profile

`Properties/launchSettings.json` bleibt die Stelle fuer ASP.NET-/Blazor-Profile:

| Einstellung | Zweck |
| --- | --- |
| Einstellung | Zweck |
| --- | --- |
| `applicationUrl` | Lokale HTTP-/HTTPS-Listener, einschliesslich Port `7010` |
| `environmentVariables` | Development-Umgebung fuer die Anwendung |
| `inspectUri` | Microsoft-Blazor-Debugproxy fuer WebAssembly-Breakpoints |

`UseWebAssemblyDebugging()` bleibt in der Development-Pipeline erforderlich.
Eigene `launch.json`-Eintraege oder Build-Tasks werden nur angelegt, wenn eine
manuelle Sonderkonfiguration ausdruecklich benoetigt wird.

## Hot Reload

Nach dem Start sollten im integrierten Terminal mindestens diese Meldungen erscheinen:

```text
dotnet watch Polling file watcher is enabled
dotnet watch Hot reload enabled
dotnet watch ... Now listening on: http://localhost:7010
```

Bei einer laufenden Studio-Debugsession zuerst die Datei aendern und danach
`csdevkit.debug.hotReload` oder **Hot Reload** aus der Debug-Symbolleiste
verwenden. Fuer reine UI-Aenderungen bleibt die Debugsession aktiv.

Typische Hot-Reload-Aenderungen sind:

- `.razor`-Markup
- `.razor.css` und sonstige CSS-Dateien
- Blazor-Client-Markup und andere unterstuetzte Client-Aenderungen

Eine erfolgreiche Anwendung ist sichtbar, wenn `dotnet watch` eine Aenderung erkennt und die laufende Seite den neuen Zustand ohne manuellen Studio-Neustart rendert. Bei einer CSS-Aenderung kann die berechnete Browser-CSS-Groesse oder das sichtbare Layout als einfacher Test verwendet werden.

## Wann ein Neustart erforderlich ist

Ein kontrollierter Debug-Neustart ist erforderlich, wenn Hot Reload die Aenderung nicht anwenden kann, insbesondere bei:

- Projektdateien, NuGet-Paketen oder Build-Einstellungen
- neuen oder geaenderten Assembly- und Runtime-Vertraegen
- strukturellen Server-Aenderungen, die einen vollstaendigen Build benoetigen
- nativen Bibliotheken, Backend-Prozessen oder Modelldateien

Vor einem separaten Build, Rebuild oder Test des Studio-Projekts muss eine aktive Studio-Debugsession kontrolliert beendet werden. Es darf kein Build parallel zu einer laufenden Session ausgefuehrt werden. Wenn die Session weiter benoetigt wird, ist ein kontrollierter Debug-Restart zu verwenden.

## Linux: inotify und Polling

Linux kann die Anzahl gleichzeitig verfuegbarer inotify-Instanzen begrenzen. Ohne Gegenmassnahme beendet sich `dotnet watch` dann beispielsweise mit:

```text
The configured user limit (128) on the number of inotify instances has been reached
```

Darum sollte die laufende C#-Dev-Kit-/Watch-Session die Umgebungsvariable setzen:

```text
DOTNET_USE_POLLING_FILE_WATCHER=1
```

Polling verwendet mehr Dateisystemabfragen, benoetigt aber keine zusaetzlichen inotify-Instanzen und ist fuer diesen Entwicklungsworkflow stabiler. Wenn die Variable manuell gesetzt wird, muss sie vor `dotnet watch` exportiert werden:

```bash
export DOTNET_USE_POLLING_FILE_WATCHER=1
```

## Client-Breakpoints

Clientseitige WebAssembly-Breakpoints verwenden den Microsoft-Blazor-Debugproxy.
Sie setzen voraus, dass der Server bereits auf `http://localhost:7010` laeuft:

1. Das Serverprojekt mit **Start New Instance** im C# Dev Kit starten.
2. Warten, bis der Host erreichbar ist.
3. Den Browser ueber den `inspectUri`-Debugproxy verbinden.

Serverseitige Breakpoints gehoeren in die vom C# Dev Kit gestartete CoreCLR-
Session. Fuer WebAssembly-Breakpoints muessen `Properties/launchSettings.json`
und die Development-Pipeline die Microsoft-Blazor-Debugging-Voraussetzungen
erfuellen.

## Troubleshooting

### Hot Reload startet nicht

- Pruefe im Terminal `Hot reload enabled`.
- Pruefe, ob Port `7010` noch von einer alten Studio-Instanz belegt ist.
- Pruefe, ob `DOTNET_USE_POLLING_FILE_WATCHER=1` in der gestarteten Konfiguration gesetzt ist.
- Starte die Debugsession kontrolliert neu, falls ein vorheriger struktureller Build veraltete Assemblies hinterlassen hat.

### Browser zeigt weiterhin den alten Zustand

- Pruefe, ob `dotnet watch` die konkrete Datei erkennt.
- Pruefe die richtige Route, zum Beispiel `/backends`.
- Fuehre keinen separaten parallelen Build gegen dieselbe laufende Session aus.
- Bei einer nicht unterstuetzten Aenderung die Session kontrolliert neu starten.

### Prozess oder Port bleibt zurueck

Beende nur verwaiste, projektbezogene Studio-Prozesse kontrolliert und pruefe danach erneut Port `7010`. Ein zusaetzlicher Watchdog, eine PID-Datei oder eine Startblockade im Anwendungscode ist fuer diesen Ablauf nicht vorgesehen.
