using System;
using System.Collections.Generic;
using UnityEngine;
using Mapbox.Unity.Map;
using Mapbox.Utils;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// PDF 3.4.4 "Golge Modu" gorsel kaniti.
    /// Arac GPS ile ucarken ozgun VI-SLAM algoritmasi arka planda calisir.
    /// Bu katman GPS izini (mavi) ve SLAM izini (turuncu) ayri LineRenderer'larla
    /// harita uzerinde cizer ve anlik / ortalama / maksimum sapmayi (metre) gosterir.
    /// Boylece sistemin GNSS-bagimsiz ayni rotayi kestirebildigi juriye kanitlanir.
    ///
    /// Veri kaynagi: DigitalTwinJsonPoseBridge.OnUavGpsPose / OnUavSlamPose.
    /// Sahneye Tools > Simurgh menusunden veya elle eklenebilir; bagimliliklar otomatik bulunur.
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

        [Header("HUD")]
        [SerializeField] private bool showHud = true;

        private Vector2 _hudPos = new Vector2(-99999f, 0f);
        private bool _drag;

        private readonly List<Vector2d> _gps = new List<Vector2d>();
        private readonly List<Vector2d> _slam = new List<Vector2d>();
        private LineRenderer _gpsLine, _slamLine;
        private bool _hasGps, _hasSlam;
        private Vector2d _lastGps, _lastSlam;
        private float _devNow, _devMax, _devSum;
        private int _devN;

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

        [ContextMenu("Clear Trajectories")]
        public void ClearTrajectories()
        {
            _gps.Clear(); _slam.Clear();
            _hasGps = _hasSlam = false;
            _devNow = _devMax = _devSum = 0f; _devN = 0;
            if (_gpsLine != null) _gpsLine.positionCount = 0;
            if (_slamLine != null) _slamLine.positionCount = 0;
        }

        private void HandleGps(Vector2d latlon)
        {
            AppendPoint(_gps, latlon, ref _lastGps, ref _hasGps);
            RebuildLine(_gpsLine, _gps);
            UpdateDeviation();
        }

        private void HandleSlam(Vector2d latlon)
        {
            AppendPoint(_slam, latlon, ref _lastSlam, ref _hasSlam);
            RebuildLine(_slamLine, _slam);
            UpdateDeviation();
        }

        private void AppendPoint(List<Vector2d> list, Vector2d p, ref Vector2d last, ref bool has)
        {
            if (has && HaversineMeters(last, p) < minStepMeters)
            {
                last = p;            // konumu guncelle ama yeni vertex ekleme (cizgi sade kalsin)
                return;
            }
            list.Add(p);
            last = p; has = true;
            if (list.Count > maxPoints) list.RemoveAt(0);
        }

        private void UpdateDeviation()
        {
            if (!_hasGps || !_hasSlam) return;
            _devNow = (float)HaversineMeters(_lastGps, _lastSlam);
            if (_devNow > _devMax) _devMax = _devNow;
            _devSum += _devNow; _devN++;
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

        private void RebuildLine(LineRenderer lr, List<Vector2d> pts)
        {
            if (lr == null) return;
            if (abstractMap == null) { abstractMap = FindObjectOfType<AbstractMap>(); if (abstractMap == null) return; }
            lr.positionCount = pts.Count;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 w;
                try { w = abstractMap.GeoToWorldPosition(pts[i], true); }
                catch { continue; }
                w.y += yOffset;
                lr.SetPosition(i, w);
            }
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
            float w = 282f, h = 116f;
            // Varsayilan: sol ust, telemetri panelinin altinda. Baslik seridinden surukle.
            Vector2 def = new Vector2(16f, 172f);
            Rect r = TwinHudTheme.Drag(ref _hudPos, ref _drag, def, w, h, "twinhud_traj");
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
            y += 25f;

            float avg = _devN > 0 ? _devSum / _devN : 0f;
            Color dc = _devNow < 2f ? TwinHudTheme.Good : (_devNow < 5f ? TwinHudTheme.Warn : TwinHudTheme.Bad);
            var valStyle = new GUIStyle(TwinHudTheme.Value) { normal = { textColor = dc } };
            GUI.Label(new Rect(x, y, w - 32f, 22f), $"Sapma  {_devNow:F2} m", valStyle);
            y += 24f;
            GUI.Label(new Rect(x, y, w - 32f, 16f), $"ortalama {avg:F2} m   ·   maksimum {_devMax:F2} m", TwinHudTheme.Small);
        }
    }
}
