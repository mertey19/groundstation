#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using GroundStation.DigitalTwin;
using GroundStation.Drone;
using GroundStation.Routes;
using Mapbox.Unity.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class GroundStationRegressionChecks
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    private static GameObject _root;
    private static DigitalTwinRemoteState _state;
    private static DigitalTwinJsonPoseBridge _bridge;
    private static RouteManager _route;
    private static DroneWaypointFollower _drone;
    private static readonly StringBuilder Report = new StringBuilder();
    private static int _checks;
    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [MenuItem("Tools/Simurgh/Yer Istasyonu Regresyon Testleri")]
    public static void Run()
    {
        _checks = 0; Report.Clear(); Directory.CreateDirectory("Logs");
        try
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run outside Play mode");
            if (Application.isBatchMode) EditorSceneManager.OpenScene("Assets/deneme.unity");
            var map = Object.FindObjectOfType<AbstractMap>();
            Check(map != null, "Scene map exists");
            _root = new GameObject("Ground station regression fixture"); _root.SetActive(false);
            _state = _root.AddComponent<DigitalTwinRemoteState>();
            _drone = Child("isolated drone").AddComponent<DroneWaypointFollower>();
            _route = Child("isolated route").AddComponent<RouteManager>();
            _route.ReplaceRoute(new List<WaypointData> { new WaypointData(0, Vector3.zero, 35) });
            _bridge = _root.AddComponent<DigitalTwinJsonPoseBridge>();
            Set(_bridge, "abstractMap", map); Set(_bridge, "drone", _drone); Set(_bridge, "routeManager", _route); Set(_bridge, "remoteState", _state);
            Set(_bridge, "smoothPoseUpdates", false);
            ValidationAndTelemetry();
            RoutesAndReplay();
            Commands();
            ExportAndSurvey();
            Sqlite();
            HudRegressionChecks.Run();
            Report.AppendLine("Existing HUD regression checks: PASS (14)");
            File.WriteAllText("Logs/groundstation-regression-checks.txt", "PASS: " + _checks + " ground-station checks\n" + Report);
            Debug.Log("[GroundStationRegressionChecks] PASS: " + _checks + " checks. Only isolated fixtures and loopback UDP were used.");
        }
        catch (Exception ex)
        {
            File.WriteAllText("Logs/groundstation-regression-checks.txt", "FAIL after " + _checks + " checks\n" + Report + ex);
            Debug.LogException(ex);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
        finally { if (_root != null) Object.DestroyImmediate(_root); }
    }

    private static void ValidationAndTelemetry()
    {
        Set(_bridge, "validateAuthToken", false); // Old scene settings cannot disable network authentication.
        Check(!_bridge.TryApplyDigitalTwinJson(Message("bad-auth", "uav", 1, Telemetry(10)).Replace("simurgh-2026", "wrong")), "Wrong authentication token rejected even with old scene flag off");
        Check(!_bridge.TryApplyDigitalTwinJson("{\"lat\":37.78,\"lon\":-122.4,\"alt\":35}"), "Legacy pose cannot bypass authentication");
        Check(!_bridge.TryApplyPoseJson("{}"), "Public legacy entry point is closed");
        Check(!_bridge.TryApplyDigitalTwinJson(Message("old-new-source", "uav", 1, Telemetry(80), Now - 86400000)), "First packet 24 hours old rejected");
        Check(!_bridge.TryApplyDigitalTwinJson(Message("future", "uav", 1, Telemetry(80), Now + 60000)), "Future packet rejected");
        Check(!_bridge.TryApplyDigitalTwinJson(Message("missing-time", "uav", 1, Telemetry(80), 0)), "Timestamp is required");
        Check(!_bridge.TryApplyDigitalTwinJson(Message("wrong-vehicle", "boat", 1, Telemetry(80))), "Unknown vehicle cannot masquerade as UAV");
        Check(!_bridge.TryApplyDigitalTwinJson(Message("missing-coordinate", "uav", 1, "\"pose\":{\"altitudeM\":5}")), "Partial pose cannot teleport vehicle to zero coordinates");
        Check(!_bridge.TryApplyDigitalTwinJson(Message("invalid-coordinate", "uav", 1, "\"pose\":{\"latitude\":200,\"longitude\":0}")), "Invalid coordinates rejected");
        Check(!_bridge.TryApplyDigitalTwinJson(Message("nan", "uav", 1, Telemetry(80).Replace("173", "\"NaN\""))), "Non-finite telemetry rejected");
        Check(!_bridge.TryApplyDigitalTwinJson(Message("empty-message", "uav", 1, "\"missionPhase\":\"\"")), "Empty message does not refresh connection");
        Check(_bridge.TryApplyDigitalTwinJson(Message("uav-main", "uav", 1, Telemetry(10))), "Authenticated UAV telemetry accepted");
        Check(_state.HasFreshBattery && _state.LastBatteryPercent == 10, "UAV critical battery is visible");
        Check(_bridge.TryApplyDigitalTwinJson(Message("rover-main", "rover", 1, Telemetry(95))), "Independent rover telemetry accepted");
        Check(_state.LastBatteryPercent == 10 && _state.LastVehicleType == "uav", "Rover never replaces UAV flight-safety state");
        Check(_state.GetVehicleState("rover").LastBatteryPercent == 95, "Rover state remains separately accessible");
        Check(_bridge.TryApplyDigitalTwinJson(Message("another-uav", "uav", 1, Telemetry(99))), "Additional source retained independently");
        Check(_state.LastBatteryPercent == 10 && _state.GetVehicleState("uav", "another-uav").LastBatteryPercent == 99, "Source changes do not silently replace selected UAV");
        Check(!_bridge.TryApplyDigitalTwinJson(Message("another-uav", "uav", 2, "\"route\":{\"waypoints\":[]}")) && _route.GetRouteData().Count == 1, "Unselected source cannot clear the active UAV route");
        Check(!_bridge.TryApplyDigitalTwinJson(Message("uav-main", "uav", 1, Telemetry(90))), "Duplicate sequence rejected");
        Check(_bridge.TryApplyDigitalTwinJson(Message("uav-main", "rover", 1, Telemetry(50))), "Same source name has independent per-vehicle ordering");
        var ui = Child("telemetry UI").AddComponent<DroneTelemetryUI>();
        _drone.transform.position = new Vector3(0, 50, 0);
        Set(ui, "remoteState", _state); Set(ui, "drone", _drone); Set(ui, "droneTransform", _drone.transform);
        Call(ui, "Update");
        Check(ui.LastAltitudeText.Contains("173") && ui.LastSpeedText.Contains("12") && ui.LastModeText.Contains("CANLI"), "Main HUD uses received values, not local model altitude/speed");
        Set(_state.SelectedUav, "_telemetryReceivedAt", Time.unscaledTime - 6f); Call(ui, "Update");
        Check(ui.LastModeText.Contains("GÜNCEL DEĞİL") && ui.LastAltitudeText.Contains("—"), "Stale live HUD never falls back to simulation");
        Check(_bridge.TryApplyDigitalTwinJson(Message("uav-main", "uav", 2, Telemetry(10))), "Next UAV packet restores freshness");
    }

    private static void RoutesAndReplay()
    {
        Check(_bridge.TryApplyDigitalTwinJson(Message("uav-main", "uav", 3, "\"routeMode\":\"replace\",\"route\":{\"waypoints\":[]}")), "Empty replace is acknowledged");
        Check(_route.GetRouteData().Count == 0, "Empty replace clears the previous route");
        _route.ReplaceRoute(new List<WaypointData> { new WaypointData(0, new Vector3(10,0,10), 35) });
        Check(_bridge.TryApplyDigitalTwinJson(Message("uav-main", "uav", 4, "\"routeMode\":\"append\",\"route\":{\"waypoints\":[]}")) && _route.GetRouteData().Count == 1, "Empty append preserves route");
        Check(!_bridge.TryApplyDigitalTwinJson(Message("uav-main", "uav", 5, "\"route\":{\"waypoints\":[{\"latitude\":999,\"longitude\":1}]}")) && _route.GetRouteData().Count == 1, "Invalid route rejected atomically");
        string live = Message("uav-main", "uav", 5, Telemetry(77));
        Check(_bridge.TryApplyDigitalTwinJson(live), "Rejected route did not consume sequence");
        var recorder = Child("recorder").AddComponent<DigitalTwinOperationRecorder>(); Set(recorder, "ingressBehaviour", _bridge);
        string file = Path.GetFullPath("Logs/regression-replay-fixture.jsonl");
        var rows = new[]
        {
            Mapbox.Json.JsonConvert.SerializeObject(new { type="ingress", timeMs=1000, payload=Message("uav-main","uav",5,Telemetry(33),Now-86400000) }),
            "broken line",
            Mapbox.Json.JsonConvert.SerializeObject(new { type="ingress", timeMs=2000, payload=Message("uav-main","uav",6,Telemetry(44),Now-86400000+1000) }),
            Mapbox.Json.JsonConvert.SerializeObject(new { type="ingress", timeMs=3000, payload="{broken message" })
        };
        File.WriteAllLines(file, rows); recorder.ReplayFromFile(file);
        Check(recorder.ReplayApplied == 1 && recorder.IsReplaying && _state.LastBatteryPercent == 33, "Old recorded packet replays in isolated validation scope");
        Check(_state.IsReplay && !_bridge.TryApplyDigitalTwinJson(Message("uav-main", "uav", 6, Telemetry(80))), "Live ingress cannot contaminate playback");
        recorder.AdvanceReplay(0.5f); Check(recorder.ReplayApplied == 1, "Playback preserves original timing");
        recorder.AdvanceReplay(1.1f); Check(recorder.ReplayApplied == 2 && _state.LastBatteryPercent == 44, "Second recorded frame applied at its timestamp");
        recorder.AdvanceReplay(3); Check(recorder.ReplayRejected == 2 && recorder.LastReplayInfo.Contains("2 reddedildi"), "Malformed lines and rejected packets counted honestly");
        Check(_state.IsReplay && !recorder.IsReplaying, "Finished replay remains visibly in replay mode");
        recorder.StopReplay();
        Check(!_state.IsReplay && _state.LastBatteryPercent == 77 && _route.GetRouteData().Count == 1, "Exiting playback restores live state and planned route");
        Check(_bridge.TryApplyDigitalTwinJson(Message("uav-main", "uav", 6, Telemetry(10))), "Live ordering resumes independently after playback");
    }

    private static void Commands()
    {
        var ingress = Child("ingress").AddComponent<DigitalTwinUdpIngress>(); Set(ingress, "sendAck", false); Set(ingress, "_ingress", _bridge);
        var egress = Child("egress").AddComponent<DigitalTwinCommandEgress>(); Set(egress, "udpIngress", ingress); Set(egress, "remoteState", _state);
        Set(egress, "missionEngine", Child("isolated mission").AddComponent<DigitalTwinMissionEngine>());
        var sent = new List<string>(); Action<byte[], IPEndPoint> capture = (bytes, endpoint) => sent.Add(Encoding.UTF8.GetString(bytes)); Set(egress, "TestSend", capture);
        Check(!egress.SendCommand("hold") && sent.Count == 0, "No command without a verified peer");
        Queue(ingress, Message("uav-main", "uav", 7, Telemetry(10)), "192.0.2.10"); Call(ingress, "Update");
        Queue(ingress, Message("rover-main", "rover", 2, Telemetry(95)), "192.0.2.20"); Call(ingress, "Update");
        Check(egress.TryResolveEndpoint("uav", out var uav, out var source) && uav.Address.ToString() == "192.0.2.10" && source == "uav-main", "UAV command target is tied to selected authenticated source");
        Check(egress.TryResolveEndpoint("rover", out var rover, out _) && rover.Address.ToString() == "192.0.2.20", "Rover target is independent");
        Queue(ingress, "{broken", "192.0.2.99"); Call(ingress, "Update");
        Check(egress.TryResolveEndpoint("uav", out var after, out _) && after.Equals(uav), "Rejected malformed packet cannot redirect commands");
        Queue(ingress, Message("uav-main", "uav", 8, Telemetry(80), Now - 86400000), "192.0.2.99"); Call(ingress, "Update");
        Check(egress.TryResolveEndpoint("uav", out after, out _) && after.Equals(uav), "Expired packet cannot redirect commands");
        Check(egress.SendCommand("hold") && egress.LastStatus == VehicleCommandStatus.Waiting, "Socket send means waiting, not executed");
        string firstId = egress.LastCommandId; int count = sent.Count;
        egress.HandleAck(Ack(firstId, "applied", "rover"), uav);
        Check(egress.LastStatus == VehicleCommandStatus.Waiting, "Wrong-vehicle command ACK ignored");
        egress.HandleAck(Ack(firstId, "applied"), new IPEndPoint(IPAddress.Parse("192.0.2.99"), 19092));
        Check(egress.LastStatus == VehicleCommandStatus.Waiting, "Wrong-sender command ACK ignored");
        egress.HandleAck(Ack(firstId, "applied").Replace("simurgh-2026", "wrong"), uav);
        Check(egress.LastStatus == VehicleCommandStatus.Waiting, "Unauthenticated command ACK ignored");
        egress.Tick(Time.unscaledTime + 2.1f);
        Check(sent.Count == count + 1 && sent[sent.Count - 1] == sent[sent.Count - 2], "Retry reuses exact command ID and bytes");
        egress.HandleAck(Ack(firstId, "accepted"), uav); count = sent.Count; egress.Tick(Time.unscaledTime + 4.5f);
        Check(egress.LastStatus == VehicleCommandStatus.Accepted && sent.Count == count, "Accepted command waits for application without resending");
        egress.HandleAck(Ack(firstId, "applied"), uav); Check(egress.LastStatus == VehicleCommandStatus.Applied, "Only verified applied ACK reports execution");
        Check(egress.SetSpeed(12), "Live speed command enters confirmation flow"); egress.Tick(Time.unscaledTime + 11);
        Check(egress.LastStatus == VehicleCommandStatus.TimedOut && egress.LastCommandInfo.Contains("bilinmiyor"), "Missing ACK yields unknown outcome, not false success");
        Check(!egress.SetAltitude(-5), "Invalid altitude command rejected before transport");
        _route.ReplaceRoute(new List<WaypointData> { new WaypointData(0, Vector3.zero, 41.304, -81.752, 35), new WaypointData(1, Vector3.one, 41.305, -81.753, 35) });
        Check(egress.UploadAndStart(_route.GetRouteData()), "Route upload starts a tracked transaction");
        string uploadId = egress.LastCommandId; int uploads = sent.Count;
        Check(sent[uploads - 1].Contains("upload_route") && !sent[uploads - 1].Contains("start_mission"), "Mission does not start before route ACK");
        egress.HandleAck(Ack(uploadId, "applied"), uav);
        Check(sent.Count == uploads + 1 && sent[sent.Count - 1].Contains("start_mission"), "Applied route ACK triggers mission start");
        egress.HandleAck(Ack(egress.LastCommandId, "applied"), uav);
        Check(egress.UploadAndStart(_route.GetRouteData()), "Second route can be staged"); uploadId = egress.LastCommandId;
        Check(egress.SendCommand("hold"), "Operator hold interrupts pending mission upload"); count = sent.Count;
        egress.HandleAck(Ack(uploadId, "applied"), uav);
        Check(sent.Count == count, "Late upload ACK after hold never starts a mission");
        egress.CancelPending("test complete"); _bridge.BeginReplay(); count = sent.Count;
        Check(!egress.SendReturnToLaunch() && sent.Count == count, "Replay never emits a live vehicle command"); _bridge.EndReplay();
        var peers = (IDictionary)Get(ingress, "_peers"); SetPublic(peers["uav"], "seenAt", Time.unscaledTime - 6f);
        Check(!egress.TryResolveEndpoint("uav", out _, out _), "Command peer expires independently");
        // Verify actual transport exclusively against a local socket with an ephemeral port.
        ingress.StopIngress();
        Queue(ingress, Message("uav-main", "uav", 8, Telemetry(10)), "127.0.0.1"); Call(ingress, "Update");
        using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            socket.Client.ReceiveTimeout = 1500;
            Set(egress, "targetPort", ((IPEndPoint)socket.Client.LocalEndPoint).Port); Set(egress, "TestSend", null);
            Check(egress.SendCommand("hold"), "UDP command sends to local test peer");
            var endpoint = new IPEndPoint(IPAddress.Any, 0); var bytes = socket.Receive(ref endpoint);
            var command = Mapbox.Json.JsonConvert.DeserializeObject<VehicleCommandMessage>(Encoding.UTF8.GetString(bytes));
            Check(command.command == "hold" && command.targetSourceId == "uav-main", "Loopback peer receives correctly addressed command envelope");
            Queue(ingress, Ack(command.commandId, "applied"), "127.0.0.1"); Call(ingress, "Update");
            Check(egress.LastStatus == VehicleCommandStatus.Applied, "Ingress command ACK dispatch reaches command tracker");
        }
    }

    private static void ExportAndSurvey()
    {
        var exporter = Child("exporter").AddComponent<RouteExporter>();
        var panel = Child("controls").AddComponent<DroneControlPanel>(); Set(panel, "routeManager", _route); Set(panel, "routeExporter", exporter);
        panel.OnExportJsonClicked();
        Check(File.Exists(panel.LastExportPath) && File.ReadAllText(panel.LastExportPath).Contains("waypoints"), "JSON export button creates a readable route file");
        Check(panel.LastOperationInfo.Contains("Kaydedildi"), "Export success includes visible filename");
        // This uniquely named file was created by this fixture, not a user export.
        File.Delete(panel.LastExportPath);
        var polygon = new List<Vector3> { new Vector3(0,0,0), new Vector3(120,0,0), new Vector3(120,0,120), new Vector3(90,0,120),
            new Vector3(90,0,30), new Vector3(30,0,30), new Vector3(30,0,120), new Vector3(0,0,120) };
        foreach (float angle in new[] { 0f, 17f, 90f, 173f })
        {
            for (int winding = 0; winding < 2; winding++)
            {
                Check(PolygonSurveyPath.TryBuild(polygon, angle * Mathf.Deg2Rad, 20, 20, out var points, out var transit, out var error), "Concave polygon planned at " + angle + " degrees / winding " + winding + ": " + error);
                bool allInside = true;
                for (int i = 1; i < points.Count; i++)
                    for (int j = 0; j <= 100; j++)
                    {
                        var p = Vector3.Lerp(points[i - 1], points[i], j / 100f);
                        if (p.x < -0.01f || p.x > 120.01f || p.z < -0.01f || p.z > 120.01f || (p.x > 30.01f && p.x < 89.99f && p.z > 30.01f)) allInside = false;
                    }
                Check(allInside, "All route legs stay inside analytic U-shaped area at " + angle + " degrees / winding " + winding);
                polygon.Reverse();
            }
        }
        var invalid = new List<Vector3> { Vector3.zero, new Vector3(100,0,100), new Vector3(100,0,0), new Vector3(0,0,100) };
        Check(!PolygonSurveyPath.TryBuild(invalid, 0, 20, 20, out _, out _, out _), "Self-intersecting area rejected");
        var planner = Child("survey").AddComponent<SurveyMissionPlanner>(); Set(planner, "routeManager", _route); Set(planner, "edgeInset", 0f);
        planner.SetSurveyPolygon(polygon); planner.SetTransects(0, 20); planner.GenerateSurveyRoute();
        Check(string.IsNullOrEmpty(planner.LastPlanError) && planner.LastPlanStats.turnaroundDistanceM == 0 && _route.GetRouteData().Count > 2, "Production planner clips polygon and does not extend turns outside it");
        int before = _route.GetRouteData().Count; planner.SetSurveyPolygon(invalid); planner.GenerateSurveyRoute();
        Check(_route.GetRouteData().Count == before && !string.IsNullOrEmpty(planner.LastPlanError), "Invalid survey preserves existing route and explains failure");
    }
    private static void Sqlite()
    {
        string path = Path.GetFullPath("Logs/sqlite-regression.db");
        using (var db = new SQLite4Unity3d.SQLiteConnection(path))
        {
            db.Execute("CREATE TABLE IF NOT EXISTS regression (id INTEGER PRIMARY KEY, value TEXT)");
            db.Execute("INSERT OR REPLACE INTO regression(id,value) VALUES(1,?)", "tile-cache-roundtrip");
            Check(db.ExecuteScalar<string>("SELECT value FROM regression WHERE id=1") == "tile-cache-roundtrip", "Unity SQLite driver writes and reads disk data");
        }
        using (var db = new SQLite4Unity3d.SQLiteConnection(path))
            Check(db.ExecuteScalar<int>("SELECT COUNT(*) FROM regression") == 1, "SQLite data persists after connection reopen");
    }
    private static string Telemetry(int battery) => "\"telemetry\":{\"altitudeM\":173,\"speedMps\":12,\"mode\":\"AUTO\",\"batteryPercent\":" + battery + "}";
    private static string Message(string source, string vehicle, long seq, string payload, long timestamp = -1) => "{\"schemaVersion\":\"1.0\",\"authToken\":\"simurgh-2026\",\"sourceId\":\"" + source + "\",\"vehicleType\":\"" + vehicle + "\",\"sequenceId\":" + seq + ",\"timestampMs\":" + (timestamp < 0 ? Now : timestamp) + "," + payload + "}";
    private static string Ack(string id, string status, string vehicle = "uav") => Mapbox.Json.JsonConvert.SerializeObject(new VehicleCommandAck { schemaVersion="1.0", type="command_ack", commandId=id, status=status, sourceId="uav-main", vehicleType=vehicle, timestampMs=Now, authToken="simurgh-2026" });
    private static void Check(bool ok, string description) { if (!ok) throw new Exception(description); _checks++; Report.AppendLine("PASS " + description); }
    private static GameObject Child(string name) { var go = new GameObject(name); go.transform.SetParent(_root.transform, false); return go; }
    private static void Set(object o, string n, object v) => o.GetType().GetField(n, Hidden).SetValue(o, v);
    private static void SetPublic(object o, string n, object v) => o.GetType().GetField(n).SetValue(o, v);
    private static object Get(object o, string n) => o.GetType().GetField(n, Hidden).GetValue(o);
    private static void Call(object o, string n) => o.GetType().GetMethod(n, Hidden).Invoke(o, null);
    private static void Queue(DigitalTwinUdpIngress ingress, string json, string address)
    {
        var type = typeof(DigitalTwinUdpIngress).GetNestedType("UdpPacket", BindingFlags.NonPublic); object packet = Activator.CreateInstance(type);
        type.GetField("json").SetValue(packet, json); type.GetField("sender").SetValue(packet, new IPEndPoint(IPAddress.Parse(address), 19090));
        object queue = Get(ingress, "_queue"); queue.GetType().GetMethod("Enqueue").Invoke(queue, new[] { packet });
    }
}
#endif
