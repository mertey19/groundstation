#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using GroundStation.DigitalTwin;

/// <summary>
/// Tools > Simurgh > UI Butonlarini Temala Ekle
/// Sahneye UiButtonThemer ekler; Play'de tum UI butonlari ortak temaya gecer.
/// </summary>
public static class UiButtonThemerMenu
{
    [MenuItem("Tools/Simurgh/UI Butonlarini Temala Ekle")]
    public static void AddThemer()
    {
        var existing = Object.FindObjectOfType<UiButtonThemer>();
        if (existing != null)
        {
            Selection.activeObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            Debug.Log("[Simurgh] UI buton temasi zaten sahnede.");
            return;
        }

        var go = new GameObject("UiButtonThemer");
        go.AddComponent<UiButtonThemer>();
        Undo.RegisterCreatedObjectUndo(go, "Add UiButtonThemer");
        Selection.activeObject = go;
        EditorGUIUtility.PingObject(go);
        Debug.Log("[Simurgh] UI buton temasi eklendi. Play'e basinca tum butonlar temalanir.");
    }
}
#endif
