using GroundStation.Drone;
using GroundStation.Routes;
using Mapbox.Unity.Map;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    public class DigitalTwinAutoBootstrap : MonoBehaviour
    {
        [SerializeField] private bool runOnAwake = true;
        [SerializeField] private GameObject bridgeRoot;
        [SerializeField] private DigitalTwinJsonPoseBridge jsonBridge;
        [SerializeField] private DigitalTwinRoverAdapter roverAdapter;
        [SerializeField] private DigitalTwinRemoteState remoteState;
        [SerializeField] private DigitalTwinMissionEngine missionEngine;
        [SerializeField] private DigitalTwinUdpIngress udpIngress;
        [SerializeField] private DigitalTwinAdaptiveFlowController adaptiveFlow;
        [SerializeField] private DigitalTwinOperationRecorder recorder;
        [SerializeField] private DigitalTwinSampleScenarioPlayer sampleScenarioPlayer;
        [SerializeField] private DigitalTwinImageryService imageryService;
        [SerializeField] private DigitalTwinKmlPosePlayer kmlPosePlayer;

        private void Awake()
        {
            if (runOnAwake)
                EnsureSetup();
        }

        [ContextMenu("Ensure Digital Twin Setup")]
        public void EnsureSetup()
        {
            ResolveRoots();
            EnsureBridgeComponents();
            EnsureReferences();
        }

        private void ResolveRoots()
        {
            if (bridgeRoot == null)
            {
                var existing = GameObject.Find("DigitalTwinBridge");
                if (existing != null) bridgeRoot = existing;
            }
            if (bridgeRoot == null)
            {
                bridgeRoot = new GameObject("DigitalTwinBridge");
            }

            if (jsonBridge == null) jsonBridge = FindObjectOfType<DigitalTwinJsonPoseBridge>();
            if (roverAdapter == null) roverAdapter = FindObjectOfType<DigitalTwinRoverAdapter>();
        }

        private void EnsureBridgeComponents()
        {
            if (bridgeRoot == null) return;

            remoteState = bridgeRoot.GetComponent<DigitalTwinRemoteState>() ?? bridgeRoot.AddComponent<DigitalTwinRemoteState>();
            missionEngine = bridgeRoot.GetComponent<DigitalTwinMissionEngine>() ?? bridgeRoot.AddComponent<DigitalTwinMissionEngine>();
            udpIngress = bridgeRoot.GetComponent<DigitalTwinUdpIngress>() ?? bridgeRoot.AddComponent<DigitalTwinUdpIngress>();
            adaptiveFlow = bridgeRoot.GetComponent<DigitalTwinAdaptiveFlowController>() ?? bridgeRoot.AddComponent<DigitalTwinAdaptiveFlowController>();
            recorder = bridgeRoot.GetComponent<DigitalTwinOperationRecorder>() ?? bridgeRoot.AddComponent<DigitalTwinOperationRecorder>();
            sampleScenarioPlayer = bridgeRoot.GetComponent<DigitalTwinSampleScenarioPlayer>() ?? bridgeRoot.AddComponent<DigitalTwinSampleScenarioPlayer>();
            imageryService = bridgeRoot.GetComponent<DigitalTwinImageryService>() ?? bridgeRoot.AddComponent<DigitalTwinImageryService>();
            kmlPosePlayer = bridgeRoot.GetComponent<DigitalTwinKmlPosePlayer>() ?? bridgeRoot.AddComponent<DigitalTwinKmlPosePlayer>();
        }

        private void EnsureReferences()
        {
            if (jsonBridge == null) return;

            var map = FindObjectOfType<AbstractMap>();
            var drone = FindObjectOfType<DroneWaypointFollower>();
            var routeManager = FindObjectOfType<RouteManager>();

            AssignIfEmpty(jsonBridge, "abstractMap", map);
            AssignIfEmpty(jsonBridge, "drone", drone);
            AssignIfEmpty(jsonBridge, "routeManager", routeManager);
            AssignIfEmpty(jsonBridge, "roverAdapter", roverAdapter);
            AssignIfEmpty(jsonBridge, "remoteState", remoteState);
            AssignIfEmpty(jsonBridge, "missionEngine", missionEngine);
            AssignIfEmpty(jsonBridge, "imageryService", imageryService);

            AssignIfEmpty(roverAdapter, "abstractMap", map);
            AssignIfEmpty(udpIngress, "ingressBehaviour", jsonBridge);
            AssignIfEmpty(adaptiveFlow, "missionEngine", missionEngine);
            AssignIfEmpty(adaptiveFlow, "remoteState", remoteState);
            AssignIfEmpty(adaptiveFlow, "udpIngress", udpIngress);
            AssignIfEmpty(recorder, "udpIngress", udpIngress);
            AssignIfEmpty(recorder, "ingressBehaviour", jsonBridge);
            AssignIfEmpty(sampleScenarioPlayer, "ingressBehaviour", jsonBridge);
            AssignIfEmpty(sampleScenarioPlayer, "abstractMap", map);
            AssignIfEmpty(imageryService, "remoteState", remoteState);
            AssignIfEmpty(kmlPosePlayer, "bridge", jsonBridge);
        }

        private static void AssignIfEmpty(Object target, string fieldName, Object value)
        {
            if (target == null || value == null) return;
            var field = target.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field == null) return;
            var current = field.GetValue(target) as Object;
            if (current != null) return;
            field.SetValue(target, value);
        }
    }
}
