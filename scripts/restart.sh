#!/usr/bin/env bash
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
"$DIR/stop.sh" "${1:-.}" || true
"$DIR/start.sh" "${1:-.}"
