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

            if (startButton != null) startButton.onClick.AddListener(OnStartClicked);
            if (stopButton != null) stopButton.onClick.AddListener(OnStopClicked);
            if (clearRouteButton != null) clearRouteButton.onClick.AddListener(OnClearRouteClicked);
            if (exportJsonButton != null) exportJsonButton.onClick.AddListener(OnExportJsonClicked);
            if (generateSurveyButton != null) generateSurveyButton.onClick.AddListener(OnGenerateSurveyClicked);

            var settings = waypointMapEditUiProfile != null
                ? waypointMapEditUiProfile.CreateSettingsSnapshot()
                : waypointMapEditUiSettings;
            WaypointMapEditUi.EnsureBelowDroneControlPanel(this, settings);
        }

        public void OnStartClicked()
        {
            if (drone != null)
                drone.StartRoute();
        }

        public void OnStopClicked()
        {
            if (drone != null)
                drone.StopRoute();
        }

        public void OnClearRouteClicked()
        {
            if (routeManager != null)
                routeManager.ClearRoute();
        }

        public void OnExportJsonClicked()
        {
            if (routeExporter == null || routeManager == null)
                return;

            string json = routeExporter.ExportToJson();
            Debug.Log($"[DroneControlPanel] Route JSON:\n{json}");
        }

        public void OnGenerateSurveyClicked()
        {
            if (surveyMissionPlanner == null)
                surveyMissionPlanner = FindObjectOfType<SurveyMissionPlanner>();
            if (surveyMissionPlanner != null)
                surveyMissionPlanner.GenerateSurveyRoute();
        }
    }
}