using System;

namespace GroundStation.DigitalTwin
{
    [Serializable]
    public class TwinSlamPoseBlock
    {
        public float latitude;
        public float longitude;
        public float altitudeM;
        public float yawDeg;
        public float pitchDeg;
        public float rollDeg;
        public float confidence;
    }

    [Serializable]
    public class TwinMeshStatus
    {
        public int hopCount;
        public float signalDbm;
        public float snrDb;
        public float latencyMs;
        public float packetLossPercent;
        public float linkQualityPercent;
        public bool relayModeActive;
    }

    [Serializable]
    public class TwinMissionDelta
    {
        public string phase = "";
        public string status = "";
        public string activeVehicle = "";
        public string warning = "";
        public string note = "";
    }

    [Serializable]
    public class TwinObstacleDelta
    {
        public string id = "";
        public string operation = "";
        public string kind = "";
        public float latitude;
        public float longitude;
        public float radiusM;
        public float severity;
    }

    [Serializable]
    public class TwinTargetDelta
    {
        public string id = "";
        public string operation = "";
        public string kind = "";
        public float latitude;
        public float longitude;
        public bool reached;
        public float confidence;
    }

    [Serializable]
    public class TwinVoxelCellDelta
    {
        public string id = "";
        public string operation = "";
        public float latitude;
        public float longitude;
        public float altitudeM;
        public float sizeM = 1.0f;
        public float occupancy;
    }

    public static class TwinOperationPhases
    {
        public const string Scan = "scan";
        public const string JointOperation = "joint_operation";
        public const string DynamicReplan = "dynamic_replan";
        public const string Complete = "complete";
    }

    public static class TwinVehicleTypes
    {
        public const string Uav = "uav";
        public const string Rover = "rover";

        public static bool IsUav(string vehicleType)
        {
            if (string.IsNullOrEmpty(vehicleType))
                return true;
            return vehicleType.Equals(Uav, StringComparison.OrdinalIgnoreCase)
                || vehicleType.Equals("iha", StringComparison.OrdinalIgnoreCase)
                || vehicleType.Equals("drone", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsRover(string vehicleType)
        {
            if (string.IsNullOrEmpty(vehicleType))
                return false;
            return vehicleType.Equals(Rover, StringComparison.OrdinalIgnoreCase)
                || vehicleType.Equals("ika", StringComparison.OrdinalIgnoreCase)
                || vehicleType.Equals("ugv", StringComparison.OrdinalIgnoreCase)
                || vehicleType.Equals("ground", StringComparison.OrdinalIgnoreCase);
        }
    }
}
