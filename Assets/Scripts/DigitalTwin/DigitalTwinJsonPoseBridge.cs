using System.Collections.Generic;
using UnityEngine;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using GroundStation.Drone;
using GroundStation.Routes;

namespace GroundStation.DigitalTwin
{
    public enum DigitalTwinApplyStatusCode
    {
        Ok,
        InvalidJson,
        InvalidSchema,
        RejectedUnauthorized,
        RejectedOutOfOrder,
        RejectedOldTimestamp,
        RejectedFutureTimestamp,
        MapOrDroneMissing,
        ReplayActive,
        RouteApplyFailed
    }

    [System.Serializable]
    public struct DigitalTwinApplyStatus
    {
        public DigitalTwinApplyStatusCode code;
        public string message;
        public long sequenceId;
        public long timestampMs;
        public string sourceId;
    }

    public class DigitalTwinJsonPoseBridge : MonoBehaviour, IDigitalTwinIngress
    {
        [Header("Hedefler")]
        [SerializeField] private AbstractMap abstractMap;
        [SerializeField] private DroneWaypointFollower drone;
        [SerializeField] private DigitalTwinRoverAdapter roverAdapter;
        [SerializeField] private RouteManager routeManager;
        [SerializeField] private DigitalTwinRemoteState remoteState;
        [SerializeField] private DigitalTwinMissionEngine missionEngine;
        [SerializeField] private DigitalTwinImageryService imageryService;
        [SerializeField] private DigitalTwinRoverRouteView roverRouteView;

        [Header("Poz")]
        [SerializeField] private bool applyYaw = true;
        [SerializeField] private bool applyPitchRoll;
        [SerializeField] private bool smoothPoseUpdates = true;
        [SerializeField] private float positionLerpSpeed = 12f;
        [SerializeField] private float rotationLerpSpeed = 10f;

        [Header("Message Validation")]
        [SerializeField] private bool rejectOutOfOrderSequence = true;
        [SerializeField] private bool rejectOlderTimestamps = true;
        [SerializeField] private long maxFutureTimestampMs = 5000;
        [SerializeField] private bool validateAuthToken = true;
        [SerializeField] private long maxPacketAgeMs = 5000;
        private bool _localInput;
        public bool IsReplaying { get; private set; }
        public string LastAcceptedVehicle { get; private set; } = "";
        public string LastAcceptedSource { get; private set; } = "";
        public bool LastAcceptedForDisplay { get; private set; }
        [SerializeField] private string expectedAuthToken = "simurgh-2026";


        // COKLU-KAYNAK dogrulama (PDF ortak harekat fazi): IHA + Rover ayni anda kendi
        // sayaclariyla gonderir. Global tek sayac ikinci aracin TUM mesajlarini
        // "out-of-order" diye reddederdi; durum kaynak (sourceId) basina tutulur.
        private class SourceValidationState
        {
            public long lastSequenceId = -1;
            public long lastTimestampMs = -1;
            public float lastSeenAt = -1f;
        }
        private readonly Dictionary<string, SourceValidationState> _sourceStates = new Dictionary<string, SourceValidationState>();
        [Tooltip("Kaynak bu kadar sn sustuktan sonra sequence/timestamp durumu sifirlanir (gonderici yeniden baslama toleransi).")]
        [SerializeField] private float sourceResetTimeoutSeconds = 10f;
        private bool _hasTargetPose;
        private List<WaypointData> _routeBeforeReplay;
        private Vector3 _positionBeforeReplay;
        private Quaternion _rotationBeforeReplay;
        private Vector3 _roverPositionBeforeReplay;
        private Quaternion _roverRotationBeforeReplay;
        private List<Vector2d> _roverRouteBeforeReplay;
        public Vector2d LastUavGeo { get; private set; }
        public float LastUavYaw { get; private set; }
        private float _lastUavPoseAt = -1f;
        public bool HasRecentUavPose => _lastUavPoseAt >= 0f && Time.unscaledTime - _lastUavPoseAt <= 5f;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation = Quaternion.identity;
        private readonly Dictionary<string, TwinObstacleDelta> _obstacleState = new Dictionary<string, TwinObstacleDelta>();
        private readonly Dictionary<string, TwinTargetDelta> _targetState = new Dictionary<string, TwinTargetDelta>();

