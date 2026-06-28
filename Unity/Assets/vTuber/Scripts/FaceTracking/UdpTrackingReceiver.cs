using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace VTuberMyAvatar
{
    /// <summary>
    /// Receives tracking packets from the Python webcam tracker over localhost
    /// UDP on a background thread, and exposes the most recent decoded frame to
    /// the main thread without blocking it.
    /// </summary>
    public class UdpTrackingReceiver : MonoBehaviour
    {
        [Tooltip("UDP port to listen on. Must match the tracker's udp_port.")]
        public int port = 39539;

        public bool IsListening { get; private set; }

        /// <summary>Seconds since the last packet from the tracker arrived - any packet,
        /// including face-not-found ones, so this tracks whether the tracker process is
        /// alive (large = tracker not running).</summary>
        public float SecondsSinceLastPacket =>
            _lastPacketSeconds < 0 ? float.MaxValue : (float)_clock.Elapsed.TotalSeconds - _lastPacketSeconds;

        /// <summary>True if we've heard from the tracker recently.</summary>
        public bool TrackerConnected => SecondsSinceLastPacket < 1.0f;

        public float PacketsPerSecond { get; private set; }

        private Socket _socket;
        private Thread _thread;
        private volatile bool _running;

        private readonly object _lock = new object();
        private readonly float[] _recvShapes = new float[TrackingProtocol.NumShapes];
        private TrackingFrame _shared = TrackingFrame.CreateEmpty();
        private bool _hasNew;

        // Main-thread snapshot, refreshed once per frame in Update() so that every
        // driver can read the same latest frame without racing to "consume" it.
        private TrackingFrame _current = TrackingFrame.CreateEmpty();

        /// <summary>Latest tracking frame, safe to read on the main thread.</summary>
        public TrackingFrame Current => _current;

        /// <summary>True once at least one valid packet has been decoded.</summary>
        public bool HasEverReceived { get; private set; }

        // Thread-safe clock. Unity's Time API may only be used on the main thread,
        // so the network thread timestamps packets with a Stopwatch instead.
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private volatile float _lastPacketSeconds = -1f;

        private int _packetsThisSecond;
        private float _ppsTimer;

        private void OnEnable()
        {
            StartListening();
        }

        private void OnDisable()
        {
            StopListening();
        }

        private void Update()
        {
            // Refresh the main-thread snapshot once per frame from the network buffer.
            lock (_lock)
            {
                if (_hasNew)
                {
                    if (_current.Shapes == null || _current.Shapes.Length != TrackingProtocol.NumShapes)
                        _current.Shapes = new float[TrackingProtocol.NumShapes];
                    Array.Copy(_recvShapes, _current.Shapes, TrackingProtocol.NumShapes);
                    _current.Frame = _shared.Frame;
                    _current.FaceValid = _shared.FaceValid;
                    _current.HeadPitch = _shared.HeadPitch;
                    _current.HeadYaw = _shared.HeadYaw;
                    _current.HeadRoll = _shared.HeadRoll;
                    _current.HeadPosX = _shared.HeadPosX;
                    _current.HeadPosY = _shared.HeadPosY;
                    _current.HeadPosZ = _shared.HeadPosZ;
                    _hasNew = false;
                    HasEverReceived = true;
                }
            }

            // Maintain a simple packets-per-second readout on the main thread.
            _ppsTimer += Time.unscaledDeltaTime;
            if (_ppsTimer >= 1f)
            {
                // Atomically read-and-reset: the count is incremented on the network thread.
                int count = Interlocked.Exchange(ref _packetsThisSecond, 0);
                PacketsPerSecond = count / _ppsTimer;
                _ppsTimer = 0f;
            }
        }

        /// <summary>
        /// Stops and restarts the listener on a new port. Setting <see cref="port"/>
        /// directly has no effect on an already-bound socket - call this instead when
        /// changing the port live (e.g. from the in-app settings panel).
        /// </summary>
        public void Rebind(int newPort)
        {
            StopListening();
            port = newPort;
            StartListening();
        }

        private void StartListening()
        {
            if (_running) return;
            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                // Receive timeout in milliseconds, so the thread can poll _running.
                _socket.ReceiveTimeout = 500;
                _socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
                _running = true;
                _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "VTuberUdpTracking" };
                _thread.Start();
                IsListening = true;
                Debug.Log($"[vTuber] UDP tracking receiver listening on 127.0.0.1:{port}");
            }
            catch (Exception e)
            {
                IsListening = false;
                Debug.LogError($"[vTuber] Failed to start UDP receiver on port {port}: {e.Message}");
            }
        }

        private void StopListening()
        {
            _running = false;
            try { _socket?.Close(); } catch { /* ignore */ }
            _socket = null;
            if (_thread != null && _thread.IsAlive)
            {
                _thread.Join(750);
                _thread = null;
            }
            IsListening = false;
        }

        private void ReceiveLoop()
        {
            var buffer = new byte[512];
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);

            while (_running)
            {
                int n;
                try
                {
                    n = _socket.ReceiveFrom(buffer, ref remote);
                }
                catch (SocketException)
                {
                    // Timeout or transient error; loop and re-check _running.
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    // Socket closed during shutdown.
                    break;
                }

                if (n < TrackingProtocol.PacketBytes) continue;
                // We only target little-endian platforms.
                if (!BitConverter.IsLittleEndian) continue;

                int o = 0;
                uint magic = BitConverter.ToUInt32(buffer, o); o += 4;
                if (magic != TrackingProtocol.Magic) continue;
                uint version = BitConverter.ToUInt32(buffer, o); o += 4;
                if (version != TrackingProtocol.Version) continue;
                uint frame = BitConverter.ToUInt32(buffer, o); o += 4;
                uint flags = BitConverter.ToUInt32(buffer, o); o += 4;

                lock (_lock)
                {
                    for (int i = 0; i < TrackingProtocol.NumShapes; i++)
                    {
                        _recvShapes[i] = BitConverter.ToSingle(buffer, o);
                        o += 4;
                    }
                    _shared.Frame = frame;
                    _shared.FaceValid = (flags & TrackingProtocol.FlagFaceValid) != 0;
                    _shared.HeadPitch = BitConverter.ToSingle(buffer, o); o += 4;
                    _shared.HeadYaw = BitConverter.ToSingle(buffer, o); o += 4;
                    _shared.HeadRoll = BitConverter.ToSingle(buffer, o); o += 4;
                    _shared.HeadPosX = BitConverter.ToSingle(buffer, o); o += 4;
                    _shared.HeadPosY = BitConverter.ToSingle(buffer, o); o += 4;
                    _shared.HeadPosZ = BitConverter.ToSingle(buffer, o); o += 4;
                    _hasNew = true;
                }

                _lastPacketSeconds = (float)_clock.Elapsed.TotalSeconds;
                Interlocked.Increment(ref _packetsThisSecond);
            }
        }
    }
}
