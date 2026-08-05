# ExileAPI Linux / Proton fork

This is a Linux-focused fork of [ExileApi-Compiled](https://github.com/exApiTools/ExileApi-Compiled).
It replaces `ClickableTransparentOverlay.dll` with an ABI-compatible renderer
for Wine/Proton: transparent areas stay transparent, ImGui/F12 remains usable,
and draw-only plugin overlays keep click-through behaviour. No game files are
modified and nothing is injected into the Path of Exile process.

The default renderer is a native X11/GLX compositor designed for KDE Plasma,
XWayland and Proton. It avoids the old monitor-sized BGRA readback on every
frame.

> **Use at your own risk.** ExileAPI reads Path of Exile process memory.
> This fork does not make any claim about game rules, account safety or an
> equivalent of Windows' restricted-user setup.

## Quick start

1. Download the newest `linux-build-*` ZIP from this repository's
   [Releases](../../releases), or clone the repository.
2. Extract it somewhere writable, for example `~/ExileApi-Compiled`.
3. Start Path of Exile once through Steam with Proton. This creates its Proton
   prefix.
4. Start PoE normally through Steam and wait for the game window.
5. In a terminal, run:

   ```bash
   cd ~/ExileApi-Compiled
   ./run-with-poe-proton.sh
   ```

6. Press <kbd>F12</kbd> in PoE to open or close the ExileAPI menu.

The included script is configured for the local Steam library layout used by
this fork. If yours differs, override paths for that command:

```bash
POE_LIBRARY=/path/to/SteamLibrary \
PROTON=/path/to/your/proton \
./run-with-poe-proton.sh
```

## Everyday use

- Start PoE via Steam first, then run `./run-with-poe-proton.sh`.
- The native GPU renderer is the default; no backend flag is required.
- In the F12 menu, normal clicks and mouse-wheel scrolling work. Outside
  interactive UI, the overlay passes mouse input through to PoE.
- The renderer supports normal windowed and borderless-fullscreen XWayland
  modes on the validated Plasma setup.

## Fallback

The original `ClickableTransparentOverlay.dll` is retained locally as a safe
fallback. Check or change the active renderer with:

```bash
./renderer-control.sh status
./renderer-control.sh restore  # restore the original DLL
./renderer-control.sh install  # reinstall this fork's renderer
```

For compatibility testing, select an older renderer for one run:

```bash
EXILEAPI_OVERLAY_BACKEND=layered ./run-with-poe-proton.sh
EXILEAPI_OVERLAY_BACKEND=legacy ./run-with-poe-proton.sh
```

## Building from source

Release ZIPs already contain the compiled renderer. To rebuild after changing
source, run:

```bash
./build-layered-renderer.sh
./renderer-control.sh install
```

The build uses the .NET SDK from PoE's Proton prefix and a local C compiler;
it does not change Linux system settings.

## More information

For architecture, prerequisites, diagnostics, validation and the automated
upstream/release workflows, read [README-LAYERED-RENDERER.md](README-LAYERED-RENDERER.md).

For the original Windows-oriented project and plugin documentation, see
[exApiTools/ExileApi-Compiled](https://github.com/exApiTools/ExileApi-Compiled).
