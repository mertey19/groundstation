#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using GroundStation.DigitalTwin;

/// <summary>
/// Tools > Simurgh > GPS-SLAM Yorunge Katmani Ekle
/// Sahneye DigitalTwinTrajectoryComparison ekler. Bagimliliklar (AbstractMap, JsonPoseBridge)
/// otomatik bulunur. Play'de GPS izi (mavi) + SLAM izi (turuncu) + sapma HUD gosterilir.
/// </summary>
public static class TrajectoryComparisonMenu
{
    [MenuItem("Tools/Simurgh/GPS-SLAM Yorunge Katmani Ekle")]
    public static void AddTrajectoryComparison()
    {
        var existing = Object.FindObjectOfType<DigitalTwinTrajectoryComparison>();
        if (existing != null)
        {
            Selection.activeObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            Debug.Log("[Simurgh] Yorunge katmani zaten sahnede.");
            return;
        }

        var go = new GameObject("DigitalTwinTrajectoryComparison");
        go.AddComponent<DigitalTwinTrajectoryComparison>();
        Undo.RegisterCreatedObjectUndo(go, "Add TrajectoryComparison");
        Selection.activeObject = go;
        EditorGUIUtility.PingObject(go);
        Debug.Log("[Simurgh] GPS-SLAM yorunge katmani eklendi. Play'e bas, JSON koprusunden pose+slamPose yolla.");
    }
}
#endif
