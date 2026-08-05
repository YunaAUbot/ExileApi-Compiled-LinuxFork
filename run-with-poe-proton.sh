#!/usr/bin/env bash
# Start ExileAPI in Path of Exile's existing Steam/Proton prefix.
# Start Path of Exile through Steam first, then run this script.

set -euo pipefail

HUD_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
HUD_EXE="$HUD_DIR/Loader.exe"
source "$HUD_DIR/steam-proton-env.sh"
# This affects only the HUD process. PoE, started separately by Steam, keeps DXVK.
# WineD3D is slower but may correctly composite this application's transparent D3D11 overlay.
PROTON_USE_WINED3D="${PROTON_USE_WINED3D:-1}"
## Native GLX/X11 compositor is the validated default on Plasma/XWayland.
## Set EXILEAPI_OVERLAY_BACKEND=layered or =legacy only for fallback diagnostics.
EXILEAPI_OVERLAY_BACKEND="${EXILEAPI_OVERLAY_BACKEND:-gpu-probe}"
EXILEAPI_GPU_FORCE_INPUT="${EXILEAPI_GPU_FORCE_INPUT:-0}"

PREFIX="$POE_LIBRARY/steamapps/compatdata/$APP_ID/pfx"

[[ -f "$HUD_EXE" ]] || { echo "Loader.exe nicht gefunden: $HUD_EXE" >&2; exit 1; }
[[ -d "$PREFIX" ]] || {
  echo "PoE-Proton-Prefix nicht gefunden: $PREFIX" >&2
  echo "Bitte Path of Exile einmal über Steam mit Proton starten und beenden." >&2
  exit 1
}

echo "Starte ExileAPI im Proton-Prefix von Path of Exile …"
echo "PoE muss bereits über Steam laufen."
echo "HUD renderer: $([[ "$PROTON_USE_WINED3D" == "1" ]] && echo WineD3D || echo DXVK)"
echo "Overlay backend: $EXILEAPI_OVERLAY_BACKEND"
[[ "$EXILEAPI_GPU_FORCE_INPUT" == "1" ]] && echo "Native GPU input: forced test mode"

STEAM_COMPAT_DATA_PATH="$POE_LIBRARY/steamapps/compatdata/$APP_ID" \
STEAM_COMPAT_CLIENT_INSTALL_PATH="$STEAM_ROOT" \
STEAM_COMPAT_APP_ID="$APP_ID" \
PROTON_USE_WINED3D="$PROTON_USE_WINED3D" \
EXILEAPI_OVERLAY_BACKEND="$EXILEAPI_OVERLAY_BACKEND" \
EXILEAPI_GPU_FORCE_INPUT="$EXILEAPI_GPU_FORCE_INPUT" \
"$PROTON" run "$HUD_EXE"
