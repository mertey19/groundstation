using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    public class DigitalTwinOperationRecorder : MonoBehaviour
    {
        [Serializable]
        private class LogEntry { public string type; public long timeMs; public string payload; }
        [SerializeField] private DigitalTwinUdpIngress udpIngress;
        [SerializeField] private MonoBehaviour ingressBehaviour;
        [SerializeField] private bool autoRecordOnEnable;
        [SerializeField] private string filePrefix = "digital_twin_log";
        [SerializeField, Range(0.25f, 4f)] private float replaySpeed = 1f;
        [SerializeField, Range(0.25f, 10f)] private float flushIntervalSeconds = 1f;
        [SerializeField] private int maxQueuedLines = 2000;
        [SerializeField] private int maxFileBytes = 32 * 1024 * 1024;
        private readonly List<LogEntry> _entries = new List<LogEntry>();
        private readonly List<LogEntry> _playback = new List<LogEntry>();
        private readonly ConcurrentQueue<string> _writeQueue = new ConcurrentQueue<string>();
        private DigitalTwinJsonPoseBridge _bridge;
        private bool _recording;
        private string _lastSavedPath = "";
        private int _cursor;
        private long _origin;
        private float _startedAt;
        private float _nextFlush;
        private Thread _writer;
        private StreamWriter _stream;
        private volatile bool _writeRunning;
        private volatile string _ioError = "";
        private long _bytesWritten;
        private int _dropped;
        public int ReplayApplied { get; private set; }
        public int ReplayRejected { get; private set; }
        public bool IsReplaying { get; private set; }
        public string LastReplayInfo { get; private set; } = "";
        public string LastSavedPath => _lastSavedPath;
        public int DroppedRecordCount => _dropped;
        public string LastIoError => _ioError;
        private void Awake()
        {
            if (udpIngress == null) udpIngress = FindObjectOfType<DigitalTwinUdpIngress>();
            ResolveBridge();
        }
        private void ResolveBridge()
        {
            if (ingressBehaviour == null) ingressBehaviour = FindObjectOfType<DigitalTwinJsonPoseBridge>();
            _bridge = ingressBehaviour as DigitalTwinJsonPoseBridge;
        }
        private void OnEnable()
        {
            if (udpIngress != null)
            {
                udpIngress.OnAcceptedJson += OnJsonReceived;
                udpIngress.OnAckSent += OnAckSent;
            }
            if (autoRecordOnEnable) StartRecording();
        }
        private void OnDisable()
        {
            if (udpIngress != null)
            {
                udpIngress.OnAcceptedJson -= OnJsonReceived;
                udpIngress.OnAckSent -= OnAckSent;
            }
            StopReplay();
            CloseWriter(true);
        }
        private void Update()
        {
            if (IsReplaying) AdvanceReplay((Time.unscaledTime - _startedAt) * replaySpeed);
            if (_recording && !string.IsNullOrEmpty(_ioError))
            {
                _recording = false;
                LastReplayInfo = _ioError;
            }
            if (_recording && Time.unscaledTime >= _nextFlush)
            {
                _nextFlush = Time.unscaledTime + Mathf.Max(0.25f, flushIntervalSeconds);
                Volatile.Write(ref _flushRequested, 1);
            }
        }
        private int _flushRequested;
        [ContextMenu("Start Recording")]
        public void StartRecording()
        {
            CloseWriter(true);
            _entries.Clear();
            _dropped = 0;
            _ioError = "";
            _bytesWritten = 0;
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "digital-twin-logs");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, Path.GetFileName(filePrefix) + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".jsonl");
                _stream = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = false };
                _lastSavedPath = path;
                _writeRunning = true;
                _writer = new Thread(WriteLoop) { IsBackground = true, Name = "Simurgh recorder" };
                _writer.Start();
                _recording = true;
                LastReplayInfo = "Kayıt dosyasına yazılıyor: " + path;
            }
            catch (Exception e)
            {
                _recording = false;
                LastReplayInfo = "Kayıt dosyası açılamadı: " + e.Message;
            }
        }
        [ContextMenu("Stop Recording")]
        public void StopRecording()
        {
            _recording = false;
            CloseWriter(true);
        }
        [ContextMenu("Save Recording")]
        public void SaveRecording()
        {
            if (_recording && !string.IsNullOrEmpty(_lastSavedPath))
            {
                Volatile.Write(ref _flushRequested, 1);
                LastReplayInfo = "Artımlı kayıt: " + _lastSavedPath + " (son satır güç kaybında kaybolabilir)";
                return;
            }
            if (_entries.Count == 0) { LastReplayInfo = "Kaydedilecek veri yok"; return; }
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "digital-twin-logs");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, Path.GetFileName(filePrefix) + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".jsonl");
                using (var writer = new StreamWriter(path, false))
                    foreach (var entry in _entries) writer.WriteLine(Mapbox.Json.JsonConvert.SerializeObject(entry));
                _lastSavedPath = path; LastReplayInfo = "Kayıt kaydedildi: " + path;
            }
            catch (Exception e) { LastReplayInfo = "Kayıt yazılamadı: " + e.Message; }
        }
        [ContextMenu("Replay Last Recording")]
        public void ReplayLastRecording() => ReplayFromFile(_lastSavedPath);
        public void ReplayFromFile(string path)
        {
            ResolveBridge();
            if (_bridge == null || !File.Exists(path)) { LastReplayInfo = "Kayıt dosyası veya veri köprüsü bulunamadı"; return; }
            StopReplay();
            _playback.Clear(); ReplayApplied = ReplayRejected = _cursor = 0;
            try
            {
                if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new IOException("Kayıt dosyası 64 MB sınırını aşıyor");
                long previous = -1;
                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    LogEntry entry;
                    try { entry = Mapbox.Json.JsonConvert.DeserializeObject<LogEntry>(line); }
                    catch { ReplayRejected++; continue; }
                    if (entry == null || entry.type != "ingress") continue;
                    if (entry.timeMs < previous || string.IsNullOrWhiteSpace(entry.payload)) { ReplayRejected++; continue; }
                    previous = entry.timeMs; _playback.Add(entry);
                    if (_playback.Count > 100000) throw new IOException("Kayıt mesaj sınırını aşıyor");
                }
            }
            catch (Exception e) { _playback.Clear(); LastReplayInfo = "Kayıt okunamadı: " + e.Message; return; }
            if (_playback.Count == 0) { LastReplayInfo = "Oynatılabilir mesaj yok"; return; }
            var commands = FindObjectOfType<DigitalTwinCommandEgress>();
            if (commands != null) commands.CancelPending("Kayıt oynatma başladı; canlı işlem iptal edildi");
            _bridge.BeginReplay();
            _origin = _playback[0].timeMs; _startedAt = Time.unscaledTime; IsReplaying = true;
            AdvanceReplay(0);
        }
        public void AdvanceReplay(float elapsedSeconds)
        {
            if (!IsReplaying || _bridge == null) return;
            int budget = 200;
            while (_cursor < _playback.Count && budget-- > 0 && _playback[_cursor].timeMs - _origin <= elapsedSeconds * 1000d)
            {
                bool applied = _bridge.TryApplyReplayJson(_playback[_cursor++].payload);
                if (applied) ReplayApplied++; else ReplayRejected++;
            }
            if (_cursor == _playback.Count)
            {
                IsReplaying = false;
                LastReplayInfo = "Kayıt tamamlandı · " + ReplayApplied + " uygulandı, " + ReplayRejected + " reddedildi";
            }
            else LastReplayInfo = "Kayıt oynatılıyor · " + ReplayApplied + "/" + _playback.Count;
        }
        [ContextMenu("Exit Replay")]
        public void StopReplay()
        {
            IsReplaying = false;
            if (_bridge != null && _bridge.IsReplaying) _bridge.EndReplay();
        }
        public void RecordPayload(string type, string payload) => Record(type, payload);
        private void OnJsonReceived(string json) => Record("ingress", json);
        private void OnAckSent(string json) => Record("ack", json);
        private void Record(string type, string payload)
        {
            if (!_recording || string.IsNullOrEmpty(payload)) return;
            var entry = new LogEntry { type = type, timeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), payload = MaskSecrets(payload) };
            if (_entries.Count < 500) _entries.Add(entry);
            if (_writeQueue.Count >= Math.Max(32, maxQueuedLines))
            {
                _dropped++;
                LastReplayInfo = "Kayıt kuyruğu doldu; satır düşürüldü";
                return;
            }
            _writeQueue.Enqueue(Mapbox.Json.JsonConvert.SerializeObject(entry));
        }
        internal static string MaskSecrets(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return payload;
            return Regex.Replace(payload, "\"authToken\"\\s*:\\s*\"[^\"]*\"", "\"authToken\":\"\"");
        }
        private void WriteLoop()
        {
            while (_writeRunning || !_writeQueue.IsEmpty)
            {
                while (_writeQueue.TryDequeue(out var line))
                {
                    try
                    {
                        if (_stream == null) return;
                        if (_bytesWritten + line.Length + 2 > Math.Max(1024, maxFileBytes))
                        {
                            _ioError = "Kayıt dosyası boyut sınırına ulaştı";
                            _writeRunning = false;
                            return;
                        }
                        _stream.WriteLine(line);
                        _bytesWritten += line.Length + 1;
                    }
                    catch (Exception e)
                    {
                        _ioError = "Kayıt diske yazılamadı: " + e.Message;
                        _writeRunning = false;
                        return;
                    }
                }
                if (Volatile.Read(ref _flushRequested) != 0)
                {
                    try { _stream?.Flush(); } catch (Exception e) { _ioError = "Kayıt flush başarısız: " + e.Message; _writeRunning = false; return; }
                    Volatile.Write(ref _flushRequested, 0);
                }
                if (_writeRunning) Thread.Sleep(15);
            }
        }
        private void CloseWriter(bool flush)
        {
            _writeRunning = false;
            var writer = _writer;
            _writer = null;
            if (writer != null && writer.IsAlive)
                try { writer.Join(500); } catch { }
            if (flush)
            {
                try { _stream?.Flush(); } catch { }
            }
            try { _stream?.Dispose(); } catch { }
            _stream = null;
            while (_writeQueue.TryDequeue(out _)) { }
        }
    }
}
