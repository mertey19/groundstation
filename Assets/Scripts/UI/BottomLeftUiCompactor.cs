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
        [Tooltip("WP dugmesinin sol kenardan uzakligi (sanal 1080p birimi).")]
        [SerializeField] private float buttonX = 340f;
        [SerializeField] private float buttonBottomMargin = 14f;
        [SerializeField] private Vector2 buttonSize = new Vector2(118f, 28f);

        private bool _altDone, _stripDone;
        private float _nextTryAt;
        private Toggle _wpToggle;
        private GUIStyle _hintStyle;

        private void Update()
        {
            if ((_altDone || !hideAltitudePanel) && (_stripDone || !compactWaypointStrip))
                return;   // arama bitti; OnGUI dugmesi icin bilesen acik kalir
            if (Time.unscaledTime < _nextTryAt) return;
            _nextTryAt = Time.unscaledTime + 0.5f;   // strip runtime'da sonradan kurulur; tekrar dene

            if (hideAltitudePanel && !_altDone)
                TryHideAltitudePanel();
            if (compactWaypointStrip && !_stripDone)
                TryTakeOverStrip();
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
