#!/usr/bin/env bash
# Environment diagnostics for Websiter development.
set -uo pipefail
cd "$(dirname "$0")/.."
echo "== Websiter doctor =="
command -v dotnet >/dev/null && echo "dotnet: $(dotnet --version)" || echo "dotnet: MISSING"
command -v websiter >/dev/null && echo "websiter CLI: $(websiter --version 2>/dev/null | head -1)" || echo "websiter CLI: not installed"
uname -a | sed 's/^/os: /'
echo "shell: $SHELL"
[ -d .git ] && echo "git: repo" || echo "git: not a repo (or .git missing)"
