using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.UI
{
    /// <summary>
    /// Sol alt kose duzenleyici:
    ///   1) "Yukseklik (m)" giris panelini gizler (yerini karekod foto galerisi alir;
    ///      survey yuksekligi Inspector'daki varsayilan degerden okunmaya devam eder),
    ///   2) Buyuk "Haritadan | KAPALI" seridini KUCUK bir "WP ekle" dugmesine donusturur
    ///      (islev ayni: acikken haritaya tiklayarak waypoint eklenir).
    /// Runtime'da calisir; sahne dosyasina dokunmaz. AutoBootstrap ekler.
    /// </summary>
    public class BottomLeftUiCompactor : MonoBehaviour
    {
        [SerializeField] private bool hideAltitudePanel = true;
        [SerializeField] private bool compactWaypointStrip = true;
        [Tooltip("Kucuk WP seridinin sol-alt kose konumu (canvas birimi).")]
        [SerializeField] private Vector2 stripPosition = new Vector2(250f, 10f);
        [SerializeField] private Vector2 stripSize = new Vector2(148f, 32f);

        private bool _altDone, _stripDone;
        private float _nextTryAt;

        private void Update()
        {
            if ((_altDone || !hideAltitudePanel) && (_stripDone || !compactWaypointStrip))
            {
                enabled = false;   // is bitti, maliyet birakma
                return;
            }
            if (Time.unscaledTime < _nextTryAt) return;
            _nextTryAt = Time.unscaledTime + 0.5f;   // strip runtime'da sonradan kurulur; tekrar dene

            if (hideAltitudePanel && !_altDone)
                TryHideAltitudePanel();
            if (compactWaypointStrip && !_stripDone)
                TryCompactStrip();
        }

        private void TryHideAltitudePanel()
        {
            var appearance = FindObjectOfType<AltitudePanelAppearance>();
            if (appearance == null) return;
            appearance.gameObject.SetActive(false);
            _altDone = true;
        }

        private void TryCompactStrip()
        {
            var strip = GameObject.Find("WaypointMapEditStrip");
            if (strip == null) return;
            var rt = strip.GetComponent<RectTransform>();
            if (rt == null) return;

            // Sol-alt koseye, galerinin yanina kucuk serit.
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = stripSize;
            rt.anchoredPosition = stripPosition;

            var hlg = strip.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.padding = new RectOffset(8, 6, 3, 3);
                hlg.spacing = 6;
            }

            var label = strip.transform.Find("Label");
            if (label != null)
            {
                var txt = label.GetComponent<Text>();
                if (txt != null)
                {
                    txt.text = "WP ekle";
                    txt.fontSize = 13;
                }
                var le = label.GetComponent<LayoutElement>();
                if (le != null) { le.minWidth = 52f; le.flexibleWidth = 0f; }
            }

            var toggleTr = strip.transform.Find("Toggle");
            if (toggleTr != null)
            {
                var tle = toggleTr.GetComponent<LayoutElement>();
                if (tle != null) { tle.preferredWidth = 62f; tle.preferredHeight = 24f; }
                var stateText = toggleTr.GetComponentInChildren<Text>(true);
                if (stateText != null) stateText.fontSize = 11;
            }

            var rowLe = strip.GetComponent<LayoutElement>();
            if (rowLe != null) { rowLe.minHeight = stripSize.y; rowLe.preferredHeight = stripSize.y; }

            _stripDone = true;
        }
    }
}
