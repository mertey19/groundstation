using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// YKI -> arac KOMUT kanali (PDF 3.3 manuel mudahale / acil durum).
    /// Simdiye kadar sistem tek yonluydu (telemetri ingest + ACK); bu bilesen
    /// operatorun RTL / acil dur gibi komutlarini UDP JSON olarak araca gonderir.
    ///
    /// Hedef adres cozumu: oncelik son telemetri paketini gonderen IP (arac zaten
    /// konusuyor), yoksa Inspector'daki targetHost. Port ayri (19092) — aracin
    /// gorev bilgisayari bu portu dinleyip komutu MAVLink/MAVSDK'ya cevirir.
    ///
    /// Komut JSON'u: {"schemaVersion":"1.0","type":"command","command":"rtl",
    ///   "vehicleType":"uav","timestampMs":...,"authToken":"..."}
    /// </summary>
    public class DigitalTwinCommandEgress : MonoBehaviour
    {
        [Header("Hedef")]
        [SerializeField] private string targetHost = "127.0.0.1";
        [SerializeField] private int targetPort = 19092;
        [Tooltip("Acik ise son telemetri gonderen aracin IP'si hedef alinir (port yine targetPort).")]
        [SerializeField] private bool preferLastIngressSenderIp = true;

        [Header("Kimlik")]
        [SerializeField] private string authToken = "simurgh-2026";

        [Header("Bagimliliklar (otomatik bulunur)")]
        [SerializeField] private DigitalTwinUdpIngress udpIngress;
        [SerializeField] private DigitalTwinMissionEngine missionEngine;
        [SerializeField] private DigitalTwinRemoteState remoteState;

        public string LastCommandInfo { get; private set; } = "";

        [Serializable]
        private class CommandMessage
        {
            public string schemaVersion = DigitalTwinJsonSchema.Version1;
            public string type = "command";
            public string command = "";
            public string vehicleType = "";
            public long timestampMs;
            public string authToken = "";
        }

        private void Awake()
        {
            if (udpIngress == null) udpIngress = FindObjectOfType<DigitalTwinUdpIngress>();
            if (missionEngine == null) missionEngine = FindObjectOfType<DigitalTwinMissionEngine>();
            if (remoteState == null) remoteState = FindObjectOfType<DigitalTwinRemoteState>();
        }

        /// <summary>"rtl" | "emergency_stop" | "hold" ... komutunu araca gonderir.</summary>
        public bool SendCommand(string command, string vehicleType = "uav")
        {
            if (string.IsNullOrEmpty(command)) return false;

            var msg = new CommandMessage
            {
                command = command,
                vehicleType = vehicleType ?? "uav",
                timestampMs = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds,
                authToken = authToken
            };
            string json = JsonUtility.ToJson(msg, false);
            IPEndPoint endpoint = ResolveEndpoint();

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                using (var sender = new UdpClient())
                    sender.Send(bytes, bytes.Length, endpoint);
            }
            catch (SocketException e)
            {
                Debug.LogWarning("[CommandEgress] Komut gonderilemedi: " + e.Message);
                LastCommandInfo = command + " GONDERILEMEDI";
                return false;
            }

            // Hakem kaniti: her operator mudahalesi gorev gunlugune ve kayda islenir.
            LastCommandInfo = command + " -> " + endpoint;
            if (missionEngine != null)
                missionEngine.PushExternalEvent("operator_intervention", "command=" + command + " target=" + endpoint);
            if (remoteState != null)
                remoteState.SetWarning("Operatör komutu gönderildi: " + CommandLabelTr(command));
            Debug.Log("[CommandEgress] " + LastCommandInfo);
            return true;
        }

        public bool SendReturnToLaunch() => SendCommand("rtl");
        public bool SendEmergencyStop() => SendCommand("emergency_stop");

        private IPEndPoint ResolveEndpoint()
        {
            if (preferLastIngressSenderIp && udpIngress != null && udpIngress.LastSenderEndpoint != null)
                return new IPEndPoint(udpIngress.LastSenderEndpoint.Address, targetPort);

            IPAddress ip;
            if (!IPAddress.TryParse(targetHost, out ip))
                ip = IPAddress.Loopback;
            return new IPEndPoint(ip, targetPort);
        }

        private static string CommandLabelTr(string command)
        {
            switch (command)
            {
                case "rtl": return "EVE DÖN (RTL)";
                case "emergency_stop": return "ACİL DUR";
                case "hold": return "BEKLE";
                default: return command;
            }
        }
    }
}