        public DigitalTwinApplyStatus LastApplyStatus { get; private set; }
        public event System.Action<DigitalTwinApplyStatus> OnJsonApplied;

        // PDF 3.4.4 "Golge Modu": arac GPS ile ucar, ozgun VI-SLAM arka planda calisir.
        // Yorunge karsilastirma katmani bu iki olayi dinleyip GPS/SLAM izini ayri cizer.
        public event System.Action<Vector2d> OnUavGpsPose;
        public event System.Action<Vector2d> OnUavSlamPose;

        /// <summary>Aracin konumunun su an hangi kaynaktan geldigi (GPS kaybi senaryosu gostergesi).</summary>
        public enum TwinPoseSource { None, Gps, Slam }
        public TwinPoseSource ActivePoseSource { get; private set; } = TwinPoseSource.None;
        /// <summary>Son SLAM pozunun guven degeri (0-1); -1 = veri yok.</summary>
        public float LastSlamConfidence { get; private set; } = -1f;
        public event System.Action<TwinPoseSource> OnPoseSourceChanged;

        private void SetPoseSource(TwinPoseSource source)
        {
            if (source == ActivePoseSource) return;
            ActivePoseSource = source;
            OnPoseSourceChanged?.Invoke(source);
            if (source == TwinPoseSource.Slam && remoteState != null)
                remoteState.SetWarning("GPS yok — konum VI-SLAM kestirimiyle sürdürülüyor.");
        }

        private void Awake()
        {
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (drone == null) drone = FindObjectOfType<DroneWaypointFollower>();
            if (roverAdapter == null) roverAdapter = FindObjectOfType<DigitalTwinRoverAdapter>();
            if (routeManager == null) routeManager = FindObjectOfType<RouteManager>();
            if (remoteState == null) remoteState = GetComponent<DigitalTwinRemoteState>();
            if (remoteState == null) remoteState = FindObjectOfType<DigitalTwinRemoteState>();
            if (missionEngine == null) missionEngine = FindObjectOfType<DigitalTwinMissionEngine>();
            if (imageryService == null) imageryService = FindObjectOfType<DigitalTwinImageryService>();
        }

        private void Update()
        {
            if (drone == null)
                return;

            if (_hasTargetPose && smoothPoseUpdates && HasRecentUavPose)
            {
                float dt = Time.unscaledDeltaTime;
                float posT = 1f - Mathf.Exp(-Mathf.Max(0.01f, positionLerpSpeed) * dt);
                float rotT = 1f - Mathf.Exp(-Mathf.Max(0.01f, rotationLerpSpeed) * dt);
                drone.transform.position = Vector3.Lerp(drone.transform.position, _targetPosition, posT);
                drone.transform.rotation = Quaternion.Slerp(drone.transform.rotation, _targetRotation, rotT);
            }

            // Each stream expires independently. Preserve mission/mesh state and
            // show stale telemetry explicitly instead of falling back to simulation.

        }

        public bool TryApplyDigitalTwinJson(string json)
        {
            validateAuthToken = true;
            if (IsReplaying && !_localInput)
            {
                PublishStatus(DigitalTwinApplyStatusCode.ReplayActive, "Kayıt oynatılıyor; canlı veri uygulanmadı", 0, 0, "");
                return false;
            }
            try
            {
                var root = Mapbox.Json.Linq.JObject.Parse(json ?? "");
                if (root["schemaVersion"] == null)
                {
                    PublishStatus(DigitalTwinApplyStatusCode.InvalidSchema, "Sürümlü ve doğrulanmış mesaj gerekli", 0, 0, "");
                    return false;
                }
                var msg = root.ToObject<DigitalTwinMessageV1>();
                foreach (string poseKey in new[] { "pose", "slamPose" })
                    if (root[poseKey] is Mapbox.Json.Linq.JObject pose && (pose["latitude"] == null || pose["longitude"] == null))
                        throw new System.FormatException("Poz enlem ve boylam içermeli");
                // Field initializers and missing/null blocks are preserved by Json.NET.
                if (msg.telemetry != null && root["telemetry"] is Mapbox.Json.Linq.JObject telemetry)
                {
                    if (telemetry["altitudeM"] == null || telemetry["speedMps"] == null || telemetry["mode"] == null)
                        throw new System.FormatException("Telemetri için altitudeM, speedMps ve mode gerekli");
                }
                if (root["route"] is Mapbox.Json.Linq.JObject route && route["waypoints"] is Mapbox.Json.Linq.JArray waypoints)
                    foreach (var waypoint in waypoints)
                        if ((string)waypoint["operation"] != "remove" && (waypoint["latitude"] == null || waypoint["longitude"] == null))
                            throw new System.FormatException("Waypoint enlem ve boylam içermeli");
                return ApplyMessageV1(msg);
            }
            catch (System.Exception e)
            {
                PublishStatus(DigitalTwinApplyStatusCode.InvalidJson, "Geçersiz mesaj: " + e.GetType().Name, 0, 0, "");
                return false;
            }
        }

