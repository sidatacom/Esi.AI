# EsiMCP `vscode_debug`-Pfad

Diese Seite beschreibt die native `vscode_debug_*`-MCP-Schnittstelle von EsiMCP, einschliesslich Projekt-/Konfigurationsstart, Buildprotokollen, .NET Watch und explizitem Rebuild.

## Warum der Pfad wieder implementiert wurde

Ein MCP-Agent kann einen interaktiven VS-Code-Dialog nicht selbst bedienen, wenn ein Command auf eine Benutzerauswahl wartet. EsiMCP startet daher Debugkonfigurationen ueber die oeffentliche VS-Code-Debug-API und verwendet keine privaten Commands, Broker oder dynamischen Projektmodelle anderer Erweiterungen. Die standalone C#-Erweiterung darf den `coreclr`-Adapter bereitstellen.

`debug.hotReload` startet im `watch`-Modus einen sichtbaren VS-Code-Task mit `dotnet watch`. Der .NET SDK Watcher baut beim Start und spielt unterstuetzte Edits in die laufende App ein; nur nicht unterstuetzte Edits loesen einen App-Neustart aus. `stopWatch` beendet den Task. Diese Runtime-Watch-Session hat keinen angehaengten VS-Code-Debugger und verwendet keine C#-Dev-Kit-Commands.

`rebuild` bleibt ein ausdruecklicher vollstaendiger Build und Debugger-Neustart fuer Aenderungen, die einen Neustart erfordern. Der Pfad garantiert nicht, dass andere Erweiterungen niemals eigene UI anzeigen.

## Architektur und Struktur

Der Pfad ist eine zusaetzliche MCP-Namensflaeche. Er ersetzt weder die C#-Dev-Kit-Tools noch dupliziert er deren Debugger-Implementierung. Mit der Command-Freigabeliste (englisch: Allowlist) ist keine Nutzereinstellung gemeint: `DEBUG_TOOLS` ist ein fest im EsiMCP-Code definiertes Array der aktuell 19 unterstuetzten Commands. Der Wrapper akzeptiert nur IDs aus diesem Katalog und weist beliebige oder unbekannte VS-Code-Command-IDs ab. `vscode_debug_list_commands` gibt den Katalog zur Laufzeit aus.

```text
MCP-Client
  -> vscode_debug_list_commands | vscode_debug_execute_command
  -> MCP-Tool-Registry in src/mcp/server.ts
  -> src/mcp/tools/vscode-debug.ts
  -> DEBUG_TOOLS-Allowlist in src/mcp/tools/debug.ts
  -> DebugManager und bei Readiness Terminal SessionManager
  -> VS Code Debug-, Tasks- und Workspace-APIs
```

| Bereich | Verantwortung |
| --- | --- |
| `src/mcp/server.ts` | Registriert `VSCODE_DEBUG_TOOLS` neben Terminal- und Access-Tools. |
| `src/mcp/tools/vscode-debug.ts` | Definiert die zwei `vscode_debug_*`-Wrapper, stellt den Command-Katalog bereit und leitet nur freigegebene IDs an vorhandene Handler weiter. |
| `src/mcp/tools/debug.ts` | Besitzt die gemeinsame `DEBUG_TOOLS`-Allowlist, Zod-Argumentschemas und Handler fuer Debug-Operationen. |
| `src/mcp/tools/schemas.ts` | Validiert MCP-Eingaben und erzeugt JSON-Schemas fuer den Tool-Katalog. |
| `src/debug/manager.ts` | Kontrolliert Debug-Sessions, Watch-Task-Lebenszyklus, begrenzte Buildprozesse und die VS-Code Debug-/Task-APIs. |
| `test/unit/mcp-tools.test.ts` | Prueft Toolregistrierung, strukturierte Buildfehler und Debug-Dispatch. |

