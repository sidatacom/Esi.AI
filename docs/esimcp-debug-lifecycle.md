# EsiMCP Debug-Lifecycle

Diese Anleitung beschreibt den verifizierten Live-Ablauf fuer `Esi.AI.Studio` mit
den virtuellen EsiMCP-Commands `readyness`, `restart` und `stop`.

## Voraussetzungen

- VS Code mit C# Dev Kit
- das Workspace-Projekt `Esi.AI`
- die installierte EsiMCP-Extension
- das Studio-Projekt `src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj`
- ein freier Port `7010`

Der normale Start erfolgt ueber C# Dev Kit. Es ist keine manuelle
`.vscode/launch.json` erforderlich.

## Command-Uebersicht

| Command | Zweck | Erwartetes Ergebnis |
| --- | --- | --- |
| `csdevkit.debug.active.session` | Aktive Debug-Session lesen | Session-ID als String oder `null` |
| `csdevkit.debug.projectDebugLaunch` | Studio-Debug-Session starten | Startbefehl wird angenommen; danach ID pruefen |
| `csdevkit.debug.check.host.readyness` | Bereitschaft des Hosts pruefen | `{ "ready": true }` |
| `csdevkit.debug.restart` | Aktive Session stoppen und neu starten | `{ "restarted": true }` |
| `csdevkit.debug.stop` | Aktive Session kontrolliert stoppen | `{ "stopped": true }` |

`readyness` ist die bestehende Schreibweise des virtuellen Commands und muss
genau so verwendet werden.

## Verifizierter Ablauf

### 1. Vorhandene Session pruefen

```json
{
  "commandId": "csdevkit.debug.active.session",
  "arguments": []
}
```

Wenn eine Session-ID zurueckkommt, zuerst `csdevkit.debug.stop` ausfuehren und
anschliessend erneut pruefen. Eine zweite Studio-Session darf nicht parallel
gestartet werden.

### 2. Studio starten

```json
{
  "commandId": "csdevkit.debug.projectDebugLaunch",
  "arguments": [
    {
      "scheme": "file",
      "authority": "",
      "path": "/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj",
      "query": "",
      "fragment": "",
      "fsPath": "/home/llm/Git/Esi.AI/src/Esi.AI/Esi.AI.Studio/Esi.AI.Studio.csproj"
    }
  ]
}
```

Danach die Session-ID lesen und die Readiness pruefen:

```json
{
  "commandId": "csdevkit.debug.active.session",
  "arguments": []
}
```

```json
{
  "commandId": "csdevkit.debug.check.host.readyness",
  "arguments": []
}
```

Der Host ist bereit, wenn `{ "ready": true }` zurueckkommt. Bei Studio ist
Port `7010` der erwartete Entwicklungsport.

### 3. Restart mit ID-Pruefung

```json
{
  "commandId": "csdevkit.debug.restart",
  "arguments": []
}
```

Nach `{ "restarted": true }` muss `active.session` erneut aufgerufen werden.
Die neue ID muss sich von der ID vor dem Restart unterscheiden. Eine von VS
Code recycelte ID wird absichtlich als Fehler behandelt. Anschliessend erneut
`check.host.readyness` aufrufen.

Optional kann zwischen Stop und Start ein exakt benannter Task aus
`tasks.json` ausgefuehrt werden:

```json
{
  "commandId": "csdevkit.debug.restart",
  "arguments": [
    { "rebuildTaskName": "build" }
  ]
}
```

Der Taskname muss exakt mit einem vorhandenen Tasknamen uebereinstimmen.

### 4. Session stoppen

```json
{
  "commandId": "csdevkit.debug.stop",
  "arguments": []
}
```

Danach muessen beide Checks erfolgreich sein:

```json
{
  "commandId": "csdevkit.debug.active.session",
  "arguments": []
}
```

Erwartet wird `null`. Zusaetzlich muss Port `7010` frei sein, zum Beispiel:

```bash
ss -ltn '( sport = :7010 )'
```

Eine Ausgabe ohne Listener-Zeile bestaetigt, dass keine Studio-Instanz mehr auf
dem Port lauscht.

## Live-Test vom 6. September 2026

Der Ablauf wurde mit EsiMCP `1.0.29` live ausgefuehrt:

1. Vor dem Start: aktive Session `null`.
2. Start erfolgreich; Session-ID `a44a6f53-8e54-4f1c-bb60-67d39837b4a1`.
3. Readiness: `{ "ready": true }`.
4. Restart: `{ "restarted": true }`.
5. Neue Session-ID `65e065bf-20de-4926-9eba-0ca3c0ff0065`; sie unterscheidet sich von der ersten ID.
6. Readiness nach Restart: `{ "ready": true }`.
7. Stop: `{ "stopped": true }`.
8. Nach dem Stop: aktive Session `null`.
9. Port `7010` war frei.

Die vollstaendige Sitzungsaufzeichnung liegt in
`docs/history/20260906-214000.md`.