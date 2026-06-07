using UnityEngine;
using Mapbox.Unity.Map;
using Mapbox.Utils;

namespace GroundStation.Routes
{
    /// <summary>
    /// RouteData'dan okur ve LineRenderer ile sahnede rota çizer.
    /// Veri ile görsel tek kaynak (RouteManager) üzerinden senkron kalır.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class RouteVisualizer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RouteManager routeManager;
        [SerializeField] private LineRenderer lineRenderer;
        [SerializeField] private LineRenderer glowLineRenderer;
        [SerializeField] private AbstractMap abstractMap;

        [Header("Visual Settings")]
        [Tooltip("Rota cizgisinin kalinligi (harita uzerinde).")]
        [SerializeField] private float lineWidth = 8f;
        [Tooltip("Rota cizgisinin rengi (yesil).")]
        [SerializeField] private Color lineColor = new Color(0.08f, 1f, 0.15f, 1f);
        [Tooltip("Cizgiyi zemin ustunde daha gorunur yapmak icin yukseklik ofseti.")]
        [SerializeField] private float lineHeightOffset = 1.5f;
        [SerializeField] private bool adaptLineToTerrain = true;
        [Tooltip("True ise line Y degeri sabit kalir, terrain/zoom nedeniyle yukari-asagi oynamaz.")]
        [SerializeField] private bool keepLineWorldYFixed = true;
        [Header("Survey Visual (QGC-like)")]
        [SerializeField] private bool useQgcLikeStyleForSurvey = true;
        [SerializeField] private float surveyLineWidth = 0.85f;
        [SerializeField] private Color surveyLineColor = new Color(1f, 1f, 1f, 0.76f);
        [SerializeField] private bool disableGlowForSurvey = true;
        [Header("Glow")]
        [SerializeField] private bool enableGlow = true;
        [SerializeField] private float glowWidthMultiplier = 2.2f;
        [SerializeField] private Color glowColor = new Color(0.3f, 1f, 0.4f, 0.35f);
        [SerializeField] private bool useWorldSpace = true;
        [SerializeField] [Tooltip("Waypoint'leri küçük sphere veya marker ile göstermek için (opsiyonel)")]
        private bool showWaypointGizmos = true;
        [SerializeField] private float periodicRefreshSeconds = 0.5f;

        private float _nextRefreshTime;

        private void Awake()
        {
            if (lineRenderer == null)
                lineRenderer = GetComponent<LineRenderer>();
            EnsureGlowRenderer();
            ResolveBestRouteManager();
            if (abstractMap == null)
                abstractMap = FindObjectOfType<AbstractMap>();

            ConfigureLineRenderer();
        }

        private void OnEnable()
        {
            ResolveBestRouteManager();
            if (routeManager != null)
                routeManager.OnRouteChanged += OnRouteChanged;
            RefreshVisual();
        }

        private void OnDisable()
        {
            if (routeManager != null)
                routeManager.OnRouteChanged -= OnRouteChanged;
        }

        private void Update()
        {
            if (periodicRefreshSeconds <= 0f) return;
            if (Time.unscaledTime < _nextRefreshTime) return;
            _nextRefreshTime = Time.unscaledTime + periodicRefreshSeconds;
            RefreshVisual();
        }

        private void ConfigureLineRenderer()
        {
            if (lineRenderer == null) return;
            ConfigureRenderer(lineRenderer, lineWidth, lineColor);

            if (glowLineRenderer != null)
                ConfigureRenderer(glowLineRenderer, lineWidth * glowWidthMultiplier, glowColor);
        }

        private void ConfigureRenderer(LineRenderer renderer, float width, Color color)
        {
            renderer.useWorldSpace = useWorldSpace;
            renderer.positionCount = 0;
            renderer.startWidth = width;
            renderer.endWidth = width;
            renderer.material = renderer.material != null ? renderer.material : CreateDefaultMaterial();
            renderer.startColor = color;
            renderer.endColor = color;
            renderer.loop = false;
            renderer.numCornerVertices = 8;
            renderer.numCapVertices = 8;
            // Waypoint markerlar cizginin ustunde gorunsun.
            renderer.sortingOrder = -10;
        }

