using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Mapbox.Unity.Map;
using Mapbox.Utils;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// ORB-SLAM3 / EuRoC yorunge dosyalarini yer istasyonunda dogrudan oynatir.
    /// Harici surec (Python besleyici) gerekmeden GERCEK KONUM (ground truth) ve
    /// SLAM KESTIRIMI ayni anda <see cref="DigitalTwinJsonPoseBridge"/>'e beslenir;
    /// <see cref="DigitalTwinTrajectoryComparison"/> ikisini farkli renklerde cizer.
    ///
    /// Desteklenen bicimler (otomatik algilanir):
    ///   TUM   : "timestamp tx ty tz qx qy qz qw"          (bosluk ayracli, ORB-SLAM3 ciktisi)
    ///   EuRoC : "timestamp_ns,px,py,pz,qw,qx,qy,qz[,...]" (virgul ayracli, ASL ground truth)
    ///
    /// ORB-SLAM3 monoculer ciktisi KEYFI cerceve ve olcektedir; oynatmadan once
    /// SLAM yorungesi ground truth'a benzerlik donusumuyle (olcek + yaw + oteleme)
    /// hizalanir. Tam 3B Sim(3) hizalamasi ve ATE/RMSE raporu icin Tools/SlamFeeder
    /// altindaki Python besleyici kullanilir; burada harita gorunumu 2B oldugu icin
    /// kapali formdaki duzlem cozumu yeterlidir.
    /// </summary>
    public class DigitalTwinTrajectoryDatasetPlayer : MonoBehaviour
    {
        [Header("Veri dosyalari (StreamingAssets altinda goreceli yol)")]
        [Tooltip("Gercek konum. Bos birakilirsa oynatma baslamaz.")]
        [SerializeField] private string groundTruthFile = "SlamDataset/MH01_GT.txt";
        [Tooltip("SLAM kestirimi (ORB-SLAM3 ciktisi). Bos ise yalniz gercek konum cizilir.")]
        [SerializeField] private string slamFile = "SlamDataset/MH01_SLAM_sim.txt";
        [SerializeField] private TextAsset groundTruthAsset;
        [SerializeField] private TextAsset slamAsset;

        [Header("Cografi yerlestirme")]
        [Tooltip("Yerel cercevenin oturtulacagi enlem/boylam. (0,0) ise haritanin mevcut merkezi kullanilir.")]
        [SerializeField] private Vector2 anchorLatLon = Vector2.zero;
        [Tooltip("Yorunge buyutme carpani. 1 = gercek olcek. Veri kumeleri 10-30 m oldugu icin " +
                 "harita zoom 20+ degilse iz cok kucuk gorunur; buyutulurse SAPMA DEGERLERI DE " +
                 "ayni oranda buyur (hakem sunumunda 1 birakin).")]
        [SerializeField] private float worldScale = 1f;
        [SerializeField] private float altitudeOffsetM = 30f;
        [Tooltip("Yerel eksenler -> (dogu, kuzey, yukari). EuRoC dunya cercevesi z-yukaridir.")]
        [SerializeField] private bool zIsUp = true;

        [Header("Oynatma")]
        [SerializeField] private DigitalTwinJsonPoseBridge bridge;
        [SerializeField] private bool playOnStart = true;
        [SerializeField] private float speed = 1f;
        [SerializeField] private float maxMessagesPerSecond = 20f;
        [SerializeField] private bool loop;
        [SerializeField] private bool alignSlamToGroundTruth = true;
        [Tooltip("Zaman eslestirme toleransi (saniye).")]
        [SerializeField] private float maxAssociationDt = 0.05f;
        [SerializeField] private bool moveMapToAnchorOnStart = true;

        [Header("Mesaj alanlari")]
        [SerializeField] private string sourceId = "dataset-player";
        [SerializeField] private string authToken = "simurgh-2026";
        [SerializeField] private string vehicleType = "uav";
        [SerializeField] private string missionPhase = "scan";

        private struct Sample
        {
            public double t;
            public double x, y, z;   // yerel cerceve (metre)
            public double qx, qy, qz, qw;
        }

        private Coroutine _routine;
        private long _seq;

        public bool IsPlaying => _routine != null;
        /// <summary>Hizalamada bulunan olcek carpani (monoculer olcek belirsizligi gostergesi).</summary>
        public double LastAlignmentScale { get; private set; } = 1.0;
        /// <summary>Hizalama sonrasi ortalama sapma (metre) — HUD'daki degerle karsilastirmak icin.</summary>
        public double LastMeanDeviationM { get; private set; }

        private void Awake()
        {
            if (bridge == null) bridge = FindObjectOfType<DigitalTwinJsonPoseBridge>();
        }

        private void Start()
        {
            if (playOnStart) BeginPlayback();
        }

        private void OnDisable() => StopPlayback();

        [ContextMenu("Veri kumesini oynat")]
        public void BeginPlayback()
        {
            StopPlayback();

            string gtText = LoadText(groundTruthAsset, groundTruthFile);
            if (string.IsNullOrWhiteSpace(gtText))
            {
                Debug.LogWarning("[YorungeVeri] Gercek konum dosyasi okunamadi: " + groundTruthFile);
                return;
            }
            var gt = Parse(gtText);
            if (gt.Count < 2)
            {
                Debug.LogWarning("[YorungeVeri] Gercek konum dosyasinda gecerli poz yok: " + groundTruthFile);
                return;
            }

            var slam = new List<Sample>();
            string slamText = LoadText(slamAsset, slamFile);
            if (!string.IsNullOrWhiteSpace(slamText))
                slam = Parse(slamText);
            else if (!string.IsNullOrWhiteSpace(slamFile))
                Debug.LogWarning("[YorungeVeri] SLAM dosyasi okunamadi, yalniz gercek konum cizilecek: " + slamFile);

            if (bridge == null) bridge = FindObjectOfType<DigitalTwinJsonPoseBridge>();
            if (bridge == null)
            {
                Debug.LogWarning("[YorungeVeri] DigitalTwinJsonPoseBridge sahnede yok.");
                return;
            }

            // SLAM ornegini GT indeksine bagla (zaman damgasina gore en yakin komsu).
            var slamForGt = Associate(gt, slam, maxAssociationDt);
            if (slam.Count > 0 && alignSlamToGroundTruth)
                AlignInPlace(gt, slam, slamForGt);

            Vector2 anchor = ResolveAnchor(gt);
            if (moveMapToAnchorOnStart) TryMoveMap(anchor);

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[YorungeVeri] {0} gercek poz, {1} SLAM pozu, {2} eslesme. Olcek={3:F4}, ort sapma={4:F3} m. Capa {5:F6},{6:F6}",
                gt.Count, slam.Count, CountPairs(slamForGt), LastAlignmentScale, LastMeanDeviationM, anchor.x, anchor.y));

            _routine = StartCoroutine(PlayCo(gt, slam, slamForGt, anchor));
        }

        [ContextMenu("Oynatmayi durdur")]
        public void StopPlayback()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
        }

        // --------------------------------------------------------------- okuma

        private string LoadText(TextAsset asset, string relativePath)
        {
            if (asset != null) return asset.text;
            if (string.IsNullOrWhiteSpace(relativePath)) return null;
            string path = Path.Combine(Application.streamingAssetsPath, relativePath.TrimStart('/', '\\'));
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        private static readonly char[] CommaSeparators = { ',', ';' };

        private static List<Sample> Parse(string text)
        {
            var list = new List<Sample>(4096);
            var lines = text.Split('\n');
            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li].Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == '%') continue;

                bool comma = line.IndexOf(',') >= 0;
                string[] parts = comma
                    ? line.Split(CommaSeparators, StringSplitOptions.RemoveEmptyEntries)
                    : line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 8) continue;

                var v = new double[8];
                bool ok = true;
                for (int i = 0; i < 8 && ok; i++)
                    ok = double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]);
                if (!ok) continue; // basliklar ve bozuk satirlar sessizce atlanir

                var s = new Sample
                {
                    t = NormalizeTime(v[0]),
                    x = v[1], y = v[2], z = v[3]
                };
                if (comma) { s.qw = v[4]; s.qx = v[5]; s.qy = v[6]; s.qz = v[7]; } // EuRoC: qw once
                else { s.qx = v[4]; s.qy = v[5]; s.qz = v[6]; s.qw = v[7]; }       // TUM
                list.Add(s);
            }
            list.Sort((a, b) => a.t.CompareTo(b.t));
            return list;
        }

        private static double NormalizeTime(double raw)
        {
            if (raw > 1e14) return raw / 1e9;  // EuRoC nanosaniye
            if (raw > 1e11) return raw / 1e6;  // mikrosaniye
            return raw;
        }

        // ---------------------------------------------------- eslestirme/hizalama

        /// <returns>GT indeksi -> SLAM indeksi (-1 = eslesme yok / izleme kaybi)</returns>
        private static int[] Associate(List<Sample> gt, List<Sample> slam, float maxDt)
        {
            var map = new int[gt.Count];
            for (int i = 0; i < map.Length; i++) map[i] = -1;
            if (slam.Count == 0) return map;

            int j = 0;
            for (int i = 0; i < gt.Count; i++)
            {
                while (j + 1 < slam.Count &&
                       Math.Abs(slam[j + 1].t - gt[i].t) <= Math.Abs(slam[j].t - gt[i].t))
                    j++;
                if (Math.Abs(slam[j].t - gt[i].t) <= maxDt) map[i] = j;
            }
            return map;
        }

        private static int CountPairs(int[] map)
        {
            int n = 0;
            for (int i = 0; i < map.Length; i++) if (map[i] >= 0) n++;
            return n;
        }

        /// <summary>
        /// SLAM yorungesini GT'ye oturtur: duzlemde olcek + donme, dikeyde olcek + oteleme.
        /// Kapali form en kucuk kareler cozumu (Umeyama'nin 2B ozel hali) — SVD gerektirmez.
        /// </summary>
        private void AlignInPlace(List<Sample> gt, List<Sample> slam, int[] map)
        {
            int n = 0;
            double msx = 0, msy = 0, msz = 0, mdx = 0, mdy = 0, mdz = 0;
            for (int i = 0; i < map.Length; i++)
            {
                int j = map[i];
                if (j < 0) continue;
                msx += slam[j].x; msy += slam[j].y; msz += slam[j].z;
                mdx += gt[i].x; mdy += gt[i].y; mdz += gt[i].z;
                n++;
            }
            if (n < 3)
            {
                Debug.LogWarning("[YorungeVeri] Hizalama icin yeterli eslesme yok (" + n + "); ham SLAM cercevesi kullanilacak.");
                return;
            }
            msx /= n; msy /= n; msz /= n; mdx /= n; mdy /= n; mdz /= n;

            double num = 0, den = 0, var = 0;
            for (int i = 0; i < map.Length; i++)
            {
                int j = map[i];
                if (j < 0) continue;
                double sx = slam[j].x - msx, sy = slam[j].y - msy;
                double dx = gt[i].x - mdx, dy = gt[i].y - mdy;
                num += sx * dy - sy * dx;   // capraz carpim -> sin
                den += sx * dx + sy * dy;   // ic carpim     -> cos
                var += sx * sx + sy * sy;
            }
            double theta = Math.Atan2(num, den);
            double c = Math.Cos(theta), s = Math.Sin(theta);
            double scale = var > 1e-12 ? Math.Sqrt(num * num + den * den) / var : 1.0;
            if (scale <= 1e-6 || double.IsNaN(scale)) scale = 1.0;
            LastAlignmentScale = scale;

            double tx = mdx - scale * (c * msx - s * msy);
            double ty = mdy - scale * (s * msx + c * msy);
            double tz = mdz - scale * msz;

            for (int j = 0; j < slam.Count; j++)
            {
                var p = slam[j];
                double x = scale * (c * p.x - s * p.y) + tx;
                double y = scale * (s * p.x + c * p.y) + ty;
                double z = scale * p.z + tz;
                p.x = x; p.y = y; p.z = z;
                slam[j] = p;
            }

            double devSum = 0;
            for (int i = 0; i < map.Length; i++)
            {
                int j = map[i];
                if (j < 0) continue;
                double dx = gt[i].x - slam[j].x, dy = gt[i].y - slam[j].y, dz = gt[i].z - slam[j].z;
                devSum += Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
            LastMeanDeviationM = devSum / n;
        }

        // ------------------------------------------------------------- oynatma

        private Vector2 ResolveAnchor(List<Sample> gt)
        {
            if (Mathf.Abs(anchorLatLon.x) > 0.0001f || Mathf.Abs(anchorLatLon.y) > 0.0001f)
                return anchorLatLon;
            var map = FindObjectOfType<AbstractMap>();
            if (map != null)
            {
                var c = map.CenterLatitudeLongitude;
                if (Math.Abs(c.x) > 0.0001 || Math.Abs(c.y) > 0.0001)
                    return new Vector2((float)c.x, (float)c.y);
            }
            return new Vector2(39.8719f, 32.7302f); // proje varsayilani
        }

        private void TryMoveMap(Vector2 anchor)
        {
            var map = FindObjectOfType<AbstractMap>();
            if (map == null) return;
            if (TryUpdateMapCenter(map, anchor)) return;

            // Harita henuz Initialize edilmemisse UpdateMap NullReference atar (Start
            // sirasinda tipik). Hazir olunca bir kez daha dene.
            Action retry = null;
            retry = () =>
            {
                map.OnInitialized -= retry;
                if (this != null) TryUpdateMapCenter(map, anchor);
            };
            map.OnInitialized += retry;
        }

        private static bool TryUpdateMapCenter(AbstractMap map, Vector2 anchor)
        {
            try
            {
                map.UpdateMap(new Vector2d(anchor.x, anchor.y), map.Zoom);
                return true;
            }
            catch (Exception e)
            {
                Debug.Log("[YorungeVeri] Harita merkezi henuz guncellenemedi (" + e.GetType().Name + "), harita hazir olunca tekrar denenecek.");
                return false;
            }
        }

        private void LocalToEnu(Sample s, Sample origin, out double east, out double north, out double up)
        {
            double dx = (s.x - origin.x) * worldScale;
            double dy = (s.y - origin.y) * worldScale;
            double dz = (s.z - origin.z) * worldScale;
            if (zIsUp) { east = dx; north = dy; up = dz; }
            else { east = dx; north = dz; up = dy; }
        }

        private IEnumerator PlayCo(List<Sample> gt, List<Sample> slam, int[] map, Vector2 anchor)
        {
            const double MetersPerDegLat = 111132.95;
            double lonScale = MetersPerDegLat * Math.Cos(anchor.x * Math.PI / 180.0);
            float minPeriod = maxMessagesPerSecond > 0.01f ? 1f / maxMessagesPerSecond : 0f;

            do
            {
                var origin = gt[0];
                double t0 = gt[0].t;
                float wall0 = Time.realtimeSinceStartup;
                double lastSentT = double.NegativeInfinity;
                double prevGe = 0, prevGn = 0, prevSe = 0, prevSn = 0;
                bool hasPrevGt = false, hasPrevSlam = false;
                float gtYaw = 0f, slamYaw = 0f;

                int sinceYield = 0;
                for (int i = 0; i < gt.Count; i++)
                {
                    // Veri kumesi zamanlamasini koru (speed ile hizlandir/yavaslat).
                    float targetWall = wall0 + (float)((gt[i].t - t0) / Math.Max(0.001f, speed));
                    float wait = targetWall - Time.realtimeSinceStartup;
                    if (wait > 0f) { yield return new WaitForSecondsRealtime(wait); sinceYield = 0; }
                    else if (++sinceYield >= 256)
                    {
                        // Cok yuksek speed degerlerinde tum veri kumesi tek karede islenir ve
                        // loop aciksa oyun donar; en az bu sikliktla kareye geri don.
                        sinceYield = 0;
                        yield return null;
                    }

                    if (gt[i].t - lastSentT < minPeriod * speed) continue;
                    lastSentT = gt[i].t;

                    LocalToEnu(gt[i], origin, out double ge, out double gn, out double gu);
                    if (hasPrevGt)
                    {
                        float h = Heading(prevGe, prevGn, ge, gn);
                        if (!float.IsNaN(h)) gtYaw = h;
                    }
                    double gtLat = anchor.x + gn / MetersPerDegLat;
                    double gtLon = anchor.y + ge / lonScale;

                    float speedMps = 0f;
                    if (hasPrevGt && i > 0)
                    {
                        double dt = Math.Max(1e-3, gt[i].t - gt[i - 1].t);
                        double d = Math.Sqrt((ge - prevGe) * (ge - prevGe) + (gn - prevGn) * (gn - prevGn));
                        speedMps = (float)(d / dt / Math.Max(0.0001f, worldScale));
                    }

                    bool hasSlam = map[i] >= 0;
                    double slamLat = 0, slamLon = 0, su = 0;
                    if (hasSlam)
                    {
                        LocalToEnu(slam[map[i]], origin, out double se, out double sn, out su);
                        if (hasPrevSlam)
                        {
                            float h = Heading(prevSe, prevSn, se, sn);
                            if (!float.IsNaN(h)) slamYaw = h;
                        }
                        else slamYaw = gtYaw;
                        slamLat = anchor.x + sn / MetersPerDegLat;
                        slamLon = anchor.y + se / lonScale;
                        prevSe = se; prevSn = sn; hasPrevSlam = true;
                    }

                    string json = BuildJson(
                        ++_seq, gtLat, gtLon, gu + altitudeOffsetM, gtYaw,
                        hasSlam, slamLat, slamLon, su + altitudeOffsetM, slamYaw,
                        hasSlam ? EstimateConfidence(i, map) : 0f,
                        speedMps, i, gt.Count);
                    bridge.TryApplyDigitalTwinJson(json);

                    prevGe = ge; prevGn = gn; hasPrevGt = true;
                }
                if (loop) Debug.Log("[YorungeVeri] Tur bitti, bastan baslaniyor.");
            } while (loop && enabled);

            _routine = null;
        }

        /// <summary>
        /// Guven degeri SLAM akisinin KENDI surekliligiden turetilir (gercek ucusta ground
        /// truth bilinmez): eslesme yoksa/bosluk varsa duser, kesintisiz izlemede yukselir.
        /// Gercek ORB-SLAM3 ile izlenen harita noktasi sayisi kullanilmalidir.
        /// </summary>
        private float EstimateConfidence(int i, int[] map)
        {
            int gapBefore = 0;
            for (int k = i - 1; k >= 0 && k > i - 40 && map[k] < 0; k--) gapBefore++;
            if (gapBefore > 0) return Mathf.Clamp(0.35f + 0.015f * (40 - gapBefore), 0.2f, 0.75f);
            return 0.93f;
        }

        private static float Heading(double e0, double n0, double e1, double n1)
        {
            double de = e1 - e0, dn = n1 - n0;
            if (Math.Abs(de) < 1e-7 && Math.Abs(dn) < 1e-7) return float.NaN;
            float deg = (float)(Math.Atan2(de, dn) * 180.0 / Math.PI);
            return deg < 0f ? deg + 360f : deg;
        }

        /// <summary>
        /// JSON elle kurulur: JsonUtility null bir [Serializable] alani ATLAMAZ, varsayilan
        /// degerlerle yazar. Izleme kaybi karelerinde bu, SLAM izinin (0,0) koordinatina
        /// sicramasi demek olurdu; burada slamPose blogu yalnizca veri varken eklenir.
        /// </summary>
        private string BuildJson(
            long seq, double gtLat, double gtLon, double gtAlt, float gtYaw,
            bool hasSlam, double slamLat, double slamLon, double slamAlt, float slamYaw, float confidence,
            float speedMps, int index, int total)
        {
            long ts = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
            var sb = new StringBuilder(560);
            var ci = CultureInfo.InvariantCulture;
            sb.Append("{\"schemaVersion\":\"").Append(DigitalTwinJsonSchema.Version1).Append("\"");
            sb.Append(",\"sequenceId\":").Append(seq.ToString(ci));
            sb.Append(",\"timestampMs\":").Append(ts.ToString(ci));
            sb.Append(",\"sourceId\":\"").Append(sourceId).Append("\"");
            sb.Append(",\"authToken\":\"").Append(authToken).Append("\"");
            sb.Append(",\"vehicleType\":\"").Append(vehicleType).Append("\"");
            sb.Append(",\"missionPhase\":\"").Append(missionPhase).Append("\"");
            sb.Append(",\"pose\":{\"latitude\":").Append(gtLat.ToString("F9", ci));
            sb.Append(",\"longitude\":").Append(gtLon.ToString("F9", ci));
            sb.Append(",\"altitudeM\":").Append(gtAlt.ToString("F3", ci));
            sb.Append(",\"yawDeg\":").Append(gtYaw.ToString("F2", ci));
            sb.Append(",\"pitchDeg\":0,\"rollDeg\":0}");
            if (hasSlam)
            {
                sb.Append(",\"slamPose\":{\"latitude\":").Append(slamLat.ToString("F9", ci));
                sb.Append(",\"longitude\":").Append(slamLon.ToString("F9", ci));
                sb.Append(",\"altitudeM\":").Append(slamAlt.ToString("F3", ci));
                sb.Append(",\"yawDeg\":").Append(slamYaw.ToString("F2", ci));
                sb.Append(",\"pitchDeg\":0,\"rollDeg\":0,\"confidence\":").Append(confidence.ToString("F3", ci)).Append("}");
            }
            sb.Append(",\"telemetry\":{\"altitudeM\":").Append(gtAlt.ToString("F2", ci));
            sb.Append(",\"speedMps\":").Append(speedMps.ToString("F2", ci));
            sb.Append(",\"mode\":\"DATASET\",\"waypointIndex\":").Append(index.ToString(ci));
            sb.Append(",\"hopCount\":1,\"signalDbm\":-57,\"snrDb\":21,\"latencyMs\":38,\"packetLossPercent\":0.4");
            sb.Append(",\"batteryPercent\":").Append((98f - 60f * index / Mathf.Max(1, total)).ToString("F1", ci));
            sb.Append(",\"batteryVoltage\":24.1}");
            sb.Append(",\"ackRequested\":false}");
            return sb.ToString();
        }
    }
}