        // Local players opt in explicitly; an untrusted sourceId can never enable this path.
        public bool TryApplySampleJson(string json)
        {
            if (IsReplaying || (!GroundStationMode.SimulationSelected && remoteState != null && remoteState.UseJsonTelemetry && !remoteState.IsSample)) return false;
            if (remoteState != null) remoteState.IsSample = true;
            _localInput = true;
            try { return TryApplyDigitalTwinJson(json); }
            finally { _localInput = false; }
        }

        public void BeginReplay()
        {
            if (IsReplaying) EndReplay();
            if (routeManager != null)
            {
                _routeBeforeReplay = new List<WaypointData>();
                if (routeManager.GetRouteData()?.waypoints != null)
                    foreach (var wp in routeManager.GetRouteData().waypoints) _routeBeforeReplay.Add(wp.CloneWithIndex(wp.index));
            }
            if (drone != null) { _positionBeforeReplay = drone.transform.position; _rotationBeforeReplay = drone.transform.rotation; }
            if (roverAdapter != null)
            {
                if (roverAdapter.RoverTransform != null)
                { _roverPositionBeforeReplay = roverAdapter.RoverTransform.position; _roverRotationBeforeReplay = roverAdapter.RoverTransform.rotation; }
                roverAdapter.ResetPoseFreshness();
            }
            _roverRouteBeforeReplay = roverRouteView != null ? roverRouteView.CopyRoute() : new List<Vector2d>();
            IsReplaying = true;
            _hasTargetPose = false;
            _lastUavPoseAt = -1f;
            foreach (var key in new List<string>(_sourceStates.Keys))
                if (key.StartsWith("replay/")) _sourceStates.Remove(key);
            if (drone != null) drone.StopRoute();
            if (remoteState != null) remoteState.SetReplay(true);
        }

        public void EndReplay()
        {
            if (IsReplaying)
            {
                if (routeManager != null && _routeBeforeReplay != null) routeManager.ReplaceRoute(_routeBeforeReplay);
                if (drone != null) drone.transform.SetPositionAndRotation(_positionBeforeReplay, _rotationBeforeReplay);
                if (roverAdapter != null && roverAdapter.RoverTransform != null)
                    roverAdapter.RoverTransform.SetPositionAndRotation(_roverPositionBeforeReplay, _roverRotationBeforeReplay);
                if (roverRouteView != null) roverRouteView.SetRoute(_roverRouteBeforeReplay);
                _routeBeforeReplay = null;
            }
            IsReplaying = false;
            _hasTargetPose = false;
            _lastUavPoseAt = -1f;
            if (remoteState != null) remoteState.SetReplay(false);
            if (roverAdapter != null) roverAdapter.ResetPoseFreshness();
        }

        public bool TryApplyReplayJson(string json)
        {
            if (!IsReplaying) return false;
            _localInput = true;
            try { return TryApplyDigitalTwinJson(json); }
            finally { _localInput = false; }
        }

        public void ClearJsonTelemetryOverride()
        {
            if (remoteState != null)
                remoteState.Clear();
        }

