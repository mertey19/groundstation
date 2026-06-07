using System.Collections.Generic;
using UnityEngine;
using Mapbox.Unity.Map;
using Mapbox.Utils;

namespace GroundStation.Routes
{
    /// <summary>
    /// RouteManager'daki waypoint'ler i�in sahnede marker prefab'lar� �retir.
    /// LineRenderer �izgisine ek olarak mavi noktalar/pinler g�r�n�r.
    /// </summary>
    public class RouteMarkerVisualizer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RouteManager routeManager;
        [SerializeField] private GameObject waypointMarkerPrefab;
        [SerializeField] private AbstractMap abstractMap;

        [Header("Settings")]
        [SerializeField] private Vector3 markerOffset = new Vector3(0f, 8f, 0f);
        [Tooltip("Kirmizi waypoint marker'larin ekranda gorunen buyuklugu (1 = prefab boyutu).")]
        [SerializeField] private float markerScale = 4f;
        [Tooltip("Inspector eski degerleri ezse bile markerin en az bu kadar buyuk olmasini zorlar.")]
        [SerializeField] private float minimumForcedMarkerScale = 0f;
        [Tooltip("True ise marker boyutu sabitlenir (Inspector override etkilenmez).")]
        [SerializeField] private bool forceFixedMarkerScale = true;
        [SerializeField] private float fixedMarkerScale = 10f;
        [SerializeField] private Color markerColor = new Color(1f, 0.08f, 0.08f, 1f);
        [SerializeField] private bool autoRefreshOnEnable = true;
        [SerializeField] private bool adaptMarkersToTerrain = true;
        [Tooltip("True ise marker Y degeri sabit kalir, terrain'e gore yukari-asagi oynamaz.")]
        [SerializeField] private bool keepMarkerWorldYFixed = true;
        [SerializeField] private float periodicRefreshSeconds = 0.5f;
        [Header("Survey Mode")]
        [Tooltip("Survey rotada (metadata.label=survey) markerlari gizler; QGC benzeri temiz gorunum verir.")]
        [SerializeField] private bool hideMarkersForSurveyRoute = false;

        private readonly List<GameObject> _activeMarkers = new List<GameObject>();
        private float _nextRefreshTime;

        private void Awake()
        {
            ResolveBestRouteManager();
            if (abstractMap == null)
                abstractMap = FindObjectOfType<AbstractMap>();
        }

        private void OnEnable()
        {
            ResolveBestRouteManager();
            if (routeManager != null)
                routeManager.OnRouteChanged += OnRouteChanged;

            if (autoRefreshOnEnable)
                RefreshMarkers();
        }

        private void OnDisable()
        {
            if (routeManager != null)
                routeManager.OnRouteChanged -= OnRouteChanged;

            ClearMarkers();
        }

        private void OnRouteChanged(RouteData data)
        {
            RefreshMarkers();
        }

        private void Update()
        {
            if (periodicRefreshSeconds <= 0f) return;
            if (Time.unscaledTime < _nextRefreshTime) return;
            _nextRefreshTime = Time.unscaledTime + periodicRefreshSeconds;
            RefreshMarkers();
        }

        public void RefreshMarkers()
        {
            ResolveBestRouteManager();
            ClearMarkers();

            if (routeManager == null)
                return;

            var data = routeManager.GetRouteData();
            if (data == null || data.waypoints == null)
                return;

            if (hideMarkersForSurveyRoute && IsSurveyRoute(data))
                return;

            foreach (var wp in data.waypoints)
            {
                Vector3 pos = GetVisibleWorldPosition(wp, markerOffset);
                var marker = CreateMarkerAt(pos);
                marker.name = $"WaypointMarker_{wp.index}";
                float finalScale = forceFixedMarkerScale
                    ? fixedMarkerScale
                    : Mathf.Max(minimumForcedMarkerScale, markerScale);
                if (finalScale > 0.01f)
                    marker.transform.localScale = Vector3.one * finalScale;
                var renderer = marker.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    EnsureVisibleMarkerMaterial(renderer);
                    // Markerlar line'dan once degil, line'in ustunde render edilsin.
                    renderer.sortingOrder = 10;
                }
                _activeMarkers.Add(marker);
            }
        }

        private GameObject CreateMarkerAt(Vector3 pos)
        {
            GameObject marker;
            if (waypointMarkerPrefab != null)
            {
                marker = Instantiate(waypointMarkerPrefab, pos, Quaternion.identity, transform);
            }
            else
            {
                // Fallback: prefab atanmamis olsa bile waypoint gorunsun.
                marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.transform.SetParent(transform, false);
                marker.transform.position = pos;
                var col = marker.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }
            return marker;
        }

        private void EnsureVisibleMarkerMaterial(Renderer renderer)
        {
            if (renderer == null) return;
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.color = markerColor;
                renderer.material = mat;
            }
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
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

        private void ClearMarkers()
        {
            for (int i = 0; i < _activeMarkers.Count; i++)
            {
                if (_activeMarkers[i] != null)
                    Destroy(_activeMarkers[i]);
            }
            _activeMarkers.Clear();
        }

        private Vector3 GetVisibleWorldPosition(WaypointData waypoint, Vector3 offset)
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

            if (keepMarkerWorldYFixed)
                return worldPos + offset;

            if (adaptMarkersToTerrain && abstractMap != null)
            {
                try
                {
                    var geo = abstractMap.WorldToGeoPosition(worldPos);
                    var terrainPos = abstractMap.GeoToWorldPosition(geo, true);
                    return terrainPos + offset;
                }
                catch { }
            }
            return worldPos + offset;
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
    }
}