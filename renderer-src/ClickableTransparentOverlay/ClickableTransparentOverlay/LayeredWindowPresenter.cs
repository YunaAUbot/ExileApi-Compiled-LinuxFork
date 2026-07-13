namespace ClickableTransparentOverlay;

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

/// <summary>
/// Presents a premultiplied BGRA D3D11 render target through the Win32 layered
/// window API.  Unlike a transparent swap chain this is implemented by Wine's
/// window manager path and does not depend on DWM composition.
/// </summary>
internal sealed unsafe class LayeredWindowPresenter : IDisposable
{
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;
    private readonly IntPtr hwnd;
    private ID3D11Texture2D? target;
    private ID3D11Texture2D? staging;
    private ID3D11RenderTargetView? view;
    private IntPtr screenDc;
    private IntPtr memoryDc;
    private IntPtr bitmap;
    private IntPtr oldBitmap;
    private IntPtr bits;
    private int width;
    private int height;

    public LayeredWindowPresenter(ID3D11Device device, ID3D11DeviceContext context, IntPtr hwnd)
    {
        this.device = device;
        this.context = context;
        this.hwnd = hwnd;
        device.AddRef();
        context.AddRef();
    }

    public ID3D11RenderTargetView RenderTargetView => view ?? throw new InvalidOperationException("Presenter has not been resized.");

    public void Resize(int newWidth, int newHeight)
    {
        if (newWidth <= 0 || newHeight <= 0 || (newWidth == width && newHeight == height)) return;
        ReleaseSizeResources();
        width = newWidth;
        height = newHeight;

        var targetDescription = new Texture2DDescription(
            Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
            BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, 1, 0);
        target = device.CreateTexture2D(targetDescription);
        view = device.CreateRenderTargetView(target);

        var stagingDescription = new Texture2DDescription(
            Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
            BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read, 1, 0);
        staging = device.CreateTexture2D(stagingDescription);

        screenDc = GetDC(IntPtr.Zero);
        memoryDc = CreateCompatibleDC(screenDc);
        var info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = (uint)sizeof(BitmapInfoHeader),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = 0,
                SizeImage = (uint)(width * height * 4)
            }
        };
        bitmap = CreateDIBSection(memoryDc, ref info, 0, out bits, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) ThrowLastWin32("CreateDIBSection");
        oldBitmap = SelectObject(memoryDc, bitmap);
    }

    public void Present(int x, int y)
    {
        if (target is null || staging is null || bits == IntPtr.Zero) return;
        context.CopyResource(staging, target);
        var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var source = (byte*)mapped.DataPointer;
            var destination = (byte*)bits;
            var rowBytes = width * 4;
            for (var row = 0; row < height; row++)
                Buffer.MemoryCopy(source + row * mapped.RowPitch, destination + row * rowBytes, rowBytes, rowBytes);
        }
        finally
        {
            context.Unmap(staging, 0);
        }

        var destinationPoint = new NativePoint(x, y);
        var sourcePoint = new NativePoint(0, 0);
        var size = new NativeSize(width, height);
        var blend = new BlendFunction { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        if (!UpdateLayeredWindow(hwnd, screenDc, ref destinationPoint, ref size, memoryDc, ref sourcePoint, 0, ref blend, 2))
        {
            var error = Marshal.GetLastWin32Error();
            try
            {
                System.IO.File.AppendAllText("ClickableTransparentOverlay.renderer.log",
                    $"{DateTimeOffset.Now:O} ULW diagnostic error={error} hwnd=0x{hwnd:x} size={width}x{height} pos={x},{y} screenDc=0x{screenDc:x} memoryDc=0x{memoryDc:x} bitmap=0x{bitmap:x} bits=0x{bits:x} exstyle=0x{ClickableTransparentOverlay.Win32.User32.GetWindowLong(hwnd, -20):x}{Environment.NewLine}");
            }
            catch { }
            throw new Win32Exception(error, "UpdateLayeredWindow failed");
        }
    }

    public void Dispose()
    {
        ReleaseSizeResources();
        context.Release();
        device.Release();
    }

    private void ReleaseSizeResources()
    {
        view?.Dispose(); view = null;
        target?.Dispose(); target = null;
        staging?.Dispose(); staging = null;
        if (oldBitmap != IntPtr.Zero && memoryDc != IntPtr.Zero) SelectObject(memoryDc, oldBitmap);
        oldBitmap = IntPtr.Zero;
        if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
        bitmap = IntPtr.Zero;
        bits = IntPtr.Zero;
        if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
        memoryDc = IntPtr.Zero;
        if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
        screenDc = IntPtr.Zero;
    }

    private static void ThrowLastWin32(string operation) => throw new Win32Exception(Marshal.GetLastWin32Error(), operation + " failed");

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; public NativePoint(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width, Height; public NativeSize(int w, int h) { Width = w; Height = h; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BlendFunction { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter; public uint ColorsUsed, ColorsImportant;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapInfoHeader Header; public uint Colors; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr destinationDc, ref NativePoint destinationPoint, ref NativeSize size, IntPtr sourceDc, ref NativePoint sourcePoint, uint colorKey, ref BlendFunction blend, uint flags);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);
}
