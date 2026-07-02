using UnityEngine;
using UnityEngine.UI;
using GroundStation.Drone;
using GroundStation.Routes;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// Tek kaynaktan (DroneWaypointFollower + RouteManager) veri okur; Digital Twin view ve telemetry'yi gunceller.
    /// Panel acikken her frame calisir.
    /// </summary>
    public class DigitalTwinPresenter : MonoBehaviour
    {
        [Header("Data source")]
        [SerializeField] private DroneWaypointFollower drone;
        [SerializeField] private RouteManager routeManager;
        [SerializeField] private DroneTelemetryUI mainTelemetryUI;
        [SerializeField] private bool mirrorMainTelemetryPanelFormat = true;
        [Tooltip("JSON koprusu telemetri yazdiginda twin panelde bunu onceliklendir.")]
        [SerializeField] private DigitalTwinRemoteState jsonTelemetryOverride;

        [Header("View")]
        [SerializeField] private RectTransform twinPanelRoot;
        [SerializeField] private RectTransform twinViewport;
        [SerializeField] private DigitalTwinDroneView droneView;
        [SerializeField] private DigitalTwinRouteView routeView;

        [Header("Twin panel telemetry (opsiyonel)")]
        [SerializeField] private Text altitudeText;
        [SerializeField] private Text speedText;
        [SerializeField] private Text modeText;
        [SerializeField] private Text waypointIndexText;
        [SerializeField] private Text flightDurationText;
        [SerializeField] private Text fpsText;
        [SerializeField] private Text missionText;
        [SerializeField] private Text meshStatusText;
        [SerializeField] private Text warningText;
        [SerializeField] private RectTransform twinTelemetryRoot;

        [Header("World bounds (XZ)")]
        [Tooltip("Rota bosken kullanilacak alan yari capi (metre).")]
        [SerializeField] private float defaultBoundsRadius = 100f;
        [SerializeField] private float boundsPadding = 20f;

        private float _minX, _maxX, _minZ, _maxZ;
        private bool _boundsDirty = true;

        private bool _telemetryResolved;
        private bool _viewRefsResolved;
        private bool _viewportBackgroundEnsured;
        private float _nextSourceResolveTime;
        private bool _mirroredLayoutOnce;
        private bool _fallbackTelemetryCreated;
        private static Sprite _fallbackUiSprite;
        private DigitalTwin3DView _cachedTwin3DView;
        private float _nextTwin3DViewRefreshTime;

        private void Awake()
        {
            if (drone == null) drone = FindObjectOfType<DroneWaypointFollower>();
            if (routeManager == null) routeManager = FindObjectOfType<RouteManager>();
            if (mainTelemetryUI == null) mainTelemetryUI = FindObjectOfType<DroneTelemetryUI>();
            ResolveViewRefsIfNeeded();
        }

        private void Start()
        {
            ResolveViewRefsIfNeeded();
            ResolveTelemetryRefsIfNeeded();
        }

        /// <summary>
        /// Twin Viewport, Drone View, Route View atanmamissa panel icinde isimle bulur.
        /// </summary>
        private void ResolveViewRefsIfNeeded()
        {
            if (_viewRefsResolved && twinViewport != null && droneView != null && routeView != null)
                return;

            if (twinViewport == null)
            {
                Transform searchRoot = transform;
                for (int attempt = 0; attempt < 3 && searchRoot != null; attempt++)
                {
                    var t = searchRoot.Find("TwinViewPort");
                    if (t == null) t = searchRoot.Find("TwinViewport");
                    if (t == null)
                    {
                        foreach (Transform c in searchRoot)
                        {
                            if (c.name.ToLowerInvariant().Contains("twinview"))
                            {
                                t = c;
                                break;
                            }
                        }
                    }
                    if (t != null)
                    {
                        twinViewport = t as RectTransform ?? t.GetComponent<RectTransform>();
                        break;
                    }
                    searchRoot = searchRoot.parent;
                }
            }

            if (twinViewport != null)
            {
                if (droneView == null)
                    droneView = twinViewport.GetComponentInChildren<DigitalTwinDroneView>(true);
                if (routeView == null)
                    routeView = twinViewport.GetComponent<DigitalTwinRouteView>();
                if (routeView == null)
                    routeView = twinViewport.GetComponentInChildren<DigitalTwinRouteView>(true);

                if (droneView == null)
                    droneView = CreateFallbackDroneView(twinViewport);
            }

            _viewRefsResolved = true;
        }

        private static DigitalTwinDroneView CreateFallbackDroneView(RectTransform parent)
        {
            if (parent == null) return null;

            var go = new GameObject("TwinDroneFallback", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(DigitalTwinDroneView));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(24f, 24f);

            var img = go.GetComponent<Image>();
            img.sprite = EnsureFallbackUiSprite();
            img.color = new Color(1f, 0.1f, 0.1f, 1f);
            img.raycastTarget = false;

            return go.GetComponent<DigitalTwinDroneView>();
        }

        private static Sprite EnsureFallbackUiSprite()
        {
            if (_fallbackUiSprite != null) return _fallbackUiSprite;
            var tex = Texture2D.whiteTexture;
            _fallbackUiSprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f);
            return _fallbackUiSprite;
        }

        /// <summary>
        /// Inspector'da atanmamissa panel icindeki tum Text'leri tarayip isimle eslestirir (kapali objeler dahil).
        /// </summary>
        private void ResolveTelemetryRefsIfNeeded()
        {
            if (_telemetryResolved) return;
            if (HasTwinScopedTelemetryRefs())
            {
                _telemetryResolved = true;
                return;
            }

            Transform root = ResolveTwinPanelRoot();
            if (root == null) root = transform;
            var texts = root.GetComponentsInChildren<Text>(true);
            foreach (var text in texts)
            {
                if (!InScope(text, root)) continue;
                string n = text.gameObject.name.ToLowerInvariant();
                if (altitudeText == null && (n.Contains("altitude") || n.Contains("yukseklik")))
                    altitudeText = text;
                else if (speedText == null && (n.Contains("speed") || n.Contains("hiz")))
                    speedText = text;
                else if (modeText == null && n.Contains("mode"))
                    modeText = text;
                else if (waypointIndexText == null && (n.Contains("wpindex") || n.Contains("waypoint") || n.Contains("wp index")))
                    waypointIndexText = text;
                else if (flightDurationText == null && (n.Contains("flight") || n.Contains("ucus") || n.Contains("sure")))
                    flightDurationText = text;
                else if (fpsText == null && n.Contains("fps"))
                    fpsText = text;
                else if (missionText == null && (n.Contains("mission") || n.Contains("faz") || n.Contains("phase")))
                    missionText = text;
                else if (meshStatusText == null && (n.Contains("mesh") || n.Contains("link") || n.Contains("signal")))
                    meshStatusText = text;
                else if (warningText == null && (n.Contains("warn") || n.Contains("uyari") || n.Contains("alert")))
                    warningText = text;
            }

            EnsureFallbackTelemetryUiIfMissing();
            _telemetryResolved = (altitudeText != null || speedText != null || modeText != null || waypointIndexText != null || flightDurationText != null || fpsText != null);
        }

        private bool HasTwinScopedTelemetryRefs()
        {
            Transform root = ResolveTwinPanelRoot();
            return IsTwinScopedText(altitudeText) &&
                   IsTwinScopedText(speedText) &&
                   IsTwinScopedText(modeText) &&
                   IsTwinScopedText(waypointIndexText) &&
                   InScope(altitudeText, root) &&
                   InScope(speedText, root) &&
                   InScope(modeText, root) &&
                   InScope(waypointIndexText, root);
        }

        private bool IsTwinScopedText(Text txt)
        {
            if (txt == null) return false;
            Transform twinRoot = ResolveTwinPanelRoot();
            if (twinRoot == null) twinRoot = transform;
            return txt.transform.IsChildOf(twinRoot);
        }

        private static bool InScope(Text txt, Transform root)
        {
            return txt != null && root != null && txt.transform.IsChildOf(root);
        }

        private Transform ResolveTwinPanelRoot()
        {
            if (twinPanelRoot != null) return twinPanelRoot;
            if (twinViewport != null && twinViewport.parent != null)
                twinPanelRoot = twinViewport.parent as RectTransform;
            if (twinPanelRoot == null)
                twinPanelRoot = transform as RectTransform;
            return twinPanelRoot;
        }

        /// <summary>
        /// Viewport tamamen siyah kalmasin diye bir kez arka plan rengini koyu gri yapar (veya Image ekler).
        /// </summary>
        private void EnsureViewportVisibleBackground()
        {
            if (_viewportBackgroundEnsured || twinViewport == null) return;

            var img = twinViewport.GetComponent<Image>();
            bool createdNow = false;
            if (img == null)
            {
                img = twinViewport.gameObject.AddComponent<Image>();
                createdNow = true;
            }
            if (img != null)
            {
                // Yeni eklenen Image varsayilan beyaz gelir; beyaz perdeyi engelle.
                if (createdNow)
                {
                    img.color = new Color(0.10f, 0.12f, 0.16f, 0.06f);
                    img.raycastTarget = false;
                }
                else if (img.color.r > 0.9f && img.color.g > 0.9f && img.color.b > 0.9f && img.color.a > 0.2f)
                {
                    // Sahnedeki olasi beyaz viewport image'i otomatik yumuşat.
                    img.color = new Color(0.10f, 0.12f, 0.16f, 0.06f);
                    img.raycastTarget = false;
                }
                else if (img.color.r < 0.1f && img.color.g < 0.1f && img.color.b < 0.1f)
                {
                    // Tam siyahsa daha okunur koyu gri-maviye cek.
                    img.color = new Color(0.12f, 0.12f, 0.18f, img.color.a <= 0.01f ? 0.06f : Mathf.Min(img.color.a, 0.12f));
                }
            }
            _viewportBackgroundEnsured = true;
        }

        private void OnEnable()
        {
            if (routeManager != null)
                routeManager.OnRouteChanged += OnRouteChanged;
            _mirroredLayoutOnce = false;
            _nextTwin3DViewRefreshTime = 0f;
        }

        private void OnDisable()
        {
            if (routeManager != null)
                routeManager.OnRouteChanged -= OnRouteChanged;
        }

        private void OnRouteChanged(RouteData _)
        {
            _boundsDirty = true;
        }

        private void LateUpdate()
        {
            EnsureDataSources();
            var twin3D = GetCachedTwin3DView();
            bool use3DOnly = twin3D != null && twin3D.enabled;
            if (use3DOnly)
            {
                ResolveTelemetryRefsIfNeeded();
                UpdateTelemetry();
                return;
            }
            ResolveViewRefsIfNeeded();
            if (twinViewport == null)
                return;
            if (!twinViewport.gameObject.activeInHierarchy)
                return;

            EnsureViewportVisibleBackground();
            RefreshBounds();
            UpdateDroneView();
            UpdateRouteView();
            UpdateTelemetry();
        }

        private DigitalTwin3DView GetCachedTwin3DView()
        {
            if (Time.unscaledTime >= _nextTwin3DViewRefreshTime)
            {
                _cachedTwin3DView = GetComponentInChildren<DigitalTwin3DView>(true);
                _nextTwin3DViewRefreshTime = Time.unscaledTime + 2f;
            }
            return _cachedTwin3DView;
        }

        private void EnsureDataSources()
        {
            if (Time.unscaledTime < _nextSourceResolveTime) return;
            _nextSourceResolveTime = Time.unscaledTime + 1f;
            if (drone == null) drone = FindObjectOfType<DroneWaypointFollower>();
            if (routeManager == null) routeManager = FindObjectOfType<RouteManager>();
            if (mainTelemetryUI == null) mainTelemetryUI = FindObjectOfType<DroneTelemetryUI>();
            if (jsonTelemetryOverride == null) jsonTelemetryOverride = FindObjectOfType<DigitalTwinRemoteState>();
            if (twinTelemetryRoot == null && altitudeText != null)
                twinTelemetryRoot = altitudeText.transform.parent as RectTransform;
        }

        private void RefreshBounds()
        {
            if (!_boundsDirty && routeManager == null)
                return;

            float cx = 0f, cz = 0f;
            bool hasAny = false;

            if (drone != null)
            {
                var p = drone.transform.position;
                cx = p.x; cz = p.z;
                _minX = p.x - defaultBoundsRadius;
                _maxX = p.x + defaultBoundsRadius;
                _minZ = p.z - defaultBoundsRadius;
                _maxZ = p.z + defaultBoundsRadius;
                hasAny = true;
            }

            var data = routeManager != null ? routeManager.GetRouteData() : null;
            if (data != null && data.waypoints != null && data.waypoints.Count > 0)
            {
                for (int i = 0; i < data.waypoints.Count; i++)
                {
                    var wp = data.waypoints[i];
                    float x = wp.worldPosition.x, z = wp.worldPosition.z;
                    if (!hasAny)
                    {
                        _minX = x; _maxX = x; _minZ = z; _maxZ = z;
                        hasAny = true;
                    }
                    else
                    {
                        if (x < _minX) _minX = x;
                        if (x > _maxX) _maxX = x;
                        if (z < _minZ) _minZ = z;
                        if (z > _maxZ) _maxZ = z;
                    }
                }
            }

            if (!hasAny)
            {
                _minX = cx - defaultBoundsRadius;
                _maxX = cx + defaultBoundsRadius;
                _minZ = cz - defaultBoundsRadius;
                _maxZ = cz + defaultBoundsRadius;
            }

            float pad = boundsPadding;
            _minX -= pad; _maxX += pad; _minZ -= pad; _maxZ += pad;
            float rangeX = _maxX - _minX;
            float rangeZ = _maxZ - _minZ;
            if (rangeX < 1f) { _minX -= 0.5f; _maxX += 0.5f; }
            if (rangeZ < 1f) { _minZ -= 0.5f; _maxZ += 0.5f; }
            _boundsDirty = false;
        }

        private Vector2 WorldXZToViewport(Vector3 worldPos)
        {
            float rangeX = _maxX - _minX;
            float rangeZ = _maxZ - _minZ;
            if (rangeX < 0.0001f) rangeX = 1f;
            if (rangeZ < 0.0001f) rangeZ = 1f;
            float nx = (worldPos.x - _minX) / rangeX;
            float nz = (worldPos.z - _minZ) / rangeZ;
            var rect = twinViewport.rect;
            float w = rect.width;
            float h = rect.height;
            if (w < 50f) w = 800f;
            if (h < 50f) h = 600f;
            float localX = (nx - 0.5f) * w;
            float localZ = (nz - 0.5f) * h;
            return new Vector2(localX, localZ);
        }

        private void UpdateDroneView()
        {
            if (droneView == null || drone == null) return;

            Vector2 viewPos = WorldXZToViewport(drone.transform.position);
            float headingDeg = drone.transform.eulerAngles.y;
            droneView.SetPosition(viewPos);
            droneView.SetRotation(headingDeg);
        }

        private void UpdateRouteView()
        {
            if (routeView == null) return;

            var data = routeManager != null ? routeManager.GetRouteData() : null;
            if (data == null || data.waypoints == null || data.waypoints.Count == 0)
            {
                routeView.SetWaypoints(null);
                return;
            }

            var viewPoints = new Vector2[data.waypoints.Count];
            for (int i = 0; i < data.waypoints.Count; i++)
                viewPoints[i] = WorldXZToViewport(data.waypoints[i].worldPosition);
            routeView.SetWaypoints(viewPoints);
        }

        private void UpdateTelemetry()
        {
            ResolveTelemetryRefsIfNeeded();
            if (!_telemetryResolved) ResolveTelemetryRefsIfNeeded();
            if (drone == null) return;

            if (jsonTelemetryOverride != null && jsonTelemetryOverride.UseJsonTelemetry)
            {
                if (altitudeText != null && !string.IsNullOrEmpty(jsonTelemetryOverride.LastAltitudeText))
                    altitudeText.text = jsonTelemetryOverride.LastAltitudeText;
                if (speedText != null && !string.IsNullOrEmpty(jsonTelemetryOverride.LastSpeedText))
                    speedText.text = jsonTelemetryOverride.LastSpeedText;
                if (modeText != null && !string.IsNullOrEmpty(jsonTelemetryOverride.LastModeText))
                {
                    string mode = jsonTelemetryOverride.LastModeText;
                    if (!string.IsNullOrEmpty(jsonTelemetryOverride.LastSourceId))
                        mode += " [" + jsonTelemetryOverride.LastSourceId + "]";
                    modeText.text = mode;
                }
                if (waypointIndexText != null && !string.IsNullOrEmpty(jsonTelemetryOverride.LastWaypointText))
                    waypointIndexText.text = jsonTelemetryOverride.LastWaypointText;
                if (missionText != null && !string.IsNullOrEmpty(jsonTelemetryOverride.LastMissionText))
                    missionText.text = jsonTelemetryOverride.LastMissionText;
                if (meshStatusText != null)
                {
                    string meshLine = jsonTelemetryOverride.LastMeshText ?? "";
                    bool hasMesh = !string.IsNullOrEmpty(meshLine);
                    bool hasImg = !string.IsNullOrEmpty(jsonTelemetryOverride.LastImageryStatusText);
                    if (hasImg)
                        meshLine = hasMesh
                            ? jsonTelemetryOverride.LastImageryStatusText + "\n" + meshLine
                            : jsonTelemetryOverride.LastImageryStatusText;
                    if (hasMesh || hasImg)
                        meshStatusText.text = meshLine;
                }
                if (warningText != null)
                {
                    string counters = string.Format("Objeler: Engeller {0} | Hedefler {1}",
                        jsonTelemetryOverride.OperationalState.obstacleCount,
                        jsonTelemetryOverride.OperationalState.targetCount);
                    string voxels = string.Format("Voxel: {0}", jsonTelemetryOverride.OperationalState.voxelCount);
                    string link = jsonTelemetryOverride.LastVehicleStatusText;
                    if (string.IsNullOrEmpty(jsonTelemetryOverride.LastWarningText))
                        warningText.text = string.IsNullOrEmpty(link) ? counters + " | " + voxels : link + " | " + counters + " | " + voxels;
                    else
                        warningText.text = string.IsNullOrEmpty(link)
                            ? jsonTelemetryOverride.LastWarningText
                            : link + " | " + jsonTelemetryOverride.LastWarningText;
                }
                if (flightDurationText != null && mainTelemetryUI != null && !string.IsNullOrEmpty(mainTelemetryUI.LastFlightDurationText))
                    flightDurationText.text = mainTelemetryUI.LastFlightDurationText;
                else if (flightDurationText != null)
                {
                    float sec = drone.FlightDurationSeconds;
                    int m = Mathf.FloorToInt(sec / 60f);
                    int s = Mathf.FloorToInt(sec % 60f);
                    flightDurationText.text = string.Format("U\u00E7u\u015F: {0:D2}:{1:D2}", m, s);
                }
                if (fpsText != null)
                {
                    if (mainTelemetryUI != null && !string.IsNullOrEmpty(mainTelemetryUI.LastFpsText))
                        fpsText.text = mainTelemetryUI.LastFpsText;
                    else
                        fpsText.text = string.Format("FPS: {0:F0}", 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime));
                }
                return;
            }

            if (mirrorMainTelemetryPanelFormat && mainTelemetryUI != null)
            {
                MirrorTelemetryLayoutFromMainIfNeeded();
                if (altitudeText != null && !string.IsNullOrEmpty(mainTelemetryUI.LastAltitudeText))
                    altitudeText.text = mainTelemetryUI.LastAltitudeText;
                if (speedText != null && !string.IsNullOrEmpty(mainTelemetryUI.LastSpeedText))
                    speedText.text = mainTelemetryUI.LastSpeedText;
                if (modeText != null && !string.IsNullOrEmpty(mainTelemetryUI.LastModeText))
                    modeText.text = mainTelemetryUI.LastModeText;
                if (waypointIndexText != null && !string.IsNullOrEmpty(mainTelemetryUI.LastWaypointText))
                    waypointIndexText.text = mainTelemetryUI.LastWaypointText;
                if (missionText != null)
                    missionText.text = "";
                if (meshStatusText != null)
                    meshStatusText.text = "";
                if (warningText != null)
                    warningText.text = "";
                if (flightDurationText != null)
                {
                    if (!string.IsNullOrEmpty(mainTelemetryUI.LastFlightDurationText))
                        flightDurationText.text = mainTelemetryUI.LastFlightDurationText;
                    else
                    {
                        float sec = drone.FlightDurationSeconds;
                        int m = Mathf.FloorToInt(sec / 60f);
                        int s = Mathf.FloorToInt(sec % 60f);
                        flightDurationText.text = string.Format("Uçuş: {0:D2}:{1:D2}", m, s);
                    }
                }
                if (fpsText != null)
                {
                    if (!string.IsNullOrEmpty(mainTelemetryUI.LastFpsText))
                        fpsText.text = mainTelemetryUI.LastFpsText;
                    else
                        fpsText.text = string.Format("FPS: {0:F0}", 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime));
                }
                return;
            }

            float alt = drone.transform.position.y;
            float speed = drone.CurrentSpeed;
            int wpIndex = drone.CurrentWaypointIndex;
            bool running = drone.IsRunning;

            if (altitudeText != null)
                altitudeText.text = string.Format("Y\u00FCkseklik: {0:F1} m", alt);
            if (speedText != null)
                speedText.text = string.Format("H\u0131z: {0:F1} m/s", speed);
            if (modeText != null)
                modeText.text = running ? "Mod: GUIDED (Route)" : "Mod: IDLE";
            if (waypointIndexText != null)
                waypointIndexText.text = string.Format("WP Index: {0}", wpIndex);
            if (missionText != null)
                missionText.text = "";
            if (meshStatusText != null)
                meshStatusText.text = "";
            if (warningText != null)
                warningText.text = "";
            if (flightDurationText != null)
            {
                float sec = drone.FlightDurationSeconds;
                int m = Mathf.FloorToInt(sec / 60f);
                int s = Mathf.FloorToInt(sec % 60f);
                flightDurationText.text = string.Format("Uçuş: {0:D2}:{1:D2}", m, s);
            }
            if (fpsText != null)
                fpsText.text = string.Format("FPS: {0:F0}", 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime));
        }

        private void MirrorTelemetryLayoutFromMainIfNeeded()
        {
            if (_mirroredLayoutOnce || mainTelemetryUI == null) return;
            ResolveTwinTelemetryRootIfNeeded();

            CopyTextStyle(altitudeText, mainTelemetryUI.AltitudeTextUI);
            CopyTextStyle(speedText, mainTelemetryUI.SpeedTextUI);
            CopyTextStyle(modeText, mainTelemetryUI.ModeTextUI);
            CopyTextStyle(waypointIndexText, mainTelemetryUI.WaypointIndexTextUI);
            CopyTextStyle(flightDurationText, mainTelemetryUI.FlightDurationTextUI);
            CopyTextStyle(fpsText, mainTelemetryUI.FpsTextUI);

            _mirroredLayoutOnce = true;
        }

        private void ResolveTwinTelemetryRootIfNeeded()
        {
            if (twinTelemetryRoot != null) return;
            if (altitudeText != null)
                twinTelemetryRoot = altitudeText.transform.parent as RectTransform;
            if (twinTelemetryRoot != null) return;

            Transform root = ResolveTwinPanelRoot();
            if (root == null) root = transform;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("telemetry") || n.Contains("hud"))
                {
                    twinTelemetryRoot = t as RectTransform;
                    if (twinTelemetryRoot != null) break;
                }
            }

            if (twinTelemetryRoot == null)
                EnsureFallbackTelemetryUiIfMissing();
        }

        private static void CopyTextStyle(Text target, Text source)
        {
            if (target == null || source == null) return;
            target.fontSize = source.fontSize;
            target.fontStyle = source.fontStyle;
            target.alignment = source.alignment;
            target.color = source.color;
            target.horizontalOverflow = source.horizontalOverflow;
            target.verticalOverflow = source.verticalOverflow;
        }

        private void EnsureFallbackTelemetryUiIfMissing()
        {
            if (_fallbackTelemetryCreated) return;
            if (HasTwinScopedTelemetryRefs()) return;

            Transform root = ResolveTwinPanelRoot();
            if (root == null) root = transform;
            if (root == null) root = transform;

            var existing = root.Find("TwinTelemetryAuto");
            RectTransform panelRt;
            if (existing != null)
            {
                panelRt = existing as RectTransform ?? existing.GetComponent<RectTransform>();
            }
            else
            {
                var panel = new GameObject("TwinTelemetryAuto", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                panel.transform.SetParent(root, false);
                panelRt = panel.GetComponent<RectTransform>();
                panelRt.anchorMin = new Vector2(1f, 1f);
                panelRt.anchorMax = new Vector2(1f, 1f);
                panelRt.pivot = new Vector2(1f, 1f);
                panelRt.anchoredPosition = new Vector2(-20f, -70f);
                panelRt.sizeDelta = new Vector2(320f, 180f);

                var panelImage = panel.GetComponent<Image>();
                panelImage.color = new Color(0.05f, 0.07f, 0.10f, 0.72f);
                panelImage.raycastTarget = false;
            }

            EnsureStatusHeader(panelRt);
            twinTelemetryRoot = panelRt;
            altitudeText = EnsureFallbackLine(panelRt, "AltitudeText", 0);
            speedText = EnsureFallbackLine(panelRt, "SpeedText", 1);
            modeText = EnsureFallbackLine(panelRt, "ModeText", 2);
            waypointIndexText = EnsureFallbackLine(panelRt, "WpIndexText", 3);
            flightDurationText = EnsureFallbackLine(panelRt, "FlightDurationText", 4);
            fpsText = EnsureFallbackLine(panelRt, "FpsText", 5);

            _fallbackTelemetryCreated = true;
        }

        private static void EnsureStatusHeader(RectTransform panelRt)
        {
            if (panelRt == null) return;
            EnsureHeaderLine(panelRt, "TwinStatusTitle", "Dijital İkiz Durumu", new Vector2(12f, -8f), 18, FontStyle.Bold, new Color(0.92f, 0.95f, 1f, 1f));
            EnsureHeaderLine(panelRt, "TwinStatusBadge", "İkiz Aktif", new Vector2(12f, -32f), 14, FontStyle.Bold, new Color(0.55f, 1f, 0.55f, 1f));
        }

        private static void EnsureHeaderLine(RectTransform parent, string objectName, string label, Vector2 anchoredPos, int fontSize, FontStyle style, Color color)
        {
            Transform existing = parent.Find(objectName);
            Text txt;
            if (existing != null)
            {
                txt = existing.GetComponent<Text>();
                if (txt != null)
                {
                    txt.text = label;
                    return;
                }
            }

            var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(-16f, 24f);

            txt = go.GetComponent<Text>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) txt.font = font;
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.color = color;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Truncate;
            txt.raycastTarget = false;
            txt.text = label;
        }

        private static Text EnsureFallbackLine(RectTransform parent, string objectName, int index)
        {
            Transform existing = parent.Find(objectName);
            Text txt;
            if (existing != null)
            {
                txt = existing.GetComponent<Text>();
                if (txt != null) return txt;
            }

            var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(10f, -58f - index * 20f);
            rt.sizeDelta = new Vector2(-16f, 20f);

            txt = go.GetComponent<Text>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) txt.font = font;
            txt.fontSize = 14;
            txt.fontStyle = FontStyle.Normal;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.color = new Color(0.84f, 0.95f, 1f, 1f);
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Truncate;
            txt.raycastTarget = false;
            txt.text = "---";
            return txt;
        }
    }
}
