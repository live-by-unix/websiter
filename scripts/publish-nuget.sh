#!/usr/bin/env bash
# Publish Websiter packages to nuget.org. Requires NUGET_API_KEY env var.
set -euo pipefail
cd "$(dirname "$0")/.."
if [ -z "${NUGET_API_KEY:-}" ]; then
  echo "ERROR: Set NUGET_API_KEY to your nuget.org API key." >&2
  exit 1
fi
dotnet pack src/Websiter.CLI/Websiter.CLI.csproj -c Release -o artifacts --nologo
dotnet nuget push "artifacts/Websiter.CLI."*.nupkg --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json
