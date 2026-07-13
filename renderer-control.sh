#!/usr/bin/env bash
set -euo pipefail

HERE="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ACTIVE="$HERE/ClickableTransparentOverlay.dll"
ORIGINAL="$HERE/renderer-backup/ClickableTransparentOverlay.original-9.1.0.dll"
REPLACEMENT="$HERE/renderer-src/ClickableTransparentOverlay/ClickableTransparentOverlay/bin/Release/net10.0-windows/ClickableTransparentOverlay.dll"

case "${1:-status}" in
  status)
    sha256sum "$ACTIVE" "$ORIGINAL" 2>/dev/null || true
    ;;
  install)
    [[ -f "$REPLACEMENT" ]] || "$HERE/build-layered-renderer.sh"
    mkdir -p "$HERE/renderer-backup"
    [[ -f "$ORIGINAL" ]] || cp -a "$ACTIVE" "$ORIGINAL"
    cp -a "$REPLACEMENT" "$ACTIVE"
    echo "Layered-Renderer installiert."
    sha256sum "$ACTIVE"
    ;;
  restore)
    [[ -f "$ORIGINAL" ]] || { echo "Original-Backup fehlt: $ORIGINAL" >&2; exit 1; }
    cp -a "$ORIGINAL" "$ACTIVE"
    echo "Original-Renderer wiederhergestellt."
    sha256sum "$ACTIVE"
    ;;
  *)
    echo "Usage: $0 [status|install|restore]" >&2
    exit 2
    ;;
esac
