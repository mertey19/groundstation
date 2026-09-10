using UnityEngine;

namespace GroundStation.DigitalTwin
{
    public static class GroundStationMode
    {
        private enum Selection { Auto, Simulation, Live }
        private static Selection _selection;
        public static bool SimulationSelected => _selection == Selection.Simulation;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => _selection = Selection.Auto;
        public static bool IsLive(DigitalTwinRemoteState state) => !SimulationSelected && state != null && !state.IsSample && !state.IsReplay
            && (_selection == Selection.Live || state.UseJsonTelemetry);
        public static void SelectSimulation(bool simulation)
        {
            _selection = simulation ? Selection.Simulation : Selection.Live;
            var state = Object.FindObjectOfType<DigitalTwinRemoteState>();
            if (!simulation && state != null) state.IsSample = false;
            var bridge = Object.FindObjectOfType<DigitalTwinJsonPoseBridge>();
            if (bridge != null) bridge.EndReplay();
            var drone = Object.FindObjectOfType<GroundStation.Drone.DroneWaypointFollower>();
            if (drone != null) drone.StopRoute();
            var commands = Object.FindObjectOfType<DigitalTwinCommandEgress>();
            if (commands != null) commands.CancelPending("Kontrol modu değişti; araç sonucu bilinmiyor");
        }
    }
}
