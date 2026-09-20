using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// The Mac's music, in the room. Connects to the Jarvis app's stream
    /// (`OPUSPCM1 48000 2` header, then raw 16-bit LE stereo PCM), keeps a
    /// short ring buffer, and plays it through a streaming AudioClip on a
    /// spatialised AudioSource — so the music comes from wherever you put
    /// this. Reconnects on its own; silence while it can't reach the Mac.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class MacSpeaker : MonoBehaviour
    {
        public string host = "";
        public int port = 48100;
        /// <summary>Seconds buffered before playback starts (latency floor).</summary>
        public float prebufferSeconds = 0.25f;

        private const int Rate = 48000;
        private const int Channels = 2;
        private const int RingSeconds = 3;

        private readonly float[] _ring = new float[Rate * Channels * RingSeconds];
        private int _write, _read, _count; // in floats
        private readonly object _lock = new object();
        private bool _primed;
        private Thread _thread;
        private volatile bool _stop;
        private volatile string _state = "off";
        private float _level;

        public string State => _state;
        /// <summary>Recent output loudness, 0–1, for a meter.</summary>
        public float Level => _level;
        public bool Connected => _state == "connected";

        private void Start()
        {
            var src = GetComponent<AudioSource>();
            src.clip = AudioClip.Create("mac", Rate, Channels, Rate, true, OnRead);
            src.loop = true;
            src.spatialBlend = 1f;
            src.minDistance = 0.6f;
            src.maxDistance = 8f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.Play();
            if (!string.IsNullOrEmpty(host)) Connect();
        }

        public void Connect()
        {
            Disconnect();
            _stop = false;
            _thread = new Thread(Pump) { IsBackground = true, Name = "MacSpeaker" };
            _thread.Start();
        }

        public void Disconnect()
        {
            _stop = true;
            _thread = null;
            lock (_lock) { _count = 0; _read = _write = 0; _primed = false; }
            _state = "off";
        }

        private void OnDestroy() => Disconnect();

        private void Pump()
        {
            var buf = new byte[4096 * 4];
            while (!_stop)
            {
                TcpClient client = null;
                try
                {
                    _state = "connecting…";
                    client = new TcpClient { NoDelay = true, ReceiveBufferSize = 1 << 16 };
                    client.Client.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.KeepAlive, true);
                    if (!client.ConnectAsync(host, port).Wait(4000)) throw new TimeoutException("connect timed out");
                    using var stream = client.GetStream();
                    var header = ReadLine(stream);
                    if (!header.StartsWith("OPUSPCM1")) throw new IOException("not a music stream: " + header);
                    _state = "connected";
                    var carry = 0;
                    while (!_stop)
                    {
                        var n = stream.Read(buf, carry, buf.Length - carry);
                        if (n <= 0) throw new IOException("stream ended");
                        n += carry;
                        var whole = n & ~1; // 16-bit samples
                        Push(buf, whole);
                        carry = n - whole;
                        if (carry > 0) buf[0] = buf[whole];
                    }
                }
                catch (Exception e)
                {
                    if (_stop) break;
                    _state = "no Mac: " + Short(e);
                    Debug.Log("MacSpeaker: " + _state);
                    lock (_lock) { _count = 0; _read = _write = 0; _primed = false; }
                    Thread.Sleep(1000);
                }
                finally { client?.Dispose(); }
            }
        }

        private static string Short(Exception e)
        {
            var m = e is AggregateException a && a.InnerException != null ? a.InnerException.Message : e.Message;
            return m.Length > 40 ? m.Substring(0, 39) + "…" : m;
        }

        private static string ReadLine(NetworkStream s)
        {
            var sb = new StringBuilder();
            int b;
            while ((b = s.ReadByte()) >= 0 && b != '\n') sb.Append((char)b);
            return sb.ToString();
        }

        private void Push(byte[] bytes, int count)
        {
            lock (_lock)
            {
                for (var i = 0; i + 1 < count; i += 2)
                {
                    var v = (short)(bytes[i] | (bytes[i + 1] << 8)) / 32768f;
                    _ring[_write] = v;
                    _write = (_write + 1) % _ring.Length;
                    if (_count < _ring.Length) _count++;
                    else _read = (_read + 1) % _ring.Length; // full: drop the oldest, keep latency bounded
                }
                if (!_primed && _count >= (int)(prebufferSeconds * Rate * Channels)) _primed = true;
            }
        }

        private void OnRead(float[] data)
        {
            var sum = 0f;
            lock (_lock)
            {
                if (!_primed || _count < data.Length)
                {
                    Array.Clear(data, 0, data.Length);
                    if (_primed && _count < data.Length) _primed = false; // underrun: refill before resuming
                    _level = 0;
                    return;
                }
                for (var i = 0; i < data.Length; i++)
                {
                    var v = _ring[_read];
                    data[i] = v;
                    sum += v * v;
                    _read = (_read + 1) % _ring.Length;
                }
                _count -= data.Length;
            }
            _level = Mathf.Clamp01(Mathf.Sqrt(sum / data.Length) * 4f);
        }
    }
}
