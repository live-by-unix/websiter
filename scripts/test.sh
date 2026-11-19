#!/usr/bin/env bash
# Run the full Websiter test suite.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet test Websiter.slnx --nologo