        private bool ApplyMessageV1(DigitalTwinMessageV1 msg)
        {
            if (!ValidateMessage(msg, out var rejectCode, out var rejectMessage))
            {
                PublishStatus(rejectCode, rejectMessage, msg.sequenceId, msg.timestampMs, msg.sourceId);
                return false;
            }

            if (!_localInput && remoteState != null) remoteState.IsSample = false;
            bool selected = remoteState == null || remoteState.IsSelectedSource(msg.vehicleType, msg.sourceId);
            bool showPose = selected && (_localInput || !GroundStationMode.SimulationSelected);
            bool applied = false;
            bool isUavPayload = TwinVehicleTypes.IsUav(msg.vehicleType);
            bool isRoverPayload = TwinVehicleTypes.IsRover(msg.vehicleType);
            if (!selected && msg.route != null)
            {
                PublishStatus(DigitalTwinApplyStatusCode.RouteApplyFailed, "Seçili olmayan kaynak aktif rotayı değiştiremez", msg.sequenceId, msg.timestampMs, msg.sourceId);
                return false;
            }

            bool shouldApplyRoute = isUavPayload
                                    && msg.route != null
                                    && msg.route.waypoints != null;
            if (shouldApplyRoute)
            {
                if (routeManager == null)
                {
                    PublishStatus(DigitalTwinApplyStatusCode.RouteApplyFailed, "Rota var ama RouteManager bagli degil", msg.sequenceId, msg.timestampMs, msg.sourceId);
                    return false;
                }

                var routeMode = ResolveRouteMode(msg);
                bool routeOk = routeMode == "append"
                    ? ApplyRouteAppend(msg.route)
                    : routeMode == "patch"
                        ? ApplyRoutePatch(msg.route)
                        : ApplyRouteReplace(msg.route);

                if (!routeOk)
                {
                    PublishStatus(DigitalTwinApplyStatusCode.RouteApplyFailed, "Rota uygulanamadi", msg.sequenceId, msg.timestampMs, msg.sourceId);
                    return false;
                }

                applied = true;
            }

            if (showPose && isUavPayload && (msg.pose != null || msg.slamPose != null))
            {
                if (abstractMap == null || drone == null)
                {
                    PublishStatus(DigitalTwinApplyStatusCode.MapOrDroneMissing, "Pose var ama map veya drone bagli degil", msg.sequenceId, msg.timestampMs, msg.sourceId);
                    return false;
                }

                // Gorev gercegi (PDF 3.4.4): arac GPS ile ucar, VI-SLAM "golge modda" calisir.
                // Drone GPS pozunu takip eder; GPS yoksa SLAM'e duser. Her iki konum da
                // yorunge karsilastirma katmanina yayinlanir (GNSS-bagimsizlik kaniti).
                if (!_localInput && drone.IsRunning) drone.StopRoute();
                bool poseApplied = false;
                TwinPoseSource poseSource = TwinPoseSource.None;
                if (msg.pose != null)
                {
                    poseApplied = ApplyVehiclePose(msg.pose);
                    if (poseApplied) poseSource = TwinPoseSource.Gps;
                    OnUavGpsPose?.Invoke(new Vector2d(msg.pose.latitude, msg.pose.longitude));
                }
                if (msg.slamPose != null)
                {
                    LastSlamConfidence = msg.slamPose.confidence;
                    OnUavSlamPose?.Invoke(new Vector2d(msg.slamPose.latitude, msg.slamPose.longitude));
                    if (!poseApplied)
                    {
                        // GPS kesildi: SLAM kestirimine dus (PDF gorev senaryosu).
                        poseApplied = ApplySlamPose(msg.slamPose);
                        if (poseApplied) poseSource = TwinPoseSource.Slam;
                    }
                }
                if (poseApplied) SetPoseSource(poseSource);
                applied |= poseApplied;
            }
            else if (showPose && isRoverPayload && (msg.pose != null || msg.slamPose != null))
            {
                bool roverApplied = false;
                if (roverAdapter != null)
                {
                    if (msg.slamPose != null)
                        roverApplied = roverAdapter.TryApplySlamPose(msg.slamPose);
                    if (!roverApplied && msg.pose != null)
                        roverApplied = roverAdapter.TryApplyPose(msg.pose);
                }
                if (!roverApplied && remoteState != null)
                    remoteState.SetWarning("Rover pozu geldi ancak rover adapter bulunamadi.");
                applied |= roverApplied;
            }

            if (msg.telemetry != null && remoteState != null)
            {
                remoteState.ApplyTelemetry(msg.telemetry, msg.sourceId, msg.timestampMs, msg.vehicleType);
                applied = true;
            }

            if (remoteState != null)
            {
                remoteState.ApplyOperationalState(msg);
                applied = true;
            }

            if (selected && !IsReplaying && ApplyMissionDelta(msg))
                applied = true;

            if (selected && !IsReplaying && missionEngine != null)
            {
                if (missionEngine.ApplyMessage(msg))
                    applied = true;
                if (remoteState != null)
                {
                    remoteState.UpdateDeltaCounts(missionEngine.ObstacleCount, missionEngine.TargetCount, missionEngine.VoxelCount);
                    remoteState.SetVehicleStatusLine(missionEngine.BuildVehicleStatusLine());
                    remoteState.SetMissionPhaseAndStatus(missionEngine.CurrentPhase, missionEngine.CurrentPhaseStatus);
                    if (missionEngine.HasRoverDetour)
                        remoteState.SetWarning("Uyarı: Rover kaçınma rotası üretildi; araca gönderilmedi.");
                    else if (missionEngine.LastRoverPlanStatus == RoverPlanStatus.Blocked && !string.IsNullOrEmpty(missionEngine.LastRoverPlanError))
                        remoteState.SetWarning("Rover için geçerli rota bulunamadı.");
                }
            }

            if (imageryService != null && msg.imagery != null)
            {
                if (imageryService.TryApplyImagery(msg.imagery, msg.sourceId))
                {
                    applied = true;
                    }
            }

            // ROVER rotasi (PDF: IHA VE Rover icin ortak ara nokta yonetimi):
            // rover payload'indaki rota haritada ayri turuncu katman olarak cizilir.
            if (isRoverPayload && msg.route != null && msg.route.waypoints != null)
            {
                if (roverRouteView == null)
                {
                    roverRouteView = FindObjectOfType<DigitalTwinRoverRouteView>();
                    if (roverRouteView == null)
                        roverRouteView = new GameObject("DigitalTwinRoverRouteView").AddComponent<DigitalTwinRoverRouteView>();
                }
                string mode = ResolveRouteMode(msg);
                var roverPts = mode == "replace" ? new List<Vector2d>() : roverRouteView.CopyRoute();
                for (int i = 0; i < msg.route.waypoints.Length; i++)
                {
                    var wp = msg.route.waypoints[i];
                    int index = wp.index >= 0 ? wp.index : i;
                    if (mode == "patch" && wp.operation == "remove")
                    { if (index < roverPts.Count) roverPts.RemoveAt(index); }
                    else if (mode == "patch" && index < roverPts.Count) roverPts[index] = new Vector2d(wp.latitude, wp.longitude);
                    else roverPts.Add(new Vector2d(wp.latitude, wp.longitude));
                }
                roverRouteView.SetRoute(roverPts);
                applied = true;
            }


            if (applied)
            {
                CommitValidation(msg);
                LastAcceptedVehicle = DigitalTwinRemoteState.VehicleKey(msg.vehicleType);
                LastAcceptedSource = msg.sourceId;
                LastAcceptedForDisplay = selected && !_localInput;
            }
            PublishStatus(applied ? DigitalTwinApplyStatusCode.Ok : DigitalTwinApplyStatusCode.MapOrDroneMissing,
                applied ? "Mesaj uygulandi" : "Uygulanacak gecerli veri yok", msg.sequenceId, msg.timestampMs, msg.sourceId);
            return applied;
        }

