namespace ClickableTransparentOverlay
{
    using ImGuiNET;
    using System;
    using Win32;

    internal class ImGuiInputHandler
    {
        readonly IntPtr hwnd;
        ImGuiMouseCursor lastCursor;
        // Once the visible renderer is the native GLX window, Wine's hidden
        // HWND no longer has a meaningful ScreenToClient mouse coordinate.
        // Retain the last native coordinate until the next X11 update instead
        // of falling back to the hidden HWND on every idle frame.
        private bool hasNativeMousePosition;
        private float nativeMouseX;
        private float nativeMouseY;
        private readonly bool[] globalMouseButtons = new bool[3];

        public ImGuiInputHandler(IntPtr hwnd)
        {
            this.hwnd = hwnd;
        }

        public bool Update(bool preserveGlobalButtons = false)
        {
            var io = ImGui.GetIO();
            UpdateMousePosition(io, hwnd);
            var mouseCursor = io.MouseDrawCursor ? ImGuiMouseCursor.None : ImGui.GetMouseCursor();
            if (mouseCursor != lastCursor)
            {
                lastCursor = mouseCursor;

                // only required if mouse icon changes
                // while mouse isn't moved otherwise redundent.
                // so practically it's redundent.
                UpdateMouseCursor(io, mouseCursor);
            }

            if (!preserveGlobalButtons && !io.WantCaptureMouse && ImGui.IsAnyMouseDown())
            {
                // workaround: where overlay gets stuck in a non-clickable mode forever.
                for (var i = 0; i < 5; i++)
                {
                    io.AddMouseButtonEvent(i, false);
                }
            }

            return io.WantCaptureMouse;
        }

        /// <summary>
        /// Read after the current ImGui frame was built. This makes the native
        /// input region follow the UI that is visible now, rather than the
        /// previous frame's capture state.
        /// </summary>
        public bool WantsMouseCapture() => ImGui.GetIO().WantCaptureMouse;

        internal bool WantsKeyboardCapture()
        {
            var io = ImGui.GetIO();
            return io.WantCaptureKeyboard || io.WantTextInput;
        }

        internal void AddNativeMouseButton(int button, bool down)
        {
            if (button >= 0 && button < 5) ImGui.GetIO().AddMouseButtonEvent(button, down);
        }

        internal void AddNativeMouseWheel(float horizontal, float vertical)
        {
            ImGui.GetIO().AddMouseWheelEvent(horizontal, vertical);
        }

        internal void AddNativeKey(uint keysym, bool down, uint codepoint)
        {
            var io = ImGui.GetIO();
            if (TryMapNativeKey(keysym, out var key)) io.AddKeyEvent(key, down);
            if (down && codepoint != 0) io.AddInputCharacter(codepoint);
        }

        internal void AddNativeMousePosition(float x, float y)
        {
            this.hasNativeMousePosition = true;
            this.nativeMouseX = x;
            this.nativeMouseY = y;
        }

        // The native GPU surface deliberately has an empty X Shape input
        // region.  Poll button edges so ImGui still observes complete
        // down/up pairs while PoE receives the original click normally.
        internal void UpdateGlobalMouseButtons()
        {
            var io = ImGui.GetIO();
            var keys = new[] { VK.LBUTTON, VK.RBUTTON, VK.MBUTTON };
            for (var button = 0; button < keys.Length; button++)
            {
                var down = (User32.GetAsyncKeyState(keys[button]) & 0x8000) != 0;
                if (down != this.globalMouseButtons[button])
                {
                    this.globalMouseButtons[button] = down;
                    io.AddMouseButtonEvent(button, down);
                }
            }
        }

