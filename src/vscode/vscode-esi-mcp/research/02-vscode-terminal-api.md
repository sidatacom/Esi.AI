# VSCode Terminal API - Capabilities and Limitations

## Terminal Creation
```typescript
const terminal = vscode.window.createTerminal('MyTerminal');
terminal.sendText("command", addNewLine);
terminal.show();
```

## Pseudoterminal Interface (Custom PTY)
```typescript
interface Pseudoterminal {
  open(): void;
  close(): void;
  handleInput(data: string): void;
  onDidWrite: Event<string>;        // Writes data to terminal UI
  setDimensions(cols, rows): void;
  onDidClose: Event<number | void>;
}

vscode.window.createTerminal({ pty: myPseudoterminal });
```

## Critical Limitation
The standard Terminal API **does NOT provide direct output reading** from integrated terminals.
`sendText()` sends text to the shell, but capturing output requires workarounds.

## Shell Integration API (VSCode 1.93+)
- `onDidStartTerminalShellExecution` - Event fired when a command starts
- `execution.read()` - AsyncIterable<string> with output chunks
- `execution.exitCode` - Thenable<number | undefined>
- Requires active shell integration (automatic in bash/zsh/PowerShell)

## Recommended Strategy
1. **Primary**: Shell Integration API (when available)
2. **Fallback**: Polling-based output stabilization detection
