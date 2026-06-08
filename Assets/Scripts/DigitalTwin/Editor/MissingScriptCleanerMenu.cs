#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

/// <summary>
/// Tools > Simurgh > Missing Script'leri Temizle
/// Sahnedeki tum "missing script" (silinmis component referansi) kalintilarini kaldirir.
/// LiveLocationTracker gibi sonradan silinen script'lerin biraktigi sari uyarilari giderir.
/// Edit modunda calistir (Play'de degil), sonra Ctrl+S.
/// </summary>
public static class MissingScriptCleanerMenu
{
    [MenuItem("Tools/Simurgh/Missing Script'leri Temizle")]
    public static void Clean()
    {
        int removed = 0;
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                int c = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                if (c > 0)
                {
                    removed += c;
                    EditorUtility.SetDirty(t.gameObject);
                }
            }
        }

        if (removed > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Simurgh] {removed} adet missing script temizlendi. Sahneyi kaydet (Ctrl+S).");
        }
        else
        {
            Debug.Log("[Simurgh] Missing script bulunamadi — sahne temiz.");
        }
    }
}
#endif
