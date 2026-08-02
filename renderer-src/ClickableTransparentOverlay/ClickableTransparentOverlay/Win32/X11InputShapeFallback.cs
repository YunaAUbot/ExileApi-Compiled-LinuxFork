namespace ClickableTransparentOverlay.Win32
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading.Tasks;

    // Wine cannot expose libX11 directly to a managed Windows assembly. This
    // tiny companion uses the X Shape input region only; it is launched on
    // state changes and has no IPC, injection, or persistent bridge process.
    internal sealed class X11InputShapeFallback
    {
        private readonly string helper;
        private readonly object gate = new();
        private bool? appliedInteractive;
        private bool desiredInteractive;
        private Task applyTask;

        private X11InputShapeFallback(string helper) => this.helper = helper;

        internal static X11InputShapeFallback TryCreate()
        {
            var windowsPath = AppContext.BaseDirectory;
            Log($"x11-input base={windowsPath}");
            if (!windowsPath.StartsWith("Z:\\", StringComparison.OrdinalIgnoreCase)) return null;
            var helper = Path.Combine(windowsPath, "exileapi-x11-input-shape");
            Log($"x11-input helper={helper} exists={File.Exists(helper)}");
            if (!File.Exists(helper)) return null;
            return new X11InputShapeFallback("/" + helper.Substring(3).Replace('\\', '/'));
        }

        internal void SetInteractive(bool value)
        {
            lock (this.gate)
            {
                this.desiredInteractive = value;
                if (this.appliedInteractive == value && (this.applyTask is null || this.applyTask.IsCompleted)) return;
                if (this.applyTask is null || this.applyTask.IsCompleted)
                {
                    this.applyTask = Task.Run(this.ApplyLatestState);
                }
            }
        }

        // start.exe launches a separate Wine/X11 process. Starting one process
        // per ImGui state transition lets older requests finish after newer
        // ones, leaving the input region in the wrong state. Serialize the
        // requests and coalesce changes so the final X Shape update always
        // matches the latest rendered UI state.
        private void ApplyLatestState()
        {
            while (true)
            {
                bool target;
                lock (this.gate)
                {
                    target = this.desiredInteractive;
                    if (this.appliedInteractive == target)
                    {
                        this.applyTask = null;
                        return;
                    }
                }

                try
                {
                    using var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = @"C:\\windows\\system32\\start.exe",
                        Arguments = $"/unix {this.helper} ExileApi {(target ? "interactive" : "passthrough")}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    });
                    if (process is null || !process.WaitForExit(1000))
                    {
                        Log($"x11-input helper timeout target={(target ? "interactive" : "passthrough")}");
                        this.MarkWorkerStopped();
                        return;
                    }
                    if (process.ExitCode != 0)
                    {
                        Log($"x11-input helper failed exit={process.ExitCode} target={(target ? "interactive" : "passthrough")}");
                        this.MarkWorkerStopped();
                        return;
                    }
                }
                catch (Exception error)
                {
                    Log($"x11-input launch failed={error.Message}");
                    this.MarkWorkerStopped();
                    return;
                }

                lock (this.gate)
                {
                    this.appliedInteractive = target;
                    if (this.desiredInteractive == target)
                    {
                        this.applyTask = null;
                        return;
                    }
                }
            }
        }

        private void MarkWorkerStopped()
        {
            lock (this.gate) this.applyTask = null;
        }

        private static void Log(string message)
        {
            try { File.AppendAllText("ClickableTransparentOverlay.renderer.log", $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); }
            catch { }
        }
    }
}
