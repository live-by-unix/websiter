#!/usr/bin/env bash
# Websiter repo setup: verify prerequisites and restore/build.
set -euo pipefail
cd "$(dirname "$0")/.."

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: dotnet SDK not found. Install .NET 10 SDK: https://dotnet.microsoft.com/download" >&2
  exit 1
fi

SDK_VERSION="$(dotnet --version)"
MAJOR="${SDK_VERSION%%.*}"
if [ "$MAJOR" -lt 10 ]; then
  echo "ERROR: .NET SDK 10+ required (found $SDK_VERSION)" >&2
  exit 1
fi

echo "==> .NET SDK $SDK_VERSION detected"
dotnet restore Websiter.slnx
dotnet build Websiter.slnx --nologo -v q
echo "==> Setup complete. Run 'dotnet test Websiter.slnx' to verify."
