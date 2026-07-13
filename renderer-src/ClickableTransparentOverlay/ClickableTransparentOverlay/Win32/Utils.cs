namespace ClickableTransparentOverlay.Win32
{
    using System;
    using System.Diagnostics;

    public static class Utils
    {
        public static int Loword(int number) => number & 0x0000FFFF;
        public static int Hiword(int number) => number >> 16;

        /// <summary>
        /// Gets a value indicating whether the overlay is clickable or not.
        /// </summary>
        internal static bool IsClickable { get; private set; } = true;

        private static WindowExStyles Clickable = 0;
        private static WindowExStyles NotClickable = 0;

        private static readonly Stopwatch sw = Stopwatch.StartNew();
        private static readonly long[] nVirtKeyTimeouts = new long[256]; // Total VirtKeys are 256.

        /// <summary>
        /// Returns true if the key is pressed.
        /// For keycode information visit: https://www.pinvoke.net/default.aspx/user32.getkeystate.
        ///
        /// This function can return True multiple times (in multiple calls) per keypress. It
        /// depends on how long the application user pressed the key for and how many times
        /// caller called this function while the key was pressed. Caller of this function is
        /// responsible to mitigate this behaviour.
        /// </summary>
        /// <param name="nVirtKey">key code to look.</param>
        /// <returns>weather the key is pressed or not.</returns>
        public static bool IsKeyPressed(VK nVirtKey)
        {
            return Convert.ToBoolean(User32.GetKeyState(nVirtKey) & 0x8000);
        }

        /// <summary>
        /// A wrapper function around <see cref="IsKeyPressed"/> to ensure a single key-press
        /// yield single true even if the function is called multiple times.
        ///
        /// This function might miss a key-press, which may degrade the user-experience,
        /// so use this function to the minimum e.g. just to enable/disable/show/hide the overlay.
        /// And, it would be nice to allow application user to configure the timeout value to
        /// their liking.
        /// </summary>
        /// <param name="nVirtKey">key to look for, for details read <see cref="IsKeyPressed"/> description.</param>
        /// <param name="timeout">timeout in milliseconds</param>
        /// <returns>true if the key is pressed and key is not in timeout.</returns>
        public static bool IsKeyPressedAndNotTimeout(VK nVirtKey, int timeout = 200)
        {
            var actual = IsKeyPressed(nVirtKey);
            var currTime = sw.ElapsedMilliseconds;
            if (actual && currTime > nVirtKeyTimeouts[(int)nVirtKey])
            {
                nVirtKeyTimeouts[(int)nVirtKey] = currTime + timeout;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Allows the window to become transparent.
        /// </summary>
        /// <param name="handle">
        /// Window native pointer.
        /// </param>
        internal static void InitTransparency(IntPtr handle)
        {
            Clickable = (WindowExStyles)User32.GetWindowLong(handle, (int)WindowLongParam.GWL_EXSTYLE);
            NotClickable = Clickable | WindowExStyles.WS_EX_LAYERED | WindowExStyles.WS_EX_TRANSPARENT;
            var margins = new Dwmapi.Margins(-1);
            _ = Dwmapi.DwmExtendFrameIntoClientArea(handle, ref margins);
            SetOverlayClickable(handle, true);
        }

        internal static void InitLayeredInput(IntPtr handle)
        {
            var before = User32.GetWindowLong(handle, (int)WindowLongParam.GWL_EXSTYLE);
            Clickable = (WindowExStyles)before | WindowExStyles.WS_EX_LAYERED;
            NotClickable = Clickable | WindowExStyles.WS_EX_TRANSPARENT;
            User32.SetWindowLong(handle, (int)WindowLongParam.GWL_EXSTYLE, (uint)Clickable);
            IsClickable = true;
            EnsureTopmost(handle);
        }

        /// <summary>
        /// Enables (clickable) / Disables (not clickable) the Window keyboard/mouse inputs.
        /// NOTE: This function depends on InitTransparency being called when the Window was created.
        /// </summary>
        /// <param name="handle">Veldrid window handle in IntPtr format.</param>
        /// <param name="WantClickable">Set to true if you want to make the window clickable otherwise false.</param>
        internal static void SetOverlayClickable(IntPtr handle, bool WantClickable)
        {
            if (IsClickable ^ WantClickable)
            {
                if (WantClickable)
                {
                    User32.SetWindowLong(handle, (int)WindowLongParam.GWL_EXSTYLE, (uint)Clickable);
                }
                else
                {
                    User32.SetWindowLong(handle, (int)WindowLongParam.GWL_EXSTYLE, (uint)NotClickable);
                }

                IsClickable = WantClickable;
                EnsureTopmost(handle);
            }
        }

        internal static void EnsureTopmost(IntPtr handle)
        {
            // Reassert z-order without activating the overlay. Wine/XWayland can
            // place a layered window behind a newly focused fullscreen window
            // even when WS_EX_TOPMOST remains set.
            //
            // Do not supply a monitor-sized rectangle here. On a multi-monitor
            // Wine desktop GetSystemMetrics returns the primary monitor, while
            // ExileCore maintains the PoE client rectangle. Repeatedly applying
            // that size causes WM_SIZE to oscillate between the two rectangles.
            const uint SWP_NOSIZE = 0x0001;
            const uint SWP_NOMOVE = 0x0002;
            const uint SWP_NOACTIVATE = 0x0010;
            const uint SWP_SHOWWINDOW = 0x0040;
            User32.SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
    }
}
