using System.Collections.Generic;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using UnityEngine;
using GroundStation.Routes;

namespace GroundStation.DigitalTwin
{
    public class DigitalTwinMissionEngine : MonoBehaviour
    {
        [System.Serializable]
        public struct MissionEvent
        {
            public string eventType;
            public string details;
            public float timeSeconds;
        }

        [System.Serializable]
        public struct MeshSample
        {
            public float timeSeconds;
            public int hopCount;
            public float linkQualityPercent;
            public float latencyMs;
            public float packetLossPercent;
        }

        [Header("References")]
        [SerializeField] private AbstractMap abstractMap;
        [SerializeField] private Transform obstacleRoot;
        [SerializeField] private Transform targetRoot;
        [SerializeField] private Transform voxelRoot;
        [SerializeField] private DigitalTwinRoverAdapter roverAdapter;

        [Header("Visual Prefabs (optional)")]
        [SerializeField] private GameObject obstaclePrefab;
        [SerializeField] private GameObject targetPrefab;
        [SerializeField] private GameObject voxelPrefab;

        [Header("Visual Settings")]
        [SerializeField] private float obstacleDefaultSize = 2.5f;
        [SerializeField] private float targetDefaultSize = 2.0f;
        [SerializeField] private float voxelDefaultSize = 1.0f;
        [SerializeField] private float markerYOffset = 0.35f;
        [SerializeField] private float staleVehicleTimeoutSeconds = 2.0f;
        [SerializeField] private float lowLinkQualityThreshold = 35f;
        [SerializeField] private float emergencyLinkQualityThreshold = 20f;
        [SerializeField] private float roverAvoidanceClearanceMeters = 6f;
        [SerializeField] private bool enableRoverReplan = true;
        [SerializeField] private int maxEventLogEntries = 120;
        [SerializeField] private int maxMeshSamples = 180;

        private readonly Dictionary<string, GameObject> _obstacles = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, GameObject> _targets = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, GameObject> _voxels = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, float> _vehicleLastSeen = new Dictionary<string, float>();
        private readonly List<MissionEvent> _eventLog = new List<MissionEvent>();
        private readonly List<MeshSample> _meshHistory = new List<MeshSample>();
        private readonly List<Vector3> _lastRoverDetour = new List<Vector3>();
        private string _currentPhase = "";
        private string _phaseStatus = "";
        private int _lastHopCount = -1;
        private bool _twinOnlyModeRecommended;
        private bool _emergencyTwinOnly;
        private bool _lastRelayActive;

        // Hedef (karekod) takibi: hakem kaniti icin okunan icerik + basari sayaci.
        private readonly Dictionary<string, bool> _targetReachedState = new Dictionary<string, bool>();
        private readonly Dictionary<string, string> _targetContents = new Dictionary<string, string>();
        private readonly List<string> _targetOrder = new List<string>();

        // Leak onleme: entity'ler TEK paylasilan materyali kullanir; renkler
        // MaterialPropertyBlock ile verilir (instanced material sizintisi + GC yok).
        private static Material _sharedEntityMaterial;
        private static MaterialPropertyBlock _sharedMpb;

        public string CurrentPhase => _currentPhase;
        public string CurrentPhaseStatus => _phaseStatus;
        public int ObstacleCount => _obstacles.Count;
        public int TargetCount => _targets.Count;
        public int VoxelCount => _voxels.Count;
        public bool TwinOnlyModeRecommended => _twinOnlyModeRecommended;
        public bool EmergencyTwinOnly => _emergencyTwinOnly;
        public IReadOnlyList<MissionEvent> EventLog => _eventLog;
        public IReadOnlyList<Vector3> LastRoverDetour => _lastRoverDetour;
        public bool HasRoverDetour => _lastRoverDetour.Count >= 3;

        /// <summary>Mesh ornek gecmisi (sparkline/trend gorsellestirmesi icin).</summary>
        public IReadOnlyList<MeshSample> MeshHistory => _meshHistory;

        public struct TargetInfo
        {
            public string id;
            public bool reached;
            public string content;
        }

        public int TargetsReachedCount { get; private set; }
        public int TargetsTotalCount => _targetOrder.Count;

