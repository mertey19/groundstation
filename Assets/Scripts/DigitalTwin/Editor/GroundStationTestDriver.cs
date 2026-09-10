#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Opt-in test runner for an already open editor. A fixed request file avoids opening
// another Unity instance or interacting with the operator's other desktop windows.
[InitializeOnLoad]
public static class GroundStationTestDriver
{
    private const string Request = "Temp/groundstation-test-request.txt";
    private static int _stage { get => SessionState.GetInt("GroundStationTestDriver.stage", 0); set => SessionState.SetInt("GroundStationTestDriver.stage", value); }
    private static DateTime _since;
    private static double _next;
    static GroundStationTestDriver() => EditorApplication.update += Tick;
    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < _next) return;
        _next = EditorApplication.timeSinceStartup + 0.5;
        try
        {
            if (_stage == 0)
            {
                if (!File.Exists(Request)) return;
                string request = File.ReadAllText(Request).Trim(); File.Delete(Request);
                if (request == "inspect_map") { InspectMap(); return; }
                if (request == "inspect")
                {
                    var report = new System.Text.StringBuilder();
                    foreach (var panel in UnityEngine.Object.FindObjectsOfType<GroundStation.Drone.DroneControlPanel>(true))
                    {
                        var serialized = new SerializedObject(panel);
                        report.AppendLine("Controller " + panel.name + " / " + panel.transform.GetType().Name + " active=" + panel.isActiveAndEnabled);
                        foreach (string field in new[] { "startButton", "stopButton", "exportJsonButton" })
                        {
                            var button = serialized.FindProperty(field).objectReferenceValue as UnityEngine.UI.Button;
                            report.AppendLine(field + "=" + (button != null ? button.name + " parent=" + button.transform.parent.name : "null"));
                        }
                    }
                    foreach (var button in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Button>())
                        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                            report.AppendLine(button.name + " -> " + button.onClick.GetPersistentTarget(i) + "." + button.onClick.GetPersistentMethodName(i));
                    foreach (var text in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>(true))
                        if (text.name == "Mode" || text.name == "Status") report.AppendLine("Status text " + text.name + " active=" + text.gameObject.activeInHierarchy + " pos=" + text.transform.position + " text=" + text.text);
                    File.WriteAllText("Logs/groundstation-runtime-ui.txt", report.ToString());
                    return;
                }
                if (request == "stop") { EditorApplication.isPlaying = false; return; }
                if (request == "live")
                {
                    if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; _stage = 5; _next = EditorApplication.timeSinceStartup + 8; }
                    else { _since = DateTime.UtcNow; LiveMapChecks.Run(); _stage = 6; }
                    return;
                }
                if (request != "all") return;
                if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; _stage = 1; return; }
                _stage = 1;
            }
            if (_stage == 1)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                GroundStationRegressionChecks.Run();
                if (!File.ReadAllText("Logs/groundstation-regression-checks.txt").StartsWith("PASS")) throw new Exception("Ground station regression failed");
                _stage = 2; EditorApplication.isPlaying = true; _next = EditorApplication.timeSinceStartup + 8;
            }
            else if (_stage == 2 && EditorApplication.isPlaying)
            {
                CheckRuntimeUi();
                _since = DateTime.UtcNow; MapRasterLoadingChecks.Run(); _stage = 3;
            }
            else if (_stage == 3 && Completed("Logs/map-raster-loading-checks.txt"))
            {
                _since = DateTime.UtcNow; DigitalTwinSiteChecks.Run(); _stage = 4;
            }
            else if (_stage == 4 && Completed("Logs/digital-twin-checks.txt"))
            {
                File.WriteAllText("Logs/groundstation-full-validation.txt", "PASS: ground station, HUD, map raster and digital twin checks\n" + DateTime.Now.ToString("O"));
                _stage = 0;
            }
            else if (_stage == 5 && EditorApplication.isPlaying)
            { _since = DateTime.UtcNow; LiveMapChecks.Run(); _stage = 6; }
            else if (_stage == 6 && Completed("Logs/live-map-checks.txt")) _stage = 0;
            else if (_stage >= 3 && DateTime.UtcNow - _since > TimeSpan.FromMinutes(3)) throw new TimeoutException("Runtime validation timed out");
        }
        catch (Exception ex)
        {
            _stage = 0;
            File.WriteAllText("Logs/groundstation-full-validation.txt", "FAIL\n" + ex);
            Debug.LogException(ex);
        }
    }
    private static bool Completed(string path)
    {
        if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) < _since) return false;
        if (!File.ReadAllText(path).StartsWith("PASS")) throw new Exception(path + " failed; see the report");
        return true;
    }
    private static void InspectMap()
    {
        var report = new System.Text.StringBuilder();
        var map = UnityEngine.Object.FindObjectOfType<Mapbox.Unity.Map.AbstractMap>();
        report.AppendLine("Play=" + EditorApplication.isPlaying);
        foreach (var camera in UnityEngine.Object.FindObjectsOfType<Camera>())
            report.AppendLine("Camera " + camera.name + " enabled=" + camera.enabled + " depth=" + camera.depth + " clear=" + camera.clearFlags + " background=" + camera.backgroundColor + " pos=" + camera.transform.position);
        if (map != null && map.MapVisualizer != null && map.CurrentExtent != null)
        {
            report.AppendLine("Extent=" + map.CurrentExtent.Count + " active=" + map.MapVisualizer.ActiveTiles.Count + " terrain=" + map.Terrain.ElevationType);
            foreach (var id in map.CurrentExtent)
                if (!map.MapVisualizer.ActiveTiles.ContainsKey(id)) report.AppendLine("Missing " + id);
            foreach (var tile in map.MapVisualizer.ActiveTiles.Values)
            {
                var mesh = tile.MeshFilter.sharedMesh;
                report.AppendLine(tile.name + " state=" + tile.RasterDataState + " elevation=" + tile.HeightDataState + " visual=" + tile.HasRasterVisual + " enabled=" + tile.MeshRenderer.enabled + " vertices=" + mesh.vertexCount + " submeshes=" + mesh.subMeshCount + " type=" + tile.ElevationType);
                foreach (var material in tile.MeshRenderer.sharedMaterials)
                    report.AppendLine("  material=" + material.name + " shader=" + material.shader.name + " texture=" + material.mainTexture + " matchesRaster=" + (material.mainTexture == tile.GetRasterData()));
            }
        }
        File.WriteAllText("Logs/map-render-inspection.txt", report.ToString());
        ScreenCapture.CaptureScreenshot("Logs/map-render-inspection.png");
    }
    private static void CheckRuntimeUi()
    {
        var panel = UnityEngine.Object.FindObjectOfType<GroundStation.Drone.DroneControlPanel>();
        if (panel == null) throw new Exception("Control panel missing");
        var rowObject = GameObject.Find("OperationStatus");
        var row = rowObject != null ? rowObject.transform as RectTransform : null;
        var control = panel.transform as RectTransform;
        if (row == null || control == null || row.parent != control.parent || Mathf.Abs(row.rect.width - control.rect.width) > 1)
            throw new Exception("Status strip does not align with the actual scene control panel");
        var a = new Vector3[4]; var b = new Vector3[4]; row.GetWorldCorners(a); control.GetWorldCorners(b);
        if (a[0].y <= b[1].y || a[1].y > Screen.height || a[0].x < 0 || a[2].x > Screen.width)
            throw new Exception("Status strip overlaps buttons or leaves the viewport");
        foreach (var label in row.GetComponentsInChildren<UnityEngine.UI.Text>())
            if (label.canvasRenderer.GetColor().grayscale < 0.5f || label.color.grayscale < 0.5f)
                throw new Exception("Button theme makes status text unreadable");
        var serialized = new SerializedObject(panel);
        var export = serialized.FindProperty("exportJsonButton").objectReferenceValue as UnityEngine.UI.Button;
        var before = new System.Collections.Generic.HashSet<string>(Directory.GetFiles(Application.persistentDataPath, "route_*.json"));
        export.onClick.Invoke(); // Only local file export, never flight controls.
        int created = 0;
        foreach (string path in Directory.GetFiles(Application.persistentDataPath, "route_*.json"))
            if (!before.Contains(path)) { created++; File.Delete(path); }
        if (created != 1) throw new Exception("Export click creates " + created + " files; expected exactly one");
        if (!panel.LastOperationInfo.Contains("Kaydedildi")) throw new Exception("Export result is not visible");
        typeof(GroundStation.Drone.DroneControlPanel).GetProperty("LastExportPath").SetValue(panel, "");
        typeof(GroundStation.Drone.DroneControlPanel).GetProperty("LastOperationInfo").SetValue(panel, "Yer istasyonu hazır");
        File.WriteAllText("Logs/groundstation-ui-checks.txt", "PASS: 5 runtime UI checks\nStatus row aligned with actual controls\nNo overlap or viewport overflow\nText remains readable after button theme\nOne file per real scene export click\nVisible export feedback\n");
    }
}
#endif
