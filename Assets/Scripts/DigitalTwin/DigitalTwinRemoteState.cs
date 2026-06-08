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

    public class DigitalTwinRemoteState : MonoBehaviour
    {
        [SerializeField] private bool useJsonTelemetry;

        public bool UseJsonTelemetry => useJsonTelemetry;
        public DigitalTwinOperationalState OperationalState { get; private set; }

        public string LastAltitudeText { get; private set; } = "";
        public string LastSpeedText { get; private set; } = "";
        public string LastModeText { get; private set; } = "";
        public string LastWaypointText { get; private set; } = "";
        public string LastMissionText { get; private set; } = "";
        public string LastMeshText { get; private set; } = "";
        public TwinMeshStatus LastMeshStatus { get; private set; }
        public bool HasMeshStatus { get; private set; }
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

            useJsonTelemetry = true;
            LastAltitudeText = string.Format(CultureInfo.InvariantCulture, "Y\u00FCkseklik: {0:F1} m", t.altitudeM);
            LastSpeedText = string.Format(CultureInfo.InvariantCulture, "H\u0131z: {0:F1} m/s", t.speedMps);
            LastModeText = string.IsNullOrEmpty(t.mode) ? "Mod: (JSON)" : "Mod: " + t.mode;
            LastWaypointText = string.Format(CultureInfo.InvariantCulture, "WP: {0}", t.waypointIndex);
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
                if (!string.IsNullOrEmpty(msg.mission.activeVehicle))
                    LastVehicleType = msg.mission.activeVehicle;
            }

            if (!string.IsNullOrEmpty(LastMissionPhase) || !string.IsNullOrEmpty(missionStatus))
                LastMissionText = string.Format(CultureInfo.InvariantCulture, "Faz: {0} | Durum: {1}", Safe(LastMissionPhase), Safe(missionStatus));

            if (msg.meshLink != null)
            {
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
            useJsonTelemetry = false;
            LastAltitudeText = LastSpeedText = LastModeText = LastWaypointText = "";
            LastMissionText = LastMeshText = LastWarningText = "";
            LastVehicleStatusText = "";
            LastImageryStatusText = "";
            HasMeshStatus = false;
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
}