        /// <summary>Hedefleri ekleme sirasiyla doldurur (karekod listesi paneli icin).</summary>
        public void GetTargetInfos(List<TargetInfo> buffer)
        {
            if (buffer == null) return;
            buffer.Clear();
            for (int i = 0; i < _targetOrder.Count; i++)
            {
                string id = _targetOrder[i];
                bool reached;
                _targetReachedState.TryGetValue(id, out reached);
                string content;
                _targetContents.TryGetValue(id, out content);
                buffer.Add(new TargetInfo { id = id, reached = reached, content = content ?? "" });
            }
        }

        public event System.Action<MissionEvent> OnMissionEvent;

        private void Awake()
        {
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (roverAdapter == null) roverAdapter = FindObjectOfType<DigitalTwinRoverAdapter>();
            if (obstacleRoot == null)
                obstacleRoot = EnsureRoot("DigitalTwinObstacles");
            if (targetRoot == null)
                targetRoot = EnsureRoot("DigitalTwinTargets");
            if (voxelRoot == null)
                voxelRoot = EnsureRoot("DigitalTwinVoxels");
        }

        public bool ApplyMessage(DigitalTwinMessageV1 msg)
        {
            if (msg == null)
                return false;

            bool changed = false;
            changed |= UpdateMissionState(msg);
            changed |= UpdateVehicleHeartbeat(msg);
            changed |= UpdateAdaptiveMode(msg);
            changed |= ApplyObstacleDeltas(msg.obstacles);
            changed |= ApplyTargetDeltas(msg.targets);
            changed |= ApplyVoxelDeltas(msg.voxelCells);
            changed |= UpdateMeshHistory(msg.meshLink);
            changed |= ComputeRoverDetourIfNeeded(msg);
            return changed;
        }

        public string BuildVehicleStatusLine()
        {
            bool uavOnline = IsVehicleOnline(TwinVehicleTypes.Uav);
            bool roverOnline = IsVehicleOnline(TwinVehicleTypes.Rover);
            string streamMode = _twinOnlyModeRecommended ? "Yalnız İkiz" : "Hibrit";
            if (_emergencyTwinOnly)
                streamMode = "ACİL·Yalnız İkiz";
            return "Bağlantı: İHA " + (uavOnline ? "Çevrimiçi" : "Çevrimdışı") + " | Rover " + (roverOnline ? "Çevrimiçi" : "Çevrimdışı") + " | Akış " + streamMode;
        }

        public string BuildMeshTrendSummary()
        {
            if (_meshHistory.Count < 2)
                return "Mesh eğilimi: veri bekleniyor";
            var first = _meshHistory[0];
            var last = _meshHistory[_meshHistory.Count - 1];
            float qualityDelta = last.linkQualityPercent - first.linkQualityPercent;
            string trend = qualityDelta > 5f ? "yükseliyor" : qualityDelta < -5f ? "düşüyor" : "stabil";
            return "Mesh eğilimi: " + trend + " (" + qualityDelta.ToString("F0") + ")";
        }

        private bool IsVehicleOnline(string vehicleType)
        {
            if (!_vehicleLastSeen.TryGetValue(vehicleType, out var seenAt))
                return false;
            return Time.unscaledTime - seenAt <= Mathf.Max(0.2f, staleVehicleTimeoutSeconds);
        }

        private bool UpdateMissionState(DigitalTwinMessageV1 msg)
        {
            string nextPhase = msg.missionPhase;
            if (string.IsNullOrEmpty(nextPhase) && msg.mission != null && !string.IsNullOrEmpty(msg.mission.phase))
                nextPhase = msg.mission.phase;
            string nextStatus = msg.mission != null ? msg.mission.status : "";

            bool changed = false;
            if (!string.IsNullOrEmpty(nextPhase) && !string.Equals(_currentPhase, nextPhase, System.StringComparison.Ordinal))
            {
                _currentPhase = nextPhase;
                changed = true;
                PushEvent("phase_change", "phase=" + nextPhase);
            }
            if (!string.IsNullOrEmpty(nextStatus) && !string.Equals(_phaseStatus, nextStatus, System.StringComparison.Ordinal))
            {
                _phaseStatus = nextStatus;
                changed = true;
                PushEvent("phase_status", "status=" + nextStatus);
            }
            return changed;
        }

        private bool UpdateVehicleHeartbeat(DigitalTwinMessageV1 msg)
        {
            if (string.IsNullOrEmpty(msg.vehicleType))
                return false;

            string normalized = TwinVehicleTypes.IsRover(msg.vehicleType) ? TwinVehicleTypes.Rover : TwinVehicleTypes.Uav;
            _vehicleLastSeen[normalized] = Time.unscaledTime;
            return true;
        }

