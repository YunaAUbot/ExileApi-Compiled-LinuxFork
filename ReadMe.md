Compiled ExileAPI
==================

## Linux / Proton

This fork supplies a Wine/Proton-compatible replacement for
`ClickableTransparentOverlay.dll`. It renders through a layered Win32 window,
so transparent regions expose the PoE client instead of becoming black. It
needs neither DLL injection nor LinuxOverlayBridge. See
[README-LAYERED-RENDERER.md](README-LAYERED-RENDERER.md) for installation,
build, fallback and validation instructions.

~~It is highly recommended that you use the [limited user method explained here](https://www.ownedcore.com/forums/mmo/path-of-exile/poe-bots-programs/676345-run-poe-limited-user.html).~~

#### I dont know how I would do the equivalent of the limited user method on Linux so use at own risk.

This reads the memory of the Path of Exile client application and displays it on transparent overlay.

### Keyboard Info

* Press F12 to show / hide the Menu

### Original Exile API
Refer to original [ExileApi-Compiled](https://github.com/exApiTools/ExileApi-Compiled) for more Information. I just did the Linux port and am not affiliated with them in any form. 
