using GroundStation.DigitalTwin;
using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.UI
{
    /// <summary>
    /// Compact waypoint control. The altitude input is visible only while placing
    /// waypoints; the original toggle binding stays alive behind a CanvasGroup.
    /// </summary>
    public class BottomLeftUiCompactor : MonoBehaviour
    {
        [SerializeField] private bool hideAltitudePanel = true;
        [SerializeField] private bool compactWaypointStrip = true;
        [Header("Sag ust duzeni")]
        [Tooltip("Hiz/Yukseklik butonlarini sag ust koseye tasi.")]
        [SerializeField] private bool moveSpeedPanelTopRight = true;
        [SerializeField] private Vector2 speedPanelTopRightOffset = new Vector2(-14f, -14f);
        [Tooltip("Zoom +/- butonlarini gizle (tekerlek ile zoom calismaya devam eder).")]
        [SerializeField] private bool hideZoomButtons = true;
        [Tooltip("Sag HUD kolonu (Ucus Guvenligi...) hiz panelinin altindan baslasin (sanal birim).")]
        [SerializeField] private float rightColumnStartBelowSpeedPanel = 300f;
        [Tooltip("WP dugmesinin sol kenardan uzakligi (sanal 1080p birimi).")]
        [SerializeField] private float buttonX = 16f;
        [SerializeField] private float buttonBottomMargin = 14f;
        [SerializeField] private Vector2 buttonSize = new Vector2(118f, 28f);

        private bool _altDone, _stripDone, _speedDone, _zoomDone;
        private float _nextTryAt;
        private Toggle _wpToggle;
        private GUIStyle _hintStyle;
        private GameObject _altitudePanel;

        private void Update()
        {
            if (_altitudePanel != null && _wpToggle != null)
                _altitudePanel.SetActive(_wpToggle.isOn);
            if ((_altDone || !hideAltitudePanel) && (_stripDone || !compactWaypointStrip) &&
                (_speedDone || !moveSpeedPanelTopRight) && (_zoomDone || !hideZoomButtons))
                return;   // arama bitti; OnGUI dugmesi icin bilesen acik kalir
            if (Time.unscaledTime < _nextTryAt) return;
            _nextTryAt = Time.unscaledTime + 0.5f;   // bazi UI'lar runtime'da sonradan kurulur; tekrar dene

            if (hideAltitudePanel && !_altDone)
                TryHideAltitudePanel();
            if (compactWaypointStrip && !_stripDone)
                TryTakeOverStrip();
            if (moveSpeedPanelTopRight && !_speedDone)
                TryMoveSpeedPanel();
            if (hideZoomButtons && !_zoomDone)
                TryHideZoomButtons();
        }

        private void TryMoveSpeedPanel()
        {
            var sp = FindObjectOfType<DroneSpeedAltitudePanel>();
            RectTransform rt = sp != null ? sp.transform as RectTransform : null;
            if (rt == null)
            {
                // Yedek: adi hiz/speed iceren canvas paneli.
                var canvas = FindObjectOfType<Canvas>();
                if (canvas == null) return;
                foreach (Transform child in canvas.transform)
                {
                    string n = child.name.ToLowerInvariant();
                    if ((n.Contains("speed") || n.Contains("hiz")) && child is RectTransform crt) { rt = crt; break; }
                }
                if (rt == null) return;
            }

            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = speedPanelTopRightOffset;

            // Sag HUD kolonu (Ucus Guvenligi, Kamera, Hedefler) panelin altindan baslasin.
            if (DigitalTwinHudWorkspace.Instance == null)
                TwinHudTheme.RightColumnStartY = rightColumnStartBelowSpeedPanel;
            _speedDone = true;
        }

        private void TryHideZoomButtons()
        {
            var zp = FindObjectOfType<MapZoomPanel>();
            if (zp == null) return;

            // Hide the background too; otherwise it covers the speed controls.
            zp.gameObject.SetActive(false);
            _zoomDone = true;
        }

        private void TryHideAltitudePanel()
        {
            var appearance = FindObjectOfType<AltitudePanelAppearance>(true);
            if (appearance == null) return;
            _altitudePanel = appearance.gameObject;
            var rect = appearance.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0f);
                rect.anchoredPosition = new Vector2(20f, 130f);
            }
            appearance.gameObject.SetActive(false);
            _altDone = true;
        }

        private void TryTakeOverStrip()
        {
            var strip = GameObject.Find("WaypointMapEditStrip");
            if (strip == null) return;

            _wpToggle = strip.GetComponentInChildren<Toggle>(true);
            if (_wpToggle == null) return;

            // Keep the binding alive without leaving invisible raycast targets.
            HideGraphics(strip);

            // Eski ipucu metnini de ekran disina al (IMGUI ipucumuz onun yerine gecer).
            var hint = GameObject.Find("MapPlacementHint");
            if (hint == null)
            {
                var t = strip.transform.parent != null ? strip.transform.parent.Find("MapPlacementHint") : null;
                if (t != null) hint = t.gameObject;
            }
            if (hint != null)
            {
                HideGraphics(hint);
            }

            _stripDone = true;
        }

        private static void HideGraphics(GameObject root)
        {
            var group = root.GetComponent<CanvasGroup>();
            if (group == null) group = root.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }

        private void OnGUI()
        {
            if (DigitalTwinUIController.SiteWorkspaceOpen) return;
            if (_wpToggle == null) return;
            TwinHudTheme.BeginScaledHud();

            bool on = _wpToggle.isOn;
            var r = new Rect(buttonX, TwinHudTheme.ScreenH - buttonSize.y - buttonBottomMargin,
                             buttonSize.x, buttonSize.y);
            HudInputBlocker.Register(r);

            // Acikken buton altinda accent serit — durum bir bakista belli olur.
            if (on)
                TwinHudTheme.Fill(new Rect(r.x, r.yMax + 2f, r.width, 3f), TwinHudTheme.Accent, 1.5f);

            if (GUI.Button(r, on ? "WP Ekleme: AÇIK" : "WP Ekle", TwinHudTheme.Button))
                _wpToggle.isOn = !on;

            if (on)
            {
                if (_hintStyle == null)
                    _hintStyle = new GUIStyle(TwinHudTheme.Small) { alignment = TextAnchor.MiddleLeft };
                GUI.Label(new Rect(r.x, r.y - 22f, 300f, 18f), "Haritaya tıklayarak waypoint ekle", _hintStyle);
            }

            TwinHudTheme.EndScaledHud();
        }
    }
}
