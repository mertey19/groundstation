using GroundStation.UI;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    /// <summary>One optional detail panel at a time; data collectors stay enabled.</summary>
    public class DigitalTwinHudWorkspace : MonoBehaviour
    {
        public enum Detail { None, Trajectory, Mesh, Camera, Targets, Photos, Demo }
        public static DigitalTwinHudWorkspace Instance { get; private set; }
        public Detail Selected { get; private set; }
        private static readonly string[] Labels = { "Yörünge", "Ağ", "Kamera", "Hedefler", "Fotoğraflar", "Demo" };
        private DigitalTwinMissionSummaryPanel _summary;
        private RectTransform _telemetry, _speed, _surveyToggle;
        private float _nextResolve;
        private readonly Vector3[] _corners = new Vector3[4];

        public static bool Shows(Detail detail) => Instance == null || Instance.Selected == detail;
        public static bool IsSelected(Detail detail) => Instance != null && Instance.Selected == detail;
        public float ToolsY => DigitalTwinUIController.SiteWorkspaceOpen ? 392f : Mathf.Max(150f, Bottom(_telemetry) + 12f);
        public Rect ToolsRect => new Rect(16f, ToolsY, 344f, 102f);

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void Select(Detail detail) { Selected = detail; }

        private void Update()
        {
            if (Time.unscaledTime >= _nextResolve)
            {
                _nextResolve = Time.unscaledTime + 1f;
                if (_summary == null) _summary = FindObjectOfType<DigitalTwinMissionSummaryPanel>();
                if (_telemetry == null) _telemetry = FindRect("TelemetryPanel");
                if (_speed == null)
                {
                    var speed = FindObjectOfType<DroneSpeedAltitudePanel>();
                    if (speed != null) _speed = speed.transform as RectTransform;
                }
                if (_surveyToggle == null) _surveyToggle = FindRect("SurveyPanelToggleButton");
            }
            TwinHudTheme.RightColumnStartY = DigitalTwinUIController.SiteWorkspaceOpen ? 110f : Mathf.Max(130f, Mathf.Max(Bottom(_speed), Bottom(_surveyToggle)) + 16f);
            if (Input.GetKeyDown(KeyCode.Escape) && !HudInputBlocker.IsEditingText)
                Selected = Detail.None;
        }

        private static RectTransform FindRect(string name)
        {
            var go = GameObject.Find(name);
            return go != null ? go.transform as RectTransform : null;
        }

        private float Bottom(RectTransform rect)
        {
            if (rect == null || !rect.gameObject.activeInHierarchy) return 0f;
            rect.GetWorldCorners(_corners);
            var canvas = rect.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 bottom = RectTransformUtility.WorldToScreenPoint(camera, _corners[0]);
            return (Screen.height - bottom.y) / TwinHudTheme.UiScale;
        }

        public Rect DetailRect(float width, float height)
        {
            return new Rect(16f, ToolsRect.yMax + 8f, width, height);
        }

        private void OnGUI()
        {
            TwinHudTheme.BeginScaledHud();
            Rect r = ToolsRect;
            TwinHudTheme.Panel(r);
            GUI.Label(new Rect(r.x + 12f, r.y + 10f, 180f, 18f), "AYRINTILAR", TwinHudTheme.Title);
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && _summary != null;
            if (GUI.Button(new Rect(r.xMax - 96f, r.y + 8f, 84f, 22f), "Görev özeti", TwinHudTheme.Button))
                _summary.ToggleVisible();
            GUI.enabled = wasEnabled;
            for (int i = 0; i < Labels.Length; i++)
            {
                Detail detail = (Detail)(i + 1);
                Rect button = new Rect(r.x + 12f + (i % 3) * 108f, r.y + 36f + (i / 3) * 30f, 104f, 26f);
                Color previous = GUI.backgroundColor;
                if (Selected == detail) GUI.backgroundColor = TwinHudTheme.Accent;
                if (GUI.Button(button, Labels[i], TwinHudTheme.Button))
                    Selected = Selected == detail ? Detail.None : detail;
                GUI.backgroundColor = previous;
            }
            TwinHudTheme.EndScaledHud();
        }
    }
}