        public bool ProcessMessage(WindowMessage msg, UIntPtr wParam, IntPtr lParam)
        {
            if (ImGui.GetCurrentContext() == IntPtr.Zero)
                return false;

            var io = ImGui.GetIO();
            switch (msg)
            {
                case WindowMessage.SetFocus:
                case WindowMessage.KillFocus:
                    io.AddFocusEvent(msg == WindowMessage.SetFocus);
                    break;
                case WindowMessage.LButtonDown:
                case WindowMessage.LButtonDoubleClick:
                case WindowMessage.LButtonUp:
                    io.AddMouseButtonEvent(0, msg != WindowMessage.LButtonUp);
                    break;
                case WindowMessage.RButtonDown:
                case WindowMessage.RButtonDoubleClick:
                case WindowMessage.RButtonUp:
                    io.AddMouseButtonEvent(1, msg != WindowMessage.RButtonUp);
                    break;
                case WindowMessage.MButtonDown:
                case WindowMessage.MButtonDoubleClick:
                case WindowMessage.MButtonUp:
                    io.AddMouseButtonEvent(2, msg != WindowMessage.MButtonUp);
                    break;
                case WindowMessage.XButtonDown:
                case WindowMessage.XButtonDoubleClick:
                case WindowMessage.XButtonUp:
                    io.AddMouseButtonEvent(
                        GET_XBUTTON_WPARAM(wParam) == 1 ? 3 : 4,
                        msg != WindowMessage.XButtonUp);
                    break;
                case WindowMessage.MouseWheel:
                    io.AddMouseWheelEvent(0.0f, GET_WHEEL_DELTA_WPARAM(wParam) / WHEEL_DELTA);
                    break;
                case WindowMessage.MouseHWheel:
                    io.AddMouseWheelEvent(-GET_WHEEL_DELTA_WPARAM(wParam) / WHEEL_DELTA, 0.0f);
                    break;
                case WindowMessage.KeyDown:
                case WindowMessage.SysKeyDown:
                case WindowMessage.KeyUp:
                case WindowMessage.SysKeyUp:
                    bool is_key_down = msg == WindowMessage.SysKeyDown || msg == WindowMessage.KeyDown;
                    if ((ulong)wParam < 256 && TryMapKey((VK)wParam, out ImGuiKey imguikey))
                    {
                        if (imguikey == ImGuiKey.PrintScreen && !is_key_down)
                        {
                            io.AddKeyEvent(imguikey, true);
                        }

                        io.AddKeyEvent(imguikey, is_key_down);
                    }

                    break;
                case WindowMessage.Char:
                    io.AddInputCharacterUTF16((ushort)wParam);
                    break;
                case WindowMessage.SetCursor:
                    if (Utils.Loword((int)(long)lParam) == 1)
                    {
                        var mouseCursor = io.MouseDrawCursor ? ImGuiMouseCursor.None : ImGui.GetMouseCursor();
                        lastCursor = mouseCursor;
                        if (UpdateMouseCursor(io, mouseCursor))
                        {
                            return true;
                        }
                    }

                    break;
            }

            return false;
        }

        private void UpdateMousePosition(ImGuiIOPtr io, IntPtr handleWindow)
        {
            if (this.hasNativeMousePosition)
            {
                io.AddMousePosEvent(this.nativeMouseX, this.nativeMouseY);
                return;
            }
            if (User32.GetCursorPos(out POINT pos) && User32.ScreenToClient(handleWindow, ref pos))
            {
                io.AddMousePosEvent(pos.X, pos.Y);
            }
        }

