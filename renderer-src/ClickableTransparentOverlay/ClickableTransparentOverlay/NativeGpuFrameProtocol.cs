namespace ClickableTransparentOverlay
{
    using System;
    using System.IO;
    using ImGuiNET;

    // Binary frame representation for the Wine -> native GLX boundary. It
    // intentionally contains only ImGui geometry (20-byte vertices, UInt16
    // indices and scissor rectangles), never a monitor-sized pixel buffer.
    internal static unsafe class NativeGpuFrameProtocol
    {
        internal const uint FrameMagic = 0x31464745; // "EGF1"
        internal const uint FontMagic = 0x31415445; // "ETA1"
        internal const uint InputModeMagic = 0x31435345; // "ESC1"
        internal const uint MouseInputMagic = 0x31494E45; // "ENI1"
        internal static (float Width, float Height) LastDisplaySize { get; private set; }

        internal static byte[] SerializeInputMode(bool interactive)
        {
            using var stream = new MemoryStream(8);
            using var writer = new BinaryWriter(stream);
            writer.Write(InputModeMagic);
            writer.Write(interactive ? 1u : 0u);
            return stream.ToArray();
        }

        internal static byte[] SerializeFont(byte[] pixels, int width, int height)
        {
            using var stream = new MemoryStream(16 + pixels.Length);
            using var writer = new BinaryWriter(stream);
            writer.Write(FontMagic);
            writer.Write(width);
            writer.Write(height);
            writer.Write(pixels.Length);
            writer.Write(pixels);
            return stream.ToArray();
        }

        internal static byte[] Serialize(ImDrawDataPtr data, IntPtr fontTexture)
        {
            LastDisplaySize = (data.DisplaySize.X, data.DisplaySize.Y);
            using var stream = new MemoryStream(Math.Max(256, data.TotalVtxCount * 20 + data.TotalIdxCount * 2));
            using var writer = new BinaryWriter(stream);
            writer.Write(FrameMagic);
            writer.Write(data.TotalVtxCount);
            writer.Write(data.TotalIdxCount);
            writer.Write(data.CmdListsCount);
            writer.Write(data.DisplayPos.X); writer.Write(data.DisplayPos.Y);
            writer.Write(data.DisplaySize.X); writer.Write(data.DisplaySize.Y);
            for (var listIndex = 0; listIndex < data.CmdListsCount; listIndex++)
            {
                var list = data.CmdLists[listIndex];
                writer.Write(list.VtxBuffer.Size);
                writer.Write(list.IdxBuffer.Size);
                writer.Write(list.CmdBuffer.Size);
                writer.Write(new ReadOnlySpan<byte>((void*)list.VtxBuffer.Data, list.VtxBuffer.Size * sizeof(ImDrawVert)));
                writer.Write(new ReadOnlySpan<byte>((void*)list.IdxBuffer.Data, list.IdxBuffer.Size * sizeof(ushort)));
                for (var commandIndex = 0; commandIndex < list.CmdBuffer.Size; commandIndex++)
                {
                    var command = list.CmdBuffer[commandIndex];
                    writer.Write(command.ElemCount);
                    writer.Write(command.IdxOffset);
                    writer.Write(command.VtxOffset);
                    writer.Write(command.ClipRect.X); writer.Write(command.ClipRect.Y);
                    writer.Write(command.ClipRect.Z); writer.Write(command.ClipRect.W);
                    // Font/white-pixel drawlists are supported first. Other
                    // texture IDs are explicitly marked for the later image
                    // transport rather than silently falling back to a frame
                    // readback.
                    writer.Write(command.GetTexID() == fontTexture ? 1u : 0u);
                }
            }
            return stream.ToArray();
        }
    }
}