`vscode_debug_execute_command` ruft keine beliebige VS-Code-Command-ID auf. Es sucht `commandId` ausschliesslich in `DEBUG_TOOLS`; unbekannte IDs werden abgewiesen. Der Katalog ist die Laufzeitquelle fuer die unterstuetzten Operationen.

## MCP-Aufrufvertrag

Die Toolnamen sind `vscode_debug_list_commands` und `vscode_debug_execute_command`. Der Ausfuehrungs-Wrapper erwartet eine Command-ID und optionale Argumente als einzelnes Objekt:

```json
{
  "commandId": "debug.restart",
  "arguments": {
    "rebuildTaskName": "build"
  }
}
```

Bei Commands ohne Parameter kann `arguments` entfallen oder als `{}` uebergeben werden. Anders als beim C#-Dev-Kit-Wrapper ist `arguments` hier **kein positionsbasiertes Array**. Die Eingabe wird gegen das Schema des ausgewaehlten Debug-Commands validiert.

`vscode_debug_list_commands` liefert fuer jedes freigegebene Command `command`, `title`, `description`, `argumentsSchema` und `registered`. Die Liste ist die Laufzeitquelle fuer gueltige IDs und Eingaben; sie wird aus `DEBUG_TOOLS` aufgebaut und nicht separat gepflegt.

## Verfuegbare Commands

Alle folgenden IDs werden als `commandId` an `vscode_debug_execute_command` uebergeben.

| Command | Argumente | Verhalten |
| --- | --- | --- |
| `debug.active.session` | keine | Gibt die ID der aktiven Debug-Session oder `null` zurueck. |
| `debug.settings` | `setting` | Liest einen vollqualifizierten Workspace-Setting-Namen; Wildcards sind nicht erlaubt. |
| `debug.launchProject` | `projectFile`, optional `workspaceFolder`, `targetFramework`, `configuration` | Stoppt aktive Debugsessions, baut ein `.csproj`, loest `TargetPath` auf und startet diesen mit `coreclr`. |
| `debug.launchFile` | `configurationName`, optional `workspaceFolder`, `projectFile` | Findet eine eindeutige `launch.json`-Konfiguration, baut ueber `projectFile` oder einen `process`-`preLaunchTask`, und startet sie. |
| `debug.hotReload` | `mode`, optional `projectFile`, `configurationName`, Workspace-/Buildoptionen | `watch` startet `dotnet watch` fuer einen `.csproj`; `stopWatch` beendet den Task; `rebuild` fuehrt einen ausdruecklichen Build und Debugger-Neustart aus. |
| `debug.check.host.readyness` | optional `sessionId` | Wartet auf die konfigurierte Host-Readiness ueber die vorhandene Session-/Terminal-Logik und meldet `{ "ready": true/false }`. Die historische Schreibweise `readyness` ist Teil der ID. |
| `debug.wait.for.event` | optional `timeoutMs`, `type` | Wartet auf ein Debug-Ereignis: `paused`, `continued` oder `terminated`. Standard-Timeout ist 30 Sekunden; zulaessig sind 100 bis 120000 ms. |
| `debug.stop` | keine | Stoppt die aktive Session und setzt die gespeicherte Host-Readiness zurueck. |
| `debug.step.over` | keine | Fuehrt Step Over aus. |
| `debug.step.into` | keine | Fuehrt Step Into aus. |
| `debug.step.out` | keine | Fuehrt Step Out aus. |
| `debug.continue` | keine | Setzt die pausierte Ausfuehrung fort. |
| `debug.pause` | keine | Pausiert die aktive Ausfuehrung. |
| `debug.restart` | optional `rebuildTaskName` | Stoppt die aktive Session, fuehrt optional einen exakt benannten Task aus und startet dieselbe Debug-Konfiguration neu. |
| `debug.add.breakpoint` | `fileFullPath`, `line`, optional `condition` | Setzt einen Source-Breakpoint; die Antwort enthaelt den Bindungsstatus des Adapters. |
| `debug.add.logpoint` | `fileFullPath`, `line`, `logMessage`, optional `condition` | Setzt einen Logpoint an einer Source-Zeile. |
| `debug.remove.breakpoint` | `fileFullPath`, `line` | Entfernt den Source-Breakpoint an der angegebenen Zeile. |
| `debug.clear.all.breakpoints` | keine | Entfernt alle Breakpoints. |
| `debug.list.breakpoints` | keine | Gibt die Source-Breakpoints zurueck. |
| `debug.list.variable.names` | optional `scope` | Listet begrenzte Variablennamen aus dem pausierten Scope `local`, `global` oder `all`; Standard ist `local`. |
| `debug.get.variables.values` | `variableNames`, optional `scope` | Liest explizit angeforderte Variablenwerte aus einem pausierten Scope. Es sind 1 bis 20 Namen erlaubt; Wildcard- und `all`-Anfragen sind gesperrt. |
| `debug.evaluate.expression` | `expression` | Wertet einen einzelnen Ausdruck im pausierten Frame aus; die Eingabe ist auf 500 Zeichen begrenzt und Wildcards sind gesperrt. |