        private static bool UpdateMouseCursor(ImGuiIOPtr io, ImGuiMouseCursor requestedcursor)
        {
            if ((io.ConfigFlags & ImGuiConfigFlags.NoMouseCursorChange) != 0)
                return false;

            if (requestedcursor == ImGuiMouseCursor.None)
            {
                User32.SetCursor(IntPtr.Zero);
            }
            else
            {
                var cursor = SystemCursor.IDC_ARROW;
                switch (requestedcursor)
                {
                    case ImGuiMouseCursor.Arrow: cursor = SystemCursor.IDC_ARROW; break;
                    case ImGuiMouseCursor.TextInput: cursor = SystemCursor.IDC_IBEAM; break;
                    case ImGuiMouseCursor.ResizeAll: cursor = SystemCursor.IDC_SIZEALL; break;
                    case ImGuiMouseCursor.ResizeEW: cursor = SystemCursor.IDC_SIZEWE; break;
                    case ImGuiMouseCursor.ResizeNS: cursor = SystemCursor.IDC_SIZENS; break;
                    case ImGuiMouseCursor.ResizeNESW: cursor = SystemCursor.IDC_SIZENESW; break;
                    case ImGuiMouseCursor.ResizeNWSE: cursor = SystemCursor.IDC_SIZENWSE; break;
                    case ImGuiMouseCursor.Hand: cursor = SystemCursor.IDC_HAND; break;
                    case ImGuiMouseCursor.NotAllowed: cursor = SystemCursor.IDC_NO; break;
                }

                User32.SetCursor(User32.LoadCursor(IntPtr.Zero, cursor));
            }

            return true;
        }

        private static bool TryMapKey(VK key, out ImGuiKey result)
        {
            static ImGuiKey KeyToImGuiKeyShortcut(VK keyToConvert, VK startKey1, ImGuiKey startKey2)
            {
                var changeFromStart1 = (int)keyToConvert - (int)startKey1;
                return startKey2 + changeFromStart1;
            }

            result = key switch
            {
                >= VK.F1 and <= VK.F24 => KeyToImGuiKeyShortcut(key, VK.F1, ImGuiKey.F1),
                >= VK.NUMPAD0 and <= VK.NUMPAD9 => KeyToImGuiKeyShortcut(key, VK.NUMPAD0, ImGuiKey.Keypad0),
                >= VK.KEY_A and <= VK.KEY_Z => KeyToImGuiKeyShortcut(key, VK.KEY_A, ImGuiKey.A),
                >= VK.KEY_0 and <= VK.KEY_9 => KeyToImGuiKeyShortcut(key, VK.KEY_0, ImGuiKey._0),
                VK.TAB => ImGuiKey.Tab,
                VK.LEFT => ImGuiKey.LeftArrow,
                VK.RIGHT => ImGuiKey.RightArrow,
                VK.UP => ImGuiKey.UpArrow,
                VK.DOWN => ImGuiKey.DownArrow,
                VK.PRIOR => ImGuiKey.PageUp,
                VK.NEXT => ImGuiKey.PageDown,
                VK.HOME => ImGuiKey.Home,
                VK.END => ImGuiKey.End,
                VK.INSERT => ImGuiKey.Insert,
                VK.DELETE => ImGuiKey.Delete,
                VK.BACK => ImGuiKey.Backspace,
                VK.SPACE => ImGuiKey.Space,
                VK.RETURN => ImGuiKey.Enter,
                VK.ESCAPE => ImGuiKey.Escape,
                VK.OEM_7 => ImGuiKey.Apostrophe,
                VK.OEM_COMMA => ImGuiKey.Comma,
                VK.OEM_MINUS => ImGuiKey.Minus,
                VK.OEM_PERIOD => ImGuiKey.Period,
                VK.OEM_2 => ImGuiKey.Slash,
                VK.OEM_1 => ImGuiKey.Semicolon,
                VK.OEM_PLUS => ImGuiKey.Equal,
                VK.OEM_4 => ImGuiKey.LeftBracket,
                VK.OEM_5 => ImGuiKey.Backslash,
                VK.OEM_6 => ImGuiKey.RightBracket,
                VK.OEM_3 => ImGuiKey.GraveAccent,
                VK.CAPITAL => ImGuiKey.CapsLock,
                VK.SCROLL => ImGuiKey.ScrollLock,
                VK.NUMLOCK => ImGuiKey.NumLock,
                VK.SNAPSHOT => ImGuiKey.PrintScreen,
                VK.PAUSE => ImGuiKey.Pause,
                VK.DECIMAL => ImGuiKey.KeypadDecimal,
                VK.DIVIDE => ImGuiKey.KeypadDivide,
                VK.MULTIPLY => ImGuiKey.KeypadMultiply,
                VK.SUBTRACT => ImGuiKey.KeypadSubtract,
                VK.ADD => ImGuiKey.KeypadAdd,
                VK.SHIFT => ImGuiKey.ModShift,
                VK.CONTROL => ImGuiKey.ModCtrl,
                VK.MENU => ImGuiKey.ModAlt,
                VK.LSHIFT => ImGuiKey.LeftShift,
                VK.LCONTROL => ImGuiKey.LeftCtrl,
                VK.LMENU => ImGuiKey.LeftAlt,
                VK.LWIN => ImGuiKey.LeftSuper,
                VK.RSHIFT => ImGuiKey.RightShift,
                VK.RCONTROL => ImGuiKey.RightCtrl,
                VK.RMENU => ImGuiKey.RightAlt,
                VK.RWIN => ImGuiKey.RightSuper,
                VK.APPS => ImGuiKey.Menu,
                VK.BROWSER_BACK => ImGuiKey.AppBack,
                VK.BROWSER_FORWARD => ImGuiKey.AppForward,
                _ => ImGuiKey.None
            };

            return result != ImGuiKey.None;
        }

