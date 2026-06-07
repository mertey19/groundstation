using GroundStation.Drone;
using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.UI
{
    [System.Serializable]
    public class WaypointMapEditUiSettings
    {
        [Header("Texts")]
        public string stripLabel = "Haritadan";
        public string hintText = "Haritaya tiklayarak waypoint ekleyebilirsiniz.";

        [Header("Sizes")]
        public float stripHeight = 44f;
        public float stripWidth = 260f;
        public float spacingBelowControlPanel = 8f;
        public float gapFromAltitudePanel = 12f;
        public float fallbackBottomInset = 20f;
        public float hintHeight = 30f;
        public float hintSpacing = 6f;
        public int stripLabelFontSize = 18;
        public int hintFontSize = 17;
        public float toggleWidth = 96f;
        public float toggleHeight = 30f;
        public float toggleCheckSize = 18f;

        [Header("Colors")]
        public Color stripBackgroundColor = new Color(0.07f, 0.09f, 0.13f, 0.92f);
        public Color stripLabelColor = Color.white;
        public Color hintTextColor = new Color(1f, 1f, 1f, 0.9f);
        public Color toggleBackgroundColor = new Color(1f, 1f, 1f, 0.22f);
        public Color toggleCheckColor = new Color(0.25f, 0.72f, 1f, 1f);

        public WaypointMapEditUiSettings Clone()
        {
            return new WaypointMapEditUiSettings
            {
                stripLabel = stripLabel,
                hintText = hintText,
                stripHeight = stripHeight,
                stripWidth = stripWidth,
                spacingBelowControlPanel = spacingBelowControlPanel,
                gapFromAltitudePanel = gapFromAltitudePanel,
                fallbackBottomInset = fallbackBottomInset,
                hintHeight = hintHeight,
                hintSpacing = hintSpacing,
                stripLabelFontSize = stripLabelFontSize,
                hintFontSize = hintFontSize,
                toggleWidth = toggleWidth,
                toggleHeight = toggleHeight,
                toggleCheckSize = toggleCheckSize,
                stripBackgroundColor = stripBackgroundColor,
                stripLabelColor = stripLabelColor,
                hintTextColor = hintTextColor,
                toggleBackgroundColor = toggleBackgroundColor,
                toggleCheckColor = toggleCheckColor
            };
        }
    }

    /// <summary>
    /// Builds runtime map-click edit strip below DroneControlPanel.
    /// </summary>
    public static class WaypointMapEditUi
    {
        private static Sprite _whiteSprite;

        public static void EnsureBelowDroneControlPanel(DroneControlPanel controlPanel, WaypointMapEditUiSettings settings)
        {
            if (controlPanel == null) return;
            if (settings == null) settings = new WaypointMapEditUiSettings();

            var cpRt = controlPanel.GetComponent<RectTransform>();
            var canvas = controlPanel.GetComponentInParent<Canvas>();
            if (cpRt == null || canvas == null) return;

            // Prevent duplicates, but only for the runtime strip we own.
            if (canvas.transform.Find("WaypointMapEditStrip") != null)
                return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(cpRt);

            float stripH = Mathf.Max(28f, settings.stripHeight);

            var row = new GameObject("WaypointMapEditStrip", typeof(RectTransform));
            row.transform.SetParent(canvas.transform, false);
            var rowRt = row.GetComponent<RectTransform>();
            PlaceStripNearAltitudePanelOrFallback(cpRt, canvas, rowRt, stripH, settings);

            var bg = row.AddComponent<Image>();
            bg.color = settings.stripBackgroundColor;
            var bgOutline = row.AddComponent<Outline>();
            bgOutline.effectColor = new Color(1f, 1f, 1f, 0.14f);
            bgOutline.effectDistance = new Vector2(1f, -1f);

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(12, 10, 6, 6);
            hlg.spacing = 10;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            var rowLe = row.AddComponent<LayoutElement>();
            rowLe.minHeight = stripH;
            rowLe.preferredHeight = stripH;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(row.transform, false);
            var labelText = labelGo.AddComponent<Text>();
            labelText.text = settings.stripLabel;
            labelText.fontSize = Mathf.Max(10, settings.stripLabelFontSize);
            labelText.alignment = TextAnchor.MiddleLeft;
            labelText.color = settings.stripLabelColor;
            labelText.horizontalOverflow = HorizontalWrapMode.Wrap;
            if (font != null) labelText.font = font;
            var labelLe = labelGo.AddComponent<LayoutElement>();
            labelLe.flexibleWidth = 1f;
            labelLe.minWidth = 120f;

            Toggle toggle = BuildToggle(row.transform, settings);

            LayoutRebuilder.ForceRebuildLayoutImmediate(rowRt);

            var hintGo = new GameObject("MapPlacementHint", typeof(RectTransform));
            hintGo.transform.SetParent(canvas.transform, false);
            var hintRt = hintGo.GetComponent<RectTransform>();
            float hintH = Mathf.Max(20f, settings.hintHeight);
            float hintGap = Mathf.Max(0f, settings.hintSpacing);
            PlaceHintBelowStrip(canvas, rowRt, hintRt, hintH, hintGap, Mathf.Min(cpRt.rect.width, 720f));
            var hintText = hintGo.AddComponent<Text>();
            hintText.text = settings.hintText;
            hintText.fontSize = Mathf.Max(10, settings.hintFontSize);
            hintText.alignment = TextAnchor.MiddleCenter;
            hintText.color = settings.hintTextColor;
            if (font != null) hintText.font = font;
            hintGo.SetActive(false);

            var panel = row.AddComponent<WaypointMapEditPanel>();
            panel.Configure(toggle, hintGo);

            rowRt.SetAsLastSibling();
            hintRt.SetAsLastSibling();
        }

        /// <summary>
        /// Oncelik: AltitudePanel'in hemen sagina yerlestir.
        /// AltitudePanel bulunamazsa ControlPanel'in altina fallback uygula.
        /// </summary>
        private static void PlaceStripNearAltitudePanelOrFallback(
            RectTransform cpRt,
            Canvas canvas,
            RectTransform stripRt,
            float stripHeight,
            WaypointMapEditUiSettings settings)
        {
            var canvasRt = canvas.transform as RectTransform;
            Camera eventCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : (canvas.worldCamera != null ? canvas.worldCamera : Camera.main);

            stripRt.localScale = Vector3.one;
            stripRt.anchorMin = stripRt.anchorMax = new Vector2(0f, 0f);
            stripRt.pivot = new Vector2(0f, 0f);

            float targetWidth = Mathf.Max(180f, settings.stripWidth);
            stripRt.sizeDelta = new Vector2(targetWidth, stripHeight);

            var altitudeRt = FindAltitudePanelRect(canvas.transform);
            if (altitudeRt != null)
            {
                // Altitude panel tipik olarak alt-sol anchor ile yerlesiyor.
                // Bu durumda anchoredPosition tabanli hesap cok daha stabildir.
                if (Mathf.Abs(altitudeRt.anchorMin.x) < 0.01f && Mathf.Abs(altitudeRt.anchorMin.y) < 0.01f &&
                    Mathf.Abs(altitudeRt.anchorMax.x) < 0.01f && Mathf.Abs(altitudeRt.anchorMax.y) < 0.01f)
                {
                    float altX = altitudeRt.anchoredPosition.x + altitudeRt.rect.width + Mathf.Max(6f, settings.gapFromAltitudePanel);
                    float altY = altitudeRt.anchoredPosition.y;
                    stripRt.anchoredPosition = ClampBottomLeftPositionInsideCanvas(canvasRt, altX, altY, targetWidth, stripHeight);
                }
                else
                {
                    Vector3[] wcAlt = new Vector3[4];
                    altitudeRt.GetWorldCorners(wcAlt);
                    Vector3 rightBottomWorld = wcAlt[3];
                    Vector3 screenPoint = RectTransformUtility.WorldToScreenPoint(eventCam, rightBottomWorld);
                    Vector2 localCanvas;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screenPoint, eventCam, out localCanvas);
                    stripRt.anchoredPosition = ClampBottomLeftPositionInsideCanvas(
                        canvasRt,
                        localCanvas.x + Mathf.Max(6f, settings.gapFromAltitudePanel),
                        localCanvas.y,
                        targetWidth,
                        stripHeight);
                }
                return;
            }

            // Fallback: ControlPanel altina, alt kenara yakin.
            Vector3[] wcCp = new Vector3[4];
            cpRt.GetWorldCorners(wcCp);
            Vector3 bottomCenterWorld = (wcCp[0] + wcCp[3]) * 0.5f;
            Vector3 cpScreenPoint = RectTransformUtility.WorldToScreenPoint(eventCam, bottomCenterWorld);
            Vector2 cpLocalCanvas;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, cpScreenPoint, eventCam, out cpLocalCanvas);
            float x = cpLocalCanvas.x - targetWidth * 0.5f;
            float y = Mathf.Max(settings.fallbackBottomInset, cpLocalCanvas.y - stripHeight - settings.spacingBelowControlPanel);
            stripRt.anchoredPosition = ClampBottomLeftPositionInsideCanvas(canvasRt, x, y, targetWidth, stripHeight);
        }

        private static void PlaceHintBelowStrip(
            Canvas canvas,
            RectTransform stripRt,
            RectTransform hintRt,
            float hintHeight,
            float gapPixels,
            float maxWidth)
        {
            var canvasRt = canvas.transform as RectTransform;
            Camera eventCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : (canvas.worldCamera != null ? canvas.worldCamera : Camera.main);

            Vector3[] wc = new Vector3[4];
            stripRt.GetWorldCorners(wc);
            Vector3 bottomCenterWorld = (wc[0] + wc[3]) * 0.5f;

            Vector3 screenPoint = RectTransformUtility.WorldToScreenPoint(eventCam, bottomCenterWorld);
            Vector2 localCanvas;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screenPoint, eventCam, out localCanvas);

            hintRt.localScale = Vector3.one;
            hintRt.anchorMin = hintRt.anchorMax = new Vector2(0.5f, 0.5f);
            hintRt.pivot = new Vector2(0.5f, 1f);
            hintRt.sizeDelta = new Vector2(maxWidth, hintHeight);
            // Pivot top: strip alt kenarinin gap kadar altinda ipucunun ust kenari
            hintRt.anchoredPosition = localCanvas + new Vector2(0f, -gapPixels);
        }

        private static Toggle BuildToggle(Transform parent, WaypointMapEditUiSettings settings)
        {
            var toggleGo = new GameObject("Toggle", typeof(RectTransform));
            toggleGo.transform.SetParent(parent, false);
            var toggle = toggleGo.AddComponent<Toggle>();
            var tle = toggleGo.AddComponent<LayoutElement>();
            tle.preferredWidth = Mathf.Max(24f, settings.toggleWidth);
            tle.preferredHeight = Mathf.Max(20f, settings.toggleHeight);

            var bgGo = new GameObject("Background", typeof(RectTransform));
            bgGo.transform.SetParent(toggleGo.transform, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.sprite = WhiteSprite();
            bgImg.color = settings.toggleBackgroundColor;
            var bgOutline = bgGo.AddComponent<Outline>();
            bgOutline.effectColor = new Color(1f, 1f, 1f, 0.18f);
            bgOutline.effectDistance = new Vector2(1f, -1f);

            var ckGo = new GameObject("Checkmark", typeof(RectTransform));
            ckGo.transform.SetParent(bgGo.transform, false);
            var ckRt = ckGo.GetComponent<RectTransform>();
            ckRt.anchorMin = new Vector2(0f, 0f);
            ckRt.anchorMax = new Vector2(1f, 1f);
            float inset = Mathf.Max(2f, settings.toggleCheckSize * 0.15f);
            ckRt.offsetMin = new Vector2(inset, inset);
            ckRt.offsetMax = new Vector2(-inset, -inset);
            var ckImg = ckGo.AddComponent<Image>();
            ckImg.sprite = WhiteSprite();
            ckImg.color = settings.toggleCheckColor;

            var txtGo = new GameObject("StateText", typeof(RectTransform));
            txtGo.transform.SetParent(bgGo.transform, false);
            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;
            var txt = txtGo.AddComponent<Text>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) txt.font = font;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.fontStyle = FontStyle.Bold;
            txt.fontSize = 14;
            txt.color = new Color(0.1f, 0.1f, 0.1f, 1f);

            toggle.targetGraphic = bgImg;
            toggle.graphic = ckImg;
            toggle.isOn = false;
            ckImg.enabled = false;
            txt.text = "KAPALI";
            toggle.onValueChanged.AddListener(isOn =>
            {
                ckImg.enabled = isOn;
                txt.text = isOn ? "ACIK" : "KAPALI";
                bgImg.color = isOn ? new Color(0.84f, 0.93f, 1f, 0.95f) : settings.toggleBackgroundColor;
            });
            return toggle;
        }

        private static RectTransform FindAltitudePanelRect(Transform canvasRoot)
        {
            if (canvasRoot == null) return null;
            foreach (Transform child in canvasRoot)
            {
                string n = child.name.ToLowerInvariant();
                if (n.Contains("speed")) continue;
                if (n.Contains("left") || n.Contains("altitudepanel") || n.Contains("altitudeinput"))
                {
                    var rt = child as RectTransform;
                    if (rt != null) return rt;
                }
            }
            return null;
        }

        private static Vector2 ClampBottomLeftPositionInsideCanvas(
            RectTransform canvasRt,
            float x,
            float y,
            float width,
            float height)
        {
            if (canvasRt == null) return new Vector2(x, y);
            var r = canvasRt.rect;
            float minX = r.xMin + 4f;
            float minY = r.yMin + 4f;
            float maxX = r.xMax - width - 4f;
            float maxY = r.yMax - height - 4f;
            return new Vector2(
                Mathf.Clamp(x, minX, maxX),
                Mathf.Clamp(y, minY, maxY));
        }

        private static Sprite WhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            var tex = Texture2D.whiteTexture;
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            return _whiteSprite;
        }
    }
}
