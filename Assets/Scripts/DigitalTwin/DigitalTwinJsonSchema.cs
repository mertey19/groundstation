using System;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// Sabitler ve ornek JSON.
    /// </summary>
    public static class DigitalTwinJsonSchema
    {
        public const string Version1 = "1.0";

        public const string ExampleFullMessageV1 = @"{
  ""schemaVersion"": ""1.0"",
  ""sequenceId"": 12045,
  ""timestampMs"": 1711804800123,
  ""sourceId"": ""ros-bridge-a"",
  ""authToken"": ""simurgh-2026"",
  ""vehicleType"": ""uav"",
  ""missionPhase"": ""scan"",
  ""pose"": {
    ""latitude"": 37.7849,
    ""longitude"": -122.4034,
    ""altitudeM"": 35.0,
    ""yawDeg"": 45.0,
    ""pitchDeg"": 0.0,
    ""rollDeg"": 0.0
  },
  ""slamPose"": {
    ""latitude"": 37.78492,
    ""longitude"": -122.40338,
    ""altitudeM"": 34.9,
    ""yawDeg"": 44.7,
    ""pitchDeg"": 0.0,
    ""rollDeg"": 0.0,
    ""confidence"": 0.93
  },
  ""telemetry"": {
    ""altitudeM"": 35.0,
    ""speedMps"": 8.5,
    ""mode"": ""AUTO"",
    ""waypointIndex"": 2,
    ""hopCount"": 1,
    ""signalDbm"": -56,
    ""snrDb"": 22,
    ""latencyMs"": 43,
    ""packetLossPercent"": 0.8,
    ""batteryPercent"": 87.5,
    ""batteryVoltage"": 24.6
  },
  ""meshLink"": {
    ""hopCount"": 1,
    ""signalDbm"": -56,
    ""snrDb"": 22,
    ""latencyMs"": 43,
    ""packetLossPercent"": 0.8,
    ""linkQualityPercent"": 91,
    ""relayModeActive"": false
  },
  ""mission"": {
    ""phase"": ""scan"",
    ""status"": ""running"",
    ""activeVehicle"": ""uav"",
    ""warning"": """",
    ""note"": ""Pioneer scan in progress""
  },
  ""route"": {
    ""waypoints"": [
      { ""index"": 0, ""operation"": ""upsert"", ""latitude"": 37.7845, ""longitude"": -122.4040, ""altitudeM"": 40.0 },
      { ""index"": 1, ""operation"": ""upsert"", ""latitude"": 37.7855, ""longitude"": -122.4028, ""altitudeM"": 40.0 }
    ]
  },
  ""obstacles"": [
    { ""id"": ""obs-17"", ""operation"": ""upsert"", ""kind"": ""moving"", ""latitude"": 37.7850, ""longitude"": -122.4031, ""radiusM"": 4.0, ""severity"": 0.7 }
  ],
  ""targets"": [
    { ""id"": ""qr-3"", ""operation"": ""upsert"", ""kind"": ""qrcode"", ""latitude"": 37.7853, ""longitude"": -122.4029, ""reached"": false, ""confidence"": 0.89, ""decodedContent"": ""KESIF-NOKTA-3"" }
  ],
  ""voxelCells"": [
    { ""id"": ""vox-101"", ""operation"": ""upsert"", ""latitude"": 37.78512, ""longitude"": -122.40322, ""altitudeM"": 8.0, ""sizeM"": 1.2, ""occupancy"": 0.92 }
  ],
  ""routeMode"": ""replace"",
  ""replaceRoute"": false,
  ""ackRequested"": true,
  ""imagery"": {
    ""pipeline"": ""google_earth_studio"",
    ""mode"": ""frame_sequence"",
    ""label"": ""Campus orbit render"",
    ""streamingAssetsFile"": ""GoogleEarthStudio/Campus/frame_0001.png"",
    ""streamingAssetsFolder"": """",
    ""fileNamePattern"": ""frame_{0:D4}.png"",
    ""frameNumber"": 1,
    ""resourceTexturePath"": """",
    ""overlayAlpha"": 0.88,
    ""streamingAssetsVideoFile"": """",
    ""videoLoopMode"": ""loop""
  }
}";
    }

    /// <summary>
    /// Eski / minimal tek satir poz (kokte pose objesi yok).
    /// </summary>
    [Serializable]
    public class TwinPoseFlatLegacy
    {
        public float lat;
        public float lon;
        public float alt;
        public float yaw;
    }
}

