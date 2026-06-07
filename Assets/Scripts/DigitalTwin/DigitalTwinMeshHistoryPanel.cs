using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.DigitalTwin
{
    public class DigitalTwinMeshHistoryPanel : MonoBehaviour
    {
        [SerializeField] private DigitalTwinMissionEngine missionEngine;
        [SerializeField] private Text meshTrendText;
        [SerializeField] private float refreshInterval = 0.5f;

        private float _nextRefresh;

        private void Awake()
        {
            if (missionEngine == null) missionEngine = FindObjectOfType<DigitalTwinMissionEngine>();
            if (meshTrendText == null)
                meshTrendText = GetComponent<Text>();
        }

        private void Update()
        {
            if (missionEngine == null || meshTrendText == null)
                return;
            if (Time.unscaledTime < _nextRefresh)
                return;
            _nextRefresh = Time.unscaledTime + Mathf.Max(0.1f, refreshInterval);
            meshTrendText.text = missionEngine.BuildMeshTrendSummary();
        }
    }
}
