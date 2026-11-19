#!/usr/bin/env bash
# Remove the globally installed websiter CLI.
set -euo pipefail

INSTALL_DIR="${WEBSITER_INSTALL_DIR:-$HOME/.local/share/websiter}"
BIN_DIR="${WEBSITER_BIN_DIR:-$HOME/.local/bin}"

rm -f "$BIN_DIR/websiter"
dotnet tool uninstall --global websiter 2>/dev/null || true
if [ -d "$INSTALL_DIR" ]; then
  rm -rf "$INSTALL_DIR"
  echo "==> Removed $INSTALL_DIR"
else
  echo "==> No installation found at $INSTALL_DIR"
fi