        private static string ResolveRouteMode(DigitalTwinMessageV1 msg)
        {
            if (msg == null)
                return "replace";
            if (msg.replaceRoute)
                return "replace";
            if (string.IsNullOrEmpty(msg.routeMode))
                return "replace";
            if (msg.routeMode.Equals("append", System.StringComparison.OrdinalIgnoreCase))
                return "append";
            if (msg.routeMode.Equals("patch", System.StringComparison.OrdinalIgnoreCase))
                return "patch";
            return "replace";
        }

        private string SourceKey(DigitalTwinMessageV1 msg) => (IsReplaying ? "replay/" : _localInput ? "sample/" : "live/")
            + DigitalTwinRemoteState.VehicleKey(msg.vehicleType) + "/" + msg.sourceId;

        private bool ValidateMessage(DigitalTwinMessageV1 msg, out DigitalTwinApplyStatusCode code, out string error)
        {
            code = DigitalTwinApplyStatusCode.InvalidSchema;
            error = "Mesaj kimliği veya içeriği geçersiz";
            if (msg == null || msg.schemaVersion != DigitalTwinJsonSchema.Version1 || string.IsNullOrWhiteSpace(msg.sourceId)
                || string.IsNullOrWhiteSpace(msg.vehicleType) || (!TwinVehicleTypes.IsUav(msg.vehicleType) && !TwinVehicleTypes.IsRover(msg.vehicleType))) return false;
            if (!_localInput && validateAuthToken && (string.IsNullOrEmpty(expectedAuthToken) || msg.authToken != expectedAuthToken))
            { code = DigitalTwinApplyStatusCode.RejectedUnauthorized; error = "Auth token geçersiz"; return false; }
            if (!_localInput && (msg.timestampMs <= 0 || msg.sequenceId <= 0)) return false;
            long now = GetUnixTimeMs();
            if (!_localInput && msg.timestampMs < now - System.Math.Max(1, maxPacketAgeMs))
            { code = DigitalTwinApplyStatusCode.RejectedOldTimestamp; error = "Telemetri zaman aşımına uğramış"; return false; }
            if (!_localInput && msg.timestampMs > now + System.Math.Max(0, maxFutureTimestampMs))
            { code = DigitalTwinApplyStatusCode.RejectedFutureTimestamp; error = "Timestamp fazla gelecekte"; return false; }
            if (!DigitalTwinMessageValidation.ValidPayload(msg, out error)) return false;
            if (_sourceStates.TryGetValue(SourceKey(msg), out var state))
            {
                bool reset = !IsReplaying && sourceResetTimeoutSeconds > 0 && Time.unscaledTime - state.lastSeenAt > sourceResetTimeoutSeconds;
                if (!reset && rejectOutOfOrderSequence && msg.sequenceId > 0 && msg.sequenceId <= state.lastSequenceId)
                { code = DigitalTwinApplyStatusCode.RejectedOutOfOrder; error = "Sequence sırası geride"; return false; }
                if (!reset && rejectOlderTimestamps && msg.timestampMs > 0 && msg.timestampMs < state.lastTimestampMs)
                { code = DigitalTwinApplyStatusCode.RejectedOldTimestamp; error = "Timestamp sırası geride"; return false; }
            }
            code = DigitalTwinApplyStatusCode.Ok; error = ""; return true;
        }

