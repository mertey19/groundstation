using UnityEditor;
using UnityEngine;
using GroundStation.Routes;

namespace GroundStation.Routes.Editor
{
    [CustomEditor(typeof(PathfindingToRouteBridge))]
    public class PathfindingToRouteBridgeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var bridge = (PathfindingToRouteBridge)target;
            EditorGUILayout.Space(4);
            if (GUILayout.Button("Apply Test Path to Route"))
            {
                if (Application.isPlaying)
                    bridge.ApplyTestPath();
                else
                    Debug.LogWarning("Enter Play mode to apply test path.");
            }
        }
    }
}
