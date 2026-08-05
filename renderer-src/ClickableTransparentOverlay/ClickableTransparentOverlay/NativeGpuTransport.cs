namespace ClickableTransparentOverlay
{
    using System;
    using System.Net.Sockets;

    // Best-effort loopback transport. A failed/slow native renderer never
    // blocks ExileCore's render thread: frames are simply dropped until the
    // next reconnect attempt.
    internal sealed class NativeGpuTransport : IDisposable
    {
        private readonly int port;
        private TcpClient client;
        private NetworkStream stream;
        private DateTime nextConnectAttempt = DateTime.MinValue;
        private int expectedIncoming = -1;
        private byte[] incoming;
        private int incomingCount;

        internal NativeGpuTransport(int port) => this.port = port;

        internal bool TrySend(byte[] frame)
        {
            try
            {
                if (!EnsureConnected()) return false;
                var length = BitConverter.GetBytes(frame.Length);
                this.stream.Write(length, 0, length.Length);
                this.stream.Write(frame, 0, frame.Length);
                return true;
            }
            catch
            {
                DisposeConnection();
                return false;
            }
        }

        internal bool TryReadMouse(out int button, out bool down, out float x, out float y)
        {
            button = 0; down = false; x = 0; y = 0;
            try
            {
                if (this.stream is null || this.client.Available == 0) return false;
                if (this.expectedIncoming < 0)
                {
                    if (this.client.Available < 4) return false;
                    var length = new byte[4]; this.stream.ReadExactly(length);
                    this.expectedIncoming = BitConverter.ToInt32(length, 0);
                    if (this.expectedIncoming != 20) { this.expectedIncoming = -1; return false; }
                    this.incoming = new byte[this.expectedIncoming]; this.incomingCount = 0;
                }
                var available = Math.Min(this.client.Available, this.expectedIncoming - this.incomingCount);
                if (available > 0) this.incomingCount += this.stream.Read(this.incoming, this.incomingCount, available);
                if (this.incomingCount != this.expectedIncoming) return false;
                var message = this.incoming; this.expectedIncoming = -1; this.incoming = null;
                if (BitConverter.ToUInt32(message, 0) != NativeGpuFrameProtocol.MouseInputMagic) return false;
                button = BitConverter.ToInt32(message, 4); down = BitConverter.ToUInt32(message, 8) != 0;
                x = BitConverter.ToSingle(message, 12); y = BitConverter.ToSingle(message, 16);
                return true;
            }
            catch { DisposeConnection(); return false; }
        }

        private bool EnsureConnected()
        {
            if (this.stream is not null) return true;
            if (DateTime.UtcNow < this.nextConnectAttempt) return false;
            this.nextConnectAttempt = DateTime.UtcNow.AddMilliseconds(500);
            var candidate = new TcpClient { NoDelay = true };
            try
            {
                var connect = candidate.ConnectAsync("127.0.0.1", this.port);
                if (!connect.Wait(10)) { candidate.Dispose(); return false; }
                this.client = candidate;
                this.stream = candidate.GetStream();
                return true;
            }
            catch { candidate.Dispose(); return false; }
        }

        private void DisposeConnection()
        {
            this.stream?.Dispose(); this.stream = null;
            this.client?.Dispose(); this.client = null;
            this.expectedIncoming = -1; this.incoming = null; this.incomingCount = 0;
        }

        public void Dispose() => DisposeConnection();
    }
}
