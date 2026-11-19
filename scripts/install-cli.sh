#!/usr/bin/env bash
# Install the websiter CLI globally from a local build.
# Publishes to ~/.local/share/websiter and links websiter into ~/.local/bin.
set -euo pipefail
cd "$(dirname "$0")/.."

INSTALL_DIR="${WEBSITER_INSTALL_DIR:-$HOME/.local/share/websiter}"
BIN_DIR="${WEBSITER_BIN_DIR:-$HOME/.local/bin}"

dotnet publish src/Websiter.CLI -c Release -o "$INSTALL_DIR" --nologo
mkdir -p "$BIN_DIR"
ln -sf "$INSTALL_DIR/websiter" "$BIN_DIR/websiter"
chmod +x "$INSTALL_DIR/websiter" 2>/dev/null || true

case ":$PATH:" in
  *":$BIN_DIR:"*) ;;
  *) echo "NOTE: $BIN_DIR is not on your PATH. Add it with:" >&2
     echo "  export PATH=\"$BIN_DIR:\$PATH\"" >&2 ;;
esac

echo "==> Installed. Run: websiter --version"
