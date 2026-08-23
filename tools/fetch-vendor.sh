#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VENDOR="$ROOT/src/NetOpenGrid.Infrastructure/Assets/vendor"

mkdir -p "$VENDOR"

echo "[netopengrid] Downloading vendor assets to $VENDOR (embedded into the component assembly)"

curl -fsSL "https://unpkg.com/htmx.org@2/dist/htmx.min.js" -o "$VENDOR/htmx.min.js"
curl -fsSL "https://cdn.jsdelivr.net/npm/alpinejs@3/dist/cdn.min.js" -o "$VENDOR/alpine.min.js"

echo "[netopengrid] Done (rebuild NetOpenGrid.Infrastructure to re-embed):"
ls -la "$VENDOR"
