using System;
using System.Collections.Generic;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// Google Earth Studio (veya Earth) KML kamera yolunu okuyup Digital Twin JSON poz mesajlari olarak oynatir.
    /// </summary>
    public class DigitalTwinKmlPosePlayer : MonoBehaviour
    {
        [Header("KML kaynak (biri dolu olmali)")]
        [SerializeField] private TextAsset kmlTextAsset;
        [Tooltip("StreamingAssets altindaki goreceli yol, or. GoogleEarthStudio/sample-campus-flight.kml")]
        [SerializeField] private string kmlStreamingAssetsRelativePath = "GoogleEarthStudio/sample-campus-flight.kml";

        [Header("Oynatma")]
        [SerializeField] private DigitalTwinJsonPoseBridge bridge;
        [SerializeField] private float secondsPerPoint = 0.25f;
        [SerializeField] private bool loop = true;
        [SerializeField] private string sourceId = "google-earth-studio-kml";
        [SerializeField] private string authToken = "simurgh-2026";
        [SerializeField] private string vehicleType = "uav";
        [SerializeField] private string missionPhase = "scan";

        private Coroutine _routine;

        private void Awake()
        {
            if (bridge == null)
                bridge = FindObjectOfType<DigitalTwinJsonPoseBridge>();
        }

        [ContextMenu("KML poz akisini baslat")]
        public void BeginPlaybackFromKml()
        {
            StopPlayback();
            string xml = LoadKmlXml();
            if (string.IsNullOrWhiteSpace(xml))
            {
                Debug.LogWarning("[DigitalTwinKmlPose] KML icerik yok (TextAsset veya StreamingAssets yolu).");
                return;
            }

            if (!DigitalTwinKmlCameraPathReader.TryParse(xml, out var pts) || pts.Count < 1)
            {
                Debug.LogWarning("[DigitalTwinKmlPose] KML icinde gx:coord veya LineString coordinates bulunamadi.");
                return;
            }

            if (bridge == null)
                bridge = FindObjectOfType<DigitalTwinJsonPoseBridge>();
            if (bridge == null)
            {
                Debug.LogWarning("[DigitalTwinKmlPose] DigitalTwinJsonPoseBridge bulunamadi.");
                return;
            }

            _routine = StartCoroutine(PlayCo(pts));
        }

        public void StopPlayback()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
        }

        private string LoadKmlXml()
        {
            if (kmlTextAsset != null)
                return kmlTextAsset.text;
            if (string.IsNullOrWhiteSpace(kmlStreamingAssetsRelativePath))
                return null;
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, kmlStreamingAssetsRelativePath.TrimStart('/', '\\'));
            if (!System.IO.File.Exists(path))
            {
                Debug.LogWarning("[DigitalTwinKmlPose] KML dosyasi yok: " + path);
                return null;
            }
            return System.IO.File.ReadAllText(path);
        }

        private System.Collections.IEnumerator PlayCo(List<DigitalTwinKmlCameraPathReader.KmlPoint> pts)
        {
            long seq = Math.Max(1L, (long)(Time.realtimeSinceStartup * 1000f) % 1000000L);
            int n = pts.Count;
            int i = 0;
            while (enabled)
            {
                var cur = pts[i];
                float yaw = 0f;
                if (n > 1)
                {
                    int iNext = i + 1 < n ? i + 1 : (loop ? 0 : i);
                    int iPrev = i - 1 >= 0 ? i - 1 : (loop ? n - 1 : i);
                    yaw = DigitalTwinKmlCameraPathReader.BearingDegreesDeg(
                        pts[iPrev].latitude, pts[iPrev].longitude,
                        pts[iNext].latitude, pts[iNext].longitude);
                }

                long ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                var msg = new DigitalTwinMessageV1
                {
                    schemaVersion = DigitalTwinJsonSchema.Version1,
                    sequenceId = seq++,
                    timestampMs = ts,
                    sourceId = sourceId,
                    authToken = authToken,
                    vehicleType = vehicleType,
                    missionPhase = missionPhase,
                    pose = new TwinPoseBlock
                    {
                        latitude = (float)cur.latitude,
                        longitude = (float)cur.longitude,
                        altitudeM = (float)Math.Max(1.0, cur.altitudeM),
                        yawDeg = yaw,
                        pitchDeg = 0f,
                        rollDeg = 0f
                    },
                    telemetry = new TwinTelemetryBlock
                    {
                        altitudeM = (float)cur.altitudeM,
                        speedMps = EstimateSpeedMps(pts, i, secondsPerPoint),
                        mode = "GES_KML",
                        waypointIndex = i,
                        hopCount = 1,
                        signalDbm = -55f,
                        snrDb = 22f,
                        latencyMs = 40f,
                        packetLossPercent = 0.5f
                    },
                    ackRequested = false
                };

                string json = JsonUtility.ToJson(msg);
                bridge.TryApplyDigitalTwinJson(json);

                yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, secondsPerPoint));

                i++;
                if (i >= n)
                {
                    if (!loop)
                        break;
                    i = 0;
                }
            }

            _routine = null;
        }

        private static float EstimateSpeedMps(List<DigitalTwinKmlCameraPathReader.KmlPoint> pts, int i, float dt)
        {
            if (pts == null || pts.Count < 2 || dt < 0.001f)
                return 0f;
            int j = (i + 1) % pts.Count;
            var a = pts[i];
            var b = pts[j];
            double latM = (b.latitude - a.latitude) * 111320d;
            double lonM = (b.longitude - a.longitude) * 111320d * Math.Cos(a.latitude * (Math.PI / 180d));
            double alt = b.altitudeM - a.altitudeM;
            double dist = Math.Sqrt(latM * latM + lonM * lonM + alt * alt);
            return (float)Math.Max(0d, dist / dt);
        }
    }
}
