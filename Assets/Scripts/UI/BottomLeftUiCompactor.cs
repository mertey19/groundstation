using GroundStation.DigitalTwin;
using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.UI
{
    /// <summary>
    /// Sol alt kose duzenleyici:
    ///   1) "Yukseklik (m)" giris panelini gizler (yerini karekod foto galerisi alir),
    ///   2) Buyuk "Haritadan | KAPALI" uGUI seridini ekran disina tasir ve yerine
    ///      diger panellerle AYNI temada tek bir "WP Ekle" dugmesi cizer (TwinHudTheme).
    ///      Dugme, gizli seritteki Toggle'i surer — islev birebir ayni:
    ///      ACIKken haritaya tiklayarak waypoint eklenir.
    /// Runtime'da calisir; sahne dosyasina dokunmaz. AutoBootstrap ekler.
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
        [SerializeField] private float buttonX = 340f;
        [SerializeField] private float buttonBottomMargin = 14f;
        [SerializeField] private Vector2 buttonSize = new Vector2(118f, 28f);

        private bool _altDone, _stripDone, _speedDone, _zoomDone;
        private float _nextTryAt;
        private Toggle _wpToggle;
        private GUIStyle _hintStyle;

        private void Update()
        {
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
            GroundStation.DigitalTwin.TwinHudTheme.RightColumnStartY = rightColumnStartBelowSpeedPanel;
            _speedDone = true;
        }

        private void TryHideZoomButtons()
        {
            var zp = FindObjectOfType<MapZoomPanel>();
            if (zp == null) return;

            // Butonlar MapZoomPanel'de serilestirilmis; reflection ile bulup gizle.
            var t = zp.GetType();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            foreach (var fieldName in new[] { "zoomInButton", "zoomOutButton" })
            {
                var f = t.GetField(fieldName, flags);
                var b = f != null ? f.GetValue(zp) as Button : null;
                if (b != null) b.gameObject.SetActive(false);
            }
            _zoomDone = true;
        }

        private void TryHideAltitudePanel()
        {
            var appearance = FindObjectOfType<AltitudePanelAppearance>();
            if (appearance == null) return;
            appearance.gameObject.SetActive(false);
            _altDone = true;
        }

        private void TryTakeOverStrip()
        {
            var strip = GameObject.Find("WaypointMapEditStrip");
            if (strip == null) return;

            _wpToggle = strip.GetComponentInChildren<Toggle>(true);
            if (_wpToggle == null) return;

            // Seridi DEAKTIVE ETME (uzerindeki panel scripti calismali) — ekran disina tasi.
            var rt = strip.GetComponent<RectTransform>();
            if (rt != null) rt.anchoredPosition = new Vector2(-6000f, -6000f);

            // Eski ipucu metnini de ekran disina al (IMGUI ipucumuz onun yerine gecer).
            var hint = GameObject.Find("MapPlacementHint");
            if (hint == null)
            {
                var t = strip.transform.parent != null ? strip.transform.parent.Find("MapPlacementHint") : null;
                if (t != null) hint = t.gameObject;
            }
            if (hint != null)
            {
                var hrt = hint.GetComponent<RectTransform>();
                if (hrt != null) hrt.anchoredPosition = new Vector2(-6000f, -6000f);
            }

            _stripDone = true;
        }

        private void OnGUI()
        {
            if (_wpToggle == null) return;
            TwinHudTheme.BeginScaledHud();

            bool on = _wpToggle.isOn;
            var r = new Rect(buttonX, TwinHudTheme.ScreenH - buttonSize.y - buttonBottomMargin,
                             buttonSize.x, buttonSize.y);

            // Acikken buton altinda accent serit — durum bir bakista belli olur.
            if (on)
                TwinHudTheme.Fill(new Rect(r.x, r.yMax + 2f, r.width, 3f), TwinHudTheme.Accent, 1.5f);

            if (GUI.Button(r, on ? "WP Ekleme: AÇIK" : "WP Ekle", TwinHudTheme.Button))
                _wpToggle.isOn = !on;

            if (on)
            {
                if (_hintStyle == null)
                    _hintStyle = new GUIStyle(TwinHudTheme.Small) { alignment = TextAnchor.MiddleLeft };
                GUI.Label(new Rect(r.xMax + 10f, r.y, 260f, r.height), "haritaya tıklayarak waypoint ekle", _hintStyle);
            }

            TwinHudTheme.EndScaledHud();
        }
    }
}
