# Linux renderer: architecture and operations

## What this fork changes

`ClickableTransparentOverlay.dll` keeps the public assembly identity and API
expected by ExileCore and existing plugins. Plugins continue to submit normal
ImGui draw lists; no plugin source changes, game-file changes or DLL injection
are required.

The default `gpu-probe` backend sends compact ImGui geometry over loopback to a
native X11/GLX helper. The helper draws into an ARGB X11 window positioned above
the real PoE XWayland window. This avoids presenting a transparent D3D swap
chain or copying a complete BGRA monitor surface on every frame.

The helper uses an X11 input shape:

- large opaque ImGui background primitives receive pointer events;
- text-only overlay labels remain click-through to PoE;
- native mouse position, buttons and wheel events are sent back to ImGui;
- the helper is non-focusable, so clicking an F12 control does not cause
  ExileCore to hide UI because PoE lost focus;
- it is an override-redirect X11 surface raised over PoE, which keeps it above
  Borderless Fullscreen on KDE/XWayland.

The implementation follows the real PoE X11 geometry for the visible surface
and synchronizes ImGui after Wine reports delayed borderless resizes.

## Requirements

- Linux desktop with X11/XWayland available; the validated setup is Arch Linux
  with KDE Plasma Wayland, NVIDIA and Proton.
- Path of Exile installed and launched with Proton at least once.
- A writable ExileAPI folder.
- For source builds only: a C compiler, `pkg-config`, X11/Xext/Xrender/OpenGL
  development packages, plus the Windows .NET SDK in the PoE Proton prefix.

`steam-proton-env.sh` automatically looks for Steam in `~/.local/share/Steam`
and `~/.steam/steam`, parses Steam's `libraryfolders.vdf` for the PoE library,
then uses the exact compatibility tool recorded for PoE by Steam. Without that
record, it selects the newest installed Proton runner it finds. Explicit values
always win, so override `POE_LIBRARY`, `STEAM_ROOT`, `PROTON` or
`EXILEAPI_APP_ID` when automatic discovery cannot identify your installation.

## Installation and start

Release ZIPs contain the installed DLL and native helper. For a source checkout
or after renderer changes:

```bash
cd /path/to/ExileApi-Compiled
./build-layered-renderer.sh
./renderer-control.sh install
```

Start PoE through Steam, then start ExileAPI:

```bash
./run-with-poe-proton.sh
```

`gpu-probe` is the default. The script prints the selected backend at startup.
Use <kbd>F12</kbd> to show or hide the ImGui menu.

## Backend choices and fallback

```bash
# Default native X11/GLX renderer
./run-with-poe-proton.sh

# Older compatibility paths, useful for comparison or diagnostics
EXILEAPI_OVERLAY_BACKEND=layered ./run-with-poe-proton.sh
EXILEAPI_OVERLAY_BACKEND=legacy ./run-with-poe-proton.sh

# Inspect, restore, or reinstall the renderer DLL
./renderer-control.sh status
./renderer-control.sh restore
./renderer-control.sh install
```

`restore` copies `renderer-backup/ClickableTransparentOverlay.original-9.1.0.dll`
back into place. It does not delete the native helpers or renderer source.

## Diagnostics

- Renderer lifecycle, geometry and average FPS: `ClickableTransparentOverlay.renderer.log`
- ExileAPI runtime logs: `Logs/`
- Native input debug trace: `/tmp/exileapi-gpu-input.log`

The native helper exits automatically if its heartbeat disappears; it should not
remain visible after ExileAPI closes. If an abnormal termination leaves it
behind, stop only the helper process, then restart ExileAPI.

## Validation status

Validated directly against PoE on the target Plasma/XWayland/Proton setup:

- transparent pixels reveal the game rather than a black frame;
- F12 menus open, hover, click, retain focus and accept wheel scrolling;
- clicks outside interactive UI pass through to PoE, including item labels;
- normal windowed and borderless-fullscreen stacking work;
- repeated menu open/close and controlled helper shutdown were tested;
- visual validation used targeted PoE XWayland captures.

## Automated maintenance

`sync-upstream.yml` checks `exApiTools/ExileApi-Compiled:master` daily at
03:17 UTC. It pushes only conflict-free updates and keeps the fork-owned
renderer DLL during upstream synchronization.

`daily-release.yml` runs daily at 03:45 UTC and can also be launched manually
from the Actions tab. If `master` has a commit not already released as
`linux-build-*`, it archives the committed distribution and publishes a GitHub
Release ZIP. Local logs, personal config and untracked plugin folders are not
included.

## Safety note

ExileAPI reads the PoE process. This fork avoids injection and game-file edits,
but it cannot establish account safety, game-rule compliance or a Linux
equivalent of Windows' restricted-user method. Use it at your own risk.
