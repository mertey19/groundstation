using GroundStation.DigitalTwin;
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

        private DigitalTwinRemoteState _remote;
        private DigitalTwinCommandEgress _commands;
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

        private void Start()
        {
            SetCaption(speedUpButton, "Hız +");
            SetCaption(speedDownButton, "Hız −");
            SetCaption(altitudeUpButton, "İrtifa +");
            SetCaption(altitudeDownButton, "İrtifa −");
            ArrangeButtons();
        }

        private static void SetCaption(Button button, string caption)
        {
            var text = button != null ? button.GetComponentInChildren<Text>(true) : null;
            if (text == null) return;
            text.text = caption;
            text.color = GroundStation.DigitalTwin.TwinHudTheme.TextPrimary;
        }

        private void OnSpeedUp()
        {
            Adjust(true, 1);
        }

        private void OnSpeedDown()
        {
            Adjust(true, -1);
        }

        private void OnAltitudeUp()
        {
            Adjust(false, 1);
        }

        private void OnAltitudeDown()
        {
            Adjust(false, -1);
        }

        private void Adjust(bool speed, int direction)
        {
            if (_remote == null) _remote = FindObjectOfType<DigitalTwinRemoteState>();
            if (_commands == null) _commands = FindObjectOfType<DigitalTwinCommandEgress>();
            if (_remote != null && _remote.IsReplay) return;
            if (GroundStationMode.IsLive(_remote))
            {
                if (!_remote.HasFreshTelemetry || _commands == null || _remote.Telemetry == null) return;
                if (speed) _commands.SetSpeed(_remote.Telemetry.speedMps + direction * speedStep);
                else _commands.SetAltitude(_remote.Telemetry.altitudeM + direction * altitudeStep);
            }
            else if (drone != null)
            {
                if (speed) drone.MoveSpeed += direction * speedStep;
                else drone.AddAltitude(direction * altitudeStep);
            }
        }

        private void RefreshLabels()
        {
            if (_remote == null) _remote = FindObjectOfType<DigitalTwinRemoteState>();
            if (GroundStationMode.IsLive(_remote) || (_remote != null && _remote.IsReplay))
            {
                if (speedLabel != null) speedLabel.text = _remote.HasFreshTelemetry ? _remote.LastSpeedText : "Hız: —";
                if (altitudeLabel != null) altitudeLabel.text = _remote.HasFreshTelemetry ? _remote.LastAltitudeText : "Yükseklik: —";
                return;
            }
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
            StyleButton(speedUpButton, "Hız +");
            StyleButton(speedDownButton, "Hız −");
            StyleButton(altitudeUpButton, "İrtifa +");
            StyleButton(altitudeDownButton, "İrtifa −");
        }

        private void ArrangeButtons()
        {
            if (GetComponent<RectTransform>() == null) return;
            foreach (var layout in GetComponents<LayoutGroup>()) layout.enabled = false;
            Button[] buttons = { speedUpButton, speedDownButton, altitudeUpButton, altitudeDownButton };
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null || buttons[i].transform.parent != transform) continue;
                var rect = buttons[i].transform as RectTransform;
                if (rect == null) continue;
                int column = i % 2, row = i / 2;
                rect.anchorMin = new Vector2(column * 0.5f, (1 - row) * 0.5f);
                rect.anchorMax = rect.anchorMin + new Vector2(0.5f, 0.5f);
                rect.offsetMin = new Vector2(8f, 8f);
                rect.offsetMax = new Vector2(-8f, -8f);
            }
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