        private bool UpdateAdaptiveMode(DigitalTwinMessageV1 msg)
        {
            if (msg == null || msg.meshLink == null)
                return false;
            bool next = msg.meshLink.linkQualityPercent > 0f && msg.meshLink.linkQualityPercent < lowLinkQualityThreshold;
            if (next == _twinOnlyModeRecommended)
            {
                bool emergencyNext = msg.meshLink.linkQualityPercent > 0f && msg.meshLink.linkQualityPercent < emergencyLinkQualityThreshold;
                if (emergencyNext != _emergencyTwinOnly)
                {
                    _emergencyTwinOnly = emergencyNext;
                    PushEvent("stream_mode", emergencyNext ? "emergency_twin_only" : "normal");
                    return true;
                }
                return false;
            }
            _twinOnlyModeRecommended = next;
            _emergencyTwinOnly = msg.meshLink.linkQualityPercent > 0f && msg.meshLink.linkQualityPercent < emergencyLinkQualityThreshold;
            PushEvent("stream_mode", _twinOnlyModeRecommended ? "twin_only" : "hybrid");
            return true;
        }

        private bool ApplyObstacleDeltas(TwinObstacleDelta[] deltas)
        {
            if (deltas == null || deltas.Length == 0)
                return false;

            bool changed = false;
            foreach (var delta in deltas)
            {
                if (delta == null || string.IsNullOrEmpty(delta.id))
                    continue;

                string op = string.IsNullOrEmpty(delta.operation) ? "upsert" : delta.operation.ToLowerInvariant();
                if (op == "remove")
                {
                    changed |= RemoveEntity(_obstacles, delta.id);
                    continue;
                }

                float sev = Mathf.Clamp01(delta.severity);
                Color obsColor = Color.Lerp(new Color(1f, 0.78f, 0.22f, 1f), new Color(1f, 0.20f, 0.16f, 1f), sev);
                var go = UpsertEntity(_obstacles, obstacleRoot, obstaclePrefab, delta.id, obsColor, PrimitiveType.Sphere, true);
                if (go == null)
                    continue;
                changed = true;
                PlaceByGeo(go.transform, delta.latitude, delta.longitude, markerYOffset + 1f);
                float size = Mathf.Max(0.6f, delta.radiusM > 0.01f ? delta.radiusM * 2f : obstacleDefaultSize);
                go.transform.localScale = new Vector3(size, size, size);
                SetEntityColor(go.GetComponent<Renderer>(), obsColor, 0.35f + 0.5f * sev);
            }
            return changed;
        }

        private bool UpdateMeshHistory(TwinMeshStatus mesh)
        {
            if (mesh == null)
                return false;

            _meshHistory.Add(new MeshSample
            {
                timeSeconds = Time.unscaledTime,
                hopCount = mesh.hopCount,
                linkQualityPercent = mesh.linkQualityPercent,
                latencyMs = mesh.latencyMs,
                packetLossPercent = mesh.packetLossPercent
            });
            if (_meshHistory.Count > Mathf.Max(10, maxMeshSamples))
                _meshHistory.RemoveAt(0);

            if (_lastHopCount >= 0 && mesh.hopCount != _lastHopCount)
                PushEvent("mesh_hop_change", _lastHopCount + "->" + mesh.hopCount);
            _lastHopCount = mesh.hopCount;

            // Relay gecisi operator icin kritik an — olay gunlugune dusur (panel yakalar).
            if (mesh.relayModeActive != _lastRelayActive)
            {
                PushEvent("relay_mode_change", mesh.relayModeActive ? "RELAY aktif (IHA uzerinden)" : "dogrudan baglantiya donuldu");
                _lastRelayActive = mesh.relayModeActive;
            }
            return true;
        }

