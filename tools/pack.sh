#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SUFFIX="${1:-}"
OUT="$ROOT/artifacts"

export DOTNET_CLI_DISABLE_NODE_REUSE=1
export MSBUILDDISABLENODEREUSE=1

echo "[netopengrid] Shutting down stale build servers..."
dotnet build-server shutdown

echo "[netopengrid] Compiling themes (strict)..."
"$ROOT/tools/build-themes.sh" --strict

echo "[netopengrid] Running tests..."
dotnet test "$ROOT/NetOpenGrid.slnx" -c Release

rm -rf "$OUT"

if [ -n "$SUFFIX" ]; then
  echo "[netopengrid] Packing 0.1.0-$SUFFIX..."
  dotnet pack "$ROOT/NetOpenGrid.slnx" -c Release -o "$OUT" --version-suffix "$SUFFIX"
else
  echo "[netopengrid] Packing release version..."
  dotnet pack "$ROOT/NetOpenGrid.slnx" -c Release -o "$OUT"
fi

echo "[netopengrid] Packages in $OUT:"
ls -1 "$OUT"
