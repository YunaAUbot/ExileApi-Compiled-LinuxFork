#!/usr/bin/env bash
# Start ExileAPI in Path of Exile's existing Steam/Proton prefix.
# Start Path of Exile through Steam first, then run this script.

set -euo pipefail

STEAM_ROOT="${STEAM_ROOT:-$HOME/.local/share/Steam}"
POE_LIBRARY="${POE_LIBRARY:-/mnt/nvme_games1/SteamLibrary}"
APP_ID=238960
HUD_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
HUD_EXE="$HUD_DIR/Loader.exe"
PROTON="${PROTON:-$STEAM_ROOT/compatibilitytools.d/GE-Proton10-34/proton}"
# This affects only the HUD process. PoE, started separately by Steam, keeps DXVK.
# WineD3D is slower but may correctly composite this application's transparent D3D11 overlay.
PROTON_USE_WINED3D="${PROTON_USE_WINED3D:-1}"
EXILEAPI_OVERLAY_BACKEND="${EXILEAPI_OVERLAY_BACKEND:-layered}"

PREFIX="$POE_LIBRARY/steamapps/compatdata/$APP_ID/pfx"

[[ -f "$HUD_EXE" ]] || { echo "Loader.exe nicht gefunden: $HUD_EXE" >&2; exit 1; }
[[ -x "$PROTON" ]] || { echo "Proton-Runner nicht gefunden/ausführbar: $PROTON" >&2; exit 1; }
[[ -d "$PREFIX" ]] || {
  echo "PoE-Proton-Prefix nicht gefunden: $PREFIX" >&2
  echo "Bitte Path of Exile einmal über Steam mit Proton starten und beenden." >&2
  exit 1
}

echo "Starte ExileAPI im Proton-Prefix von Path of Exile …"
echo "PoE muss bereits über Steam laufen."
echo "HUD renderer: $([[ "$PROTON_USE_WINED3D" == "1" ]] && echo WineD3D || echo DXVK)"
echo "Overlay backend: $EXILEAPI_OVERLAY_BACKEND"

STEAM_COMPAT_DATA_PATH="$POE_LIBRARY/steamapps/compatdata/$APP_ID" \
STEAM_COMPAT_CLIENT_INSTALL_PATH="$STEAM_ROOT" \
STEAM_COMPAT_APP_ID="$APP_ID" \
PROTON_USE_WINED3D="$PROTON_USE_WINED3D" \
EXILEAPI_OVERLAY_BACKEND="$EXILEAPI_OVERLAY_BACKEND" \
"$PROTON" run "$HUD_EXE"
