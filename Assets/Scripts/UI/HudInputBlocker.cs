using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GroundStation.UI
{
    // IMGUI does not participate in the Canvas raycaster. Keep the last painted
    // bounds so Update can reject map input before the next OnGUI event runs.
    public static class HudInputBlocker
    {
        private static readonly List<Rect> Regions = new List<Rect>();
        private static readonly List<RaycastResult> Hits = new List<RaycastResult>();
        private static int _paintFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Regions.Clear();
            Hits.Clear();
            _paintFrame = -1;
        }

        public static void Register(Rect rect)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (_paintFrame != Time.frameCount)
            {
                Regions.Clear();
                _paintFrame = Time.frameCount;
            }
            Vector3 min = GUI.matrix.MultiplyPoint3x4(new Vector3(rect.xMin, rect.yMin));
            Vector3 max = GUI.matrix.MultiplyPoint3x4(new Vector3(rect.xMax, rect.yMax));
            Regions.Add(Rect.MinMaxRect(min.x, min.y, max.x, max.y));
        }

        public static bool ContainsScreenPoint(Vector2 point)
        {
            if (Time.frameCount - _paintFrame > 1) return false;
            point.y = Screen.height - point.y;
            foreach (Rect rect in Regions)
                if (rect.Contains(point)) return true;
            return false;
        }

        public static bool IsPointerOverUI()
        {
            if (ContainsScreenPoint(Input.mousePosition)) return true;
            var events = EventSystem.current;
            if (events == null) return false;
            Hits.Clear();
            events.RaycastAll(new PointerEventData(events) { position = Input.mousePosition }, Hits);
            foreach (var hit in Hits)
                if (hit.module is GraphicRaycaster) return true;
            return false;
        }

        public static bool IsEditingText
        {
            get
            {
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                var field = selected != null ? selected.GetComponent<InputField>() : null;
                return (field != null && field.isFocused) || GUIUtility.keyboardControl != 0;
            }
        }
    }
}
