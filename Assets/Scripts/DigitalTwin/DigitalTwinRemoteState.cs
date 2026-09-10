using System.Globalization;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    [System.Serializable]
    public struct DigitalTwinOperationalState
    {
        public bool hasJsonState;
        public string sourceId;
        public string vehicleType;
        public string missionPhase;
        public string missionStatus;
        public string warning;
        public int obstacleCount;
        public int targetCount;
        public int voxelCount;
        public long timestampMs;
    }

    public class DigitalTwinVehicleState
    {
        [SerializeField] private bool useJsonTelemetry;

        public bool UseJsonTelemetry => useJsonTelemetry;
        public TwinTelemetryBlock Telemetry { get; private set; }
        public float LastMessageReceivedAt { get; private set; } = -1f;
        private float _telemetryReceivedAt = -1f, _meshReceivedAt = -1f, _batteryReceivedAt = -1f;
        private static bool IsFresh(float receivedAt) => receivedAt >= 0f && Time.unscaledTime - receivedAt <= 5f;
        public bool HasFreshMessage => IsFresh(LastMessageReceivedAt);
        public bool HasFreshTelemetry => IsFresh(_telemetryReceivedAt);
        public bool HasFreshMesh => HasMeshStatus && IsFresh(_meshReceivedAt);
        public bool HasFreshBattery => LastBatteryPercent >= 0f && IsFresh(_batteryReceivedAt);
        public DigitalTwinOperationalState OperationalState { get; private set; }

        public string LastAltitudeText { get; private set; } = "";
        public string LastSpeedText { get; private set; } = "";
        public string LastModeText { get; private set; } = "";
        public string LastWaypointText { get; private set; } = "";
        public string LastMissionText { get; private set; } = "";
        public string LastMeshText { get; private set; } = "";
        public TwinMeshStatus LastMeshStatus { get; private set; }
        public bool HasMeshStatus { get; private set; }
        public string LastBatteryText { get; private set; } = "";
        /// <summary>Son batarya yuzdesi; -1 = veri yok.</summary>
        public float LastBatteryPercent { get; private set; } = -1f;
        public float LastBatteryVoltage { get; private set; } = -1f;
        public string LastWarningText { get; private set; } = "";
        public string LastVehicleStatusText { get; private set; } = "";
        public string LastImageryStatusText { get; private set; } = "";
        public string LastSourceId { get; private set; } = "";
        public string LastVehicleType { get; private set; } = "";
        public string LastMissionPhase { get; private set; } = "";
        public long LastTimestampMs { get; private set; } = 0;

        public void ApplyTelemetry(TwinTelemetryBlock t)
        {
            ApplyTelemetry(t, "", 0);
        }

        public void ApplyTelemetry(TwinTelemetryBlock t, string sourceId, long timestampMs)
        {
            if (t == null)
            {
                Clear();
                return;
            }

            Telemetry = t;
            useJsonTelemetry = true;
            LastMessageReceivedAt = _telemetryReceivedAt = Time.unscaledTime;
            LastAltitudeText = string.Format(CultureInfo.InvariantCulture, "Y\u00FCkseklik: {0:F1} m", t.altitudeM);
            LastSpeedText = string.Format(CultureInfo.InvariantCulture, "H\u0131z: {0:F1} m/s", t.speedMps);
            LastModeText = string.IsNullOrEmpty(t.mode) ? "Mod: (JSON)" : "Mod: " + t.mode;
            LastWaypointText = string.Format(CultureInfo.InvariantCulture, "WP: {0}", t.waypointIndex);
            if (t.batteryPercent >= 0f)
            {
                _batteryReceivedAt = Time.unscaledTime;
                LastBatteryPercent = Mathf.Clamp(t.batteryPercent, 0f, 100f);
                LastBatteryVoltage = t.batteryVoltage;
                LastBatteryText = t.batteryVoltage > 0f
                    ? string.Format(CultureInfo.InvariantCulture, "Batarya: %{0:F0} ({1:F1} V)", LastBatteryPercent, t.batteryVoltage)
                    : string.Format(CultureInfo.InvariantCulture, "Batarya: %{0:F0}", LastBatteryPercent);
            }
            LastSourceId = sourceId ?? "";
            LastTimestampMs = timestampMs;
            LastMeshText = BuildMeshText(t.hopCount, t.signalDbm, t.snrDb, t.latencyMs, t.packetLossPercent, 0f, false);
            OperationalState = BuildOperationalState(OperationalState, LastSourceId, LastVehicleType, LastMissionPhase, "", LastWarningText, LastTimestampMs, OperationalState.obstacleCount, OperationalState.targetCount, OperationalState.voxelCount);
        }

        public void ApplyOperationalState(DigitalTwinMessageV1 msg)
        {
            if (msg == null)
                return;

            useJsonTelemetry = true;
            LastMessageReceivedAt = Time.unscaledTime;
            LastSourceId = msg.sourceId ?? LastSourceId;
            LastVehicleType = string.IsNullOrEmpty(msg.vehicleType) ? LastVehicleType : msg.vehicleType;
            LastTimestampMs = msg.timestampMs > 0 ? msg.timestampMs : LastTimestampMs;

            if (!string.IsNullOrEmpty(msg.missionPhase))
                LastMissionPhase = msg.missionPhase;
            if (msg.mission != null && !string.IsNullOrEmpty(msg.mission.phase))
                LastMissionPhase = msg.mission.phase;

            string missionStatus = "";
            if (msg.mission != null)
            {
                missionStatus = msg.mission.status ?? "";
                if (!string.IsNullOrEmpty(msg.mission.warning))
                    LastWarningText = "Uyari: " + msg.mission.warning;

            }

            if (!string.IsNullOrEmpty(LastMissionPhase) || !string.IsNullOrEmpty(missionStatus))
                LastMissionText = string.Format(CultureInfo.InvariantCulture, "Faz: {0} | Durum: {1}", Safe(LastMissionPhase), Safe(missionStatus));

            if (msg.meshLink != null)
            {
                _meshReceivedAt = Time.unscaledTime;
                LastMeshText = BuildMeshText(msg.meshLink.hopCount, msg.meshLink.signalDbm, msg.meshLink.snrDb, msg.meshLink.latencyMs, msg.meshLink.packetLossPercent, msg.meshLink.linkQualityPercent, msg.meshLink.relayModeActive);
                LastMeshStatus = msg.meshLink;
                HasMeshStatus = true;
            }

            int obstacleCount = msg.obstacles != null ? msg.obstacles.Length : OperationalState.obstacleCount;
            int targetCount = msg.targets != null ? msg.targets.Length : OperationalState.targetCount;
            int voxelCount = msg.voxelCells != null ? msg.voxelCells.Length : OperationalState.voxelCount;
            OperationalState = BuildOperationalState(OperationalState, LastSourceId, LastVehicleType, LastMissionPhase, missionStatus, LastWarningText, LastTimestampMs, obstacleCount, targetCount, voxelCount);
        }

        public void UpdateDeltaCounts(int obstacleCount, int targetCount)
        {
            UpdateDeltaCounts(obstacleCount, targetCount, OperationalState.voxelCount);
        }

        public void UpdateDeltaCounts(int obstacleCount, int targetCount, int voxelCount)
        {
            OperationalState = BuildOperationalState(OperationalState, LastSourceId, LastVehicleType, LastMissionPhase, OperationalState.missionStatus, LastWarningText, LastTimestampMs, obstacleCount, targetCount, voxelCount);
        }

        public void SetWarning(string warning)
        {
            LastWarningText = string.IsNullOrEmpty(warning) ? "" : "Uyari: " + warning;
            OperationalState = BuildOperationalState(OperationalState, LastSourceId, LastVehicleType, LastMissionPhase, OperationalState.missionStatus, LastWarningText, LastTimestampMs, OperationalState.obstacleCount, OperationalState.targetCount, OperationalState.voxelCount);
        }

        public void SetVehicleStatusLine(string statusLine)
        {
            LastVehicleStatusText = statusLine ?? "";
        }

        public void SetMissionPhaseAndStatus(string phase, string status)
        {
            if (!string.IsNullOrEmpty(phase))
                LastMissionPhase = phase;
            if (!string.IsNullOrEmpty(LastMissionPhase) || !string.IsNullOrEmpty(status))
                LastMissionText = string.Format(CultureInfo.InvariantCulture, "Faz: {0} | Durum: {1}", Safe(LastMissionPhase), Safe(status));
            OperationalState = BuildOperationalState(OperationalState, LastSourceId, LastVehicleType, LastMissionPhase, status, LastWarningText, LastTimestampMs, OperationalState.obstacleCount, OperationalState.targetCount, OperationalState.voxelCount);
        }

        public void ApplyImageryStatus(TwinImageryBlock img, string sourceId, string detail)
        {
            if (img == null)
                return;

            useJsonTelemetry = true;
            string pipe = string.IsNullOrEmpty(img.pipeline) ? "imagery" : img.pipeline;
            string mode = string.IsNullOrEmpty(img.mode) ? "-" : img.mode;
            string lab = string.IsNullOrEmpty(img.label) ? "" : " | " + img.label;
            string src = string.IsNullOrEmpty(sourceId) ? "" : " | src: " + sourceId;
            LastImageryStatusText = string.Format(CultureInfo.InvariantCulture,
                "Gorsel ({0}): {1}{2}{3} | {4}",
                pipe, mode, lab, src, string.IsNullOrEmpty(detail) ? "-" : detail);
        }

        public void Clear()
        {
            Telemetry = null;
            LastMessageReceivedAt = _telemetryReceivedAt = _meshReceivedAt = _batteryReceivedAt = -1f;
            useJsonTelemetry = false;
            LastAltitudeText = LastSpeedText = LastModeText = LastWaypointText = "";
            LastMissionText = LastMeshText = LastWarningText = "";
            LastVehicleStatusText = "";
            LastImageryStatusText = "";
            HasMeshStatus = false;
            LastBatteryText = "";
            LastBatteryPercent = -1f;
            LastBatteryVoltage = -1f;
            LastSourceId = "";
            LastVehicleType = "";
            LastMissionPhase = "";
            LastTimestampMs = 0;
            OperationalState = default;
        }

        private static string BuildMeshText(int hopCount, float signalDbm, float snrDb, float latencyMs, float lossPercent, float linkQualityPercent, bool relayMode)
        {
            string relay = relayMode ? "Relay" : "Direct";
            return string.Format(CultureInfo.InvariantCulture,
                "Mesh: Hop {0} | RSSI {1:F0} dBm | SNR {2:F0} dB | Lat {3:F0} ms | Loss {4:F1}% | Link {5:F0}% | {6}",
                hopCount, signalDbm, snrDb, latencyMs, lossPercent, linkQualityPercent, relay);
        }

        private static DigitalTwinOperationalState BuildOperationalState(DigitalTwinOperationalState current, string sourceId, string vehicleType, string missionPhase, string missionStatus, string warning, long timestampMs, int obstacleCount, int targetCount, int voxelCount)
        {
            current.hasJsonState = true;
            current.sourceId = sourceId ?? "";
            current.vehicleType = vehicleType ?? "";
            current.missionPhase = missionPhase ?? "";
            current.missionStatus = missionStatus ?? "";
            current.warning = warning ?? "";
            current.timestampMs = timestampMs;
            current.obstacleCount = obstacleCount;
            current.targetCount = targetCount;
            current.voxelCount = voxelCount;
            return current;
        }

        private static string Safe(string value)
        {
            return string.IsNullOrEmpty(value) ? "-" : value;
        }
    }

    /// <summary>Independent live and playback snapshots, keyed by vehicle and source.</summary>
    public class DigitalTwinRemoteState : MonoBehaviour
    {
        private readonly System.Collections.Generic.Dictionary<string, DigitalTwinVehicleState> _states = new System.Collections.Generic.Dictionary<string, DigitalTwinVehicleState>();
        private readonly System.Collections.Generic.Dictionary<string, string> _selected = new System.Collections.Generic.Dictionary<string, string>();
        public bool IsReplay { get; private set; }
        public bool IsSample { get; set; }
        public DigitalTwinVehicleState SelectedUav => GetVehicleState("uav");
        private string Scope => IsReplay ? "replay/" : IsSample ? "sample/" : "live/";
        public static string VehicleKey(string vehicle) => TwinVehicleTypes.IsRover(vehicle) ? "rover" : "uav";
        public DigitalTwinVehicleState GetVehicleState(string vehicle, string source = null)
        {
            string v = Scope + VehicleKey(vehicle);
            if (source == null) _selected.TryGetValue(v, out source);
            string key = v + "/" + (source ?? "");
            if (!_states.TryGetValue(key, out var state)) _states[key] = state = new DigitalTwinVehicleState();
            return state;
        }
        public bool SelectSource(string vehicle, string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return false;
            _selected[Scope + VehicleKey(vehicle)] = source;
            return true;
        }
        public bool IsSelectedSource(string vehicle, string source)
        {
            string v = Scope + VehicleKey(vehicle);
            return !_selected.ContainsKey(v) || _selected[v] == (source ?? "");
        }
        public void SetReplay(bool enabled)
        {
            IsReplay = enabled;
            if (enabled)
            {
                foreach (var key in new System.Collections.Generic.List<string>(_states.Keys))
                    if (key.StartsWith("replay/")) _states.Remove(key);
                foreach (var key in new System.Collections.Generic.List<string>(_selected.Keys))
                    if (key.StartsWith("replay/")) _selected.Remove(key);
            }
        }
        public TwinTelemetryBlock Telemetry => SelectedUav.Telemetry;
        public float LastMessageReceivedAt => SelectedUav.LastMessageReceivedAt;
        public DigitalTwinOperationalState OperationalState => SelectedUav.OperationalState;
        public string LastAltitudeText => SelectedUav.LastAltitudeText;
        public string LastSpeedText => SelectedUav.LastSpeedText;
        public string LastModeText => SelectedUav.LastModeText;
        public string LastWaypointText => SelectedUav.LastWaypointText;
        public string LastMissionText => SelectedUav.LastMissionText;
        public string LastMeshText => SelectedUav.LastMeshText;
        public TwinMeshStatus LastMeshStatus => SelectedUav.LastMeshStatus;
        public bool HasMeshStatus => SelectedUav.HasMeshStatus;
        public string LastBatteryText => SelectedUav.LastBatteryText;
        public float LastBatteryPercent => SelectedUav.LastBatteryPercent;
        public float LastBatteryVoltage => SelectedUav.LastBatteryVoltage;
        public string LastWarningText => SelectedUav.LastWarningText;
        public string LastVehicleStatusText => SelectedUav.LastVehicleStatusText;
        public string LastImageryStatusText => SelectedUav.LastImageryStatusText;
        public string LastSourceId => SelectedUav.LastSourceId;
        public string LastVehicleType => SelectedUav.LastVehicleType;
        public string LastMissionPhase => SelectedUav.LastMissionPhase;
        public long LastTimestampMs => SelectedUav.LastTimestampMs;
        public bool UseJsonTelemetry => SelectedUav.UseJsonTelemetry;
        public bool HasFreshMessage => SelectedUav.HasFreshMessage;
        public bool HasFreshTelemetry => SelectedUav.HasFreshTelemetry;
        public bool HasFreshMesh => SelectedUav.HasFreshMesh;
        public bool HasFreshBattery => SelectedUav.HasFreshBattery;

        public void ApplyTelemetry(TwinTelemetryBlock t) => ApplyTelemetry(t, "", 0);
        public void ApplyTelemetry(TwinTelemetryBlock t, string source, long timestamp, string vehicle = "uav")
        {
            if (!_selected.ContainsKey(Scope + VehicleKey(vehicle))) _selected[Scope + VehicleKey(vehicle)] = source ?? "";
            GetVehicleState(vehicle, source).ApplyTelemetry(t, source, timestamp);
        }
        public void ApplyOperationalState(DigitalTwinMessageV1 msg)
        {
            if (msg == null) return;
            if (!_selected.ContainsKey(Scope + VehicleKey(msg.vehicleType))) _selected[Scope + VehicleKey(msg.vehicleType)] = msg.sourceId ?? "";
            GetVehicleState(msg.vehicleType, msg.sourceId).ApplyOperationalState(msg);
        }
        public void UpdateDeltaCounts(int a, int b) => SelectedUav.UpdateDeltaCounts(a, b);
        public void UpdateDeltaCounts(int a, int b, int c) => SelectedUav.UpdateDeltaCounts(a, b, c);
        public void SetWarning(string s) => SelectedUav.SetWarning(s);
        public void SetVehicleStatusLine(string s) => SelectedUav.SetVehicleStatusLine(s);
        public void SetMissionPhaseAndStatus(string p, string s) => SelectedUav.SetMissionPhaseAndStatus(p, s);
        public void ApplyImageryStatus(TwinImageryBlock i, string s, string d) => SelectedUav.ApplyImageryStatus(i, s, d);
        public void Clear() { _states.Clear(); _selected.Clear(); IsReplay = IsSample = false; }
    }
}
