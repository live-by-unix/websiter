#!/usr/bin/env bash
# Run `websiter dev` in the given app directory (default: current dir).
set -euo pipefail
cd "${1:-.}"
if command -v websiter >/dev/null 2>&1; then
  exec websiter dev
else
  echo "websiter CLI not found; building from source..." >&2
  cd "$(dirname "$0")/.."
  dotnet run --project src/Websiter.CLI -- dev --root "${1:-.}"
fi
