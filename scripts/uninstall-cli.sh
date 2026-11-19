#!/usr/bin/env bash
set -euo pipefail
dotnet tool uninstall --global websiter || echo "websiter tool not installed."
