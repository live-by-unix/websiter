#!/usr/bin/env bash
# Remove build artifacts (bin/obj) across the repo.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet clean Websiter.slnx --nologo -v q || true
find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null || true
echo "==> Cleaned build artifacts."
