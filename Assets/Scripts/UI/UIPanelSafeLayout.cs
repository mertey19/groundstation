using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.UI
{
    /// <summary>
    /// Canvas ve panellerin ekranda sabit kalmasini saglar.
    /// - Canvas Screen Space - Overlay ve Scaler ayarlarini kontrol eder.
    /// - Panellerin anchor/pivot/position degerlerini ekran sinirlari icinde tutar.
    /// Hierarchy: Canvas MAP'in child'i OLMAMALI; Scene root'ta olmali.
    /// </summary>
    public class UIPanelSafeLayout : MonoBehaviour
    {
        [Header("Canvas Setup (ilk calistirmada uygulanir)")]
        [SerializeField] private bool setCanvasOverlay = true;
        [SerializeField] private bool setCanvasScaler = true;
        [SerializeField] private int referenceWidth = 1920;
        [SerializeField] private int referenceHeight = 1080;
        [SerializeField] private float matchWidthOrHeight = 0.5f;

        [Header("Adaptive Scaling (kucuk ekranlar)")]
        [SerializeField] private bool autoScaleForSmallScreens = true;
        [SerializeField] private float minSmallScreenScale = 0.78f;
        [SerializeField] private float maxSmallScreenScale = 1.0f;

        [Header("Panel Inset (ekrandan minimum bosluk)")]
        [SerializeField] private float screenInset = 20f;

        private Canvas _canvas;
        private CanvasScaler _scaler;
        private float _resolvedScreenScale = 1f;

        private void Awake()
        {
            NormalizeReadableDefaults();

            _canvas = GetComponent<Canvas>();
            _scaler = GetComponent<CanvasScaler>();

            if (setCanvasOverlay && _canvas != null)
            {
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 0;
            }

            if (setCanvasScaler && _scaler != null)
            {
                _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                _scaler.referenceResolution = new Vector2(referenceWidth, referenceHeight);
                _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                _scaler.matchWidthOrHeight = matchWidthOrHeight;
            }

            _resolvedScreenScale = ResolveSmallScreenScale();

            ApplySafeAnchorsToChildren();
            EnsurePanelAppearanceScripts();
            ApplySmallScreenTypographyScale();
        }

        private void NormalizeReadableDefaults()
        {
            telemetryWidth = Mathf.Max(300f, telemetryWidth);
            telemetryHeight = Mathf.Max(220f, telemetryHeight);
            mapStylePanelWidth = Mathf.Max(620f, mapStylePanelWidth);
            mapStylePanelHeight = Mathf.Max(72f, mapStylePanelHeight);
            mapStyleButtonWidth = Mathf.Max(180f, mapStyleButtonWidth);
            mapStyleButtonHeight = Mathf.Max(52f, mapStyleButtonHeight);
            mapStyleButtonFontSize = Mathf.Max(24, mapStyleButtonFontSize);

            controlPanelButtonWidth = Mathf.Max(138f, controlPanelButtonWidth);
            controlPanelButtonHeight = Mathf.Max(52f, controlPanelButtonHeight);
            controlPanelButtonFontSize = Mathf.Max(24, controlPanelButtonFontSize);

            altitudePanelWidth = Mathf.Max(280f, altitudePanelWidth);
            altitudePanelHeight = Mathf.Max(64f, altitudePanelHeight);
            speedAltitudePanelWidth = Mathf.Max(320f, speedAltitudePanelWidth);
            speedAltitudePanelHeight = Mathf.Max(140f, speedAltitudePanelHeight);
            surveyPanelWidth = Mathf.Max(320f, surveyPanelWidth);

            mapZoomPanelWidth = Mathf.Max(78f, mapZoomPanelWidth);
            mapZoomPanelHeight = Mathf.Max(124f, mapZoomPanelHeight);
            mapZoomButtonHeight = Mathf.Max(52f, mapZoomButtonHeight);
            mapZoomButtonFontSize = Mathf.Max(30, mapZoomButtonFontSize);
        }

        [Header("Ust paneller (boyut)")]
        [SerializeField] private float telemetryWidth = 320f;
        [SerializeField] private float telemetryHeight = 230f;
        [SerializeField] private float mapStylePanelWidth = 700f;
        [SerializeField] private float mapStylePanelHeight = 80f;
        [SerializeField] private float mapStyleButtonSpacing = 14f;
        [SerializeField] private float mapStyleButtonWidth = 205f;
        [SerializeField] private float mapStyleButtonHeight = 58f;
        [Tooltip("Uydu/Sokak/Dijital ikiz butonlarindaki yazi boyutu (0 = degistirme)")]
        [SerializeField] private int mapStyleButtonFontSize = 28;

        [Header("Alt kontrol paneli (Map Style ile ayni boyut, 4 buton okunakli)")]
        [Tooltip("Map Style ile ayni genislik (0 = mapStylePanelWidth kullan)")]
        [SerializeField] private float controlPanelWidth = 0f;
        [Tooltip("Map Style ile ayni yukseklik (0 = mapStylePanelHeight kullan)")]
        [SerializeField] private float controlPanelHeight = 0f;
        [SerializeField] private float controlPanelSpacing = 14f;
        [SerializeField] private float controlPanelPadding = 16f;
        [Tooltip("Her butonun sabit genisligi (4 buton panele sigsin)")]
        [SerializeField] private float controlPanelButtonWidth = 156f;
        [Tooltip("Buton yuksekligi (0 = panel yuksekligine gore)")]
        [SerializeField] private float controlPanelButtonHeight = 56f;
        [SerializeField] private int controlPanelButtonFontSize = 27;

        [Header("Waypoint Altitude paneli (ayri panel, alt sol)")]
        [SerializeField] private float altitudePanelWidth = 300f;
        [SerializeField] private float altitudePanelHeight = 70f;

        [Header("Hiz / Yukseklik paneli (1920x1080 sag alt)")]
        [SerializeField] private float speedAltitudePanelWidth = 340f;
        [SerializeField] private float speedAltitudePanelHeight = 168f;
        [Header("Survey Mapping paneli (sag orta)")]
        [SerializeField] private float surveyPanelWidth = 340f;
        [SerializeField] private float surveyPanelOffsetY = -40f;
        [Header("Harita Zoom paneli (+ / -) sag ust")]
        [SerializeField] private float mapZoomPanelWidth = 84f;
        [SerializeField] private float mapZoomPanelHeight = 134f;
        [SerializeField] private float mapZoomButtonHeight = 58f;
        [SerializeField] private int mapZoomButtonFontSize = 34;

        /// <summary>
        /// Canvas altindaki tum panelleri (RectTransform) ekran icinde tutacak sekilde ayarlar.
        /// Panel isimleri TelemetryPanel -> sol ust, ControlPanel -> alt orta, MapStylePanel -> ust orta.
        /// MapStylePanel icindeki butonlara HorizontalLayoutGroup uygulanir.
        /// </summary>
        private void ApplySafeAnchorsToChildren()
        {
            foreach (Transform child in transform)
            {
                var rt = child.GetComponent<RectTransform>();
                if (rt == null) continue;

                string name = child.name.ToLowerInvariant();

                // TelemetryPanel: sol ust, duzgun boyut
                if (name.Contains("telemetry"))
                {
                    rt.anchorMin = new Vector2(0f, 1f);
                    rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot = new Vector2(0f, 1f);
                    rt.anchoredPosition = new Vector2(screenInset, -screenInset);
                    rt.sizeDelta = new Vector2(telemetryWidth, telemetryHeight);
                    foreach (var txt in child.GetComponentsInChildren<Text>(true))
                    {
                        txt.fontStyle = FontStyle.Bold;
                        if (txt.fontSize < 22) txt.fontSize = 22;
                    }
                    continue;
                }

                // Survey Mapping paneli: sag orta-sol (hiz/yukseklik panelinin solunda)
                if (name.Contains("surveymapping") || name.Contains("surveypanel") || name.Contains("mappingpanel"))
                {
                    rt.anchorMin = new Vector2(1f, 1f);
                    rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(1f, 1f);
                    float belowZoomY = -(screenInset + mapZoomPanelHeight + 12f);
                    float surveyX = -(screenInset + speedAltitudePanelWidth + 16f);
                    rt.anchoredPosition = new Vector2(surveyX, belowZoomY + surveyPanelOffsetY);
                    rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, surveyPanelWidth);
                    continue;
                }

                // Map Zoom paneli: sag ust (+ / -)
                if (name.Contains("mapzoom") || name.Contains("zoompanel") || name == "zoom"
                    || ((name.Contains("right") || name.Contains("sag")) && child.GetComponentsInChildren<Button>(true).Length <= 2))
                {
                    rt.anchorMin = new Vector2(1f, 1f);
                    rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(1f, 1f);
                    rt.anchoredPosition = new Vector2(-screenInset, -screenInset);
                    rt.sizeDelta = new Vector2(mapZoomPanelWidth, mapZoomPanelHeight);

                    var vlg = child.GetComponent<VerticalLayoutGroup>();
                    if (vlg == null) vlg = child.gameObject.AddComponent<VerticalLayoutGroup>();
                    vlg.spacing = 10f;
                    vlg.childAlignment = TextAnchor.MiddleCenter;
                    vlg.childControlWidth = true;
                    vlg.childControlHeight = true;
                    vlg.childForceExpandWidth = true;
                    vlg.childForceExpandHeight = false;
                    vlg.padding = new RectOffset(8, 8, 8, 8);

                    int zoomIdx = 0;
                    foreach (Transform btn in child)
                    {
                        var le = btn.GetComponent<LayoutElement>();
                        if (le == null) le = btn.gameObject.AddComponent<LayoutElement>();
                        le.preferredHeight = mapZoomButtonHeight;
                        le.flexibleHeight = 0;

                        var txt = btn.GetComponentInChildren<Text>(true);
                        if (txt != null)
                        {
                            txt.fontSize = mapZoomButtonFontSize;
                            txt.fontStyle = FontStyle.Bold;
                            txt.alignment = TextAnchor.MiddleCenter;
                            txt.text = zoomIdx == 0 ? "+" : "-";
                            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                            txt.verticalOverflow = VerticalWrapMode.Overflow;
                        }
                        zoomIdx++;
                    }
                    continue;
                }

                // Hiz / Yukseklik paneli (yuksekligi artir/azalt, hizi artir/azalt) -> SAG ALT KOSE
                if (name.Contains("speedaltitude") || name.Contains("hizyukseklik") || name.Contains("dronespeed"))
                {
                    rt.anchorMin = new Vector2(1f, 0f);
                    rt.anchorMax = new Vector2(1f, 0f);
                    rt.pivot = new Vector2(1f, 0f);
                    rt.anchoredPosition = new Vector2(-screenInset, screenInset);
                    rt.sizeDelta = new Vector2(speedAltitudePanelWidth, speedAltitudePanelHeight);
                    ApplySpeedAltitudeLayout(child);
                    continue;
                }

                // Waypoint Altitude paneli (alt sol, Y ekseninde Control Panel ile hizalı)
                if (IsWaypointAltitudePanel(child, name))
                {
                    float ch = controlPanelHeight > 0 ? controlPanelHeight : mapStylePanelHeight;
                    float alignY = screenInset + (ch - altitudePanelHeight) * 0.5f;

                    rt.anchorMin = new Vector2(0f, 0f);
                    rt.anchorMax = new Vector2(0f, 0f);
                    rt.pivot = new Vector2(0f, 0f);
                    // Pivot zaten (0,0); ekstra yarim genislik eklemek paneli gereksiz saga iter.
                    rt.anchoredPosition = new Vector2(screenInset, alignY);
                    rt.sizeDelta = new Vector2(altitudePanelWidth, altitudePanelHeight);
                    continue;
                }

                // ControlPanel: alt orta, Map Style ile ayni boyut, 4 buton (Baslat, Durdur, Temizle, JSON Export)
                if (name.Contains("control") && !name.Contains("style") && !name.Contains("speed"))
                {
                    float cw = controlPanelWidth > 0 ? controlPanelWidth : mapStylePanelWidth;
                    float ch = controlPanelHeight > 0 ? controlPanelHeight : mapStylePanelHeight;

                    rt.anchorMin = new Vector2(0.5f, 0f);
                    rt.anchorMax = new Vector2(0.5f, 0f);
                    rt.pivot = new Vector2(0.5f, 0f);
                    rt.anchoredPosition = new Vector2(0f, screenInset);
                    rt.sizeDelta = new Vector2(cw, ch);

                    var hlg = child.GetComponent<HorizontalLayoutGroup>();
                    if (hlg == null) hlg = child.gameObject.AddComponent<HorizontalLayoutGroup>();
                    hlg.spacing = controlPanelSpacing;
                    hlg.childAlignment = TextAnchor.MiddleCenter;
                    hlg.childControlWidth = true;
                    hlg.childControlHeight = true;
                    hlg.childForceExpandWidth = false;
                    hlg.childForceExpandHeight = false;
                    int pad = (int)controlPanelPadding;
                    hlg.padding = new RectOffset(pad, pad, pad, pad);

                    float btnH = controlPanelButtonHeight > 0 ? controlPanelButtonHeight : (ch - pad * 2);
                    foreach (Transform c in child)
                    {
                        var le = c.GetComponent<LayoutElement>();
                        if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
                        le.preferredWidth = controlPanelButtonWidth;
                        le.preferredHeight = btnH;
                        le.flexibleWidth = 0;
                        if (controlPanelButtonFontSize > 0)
                        {
                            var text = c.GetComponentInChildren<Text>(true);
                            if (text != null)
                            {
                                text.fontSize = controlPanelButtonFontSize;
                                text.fontStyle = FontStyle.Bold;
                                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                                text.resizeTextForBestFit = false;
                            }
                        }
                    }
                    continue;
                }

                // MapStylePanel: ust orta, butonlar yatay dizilsin
                if (name.Contains("mapstyle") || name.Contains("styl"))
                {
                    rt.anchorMin = new Vector2(0.5f, 1f);
                    rt.anchorMax = new Vector2(0.5f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.anchoredPosition = new Vector2(0f, -screenInset);
                    rt.sizeDelta = new Vector2(mapStylePanelWidth, mapStylePanelHeight);

                    var hlg = child.GetComponent<HorizontalLayoutGroup>();
                    if (hlg == null) hlg = child.gameObject.AddComponent<HorizontalLayoutGroup>();
                    hlg.spacing = mapStyleButtonSpacing;
                    hlg.childAlignment = TextAnchor.MiddleCenter;
                    hlg.childControlWidth = true;
                    hlg.childControlHeight = true;
                    hlg.childForceExpandWidth = false;
                    hlg.childForceExpandHeight = true;
                    hlg.padding = new RectOffset((int)screenInset, (int)(screenInset * 0.5f), (int)screenInset, (int)(screenInset * 0.5f));

                    foreach (Transform btn in child)
                    {
                        var le = btn.GetComponent<LayoutElement>();
                        if (le == null) le = btn.gameObject.AddComponent<LayoutElement>();
                        le.preferredWidth = mapStyleButtonWidth;
                        if (mapStyleButtonHeight > 0) le.preferredHeight = mapStyleButtonHeight;
                        le.flexibleWidth = 0;
                        if (mapStyleButtonFontSize > 0)
                        {
                            var button = btn.GetComponent<Button>();
                            if (button != null)
                            {
                                var text = btn.GetComponentInChildren<Text>(true);
                                if (text != null)
                                {
                                    text.fontSize = mapStyleButtonFontSize;
                                    text.fontStyle = FontStyle.Bold;
                                }
                            }
                        }
                    }
                    continue;
                }

                // Sag buton grubu: ekran sag kenari
                if (name.Contains("right") || name.Contains("sag"))
                {
                    rt.anchorMin = new Vector2(1f, 0.5f);
                    rt.anchorMax = new Vector2(1f, 0.5f);
                    rt.pivot = new Vector2(1f, 0.5f);
                    rt.anchoredPosition = new Vector2(-screenInset, 0f);
                    rt.sizeDelta = new Vector2(120f, 220f);
                    continue;
                }

                // DroneControlPanel (ayri bir panel ise): alt sol
                if (name.Contains("dronecontrol") && !name.Contains("speed"))
                {
                    rt.anchorMin = new Vector2(0f, 0f);
                    rt.anchorMax = new Vector2(0f, 0f);
                    rt.pivot = new Vector2(0f, 0f);
                    rt.anchoredPosition = new Vector2(screenInset, screenInset);
                    rt.sizeDelta = new Vector2(200f, 60f);
                    continue;
                }

            }
        }

        private void EnsurePanelAppearanceScripts()
        {
            foreach (Transform child in transform)
            {
                string name = child.name.ToLowerInvariant();

                if (name.Contains("mapstyle") || name.Contains("styl"))
                {
                    if (child.GetComponent<MapStylePanelAppearance>() == null)
                    {
                        var app = child.gameObject.AddComponent<MapStylePanelAppearance>();
                        app.ApplyAppearance();
                    }
                    continue;
                }

                if (name.Contains("control") && !name.Contains("style") && !name.Contains("speed"))
                {
                    if (child.GetComponent<ControlPanelAppearance>() == null)
                    {
                        var app = child.gameObject.AddComponent<ControlPanelAppearance>();
                        app.ApplyAppearance();
                    }
                    continue;
                }

                if (IsWaypointAltitudePanel(child, name))
                {
                    if (child.GetComponent<AltitudePanelAppearance>() == null)
                    {
                        var app = child.gameObject.AddComponent<AltitudePanelAppearance>();
                        app.ApplyAppearance();
                    }
                    continue;
                }
            }
        }

        /// <summary>Panel adi "left" veya icinde AltitudeInput gecen de altitude paneli sayilir.</summary>
        private static bool IsWaypointAltitudePanel(Transform panel, string nameLower)
        {
            if (nameLower.Contains("speed") || nameLower.Contains("hiz") || nameLower.Contains("dronespeed")) return false;
            if (nameLower == "left") return true;
            if ((nameLower.Contains("waypoint") || nameLower.Contains("altitude")) && (nameLower.Contains("panel") || nameLower.Contains("input"))) return true;
            foreach (Transform c in panel.GetComponentsInChildren<Transform>(true))
                if (c != panel && c.name.ToLowerInvariant().Contains("altitudeinput")) return true;
            return false;
        }

        private void ApplySpeedAltitudeLayout(Transform panel)
        {
            var vlg = panel.GetComponent<VerticalLayoutGroup>();
            if (vlg == null) vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(12, 12, 12, 12);

            var buttons = panel.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                var b = buttons[i];
                var le = b.GetComponent<LayoutElement>();
                if (le == null) le = b.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 40f;
                le.flexibleHeight = 0f;

                var txt = EnsureButtonText(b);
                if (txt != null)
                {
                    txt.fontSize = 24;
                    txt.fontStyle = FontStyle.Bold;
                    txt.alignment = TextAnchor.MiddleCenter;
                    txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                    txt.verticalOverflow = VerticalWrapMode.Overflow;
                    txt.color = new Color(0.12f, 0.12f, 0.12f, 1f);
                    txt.text = GetSpeedAltitudeLabelByIndex(i);
                }
            }

            foreach (Transform child in panel)
            {
                var le = child.GetComponent<LayoutElement>();
                if (le == null) le = child.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 40f;
                var txt = child.GetComponentInChildren<Text>(true);
                if (txt != null)
                {
                    txt.fontSize = 24;
                    txt.fontStyle = FontStyle.Bold;
                    txt.alignment = TextAnchor.MiddleCenter;
                }
            }
        }

        private static string GetSpeedAltitudeLabelByIndex(int index)
        {
            if (index == 0) return "Hız Arttır";
            if (index == 1) return "Hız Azalt";
            if (index == 2) return "Yükseklik Arttır";
            if (index == 3) return "Yükseklik Azalt";
            return "Kontrol";
        }

        private static Text EnsureButtonText(Button button)
        {
            if (button == null) return null;
            var txt = button.GetComponentInChildren<Text>(true);
            if (txt != null) return txt;

            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(button.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            txt = go.GetComponent<Text>();

            Font fallback = null;
            try { fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (fallback == null)
            {
                try { fallback = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            }
            if (fallback != null) txt.font = fallback;
            return txt;
        }

        private float ResolveSmallScreenScale()
        {
            if (!autoScaleForSmallScreens) return 1f;
            if (referenceWidth <= 0 || referenceHeight <= 0) return 1f;

            float sx = (float)Screen.width / referenceWidth;
            float sy = (float)Screen.height / referenceHeight;
            float s = Mathf.Min(sx, sy);
            return Mathf.Clamp(s, minSmallScreenScale, maxSmallScreenScale);
        }

        private void ApplySmallScreenTypographyScale()
        {
            if (!autoScaleForSmallScreens) return;
            if (_resolvedScreenScale >= 0.99f) return;

            foreach (var txt in GetComponentsInChildren<Text>(true))
            {
                if (txt == null || txt.fontSize <= 0) continue;
                int scaled = Mathf.Max(12, Mathf.RoundToInt(txt.fontSize * _resolvedScreenScale));
                if (scaled < txt.fontSize)
                    txt.fontSize = scaled;
            }

            foreach (var input in GetComponentsInChildren<InputField>(true))
            {
                if (input == null) continue;
                if (input.textComponent != null && input.textComponent.fontSize > 0)
                {
                    int scaled = Mathf.Max(12, Mathf.RoundToInt(input.textComponent.fontSize * _resolvedScreenScale));
                    if (scaled < input.textComponent.fontSize)
                        input.textComponent.fontSize = scaled;
                }

                if (input.placeholder is Text ph && ph.fontSize > 0)
                {
                    int scaled = Mathf.Max(11, Mathf.RoundToInt(ph.fontSize * _resolvedScreenScale));
                    if (scaled < ph.fontSize)
                        ph.fontSize = scaled;
                }
            }
        }
    }
}
