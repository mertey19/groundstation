using UnityEngine;

namespace GroundStation.DigitalTwin
{
    [RequireComponent(typeof(LineRenderer))]
    public class DigitalTwinRoverDetourView : MonoBehaviour
    {
        [SerializeField] private DigitalTwinMissionEngine missionEngine;
        [SerializeField] private float lineWidth = 0.35f;
        [SerializeField] private Color lineColor = new Color(1f, 0.8f, 0.2f, 0.95f);
        [SerializeField] private float yOffset = 0.2f;
        [SerializeField] private float refreshInterval = 0.2f;

        private LineRenderer _line;
        private float _nextRefresh;

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            if (missionEngine == null) missionEngine = FindObjectOfType<DigitalTwinMissionEngine>();
            ConfigureLine();
        }

        private void Update()
        {
            if (missionEngine == null || _line == null)
                return;
            if (Time.unscaledTime < _nextRefresh)
                return;
            _nextRefresh = Time.unscaledTime + Mathf.Max(0.1f, refreshInterval);
            RefreshLine();
        }

        private void ConfigureLine()
        {
            if (_line == null) return;
            _line.useWorldSpace = true;
            _line.loop = false;
            _line.startWidth = lineWidth;
            _line.endWidth = lineWidth;
            _line.positionCount = 0;
            var mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = lineColor;
            _line.material = mat;
            _line.numCornerVertices = 2;
            _line.numCapVertices = 2;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
        }

        private void RefreshLine()
        {
            var points = missionEngine.LastRoverDetour;
            if (points == null || points.Count < 2)
            {
                _line.positionCount = 0;
                return;
            }

            _line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                p.y += yOffset;
                _line.SetPosition(i, p);
            }
        }
    }
}
