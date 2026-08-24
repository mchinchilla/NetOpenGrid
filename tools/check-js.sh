#!/usr/bin/env bash
# Syntax-checks the embedded client runtime via JavaScriptCore (macOS).
# Usage: ./tools/check-js.sh [path-to-netopengrid.js]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
JS="${1:-$ROOT/src/NetOpenGrid.Infrastructure/Assets/netopengrid.js}"

osascript -l JavaScript -e '
ObjC.import("Foundation");
const path = "'"$JS"'";
const content = $.NSString.stringWithContentsOfFileEncodingError(path, $.NSUTF8StringEncoding, null).js;
try {
  new Function(content);
  "SYNTAX OK";
} catch (e) {
  "SYNTAX ERROR: " + e.message;
}
'