        private Material CreateDefaultMaterial()
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            return new Material(shader);
        }

        private void EnsureGlowRenderer()
        {
            if (!enableGlow)
            {
                if (glowLineRenderer != null)
                    glowLineRenderer.enabled = false;
                return;
            }

            if (glowLineRenderer == null)
            {
                var go = new GameObject("RouteGlow");
                go.transform.SetParent(transform, false);
                glowLineRenderer = go.AddComponent<LineRenderer>();
            }
            glowLineRenderer.enabled = true;
        }

        private void OnRouteChanged(RouteData data)
        {
            RefreshVisual();
        }

        /// <summary>
        /// RouteManager'daki güncel rota verisine göre çizgiyi günceller.
        /// </summary>
        public void RefreshVisual()
        {
            if (lineRenderer == null) return;
            ResolveBestRouteManager();

            var data = routeManager != null ? routeManager.GetRouteData() : null;
            if (data == null || data.Count == 0)
            {
                lineRenderer.positionCount = 0;
                if (glowLineRenderer != null) glowLineRenderer.positionCount = 0;
                return;
            }

            ApplyRouteStyleByType(data);

            lineRenderer.positionCount = data.waypoints.Count;
            if (glowLineRenderer != null)
                glowLineRenderer.positionCount = data.waypoints.Count;
            for (int i = 0; i < data.waypoints.Count; i++)
            {
                var p = GetVisibleWorldPosition(data.waypoints[i], lineHeightOffset);
                lineRenderer.SetPosition(i, p);
                if (glowLineRenderer != null)
                    glowLineRenderer.SetPosition(i, p);
            }
        }

        private void ApplyRouteStyleByType(RouteData data)
        {
            bool isSurvey = IsSurveyRoute(data);
            if (isSurvey && useQgcLikeStyleForSurvey)
            {
                ConfigureRenderer(lineRenderer, surveyLineWidth, surveyLineColor);
                if (glowLineRenderer != null)
                {
                    if (disableGlowForSurvey)
                    {
                        glowLineRenderer.enabled = false;
                        glowLineRenderer.positionCount = 0;
                    }
                    else
                    {
                        glowLineRenderer.enabled = true;
                        ConfigureRenderer(glowLineRenderer, surveyLineWidth * glowWidthMultiplier, glowColor);
                    }
                }
                return;
            }

            ConfigureRenderer(lineRenderer, lineWidth, lineColor);
            if (glowLineRenderer != null)
            {
                glowLineRenderer.enabled = enableGlow;
                if (enableGlow)
                    ConfigureRenderer(glowLineRenderer, lineWidth * glowWidthMultiplier, glowColor);
                else
                    glowLineRenderer.positionCount = 0;
            }
        }

        private static bool IsSurveyRoute(RouteData data)
        {
            if (data == null || data.waypoints == null || data.waypoints.Count == 0) return false;
            if (data.waypoints.Count < 3) return false;
            for (int i = 0; i < data.waypoints.Count; i++)
            {
                var md = data.waypoints[i].metadata;
                bool isSurvey = md != null && !string.IsNullOrEmpty(md.label) &&
                                md.label.Equals("survey", System.StringComparison.OrdinalIgnoreCase);
                if (!isSurvey) return false;
            }
            return true;
        }

        private Vector3 GetVisibleWorldPosition(WaypointData waypoint, float yOffset)
        {
            Vector3 worldPos = waypoint != null ? waypoint.worldPosition : Vector3.zero;
            if (waypoint != null && waypoint.hasGeoPosition && abstractMap != null)
            {
                try
                {
                    var geo = new Vector2d(waypoint.latitude, waypoint.longitude);
                    worldPos = abstractMap.GeoToWorldPosition(geo, true);
                }
                catch { }
            }

            if (keepLineWorldYFixed)
                return worldPos + Vector3.up * yOffset;

            if (adaptLineToTerrain && abstractMap != null)
            {
                try
                {
                    var geo = abstractMap.WorldToGeoPosition(worldPos);
                    var terrainPos = abstractMap.GeoToWorldPosition(geo, true);
                    terrainPos.y += yOffset;
                    return terrainPos;
                }
                catch { }
            }
            return worldPos + Vector3.up * yOffset;
        }

        private void ResolveBestRouteManager()
        {
            var managers = FindObjectsOfType<RouteManager>();
            if (managers == null || managers.Length == 0) return;

            RouteManager best = null;
            int bestCount = -1;
            for (int i = 0; i < managers.Length; i++)
            {
                var rm = managers[i];
                if (rm == null) continue;
                int c = rm.GetRouteData() != null ? rm.GetRouteData().Count : 0;
                if (c > bestCount)
                {
                    bestCount = c;
                    best = rm;
                }
            }

            if (best != null && !ReferenceEquals(best, routeManager))
                routeManager = best;
            else if (routeManager == null)
                routeManager = managers[0];
        }

        private void OnDrawGizmos()
        {
            if (!showWaypointGizmos || !Application.isPlaying) return;
            var data = routeManager != null ? routeManager.GetRouteData() : null;
            if (data == null || data.waypoints == null) return;

            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
            foreach (var wp in data.waypoints)
            {
                Gizmos.DrawWireSphere(wp.worldPosition, 0.3f);
            }
        }
    }
}
