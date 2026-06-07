using UnityEngine;

namespace GroundStation.DigitalTwin
{
    public class DigitalTwinAdaptiveFlowController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private DigitalTwinMissionEngine missionEngine;
        [SerializeField] private DigitalTwinRemoteState remoteState;
        [SerializeField] private DigitalTwinUdpIngress udpIngress;

        [Header("Throughput Profile")]
        [SerializeField] private int normalMessagesPerFrame = 60;
        [SerializeField] private int twinOnlyMessagesPerFrame = 24;
        [SerializeField] private int emergencyMessagesPerFrame = 12;

        [Header("Optional Stream Root")]
        [SerializeField] private GameObject liveVideoRoot;
        [SerializeField] private bool disableVideoInTwinOnly = true;

        private bool _lastTwinOnly;
        private bool _lastEmergency;

        private void Awake()
        {
            if (missionEngine == null) missionEngine = FindObjectOfType<DigitalTwinMissionEngine>();
            if (remoteState == null) remoteState = FindObjectOfType<DigitalTwinRemoteState>();
            if (udpIngress == null) udpIngress = FindObjectOfType<DigitalTwinUdpIngress>();
        }

        private void Update()
        {
            if (missionEngine == null || udpIngress == null)
                return;

            bool twinOnly = missionEngine.TwinOnlyModeRecommended;
            bool emergency = missionEngine.EmergencyTwinOnly;
            if (twinOnly == _lastTwinOnly && emergency == _lastEmergency)
                return;

            _lastTwinOnly = twinOnly;
            _lastEmergency = emergency;

            if (emergency)
                udpIngress.MaxMessagesPerFrame = Mathf.Max(1, emergencyMessagesPerFrame);
            else if (twinOnly)
                udpIngress.MaxMessagesPerFrame = Mathf.Max(1, twinOnlyMessagesPerFrame);
            else
                udpIngress.MaxMessagesPerFrame = Mathf.Max(1, normalMessagesPerFrame);

            if (disableVideoInTwinOnly && liveVideoRoot != null)
                liveVideoRoot.SetActive(!twinOnly);

            if (remoteState != null)
            {
                if (emergency)
                    remoteState.SetWarning("Uyari: Link kritik, emergency TwinOnly aktif.");
                else if (twinOnly)
                    remoteState.SetWarning("Uyari: Link dusuk, TwinOnly akisa gecildi.");
            }
        }
    }
}
