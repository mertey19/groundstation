using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.UI
{
    [RequireComponent(typeof(RectTransform))]
    public class ControlPanelAppearance : MonoBehaviour
    {
        [Header("Text")]
        [SerializeField] private int fontSize = 28;
        [SerializeField] private string waypointAltitudeLabel = "Waypoint Altitude";
        [SerializeField] private bool fixWaypointLabelText = true;

        [Header("Panel Style")]
        [SerializeField] private Color panelColor = new Color(0.08f, 0.09f, 0.11f, 0.72f);
        [SerializeField] private bool addPanelOutline = true;
        [SerializeField] private Color panelOutlineColor = new Color(1f, 1f, 1f, 0.10f);

        [Header("Button Style")]
        [SerializeField] private Color buttonColor = new Color(0.97f, 0.97f, 0.98f, 0.98f);
        [SerializeField] private Color buttonHighlightColor = new Color(1f, 1f, 1f, 1f);
        [SerializeField] private Color buttonPressedColor = new Color(0.88f, 0.90f, 0.94f, 1f);
        [SerializeField] private Color buttonDisabledColor = new Color(0.7f, 0.7f, 0.7f, 0.65f);
        [SerializeField] private Color buttonTextColor = new Color(0.10f, 0.11f, 0.13f, 1f);
        [SerializeField] private bool buttonTextBold = true;
        [SerializeField] private bool addTextShadow = false;
        [SerializeField] private string[] buttonLabels = new[] { "Başlat", "Dur", "Temizle", "Json Export" };

        private void Awake()
        {
            ApplyAppearance();
        }

        private void OnEnable()
        {
            ApplyAppearance();
        }

        public void ApplyAppearance()
        {
            var panelImage = GetComponent<Image>();
            if (panelImage == null) panelImage = gameObject.AddComponent<Image>();
            panelImage.color = panelColor;
            if (addPanelOutline)
            {
                var outline = GetComponent<Outline>();
                if (outline == null) outline = gameObject.AddComponent<Outline>();
                outline.effectColor = panelOutlineColor;
                outline.effectDistance = new Vector2(1f, -1f);
            }

            foreach (var text in GetComponentsInChildren<Text>(true))
            {
                if (fontSize > 0)
                {
                    text.fontSize = fontSize;
                    text.resizeTextForBestFit = false;
                    text.horizontalOverflow = HorizontalWrapMode.Overflow;
                    text.verticalOverflow = VerticalWrapMode.Overflow;
                }
                text.alignment = TextAnchor.MiddleCenter;

                if (fixWaypointLabelText && !string.IsNullOrEmpty(waypointAltitudeLabel))
                {
                    string t = text.text ?? "";
                    if (t.Contains("Waypoint") || t.Contains("Altit") || t.Contains("Yukseklik") || t.Contains("Altitude"))
                        text.text = waypointAltitudeLabel;
                }

                var btn = text.GetComponentInParent<Button>();
                if (btn != null && text.transform.IsChildOf(btn.transform))
                {
                    text.color = buttonTextColor;
                    if (buttonTextBold) text.fontStyle = FontStyle.Bold;
                    if (addTextShadow)
                    {
                        var shadow = text.GetComponent<Shadow>();
                        if (shadow == null) shadow = text.gameObject.AddComponent<Shadow>();
                        shadow.effectColor = new Color(0f, 0f, 0f, 0.20f);
                        shadow.effectDistance = new Vector2(0f, -1f);
                    }
                }
            }

            foreach (var btn in GetComponentsInChildren<Button>(true))
            {
                var img = btn.GetComponent<Image>();
                if (img != null && img.color.a > 0.01f)
                    img.color = buttonColor;
                var cb = btn.colors;
                cb.normalColor = buttonColor;
                cb.highlightedColor = buttonHighlightColor;
                cb.pressedColor = buttonPressedColor;
                cb.disabledColor = buttonDisabledColor;
                cb.colorMultiplier = 1f;
                cb.fadeDuration = 0.08f;
                btn.colors = cb;

                var txt = EnsureButtonText(btn);
                if (txt != null)
                {
                    txt.fontSize = fontSize;
                    txt.alignment = TextAnchor.MiddleCenter;
                    txt.color = buttonTextColor;
                    txt.fontStyle = buttonTextBold ? FontStyle.Bold : FontStyle.Normal;
                }
            }

            ApplyDefaultButtonLabels();

            foreach (var input in GetComponentsInChildren<InputField>(true))
            {
                if (fontSize > 0 && input.textComponent != null)
                    input.textComponent.fontSize = fontSize;
                if (fontSize > 0 && input.placeholder != null && input.placeholder is Text ph)
                    ph.fontSize = fontSize;
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Apply Appearance Now")]
        private void EditorApply()
        {
            ApplyAppearance();
        }
#endif

        private void ApplyDefaultButtonLabels()
        {
            var buttons = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length && i < buttonLabels.Length; i++)
            {
                var txt = buttons[i].GetComponentInChildren<Text>(true);
                if (txt == null) continue;
                txt.text = buttonLabels[i];
            }
        }

        private static Text EnsureButtonText(Button button)
        {
            if (button == null) return null;
            var txt = button.GetComponentInChildren<Text>(true);
            if (txt != null) return txt;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(button.transform, false);
            var rt = textGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            txt = textGo.GetComponent<Text>();

            try { txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            return txt;
        }
    }
}
