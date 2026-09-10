#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using GroundStation.DigitalTwin;
using Mapbox.Json;
using Mapbox.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class LiveMapChecks
{
    private static IEnumerator _run;
    private static double _next;
    private static int _count;
    private static readonly StringBuilder Report = new StringBuilder();
    private static LiveMapReceiver _receiver;
    private static string _fixture;
    [MenuItem("Tools/Simurgh/Canli Harita Kontrolu (Play)")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying || _run != null) return;
        Report.Clear(); _count = 0; _next = 0; _run = Checks(); EditorApplication.update += Tick;
    }
    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < _next) return;
        try
        {
            if (!EditorApplication.isPlaying) throw new Exception("Play stopped during validation");
            if (!_run.MoveNext()) { Finish("PASS"); return; }
            _next = EditorApplication.timeSinceStartup + (_run.Current is float seconds ? seconds : 0.1f);
        }
        catch (Exception ex) { Report.AppendLine(ex.ToString()); Finish("FAIL"); Debug.LogException(ex); }
    }
    private static void Finish(string status)
    {
        EditorApplication.update -= Tick; _run = null;
        if (_receiver != null) { _receiver.AllowLocalTest = false; _receiver.ResetSession(); }
        File.WriteAllText("Logs/live-map-checks.txt", status + ": " + _count + " checks\n" + Report);
        Debug.Log("[LiveMapChecks] " + status + ": " + _count);
    }
    private static void Check(bool ok, string text)
    { if (!ok) throw new Exception(text); _count++; Report.AppendLine("PASS: " + text); }
    private static string Packet(long seq, Action<JObject> edit = null)
    {
        int end = _fixture.IndexOf('\n');
        var header = JObject.Parse(_fixture.Substring(0, end));
        header["sequence"] = seq; header["capturedAtMs"] = LiveMapProtocol.NowMs;
        edit?.Invoke(header);
        string body = _fixture.Substring(end + 1);
        int last = body.LastIndexOf("{\"type\":\"end\"");
        return header.ToString(Formatting.None) + "\n" + body.Substring(0,last) + "{\"type\":\"end\",\"sequence\":" + seq + "}\n";
    }
    private static bool Reject(string json, bool allowTest = true)
    {
        try { LiveMapProtocol.ReadSnapshot(new MemoryStream(Encoding.UTF8.GetBytes(json)), "simurgh-2026", allowTest); return false; }
        catch (Exception) { return true; }
    }
    private static Task<bool> Send(string json)
    {
        int port = _receiver.Port;
        return Task.Run(() =>
        {
            try
            {
                using (var client = new TcpClient())
                {
                    client.Connect("127.0.0.1", port); client.ReceiveTimeout = 3000;
                    var bytes = Encoding.UTF8.GetBytes(json); var stream = client.GetStream();
                    // Deliberately split at non-JSON boundaries to exercise TCP framing.
                    for (int i = 0; i < bytes.Length; i += 997) stream.Write(bytes, i, Math.Min(997, bytes.Length-i));
                    return LiveMapProtocol.ReadLine(stream).Contains("received");
                }
            }
            catch { return false; }
        });
    }
    private static IEnumerator Checks()
    {
        _fixture = File.ReadAllText("Temp/live-map-fixture.ndjson");
        var decoded = LiveMapProtocol.ReadSnapshot(new MemoryStream(Encoding.UTF8.GetBytes(_fixture)), "simurgh-2026", true, () => 100000);
        Check(decoded.Positions.Length > 10000, "Python-generated multipart cloud decodes in C#");
        Check(Vector3.Distance(decoded.Positions[0],new Vector3(-6,0,-4.8f)) < .0001f, "ROS x,y,z-up maps to Unity x,z-up,y");
        Check(decoded.Bounds.size.y > 3.3f, "Measured vertical wall height survives transport");
        Check(decoded.Header.authToken == null, "Credentials removed from retained metadata");
        Check(Reject(Packet(1,h=>h["authToken"]="wrong")), "Wrong token rejected");
        Check(Reject(Packet(1),false), "Synthetic data rejected by default");
        Check(Reject(Packet(1,h=>h["capturedAtMs"]=LiveMapProtocol.NowMs-6000)), "Stale measurement rejected");
        Check(Reject(Packet(1,h=>h["capturedAtMs"]=LiveMapProtocol.NowMs+6000)), "Future clock mismatch rejected");
        Check(Reject(Packet(1,h=>h["frameConvention"]="camera_optical")), "Unknown frame convention rejected");
        Check(Reject(Packet(1,h=>h["pointCount"]=50001)), "Oversized point count rejected before allocation");
        Check(Reject(Packet(1,h=>h["chunkCount"]=0)), "Invalid chunk count rejected");
        Check(Reject(Packet(1,h=>h["sourceId"]="<b>fake</b>")), "Markup source identifiers rejected");
        Check(Reject(Packet(1,h=>h["voxelSizeM"]=0)), "Zero sampling size rejected");
        Check(Reject(Packet(1).Replace("\"index\":0", "\"index\":2")), "Out-of-order chunk rejected");
        string truncated = Packet(1); truncated = truncated.Substring(0,truncated.LastIndexOf('{'));
        Check(Reject(truncated), "Missing commit cannot produce a map");
        Check(Reject(new string('x',LiveMapProtocol.MaxLineBytes+1)+"\n"), "Oversized line bounded");
        bool deadlineEnforced = false;
        try { long tick = 100000; LiveMapProtocol.ReadLine(new MemoryStream(Encoding.UTF8.GetBytes(new string('a',500)+"\n")),()=>tick+=100,100500); }
        catch(InvalidDataException) { deadlineEnforced = true; }
        Check(deadlineEnforced, "Absolute read deadline stops a continuously trickling connection");
        Check(Reject(Packet(1).Replace("[-6.0,-4.8,0.0", "[\"NaN\",-4.8,0.0")), "Non-numeric geometry rejected");

        _receiver = Object.FindObjectOfType<LiveMapReceiver>();
        Check(_receiver != null && _receiver.Listening, "Runtime bootstrap opens independent TCP map channel");
        _receiver.ResetSession(); _receiver.AllowLocalTest = true;
        var controller = Object.FindObjectOfType<DigitalTwinUIController>(true);
        controller.SetViewOpen(true);
        var workspace = Object.FindObjectOfType<DigitalTwinSiteWorkspace>(); workspace.SetLiveMode(true);
        var view = workspace.GetComponent<LiveMapView>();
        yield return .3f;
        Check(workspace.LiveMode && view.IsVisible && view.RenderedPointCount == 0, "Live mode starts empty, without a reference-site stand-in");
        var state = Object.FindObjectOfType<DigitalTwinRemoteState>(); string telemetrySource = state.LastSourceId;
        var ingress = Object.FindObjectOfType<DigitalTwinUdpIngress>(); var peer = ingress.LastSenderEndpoint;
        var task = Send(Packet(1)); while (!task.IsCompleted) yield return .05f; yield return .3f;
        Check(task.Result && _receiver.Current != null, "Real loopback TCP acknowledges a complete Python-produced snapshot");
        Check(view.RenderedPointCount == decoded.Positions.Length, "Every received point reaches the render mesh");
        Check(_receiver.IsFresh, "Newly measured map marked fresh");
        Check(state.LastSourceId == telemetrySource && Equals(peer,ingress.LastSenderEndpoint), "Cloud cannot select a command peer or update flight telemetry");

        var camera = workspace.GetComponent<DigitalTwin3DView>().RenderCamera;
        camera.Render(); var previous = RenderTexture.active; RenderTexture.active = camera.targetTexture;
        var pixels = new Texture2D(camera.targetTexture.width,camera.targetTexture.height,TextureFormat.RGB24,false);
        pixels.ReadPixels(new Rect(0,0,pixels.width,pixels.height),0,0); pixels.Apply(); RenderTexture.active = previous;
        int visible = 0; foreach(var p in pixels.GetPixels32()) if (p.g > 100 && p.r > 45) visible++;
        Check(visible > 500, "Camera pixel readback confirms colored geometry is visible");
        File.WriteAllBytes("Logs/live-map-render-test.png",pixels.EncodeToPNG()); Object.DestroyImmediate(pixels);
        ScreenCapture.CaptureScreenshot("Logs/live-map-ui-test.png"); yield return .2f;
        string ply = _receiver.ExportPly(Path.GetFullPath("Logs/LiveMapTestExport"));
        string text = File.ReadAllText(ply);
        Check(text.Contains("element vertex " + decoded.Positions.Length) && text.Contains("-6 -4.8 0"), "PLY export uses source ROS axes and exact point count");
        Check(!File.ReadAllText(Path.ChangeExtension(ply,".json")).Contains("simurgh-2026"), "Export sidecar excludes token");

        task = Send(Packet(1)); while(!task.IsCompleted)yield return .05f; yield return .1f;
        Check(!task.Result && _receiver.Current.Header.sequence == 1, "Duplicate version cannot replace the map");
        task = Send(Packet(2,h=>h["mapId"]="different-map")); while(!task.IsCompleted)yield return .05f;
        Check(!task.Result && _receiver.Current.Header.mapId=="synthetic-courtyard", "Different map frame session requires explicit reset");
        task = Send(Packet(2)); while(!task.IsCompleted)yield return .05f; yield return .2f;
        Check(task.Result && _receiver.Current.Header.sequence==2, "Next complete version replaces current map");
        string reduced = Packet(3,h=>{h["pointCount"]=1;h["chunkCount"]=1;});
        reduced = reduced.Substring(0,reduced.IndexOf('\n')+1) + "{\"type\":\"points\",\"index\":0,\"points\":[[1,2,3,40,50,60]]}\n{\"type\":\"end\",\"sequence\":3}\n";
        task=Send(reduced);while(!task.IsCompleted)yield return .05f;yield return .2f;
        Check(task.Result && view.RenderedPointCount==1 && _receiver.Current.Positions[0]==new Vector3(1,3,2), "Full replacement removes obsolete geometry after a map correction");
        task=Send(Packet(4));while(!task.IsCompleted)yield return .05f;yield return .2f;
        _receiver.Current.Header.capturedAtMs = LiveMapProtocol.NowMs-6000;
        Check(!_receiver.IsFresh && view.RenderedPointCount > 10000, "Lost stream becomes stale while preserving geometry");
        workspace.SetLiveMode(false); yield return .2f;
        Check(Object.FindObjectOfType<SimurghSiteImporter>().IsVisible && !view.IsVisible, "Reference survey remains separately selectable");
        workspace.SetLiveMode(true); yield return .2f;
        Check(view.IsVisible && view.RenderedPointCount > 10000 && !Object.FindObjectOfType<SimurghSiteImporter>().IsVisible, "Returning to live mode restores the measured cloud only");
        _receiver.ResetSession(); yield return .2f;
        Check(_receiver.Current == null && view.RenderedPointCount == 0, "Session reset clears geometry and old source lock");
        task=Send(Packet(1,h=>h["mapId"]="next-flight"));while(!task.IsCompleted)yield return .05f;yield return .2f;
        Check(task.Result && _receiver.Current.Header.mapId=="next-flight", "New flight accepted after explicit session reset");
        _receiver.ResetSession(); _receiver.AllowLocalTest = false;
        controller.SetViewOpen(false); yield return .2f;
        Check(!view.IsVisible && !camera.enabled, "Closing the twin releases geometry and disables its camera");
        controller.SetViewOpen(true); workspace.SetLiveMode(true); yield return .2f;
        ScreenCapture.CaptureScreenshot("Logs/live-map-waiting.png"); yield return .2f;
        Report.AppendLine("Synthetic local fixture only. No Jetson, RealSense or real flight was used; no vehicle commands sent.");
    }
}
#endif
