using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Mapbox.Unity.Map;
using Mapbox.Utils;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// PDF 3.4.4 "Golge Modu" gorsel kaniti.
    /// Arac GPS ile ucarken ozgun VI-SLAM algoritmasi arka planda calisir.
    /// Bu katman GPS izini (mavi) ve SLAM izini (turuncu) ayri LineRenderer'larla
    /// harita uzerinde cizer; anlik/ortalama/maksimum sapma + RMSE, SLAM guven degeri
    /// ve aktif konum kaynagi (GPS / SLAM) rozetini gosterir. "CSV Kaydet" ile
    /// kestirimsel konum verileri ucus sonrasi analiz icin disa aktarilir
    /// (hakem sunumu: "kestirimsel konum verilerinin eksiksiz sunulmasi").
    ///
    /// Performans: her yeni fix'te yalnizca SON vertex eklenir (tam rebuild yok);
    /// tam rebuild yalnizca harita yeniden cizildiginde veya tampon tasarsa yapilir.
    /// </summary>
    public class DigitalTwinTrajectoryComparison : MonoBehaviour
    {
        [Header("Bagimliliklar (otomatik bulunur)")]
        [SerializeField] private AbstractMap abstractMap;
        [SerializeField] private DigitalTwinJsonPoseBridge poseBridge;

        [Header("Iz ayarlari")]
        [SerializeField] private int maxPoints = 800;
        [Tooltip("Bu mesafeden yakin ardisik noktalar yeni koseye donusmez (metre).")]
        [SerializeField] private float minStepMeters = 0.4f;
        [SerializeField] private float lineWidth = 2.4f;
        [SerializeField] private float yOffset = 3.0f;
        [SerializeField] private Color gpsColor = new Color(0.18f, 0.55f, 1f, 1f);
        [SerializeField] private Color slamColor = new Color(1f, 0.55f, 0.12f, 1f);

        [Header("Kayit")]
        [Tooltip("CSV disa aktarim icin tutulan maksimum ornek sayisi.")]
        [SerializeField] private int maxRecords = 6000;
        [SerializeField] private float lowConfidenceThreshold = 0.6f;

        [Header("HUD")]
        [SerializeField] private bool showHud = true;

        private struct TrajRecord
        {
            public long tMs;
            public double gpsLat, gpsLon, slamLat, slamLon;
            public float devM;
            public float conf;
        }

        private readonly List<Vector2d> _gps = new List<Vector2d>();
        private readonly List<Vector2d> _slam = new List<Vector2d>();
        private readonly List<TrajRecord> _records = new List<TrajRecord>();
        private LineRenderer _gpsLine, _slamLine;
        private bool _hasGps, _hasSlam;
        private Vector2d _lastGps, _lastSlam;
        private float _devNow, _devMax, _devSum;
        private double _devSqSum;
        private int _devN;
        private Vector2 _hudPos = new Vector2(-99999f, 0f);
        private bool _drag;
        private string _savedPath = "";
        private float _savedPathUntil;
        private GUIStyle _valStyle, _confStyle, _srcStyle;

        private void Awake()
        {
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (poseBridge == null) poseBridge = FindObjectOfType<DigitalTwinJsonPoseBridge>();
            EnsureLines();
        }

        private void OnEnable()
        {
            if (poseBridge == null) poseBridge = FindObjectOfType<DigitalTwinJsonPoseBridge>();
            if (poseBridge != null)
            {
                poseBridge.OnUavGpsPose += HandleGps;
                poseBridge.OnUavSlamPose += HandleSlam;
            }
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (abstractMap != null) abstractMap.OnMapRedrawn += RedrawAll;
        }

        private void OnDisable()
        {
            if (poseBridge != null)
            {
                poseBridge.OnUavGpsPose -= HandleGps;
                poseBridge.OnUavSlamPose -= HandleSlam;
            }
            if (abstractMap != null) abstractMap.OnMapRedrawn -= RedrawAll;
        }

        private void OnDestroy()
        {
            // Instanced LineRenderer materyalleri GameObject destroy ile toplanmaz.
            if (_gpsLine != null && _gpsLine.material != null) Destroy(_gpsLine.material);
            if (_slamLine != null && _slamLine.material != null) Destroy(_slamLine.material);
        }

        [ContextMenu("Clear Trajectories")]
        public void ClearTrajectories()
        {
            _gps.Clear(); _slam.Clear(); _records.Clear();
            _hasGps = _hasSlam = false;
            _devNow = _devMax = _devSum = 0f; _devSqSum = 0; _devN = 0;
            if (_gpsLine != null) _gpsLine.positionCount = 0;
            if (_slamLine != null) _slamLine.positionCount = 0;
        }

        /// <summary>
        /// Kestirimsel konum verilerini CSV olarak kaydeder; dosya yolunu dondurur.
        /// Kolonlar: timestampMs;gpsLat;gpsLon;slamLat;slamLon;sapmaM;confidence
        /// </summary>
        public string SaveTrajectoryCsv()
        {
            try
            {
                string dir = System.IO.Path.Combine(Application.persistentDataPath, "digital-twin-logs");
                System.IO.Directory.CreateDirectory(dir);
                string file = System.IO.Path.Combine(dir,
                    "yorunge_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("timestampMs;gpsLat;gpsLon;slamLat;slamLon;sapmaM;confidence");
                for (int i = 0; i < _records.Count; i++)
                {
                    var r = _records[i];
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0};{1:F7};{2:F7};{3:F7};{4:F7};{5:F2};{6:F2}",
                        r.tMs, r.gpsLat, r.gpsLon, r.slamLat, r.slamLon, r.devM, r.conf));
                }
                float rmse = Rmse();
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "# ozet; ornekler={0}; ortSapmaM={1:F2}; maksSapmaM={2:F2}; rmseM={3:F2}",
                    _devN, _devN > 0 ? _devSum / _devN : 0f, _devMax, rmse));
                System.IO.File.WriteAllText(file, sb.ToString());
                Debug.Log("[Yorunge] CSV kaydedildi: " + file);
                return file;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Yorunge] CSV kaydedilemedi: " + e.Message);
                return null;
            }
        }

        private float Rmse()
        {
            return _devN > 0 ? Mathf.Sqrt((float)(_devSqSum / _devN)) : 0f;
        }

        // Gorev ozeti panelinin okudugu istatistikler.
        public float DeviationNow => _devNow;
        public float DeviationMax => _devMax;
        public float DeviationAvg => _devN > 0 ? _devSum / _devN : 0f;
        public float DeviationRmse => Rmse();
        public int SampleCount => _devN;

        private void HandleGps(Vector2d latlon)
        {
            int change = AppendPoint(_gps, latlon, ref _lastGps, ref _hasGps);
            if (change == 1) AppendVertex(_gpsLine, latlon);
            else if (change == 2) RebuildLine(_gpsLine, _gps);
            UpdateDeviation();
        }

        private void HandleSlam(Vector2d latlon)
        {
            int change = AppendPoint(_slam, latlon, ref _lastSlam, ref _hasSlam);
            if (change == 1) AppendVertex(_slamLine, latlon);
            else if (change == 2) RebuildLine(_slamLine, _slam);
            UpdateDeviation();
        }

        /// <returns>0 = vertex eklenmedi, 1 = sona eklendi, 2 = eklendi + bas kirpildi (tam rebuild gerek)</returns>
        private int AppendPoint(List<Vector2d> list, Vector2d p, ref Vector2d last, ref bool has)
        {
            if (has && HaversineMeters(last, p) < minStepMeters)
            {
                last = p;            // konumu guncelle ama yeni vertex ekleme (cizgi sade kalsin)
                return 0;
            }
            list.Add(p);
            last = p; has = true;
            if (list.Count > maxPoints)
            {
                list.RemoveAt(0);
                return 2;
            }
            return 1;
        }

        private void UpdateDeviation()
        {
            if (!_hasGps || !_hasSlam) return;
            _devNow = (float)HaversineMeters(_lastGps, _lastSlam);
            if (_devNow > _devMax) _devMax = _devNow;
            _devSum += _devNow;
            _devSqSum += (double)_devNow * _devNow;
            _devN++;

            _records.Add(new TrajRecord
            {
                tMs = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds,
                gpsLat = _lastGps.x, gpsLon = _lastGps.y,
                slamLat = _lastSlam.x, slamLon = _lastSlam.y,
                devM = _devNow,
                conf = poseBridge != null ? poseBridge.LastSlamConfidence : -1f
            });
            if (_records.Count > Mathf.Max(100, maxRecords))
                _records.RemoveRange(0, _records.Count - maxRecords);
        }

        private void EnsureLines()
        {
            if (_gpsLine == null) _gpsLine = CreateLine("GPS_Trajectory", gpsColor);
            if (_slamLine == null) _slamLine = CreateLine("SLAM_Trajectory", slamColor);
        }

        private LineRenderer CreateLine(string lineName, Color color)
        {
            var go = new GameObject(lineName);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = lineWidth;
            lr.numCornerVertices = 2;
            lr.numCapVertices = 2;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            lr.material = new Material(shader);
            lr.startColor = color; lr.endColor = color;
            lr.positionCount = 0;
            return lr;
        }

        private void AppendVertex(LineRenderer lr, Vector2d p)
        {
            if (lr == null) return;
            if (abstractMap == null) { abstractMap = FindObjectOfType<AbstractMap>(); if (abstractMap == null) return; }
            Vector3 w;
            try { w = abstractMap.GeoToWorldPosition(p, true); }
            catch { return; }
            w.y += yOffset;
            int n = lr.positionCount;
            lr.positionCount = n + 1;
            lr.SetPosition(n, w);
        }

        private void RebuildLine(LineRenderer lr, List<Vector2d> pts)
        {
            if (lr == null) return;
            if (abstractMap == null) { abstractMap = FindObjectOfType<AbstractMap>(); if (abstractMap == null) return; }

            // Once gecerli dunya koordinatlarini topla; exception'li nokta listeye hic girmez
            // (aksi halde atlanan vertex (0,0,0)'da kalir ve cizgi origin'e sicrar).
            var valid = new List<Vector3>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 w;
                try { w = abstractMap.GeoToWorldPosition(pts[i], true); }
                catch { continue; }
                w.y += yOffset;
                valid.Add(w);
            }
            lr.positionCount = valid.Count;
            if (valid.Count > 0)
                lr.SetPositions(valid.ToArray());
        }

        private void RedrawAll()
        {
            RebuildLine(_gpsLine, _gps);
            RebuildLine(_slamLine, _slam);
        }

        private static double HaversineMeters(Vector2d a, Vector2d b)
        {
            const double R = 6371000.0, D2R = Math.PI / 180.0;
            double dLat = (b.x - a.x) * D2R, dLon = (b.y - a.y) * D2R;
            double la1 = a.x * D2R, la2 = b.x * D2R;
            double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(la1) * Math.Cos(la2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2 * R * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
        }

        private void OnGUI()
        {
            if (!showHud || (!_hasGps && !_hasSlam)) return;
            float s = TwinHudTheme.BeginScaledHud();
            float w = 282f, h = 190f;
            // Sol sutun #1 (telemetrinin altinda). Anahtar v2: eski cakisan kayitli konumlari sifirlar.
            Vector2 def = new Vector2(16f, 195f);
            Rect r = TwinHudTheme.Drag(ref _hudPos, ref _drag, def, w, h, "twinhud_traj_v3");
            TwinHudTheme.Panel(r);

            float x = r.x + 16f, y = r.y + 12f;
            GUI.Label(new Rect(x, y, w - 32f, 18f), "YÖRÜNGE  ·  Gölge Modu", TwinHudTheme.Title);
            y += 23f;
            TwinHudTheme.Separator(x, y, w - 32f);
            y += 9f;

            TwinHudTheme.Dot(new Rect(x, y + 2f, 11f, 11f), gpsColor);
            GUI.Label(new Rect(x + 18f, y, 96f, 16f), "GPS izi", TwinHudTheme.Small);
            TwinHudTheme.Dot(new Rect(x + 120f, y + 2f, 11f, 11f), slamColor);
            GUI.Label(new Rect(x + 138f, y, 134f, 16f), "SLAM (VI-SLAM)", TwinHudTheme.Small);
            y += 22f;

            // Aktif konum kaynagi rozeti (GPS kaybi senaryosu kaniti).
            var src = poseBridge != null ? poseBridge.ActivePoseSource : DigitalTwinJsonPoseBridge.TwinPoseSource.None;
            bool slamActive = src == DigitalTwinJsonPoseBridge.TwinPoseSource.Slam;
            var srcRect = new Rect(x, y, w - 32f, 20f);
            Color srcCol = slamActive ? slamColor : (src == DigitalTwinJsonPoseBridge.TwinPoseSource.Gps ? gpsColor : new Color(0.5f, 0.55f, 0.62f));
            float pulse = slamActive ? 0.55f + 0.35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5f)) : 0.22f;
            TwinHudTheme.Fill(srcRect, new Color(srcCol.r, srcCol.g, srcCol.b, pulse * 0.4f), 5f);
            if (_srcStyle == null) _srcStyle = new GUIStyle(TwinHudTheme.Badge) { fontSize = 11 };
            GUI.Label(srcRect, slamActive ? "KONUM: SLAM — GPS YOK" : (src == DigitalTwinJsonPoseBridge.TwinPoseSource.Gps ? "KONUM: GPS" : "KONUM: —"), _srcStyle);
            y += 25f;

            float avg = _devN > 0 ? _devSum / _devN : 0f;
            Color dc = _devNow < 2f ? TwinHudTheme.Good : (_devNow < 5f ? TwinHudTheme.Warn : TwinHudTheme.Bad);
            if (_valStyle == null) _valStyle = new GUIStyle(TwinHudTheme.Value);
            _valStyle.normal.textColor = dc;
            GUI.Label(new Rect(x, y, w - 32f, 22f), string.Format(CultureInfo.InvariantCulture, "Sapma  {0:F2} m", _devNow), _valStyle);
            y += 23f;
            GUI.Label(new Rect(x, y, w - 32f, 16f),
                string.Format(CultureInfo.InvariantCulture, "ort {0:F2} · maks {1:F2} · RMSE {2:F2} m", avg, _devMax, Rmse()),
                TwinHudTheme.Small);
            y += 18f;

            // SLAM guven degeri.
            float conf = poseBridge != null ? poseBridge.LastSlamConfidence : -1f;
            if (_confStyle == null) _confStyle = new GUIStyle(TwinHudTheme.Small);
            _confStyle.normal.textColor = conf < 0f ? TwinHudTheme.TextSecondary : (conf < lowConfidenceThreshold ? TwinHudTheme.Bad : TwinHudTheme.Good);
            GUI.Label(new Rect(x, y, w - 32f, 16f),
                conf < 0f ? "SLAM güven: —" : string.Format(CultureInfo.InvariantCulture, "SLAM güven: %{0:F0}{1}", conf * 100f, conf < lowConfidenceThreshold ? "  · DÜŞÜK" : ""),
                _confStyle);
            y += 20f;

            if (GUI.Button(new Rect(x, y, 96f, 20f), "CSV Kaydet", TwinHudTheme.Button))
            {
                _savedPath = SaveTrajectoryCsv();
                _savedPathUntil = Time.unscaledTime + 6f;
            }
            if (GUI.Button(new Rect(x + 104f, y, 78f, 20f), "Temizle", TwinHudTheme.Button))
                ClearTrajectories();
            if (!string.IsNullOrEmpty(_savedPath) && Time.unscaledTime < _savedPathUntil)
                GUI.Label(new Rect(x, y + 22f, w - 32f, 16f), "Kaydedildi: " + System.IO.Path.GetFileName(_savedPath), TwinHudTheme.Small);

            TwinHudTheme.EndScaledHud();
        }
    }
}
