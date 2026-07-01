#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using GroundStation.DigitalTwin;

/// <summary>
/// Tools > Simurgh menusu: YKI gelistirmelerini sahneye ekler.
/// - Demo Senaryo Oynatici: faz akisini canlandirir (UDP gerektirmez).
/// - TUM YKI Gelistirmeleri: yorunge + mesh paneli + demo oynaticiyi birlikte kurar.
/// </summary>
public static class MissionDemoMenu
{
    [MenuItem("Tools/Simurgh/Demo Senaryo Oynatici Ekle")]
    public static void AddDemoPlayer()
    {
        var ex = Object.FindObjectOfType<DigitalTwinMissionDemoPlayer>();
        if (ex != null)
        {
            Selection.activeObject = ex.gameObject;
            EditorGUIUtility.PingObject(ex.gameObject);
            Debug.Log("[Simurgh] Demo oynatici zaten sahnede.");
            return;
        }
        var go = new GameObject("DigitalTwinMissionDemoPlayer");
        go.AddComponent<DigitalTwinMissionDemoPlayer>();
        Undo.RegisterCreatedObjectUndo(go, "Add MissionDemoPlayer");
        Selection.activeObject = go;
        EditorGUIUtility.PingObject(go);
        Debug.Log("[Simurgh] Demo oynatici eklendi. Play'e bas, alttaki panelden Oynat.");
    }

    [MenuItem("Tools/Simurgh/TUM YKI Gelistirmelerini Ekle (Hepsi)")]
    public static void AddAll()
    {
        EnsureSingleton<DigitalTwinTrajectoryComparison>("DigitalTwinTrajectoryComparison");
        EnsureSingleton<DigitalTwinMeshTopologyPanel>("DigitalTwinMeshTopologyPanel");
        EnsureSingleton<DigitalTwinMissionDemoPlayer>("DigitalTwinMissionDemoPlayer");
        EnsureSingleton<UiButtonThemer>("UiButtonThemer");
        EnsureSingleton<DigitalTwinCameraFeedPanel>("DigitalTwinCameraFeedPanel");
        EnsureSingleton<DigitalTwinFlightSafetyPanel>("DigitalTwinFlightSafetyPanel");
        Debug.Log("[Simurgh] Tum YKI gelistirmeleri hazir (yorunge + mesh + demo + kamera + fail-safe + buton temasi). Play -> Oynat.");
    }

    private static void EnsureSingleton<T>(string objectName) where T : Component
    {
        if (Object.FindObjectOfType<T>() != null) return;
        var go = new GameObject(objectName);
        go.AddComponent<T>();
        Undo.RegisterCreatedObjectUndo(go, "Add " + objectName);
    }
}
#endif
