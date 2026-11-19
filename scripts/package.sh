#!/usr/bin/env bash
# Publish the websiter CLI as a NuGet package into ./artifacts.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p artifacts
dotnet pack src/Websiter.CLI/Websiter.CLI.csproj -c Release -o artifacts --nologo
echo "==> Packages written to ./artifacts"
