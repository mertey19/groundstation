using GroundStation.DigitalTwin;
using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.Drone
{
    /// <summary>
    /// Drone telemetrisini UI'da gosterir.
    /// Yukseklik = drone'un WORLD position.y (Unity Y ekseni yukari). Mapbox ayri koordinat kullanmiyorsa bu dogrudur.
    /// </summary>
    public class DroneTelemetryUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private DroneWaypointFollower drone;
        [SerializeField] private Transform droneTransform;

        [Header("Ground level (opsiyonel)")]
        [Tooltip("Yukseklik = position.y - groundLevel. 0 birakirsan ham world Y kullanilir.")]
        [SerializeField] private float groundLevel = 0f;

        [Header("UI Texts (UnityEngine.UI.Text)")]
        [SerializeField] private Text altitudeText;
        [SerializeField] private Text speedText;
        [SerializeField] private Text modeText;
        [SerializeField] private Text waypointIndexText;
        [SerializeField] private Text flightDurationText;
        [SerializeField] private Text fpsText;

        [SerializeField] private DigitalTwinRemoteState remoteState;
        private float _fpsSmoothed;
        private float _fpsAccum;
        private int _fpsFrames;
        private string _lastAltitudeText = "";
        private string _lastSpeedText = "";
        private string _lastModeText = "";
        private string _lastWaypointText = "";
        private string _lastFlightDurationText = "";
        private string _lastFpsText = "";

        public string LastAltitudeText => _lastAltitudeText;
        public string LastSpeedText => _lastSpeedText;
        public string LastModeText => _lastModeText;
        public string LastWaypointText => _lastWaypointText;
        public string LastFlightDurationText => _lastFlightDurationText;
        public string LastFpsText => _lastFpsText;
        public Text AltitudeTextUI => altitudeText;
        public Text SpeedTextUI => speedText;
        public Text ModeTextUI => modeText;
        public Text WaypointIndexTextUI => waypointIndexText;
        public Text FlightDurationTextUI => flightDurationText;
        public Text FpsTextUI => fpsText;

        private void Awake()
        {
            if (drone == null)
                drone = FindObjectOfType<DroneWaypointFollower>();

            if (droneTransform == null && drone != null)
                droneTransform = drone.transform;

            StyleTelemetryPanel();
        }

        private void Update()
        {
            UpdateFps();

            if (remoteState == null) remoteState = FindObjectOfType<DigitalTwinRemoteState>();
            bool remote = remoteState != null && (GroundStationMode.IsLive(remoteState) || remoteState.IsReplay || remoteState.IsSample);
            if (remote)
            {
                bool fresh = remoteState.HasFreshTelemetry;
                _lastAltitudeText = fresh ? remoteState.LastAltitudeText : "Yükseklik: —";
                _lastSpeedText = fresh ? remoteState.LastSpeedText : "Hız: —";
                string context = remoteState.IsReplay ? "KAYIT" : remoteState.IsSample ? "ÖRNEK" : "CANLI İHA";
                _lastModeText = context + " · " + (fresh ? remoteState.LastModeText : "VERİ GÜNCEL DEĞİL");
                _lastWaypointText = fresh ? remoteState.LastWaypointText : "WP: —";
                _lastFlightDurationText = "Kaynak: " + remoteState.LastSourceId;
            }
            else
            {
                if (droneTransform == null && drone != null) droneTransform = drone.transform;
                _lastAltitudeText = droneTransform != null ? string.Format("Yükseklik: {0:F1} m", droneTransform.position.y - groundLevel) : "Yükseklik: —";
                _lastSpeedText = drone != null ? string.Format("Hız: {0:F1} m/s", drone.CurrentSpeed) : "Hız: —";
                _lastModeText = "SİMÜLASYON · " + (drone != null && drone.IsRunning ? "ROTA" : "HAZIR");
                _lastWaypointText = drone != null ? "WP: " + drone.CurrentWaypointIndex : "WP: —";
                float sec = drone != null ? drone.FlightDurationSeconds : 0;
                _lastFlightDurationText = string.Format("Süre: {0:D2}:{1:D2}", (int)(sec / 60), (int)(sec % 60));
            }
            if (altitudeText != null) altitudeText.text = _lastAltitudeText;
            if (speedText != null) speedText.text = _lastSpeedText;
            if (modeText != null) modeText.text = _lastModeText;
            if (waypointIndexText != null) waypointIndexText.text = _lastWaypointText;
            if (flightDurationText != null) flightDurationText.text = _lastFlightDurationText;
        }

        private void UpdateFps()
        {
            if (fpsText == null) return;

            _fpsAccum += Time.unscaledDeltaTime;
            _fpsFrames++;
            if (_fpsAccum >= 0.25f)
            {
                float fps = _fpsFrames / _fpsAccum;
                _fpsSmoothed = Mathf.Lerp(_fpsSmoothed, fps, 0.25f);
                _fpsAccum = 0f;
                _fpsFrames = 0;
            }
            _lastFpsText = string.Format("FPS: {0:F0}", _fpsSmoothed > 0 ? _fpsSmoothed : (1f / Time.unscaledDeltaTime));
            fpsText.text = _lastFpsText;
        }

        // Sol ust telemetri panelini koyu "cam" temaya ceker: yuvarlatilmis arka plan + acik yazi.
        private bool _styled;
        private static Sprite _roundedSprite;

        private void StyleTelemetryPanel()
        {
            if (_styled) return;
            _styled = true;

            Text[] all = { altitudeText, speedText, modeText, waypointIndexText, flightDurationText, fpsText };
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                all[i].color = new Color(0.90f, 0.95f, 1f, 1f);
                all[i].fontStyle = FontStyle.Bold;
            }

            Transform p = altitudeText != null ? altitudeText.transform.parent : transform;
            Image bg = null;
            int guard = 0;
            while (p != null && bg == null && guard < 6)
            {
                bg = p.GetComponent<Image>();
                if (bg == null) p = p.parent;
                guard++;
            }
            if (bg != null)
            {
                bg.color = new Color(0.05f, 0.07f, 0.11f, 0.92f);
                bg.sprite = RoundedSprite();
                bg.type = Image.Type.Sliced;
            }
        }

        private static Sprite RoundedSprite()
        {
            if (_roundedSprite != null) return _roundedSprite;
            int s = 48, r = 12;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float cx = Mathf.Clamp(x, r, s - 1 - r);
                    float cy = Mathf.Clamp(y, r, s - 1 - r);
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d + 0.5f)));
                }
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            _roundedSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            return _roundedSprite;
        }
    }
}