Fuer Breakpoints muss `line` mindestens 1 sein. Bedingungen sind auf 1000 Zeichen und Log-Nachrichten auf 2000 Zeichen begrenzt. Strikte Schemas weisen zusaetzliche, nicht definierte Felder zurueck.

## Projekt- Und Konfigurationsstart

Beide Launch-Operationen stoppen alle aktiven VS-Code-Debugsessions vor dem Build. Debugger-eigene Launch-Prozesse werden mit ihrer Session beendet. Projektpfade muessen innerhalb des ausgewaehlten Workspace-Ordners liegen. Buildfehler liefern `stage: "build"`, `resultCode` und `buildLogPath`; Debugstartfehler liefern `stage: "debug"`, `resultCode` und `debugLogPath`. Die Logdateien liegen unter dem OS-Tempverzeichnis `esi-mcp/debug/`.

`debug.launchProject` baut mit `dotnet build` und liest danach `TargetPath` via MSBuild aus. Fuer Multi-Target-Projekte kann `targetFramework` gesetzt werden.

`debug.launchFile` liest `.vscode/launch.json` mit JSONC-Unterstuetzung und verlangt genau einen passenden Namen mit `request: "launch"`. Zum Erfassen des Buildoutputs muss die Konfiguration `projectFile` enthalten oder auf eine `process`-basierte `preLaunchTask` verweisen. Shelltasks und nicht aufgeloeste VS-Code-Variablen werden abgelehnt. EsiMCP entfernt `preLaunchTask` und das eigene `projectFile`-Metadatum vor dem Start, damit VS Code den Build nicht ein zweites Mal ausfuehrt.

Beispiel fuer Projektstart:

```json
{
  "commandId": "debug.launchProject",
  "arguments": {
    "projectFile": "src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj",
    "workspaceFolder": "/home/llm/Git/Esi.AI"
  }
}
```

## Live Watch

Der Watch-Modus braucht `projectFile` und beendet aktive Debug-Sessions vor dem Start. `dotnet watch` fuehrt den initialen Build aus, wendet kompatible Codeaenderungen ohne App-Neustart an und startet die App bei nicht unterstuetzten Edits automatisch neu. Der Task erscheint in einem dedizierten VS-Code-Terminal. EsiMCP beendet ihn kontrolliert vor einem normalen Debug-Launch oder beim Aufruf von `stopWatch`.

```json
{
  "commandId": "debug.hotReload",
  "arguments": {
    "mode": "watch",
    "projectFile": "src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj",
    "workspaceFolder": "/home/llm/Git/Esi.AI"
  }
}
```

## Kontrollierter Rebuild

Verwende `debug.hotReload` mit `mode: "rebuild"` und genau einem Startziel, wenn ein vollstaendiger Debugger-Neustart erforderlich ist. EsiMCP stoppt zuerst den Watch-Task und Debugsessions; nach Buildfehlern wird nicht erneut gestartet. Bei Studio danach `debug.check.host.readyness` und erst dann die gewuenschte Browserroute pruefen.