        private bool ComputeRoverDetourIfNeeded(DigitalTwinMessageV1 msg)
        {
            if (!enableRoverReplan || !TwinVehicleTypes.IsRover(msg.vehicleType) || roverAdapter == null || !roverAdapter.HasRover)
                return false;
            if (_targets.Count == 0)
                return false;

            Transform roverTr = roverAdapter.transform;
            Vector3 roverPos = roverTr.position;
            Vector3 targetPos = GetFirstTargetPosition();
            if (targetPos == Vector3.zero)
                return false;

            var nearestObstacle = FindNearestObstacle(roverPos);
            _lastRoverDetour.Clear();
            _lastRoverDetour.Add(roverPos);
            if (nearestObstacle != null)
            {
                Vector3 obsPos = nearestObstacle.transform.position;
                float dist = Vector3.Distance(roverPos, obsPos);
                if (dist < roverAvoidanceClearanceMeters * 1.6f)
                {
                    Vector3 away = (roverPos - obsPos);
                    away.y = 0f;
                    if (away.sqrMagnitude < 0.01f)
                        away = Vector3.right;
                    away.Normalize();
                    Vector3 detour = obsPos + away * roverAvoidanceClearanceMeters;
                    detour.y = roverPos.y;
                    _lastRoverDetour.Add(detour);
                    _lastRoverDetour.Add(targetPos);
                    PushEvent("rover_replan", "detour_points=3");
                    return true;
                }
            }

            _lastRoverDetour.Add(targetPos);
            return false;
        }

        private GameObject FindNearestObstacle(Vector3 origin)
        {
            GameObject best = null;
            float bestDist = float.MaxValue;
            foreach (var kv in _obstacles)
            {
                if (kv.Value == null) continue;
                float d = Vector3.Distance(origin, kv.Value.transform.position);
                if (d < bestDist)
                {
                    best = kv.Value;
                    bestDist = d;
                }
            }
            return best;
        }

        private Vector3 GetFirstTargetPosition()
        {
            foreach (var kv in _targets)
            {
                if (kv.Value != null)
                    return kv.Value.transform.position;
            }
            return Vector3.zero;
        }

        /// <summary>
        /// Dis bilesenlerin (geofence, komut kanali, operator mudahalesi) gorev olay
        /// gunlugune kayit dusmesi icin genel API.
        /// </summary>
        public void PushExternalEvent(string eventType, string details)
        {
            PushEvent(eventType, details);
        }

        private void PushEvent(string eventType, string details)
        {
            var evt = new MissionEvent
            {
                eventType = eventType,
                details = details,
                timeSeconds = Time.unscaledTime
            };
            _eventLog.Add(evt);
            if (_eventLog.Count > Mathf.Max(20, maxEventLogEntries))
                _eventLog.RemoveAt(0);
            OnMissionEvent?.Invoke(evt);
        }

        private bool ApplyTargetDeltas(TwinTargetDelta[] deltas)
        {
            if (deltas == null || deltas.Length == 0)
                return false;

            bool changed = false;
            foreach (var delta in deltas)
            {
                if (delta == null || string.IsNullOrEmpty(delta.id))
                    continue;

                string op = string.IsNullOrEmpty(delta.operation) ? "upsert" : delta.operation.ToLowerInvariant();
                if (op == "remove")
                {
                    changed |= RemoveEntity(_targets, delta.id);
                    _targetReachedState.Remove(delta.id);
                    _targetContents.Remove(delta.id);
                    _targetOrder.Remove(delta.id);
                    RecountReachedTargets();
                    continue;
                }

                // Hedef takibi: karekod icerigi + ilk "ulasildi" gecisinde olay + sayac.
                if (!_targetOrder.Contains(delta.id))
                    _targetOrder.Add(delta.id);
                if (!string.IsNullOrEmpty(delta.decodedContent))
                    _targetContents[delta.id] = delta.decodedContent;
                bool wasReached;
                _targetReachedState.TryGetValue(delta.id, out wasReached);
                if (delta.reached && !wasReached)
                {
                    _targetReachedState[delta.id] = true;
                    RecountReachedTargets();
                    string content;
                    _targetContents.TryGetValue(delta.id, out content);
                    PushEvent("target_reached", delta.id
                        + (string.IsNullOrEmpty(content) ? "" : " icerik=\"" + content + "\"")
                        + " (" + TargetsReachedCount + "/" + TargetsTotalCount + ")");
                }
                else if (!_targetReachedState.ContainsKey(delta.id))
                {
                    _targetReachedState[delta.id] = delta.reached;
                    RecountReachedTargets();
                }

                Color tColor = delta.reached ? new Color(0.25f, 0.95f, 0.35f, 1f) : new Color(0.2f, 0.8f, 1f, 1f);
                var go = UpsertEntity(_targets, targetRoot, targetPrefab, delta.id, tColor, PrimitiveType.Capsule, true);
                if (go == null)
                    continue;
                changed = true;
                float tSize = Mathf.Max(0.6f, targetDefaultSize);
                PlaceByGeo(go.transform, delta.latitude, delta.longitude, markerYOffset + tSize);
                go.transform.localScale = new Vector3(tSize * 0.55f, tSize, tSize * 0.55f);
                SetEntityColor(go.GetComponent<Renderer>(), tColor, 0.55f);
            }
            return changed;
        }

