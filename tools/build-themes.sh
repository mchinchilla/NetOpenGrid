#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
THEMES_DIR="$ROOT/themes"
OUT="$ROOT/src/NetOpenGrid.Infrastructure/Assets/css"

STRICT=0
for arg in "$@"; do
  case "$arg" in
    --strict) STRICT=1 ;;
    *) echo "[netopengrid] ERROR: unknown argument '$arg'" >&2; exit 2 ;;
  esac
done

mkdir -p "$OUT"

if ! command -v tailwindcss >/dev/null 2>&1; then
  if [ "$STRICT" -eq 1 ]; then
    echo "[netopengrid] ERROR: 'tailwindcss' CLI not found in PATH. Refusing to package without freshly compiled themes." >&2
    exit 1
  fi
  echo "[netopengrid] WARNING: 'tailwindcss' CLI not found in PATH; keeping the committed theme CSS." >&2
  exit 0
fi

rm -f "$OUT"/netopengrid-*.css

for theme in "$THEMES_DIR"/*.css; do
  name="$(basename "$theme" .css)"
  echo "[netopengrid] Compiling theme '$name'..."
  tailwindcss -i "$theme" -o "$OUT/netopengrid-$name.css" --minify --silent
done

echo "[netopengrid] Themes written to: $OUT"
