using GroundStation.DigitalTwin;
using GroundStation.Routes;
using GroundStation.UI;
using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.Drone
{
    public class DroneControlPanel : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RouteManager routeManager;
        [SerializeField] private DroneWaypointFollower drone;
        [SerializeField] private RouteExporter routeExporter;
        [SerializeField] private SurveyMissionPlanner surveyMissionPlanner;

        [Header("Buttons (optional)")]
        [SerializeField] private Button startButton;
        [SerializeField] private Button stopButton;
        [SerializeField] private Button clearRouteButton;
        [SerializeField] private Button exportJsonButton;
        [SerializeField] private Button generateSurveyButton;

        [Header("Waypoint Edit Strip (auto runtime UI)")]
        [SerializeField] private WaypointMapEditUiProfile waypointMapEditUiProfile;
        [SerializeField] private WaypointMapEditUiSettings waypointMapEditUiSettings = new WaypointMapEditUiSettings();

        [SerializeField] private DigitalTwinRemoteState remoteState;
        [SerializeField] private DigitalTwinCommandEgress commandEgress;
        private Text _operationStatus, _modeCaption;
        private RectTransform _statusRow;
        public string LastExportPath { get; private set; } = "";
        public string LastOperationInfo { get; private set; } = "";
        private float _showExportUntil;

        private void Awake()
        {
            if (routeManager == null)
                routeManager = FindObjectOfType<RouteManager>();
            if (drone == null)
                drone = FindObjectOfType<DroneWaypointFollower>();
            if (routeExporter == null)
                routeExporter = FindObjectOfType<RouteExporter>();
            if (surveyMissionPlanner == null)
                surveyMissionPlanner = FindObjectOfType<SurveyMissionPlanner>();

            Bind(startButton, OnStartClicked);
            Bind(stopButton, OnStopClicked);
            Bind(clearRouteButton, OnClearRouteClicked);
            Bind(exportJsonButton, OnExportJsonClicked);
            Bind(generateSurveyButton, OnGenerateSurveyClicked);

            remoteState = FindObjectOfType<DigitalTwinRemoteState>();
            commandEgress = FindObjectOfType<DigitalTwinCommandEgress>();
            if (commandEgress == null) commandEgress = new GameObject("DigitalTwinCommandEgress").AddComponent<DigitalTwinCommandEgress>();
            var settings = waypointMapEditUiProfile != null
                ? waypointMapEditUiProfile.CreateSettingsSnapshot()
                : waypointMapEditUiSettings;
            WaypointMapEditUi.EnsureBelowDroneControlPanel(this, settings);
        }
        private void Start() => BuildStatusStrip();
        private static void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null) return;
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                if (button.onClick.GetPersistentTarget(i) == action.Target as Object
                    && button.onClick.GetPersistentMethodName(i) == action.Method.Name) return;
            button.onClick.AddListener(action);
        }
        private void OnEnable() { if (_statusRow != null) _statusRow.gameObject.SetActive(true); }
        private void OnDisable() { if (_statusRow != null) _statusRow.gameObject.SetActive(false); }
        private void OnDestroy() { if (_statusRow != null) Destroy(_statusRow.gameObject); }
        private void LateUpdate()
        {
            if (_statusRow == null || !(transform is RectTransform panel)) return;
            _statusRow.sizeDelta = new Vector2(panel.rect.width, 44);
            _statusRow.localPosition = panel.localPosition + new Vector3((0.5f - panel.pivot.x) * panel.rect.width, (1f - panel.pivot.y) * panel.rect.height + 5, 0);
        }

        public void OnStartClicked()
        {
            if (remoteState != null && remoteState.IsReplay) { LastOperationInfo = "Önce kayıt oynatmadan çıkın"; return; }
            if (GroundStationMode.IsLive(remoteState))
            {
                if (drone != null) drone.StopRoute();
                if (commandEgress != null) commandEgress.UploadAndStart(routeManager != null ? routeManager.GetRouteData() : null);
            }
            else if (drone != null) { drone.StartRoute(); LastOperationInfo = "Simülasyon başlatıldı"; }
        }

        public void OnStopClicked()
        {
            var recorder = FindObjectOfType<DigitalTwinOperationRecorder>();
            if (remoteState != null && remoteState.IsReplay) { if (recorder != null) recorder.StopReplay(); return; }
            if (GroundStationMode.IsLive(remoteState) && commandEgress != null) commandEgress.SendCommand("hold");
            if (drone != null) drone.StopRoute();
        }

        public void OnClearRouteClicked()
        {
            if (commandEgress != null) commandEgress.CancelPending("Plan temizlendi; bekleyen başlatma iptal edildi");
            LastOperationInfo = "Plan temizlendi";
            if (routeManager != null)
                routeManager.ClearRoute();
        }

        public void OnExportJsonClicked()
        {
            if (routeExporter == null || routeManager == null)
                return;

            routeExporter.SetRouteManager(routeManager);
            LastExportPath = routeExporter.ExportToFile("route_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".json");
            LastOperationInfo = string.IsNullOrEmpty(LastExportPath) ? "Rota kaydedilemedi" : "Kaydedildi · " + System.IO.Path.GetFileName(LastExportPath);
            _showExportUntil = Time.unscaledTime + 12f;
            if (_operationStatus != null) _operationStatus.text = LastOperationInfo;
        }

        private void BuildStatusStrip()
        {
            var rect = transform as RectTransform;
            if (rect == null) return;
            var row = new GameObject("OperationStatus", typeof(RectTransform), typeof(Image));
            // Sibling placement avoids becoming a fifth child of the button layout.
            row.transform.SetParent(transform.parent, false);
            var rt = (RectTransform)row.transform;
            _statusRow = rt;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0);
            LateUpdate();
            row.GetComponent<Image>().color = new Color(0.045f, 0.065f, 0.095f, 0.94f);
            _modeCaption = CreateText(row.transform, "Mode", new Vector2(0, 0), new Vector2(0.3f, 1));
            var modeButton = _modeCaption.transform.parent.gameObject.AddComponent<Button>();
            modeButton.targetGraphic = modeButton.GetComponent<Image>();
            modeButton.onClick.AddListener(() =>
            {
                var recorder = FindObjectOfType<DigitalTwinOperationRecorder>();
                if (remoteState != null && remoteState.IsReplay) { if (recorder != null) recorder.StopReplay(); }
                else GroundStationMode.SelectSimulation(GroundStationMode.IsLive(remoteState));
            });
            _operationStatus = CreateText(row.transform, "Status", new Vector2(0.3f, 0), Vector2.one);
            var openButton = _operationStatus.transform.parent.gameObject.AddComponent<Button>();
            openButton.targetGraphic = openButton.GetComponent<Image>();
            openButton.onClick.AddListener(() =>
            {
                if (!string.IsNullOrEmpty(LastExportPath) && System.IO.File.Exists(LastExportPath))
                    Application.OpenURL(new System.Uri(System.IO.Path.GetDirectoryName(LastExportPath)).AbsoluteUri);
            });
        }
        private static Text CreateText(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var area = new GameObject(name + "Area", typeof(RectTransform), typeof(Image));
            area.transform.SetParent(parent, false);
            area.GetComponent<Image>().color = new Color(0.1f, 0.13f, 0.18f);
            var rt = (RectTransform)area.transform; rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = new Vector2(2, 2); rt.offsetMax = new Vector2(-2, -2);
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(area.transform, false);
            rt = (RectTransform)go.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(8, 2); rt.offsetMax = new Vector2(-8, -2);
            var text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 18; text.color = TwinHudTheme.TextPrimary; text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = false; return text;
        }
        private void Update()
        {
            if (remoteState == null) remoteState = FindObjectOfType<DigitalTwinRemoteState>();
            if (_modeCaption != null) _modeCaption.text = remoteState != null && remoteState.IsReplay ? "KAYITTAN ÇIK"
                : GroundStationMode.IsLive(remoteState) ? "CANLI İHA ▾" : "SİMÜLASYON ▾";
            var recorder = remoteState != null && remoteState.IsReplay ? FindObjectOfType<DigitalTwinOperationRecorder>() : null;
            if (_operationStatus != null)
                _operationStatus.text = recorder != null ? recorder.LastReplayInfo : Time.unscaledTime < _showExportUntil ? LastOperationInfo
                    : commandEgress != null && !string.IsNullOrEmpty(commandEgress.LastCommandInfo) ? commandEgress.LastCommandInfo : LastOperationInfo;
        }

        public void OnGenerateSurveyClicked()
        {
            if (surveyMissionPlanner == null)
                surveyMissionPlanner = FindObjectOfType<SurveyMissionPlanner>();
            if (surveyMissionPlanner != null)
            {
                surveyMissionPlanner.GenerateSurveyRoute();
                LastOperationInfo = string.IsNullOrEmpty(surveyMissionPlanner.LastPlanError) ? "Tarama planı hazır" : surveyMissionPlanner.LastPlanError;
                _showExportUntil = Time.unscaledTime + 12f;
            }
        }
    }
}
