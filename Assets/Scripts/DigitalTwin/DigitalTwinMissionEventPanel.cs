using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.DigitalTwin
{
    public class DigitalTwinMissionEventPanel : MonoBehaviour
    {
        [SerializeField] private DigitalTwinMissionEngine missionEngine;
        [SerializeField] private Text outputText;
        [SerializeField] private int maxEventsToShow = 6;
        [SerializeField] private float refreshInterval = 0.35f;

        private float _nextRefresh;

        private void Awake()
        {
            if (missionEngine == null) missionEngine = FindObjectOfType<DigitalTwinMissionEngine>();
            if (outputText == null) outputText = GetComponent<Text>();
        }

        private void Update()
        {
            if (missionEngine == null || outputText == null)
                return;
            if (Time.unscaledTime < _nextRefresh)
                return;
            _nextRefresh = Time.unscaledTime + Mathf.Max(0.1f, refreshInterval);
            RefreshText();
        }

        private void RefreshText()
        {
            var events = missionEngine.EventLog;
            var sb = new StringBuilder(256);
            sb.Append("Mission Events");
            sb.AppendLine();
            sb.Append("Phase: ").Append(missionEngine.CurrentPhase).Append(" | ").Append(missionEngine.CurrentPhaseStatus);
            sb.AppendLine();
            sb.Append(missionEngine.BuildMeshTrendSummary());
            sb.AppendLine();

            int take = Mathf.Min(maxEventsToShow, events.Count);
            for (int i = 0; i < take; i++)
            {
                int idx = events.Count - 1 - i;
                var evt = events[idx];
                sb.Append("- ").Append(evt.eventType).Append(": ").Append(evt.details);
                sb.AppendLine();
            }
            outputText.text = sb.ToString();
        }
    }
}
