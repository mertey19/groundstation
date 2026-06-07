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
        }

        private void Update()
        {
            UpdateFps();

            if (drone == null) return;

            if (droneTransform == null && drone != null)
                droneTransform = drone.transform;

            if (droneTransform == null) return;

            float worldY = droneTransform.position.y;
            float altitude = worldY - groundLevel;

            float speed = drone.CurrentSpeed;
            int wpIndex = drone.CurrentWaypointIndex;
            bool running = drone.IsRunning;

            if (altitudeText != null)
            {
                _lastAltitudeText = string.Format("Y\u00FCkseklik: {0:F1} m", altitude);
                altitudeText.text = _lastAltitudeText;
            }

            if (speedText != null)
            {
                _lastSpeedText = string.Format("H\u0131z: {0:F1} m/s", speed);
                speedText.text = _lastSpeedText;
            }

            if (modeText != null)
            {
                _lastModeText = running ? "Mod: GUIDED (Route)" : "Mod: IDLE";
                modeText.text = _lastModeText;
            }

            if (waypointIndexText != null)
            {
                _lastWaypointText = string.Format("WP Index: {0}", wpIndex);
                waypointIndexText.text = _lastWaypointText;
            }

            float flightSec = drone.FlightDurationSeconds;
            if (flightDurationText != null)
            {
                int minutes = (int)(flightSec / 60f);
                int seconds = (int)(flightSec % 60f);
                _lastFlightDurationText = string.Format("U\u00E7u\u015F: {0:D2}:{1:D2}", minutes, seconds);
                flightDurationText.text = _lastFlightDurationText;
            }
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
    }
}
