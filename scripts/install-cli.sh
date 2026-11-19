#!/usr/bin/env bash
# Install the websiter CLI globally from a local build.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet pack src/Websiter.CLI/Websiter.CLI.csproj -c Release -o artifacts --nologo
PKG_DIR="$(ls -d artifacts | head -1)"
dotnet tool install --global websiter --add-source ./artifacts || \
  dotnet tool update --global websiter --add-source ./artifacts
echo "==> Installed. Run: websiter --version"
