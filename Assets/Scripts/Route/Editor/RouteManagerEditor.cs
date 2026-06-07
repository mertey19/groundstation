using UnityEditor;
using UnityEngine;
using GroundStation.Routes;

namespace GroundStation.Routes.Editor
{
    [CustomEditor(typeof(RouteManager))]
    public class RouteManagerEditor : UnityEditor.Editor
    {
        private string _jsonToLoad = "";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var manager = (RouteManager)target;

            if (!Application.isPlaying) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Import Test", EditorStyles.boldLabel);
            _jsonToLoad = EditorGUILayout.TextArea(_jsonToLoad, GUILayout.Height(80));
            if (GUILayout.Button("Load Route from JSON"))
            {
                if (string.IsNullOrWhiteSpace(_jsonToLoad))
                    Debug.LogWarning("Paste JSON first.");
                else if (manager.LoadFromJson(_jsonToLoad))
                    Debug.Log("Route loaded from JSON.");
                else
                    Debug.LogError("Invalid JSON or load failed.");
            }
        }
    }
}
