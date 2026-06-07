using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// Digital Twin viewport icinde waypoint noktalari ve aralarinda cizgi gosterir.
    /// WaypointDotPrefab ve LineSegmentPrefab ile runtime'da olusturur veya mevcut child'lari kullanir.
    /// </summary>
    public class DigitalTwinRouteView : MonoBehaviour
    {
        [Header("Prefabs (opsiyonel - bos birakilirsa basit Image olusturulur)")]
        [SerializeField] private GameObject waypointDotPrefab;
        [SerializeField] private GameObject lineSegmentPrefab;

        [Header("Gorsel")]
        [SerializeField] private Color waypointColor = new Color(1f, 0.3f, 0.3f, 0.9f);
        [SerializeField] private Color lineColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);
        [Tooltip("Waypoint noktalarinin capi (piksel).")]
        [SerializeField] private float dotSize = 20f;
        [Tooltip("Rota cizgisinin kalinligi (piksel).")]
        [SerializeField] private float lineThickness = 6f;

        [Header("Containers (bos birakilirsa this transform)")]
        [SerializeField] private Transform dotsContainer;
        [SerializeField] private Transform linesContainer;

        private readonly List<RectTransform> _dots = new List<RectTransform>();
        private readonly List<RectTransform> _lines = new List<RectTransform>();
        private RectTransform _rect;

        private void Awake()
        {
            _rect = transform as RectTransform;
            if (_rect == null) _rect = GetComponent<RectTransform>();
            if (dotsContainer == null) dotsContainer = transform;
            if (linesContainer == null) linesContainer = transform;
        }

        /// <summary>
        /// Viewport lokalde waypoint pozisyonlari (merkez 0,0). null veya bos liste tum noktalari temizler.
        /// </summary>
        public void SetWaypoints(Vector2[] viewPositions)
        {
            int need = viewPositions != null ? viewPositions.Length : 0;

            while (_dots.Count < need)
            {
                var rt = CreateDot();
                if (rt != null) _dots.Add(rt);
                else break;
            }

            for (int i = 0; i < _dots.Count; i++)
            {
                bool active = i < need;
                _dots[i].gameObject.SetActive(active);
                if (active && viewPositions != null)
                {
                    _dots[i].anchoredPosition = viewPositions[i];
                }
            }

            // Cizgi segmentleri: N-1 tane
            int lineCount = need > 1 ? need - 1 : 0;
            while (_lines.Count < lineCount)
            {
                var rt = CreateLineSegment();
                if (rt != null) _lines.Add(rt);
                else break;
            }

            for (int i = 0; i < _lines.Count; i++)
            {
                bool active = i < lineCount;
                _lines[i].gameObject.SetActive(active);
                if (active && viewPositions != null && i + 1 < viewPositions.Length)
                {
                    Vector2 a = viewPositions[i];
                    Vector2 b = viewPositions[i + 1];
                    Vector2 mid = (a + b) * 0.5f;
                    float len = Vector2.Distance(a, b);
                    float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
                    _lines[i].anchoredPosition = mid;
                    _lines[i].sizeDelta = new Vector2(len, lineThickness);
                    _lines[i].localEulerAngles = new Vector3(0f, 0f, -angle);
                }
            }
        }

        private RectTransform CreateDot()
        {
            GameObject go;
            if (waypointDotPrefab != null)
            {
                go = Instantiate(waypointDotPrefab, dotsContainer);
            }
            else
            {
                go = new GameObject("TwinWP");
                go.transform.SetParent(dotsContainer, false);
                var img = go.AddComponent<Image>();
                img.color = waypointColor;
            }

            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(dotSize, dotSize);
            rt.anchoredPosition = Vector2.zero;
            var img2 = go.GetComponent<Image>();
            if (img2 != null) img2.color = waypointColor;
            return rt;
        }

        private RectTransform CreateLineSegment()
        {
            GameObject go;
            if (lineSegmentPrefab != null)
            {
                go = Instantiate(lineSegmentPrefab, linesContainer);
            }
            else
            {
                go = new GameObject("TwinLine");
                go.transform.SetParent(linesContainer, false);
                var img = go.AddComponent<Image>();
                img.color = lineColor;
            }

            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(10f, lineThickness);
            rt.anchoredPosition = Vector2.zero;
            var img2 = go.GetComponent<Image>();
            if (img2 != null) img2.color = lineColor;
            return rt;
        }
    }
}
