using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    public class DigitalTwinUdpIngress : MonoBehaviour, IDigitalTwinAckSink
    {
        [Header("Ingress")]
        [SerializeField] private int listenPort = 19090;
        [SerializeField] private bool autoStartOnEnable = true;
        [SerializeField] private int maxMessagesPerFrame = 40;
        [SerializeField] private MonoBehaviour ingressBehaviour;

        [Header("ACK")]
        [SerializeField] private bool sendAck = true;
        [SerializeField] private string ackHost = "127.0.0.1";
        [SerializeField] private int ackPort = 19091;
        [SerializeField] private bool useSenderEndpointForAck = true;

        private IDigitalTwinIngress _ingress;
        private UdpClient _udp;
        private Thread _worker;
        private volatile bool _running;
        private readonly ConcurrentQueue<UdpPacket> _queue = new ConcurrentQueue<UdpPacket>();
        private IPEndPoint _lastSender;
        private IPEndPoint _ackSender;
        private sealed class VerifiedPeer
        {
            public IPEndPoint endpoint;
            public string source;
            public float seenAt;
        }
        private readonly System.Collections.Generic.Dictionary<string, VerifiedPeer> _peers = new System.Collections.Generic.Dictionary<string, VerifiedPeer>();
        public event System.Action<string, IPEndPoint> OnCommandAckReceived;
        public event System.Action<string> OnAcceptedJson;
        public bool TryGetVehicleEndpoint(string vehicle, out IPEndPoint endpoint, out string source)
        {
            endpoint = null; source = "";
            if (!_peers.TryGetValue(DigitalTwinRemoteState.VehicleKey(vehicle), out var peer) || Time.unscaledTime - peer.seenAt > 5f) return false;
            endpoint = new IPEndPoint(peer.endpoint.Address, peer.endpoint.Port);
            source = peer.source;
            return true;
        }
        public int MaxMessagesPerFrame { get => maxMessagesPerFrame; set => maxMessagesPerFrame = Mathf.Clamp(value, 1, 500); }
        /// <summary>Son paketi gonderen uc nokta (komut kanali icin hedef adres).</summary>
        public IPEndPoint LastSenderEndpoint => _lastSender;
        public event System.Action<string> OnJsonReceived;
        public event System.Action<string> OnAckSent;

        private struct UdpPacket
        {
            public string json;
            public IPEndPoint sender;
        }

        private void Awake()
        {
            ResolveIngress();
        }

        private void OnEnable()
        {
            ResolveIngress();
            if (autoStartOnEnable)
                StartIngress();
        }

        private void OnDisable()
        {
            StopIngress();
        }

        private void Update()
        {
            if (_ingress == null)
                ResolveIngress();

            int budget = Mathf.Max(1, maxMessagesPerFrame);
            while (budget-- > 0 && _queue.TryDequeue(out var packet))
            {
                _ackSender = packet.sender;
                // Command ACKs have a separate authenticated handler and never select a peer.
                try
                {
                    var envelope = Mapbox.Json.Linq.JObject.Parse(packet.json);
                    if ((string)envelope["type"] == "command_ack")
                    { OnCommandAckReceived?.Invoke(packet.json, packet.sender); continue; }
                }
                catch { }
                OnJsonReceived?.Invoke(packet.json);
                if (_ingress == null)
                    continue;

                bool ok = _ingress.TryApplyDigitalTwinJson(packet.json);
                if (ok && _ingress is DigitalTwinJsonPoseBridge bridge && bridge.LastAcceptedForDisplay)
                {
                    string vehicle = bridge.LastAcceptedVehicle;
                    // Pin a source/address while it is fresh; another authenticated sender
                    // cannot silently replace an active command peer.
                    if (!_peers.TryGetValue(vehicle, out var peer) || Time.unscaledTime - peer.seenAt > 5f
                        || (peer.source == bridge.LastAcceptedSource && peer.endpoint.Address.Equals(packet.sender.Address)))
                    {
                        _peers[vehicle] = new VerifiedPeer { endpoint = packet.sender, source = bridge.LastAcceptedSource, seenAt = Time.unscaledTime };
                        if (vehicle == "uav") _lastSender = packet.sender;
                    }
                }
                if (ok) OnAcceptedJson?.Invoke(packet.json);
                if (sendAck)
                {
                    string ack = _ingress.BuildLastAckJson();
                    PublishAck(ack);
                    OnAckSent?.Invoke(ack);
                    if (!ok)
                        Debug.LogWarning("[DigitalTwinUdpIngress] Message rejected. Ack sent.");
                }
            }
        }

        [ContextMenu("Start UDP Ingress")]
        public void StartIngress()
        {
            if (_running)
                return;
            try
            {
                _udp = new UdpClient(listenPort);
                _running = true;
                _worker = new Thread(ReceiveLoop) { IsBackground = true, Name = "DigitalTwinUdpIngress" };
                _worker.Start();
                Debug.Log("[DigitalTwinUdpIngress] Listening UDP on " + listenPort);
            }
            catch (SocketException e)
            {
                _running = false;
                Debug.LogError("[DigitalTwinUdpIngress] Failed to open UDP port " + listenPort + ": " + e.Message);
            }
        }

        [ContextMenu("Stop UDP Ingress")]
        public void StopIngress()
        {
            _running = false;
            try { _udp?.Close(); } catch { }
            _udp = null;
            if (_worker != null && _worker.IsAlive)
            {
                try { _worker.Join(200); } catch { }
            }
            _worker = null;
            while (_queue.TryDequeue(out _)) { }
            _peers.Clear();
            _lastSender = _ackSender = null;
        }

        public void PublishAck(string ackJson)
        {
            if (string.IsNullOrEmpty(ackJson))
                return;

            IPEndPoint endpoint = null;
            if (useSenderEndpointForAck && _ackSender != null)
                endpoint = _ackSender;
            if (endpoint == null)
            {
                if (!IPAddress.TryParse(ackHost, out var ip))
                    ip = IPAddress.Loopback;
                endpoint = new IPEndPoint(ip, ackPort);
            }

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(ackJson);
                using (var sender = new UdpClient())
                    sender.Send(bytes, bytes.Length, endpoint);
            }
            catch (SocketException e)
            {
                Debug.LogWarning("[DigitalTwinUdpIngress] Ack send failed: " + e.Message);
            }
        }

        private void ReceiveLoop()
        {
            while (_running)
            {
                try
                {
                    IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _udp.Receive(ref sender);
                    if (data == null || data.Length == 0)
                        continue;
                    string json = Encoding.UTF8.GetString(data);
                    if (_queue.Count < 256)
                        _queue.Enqueue(new UdpPacket { json = json, sender = sender });
                }
                catch (SocketException)
                {
                    if (_running)
                        Thread.Sleep(10);
                }
                catch
                {
                    Thread.Sleep(10);
                }
            }
        }

        private void ResolveIngress()
        {
            if (ingressBehaviour == null)
                ingressBehaviour = FindObjectOfType<DigitalTwinJsonPoseBridge>();
            _ingress = ingressBehaviour as IDigitalTwinIngress;
            if (_ingress == null && ingressBehaviour != null)
                Debug.LogWarning("[DigitalTwinUdpIngress] Selected ingressBehaviour does not implement IDigitalTwinIngress.");
        }
    }
}
