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

        [Header("Poz")]
        [SerializeField] private bool applyYaw = true;
        [SerializeField] private bool applyPitchRoll;
        [SerializeField] private bool smoothPoseUpdates = true;
        [SerializeField] private float positionLerpSpeed = 12f;
        [SerializeField] private float rotationLerpSpeed = 10f;

        [Header("Message Validation")]
        [SerializeField] private bool validateSchemaVersion = true;
        [SerializeField] private bool rejectOutOfOrderSequence = true;
        [SerializeField] private bool rejectOlderTimestamps = true;
        [SerializeField] private bool rejectFutureTimestamp = true;
        [SerializeField] private long maxFutureTimestampMs = 5000;
        [SerializeField] private bool validateAuthToken;
        [SerializeField] private string expectedAuthToken = "simurgh-2026";

        [Header("Telemetry Timeout")]
        [SerializeField] private bool clearJsonTelemetryWhenStale = true;
        [SerializeField] private float jsonTelemetryTimeoutSeconds = 2.5f;

        private long _lastSequenceId = -1;
        private long _lastTimestampMs = -1;
        private float _lastTelemetryReceivedAt = -1f;
        private bool _hasTargetPose;
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

            if (_hasTargetPose && smoothPoseUpdates)
            {
                float dt = Time.unscaledDeltaTime;
                float posT = 1f - Mathf.Exp(-Mathf.Max(0.01f, positionLerpSpeed) * dt);
                float rotT = 1f - Mathf.Exp(-Mathf.Max(0.01f, rotationLerpSpeed) * dt);
                drone.transform.position = Vector3.Lerp(drone.transform.position, _targetPosition, posT);
                drone.transform.rotation = Quaternion.Slerp(drone.transform.rotation, _targetRotation, rotT);
            }

            if (clearJsonTelemetryWhenStale &&
                jsonTelemetryTimeoutSeconds > 0f &&
                _lastTelemetryReceivedAt > 0f &&
                remoteState != null &&
                remoteState.UseJsonTelemetry &&
                Time.unscaledTime - _lastTelemetryReceivedAt > jsonTelemetryTimeoutSeconds)
            {
                remoteState.Clear();
            }
        }

        public bool TryApplyDigitalTwinJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                PublishStatus(DigitalTwinApplyStatusCode.InvalidJson, "JSON bos", 0, 0, "");
                return false;
            }

            json = json.Trim();
            if (LooksLikeMessageV1(json))
            {
                try
                {
                    var msg = JsonUtility.FromJson<DigitalTwinMessageV1>(json);
                    if (msg == null)
                    {
                        PublishStatus(DigitalTwinApplyStatusCode.InvalidJson, "JSON parse edilemedi", 0, 0, "");
                        return false;
                    }
                    NormalizeMessage(msg);
                    return ApplyMessageV1(msg);
                }
                catch
                {
                    PublishStatus(DigitalTwinApplyStatusCode.InvalidJson, "JSON parse exception", 0, 0, "");
                    return false;
                }
            }

            return TryApplyPoseJson(json);
        }

        public void ClearJsonTelemetryOverride()
        {
            if (remoteState != null)
                remoteState.Clear();
        }

        private static bool LooksLikeMessageV1(string json)
        {
            return json.IndexOf("\"schemaVersion\"", System.StringComparison.Ordinal) >= 0
                   || json.IndexOf("\"vehicleType\"", System.StringComparison.Ordinal) >= 0
                   || json.IndexOf("\"missionPhase\"", System.StringComparison.Ordinal) >= 0
                   || json.IndexOf("\"meshLink\"", System.StringComparison.Ordinal) >= 0
                   || json.IndexOf("\"imagery\"", System.StringComparison.Ordinal) >= 0
                   || (json.IndexOf("\"pose\"", System.StringComparison.Ordinal) >= 0
                       && json.IndexOf("\"latitude\"", System.StringComparison.Ordinal) >= 0);
        }

        private static void NormalizeMessage(DigitalTwinMessageV1 msg)
        {
            if (msg == null)
                return;
            if (string.IsNullOrEmpty(msg.missionPhase) && msg.mission != null && !string.IsNullOrEmpty(msg.mission.phase))
                msg.missionPhase = msg.mission.phase;
        }

        private bool ApplyMessageV1(DigitalTwinMessageV1 msg)
        {
            if (!ValidateMessage(msg, out var rejectCode, out var rejectMessage))
            {
                PublishStatus(rejectCode, rejectMessage, msg.sequenceId, msg.timestampMs, msg.sourceId);
                return false;
            }

            bool applied = false;
            bool isUavPayload = TwinVehicleTypes.IsUav(msg.vehicleType);
            bool isRoverPayload = TwinVehicleTypes.IsRover(msg.vehicleType);

            if (isUavPayload && (msg.pose != null || msg.slamPose != null))
            {
                if (abstractMap == null || drone == null)
                {
                    PublishStatus(DigitalTwinApplyStatusCode.MapOrDroneMissing, "Pose var ama map veya drone bagli degil", msg.sequenceId, msg.timestampMs, msg.sourceId);
                    return false;
                }

                // Gorev gercegi (PDF 3.4.4): arac GPS ile ucar, VI-SLAM "golge modda" calisir.
                // Drone GPS pozunu takip eder; GPS yoksa SLAM'e duser. Her iki konum da
                // yorunge karsilastirma katmanina yayinlanir (GNSS-bagimsizlik kaniti).
                bool poseApplied = false;
                if (msg.pose != null)
                {
                    poseApplied = ApplyVehiclePose(msg.pose);
                    OnUavGpsPose?.Invoke(new Vector2d(msg.pose.latitude, msg.pose.longitude));
                }
                if (msg.slamPose != null)
                {
                    OnUavSlamPose?.Invoke(new Vector2d(msg.slamPose.latitude, msg.slamPose.longitude));
                    if (!poseApplied)
                        poseApplied = ApplySlamPose(msg.slamPose);
                }
                applied |= poseApplied;
            }
            else if (isRoverPayload && (msg.pose != null || msg.slamPose != null))
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
                remoteState.ApplyTelemetry(msg.telemetry, msg.sourceId, msg.timestampMs);
                _lastTelemetryReceivedAt = Time.unscaledTime;
                applied = true;
            }

            if (remoteState != null)
            {
                remoteState.ApplyOperationalState(msg);
                applied = true;
            }

            if (ApplyMissionDelta(msg))
                applied = true;

            if (missionEngine != null)
            {
                if (missionEngine.ApplyMessage(msg))
                    applied = true;
                if (remoteState != null)
                {
                    remoteState.UpdateDeltaCounts(missionEngine.ObstacleCount, missionEngine.TargetCount, missionEngine.VoxelCount);
                    remoteState.SetVehicleStatusLine(missionEngine.BuildVehicleStatusLine());
                    remoteState.SetMissionPhaseAndStatus(missionEngine.CurrentPhase, missionEngine.CurrentPhaseStatus);
                    if (missionEngine.HasRoverDetour)
                        remoteState.SetWarning("Uyari: Rover için dinamik kaçınma rotası üretildi.");
                }
            }

            if (imageryService != null && msg.imagery != null)
            {
                if (imageryService.TryApplyImagery(msg.imagery, msg.sourceId))
                {
                    applied = true;
                    _lastTelemetryReceivedAt = Time.unscaledTime;
                }
            }

            bool shouldApplyRoute = isUavPayload
                                    && msg.route != null
                                    && msg.route.waypoints != null
                                    && msg.route.waypoints.Length > 0;
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

        private bool ValidateMessage(DigitalTwinMessageV1 msg, out DigitalTwinApplyStatusCode rejectCode, out string rejectMessage)
        {
            rejectCode = DigitalTwinApplyStatusCode.Ok;
            rejectMessage = "";

            if (msg == null)
            {
                rejectCode = DigitalTwinApplyStatusCode.InvalidJson;
                rejectMessage = "Mesaj null";
                return false;
            }

            if (validateSchemaVersion &&
                !string.IsNullOrEmpty(msg.schemaVersion) &&
                !msg.schemaVersion.Equals(DigitalTwinJsonSchema.Version1, System.StringComparison.Ordinal))
            {
                rejectCode = DigitalTwinApplyStatusCode.InvalidSchema;
                rejectMessage = "Schema version uyusmuyor";
                return false;
            }

            if (validateAuthToken)
            {
                if (string.IsNullOrEmpty(expectedAuthToken) || string.IsNullOrEmpty(msg.authToken) ||
                    !msg.authToken.Equals(expectedAuthToken, System.StringComparison.Ordinal))
                {
                    rejectCode = DigitalTwinApplyStatusCode.RejectedUnauthorized;
                    rejectMessage = "Auth token gecersiz";
                    return false;
                }
            }

            if (rejectOutOfOrderSequence && msg.sequenceId > 0 && _lastSequenceId > 0 && msg.sequenceId <= _lastSequenceId)
            {
                rejectCode = DigitalTwinApplyStatusCode.RejectedOutOfOrder;
                rejectMessage = "Sequence sirasi geride";
                return false;
            }

            if (rejectOlderTimestamps && msg.timestampMs > 0 && _lastTimestampMs > 0 && msg.timestampMs < _lastTimestampMs)
            {
                rejectCode = DigitalTwinApplyStatusCode.RejectedOldTimestamp;
                rejectMessage = "Timestamp eski";
                return false;
            }

            if (rejectFutureTimestamp && msg.timestampMs > 0)
            {
                long nowMs = GetUnixTimeMs();
                if (msg.timestampMs - nowMs > maxFutureTimestampMs)
                {
                    rejectCode = DigitalTwinApplyStatusCode.RejectedFutureTimestamp;
                    rejectMessage = "Timestamp fazla gelecekte";
                    return false;
                }
            }

            if (msg.sequenceId > 0) _lastSequenceId = msg.sequenceId;
            if (msg.timestampMs > 0) _lastTimestampMs = msg.timestampMs;
            return true;
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
                waypoint = new WaypointData(index, world, w.latitude, w.longitude, alt, null, null);
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
            if (string.IsNullOrWhiteSpace(json) || abstractMap == null || drone == null)
            {
                PublishStatus(DigitalTwinApplyStatusCode.MapOrDroneMissing, "Map veya drone eksik", 0, 0, "");
                return false;
            }

            json = json.Trim();
            try
            {
                var pose = JsonUtility.FromJson<TwinPoseFlatLegacy>(json);
                if (pose == null) return false;

                var block = new TwinPoseBlock
                {
                    latitude = pose.lat,
                    longitude = pose.lon,
                    altitudeM = pose.alt,
                    yawDeg = pose.yaw
                };

                bool ok = ApplyVehiclePose(block);
                PublishStatus(ok ? DigitalTwinApplyStatusCode.Ok : DigitalTwinApplyStatusCode.MapOrDroneMissing,
                    ok ? "Legacy pose uygulandi" : "Legacy pose uygulanamadi", 0, 0, "");
                return ok;
            }
            catch
            {
                PublishStatus(DigitalTwinApplyStatusCode.InvalidJson, "Legacy JSON parse edilemedi", 0, 0, "");
                return false;
            }
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
