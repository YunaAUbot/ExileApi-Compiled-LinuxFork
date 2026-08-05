#!/usr/bin/env bash
# Shared Steam/Proton discovery for the launcher and renderer build.
# Explicit environment variables always take precedence:
# STEAM_ROOT, POE_LIBRARY, PROTON and EXILEAPI_APP_ID.

EXILEAPI_APP_ID="${EXILEAPI_APP_ID:-238960}"

if [[ -z "${STEAM_ROOT:-}" ]]; then
  for candidate in "$HOME/.local/share/Steam" "$HOME/.steam/steam"; do
    if [[ -d "$candidate/steamapps" ]]; then
      STEAM_ROOT="$candidate"
      break
    fi
  done
fi

[[ -n "${STEAM_ROOT:-}" && -d "$STEAM_ROOT/steamapps" ]] || {
  echo "Steam installation not found. Set STEAM_ROOT to the folder containing steamapps/." >&2
  return 1 2>/dev/null || exit 1
}

if [[ -z "${POE_LIBRARY:-}" ]]; then
  libraries=("$STEAM_ROOT")
  for file in "$STEAM_ROOT/steamapps/libraryfolders.vdf" "$HOME/.steam/steam/steamapps/libraryfolders.vdf"; do
    [[ -f "$file" ]] || continue
    while IFS= read -r library; do
      [[ -n "$library" ]] && libraries+=("${library//\\\\/\\}")
    done < <(sed -nE 's/.*"path"[[:space:]]*"([^"]+)".*/\1/p' "$file")
  done

  for library in "${libraries[@]}"; do
    if [[ -d "$library/steamapps/compatdata/$EXILEAPI_APP_ID" ]] ||
       [[ -d "$library/steamapps/common/Path of Exile" ]]; then
      POE_LIBRARY="$library"
      break
    fi
  done
fi

[[ -n "${POE_LIBRARY:-}" && -d "$POE_LIBRARY/steamapps" ]] || {
  echo "Path of Exile Steam library not found. Set POE_LIBRARY to the folder containing steamapps/." >&2
  return 1 2>/dev/null || exit 1
}

if [[ -z "${PROTON:-}" ]]; then
  mapfile -t proton_candidates < <(
    find "$STEAM_ROOT/compatibilitytools.d" \
         "$STEAM_ROOT/steamapps/common" \
         "$POE_LIBRARY/steamapps/common" \
         -maxdepth 3 -type f -name proton -perm -u+x 2>/dev/null | sort -V
  )
  # Steam stores the selected compatibility tool name as the first line of
  # config_info. Prefer that exact runner so a system-wide Proton Hotfix does
  # not accidentally replace a game's configured GE-Proton version.
  config_info="$POE_LIBRARY/steamapps/compatdata/$EXILEAPI_APP_ID/config_info"
  if [[ -f "$config_info" ]]; then
    IFS= read -r configured_tool < "$config_info" || true
    for candidate in "${proton_candidates[@]}"; do
      if [[ "$(basename "$(dirname "$candidate")")" == "$configured_tool" ]]; then
        PROTON="$candidate"
        break
      fi
    done
  fi
  if (( ${#proton_candidates[@]} )); then
    PROTON="${PROTON:-${proton_candidates[${#proton_candidates[@]} - 1]}}"
  fi
fi

[[ -n "${PROTON:-}" && -x "$PROTON" ]] || {
  echo "Proton runner not found. Set PROTON to the executable proton runner." >&2
  return 1 2>/dev/null || exit 1
}

APP_ID="$EXILEAPI_APP_ID"
export STEAM_ROOT POE_LIBRARY PROTON APP_ID EXILEAPI_APP_ID
