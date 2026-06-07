using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    public class DigitalTwinOperationRecorder : MonoBehaviour
    {
        [Serializable]
        private class LogEntry
        {
            public string type;
            public long timeMs;
            public string payload;
        }

        [Header("References")]
        [SerializeField] private DigitalTwinUdpIngress udpIngress;
        [SerializeField] private MonoBehaviour ingressBehaviour;

        [Header("Recording")]
        [SerializeField] private bool autoRecordOnEnable;
        [SerializeField] private string filePrefix = "digital_twin_log";
        [SerializeField] private bool prettyJson;

        private IDigitalTwinIngress _ingress;
        private readonly List<LogEntry> _entries = new List<LogEntry>();
        private bool _recording;
        private string _lastSavedPath = "";

        private void Awake()
        {
            if (udpIngress == null) udpIngress = FindObjectOfType<DigitalTwinUdpIngress>();
            if (ingressBehaviour == null) ingressBehaviour = FindObjectOfType<DigitalTwinJsonPoseBridge>();
            _ingress = ingressBehaviour as IDigitalTwinIngress;
        }

        private void OnEnable()
        {
            if (udpIngress != null)
            {
                udpIngress.OnJsonReceived += OnJsonReceived;
                udpIngress.OnAckSent += OnAckSent;
            }
            if (autoRecordOnEnable)
                StartRecording();
        }

        private void OnDisable()
        {
            if (udpIngress != null)
            {
                udpIngress.OnJsonReceived -= OnJsonReceived;
                udpIngress.OnAckSent -= OnAckSent;
            }
        }

        [ContextMenu("Start Recording")]
        public void StartRecording()
        {
            _entries.Clear();
            _recording = true;
        }

        [ContextMenu("Stop Recording")]
        public void StopRecording()
        {
            _recording = false;
        }

        [ContextMenu("Save Recording")]
        public void SaveRecording()
        {
            if (_entries.Count == 0)
            {
                Debug.LogWarning("[DigitalTwinRecorder] No entries to save.");
                return;
            }

            string dir = Path.Combine(Application.persistentDataPath, "digital-twin-logs");
            Directory.CreateDirectory(dir);
            string file = filePrefix + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".jsonl";
            string path = Path.Combine(dir, file);

            using (var sw = new StreamWriter(path, false))
            {
                for (int i = 0; i < _entries.Count; i++)
                    sw.WriteLine(JsonUtility.ToJson(_entries[i], prettyJson));
            }

            _lastSavedPath = path;
            Debug.Log("[DigitalTwinRecorder] Saved: " + path);
        }

        [ContextMenu("Replay Last Recording")]
        public void ReplayLastRecording()
        {
            if (string.IsNullOrEmpty(_lastSavedPath) || !File.Exists(_lastSavedPath))
            {
                Debug.LogWarning("[DigitalTwinRecorder] Last recording file missing.");
                return;
            }
            ReplayFromFile(_lastSavedPath);
        }

        public void ReplayFromFile(string path)
        {
            if (_ingress == null)
                _ingress = ingressBehaviour as IDigitalTwinIngress;
            if (_ingress == null)
            {
                Debug.LogWarning("[DigitalTwinRecorder] Ingress not set.");
                return;
            }

            if (!File.Exists(path))
            {
                Debug.LogWarning("[DigitalTwinRecorder] File not found: " + path);
                return;
            }

            int replayed = 0;
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var entry = JsonUtility.FromJson<LogEntry>(line);
                if (entry == null || string.IsNullOrEmpty(entry.payload))
                    continue;
                if (entry.type != "ingress")
                    continue;
                _ingress.TryApplyDigitalTwinJson(entry.payload);
                replayed++;
            }
            Debug.Log("[DigitalTwinRecorder] Replay complete. Messages: " + replayed);
        }

        private void OnJsonReceived(string json)
        {
            if (!_recording || string.IsNullOrEmpty(json))
                return;
            _entries.Add(new LogEntry
            {
                type = "ingress",
                timeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                payload = json
            });
        }

        private void OnAckSent(string ackJson)
        {
            if (!_recording || string.IsNullOrEmpty(ackJson))
                return;
            _entries.Add(new LogEntry
            {
                type = "ack",
                timeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                payload = ackJson
            });
        }
    }
}
