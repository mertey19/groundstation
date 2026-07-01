#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using GroundStation.DigitalTwin;

/// <summary>
/// Tools > Simurgh > Canli Kamera Paneli / Fail-safe RTL Gostergesi ekleme menuleri.
/// </summary>
public static class SafetyCameraMenu
{
    [MenuItem("Tools/Simurgh/Canli Kamera Paneli Ekle")]
    public static void AddCamera() => Ensure<DigitalTwinCameraFeedPanel>("DigitalTwinCameraFeedPanel");

    [MenuItem("Tools/Simurgh/Fail-safe RTL Gostergesi Ekle")]
    public static void AddSafety() => Ensure<DigitalTwinFlightSafetyPanel>("DigitalTwinFlightSafetyPanel");

    private static void Ensure<T>(string objectName) where T : Component
    {
        var existing = Object.FindObjectOfType<T>();
        if (existing != null)
        {
            Selection.activeObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            Debug.Log("[Simurgh] " + objectName + " zaten sahnede.");
            return;
        }
        var go = new GameObject(objectName);
        go.AddComponent<T>();
        Undo.RegisterCreatedObjectUndo(go, "Add " + objectName);
        Selection.activeObject = go;
        EditorGUIUtility.PingObject(go);
        Debug.Log("[Simurgh] " + objectName + " eklendi.");
    }
}
#endif
