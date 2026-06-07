using UnityEditor;
using UnityEngine;
using GroundStation.Routes;

namespace GroundStation.Routes.Editor
{
    [CustomEditor(typeof(RouteExporter))]
    public class RouteExporterEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var exporter = (RouteExporter)target;
            if (!Application.isPlaying) return;

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Export to JSON (Console)"))
            {
                string json = exporter.ExportToJson();
                Debug.Log(json);
            }
            if (GUILayout.Button("Copy JSON to Clipboard"))
            {
                exporter.CopyJsonToClipboard();
            }
            if (GUILayout.Button("Export to File (persistentDataPath)"))
            {
                string path = exporter.ExportToFile(null, true);
                if (!string.IsNullOrEmpty(path))
                    Debug.Log($"Exported to: {path}");
            }
        }
    }
}