        private void CommitValidation(DigitalTwinMessageV1 msg)
        {
            _sourceStates[SourceKey(msg)] = new SourceValidationState
            { lastSequenceId = msg.sequenceId, lastTimestampMs = msg.timestampMs, lastSeenAt = Time.unscaledTime };
        }

        private bool ApplyVehiclePose(TwinPoseBlock pose)
        {
            if (abstractMap == null || drone == null)
                return false;

            try
            {
                var geo = new Vector2d(pose.latitude, pose.longitude);
                Vector3 world = abstractMap.GeoToWorldPosition(geo, true);
                if (pose.altitudeM > 0.5f)
                    world.y = pose.altitudeM;

                Quaternion rot = drone.transform.rotation;
                if (applyPitchRoll)
                    rot = Quaternion.Euler(pose.pitchDeg, pose.yawDeg, pose.rollDeg);
                else if (applyYaw)
                    rot = Quaternion.Euler(0f, pose.yawDeg, 0f);

                if (smoothPoseUpdates)
                {
                    _targetPosition = world;
                    _targetRotation = rot;
                    _hasTargetPose = true;
                }
                else
                {
                    drone.transform.position = world;
                    drone.transform.rotation = rot;
                }

                LastUavGeo = geo;
                LastUavYaw = pose.yawDeg;
                _lastUavPoseAt = Time.unscaledTime;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool ApplySlamPose(TwinSlamPoseBlock slamPose)
        {
            if (slamPose == null)
                return false;

            var pose = new TwinPoseBlock
            {
                latitude = slamPose.latitude,
                longitude = slamPose.longitude,
                altitudeM = slamPose.altitudeM,
                yawDeg = slamPose.yawDeg,
                pitchDeg = slamPose.pitchDeg,
                rollDeg = slamPose.rollDeg
            };
            return ApplyVehiclePose(pose);
        }

        private bool ApplyRouteReplace(TwinRouteBlock route)
        {
            if (route == null || route.waypoints == null || routeManager == null)
                return false;

            var wps = new List<WaypointData>(route.waypoints.Length);
            for (int i = 0; i < route.waypoints.Length; i++)
            {
                if (!TryCreateWaypoint(route.waypoints[i], i, out var wp))
                    return false;
                wps.Add(wp);
            }

            routeManager.ReplaceRoute(wps);
            return true;
        }

        private bool ApplyRouteAppend(TwinRouteBlock route)
        {
            if (route == null || route.waypoints == null || routeManager == null)
                return false;

            var data = routeManager.GetRouteData();
            var existing = (data != null && data.waypoints != null) ? data.waypoints : new List<WaypointData>();
            var merged = new List<WaypointData>(existing.Count + route.waypoints.Length);
            for (int i = 0; i < existing.Count; i++)
                merged.Add(existing[i].CloneWithIndex(i));

            int idx = merged.Count;
            for (int i = 0; i < route.waypoints.Length; i++)
            {
                if (!TryCreateWaypoint(route.waypoints[i], idx++, out var wp))
                    return false;
                merged.Add(wp);
            }

            routeManager.ReplaceRoute(merged);
            return true;
        }

        private bool ApplyRoutePatch(TwinRouteBlock route)
        {
            if (route == null || route.waypoints == null || routeManager == null)
                return false;

            var data = routeManager.GetRouteData();
            var existing = (data != null && data.waypoints != null) ? data.waypoints : new List<WaypointData>();
            var mutable = new List<WaypointData>(existing.Count + route.waypoints.Length);
            for (int i = 0; i < existing.Count; i++)
                mutable.Add(existing[i].CloneWithIndex(i));

            for (int i = 0; i < route.waypoints.Length; i++)
            {
                var incoming = route.waypoints[i];
                string op = string.IsNullOrEmpty(incoming.operation) ? "upsert" : incoming.operation.ToLowerInvariant();
                int index = incoming.index >= 0 ? incoming.index : i;

                if (op == "remove")
                {
                    if (index >= 0 && index < mutable.Count)
                        mutable.RemoveAt(index);
                    continue;
                }

                if (!TryCreateWaypoint(incoming, index, out var patchWp))
                    return false;

                if (index >= 0 && index < mutable.Count)
                    mutable[index] = patchWp;
                else
                    mutable.Add(patchWp);
            }

            for (int i = 0; i < mutable.Count; i++)
                mutable[i] = mutable[i].CloneWithIndex(i);

            routeManager.ReplaceRoute(mutable);
            return true;
        }

        private bool TryCreateWaypoint(TwinRouteWaypoint w, int index, out WaypointData waypoint)
        {
            waypoint = null;
            if (w == null || abstractMap == null)
                return false;

            try
            {
                var geo = new Vector2d(w.latitude, w.longitude);
                Vector3 world = abstractMap.GeoToWorldPosition(geo, true);
                if (abstractMap.Root != null)
                    world.y = abstractMap.Root.position.y;
                float alt = w.altitudeM > 0.5f ? w.altitudeM : 10f;
                waypoint = new WaypointData(index, world, w.latitude, w.longitude, alt, null,
                    new WaypointMetadata { speedOverride = w.speedMps, holdTimeSeconds = w.holdSeconds, actionId = w.action });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool ApplyMissionDelta(DigitalTwinMessageV1 msg)
        {
            bool changed = false;

            if (msg.obstacles != null)
            {
                for (int i = 0; i < msg.obstacles.Length; i++)
                {
                    var obstacle = msg.obstacles[i];
                    if (obstacle == null || string.IsNullOrEmpty(obstacle.id))
                        continue;
                    string op = string.IsNullOrEmpty(obstacle.operation) ? "upsert" : obstacle.operation.ToLowerInvariant();
                    if (op == "remove")
                        changed |= _obstacleState.Remove(obstacle.id);
                    else
                    {
                        _obstacleState[obstacle.id] = obstacle;
                        changed = true;
                    }
                }
            }

            if (msg.targets != null)
            {
                for (int i = 0; i < msg.targets.Length; i++)
                {
                    var target = msg.targets[i];
                    if (target == null || string.IsNullOrEmpty(target.id))
                        continue;
                    string op = string.IsNullOrEmpty(target.operation) ? "upsert" : target.operation.ToLowerInvariant();
                    if (op == "remove")
                        changed |= _targetState.Remove(target.id);
                    else
                    {
                        _targetState[target.id] = target;
                        changed = true;
                    }
                }
            }

            if (remoteState != null)
            {
                remoteState.UpdateDeltaCounts(_obstacleState.Count, _targetState.Count);
                if (msg.mission != null && !string.IsNullOrEmpty(msg.mission.warning))
                    remoteState.SetWarning(msg.mission.warning);
            }

            return changed;
        }

        public bool TryApplyPoseJson(string json)
        {
            // Legacy flat poses have no authenticated identity, sequence or timestamp.
            PublishStatus(DigitalTwinApplyStatusCode.InvalidSchema, "Legacy poz yerine V1 mesaj kullanın", 0, 0, "");
            return false;
        }

        private void PublishStatus(DigitalTwinApplyStatusCode code, string message, long sequenceId, long timestampMs, string sourceId)
        {
            LastApplyStatus = new DigitalTwinApplyStatus
            {
                code = code,
                message = message,
                sequenceId = sequenceId,
                timestampMs = timestampMs,
                sourceId = sourceId ?? ""
            };
            OnJsonApplied?.Invoke(LastApplyStatus);
        }

        public string BuildLastAckJson()
        {
            var ack = new DigitalTwinAckMessage
            {
                ok = LastApplyStatus.code == DigitalTwinApplyStatusCode.Ok,
                code = LastApplyStatus.code.ToString(),
                message = LastApplyStatus.message,
                sequenceId = LastApplyStatus.sequenceId,
                timestampMs = LastApplyStatus.timestampMs,
                sourceId = LastApplyStatus.sourceId
            };
            return JsonUtility.ToJson(ack, false);
        }

        [System.Serializable]
        private class DigitalTwinAckMessage
        {
            public bool ok;
            public string code;
            public string message;
            public long sequenceId;
            public long timestampMs;
            public string sourceId;
        }

        private static long GetUnixTimeMs()
        {
            return (long)(System.DateTime.UtcNow - System.DateTime.UnixEpoch).TotalMilliseconds;
        }

#if UNITY_EDITOR
        [ContextMenu("Log Example JSON (v1)")]
        private void LogExampleJson()
        {
            Debug.Log("[DigitalTwin] Ornek tam mesaj:\n" + DigitalTwinJsonSchema.ExampleFullMessageV1);
        }

        [ContextMenu("Self Test Apply + ACK")]
        private void SelfTestApplyAndAck()
        {
            bool ok = TryApplyDigitalTwinJson(DigitalTwinJsonSchema.ExampleFullMessageV1);
            Debug.Log("[DigitalTwin] SelfTestApplyAndAck ok=" + ok + " ack=" + BuildLastAckJson());
        }

        [ContextMenu("Self Test Sequence NACK")]
        private void SelfTestSequenceNack()
        {
            var msg = JsonUtility.FromJson<DigitalTwinMessageV1>(DigitalTwinJsonSchema.ExampleFullMessageV1);
            if (msg == null)
            {
                Debug.LogWarning("[DigitalTwin] SelfTestSequenceNack parse fail");
                return;
            }

            msg.sequenceId = 9000;
            string first = JsonUtility.ToJson(msg, false);
            bool firstOk = TryApplyDigitalTwinJson(first);

            msg.sequenceId = 8999;
            string second = JsonUtility.ToJson(msg, false);
            bool secondOk = TryApplyDigitalTwinJson(second);

            Debug.Log("[DigitalTwin] SequenceTest firstOk=" + firstOk + " secondOk=" + secondOk + " ack=" + BuildLastAckJson());
        }
#endif
    }
}