        // X11 keysyms used by the native non-focusable GLX compositor.
        // Printable Latin keysyms intentionally share their Unicode values.
        private static bool TryMapNativeKey(uint key, out ImGuiKey result)
        {
            result = key switch
            {
                0xff09 => ImGuiKey.Tab,
                0xff0d => ImGuiKey.Enter,
                0xff1b => ImGuiKey.Escape,
                0xff08 => ImGuiKey.Backspace,
                0xffff => ImGuiKey.Delete,
                0xff63 => ImGuiKey.Insert,
                0xff50 => ImGuiKey.Home,
                0xff57 => ImGuiKey.End,
                0xff55 => ImGuiKey.PageUp,
                0xff56 => ImGuiKey.PageDown,
                0xff51 => ImGuiKey.LeftArrow,
                0xff52 => ImGuiKey.UpArrow,
                0xff53 => ImGuiKey.RightArrow,
                0xff54 => ImGuiKey.DownArrow,
                0xffe1 or 0xffe2 => ImGuiKey.ModShift,
                0xffe3 or 0xffe4 => ImGuiKey.ModCtrl,
                0xffe9 or 0xffea => ImGuiKey.ModAlt,
                0xffeb or 0xffec => ImGuiKey.ModSuper,
                0xffbe => ImGuiKey.F1,
                0xffbf => ImGuiKey.F2,
                0xffc0 => ImGuiKey.F3,
                0xffc1 => ImGuiKey.F4,
                0xffc2 => ImGuiKey.F5,
                0xffc3 => ImGuiKey.F6,
                0xffc4 => ImGuiKey.F7,
                0xffc5 => ImGuiKey.F8,
                0xffc6 => ImGuiKey.F9,
                0xffc7 => ImGuiKey.F10,
                0xffc8 => ImGuiKey.F11,
                0xffc9 => ImGuiKey.F12,
                _ => ImGuiKey.None,
            };
            if (result != ImGuiKey.None) return true;
            if (key >= 'a' && key <= 'z') result = ImGuiKey.A + (int)(key - 'a');
            else if (key >= 'A' && key <= 'Z') result = ImGuiKey.A + (int)(key - 'A');
            else if (key >= '0' && key <= '9') result = ImGuiKey._0 + (int)(key - '0');
            else if (key == ' ') result = ImGuiKey.Space;
            else result = ImGuiKey.None;
            return result != ImGuiKey.None;
        }

        private static readonly float WHEEL_DELTA = 120;

        private static int GET_WHEEL_DELTA_WPARAM(UIntPtr wParam) => Utils.Hiword((int)wParam);

        private static int GET_XBUTTON_WPARAM(UIntPtr wParam) => Utils.Hiword((int)wParam);
    }
}
