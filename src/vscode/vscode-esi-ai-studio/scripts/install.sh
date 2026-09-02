#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
extension_dir="$(cd -- "$script_dir/.." && pwd)"
extension_id="sidatacom.vscode-esi-ai-studio"
code_command="${CODE_BIN:-code}"

if [[ -d "$HOME/.local/node/bin" ]]; then
  PATH="$HOME/.local/node/bin:$PATH"
  export PATH
fi

if ! command -v node >/dev/null 2>&1; then
  printf '%s\n' 'Node.js is required to install Esi.AI Studio Models.' >&2
  exit 1
fi

if ! command -v npm >/dev/null 2>&1; then
  printf '%s\n' 'npm is required to install Esi.AI Studio Models.' >&2
  exit 1
fi

if ! command -v "$code_command" >/dev/null 2>&1; then
  printf 'VS Code CLI not found: %s\n' "$code_command" >&2
  printf '%s\n' 'Set CODE_BIN to the VS Code executable or add code to PATH.' >&2
  exit 1
fi

cd "$extension_dir"
package_name="$(node -p "require('./package.json').name")"
version="$(node -p "require('./package.json').version")"
vsix_path="$extension_dir/${package_name}-${version}.vsix"

npm install --no-audit --no-fund
npm run build
npx --no-install vsce package --allow-missing-repository --skip-license --out "$vsix_path"
"$code_command" --uninstall-extension "$extension_id" >/dev/null 2>&1 || true
"$code_command" --install-extension "$vsix_path" --force

if ! "$code_command" --list-extensions | grep -Fxq "$extension_id"; then
  printf 'VS Code did not report the installed extension: %s\n' "$extension_id" >&2
  exit 1
fi

printf 'Installed %s %s using %s\n' "$extension_id" "$version" "$code_command"
printf 'Reload VS Code, start Esi.AI Studio, load a model, and run "Esi AI Studio: Refresh Models".\n'