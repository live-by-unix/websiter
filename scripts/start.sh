#!/usr/bin/env bash
# Start a built Websiter application (run from an app directory or pass path).
set -euo pipefail
APP_DIR="${1:-.}"
cd "$APP_DIR"
dotnet run -c Release --project . 2>/dev/null || dotnet run --project .