        private bool ApplyVoxelDeltas(TwinVoxelCellDelta[] deltas)
        {
            if (deltas == null || deltas.Length == 0)
                return false;

            bool changed = false;
            foreach (var delta in deltas)
            {
                if (delta == null || string.IsNullOrEmpty(delta.id))
                    continue;

                string op = string.IsNullOrEmpty(delta.operation) ? "upsert" : delta.operation.ToLowerInvariant();
                if (op == "remove")
                {
                    changed |= RemoveEntity(_voxels, delta.id);
                    continue;
                }

                float occ = Mathf.Clamp01(delta.occupancy);
                Color voxColor = Color.Lerp(new Color(0.25f, 0.72f, 1f, 1f), new Color(1f, 0.32f, 0.2f, 1f), occ);
                var go = UpsertEntity(_voxels, voxelRoot, voxelPrefab, delta.id, voxColor, PrimitiveType.Cube, true);
                if (go == null)
                    continue;
                changed = true;
                PlaceByGeo(go.transform, delta.latitude, delta.longitude, Mathf.Max(markerYOffset, delta.altitudeM));
                float size = Mathf.Max(0.2f, delta.sizeM > 0.01f ? delta.sizeM : voxelDefaultSize);
                go.transform.localScale = Vector3.one * size;
                SetEntityColor(go.GetComponent<Renderer>(), voxColor, 0.2f + 0.4f * occ);
            }
            return changed;
        }

        private void PlaceByGeo(Transform t, float lat, float lon, float yOffset)
        {
            if (t == null || abstractMap == null)
                return;
            var geo = new Vector2d(lat, lon);
            var world = abstractMap.GeoToWorldPosition(geo, true);
            if (abstractMap.Root != null)
                world.y = abstractMap.Root.position.y + yOffset;
            t.position = world;
        }

        private static bool RemoveEntity(Dictionary<string, GameObject> map, string id)
        {
            if (!map.TryGetValue(id, out var go))
                return false;
            map.Remove(id);
            if (go != null)
                Destroy(go);
            return true;
        }

        private static GameObject UpsertEntity(Dictionary<string, GameObject> map, Transform root, GameObject prefab, string id, Color color)
        {
            return UpsertEntity(map, root, prefab, id, color, PrimitiveType.Cylinder, false);
        }

        private static GameObject UpsertEntity(Dictionary<string, GameObject> map, Transform root, GameObject prefab, string id, Color color, PrimitiveType primitive, bool emissive)
        {
            if (map.TryGetValue(id, out var existing) && existing != null)
                return existing;

            GameObject go = prefab != null ? Instantiate(prefab, root) : GameObject.CreatePrimitive(primitive);
            go.name = id;
            if (root != null)
                go.transform.SetParent(root, true);

            // Mission objelerinde fiziksel collider gereksiz (sadece gorsel + transform takibi).
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = SharedEntityMaterial();
                SetEntityColor(renderer, color, emissive ? 0.55f : 0f);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            map[id] = go;
            return go;
        }

        private void RecountReachedTargets()
        {
            int n = 0;
            foreach (var kv in _targetReachedState)
                if (kv.Value) n++;
            TargetsReachedCount = n;
        }

        private static Material SharedEntityMaterial()
        {
            if (_sharedEntityMaterial != null) return _sharedEntityMaterial;
            var shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            _sharedEntityMaterial = new Material(shader) { name = "TwinEntityShared" };
            _sharedEntityMaterial.EnableKeyword("_EMISSION");
            _sharedEntityMaterial.hideFlags = HideFlags.HideAndDontSave;
            return _sharedEntityMaterial;
        }

        private static void SetEntityColor(Renderer r, Color color, float emission)
        {
            if (r == null) return;
            if (_sharedMpb == null) _sharedMpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(_sharedMpb);
            _sharedMpb.SetColor("_Color", color);
            _sharedMpb.SetColor("_EmissionColor", new Color(color.r, color.g, color.b) * Mathf.Max(0f, emission));
            r.SetPropertyBlock(_sharedMpb);
        }

        private Transform EnsureRoot(string rootName)
        {
            var existing = GameObject.Find(rootName);
            if (existing != null)
                return existing.transform;
            var go = new GameObject(rootName);
            return go.transform;
        }
    }
}
