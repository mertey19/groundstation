using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using GroundStation.Routes;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    public enum VehicleCommandStatus { Waiting, Accepted, Applied, Rejected, TimedOut, Cancelled }
    [Serializable]
    public sealed class VehicleCommandMessage
    {
        public string schemaVersion = "1.0", type = "command", commandId, command, vehicleType, targetSourceId, authToken;
        public long timestampMs;
        public float value;
        public string altitudeReference = "relative_home";
        public TwinRouteBlock route;
    }
    [Serializable]
    public sealed class VehicleCommandAck
    {
        public string schemaVersion, type, commandId, vehicleType, sourceId, authToken, status, message;
        public long timestampMs;
    }
    public sealed class VehicleCommandResult
    {
        public VehicleCommandMessage command;
        public VehicleCommandStatus status;
        public string message;
    }

    public class DigitalTwinCommandEgress : MonoBehaviour
    {
        [SerializeField] private int targetPort = 19092;
        [SerializeField] private string authToken = "simurgh-2026";
        [SerializeField] private DigitalTwinUdpIngress udpIngress;
        [SerializeField] private DigitalTwinMissionEngine missionEngine;
        [SerializeField] private DigitalTwinRemoteState remoteState;
        private DigitalTwinUdpIngress _subscribedIngress;
        private sealed class Pending
        {
            public VehicleCommandMessage message;
            public IPEndPoint endpoint;
            public byte[] bytes;
            public float startedAt, sentAt;
            public int attempts;
            public bool accepted;
            public Action<bool> completion;
        }
        private readonly Dictionary<string, Pending> _pending = new Dictionary<string, Pending>();
        public string LastCommandInfo { get; private set; } = "";
        public VehicleCommandStatus LastStatus { get; private set; }
        public string LastCommandId { get; private set; } = "";
        public event Action<VehicleCommandResult> OnCommandStatus;
        // Isolated editor fixtures replace only the socket send.
        internal Action<byte[], IPEndPoint> TestSend;
        private void Awake() => Resolve();
        private void OnEnable() => Resolve();
        private void Resolve()
        {
            if (udpIngress == null) udpIngress = FindObjectOfType<DigitalTwinUdpIngress>();
            if (remoteState == null) remoteState = FindObjectOfType<DigitalTwinRemoteState>();
            if (missionEngine == null) missionEngine = FindObjectOfType<DigitalTwinMissionEngine>();
            if (_subscribedIngress == udpIngress) return;
            if (_subscribedIngress != null) _subscribedIngress.OnCommandAckReceived -= HandleAck;
            _subscribedIngress = udpIngress;
            if (_subscribedIngress != null) _subscribedIngress.OnCommandAckReceived += HandleAck;
        }
        private void OnDisable()
        {
            if (_subscribedIngress != null) _subscribedIngress.OnCommandAckReceived -= HandleAck;
            _subscribedIngress = null;
            CancelPending("Bağlantı kapandı; araç sonucu bilinmiyor");
        }
        private void Update() { Resolve(); Tick(Time.unscaledTime); }
        public bool TryResolveEndpoint(string vehicle, out IPEndPoint endpoint, out string source)
        {
            endpoint = null; source = "";
            if (udpIngress == null || targetPort < 1 || targetPort > 65535
                || !udpIngress.TryGetVehicleEndpoint(vehicle, out var peer, out source)) return false;
            endpoint = new IPEndPoint(peer.Address, targetPort);
            return true;
        }
        public bool SendCommand(string command, string vehicleType = "uav") => Send(command, vehicleType, 0, null, null);
        public bool SendReturnToLaunch() => SendCommand("rtl");
        public bool SendEmergencyStop() => SendCommand("emergency_stop");
        public bool SetSpeed(float speed) => Send("set_speed", "uav", speed, null, null);
        public bool SetAltitude(float altitude) => Send("set_altitude", "uav", altitude, null, null);
        public bool UploadAndStart(RouteData data)
        {
            if (data?.waypoints == null || data.Count < 2) return Fail("Başlatmak için en az iki waypoint gerekli");
            var route = new TwinRouteBlock { waypoints = new TwinRouteWaypoint[data.Count] };
            for (int i = 0; i < data.Count; i++)
            {
                var w = data.waypoints[i];
                if (w == null || !w.hasGeoPosition || !DigitalTwinMessageValidation.Geo(w.latitude, w.longitude)
                    || !DigitalTwinMessageValidation.Finite(w.targetAltitude) || w.targetAltitude <= 0)
                    return Fail("Rota coğrafi koordinatları veya irtifası eksik");
                route.waypoints[i] = new TwinRouteWaypoint { index = i, operation = "upsert", latitude = w.latitude,
                    longitude = w.longitude, altitudeM = w.targetAltitude, speedMps = w.metadata?.speedOverride ?? -1,
                    holdSeconds = w.metadata?.holdTimeSeconds ?? 0, action = w.metadata?.actionId ?? "" };
            }
            if (!TryResolveEndpoint("uav", out var originalEndpoint, out var originalSource)) return Fail("Doğrulanmış İHA bağlantısı yok");
            return Send("upload_route", "uav", 0, route, applied =>
            {
                if (!applied) return;
                if (!TryResolveEndpoint("uav", out var current, out var source) || !current.Equals(originalEndpoint) || source != originalSource)
                { Fail("Rota yüklendi; bağlantı değiştiği için başlatılmadı"); return; }
                SendCommand("start_mission");
            });
        }
        private bool Send(string command, string vehicle, float value, TwinRouteBlock route, Action<bool> completion)
        {
            Resolve();
            if (GroundStationMode.SimulationSelected || remoteState == null || remoteState.IsReplay || remoteState.IsSample)
                return Fail("Canlı komut için CANLI İHA modunu seçin");
            if (!DigitalTwinMessageValidation.Finite(value) || (command == "set_speed" && (value < 1 || value > 50))
                || (command == "set_altitude" && (value < 1 || value > 500))) return Fail("Komut değeri sınır dışında");
            if (command != "rtl" && command != "emergency_stop" && command != "hold" && command != "set_speed"
                && command != "set_altitude" && command != "upload_route" && command != "start_mission") return Fail("Desteklenmeyen komut");
            if (string.IsNullOrEmpty(vehicle) || (!TwinVehicleTypes.IsUav(vehicle) && !TwinVehicleTypes.IsRover(vehicle))) return Fail("Araç türü geçersiz");
            vehicle = DigitalTwinRemoteState.VehicleKey(vehicle);
            if (!TryResolveEndpoint(vehicle, out var endpoint, out var source)) return Fail("Taze ve doğrulanmış araç bağlantısı yok");
            bool interrupt = command == "hold" || command == "rtl" || command == "emergency_stop";
            if (interrupt) CancelPending("Operatör müdahalesi önceki işlemi iptal etti");
            else if (_pending.Count > 0) return Fail("Önceki komutun araç onayı bekleniyor");
            var message = new VehicleCommandMessage { commandId = Guid.NewGuid().ToString("N"), command = command,
                vehicleType = vehicle, targetSourceId = source, timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                authToken = authToken, value = value, route = route };
            byte[] bytes = Encoding.UTF8.GetBytes(Mapbox.Json.JsonConvert.SerializeObject(message));
            if (bytes.Length > 60000) return Fail("Rota tek paket sınırını aşıyor; waypoint sayısını azaltın");
            var pending = new Pending { message = message, endpoint = endpoint, bytes = bytes,
                startedAt = Time.unscaledTime, completion = completion };
            _pending[message.commandId] = pending;
            LastCommandId = message.commandId;
            if (!Transmit(pending, Time.unscaledTime))
            { Finish(pending, VehicleCommandStatus.Rejected, "Komut gönderilemedi"); return false; }
            Publish(pending, VehicleCommandStatus.Waiting, "Gönderildi · araç onayı bekleniyor");
            if (missionEngine != null) missionEngine.PushExternalEvent("operator_intervention", command + " -> " + vehicle + "/" + source);
            return true;
        }
        private bool Transmit(Pending pending, float now)
        {
            try
            {
                if (TestSend != null) TestSend(pending.bytes, pending.endpoint);
                else using (var sender = new UdpClient()) sender.Send(pending.bytes, pending.bytes.Length, pending.endpoint);
                pending.sentAt = now; pending.attempts++; return true;
            }
            catch (SocketException) { return false; }
        }
        public void HandleAck(string json, IPEndPoint sender)
        {
            VehicleCommandAck ack;
            try { ack = Mapbox.Json.JsonConvert.DeserializeObject<VehicleCommandAck>(json); } catch { return; }
            if (ack == null || ack.type != "command_ack" || ack.schemaVersion != "1.0" || string.IsNullOrEmpty(ack.commandId)
                || !_pending.TryGetValue(ack.commandId, out var pending) || sender == null || !sender.Address.Equals(pending.endpoint.Address)
                || ack.authToken != authToken || ack.sourceId != pending.message.targetSourceId || ack.vehicleType != pending.message.vehicleType
                || Math.Abs((double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ack.timestampMs) > 5000) return;
            if (ack.status == "accepted")
            { pending.accepted = true; Publish(pending, VehicleCommandStatus.Accepted, "Araç aldı · uygulama sonucu bekleniyor"); }
            else if (ack.status == "applied") Finish(pending, VehicleCommandStatus.Applied, "Araç uyguladı");
            else if (ack.status == "rejected") Finish(pending, VehicleCommandStatus.Rejected, "Araç reddetti: " + (ack.message ?? ""));
        }
        public void Tick(float now)
        {
            if ((remoteState != null && (remoteState.IsReplay || remoteState.IsSample)) || GroundStationMode.SimulationSelected)
            { CancelPending("Mod değişti; bekleyen işlem iptal edildi"); return; }
            foreach (var pending in new List<Pending>(_pending.Values))
            {
                if (now - pending.startedAt >= 10f) Finish(pending, VehicleCommandStatus.TimedOut, "ONAY ALINAMADI · araç sonucu bilinmiyor");
                else if (!pending.accepted && pending.attempts < 3 && now - pending.sentAt >= 2f)
                    Transmit(pending, now); // Same ID and payload; receiver must deduplicate.
            }
        }
        public void CancelPending(string reason)
        {
            foreach (var pending in new List<Pending>(_pending.Values)) Finish(pending, VehicleCommandStatus.Cancelled, reason);
        }
        private void Finish(Pending pending, VehicleCommandStatus status, string message)
        {
            if (!_pending.Remove(pending.message.commandId)) return;
            Publish(pending, status, message);
            pending.completion?.Invoke(status == VehicleCommandStatus.Applied);
        }
        private void Publish(Pending pending, VehicleCommandStatus status, string message)
        {
            LastStatus = status;
            LastCommandInfo = Label(pending.message.command) + " · " + message;
            OnCommandStatus?.Invoke(new VehicleCommandResult { command = pending.message, status = status, message = message });
        }
        private bool Fail(string message) { LastCommandInfo = message; LastStatus = VehicleCommandStatus.Rejected; return false; }
        private static string Label(string command)
        {
            switch (command)
            {
                case "upload_route": return "Rota yükleme";
                case "start_mission": return "Görev başlatma";
                case "set_speed": return "Hız değişikliği";
                case "set_altitude": return "İrtifa değişikliği";
                case "hold": return "Bekle";
                case "rtl": return "Eve dön";
                case "emergency_stop": return "Acil dur";
                default: return command;
            }
        }
    }
}

