using System;
using System.Collections.Generic;
using System.IO;
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
        private readonly List<LogEntry> _entries = new List<LogEntry>();
        private readonly List<LogEntry> _playback = new List<LogEntry>();
        private DigitalTwinJsonPoseBridge _bridge;
        private bool _recording;
        private string _lastSavedPath = "";
        private int _cursor;
        private long _origin;
        private float _startedAt;
        public int ReplayApplied { get; private set; }
        public int ReplayRejected { get; private set; }
        public bool IsReplaying { get; private set; }
        public string LastReplayInfo { get; private set; } = "";
        public string LastSavedPath => _lastSavedPath;
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
        }
        private void Update() { if (IsReplaying) AdvanceReplay((Time.unscaledTime - _startedAt) * replaySpeed); }
        [ContextMenu("Start Recording")]
        public void StartRecording() { _entries.Clear(); _recording = true; }
        [ContextMenu("Stop Recording")]
        public void StopRecording() => _recording = false;
        [ContextMenu("Save Recording")]
        public void SaveRecording()
        {
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
        // Deterministic playback clock; also used by editor regression fixtures.
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
                IsReplaying = false; // Keep REPLAY mode and its isolated snapshot until explicit exit.
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
        private void OnJsonReceived(string json) => Record("ingress", json);
        private void OnAckSent(string json) => Record("ack", json);
        private void Record(string type, string payload)
        {
            if (!_recording || string.IsNullOrEmpty(payload)) return;
            if (_entries.Count >= 100000) { _recording = false; LastReplayInfo = "Kayıt sınırına ulaşıldı; kaydı dosyaya yazın"; return; }
            _entries.Add(new LogEntry { type = type, timeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), payload = payload });
        }
    }
}

