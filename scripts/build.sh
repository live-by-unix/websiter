#!/usr/bin/env bash
# Build Websiter (Debug by default; WEBSTER_CONFIG=Release for release).
set -euo pipefail
cd "$(dirname "$0")/.."
CONFIG="${1:-Debug}"
dotnet build Websiter.slnx -c "$CONFIG" --nologo
