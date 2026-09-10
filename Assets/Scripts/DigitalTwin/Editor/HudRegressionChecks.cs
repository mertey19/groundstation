#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using GroundStation.DigitalTwin;
using GroundStation.UI;
using UnityEditor;
using UnityEngine;

public static class HudRegressionChecks
{
    private static int _checks;

    [MenuItem("Tools/Simurgh/HUD Gorunum Raporu (Play)")]
    public static void CaptureRuntime()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
        var hud = UnityEngine.Object.FindObjectOfType<DigitalTwinHudWorkspace>();
        if (hud == null) throw new InvalidOperationException("HUD workspace missing.");
        Rect tools = hud.ToolsRect;
        Vector2 point = tools.center * TwinHudTheme.UiScale;
        point.y = Screen.height - point.y;
        if (!HudInputBlocker.ContainsScreenPoint(point)) throw new InvalidOperationException("HUD does not block map clicks.");
        Rect detail = hud.DetailRect(TwinHudTheme.LeftPanelWidth, 268f);
        if (detail.Overlaps(tools) || detail.yMax > TwinHudTheme.ScreenH - 60f)
            throw new InvalidOperationException("Detail panel does not fit the viewport.");
        var route = UnityEngine.Object.FindObjectOfType<GroundStation.Routes.RouteManager>();
        var fence = UnityEngine.Object.FindObjectOfType<DigitalTwinGeofence>();
        var state = UnityEngine.Object.FindObjectOfType<DigitalTwinRemoteState>();
        var trajectory = UnityEngine.Object.FindObjectOfType<DigitalTwinTrajectoryComparison>();
        Directory.CreateDirectory("Logs");
        string report = $"PASS: runtime layout and HUD input blocking\nViewport: {Screen.width} x {Screen.height}\nSelected: {hud.Selected}\n"
            + $"Waypoints: {(route != null ? route.GetRouteData().Count : -1)}\nFresh telemetry: {(state != null && state.HasFreshMessage)}\n"
            + $"Position fix: {(fence != null && fence.HasPositionFix)}\nGeofence breach: {(fence != null && fence.AnyBreached)}\n"
            + $"Trajectory samples: {(trajectory != null ? trajectory.SampleCount : 0)}\n";
        File.WriteAllText("Logs/hud-runtime.txt", report);
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/hud-preview.png"));
        Debug.Log(report);
    }

    [MenuItem("Tools/Simurgh/HUD Regresyon Kontrolu")]
    public static void Run()
    {
        _checks = 0;
        var root = new GameObject("HUD regression fixture") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var hold = new HoldToConfirm();
            Check(!hold.Tick(true, 0f, 1.2f), "Press does not send immediately");
            Check(!hold.Tick(true, 1f, 1.2f), "Short hold does not send");
            Check(hold.Tick(true, 1.21f, 1.2f), "Continuous hold confirms");
            Check(!hold.Tick(true, 4f, 1.2f), "Long hold sends only once");
            hold.Tick(false, 4.1f, 1.2f);
            Check(!hold.Tick(true, 5f, 1.2f), "Release rearms without confirming");
            Check(hold.Tick(true, 6.21f, 1.2f), "Second deliberate hold confirms");
            hold.Reset();
            Check(!hold.Tick(true, 10f, 1.2f), "Focus reset cancels the prior hold");

            var state = root.AddComponent<DigitalTwinRemoteState>();
            Check(!state.HasFreshMessage && !state.HasFreshBattery, "No data is unknown");
            state.ApplyTelemetry(new TwinTelemetryBlock { batteryPercent = 76f }, "fixture", 1);
            Check(state.HasFreshMessage && state.HasFreshBattery, "Telemetry freshness is recorded");
            SetField(state.SelectedUav, "_batteryReceivedAt", Time.unscaledTime - 6f);
            Check(!state.HasFreshBattery, "Old battery data expires independently");
            state.ApplyOperationalState(new DigitalTwinMessageV1 { sourceId = "fixture", meshLink = new TwinMeshStatus { linkQualityPercent = 80f } });
            Check(state.HasFreshMesh, "Mesh freshness is recorded");
            state.Clear();
            Check(!state.HasFreshMessage && !state.HasFreshMesh && state.LastBatteryPercent < 0f, "Clear removes all freshness");

            var engine = root.AddComponent<DigitalTwinMissionEngine>();
            var summary = root.AddComponent<DigitalTwinMissionSummaryPanel>();
            SetField(summary, "missionEngine", engine);
            Call(summary, "OnEnable");
            engine.PushExternalEvent("operator_intervention", "regression fixture");
            Check((int)GetField(summary, "_interventionCount") == 1, "Assigned mission engine is subscribed");
            Call(summary, "OnDisable");
            Call(summary, "OnEnable");
            Call(summary, "OnEnable");
            engine.PushExternalEvent("operator_intervention", "regression fixture");
            Check((int)GetField(summary, "_interventionCount") == 2, "Reenable subscribes once");
            Call(summary, "OnDisable");

            string report = $"PASS: {_checks} HUD regression checks\nUnity {Application.unityVersion}\n{DateTime.Now:O}\nNo vehicle commands were sent.\n";
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/hud-regression.txt", report);
            Debug.Log(report);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void Check(bool result, string message)
    {
        if (!result) throw new InvalidOperationException("HUD regression failed: " + message);
        _checks++;
    }

    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    private static void SetField(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static object GetField(object target, string name) => Field(target, name).GetValue(target);
    private static void Call(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
}
#endif
