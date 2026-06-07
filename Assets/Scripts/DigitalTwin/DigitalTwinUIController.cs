using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Mapbox.Unity.Map;
using GroundStation.Drone;
using GroundStation.Routes;

namespace GroundStation.DigitalTwin
{
    public class DigitalTwinUIController : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject digitalTwinPanel;
        [SerializeField] private DigitalTwinMap3DMode map3DMode;
        [SerializeField] private GameObject mapViewRoot;
        public enum TwinViewMode { Mode3D, Mode2DOverlay }
        [SerializeField] private TwinViewMode twinMode = TwinViewMode.Mode3D;

        [Tooltip("Acilista otomatik sample senaryo oynat. PRODUCTION icin false birakin.")]
        [SerializeField] private bool playSampleScenarioOnOpen = false;
        [SerializeField] private DigitalTwinSampleScenarioPlayer sampleScenarioPlayer;
        [SerializeField] private bool forceLoopScenarioOnOpen = true;

        [Header("Google Earth Studio KML (opsiyonel)")]
        [SerializeField] private DigitalTwinKmlPosePlayer kmlPosePlayer;
        [Tooltip("Aciksa Twin panel acildiginda KML kamera yolu poz olarak oynatilir.")]
        [SerializeField] private bool startKmlPoseStreamOnTwinOpen;
        [SerializeField] private bool stopKmlStreamWhenTwinCloses = true;

        [Header("Buton (opsiyonel - gorsel secili durumu icin)")]
        [SerializeField] private Button digitalTwinButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private GameObject buttonHighlightWhenOpen;

        private bool _isOpen;
        private Image _panelBackgroundImage;
        private Color _panelBackgroundBaseColor = new Color(0f, 0f, 0f, 0f);
        private DroneTelemetryUI _mainTelemetryUi;
        private SurveyAreaSelector _surveyAreaSelector;
        private bool _surveySelectorWasEnabled = true;

        public bool IsOpen => _isOpen;

        private void Awake()
        {
            if (map3DMode == null)
                map3DMode = FindObjectOfType<DigitalTwinMap3DMode>();
            if (map3DMode == null)
            {
                var map = FindObjectOfType<AbstractMap>();
                if (map != null)
                    map3DMode = map.gameObject.AddComponent<DigitalTwinMap3DMode>();
            }
            if (digitalTwinPanel == null)
            {
                var go = GameObject.Find("DigitalTwinPanel");
                if (go != null) digitalTwinPanel = go;
            }
            CachePanelBackground();
            if (sampleScenarioPlayer == null)
                sampleScenarioPlayer = FindObjectOfType<DigitalTwinSampleScenarioPlayer>();
            if (kmlPosePlayer == null)
                kmlPosePlayer = FindObjectOfType<DigitalTwinKmlPosePlayer>();
            ResolveCloseButtonIfNeeded();
            if (digitalTwinPanel != null)
                digitalTwinPanel.SetActive(false);
            if (mapViewRoot != null)
                mapViewRoot.SetActive(true);
            _isOpen = false;
            if (buttonHighlightWhenOpen != null)
                buttonHighlightWhenOpen.SetActive(false);
        }

