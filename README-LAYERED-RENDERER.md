# Wine/Proton layered renderer

This tree replaces `ClickableTransparentOverlay.dll` 9.1.0 with an ABI-compatible
renderer for ExileAPI on Wine/Proton. It does not inject into Path of Exile and
does not modify game files.

## Architecture

ExileCore and plugins still build normal ImGui draw lists and use the existing
Vortice D3D11 texture handles. ImGui is rendered into an off-screen
`B8G8R8A8_UNorm` texture with premultiplied alpha. A staging readback copies the
frame into a persistent top-down 32-bit DIB, which is presented with
`UpdateLayeredWindow(ULW_ALPHA)`. The transparent window is therefore not based
on DWM or on a transparent D3D swap chain.

`WS_EX_TRANSPARENT` is enabled when ImGui does not want the mouse and removed
when interactive UI is hovered. The HWND, its initialization and its message
pump stay on one dedicated thread; this is required by Wine when ExileCore's
`PostInitialized` callback is asynchronous.

Fully transparent ImGui fragments are discarded before blending. This avoids
unnecessary render-target writes for sparse alpha textures such as Radar's
recolored walkable-map texture, without any plugin-specific behavior.

When a PoE window is present, the overlay is made its owned window. This keeps
the overlay above KDE/XWayland fullscreen PoE without resizing it to the
primary monitor. The overlay always retains the client rectangle provided by
ExileCore, including on multi-monitor desktops.

The backend is selected with `EXILEAPI_OVERLAY_BACKEND`:

- `layered`: require the new renderer and surface errors immediately.
- `auto`: try layered rendering and switch to the old swap-chain backend if
  layered initialization or presentation fails.
- `legacy`: use the old transparent D3D11 swap-chain implementation.

## Build and installation

The build uses the Windows .NET 10 SDK already installed in PoE's Proton prefix;
no Linux system package or setting is changed.

```bash
cd /home/USER/ExileApi-Compiled-LinuxFork
./build-layered-renderer.sh
./renderer-control.sh install
```

Start PoE through Steam first, then start ExileAPI:

```bash
./run-with-poe-proton.sh
```

WineD3D is the default and performed better in the 3440x1044 validation. DXVK is
also functional and can be tested explicitly:

```bash
PROTON_USE_WINED3D=0 ./run-with-poe-proton.sh
```

For a strict failure instead of automatic fallback:

```bash
EXILEAPI_OVERLAY_BACKEND=layered ./run-with-poe-proton.sh
```

## Fallback and inspection

The untouched 9.1.0 DLL is stored at
`renderer-backup/ClickableTransparentOverlay.original-9.1.0.dll`.

```bash
./renderer-control.sh status
./renderer-control.sh restore   # restore upstream/original DLL
./renderer-control.sh install   # reinstall the layered DLL
```

Runtime diagnostics and average frame rate are appended to
`ClickableTransparentOverlay.renderer.log`. Existing ExileAPI logs remain in
`Logs/`.

## Validation performed

- Reflection/metadata comparison against the original DLL and ExileCore member
  references, including assembly name/version 9.1.0.0.
- Proton test host with alpha 0, alpha 0.5, alpha 1, text, animation and an
  interactive ImGui window.
- Resizes through 800x600, 960x640, 1100x720 and 3440x1440; controlled close.
- Real PoE login-screen test without LinuxOverlayBridge using WineD3D and DXVK.
- Repeated F12 open/close, interactive ImGui controls and PoE click-through.
- PoE restart confirmed that no stale Loader process or overlay window remained.
- Validation used targeted CUA captures of PoE and the overlay, including its
  transparent regions; transient screenshots and runtime logs are intentionally
  not part of the distributable repository.

The login screen cannot initialize `GameController.CurrentArea`; those logged
exceptions are unrelated to the renderer. Full in-area plugin rendering still
requires a manually logged-in character.

## Known limitation

The robust layered path reads the full overlay surface back to system memory.
At 3440x1044 the measured average was about 14 FPS with WineD3D and 8.4 FPS with
DXVK. DXVK used roughly 770 MiB RSS and 112 MiB GPU memory in the measured run.
This is intentionally kept as the reliable default architecture; a future
optimization can try a Wine-compatible GDI DXGI surface or dirty rectangles
without changing the public ABI.

## Upstream synchronization

`.github/workflows/sync-upstream.yml` fetches
`exApiTools/ExileApi-Compiled:master` daily (03:17 UTC) and can also be started
from the Actions tab. It pushes an automatic merge only when Git can merge
without conflicts. This includes a rewritten upstream history: the workflow
then attempts Git's explicit content merge for unrelated histories. A conflict
fails the workflow before its `git push`, leaving this fork unchanged for a
manual resolution.
