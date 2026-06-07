using UnityEngine;
using UnityEngine.UI;
using GroundStation.Drone;

namespace GroundStation.UI
{
    /// <summary>
    /// Drone hiz (artir/azalt) ve yukseklik (artir/azalt) butonlarini yonetir.
    /// Bu scripti yeni bir panel uzerine ekleyin; panel icinde 4 buton ve istege bagli 2 Text ekleyin.
    /// 1920x1080 icin panel konumu UIPanelSafeLayout tarafindan "speedaltitude" veya "hizyukseklik" ismiyle ayarlanir.
    /// </summary>
    public class DroneSpeedAltitudePanel : MonoBehaviour
    {
        [Header("Drone")]
        [SerializeField] private DroneWaypointFollower drone;

        [Header("Hiz butonlari")]
        [SerializeField] private Button speedUpButton;
        [SerializeField] private Button speedDownButton;
        [Tooltip("Her tiklamada eklenen/cikarilan hiz (m/s)")]
        [SerializeField] private float speedStep = 2f;

        [Header("Yukseklik butonlari")]
        [SerializeField] private Button altitudeUpButton;
        [SerializeField] private Button altitudeDownButton;
        [Tooltip("Her tiklamada eklenen/cikarilan yukseklik (m)")]
        [SerializeField] private float altitudeStep = 5f;

        [Header("Gosterge (opsiyonel)")]
        [SerializeField] private Text speedLabel;
        [SerializeField] private Text altitudeLabel;
        [Tooltip("Label guncelleme sikligi (saniye)")]
        [SerializeField] private float labelUpdateInterval = 0.2f;
        [Header("UI Text")]
        [SerializeField] private int buttonFontSize = 24;
        [SerializeField] private bool buttonTextBold = true;
        [SerializeField] private Color buttonTextColor = new Color(0.12f, 0.12f, 0.12f, 1f);
        [SerializeField] private Color buttonColor = new Color(0.94f, 0.94f, 0.94f, 1f);

        private float _nextLabelUpdate;
        private Font _uiFont;

        private void Awake()
        {
            if (drone == null)
                drone = FindObjectOfType<DroneWaypointFollower>();

            AutoBindButtonsIfMissing();
            EnsureButtonLabelsAndStyle();

            if (speedUpButton != null) speedUpButton.onClick.AddListener(OnSpeedUp);
            if (speedDownButton != null) speedDownButton.onClick.AddListener(OnSpeedDown);
            if (altitudeUpButton != null) altitudeUpButton.onClick.AddListener(OnAltitudeUp);
            if (altitudeDownButton != null) altitudeDownButton.onClick.AddListener(OnAltitudeDown);
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextLabelUpdate)
            {
                _nextLabelUpdate = Time.unscaledTime + labelUpdateInterval;
                RefreshLabels();
            }
        }

        private void OnSpeedUp()
        {
            if (drone != null)
                drone.MoveSpeed = drone.MoveSpeed + speedStep;
        }

        private void OnSpeedDown()
        {
            if (drone != null)
                drone.MoveSpeed = drone.MoveSpeed - speedStep;
        }

        private void OnAltitudeUp()
        {
            if (drone != null)
                drone.AddAltitude(altitudeStep);
        }

        private void OnAltitudeDown()
        {
            if (drone != null)
                drone.AddAltitude(-altitudeStep);
        }

        private void RefreshLabels()
        {
            if (drone == null) return;

            if (speedLabel != null)
                speedLabel.text = string.Format("H\u0131z: {0:F1} m/s", drone.MoveSpeed);

            if (altitudeLabel != null)
                altitudeLabel.text = string.Format("Y\u00FCkseklik: {0:F0} m", drone.Altitude);
        }

        private void AutoBindButtonsIfMissing()
        {
            if (speedUpButton != null && speedDownButton != null && altitudeUpButton != null && altitudeDownButton != null)
                return;

            var buttons = GetComponentsInChildren<Button>(true);
            if (buttons == null || buttons.Length == 0) return;

            if (speedUpButton == null && buttons.Length > 0) speedUpButton = buttons[0];
            if (speedDownButton == null && buttons.Length > 1) speedDownButton = buttons[1];
            if (altitudeUpButton == null && buttons.Length > 2) altitudeUpButton = buttons[2];
            if (altitudeDownButton == null && buttons.Length > 3) altitudeDownButton = buttons[3];
        }

        private void EnsureButtonLabelsAndStyle()
        {
            StyleButton(speedUpButton, "H\u0131z Artt\u0131r");
            StyleButton(speedDownButton, "H\u0131z Azalt");
            StyleButton(altitudeUpButton, "Y\u00FCkseklik Artt\u0131r");
            StyleButton(altitudeDownButton, "Y\u00FCkseklik Azalt");
        }

        private void StyleButton(Button button, string label)
        {
            if (button == null) return;

            var img = button.GetComponent<Image>();
            if (img != null) img.color = buttonColor;

            var txt = button.GetComponentInChildren<Text>(true);
            if (txt == null)
            {
                var txtGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
                txtGo.transform.SetParent(button.transform, false);
                var txtRt = txtGo.GetComponent<RectTransform>();
                txtRt.anchorMin = Vector2.zero;
                txtRt.anchorMax = Vector2.one;
                txtRt.offsetMin = Vector2.zero;
                txtRt.offsetMax = Vector2.zero;
                txt = txtGo.GetComponent<Text>();
            }

            if (txt == null) return;
            txt.text = label;
            txt.fontSize = buttonFontSize;
            txt.fontStyle = buttonTextBold ? FontStyle.Bold : FontStyle.Normal;
            txt.color = buttonTextColor;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;

            var f = ResolveUIFont();
            if (f != null) txt.font = f;
        }

        private Font ResolveUIFont()
        {
            if (_uiFont != null) return _uiFont;

            try { _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (_uiFont == null)
            {
                try { _uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            }
            if (_uiFont == null)
            {
                var anyText = GetComponentInChildren<Text>(true);
                if (anyText != null) _uiFont = anyText.font;
            }
            return _uiFont;
        }
    }
}