        private void OnEnable()
        {
            if (digitalTwinButton != null)
            {
                digitalTwinButton.onClick.RemoveListener(OnDigitalTwinButtonClicked);
                digitalTwinButton.onClick.AddListener(OnDigitalTwinButtonClicked);
            }
            ResolveCloseButtonIfNeeded();
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(OnCloseClicked);
                closeButton.onClick.AddListener(OnCloseClicked);
            }
        }

        private void OnDisable()
        {
            if (digitalTwinButton != null)
                digitalTwinButton.onClick.RemoveListener(OnDigitalTwinButtonClicked);
            if (closeButton != null)
                closeButton.onClick.RemoveListener(OnCloseClicked);
        }

        private void OnDigitalTwinButtonClicked()
        {
            ToggleView();
        }

        private void OnCloseClicked()
        {
            SetViewOpen(false);
        }

        public void ToggleView()
        {
            SetViewOpen(!_isOpen);
        }

        public void SetViewOpen(bool open)
        {
            _isOpen = open;

            if (digitalTwinPanel == null)
            {
                var go = GameObject.Find("DigitalTwinPanel");
                if (go != null)
                {
                    digitalTwinPanel = go;
                }
                else
                {
                    foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                    {
                        foreach (Transform c in root.GetComponentsInChildren<Transform>(true))
                        {
                            if (c.name == "DigitalTwinPanel") { digitalTwinPanel = c.gameObject; break; }
                        }
                        if (digitalTwinPanel != null) break;
                    }
                }
                if (digitalTwinPanel == null)
                {
                    Debug.LogError("[DigitalTwinUI] DigitalTwinPanel bulunamadi. SetViewOpen iptal edildi.");
                    return;
                }
            }

            digitalTwinPanel.SetActive(_isOpen);

            // Close button listener'i sadece OnEnable'da ekliyoruz.
            // SetViewOpen her cagrildiginda yeniden eklemek duplicate listener'a yol acar.
            // Eger panel yeni actiysa ve closeButton hala null ise coz:
            if (_isOpen && closeButton == null)
            {
                ResolveCloseButtonIfNeeded();
                if (closeButton != null)
                {
                    closeButton.onClick.RemoveListener(OnCloseClicked);
                    closeButton.onClick.AddListener(OnCloseClicked);
                }
            }

            if (map3DMode != null)
            {
                try
                {
                    if (_isOpen)
                    {
                        if (twinMode == TwinViewMode.Mode3D)
                            map3DMode.Enable3DForTwin();
                    }
                    else
                    {
                        map3DMode.Disable3DForTwin();
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[DigitalTwinUI] 3D mode transition failed. " + e.Message);
                }
            }

            ApplyTwinViewMode(_isOpen);
            ApplyHudVisibilityForTwin(_isOpen);

            // Mapbox haritasinin her zaman render edilmesi gerekiyor (RenderTexture icin).
            if (mapViewRoot != null)
                mapViewRoot.SetActive(true);

            if (buttonHighlightWhenOpen != null)
                buttonHighlightWhenOpen.SetActive(_isOpen);

            if (_isOpen && twinMode == TwinViewMode.Mode2DOverlay)
                EnsureMapImageryVisible();

            // Sample senaryo: Inspector'da playSampleScenarioOnOpen = true yapilmadikca baslamaz.
            // PRODUCTION kullanimi icin false birakin; test icin true yapabilirsiniz.
            if (_isOpen && playSampleScenarioOnOpen && sampleScenarioPlayer != null)
            {
                if (forceLoopScenarioOnOpen)
                    SetSampleLoop(sampleScenarioPlayer, true);
                sampleScenarioPlayer.PlaySampleScenario(0.2f);
            }

            if (_isOpen && startKmlPoseStreamOnTwinOpen)
            {
                if (kmlPosePlayer == null)
                    kmlPosePlayer = FindObjectOfType<DigitalTwinKmlPosePlayer>();
                if (kmlPosePlayer != null)
                    kmlPosePlayer.BeginPlaybackFromKml();
            }
            else if (!_isOpen && stopKmlStreamWhenTwinCloses)
            {
                if (kmlPosePlayer == null)
                    kmlPosePlayer = FindObjectOfType<DigitalTwinKmlPosePlayer>();
                if (kmlPosePlayer != null)
                    kmlPosePlayer.StopPlayback();
            }

            if (!_isOpen)
                RestoreMainRouteVisuals();
        }

        private void ApplyTwinViewMode(bool open)
        {
            if (digitalTwinPanel == null) return;
            CachePanelBackground();

            var twin3DViews = digitalTwinPanel.GetComponentsInChildren<DigitalTwin3DView>(true);
            if (twin3DViews != null && twin3DViews.Length > 0)
            {
                for (int i = 0; i < twin3DViews.Length; i++)
                {
                    if (twin3DViews[i] == null) continue;
                    twin3DViews[i].enabled = open && twinMode == TwinViewMode.Mode3D;
                }
            }

            // 2D overlay modda panel arka plani haritayi kapatmasin.
            if (_panelBackgroundImage != null)
            {
                if (open && twinMode == TwinViewMode.Mode2DOverlay)
                {
                    var c = _panelBackgroundImage.color;
                    c.a = 0f;
                    _panelBackgroundImage.color = c;
                    _panelBackgroundImage.raycastTarget = false;
                }
                else
                {
                    _panelBackgroundImage.color = _panelBackgroundBaseColor;
                }
            }

            if (twinMode == TwinViewMode.Mode2DOverlay)
            {
                // 2D Overlay modda: GES marker disindaki tum RawImage'lari kapat.
                // NOT: Mode3D'de bu blok hic calismiyor, RawImage'lar dokunulmuyor.
                var raws = digitalTwinPanel.GetComponentsInChildren<RawImage>(true);
                for (int i = 0; i < raws.Length; i++)
                {
                    var r = raws[i];
                    if (r == null) continue;
                    if (r.GetComponent<DigitalTwinGesImageryView>() != null)
                        continue;
                    r.texture = null;
                    r.enabled = false;
                    var c = r.color;
                    c.a = 0f;
                    r.color = c;
                    r.raycastTarget = false;
                }

                // Tam ekran beyaz Image katmanlari haritayi kapatir; seffaf yap.
                if (open)
                {
                    var panelImages = digitalTwinPanel.GetComponentsInChildren<Image>(true);
                    for (int i = 0; i < panelImages.Length; i++)
                    {
                        var im = panelImages[i];
                        if (im == null || im == _panelBackgroundImage) continue;
                        var rt = im.rectTransform;
                        if (rt == null) continue;
                        bool fullBleed = rt.anchorMin == Vector2.zero &&
                                         rt.anchorMax == Vector2.one &&
                                         Mathf.Abs(rt.offsetMin.x) < 3f && Mathf.Abs(rt.offsetMin.y) < 3f &&
                                         Mathf.Abs(rt.offsetMax.x) < 3f && Mathf.Abs(rt.offsetMax.y) < 3f;
                        if (!fullBleed) continue;
                        var c = im.color;
                        if (c.r > 0.85f && c.g > 0.85f && c.b > 0.85f && c.a > 0.12f)
                        {
                            c.a = 0f;
                            im.color = c;
                            im.raycastTarget = false;
                        }
                    }
                }
            }

            var routeViews = digitalTwinPanel.GetComponentsInChildren<DigitalTwinRouteView>(true);
            for (int i = 0; i < routeViews.Length; i++)
            {
                if (routeViews[i] != null)
                    routeViews[i].gameObject.SetActive(true);
            }

            var droneViews = digitalTwinPanel.GetComponentsInChildren<DigitalTwinDroneView>(true);
            for (int i = 0; i < droneViews.Length; i++)
            {
                if (droneViews[i] != null)
                    droneViews[i].gameObject.SetActive(true);
            }
        }

        private void CachePanelBackground()
        {
            if (digitalTwinPanel == null) return;
            if (_panelBackgroundImage != null) return;
            _panelBackgroundImage = digitalTwinPanel.GetComponent<Image>();
            if (_panelBackgroundImage != null)
                _panelBackgroundBaseColor = _panelBackgroundImage.color;
        }

        private void EnsureMapImageryVisible()
        {
            var map = FindObjectOfType<AbstractMap>();
            if (map == null || map.ImageLayer == null) return;

            try
            {
                map.ImageLayer.SetLayerSource(ImagerySourceType.MapboxSatellite);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[DigitalTwinUI] Failed to set satellite imagery: " + e.Message);
            }

            try
            {
                map.UpdateMap();
            }
            catch
            {
                // Bazi Mapbox kurulumlarinda UpdateMap hazir olmayan options ile hata atar; yok say.
            }

            try
            {
                if (map.TileProvider != null)
                    map.TileProvider.UpdateTileExtent();
            }
            catch
            {
            }
        }

        private void ResolveCloseButtonIfNeeded()
        {
            if (closeButton != null) return;
            if (digitalTwinPanel == null) return;

            Button fallback = null;
            foreach (var b in digitalTwinPanel.GetComponentsInChildren<Button>(true))
            {
                string n = b.gameObject.name.ToLowerInvariant();
                var txt = b.GetComponentInChildren<Text>(true);
                string t = txt != null ? txt.text.ToLowerInvariant() : string.Empty;
                if (n.Contains("close") || n.Contains("kapat") || n == "x" || t == "x" || t.Contains("close") || t.Contains("kapat"))
                {
                    closeButton = b;
                    break;
                }
                if (fallback == null && b != digitalTwinButton)
                    fallback = b;
            }
            if (closeButton == null)
                closeButton = fallback;
        }

        private void RestoreMainRouteVisuals()
        {
            var routeVisualizer = FindObjectOfType<RouteVisualizer>(true);
            if (routeVisualizer != null)
            {
                routeVisualizer.gameObject.SetActive(true);
                routeVisualizer.RefreshVisual();
            }

            var markerVisualizer = FindObjectOfType<RouteMarkerVisualizer>(true);
            if (markerVisualizer != null)
            {
                markerVisualizer.gameObject.SetActive(true);
                markerVisualizer.RefreshMarkers();
            }
        }

        private static void SetSampleLoop(DigitalTwinSampleScenarioPlayer player, bool value)
        {
            if (player == null) return;
            var f = typeof(DigitalTwinSampleScenarioPlayer).GetField("loop",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f != null) f.SetValue(player, value);
        }

        private void ApplyHudVisibilityForTwin(bool twinOpen)
        {
            if (_mainTelemetryUi == null)
                _mainTelemetryUi = FindObjectOfType<DroneTelemetryUI>(true);
            if (_mainTelemetryUi != null)
                SetDroneTelemetryVisible(_mainTelemetryUi, !twinOpen);

            if (_surveyAreaSelector == null)
                _surveyAreaSelector = FindObjectOfType<SurveyAreaSelector>(true);
            if (_surveyAreaSelector != null)
            {
                if (twinOpen)
                {
                    _surveySelectorWasEnabled = _surveyAreaSelector.enabled;
                    _surveyAreaSelector.enabled = false;
                }
                else
                {
                    _surveyAreaSelector.enabled = _surveySelectorWasEnabled;
                }
            }

            var surveyDbg = GameObject.Find("SurveySelectorDebug");
            if (surveyDbg != null)
                surveyDbg.SetActive(!twinOpen);
        }

        private static void SetDroneTelemetryVisible(DroneTelemetryUI telemetry, bool visible)
        {
            if (telemetry == null) return;
            SetTextObjectVisible(telemetry.AltitudeTextUI, visible);
            SetTextObjectVisible(telemetry.SpeedTextUI, visible);
            SetTextObjectVisible(telemetry.ModeTextUI, visible);
            SetTextObjectVisible(telemetry.WaypointIndexTextUI, visible);
            SetTextObjectVisible(telemetry.FlightDurationTextUI, visible);
            SetTextObjectVisible(telemetry.FpsTextUI, visible);
        }

        private static void SetTextObjectVisible(Text text, bool visible)
        {
            if (text != null)
                text.gameObject.SetActive(visible);
        }
    }
}
