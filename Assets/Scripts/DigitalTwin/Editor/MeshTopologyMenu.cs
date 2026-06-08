#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using GroundStation.DigitalTwin;

/// <summary>
/// Tools > Simurgh > Mesh Ag Paneli Ekle
/// Sahneye DigitalTwinMeshTopologyPanel ekler (IHA-Rover-YKI topoloji + canli metrikler).
/// </summary>
public static class MeshTopologyMenu
{
    [MenuItem("Tools/Simurgh/Mesh Ag Paneli Ekle")]
    public static void AddMeshPanel()
    {
        var existing = Object.FindObjectOfType<DigitalTwinMeshTopologyPanel>();
        if (existing != null)
        {
            Selection.activeObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            Debug.Log("[Simurgh] Mesh paneli zaten sahnede.");
            return;
        }

        var go = new GameObject("DigitalTwinMeshTopologyPanel");
        go.AddComponent<DigitalTwinMeshTopologyPanel>();
        Undo.RegisterCreatedObjectUndo(go, "Add MeshTopologyPanel");
        Selection.activeObject = go;
        EditorGUIUtility.PingObject(go);
        Debug.Log("[Simurgh] Mesh ag paneli eklendi.");
    }
}
#endif
