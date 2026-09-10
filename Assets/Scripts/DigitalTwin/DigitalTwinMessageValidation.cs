using System;

namespace GroundStation.DigitalTwin
{
    public static class DigitalTwinMessageValidation
    {
        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public static bool Geo(double latitude, double longitude) => Finite(latitude) && Finite(longitude)
            && latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180;
        public static bool ValidPayload(DigitalTwinMessageV1 msg, out string error)
        {
            error = "Geçersiz koordinat, telemetri veya rota";
            if (msg.pose != null && (!Geo(msg.pose.latitude, msg.pose.longitude) || !Finite(msg.pose.altitudeM)
                || !Finite(msg.pose.yawDeg) || !Finite(msg.pose.pitchDeg) || !Finite(msg.pose.rollDeg))) return false;
            if (msg.slamPose != null && (!Geo(msg.slamPose.latitude, msg.slamPose.longitude) || !Finite(msg.slamPose.altitudeM)
                || !Finite(msg.slamPose.yawDeg) || !Finite(msg.slamPose.pitchDeg) || !Finite(msg.slamPose.rollDeg)
                || !Finite(msg.slamPose.confidence) || msg.slamPose.confidence < 0 || msg.slamPose.confidence > 1)) return false;
            var t = msg.telemetry;
            if (t != null && (!Finite(t.altitudeM) || !Finite(t.speedMps) || t.speedMps < 0
                || !Finite(t.batteryPercent) || t.batteryPercent < -1 || t.batteryPercent > 100
                || !Finite(t.batteryVoltage) || t.batteryVoltage < -1 || !Finite(t.signalDbm) || !Finite(t.snrDb)
                || !Finite(t.latencyMs) || t.latencyMs < 0 || !Finite(t.packetLossPercent) || t.packetLossPercent < 0 || t.packetLossPercent > 100)) return false;
            var m = msg.meshLink;
            if (m != null && (!Finite(m.linkQualityPercent) || m.linkQualityPercent < 0 || m.linkQualityPercent > 100
                || !Finite(m.packetLossPercent) || m.packetLossPercent < 0 || m.packetLossPercent > 100
                || !Finite(m.latencyMs) || m.latencyMs < 0 || !Finite(m.signalDbm) || !Finite(m.snrDb))) return false;
            if (msg.route != null)
            {
                string mode = string.IsNullOrEmpty(msg.routeMode) || msg.replaceRoute ? "replace" : msg.routeMode.ToLowerInvariant();
                if (mode != "replace" && mode != "append" && mode != "patch") return false;
                if (msg.route.waypoints == null || msg.route.waypoints.Length > 2000) return false;
                foreach (var w in msg.route.waypoints)
                {
                    if (w == null) return false;
                    if (w.operation == "remove") { if (mode != "patch" || w.index < 0) return false; continue; }
                    if (!string.IsNullOrEmpty(w.operation) && w.operation != "upsert") return false;
                    if (!Geo(w.latitude, w.longitude) || !Finite(w.altitudeM) || w.altitudeM < 0) return false;
                    if (!Finite(w.speedMps) || w.speedMps < -1 || !Finite(w.holdSeconds) || w.holdSeconds < 0) return false;
                }
            }
            if (msg.pose == null && msg.slamPose == null && msg.telemetry == null && msg.meshLink == null && msg.mission == null
                && msg.route == null && msg.imagery == null && msg.obstacles == null && msg.targets == null && msg.voxelCells == null
                && string.IsNullOrEmpty(msg.missionPhase)) return false;
            error = "";
            return true;
        }
    }
}
