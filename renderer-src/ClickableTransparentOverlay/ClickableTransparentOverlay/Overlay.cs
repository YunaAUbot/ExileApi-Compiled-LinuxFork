namespace ClickableTransparentOverlay
{
    using ClickableTransparentOverlay.Win32;
    using SixLabors.ImageSharp;
    using SixLabors.ImageSharp.PixelFormats;
    using SixLabors.ImageSharp.Formats;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Vortice.Direct3D;
    using Vortice.Direct3D11;
    using Vortice.DXGI;
    using Vortice.Mathematics;
    using Point = System.Drawing.Point;
    using Size = System.Drawing.Size;
    using ImGuiNET;
    using System.Collections.Concurrent;

    /// <summary>
    /// A class to create clickable transparent overlay on windows machine.
    /// </summary>
    public abstract class Overlay : IDisposable
    {
        private readonly string title;
        private readonly Format format;
        private readonly BackendPreference backendPreference;

        private WNDCLASSEX wndClass;
        private Win32Window window;
        private ID3D11Device device;
        private ID3D11DeviceContext deviceContext;
        private IDXGISwapChain swapChain;
        private ID3D11Texture2D backBuffer;
        private ID3D11RenderTargetView renderView;
        private LayeredWindowPresenter layeredPresenter;
        private bool useLayeredBackend;
        private readonly Stopwatch performanceClock = Stopwatch.StartNew();
        private long renderedFrames;

        private ImGuiRenderer renderer;
        private ImGuiInputHandler inputhandler;

        private bool _disposedValue;
        private IntPtr selfPointer;
        private Thread renderThread;
        private volatile CancellationTokenSource cancellationTokenSource;
        private volatile bool overlayIsReady;

        private Dictionary<string, (IntPtr Handle, uint Width, uint Height)> loadedTexturesPtrs;

        private readonly ConcurrentQueue<(FontLoadDelegate Update, TaskCompletionSource Completion)> fontUpdates;
        private readonly TaskCompletionSource shutdownCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private enum BackendPreference { Auto, Layered, Legacy }

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="Overlay"/> class.
        /// </summary>
        public Overlay() : this("Overlay")
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Overlay"/> class.
        /// </summary>
        /// <param name="windowTitle">
        /// Title of the window created by the overlay
        /// </param>
        public Overlay(string windowTitle) : this(windowTitle, false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Overlay"/> class.
        /// </summary>
        /// <param name="DPIAware">
        /// should the overlay scale with windows scale value or not.
        /// </param>
        public Overlay(bool DPIAware) : this("Overlay", DPIAware)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Overlay"/> class.
        /// </summary>
        /// <param name="windowTitle">
        /// Title of the window created by the overlay
        /// </param>
        /// <param name="DPIAware">
        /// should the overlay scale with windows scale value or not.
        /// </param>
        public Overlay(string windowTitle, bool DPIAware)
        {
            this.VSync = true;
            this._disposedValue = false;
            this.overlayIsReady = false;
            this.title = windowTitle;
            this.cancellationTokenSource = new();
            this.format = Format.R8G8B8A8_UNorm;
            this.backendPreference = ParseBackendPreference(Environment.GetEnvironmentVariable("EXILEAPI_OVERLAY_BACKEND"));
            this.loadedTexturesPtrs = new();
            this.fontUpdates = new();
            LogRenderer($"configured backend={this.backendPreference}");
            if (DPIAware)
            {
                User32.SetProcessDPIAware();
            }
        }

        #endregion

        #region PublicAPI

        public unsafe delegate void FontLoadDelegate(ImFontConfig* fontConfig);

        public event EventHandler FocusLost;

        public IntPtr? WindowHandle => this.window?.Handle is IntPtr handle && handle != IntPtr.Zero ? handle : null;

        protected CancellationToken OverlayCloseToken => this.cancellationTokenSource.Token;

        public Vanara.PInvoke.User32.MONITORINFO WindowMonitorInfo
        {
            get
            {
                var monitor = Vanara.PInvoke.User32.MonitorFromWindow(this.window.Handle, Vanara.PInvoke.User32.MonitorFlags.MONITOR_DEFAULTTONEAREST);
                var info = Vanara.PInvoke.User32.MONITORINFO.Default;
                Vanara.PInvoke.User32.GetMonitorInfo(monitor, ref info);
                return info;
            }
        }

        /// <summary>
        /// Starts the overlay
        /// </summary>
        /// <returns>A Task that finishes once the overlay window is ready</returns>
        public async Task Start()
        {
            this.renderThread = new Thread(() =>
            {
                this.InitializeResources();
                this.ReplaceFontIfRequired();
                this.renderer.Start();
                this.RunInfiniteLoop(this.cancellationTokenSource.Token);
            });

            this.renderThread.Start();
            await WaitHelpers.SpinWait(() => this.overlayIsReady);
        }

        /// <summary>
        /// Starts the overlay and waits for the overlay window to be closed.
        /// </summary>
        /// <returns>A task that finishes once the overlay window closes</returns>
        public virtual async Task Run()
        {
            if (!this.overlayIsReady)
            {
                await this.Start();
            }

            await WaitHelpers.SpinWait(() => this.cancellationTokenSource.IsCancellationRequested);
        }

        /// <summary>
        /// Safely Closes the Overlay.
        /// </summary>
        public virtual void Close()
        {
            this.cancellationTokenSource.Cancel();
        }

        public Task WaitForShutdown() => this.shutdownCompletion.Task;

        public void SetIcon(string fileName)
        {
            if (this.window is null || !File.Exists(fileName)) return;
            var icon = User32.LoadImage(IntPtr.Zero, fileName, 1, 0, 0, 0x10);
            if (icon != IntPtr.Zero)
            {
                User32.SendMessage(this.window.Handle, 0x80, UIntPtr.Zero, icon);
                User32.SendMessage(this.window.Handle, 0x80, new UIntPtr(1), icon);
            }
        }

        /// <summary>
        /// Safely dispose all the resources created by the overlay
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Replaces the ImGui font with another one.
        /// </summary>
        /// <param name="pathName">pathname to the TTF font file.</param>
        /// <param name="size">font size to load.</param>
        /// <param name="language">supported language by the font.</param>
        /// <returns>true if the font replacement is valid otherwise false.</returns>
        public unsafe bool ReplaceFont(string pathName, int size, FontGlyphRangeType language)
        {
            if (!File.Exists(pathName))
            {
                return false;
            }

            QueueFontUpdate(config =>
            {
                var io = ImGui.GetIO();
                var glyphRange = language switch
                {
                    FontGlyphRangeType.English => io.Fonts.GetGlyphRangesDefault(),
                    FontGlyphRangeType.ChineseSimplifiedCommon => io.Fonts.GetGlyphRangesChineseSimplifiedCommon(),
                    FontGlyphRangeType.ChineseFull => io.Fonts.GetGlyphRangesChineseFull(),
                    FontGlyphRangeType.Japanese => io.Fonts.GetGlyphRangesJapanese(),
                    FontGlyphRangeType.Korean => io.Fonts.GetGlyphRangesKorean(),
                    FontGlyphRangeType.Thai => io.Fonts.GetGlyphRangesThai(),
                    FontGlyphRangeType.Vietnamese => io.Fonts.GetGlyphRangesVietnamese(),
                    FontGlyphRangeType.Cyrillic => io.Fonts.GetGlyphRangesCyrillic(),
                    _ => throw new Exception($"Font Glyph Range (${language}) is not supported.")
                };

                io.Fonts.AddFontFromFileTTF(pathName, size, config, glyphRange);
                ImGuiNative.igGetIO()->FontDefault = null;
            });

            return true;
        }

        /// <summary>
        /// Replaces the ImGui font with another one.
        /// </summary>
        /// <param name="pathName">pathname to the TTF font file.</param>
        /// <param name="size">font size to load.</param>
        /// <param name="glyphRange">custom glyph range of the font to load. Read <see cref="FontGlyphRangeType"/> for more detail.</param>
        /// <returns>>true if the font replacement is valid otherwise false.</returns>
        public unsafe bool ReplaceFont(string pathName, int size, ushort[] glyphRange)
        {
            if (!File.Exists(pathName))
            {
                return false;
            }

            QueueFontUpdate(config =>
            {
                var io = ImGui.GetIO();
                fixed (ushort* p = &glyphRange[0])
                {
                    io.Fonts.AddFontFromFileTTF(pathName, size, config, new IntPtr(p));
                    ImGuiNative.igGetIO()->FontDefault = null;
                }
            });

            return true;
        }

        /// <summary>
        /// Replaces the ImGui font with the default ImGui font.
        /// </summary>
        /// <returns>always return true</returns>
        private unsafe bool ReplaceFont()
        {
            QueueFontUpdate(config =>
            {
                var io = ImGui.GetIO();
                io.Fonts.AddFontDefault(config);
                ImGuiNative.igGetIO()->FontDefault = null;
            });

            return true;
        }

        /// <summary>
        /// Replaces the ImGui font with another one.
        /// </summary>
        /// <param name="fontLoadDelegate">instructions for loading the font</param>
        public unsafe Task ReplaceFont(FontLoadDelegate fontLoadDelegate)
        {
            // have to do this because of issue: https://github.com/ocornut/imgui/issues/6858
            ImGuiNative.igGetIO()->FontDefault = null;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            this.fontUpdates.Enqueue((fontLoadDelegate, completion));
            return completion.Task;
        }

        /// <summary>
        /// Enable or disable the vsync on the overlay.
        /// </summary>
        public bool VSync;

        /// <summary>
        /// Gets or sets the position of the overlay window.
        /// </summary>
        public Point Position
        {
            get
            {
                return this.window.Dimensions.Location;
            }

            set
            {
                if (this.window.Dimensions.Location != value)
                {
                    User32.MoveWindow(this.window.Handle, value.X, value.Y, this.window.Dimensions.Width, this.window.Dimensions.Height, true);
                    this.window.Dimensions.Location = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the size of the overlay window.
        /// </summary>
        public Size Size
        {
            get
            {
                return this.window.Dimensions.Size;
            }
            set
            {
                if (this.window.Dimensions.Size != value)
                {
                    User32.MoveWindow(this.window.Handle, this.window.Dimensions.X, this.window.Dimensions.Y, value.Width, value.Height, true);
                    this.window.Dimensions.Size = value;
                }
            }
        }

        /// <summary>
        /// Gets the number of displays available on the computer.
        /// </summary>
        public static int NumberVideoDisplays
        {
            get
            {
                return User32.GetSystemMetrics(0x50); // SM_CMONITORS
            }
        }

        /// <summary>
        /// Adds the image to the Graphic Device as a texture.
        /// Then returns the pointer of the added texture. It also
        /// cache the image internally rather than creating a new texture on every call,
        /// so this function can be called multiple times per frame.
        /// </summary>
        /// <param name="filePath">Path to the image on disk.</param>
        /// <param name="srgb"> a value indicating whether pixel format is srgb or not.</param>
        /// <param name="handle">output pointer to the image in the graphic device.</param>
        /// <param name="width">width of the loaded texture.</param>
        /// <param name="height">height of the loaded texture.</param>
        public void AddOrGetImagePointer(string filePath, bool srgb, out IntPtr handle, out uint width, out uint height)
        {
            if (this.loadedTexturesPtrs.TryGetValue(filePath, out var data))
            {
                handle = data.Handle;
                width = data.Width;
                height = data.Height;
            }
            else
            {
                var configuration = Configuration.Default.Clone();
                configuration.PreferContiguousImageBuffers = true;
                using var image = Image.Load<Rgba32>(configuration, filePath);
                handle = this.renderer.CreateImageTexture(image, srgb ? Format.R8G8B8A8_UNorm_SRgb : Format.R8G8B8A8_UNorm);
                width = (uint)image.Width;
                height = (uint)image.Height;
                this.loadedTexturesPtrs.Add(filePath, new(handle, width, height));
            }
        }

        public void AddOrGetImagePointer(string filePath, string name, bool srgb, out IntPtr handle, out uint width, out uint height)
        {
            if (this.loadedTexturesPtrs.TryGetValue(name, out var data))
            {
                handle = data.Handle; width = data.Width; height = data.Height; return;
            }
            var configuration = Configuration.Default.Clone();
            configuration.PreferContiguousImageBuffers = true;
            using var image = Image.Load<Rgba32>(configuration, filePath);
            handle = this.renderer.CreateImageTexture(image, srgb ? Format.R8G8B8A8_UNorm_SRgb : Format.R8G8B8A8_UNorm);
            width = (uint)image.Width; height = (uint)image.Height;
            this.loadedTexturesPtrs.Add(name, new(handle, width, height));
        }

        public bool HasImagePointer(string nameOrFilePath) => this.loadedTexturesPtrs.ContainsKey(nameOrFilePath);

        public IntPtr GetImagePointer(string nameOrFilePath) => this.loadedTexturesPtrs.TryGetValue(nameOrFilePath, out var data) ? data.Handle : IntPtr.Zero;

        public bool TryGetImagePointer(string nameOrFilePath, out IntPtr handle)
        {
            handle = GetImagePointer(nameOrFilePath);
            return handle != IntPtr.Zero;
        }

        /// <summary>
        /// Adds the image to the Graphic Device as a texture.
        /// Then returns the pointer of the added texture. It also
        /// cache the image internally rather than creating a new texture on every call,
        /// so this function can be called multiple times per frame.
        /// </summary>
        /// <param name="name">user friendly name given to the image.</param>
        /// <param name="image">Image data in <see cref="Image"> format.</param>
        /// <param name="srgb"> a value indicating whether pixel format is srgb or not.</param>
        /// <param name="handle">output pointer to the image in the graphic device.</param>
        public void AddOrGetImagePointer(string name, Image<Rgba32> image, bool srgb, out IntPtr handle)
        {
            if (this.loadedTexturesPtrs.TryGetValue(name, out var data))
            {
                handle = data.Handle;
            }
            else
            {
                handle = this.renderer.CreateImageTexture(image, srgb ? Format.R8G8B8A8_UNorm_SRgb : Format.R8G8B8A8_UNorm);
                this.loadedTexturesPtrs.Add(name, new(handle, (uint)image.Width, (uint)image.Height));
            }
        }

        /// <summary>
        /// Removes the image from the Overlay.
        /// </summary>
        /// <param name="key">name or pathname which was used to add the image in the first place.</param>
        /// <returns> true if the image is removed otherwise false.</returns>
        public bool RemoveImage(string key)
        {
            if (this.loadedTexturesPtrs.Remove(key, out var data))
            {
                return this.renderer.RemoveImageTexture(data.Handle);
            }

            return false;
        }

        #endregion

        protected virtual void Dispose(bool disposing)
        {
            if (this._disposedValue)
            {
                return;
            }

            if (disposing)
            {
                this.renderThread?.Join();
                foreach(var key in this.loadedTexturesPtrs.Keys.ToArray())
                {
                    this.RemoveImage(key);
                }

                this.cancellationTokenSource?.Dispose();
                this.fontUpdates?.Clear();
                this.swapChain?.Release();
                this.backBuffer?.Release();
                this.renderView?.Release();
                this.layeredPresenter?.Dispose();
                this.renderer?.Dispose();
                this.window?.Dispose();
                this.deviceContext?.Release();
                this.device?.Release();
            }

            if (this.selfPointer != IntPtr.Zero)
            {
                if (!User32.UnregisterClass(this.title, this.selfPointer))
                {
                    throw new Exception($"Failed to Unregister {this.title} class during dispose.");
                }

                this.selfPointer = IntPtr.Zero;
            }

            this._disposedValue = true;
        }

        /// <summary>
        /// Steps to execute after the overlay has fully initialized.
        /// </summary>
        protected virtual Task PostInitialized()
        {
            return Task.CompletedTask;
        }

        protected virtual void PostFrame() { }

        protected virtual Task OnClosed() => Task.CompletedTask;

        /// <summary>
        /// Abstract Task for creating the UI.
        /// </summary>
        /// <returns>Task that finishes once per frame</returns>
        protected abstract void Render();

        private void RunInfiniteLoop(CancellationToken token)
        {
            var stopwatch = Stopwatch.StartNew();
            float deltaTime = 0f;
            var clearColor = new Color4(0.0f);
            while (!token.IsCancellationRequested)
            {
                deltaTime = stopwatch.ElapsedTicks / (float)Stopwatch.Frequency;
                stopwatch.Restart();
                this.window.PumpEvents();
                Utils.SetOverlayClickable(this.window.Handle, this.inputhandler.Update());
                this.renderer.Update(deltaTime, () => { Render(); });
                var activeView = this.useLayeredBackend ? this.layeredPresenter.RenderTargetView : this.renderView;
                this.deviceContext.OMSetRenderTargets(activeView);
                this.deviceContext.ClearRenderTargetView(activeView, clearColor);
                this.renderer.Render();
                if (this.useLayeredBackend)
                {
                    try
                    {
                        this.layeredPresenter.Present(this.window.Dimensions.X, this.window.Dimensions.Y);
                        if ((this.renderedFrames % 60) == 0) Utils.EnsureTopmost(this.window.Handle);
                    }
                    catch (Exception error) when (this.backendPreference == BackendPreference.Auto)
                    {
                        LogRenderer($"layered present failed, switching to legacy: {error}");
                        SwitchToLegacyBackend();
                    }
                    if (VSync)
                    {
                        var remaining = 33 - (int)stopwatch.ElapsedMilliseconds;
                        if (remaining > 0) Thread.Sleep(remaining);
                    }
                }
                else if (VSync)
                {
                    this.swapChain.Present(1, PresentFlags.None); // Present with vsync
                }
                else
                {
                    this.swapChain.Present(0, PresentFlags.None); // Present without vsync
                }

                this.ReplaceFontIfRequired();
                this.PostFrame();
                this.renderedFrames++;
                if (this.renderedFrames % 300 == 0)
                {
                    var seconds = performanceClock.Elapsed.TotalSeconds;
                    LogRenderer($"frames={this.renderedFrames} average-fps={this.renderedFrames / seconds:F1} size={this.window.Dimensions.Width}x{this.window.Dimensions.Height} backend={(this.useLayeredBackend ? "layered" : "legacy")}");
                }
            }
            this.OnClosed().GetAwaiter().GetResult();
            this.shutdownCompletion.TrySetResult();
        }

        private void ReplaceFontIfRequired()
        {
            if (this.renderer != null)
            {
                while (this.fontUpdates.TryDequeue(out var update))
                {
                    try { this.renderer.UpdateFontTexture(update.Update); update.Completion.TrySetResult(); }
                    catch (Exception error) { update.Completion.TrySetException(error); }
                }
            }
        }

        private void QueueFontUpdate(FontLoadDelegate update)
        {
            this.fontUpdates.Enqueue((update, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)));
        }

        private void OnResize()
        {
            if (this.useLayeredBackend)
            {
                this.layeredPresenter.Resize(this.window.Dimensions.Width, this.window.Dimensions.Height);
                this.renderer.Resize(this.window.Dimensions.Width, this.window.Dimensions.Height);
                return;
            }
            if (renderView == null)//first show
            {
                using var dxgiFactory = device.QueryInterface<IDXGIDevice>().GetParent<IDXGIAdapter>().GetParent<IDXGIFactory>();
                var swapchainDesc = new SwapChainDescription()
                {
                    BufferCount = 1,
                    BufferDescription = new ModeDescription((uint)this.window.Dimensions.Width, (uint)this.window.Dimensions.Height, this.format),
                    Windowed = true,
                    OutputWindow = this.window.Handle,
                    SampleDescription = new SampleDescription(1, 0),
                    SwapEffect = SwapEffect.Discard,
                    BufferUsage = Usage.RenderTargetOutput,
                };

                this.swapChain = dxgiFactory.CreateSwapChain(this.device, swapchainDesc);
                dxgiFactory.MakeWindowAssociation(this.window.Handle, WindowAssociationFlags.IgnoreAll);

                this.backBuffer = this.swapChain.GetBuffer<ID3D11Texture2D>(0);
                this.renderView = this.device.CreateRenderTargetView(backBuffer);
            }
            else
            {
                this.renderView.Dispose();
                this.backBuffer.Dispose();

                this.swapChain.ResizeBuffers(1, (uint)this.window.Dimensions.Width, (uint)this.window.Dimensions.Height, this.format, SwapChainFlags.None);

                backBuffer = this.swapChain.GetBuffer<ID3D11Texture2D1>(0);
                renderView = this.device.CreateRenderTargetView(backBuffer);
            }

            this.renderer.Resize(this.window.Dimensions.Width, this.window.Dimensions.Height);
        }

        private void InitializeResources()
        {
            D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.None,
                new[] { FeatureLevel.Level_10_0 },
                out this.device,
                out this.deviceContext);
            this.selfPointer = Kernel32.GetModuleHandle(null);
            this.wndClass = new WNDCLASSEX
            {
                Size = Unsafe.SizeOf<WNDCLASSEX>(),
                Styles = WindowClassStyles.CS_HREDRAW | WindowClassStyles.CS_VREDRAW | WindowClassStyles.CS_PARENTDC,
                WindowProc = WndProc,
                InstanceHandle = this.selfPointer,
                CursorHandle = User32.LoadCursor(IntPtr.Zero, SystemCursor.IDC_ARROW),
                BackgroundBrushHandle = IntPtr.Zero,
                IconHandle = IntPtr.Zero,
                MenuName = string.Empty,
                ClassName = this.title,
                SmallIconHandle= IntPtr.Zero,
                ClassExtraBytes = 0,
                WindowExtraBytes = 0
            };

            if (User32.RegisterClassEx(ref this.wndClass) == 0)
            {
                throw new Exception($"Failed to Register class of name {this.wndClass.ClassName}");
            }

            this.window = new Win32Window(
                wndClass.ClassName,
                800,
                600,
                0,
                0,
                this.title,
                WindowStyles.WS_POPUP,
                WindowExStyles.WS_EX_ACCEPTFILES | WindowExStyles.WS_EX_TOPMOST |
                (this.backendPreference == BackendPreference.Legacy ? 0 : WindowExStyles.WS_EX_LAYERED));
            this.renderer = new ImGuiRenderer(device, deviceContext, 800, 600);
            this.inputhandler = new ImGuiInputHandler(this.window.Handle);
            var targetWindow = User32.FindWindow(null, "Path of Exile");
            if (targetWindow != IntPtr.Zero && targetWindow != this.window.Handle)
            {
                User32.SetWindowOwner(this.window.Handle, (int)WindowLongParam.GWLP_HWNDPARENT, targetWindow);
                LogRenderer($"attached overlay owner hwnd=0x{targetWindow:x}");
            }
            this.useLayeredBackend = this.backendPreference != BackendPreference.Legacy;
            if (this.useLayeredBackend)
            {
                try
                {
                    this.layeredPresenter = new LayeredWindowPresenter(device, deviceContext, this.window.Handle);
                    this.layeredPresenter.Resize(800, 600);
                    LogRenderer("layered backend initialized");
                }
                catch when (this.backendPreference == BackendPreference.Auto)
                {
                    this.layeredPresenter?.Dispose();
                    this.layeredPresenter = null;
                    this.useLayeredBackend = false;
                    LogRenderer("layered initialization failed; using legacy backend");
                }
            }
            this.overlayIsReady = true;
            // The HWND and its message pump must remain owned by this dedicated
            // render thread. Awaiting here without a synchronization context can
            // resume ExileCore's async initialization on a pool thread under Wine.
            this.PostInitialized().GetAwaiter().GetResult();
            // ExileCore already sizes the overlay to the PoE client rectangle in
            // PostInitialized. Maximizing here uses KDE's work area (excluding
            // panels), which puts the overlay below an EWMH fullscreen PoE
            // window. Preserve the requested game-sized rectangle instead.
            User32.ShowWindow(this.window.Handle, ShowWindowCommand.Show);
            if (this.useLayeredBackend)
            {
                // Keep exactly the rectangle requested by ExileCore. The owner
                // relationship to the PoE window handles fullscreen z-order;
                // forcing the primary monitor's dimensions breaks multi-monitor
                // and non-native-resolution configurations.
                Utils.InitLayeredInput(this.window.Handle);
            }
            else Utils.InitTransparency(this.window.Handle);
        }

        private bool ProcessMessage(WindowMessage msg, UIntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case WindowMessage.Size:
                    switch ((SizeMessage)wParam)
                    {
                        case SizeMessage.SIZE_RESTORED:
                        case SizeMessage.SIZE_MAXIMIZED:
                            var lp = (int)lParam;
                            this.window.Dimensions.Width = Utils.Loword(lp);
                            this.window.Dimensions.Height = Utils.Hiword(lp);
                            this.OnResize();
                            break;
                        default:
                            break;
                    }

                    break;
                case WindowMessage.Destroy:
                    this.Close();
                    break;
                case WindowMessage.KillFocus:
                    this.FocusLost?.Invoke(this, EventArgs.Empty);
                    break;
                default:
                    break;
            }

            return false;
        }

        private static BackendPreference ParseBackendPreference(string value) => value?.Trim().ToLowerInvariant() switch
        {
            "legacy" => BackendPreference.Legacy,
            "layered" => BackendPreference.Layered,
            _ => BackendPreference.Auto
        };

        private void SwitchToLegacyBackend()
        {
            this.layeredPresenter?.Dispose();
            this.layeredPresenter = null;
            this.useLayeredBackend = false;
            Utils.InitTransparency(this.window.Handle);
            this.OnResize();
        }

        private static void LogRenderer(string message)
        {
            try { File.AppendAllText("ClickableTransparentOverlay.renderer.log", $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); }
            catch { }
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam)
        {
            if (this.overlayIsReady)
            {
                if (this.inputhandler.ProcessMessage((WindowMessage)msg, wParam, lParam) ||
                    this.ProcessMessage((WindowMessage)msg, wParam, lParam))
                {
                    return IntPtr.Zero;
                }
            }

            return User32.DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }
}
