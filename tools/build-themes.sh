#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
THEMES_DIR="$ROOT/themes"
OUTS=(
  "$ROOT/src/NetOpenGrid.Host/wwwroot/css"
  "$ROOT/samples/NetOpenGrid.Example/wwwroot/css"
)

for out in "${OUTS[@]}"; do
  mkdir -p "$out"
done

if ! command -v tailwindcss >/dev/null 2>&1; then
  echo "[netopengrid] WARNING: 'tailwindcss' CLI not found in PATH; skipping theme compilation." >&2
  exit 0
fi

for theme in "$THEMES_DIR"/*.css; do
  name="$(basename "$theme" .css)"
  echo "[netopengrid] Compiling theme '$name'..."
  for out in "${OUTS[@]}"; do
    tailwindcss -i "$theme" -o "$out/netopengrid-$name.css" --minify --silent
  done
done

echo "[netopengrid] Themes written to: ${OUTS[*]}"
