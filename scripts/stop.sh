#!/usr/bin/env bash
# Stop a running Websiter app via its pid/state files.
set -euo pipefail
APP_DIR="${1:-.}"
STATE="$APP_DIR/.websiter"
if [ -f "$STATE/app.pid" ]; then
  PID="$(cat "$STATE/app.pid")"
  kill "$PID" 2>/dev/null && echo "Stopped app (pid $PID)" || echo "Process $PID not running"
else
  echo "No running app state found in $STATE"
  exit 1
fi