namespace GroundStation.DigitalTwin
{
    [Serializable]
    public class DigitalTwinMessageV1
    {
        public string schemaVersion = "";
        public long sequenceId;
        public long timestampMs;
        public string sourceId = "";
        public string authToken = "";
        public string vehicleType = "";
        public string missionPhase = "";
        public TwinPoseBlock pose;
        public TwinSlamPoseBlock slamPose;
        public TwinTelemetryBlock telemetry;
        public TwinMeshStatus meshLink;
        public TwinMissionDelta mission;
        public TwinRouteBlock route;
        public TwinObstacleDelta[] obstacles;
        public TwinTargetDelta[] targets;
        public TwinVoxelCellDelta[] voxelCells;
        public string routeMode = ""; // replace|append|patch
        public bool replaceRoute;
        public bool ackRequested;
        /// <summary>
        /// Google Earth Studio / harici birlestirilmis ortofoto akisi: kare dosyasi veya mozaik.
        /// Dosyalar StreamingAssets klasoru altinda tutulur; JSON'da goreceli yol verilir.
        /// </summary>
        public TwinImageryBlock imagery;
    }

    /// <summary>
    /// Google Earth Studio PNG/JPEG ciktisi veya stitch sonrasi tek mozaik dosyasi.
    /// pipeline: google_earth_stitched | google_earth_studio | custom
    /// mode: frame_sequence | single_mosaic | video_mp4
    /// </summary>
    [Serializable]
    public class TwinImageryBlock
    {
        public string pipeline = "";
        public string mode = "";
        public string label = "";
        /// <summary>Tek dosya: StreamingAssets altindaki goreceli yol, or. GoogleEarthStudio/Campus/frame_0001.png</summary>
        public string streamingAssetsFile = "";
        public string streamingAssetsFolder = "";
        public string fileNamePattern = "frame_{0:D4}.png";
        public int frameNumber;
        public string resourceTexturePath = "";
        public float overlayAlpha = 0.88f;
        /// <summary>MP4: StreamingAssets altindaki goreceli yol. Doluysa VideoPlayer kullanilir (mode bos olsa bile).</summary>
        public string streamingAssetsVideoFile = "";
        /// <summary>video icin: bos veya "loop" tekrar; "once" tek sefer.</summary>
        public string videoLoopMode = "";
        /// <summary>
        /// AG UZERINDEN goruntu iletimi: kucuk JPEG/PNG karesinin Base64 icerigi
        /// (rover karekod fotografi ~50-100KB). Doluysa dosya yollari yerine bu kullanilir.
        /// </summary>
        public string imageBase64 = "";
        /// <summary>Fotografin iliskili oldugu hedef (karekod) id'si — galeri eslestirmesi.</summary>
        public string targetId = "";
    }

    [Serializable]
    public class TwinPoseBlock
    {
        public float latitude;
        public float longitude;
        public float altitudeM;
        public float yawDeg;
        public float pitchDeg;
        public float rollDeg;
    }

    [Serializable]
    public class TwinTelemetryBlock
    {
        public float altitudeM;
        public float speedMps;
        public string mode = "";
        public int waypointIndex;
        public int hopCount;
        public float signalDbm;
        public float snrDb;
        public float latencyMs;
        public float packetLossPercent;
        // Fail-safe girdisi (PDF 3.3): batarya. -1 = veri gelmedi (alan JSON'da yoksa
        // JsonUtility alan baslangic degerini korur, boylece "yok" ile "0" ayirt edilir).
        public float batteryPercent = -1f;
        public float batteryVoltage = -1f;
    }

    [Serializable]
    public class TwinRouteBlock
    {
        public TwinRouteWaypoint[] waypoints;
    }

    [Serializable]
    public class TwinRouteWaypoint
    {
        public int index = -1;
        public string operation = "";
        public float latitude;
        public float longitude;
        public float altitudeM = 10f;
    }
}
