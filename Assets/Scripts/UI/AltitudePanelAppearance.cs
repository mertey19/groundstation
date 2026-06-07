using UnityEngine;
using UnityEngine.UI;
using System.Globalization;

namespace GroundStation.UI
{
    /// <summary>
    /// Altitude paneli: placeholder "Yukseklik", sadece sayi giris, etiket "Yukseklik (waypoint yuksekligi)".
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class AltitudePanelAppearance : MonoBehaviour
    {
        [Header("Metin")]
        [Tooltip("Placeholder (input bosken gorunen)")]
        [SerializeField] private string placeholderText = "Yükseklik (m)";
        [Tooltip("Panel etiketi (varsa)")]
        [SerializeField] private string waypointLabel = "Yükseklik";

        [Header("Okunaklilik")]
        [SerializeField] private int fontSize = 24;
        [Tooltip("Metinler kalin (bold)")]
        [SerializeField] private bool textBold = true;
        [Tooltip("Input alani panel icinde kenarlardan bosluk")]
        [SerializeField] private float inputPadding = 8f;
        [SerializeField] private Color inputBgColor = new Color(0.95f, 0.95f, 0.95f, 1f);
        [SerializeField] private Color inputTextColor = new Color(0.12f, 0.12f, 0.12f, 1f);
        [SerializeField] private Color placeholderColor = new Color(0.45f, 0.45f, 0.45f, 1f);

        private InputField _inputField;
        private string _lastValidText = "";

        private void Awake()
        {
            ApplyAppearance();
        }

        private void Start()
        {
            ApplyAppearance();
        }

        public void ApplyAppearance()
        {
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
                if (textBold) text.fontStyle = FontStyle.Bold;
                string t = text.text ?? "";
                if (!string.IsNullOrEmpty(waypointLabel))
                {
                    if (t.Contains("Waypoint") || t.Contains("Altitude") || t.Contains("Yükseklik") || t.Contains("Altit"))
                        text.text = waypointLabel;
                }
            }

            var input = GetComponentInChildren<InputField>(true);
            if (input != null)
            {
                _inputField = input;
                if (fontSize > 0 && input.textComponent != null)
                {
                    input.textComponent.fontSize = fontSize;
                    input.textComponent.color = inputTextColor;
                    input.textComponent.alignment = TextAnchor.MiddleCenter;
                    if (textBold) input.textComponent.fontStyle = FontStyle.Bold;
                }
                if (input.placeholder != null)
                {
                    if (input.placeholder is Text ph)
                    {
                        ph.text = placeholderText;
                        if (fontSize > 0) ph.fontSize = fontSize;
                        ph.color = placeholderColor;
                        ph.alignment = TextAnchor.MiddleCenter;
                    }
                }
                var inputImage = input.GetComponent<Image>();
                if (inputImage != null) inputImage.color = inputBgColor;
                input.contentType = InputField.ContentType.DecimalNumber;
                input.keyboardType = TouchScreenKeyboardType.DecimalPad;
                _lastValidText = SanitizeNumeric(input.text);
                input.text = _lastValidText;
                input.onValueChanged.RemoveAllListeners();
                input.onValueChanged.AddListener(OnInputValueChanged);
                FitInputToPanel(input);
            }
        }

        private void FitInputToPanel(InputField input)
        {
            var inputRt = input.GetComponent<RectTransform>();
            if (inputRt == null) return;
            var panelRt = transform as RectTransform;
            if (panelRt == null) return;
            inputRt.anchorMin = Vector2.zero;
            inputRt.anchorMax = Vector2.one;
            inputRt.offsetMin = new Vector2(inputPadding, inputPadding);
            inputRt.offsetMax = new Vector2(-inputPadding, -inputPadding);
        }

        private void OnInputValueChanged(string value)
        {
            if (_inputField == null) return;
            string sanitized = SanitizeNumeric(value);
            if (sanitized != value)
            {
                _inputField.onValueChanged.RemoveListener(OnInputValueChanged);
                _inputField.text = sanitized;
                _inputField.onValueChanged.AddListener(OnInputValueChanged);
            }
            if (IsValidNumber(sanitized))
                _lastValidText = sanitized;
        }

        private static string SanitizeNumeric(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            bool hasDot = false;
            var sb = new System.Text.StringBuilder();
            foreach (char c in s)
            {
                if (char.IsDigit(c))
                    sb.Append(c);
                else if ((c == '.' || c == ',') && !hasDot)
                {
                    sb.Append('.');
                    hasDot = true;
                }
            }
            return sb.ToString();
        }

        private static bool IsValidNumber(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return true;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }
    }
}
