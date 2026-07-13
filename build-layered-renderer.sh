#!/usr/bin/env bash
set -euo pipefail

HERE="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
STEAM_ROOT="${STEAM_ROOT:-$HOME/.local/share/Steam}"
POE_LIBRARY="${POE_LIBRARY:-/mnt/nvme_games1/SteamLibrary}"
APP_ID=238960
PROTON="${PROTON:-$STEAM_ROOT/compatibilitytools.d/GE-Proton10-34/proton}"
DOTNET="$POE_LIBRARY/steamapps/compatdata/$APP_ID/pfx/drive_c/Program Files/dotnet/dotnet.exe"
# Proton maps the Linux filesystem below Z:. Derive this rather than hard-code
# the checkout directory so a cloned fork can be built from any path.
WINDOWS_HERE="Z:${HERE//\//\\}"
PROJECT="$WINDOWS_HERE\\renderer-src\\ClickableTransparentOverlay\\ClickableTransparentOverlay\\ClickableTransparentOverlay.csproj"

run_dotnet() {
  STEAM_COMPAT_DATA_PATH="$POE_LIBRARY/steamapps/compatdata/$APP_ID" \
  STEAM_COMPAT_CLIENT_INSTALL_PATH="$STEAM_ROOT" \
  STEAM_COMPAT_APP_ID="$APP_ID" \
  "$PROTON" run "$DOTNET" "$@"
}

run_dotnet restore "$PROJECT"
run_dotnet build "$PROJECT" -c Release --no-restore

OUTPUT="$HERE/renderer-src/ClickableTransparentOverlay/ClickableTransparentOverlay/bin/Release/net10.0-windows/ClickableTransparentOverlay.dll"
[[ -f "$OUTPUT" ]] || { echo "Build-Ausgabe fehlt: $OUTPUT" >&2; exit 1; }
echo "Renderer gebaut: $OUTPUT"
sha256sum "$OUTPUT"