```json
{
  "commandId": "debug.hotReload",
  "arguments": {
    "mode": "rebuild",
    "projectFile": "src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj",
    "workspaceFolder": "/home/llm/Git/Esi.AI"
  }
}
```

Arbitrary detached processes werden nicht automatisch beendet; Portbelegung separat pruefen und einen stale Prozess nur nach bestaetigter Projektzugehoerigkeit stoppen.

## Bestehender Debugger-Restart

Der relevante Ablauf ist `debug.restart` mit dem exakten Namen eines Workspace-Tasks aus `tasks.json`:

```json
{
  "commandId": "debug.restart",
  "arguments": {
    "rebuildTaskName": "build"
  }
}
```

Der Ablauf in `DebugManager.restartDebugging` ist:

1. Die aktive VS-Code-Debug-Session und ihre Workspace-/Konfigurationsdaten werden gelesen. Gibt es keine aktive Session, kommt `{ "restarted": false, "message": "No active debug session" }` zurueck.
2. EsiMCP stoppt diese Session und wartet auf deren Beendigung.
3. Wenn `rebuildTaskName` angegeben ist, wird der Task mit genau diesem Namen in `vscode.tasks.fetchTasks()` gesucht und ausgefuehrt. EsiMCP wartet auf das Task-Ende. Ein nicht gefundener Task oder ein Exit-Code ungleich null wird als Fehler zurueckgegeben.
4. VS Code startet die zuvor aktive Debug-Konfiguration mit `vscode.debug.startDebugging` erneut.
5. EsiMCP wartet auf die neue Session und erkennt eine recycelte Session-ID als Fehler. Bei Erfolg kommt `{ "restarted": true }` zurueck.

Schlaegt der Task fehl, wurde die vorherige Debug-Session bereits gestoppt; der Neustart wird nicht stillschweigend ohne erfolgreichen Build fortgesetzt. Der Agent erhaelt den Fehler und kann den Zustand gezielt behandeln. Ohne `rebuildTaskName` ist es ein normaler Debugger-Restart.

## Abgrenzung zum C# Dev Kit

| EsiMCP Debug tools | Standalone C# extension |
| --- | --- |
| Projekt-/Konfigurationsauswahl, Buildlogs und Debug-Lifecycle ueber EsiMCP und oeffentliche VS-Code-APIs. | Kann den `coreclr`-Adapter bereitstellen und behandelt den Debuggerprozess. |
| Objektargumente werden gegen `DEBUG_TOOLS` validiert. | Kein C# Dev Kit und keine internen Commands oder APIs erforderlich. |
| Apply-Hot-Reload nur, wenn ein oeffentlicher Adapterrequest vorhanden ist; derzeit explizit nicht verfuegbar. | Laufzeit-/Adapterfaehigkeiten bestimmen, ob Hot Reload moeglich ist. |

`vscode_debug` ist der einzige native EsiMCP-Debugpfad. `debug.launchProject` ist der Standard fuer Projektstarts und uebernimmt das ausgewaehlte Profil aus `Properties/launchSettings.json`; `debug.launchFile` bleibt fuer ausdruecklich benoetigte benannte Konfigurationen in `.vscode/launch.json` verfuegbar. Ein separates oeffentliches `debug.start`-Werkzeug gibt es nicht.

## Weiterfuehrende Dokumente

- [EsiMCP Debug-Lifecycle](esimcp-debug-lifecycle.md): verifizierter Studio-Start-, Readiness-, Restart- und Stop-Ablauf ueber C# Dev Kit.
- [EsiMCP-README](../../../../src/vscode/vscode-esi-mcp/README.md): Paketinstallation, MCP-Tools und Einstieg.
- [EsiMCP-Implementierungsnotizen](../../../../src/vscode/vscode-esi-mcp/EsiMCP.md): Toolvertraege und Betriebsdetails des MCP-Servers.