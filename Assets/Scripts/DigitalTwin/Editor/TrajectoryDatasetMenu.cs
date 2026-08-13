#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using GroundStation.DigitalTwin;

/// <summary>
/// Tools > Simurgh > ORB-SLAM3 Veri Kumesi Oynaticisi Ekle
/// Sahneye hem yorunge karsilastirma katmanini hem de veri kumesi oynaticisini ekler.
/// Play'e basildiginda StreamingAssets/SlamDataset altindaki gercek konum (ground truth)
/// ve SLAM kestirimi dosyalari oynatilir; iki iz haritada farkli renklerde cizilir.
/// </summary>
public static class TrajectoryDatasetMenu
{
    [MenuItem("Tools/Simurgh/ORB-SLAM3 Veri Kumesi Oynaticisi Ekle")]
    public static void AddDatasetPlayer()
    {
        if (Object.FindObjectOfType<DigitalTwinTrajectoryComparison>() == null)
        {
            var traj = new GameObject("DigitalTwinTrajectoryComparison");
            traj.AddComponent<DigitalTwinTrajectoryComparison>();
            Undo.RegisterCreatedObjectUndo(traj, "Add TrajectoryComparison");
            Debug.Log("[Simurgh] Yorunge karsilastirma katmani da eklendi (izleri bu ciziyor).");
        }

        var existing = Object.FindObjectOfType<DigitalTwinTrajectoryDatasetPlayer>();
        if (existing != null)
        {
            Selection.activeObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            Debug.Log("[Simurgh] Veri kumesi oynaticisi zaten sahnede.");
            return;
        }

        var go = new GameObject("DigitalTwinTrajectoryDatasetPlayer");
        go.AddComponent<DigitalTwinTrajectoryDatasetPlayer>();
        Undo.RegisterCreatedObjectUndo(go, "Add TrajectoryDatasetPlayer");
        Selection.activeObject = go;
        EditorGUIUtility.PingObject(go);
        Debug.Log("[Simurgh] Veri kumesi oynaticisi eklendi. Play'e bas: gercek konum mavi, SLAM turuncu cizilir.");
    }
}
#endif
