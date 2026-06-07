using UnityEditor;
using UnityEngine;

namespace GroundStation.DigitalTwin.EditorTools
{
    /// <summary>
    /// digital-twin saha importer'ini tek tikla sahneye ekler.
    /// Menu: Tools > Simurgh > Add Site Importer to Scene
    /// </summary>
    public static class SimurghSiteMenu
    {
        [MenuItem("Tools/Simurgh/Add Site Importer to Scene")]
        public static void AddSiteImporter()
        {
            var existing = Object.FindObjectOfType<SimurghSiteImporter>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);
                Debug.Log("[Simurgh] SimurghSiteImporter zaten sahnede. Play'e bas (Map otomatik bulunur).");
                return;
            }

            var go = new GameObject("SimurghSite");
            go.AddComponent<SimurghSiteImporter>();
            Undo.RegisterCreatedObjectUndo(go, "Add SimurghSite");
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            Debug.Log("[Simurgh] SimurghSite eklendi. Map otomatik bulunacak. Play'e bas; harita aukerman'a gidip bina+agac olusacak.");
        }
    }
}
