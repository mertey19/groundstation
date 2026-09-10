using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    public sealed class LiveMapReceiver : MonoBehaviour
    {
        [SerializeField] private int listenPort = 19093;
        [SerializeField] private string authToken = "simurgh-2026";
        public LiveMapSnapshot Current { get; private set; }
        public bool IsFresh => Current != null && LiveMapProtocol.Fresh(Current.Header.capturedAtMs, LiveMapProtocol.NowMs);
        public bool Listening => _running;
        public string LastError => _lastError;
        public int Port => listenPort;
        public bool AllowLocalTest { get; set; } // Explicit opt-in for local integration checks only.
        public event Action<LiveMapSnapshot> Changed;
        private readonly object _gate = new object();
        private TcpListener _listener;
        private TcpClient _client;
        private Thread _worker;
        private volatile bool _running;
        private volatile string _lastError = "";
        private LiveMapSnapshot _pending;
        private string _source, _map, _frame, _kind;
        private long _sequence;
        private int _generation;

        private void OnEnable()
        {
            string configured = Environment.GetEnvironmentVariable("SIMURGH_MAP_TOKEN");
            if (!string.IsNullOrEmpty(configured)) authToken = configured;
            try
            {
                _listener = new TcpListener(IPAddress.Any, listenPort);
                _listener.Start(2); _running = true; _lastError = "";
                var listener = _listener;
                _worker = new Thread(() => Receive(listener)) { IsBackground = true, Name = "Simurgh live map" };
                _worker.Start();
            }
            catch (Exception) { _running = false; _lastError = "Canlı harita portu açılamadı: " + listenPort; }
        }
        private void OnDisable()
        {
            _running = false;
            _listener?.Stop();
            lock (_gate) _client?.Close();
            _worker?.Join(500); _worker = null;
            ResetSession();
        }
        public void ResetSession()
        {
            lock (_gate)
            {
                _generation++; _source = _map = _frame = _kind = null; _sequence = 0;
                _pending = null; Current = null; _lastError = "";
            }
            Changed?.Invoke(null);
        }
        private void Update()
        {
            LiveMapSnapshot next;
            lock (_gate) { next = _pending; _pending = null; }
            if (next == null) return;
            // Queued work must also remain fresh after a blocked/paused main thread.
            if (!LiveMapProtocol.Fresh(next.Header.capturedAtMs, LiveMapProtocol.NowMs)) return;
            Current = next; Changed?.Invoke(next);
        }
        private void Receive(TcpListener owner)
        {
            while (_running && ReferenceEquals(owner, _listener))
            {
                TcpClient client = null;
                try
                {
                    client = owner.AcceptTcpClient();
                    int generation;
                    lock (_gate) { _client = client; generation = _generation; }
                    client.ReceiveTimeout = 1500; client.SendTimeout = 1500;
                    bool local = IPAddress.IsLoopback(((IPEndPoint)client.Client.RemoteEndPoint).Address);
                    using (var stream = new BufferedStream(client.GetStream(), 8192))
                    {
                        var snapshot = LiveMapProtocol.ReadSnapshot(stream, authToken, local && AllowLocalTest);
                        lock (_gate)
                        {
                            var h = snapshot.Header;
                            if (generation != _generation || !_running || !ReferenceEquals(owner, _listener)) throw new InvalidDataException("Harita oturumu değişti.");
                            if (_source != null && (_source != h.sourceId || _map != h.mapId || _frame != h.frameId || _kind != h.dataKind))
                                throw new InvalidDataException("Başka harita oturumu geldi. Yeni harita için oturumu sıfırla.");
                            if (h.sequence <= _sequence) throw new InvalidDataException("Eski veya yinelenen harita sürümü.");
                            _source = h.sourceId; _map = h.mapId; _frame = h.frameId; _kind = h.dataKind; _sequence = h.sequence;
                            _pending = snapshot; _lastError = "";
                        }
                        // 'received' means validated and queued, not rendered or flown.
                        byte[] ack = Encoding.UTF8.GetBytes("{\"type\":\"received\",\"sequence\":" + snapshot.Header.sequence + "}\n");
                        stream.Write(ack, 0, ack.Length); stream.Flush();
                    }
                }
                catch (Exception ex)
                {
                    if (_running && ReferenceEquals(owner, _listener)) _lastError = ex is InvalidDataException ? ex.Message : "Harita aktarımı kesildi veya geçersiz paket alındı.";
                }
                finally
                {
                    client?.Close();
                    lock (_gate) if (_client == client) _client = null;
                }
            }
        }

        public string ExportPly(string directory = null)
        {
            var map = Current;
            if (map == null) throw new InvalidOperationException("Kaydedilecek ölçülmüş harita yok.");
            directory = directory ?? Path.Combine(Application.persistentDataPath, "LiveMaps");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "map_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".ply");
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                writer.WriteLine("ply\nformat ascii 1.0\ncomment Simurgh registered point cloud; ROS map x y z-up metres");
                writer.WriteLine("comment source " + map.Header.sourceId + " map " + map.Header.mapId + " kind " + map.Header.dataKind);
                writer.WriteLine("element vertex " + map.Positions.Length + "\nproperty float x\nproperty float y\nproperty float z\nproperty uchar red\nproperty uchar green\nproperty uchar blue\nend_header");
                for (int i = 0; i < map.Positions.Length; i++)
                {
                    Vector3 p = map.Positions[i]; Color32 c = map.Colors[i];
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:R} {1:R} {2:R} {3} {4} {5}", p.x, p.z, p.y, c.r, c.g, c.b));
                }
            }
            File.WriteAllText(Path.ChangeExtension(path, ".json"), Mapbox.Json.JsonConvert.SerializeObject(map.Header, Mapbox.Json.Formatting.Indented));
            return path;
        }
    }
}
