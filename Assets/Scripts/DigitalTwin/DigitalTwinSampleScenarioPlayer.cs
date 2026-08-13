using System.Collections;
using System.Collections.Generic;
using System.IO;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// Generates and replays a small Digital Twin scenario stream.
    /// Useful for demoing Digital Twin flow without UDP sender.
    /// </summary>
    public class DigitalTwinSampleScenarioPlayer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private MonoBehaviour ingressBehaviour;
        [SerializeField] private AbstractMap abstractMap;

        [Header("Playback")]
        [SerializeField] private bool autoPlayOnStart;
        [SerializeField] private float autoPlayDelaySeconds = 1.0f;
        [SerializeField] private float intervalSeconds = 0.35f;
        [SerializeField] private int sampleCount = 28;
        [SerializeField] private bool loop;
        [SerializeField] private TextAsset jsonlScenarioAsset;
        [SerializeField] private bool preferJsonlAssetWhenAssigned = true;
        [SerializeField] private bool requireJsonlScenario = true;
        [SerializeField] private bool autoLoadJsonlFromResources = true;
        [SerializeField] private string resourcesJsonlPath = "DigitalTwin/sample-campus";
        [SerializeField] private bool recenterLoadedJsonlToMapCenter = false;
        [SerializeField] private float recenterMinDeltaDeg = 0.000001f;

        [Header("Message Identity")]
        [SerializeField] private string sourceId = "demo-google-earth";
        [SerializeField] private string authToken = "simurgh-2026";
        [SerializeField] private string vehicleType = "uav";

        [Header("Path")]
        [SerializeField] private float baseAltitudeM = 42f;
        [SerializeField] private float radiusLatDeg = 0.00040f;
        [SerializeField] private float radiusLonDeg = 0.00050f;
        [SerializeField] private Vector2 fallbackCenterLatLon = new Vector2(39.8719f, 32.7302f);

        private IDigitalTwinIngress _ingress;
        private Coroutine _playRoutine;
        private long _sequence;
        private string _jsonlFallbackContent = "";

        private void Awake()
        {
            // Campus demo JSONL should keep original geo coordinates.
            if (!string.IsNullOrWhiteSpace(resourcesJsonlPath) &&
                resourcesJsonlPath.ToLowerInvariant().Contains("sample-campus"))
            {
                recenterLoadedJsonlToMapCenter = false;
            }

            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (ingressBehaviour == null) ingressBehaviour = FindObjectOfType<DigitalTwinJsonPoseBridge>();
            if (jsonlScenarioAsset == null && autoLoadJsonlFromResources && !string.IsNullOrWhiteSpace(resourcesJsonlPath))
                jsonlScenarioAsset = Resources.Load<TextAsset>(resourcesJsonlPath);
            if (jsonlScenarioAsset == null)
                TryLoadJsonlFromFileSystem();
            _ingress = ingressBehaviour as IDigitalTwinIngress;
            _sequence = System.Math.Max(1000L, (long)(Time.realtimeSinceStartup * 1000f));
        }

        private void Start()
        {
            if (!autoPlayOnStart) return;
            PlaySampleScenario(autoPlayDelaySeconds);
        }

        [ContextMenu("Play Sample Scenario")]
        public void PlaySampleScenario()
        {
            PlaySampleScenario(0f);
        }

        [ContextMenu("Play Assigned JSONL Scenario")]
        public void PlayAssignedJsonlScenario()
        {
            if (jsonlScenarioAsset == null)
            {
                Debug.LogWarning("[DigitalTwinSample] jsonlScenarioAsset not assigned.");
                return;
            }
            StopSampleScenario();
            _playRoutine = StartCoroutine(PlayRoutineFromFrames(ParseJsonlFrames(jsonlScenarioAsset.text), 0f));
        }

        public void PlaySampleScenario(float delaySeconds)
        {
            StopSampleScenario();
            _playRoutine = StartCoroutine(PlayRoutine(Mathf.Max(0f, delaySeconds)));
        }

        [ContextMenu("Stop Sample Scenario")]
        public void StopSampleScenario()
        {
            if (_playRoutine != null)
            {
                StopCoroutine(_playRoutine);
                _playRoutine = null;
            }
        }

        private IEnumerator PlayRoutine(float delay)
        {
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);

            do
            {
                var frames = BuildFrames();
                yield return PlayRoutineFromFrames(frames, 0f);
            } while (loop);

            _playRoutine = null;
        }

        private IEnumerator PlayRoutineFromFrames(List<string> frames, float delay)
        {
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);

            if (frames == null || frames.Count == 0)
            {
                Debug.LogWarning("[DigitalTwinSample] No frames to play.");
                yield break;
            }

            for (int i = 0; i < frames.Count; i++)
            {
                if (_ingress == null) _ingress = ingressBehaviour as IDigitalTwinIngress;
                if (_ingress != null)
                    _ingress.TryApplyDigitalTwinJson(frames[i]);

                yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, intervalSeconds));
            }
        }

        private List<string> BuildFrames()
        {
            if (preferJsonlAssetWhenAssigned && jsonlScenarioAsset != null)
            {
                var parsed = ParseJsonlFrames(jsonlScenarioAsset.text);
                if (parsed.Count > 0)
                {
                    if (recenterLoadedJsonlToMapCenter)
                        parsed = RecenterFramesToCurrentMap(parsed);
                    Debug.Log("[DigitalTwinSample] Playing JSONL frames: " + parsed.Count);
                    return parsed;
                }

                if (requireJsonlScenario)
                {
                    Debug.LogError("[DigitalTwinSample] JSONL assigned but 0 frame parsed. Fallback disabled.");
                    return new List<string>();
                }
            }

            if (preferJsonlAssetWhenAssigned && !string.IsNullOrWhiteSpace(_jsonlFallbackContent))
            {
                var parsed = ParseJsonlFrames(_jsonlFallbackContent);
                if (parsed.Count > 0)
                {
                    if (recenterLoadedJsonlToMapCenter)
                        parsed = RecenterFramesToCurrentMap(parsed);
                    Debug.Log("[DigitalTwinSample] Playing JSONL frames (filesystem fallback): " + parsed.Count);
                    return parsed;
                }
            }

            if (requireJsonlScenario)
            {
                Debug.LogError("[DigitalTwinSample] requireJsonlScenario is ON and no valid JSONL is available.");
                return new List<string>();
            }

            Vector2 center = ResolveCenterLatLon();
            int count = Mathf.Max(6, sampleCount);
            var frames = new List<string>(count);

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / (count - 1);
                float angle = t * Mathf.PI * 2f;
                float lat = center.x + Mathf.Sin(angle) * radiusLatDeg;
                float lon = center.y + Mathf.Cos(angle) * radiusLonDeg;
                float yaw = Mathf.Repeat((angle * Mathf.Rad2Deg) + 90f, 360f);

                var msg = new DigitalTwinMessageV1
                {
                    schemaVersion = DigitalTwinJsonSchema.Version1,
                    sequenceId = ++_sequence,
                    timestampMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    sourceId = sourceId,
                    authToken = authToken,
                    vehicleType = vehicleType,
                    missionPhase = t < 0.5f ? "scan" : "inspect",
                    pose = new TwinPoseBlock
                    {
                        latitude = lat,
                        longitude = lon,
                        altitudeM = baseAltitudeM + Mathf.Sin(angle * 2f) * 3f,
                        yawDeg = yaw,
                        pitchDeg = 0f,
                        rollDeg = 0f
                    },
                    telemetry = new TwinTelemetryBlock
                    {
                        altitudeM = baseAltitudeM,
                        speedMps = 7.8f + Mathf.Sin(angle * 1.5f) * 1.2f,
                        mode = "AUTO",
                        waypointIndex = Mathf.Clamp(i / 4, 0, 99),
                        hopCount = 1,
                        signalDbm = -55f + Mathf.Sin(angle) * 4f,
                        snrDb = 20f + Mathf.Cos(angle) * 3f,
                        latencyMs = 35f + Mathf.Abs(Mathf.Sin(angle * 1.3f)) * 12f,
                        packetLossPercent = 0.4f + Mathf.Abs(Mathf.Cos(angle * 1.7f)) * 0.6f
                    },
                    mission = new TwinMissionDelta
                    {
                        phase = t < 0.5f ? "scan" : "inspect",
                        status = "running",
                        activeVehicle = vehicleType,
                        warning = "",
                        note = t < 0.5f ? "Survey path sweep" : "Target verification"
                    },
                    ackRequested = false
                };

                if (i == 0)
                {
                    msg.route = BuildRouteAround(center);
                    msg.routeMode = "replace";
                    msg.replaceRoute = true;
                }

                frames.Add(JsonUtility.ToJson(msg));
            }

            return frames;
        }

        private TwinRouteBlock BuildRouteAround(Vector2 center)
        {
            float latR = radiusLatDeg * 0.9f;
            float lonR = radiusLonDeg * 0.9f;
            float alt = baseAltitudeM + 6f;

            return new TwinRouteBlock
            {
                waypoints = new[]
                {
                    new TwinRouteWaypoint { index = 0, operation = "upsert", latitude = center.x - latR, longitude = center.y - lonR, altitudeM = alt },
                    new TwinRouteWaypoint { index = 1, operation = "upsert", latitude = center.x + latR, longitude = center.y - lonR, altitudeM = alt },
                    new TwinRouteWaypoint { index = 2, operation = "upsert", latitude = center.x + latR, longitude = center.y + lonR, altitudeM = alt },
                    new TwinRouteWaypoint { index = 3, operation = "upsert", latitude = center.x - latR, longitude = center.y + lonR, altitudeM = alt }
                }
            };
        }

        private Vector2 ResolveCenterLatLon()
        {
            if (abstractMap != null)
            {
                Vector2d c = abstractMap.CenterLatitudeLongitude;
                if (!double.IsNaN(c.x) && !double.IsNaN(c.y))
                    return new Vector2((float)c.x, (float)c.y);
            }
            return fallbackCenterLatLon;
        }

        private List<string> ParseJsonlFrames(string content)
        {
            var frames = new List<string>();
            if (string.IsNullOrWhiteSpace(content)) return frames;

            string[] lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

                // Recorder JSONL line: { "type":"ingress", "payload":"{...}" }
                if (line.Contains("\"payload\""))
                {
                    var entry = JsonUtility.FromJson<ReplayLogEntry>(line);
                    if (entry != null && !string.IsNullOrEmpty(entry.payload) &&
                        (string.IsNullOrEmpty(entry.type) || entry.type == "ingress"))
                    {
                        frames.Add(entry.payload);
                    }
                    continue;
                }

                // Direct JSONL message line.
                if (line.StartsWith("{") && line.EndsWith("}"))
                    frames.Add(line);
            }

            return frames;
        }

        private void TryLoadJsonlFromFileSystem()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            try
            {
                string assetsRoot = Application.dataPath;
                string rel = string.IsNullOrWhiteSpace(resourcesJsonlPath) ? "DigitalTwin/sample-campus" : resourcesJsonlPath;
                rel = rel.Replace("\\", "/").TrimStart('/');
                string full = Path.Combine(assetsRoot, "Resources", rel + ".jsonl");
                if (File.Exists(full))
                {
                    _jsonlFallbackContent = File.ReadAllText(full);
                    Debug.Log("[DigitalTwinSample] Loaded JSONL via filesystem fallback: " + full);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[DigitalTwinSample] Filesystem fallback load failed: " + e.Message);
            }
#endif
        }

        private List<string> RecenterFramesToCurrentMap(List<string> frames)
        {
            if (frames == null || frames.Count == 0) return frames;
            Vector2 targetCenter = ResolveCenterLatLon();

            Vector2 sourceCenter;
            if (!TryExtractFramesCenter(frames, out sourceCenter))
                return frames;

            float dLat = targetCenter.x - sourceCenter.x;
            float dLon = targetCenter.y - sourceCenter.y;
            if (Mathf.Abs(dLat) < recenterMinDeltaDeg && Mathf.Abs(dLon) < recenterMinDeltaDeg)
                return frames;

            var shifted = new List<string>(frames.Count);
            for (int i = 0; i < frames.Count; i++)
            {
                var msg = JsonUtility.FromJson<DigitalTwinMessageV1>(frames[i]);
                if (msg == null)
                {
                    shifted.Add(frames[i]);
                    continue;
                }

                ApplyLatLonOffset(msg, dLat, dLon);
                shifted.Add(JsonUtility.ToJson(msg));
            }

            return shifted;
        }

        private static bool TryExtractFramesCenter(List<string> frames, out Vector2 center)
        {
            for (int i = 0; i < frames.Count; i++)
            {
                var msg = JsonUtility.FromJson<DigitalTwinMessageV1>(frames[i]);
                if (msg == null) continue;
                if (TryExtractMessageCenter(msg, out center))
                    return true;
            }
            center = default;
            return false;
        }

        private static bool TryExtractMessageCenter(DigitalTwinMessageV1 msg, out Vector2 center)
        {
            if (msg != null && msg.route != null && msg.route.waypoints != null && msg.route.waypoints.Length > 0)
            {
                float latSum = 0f;
                float lonSum = 0f;
                int count = 0;
                for (int i = 0; i < msg.route.waypoints.Length; i++)
                {
                    latSum += msg.route.waypoints[i].latitude;
                    lonSum += msg.route.waypoints[i].longitude;
                    count++;
                }
                if (count > 0)
                {
                    center = new Vector2(latSum / count, lonSum / count);
                    return true;
                }
            }

            if (msg != null && msg.pose != null)
            {
                center = new Vector2((float)msg.pose.latitude, (float)msg.pose.longitude);
                return true;
            }

            if (msg != null && msg.slamPose != null)
            {
                center = new Vector2((float)msg.slamPose.latitude, (float)msg.slamPose.longitude);
                return true;
            }

            center = default;
            return false;
        }

        private static void ApplyLatLonOffset(DigitalTwinMessageV1 msg, float dLat, float dLon)
        {
            if (msg == null) return;

            if (msg.pose != null)
            {
                msg.pose.latitude += dLat;
                msg.pose.longitude += dLon;
            }

            if (msg.slamPose != null)
            {
                msg.slamPose.latitude += dLat;
                msg.slamPose.longitude += dLon;
            }

            if (msg.route != null && msg.route.waypoints != null)
            {
                for (int i = 0; i < msg.route.waypoints.Length; i++)
                {
                    msg.route.waypoints[i].latitude += dLat;
                    msg.route.waypoints[i].longitude += dLon;
                }
            }

            if (msg.obstacles != null)
            {
                for (int i = 0; i < msg.obstacles.Length; i++)
                {
                    msg.obstacles[i].latitude += dLat;
                    msg.obstacles[i].longitude += dLon;
                }
            }

            if (msg.targets != null)
            {
                for (int i = 0; i < msg.targets.Length; i++)
                {
                    msg.targets[i].latitude += dLat;
                    msg.targets[i].longitude += dLon;
                }
            }

            if (msg.voxelCells != null)
            {
                for (int i = 0; i < msg.voxelCells.Length; i++)
                {
                    msg.voxelCells[i].latitude += dLat;
                    msg.voxelCells[i].longitude += dLon;
                }
            }
        }

        [System.Serializable]
        private class ReplayLogEntry
        {
            public string type;
            public string payload;
        }
    }
}
