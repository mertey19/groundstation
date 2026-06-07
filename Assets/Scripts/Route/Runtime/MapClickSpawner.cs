using UnityEngine;
using UnityEngine.EventSystems;
using GroundStation.Routes;
using GroundStation.UI;
using Mapbox.Unity.Map;
using Mapbox.Utils;

namespace GroundStation.Inputs
{
    /// <summary>
    /// Haritaya sol tik ile tek waypoint ekler. Yukseklik AltitudeInputProvider veya default ile alinir.
    /// Cift tetikleme ve UI tiklamasini engeller. Marker RouteMarkerVisualizer'da; bu script sadece AddWaypoint cagirir.
    /// </summary>
    public class MapClickSpawner : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera mapCamera;
        [SerializeField] private RouteManager routeManager;
        [SerializeField] private AbstractMap abstractMap;
        [SerializeField] private MapCameraController mapCameraController;
        [Tooltip("Bos birakilirsa her waypoint icin default waypoint altitude (10) kullanilir.")]
        [SerializeField] private AltitudeInputProvider altitudeInputProvider;

        [Header("Marker (genelde kapali)")]
        [Tooltip("true = bu script prefab instantiate eder (RouteMarkerVisualizer kapatilsin). false = sadece waypoint ekler, marker'i RouteMarkerVisualizer yapar.")]
        [SerializeField] private bool spawnMarkerDirectly = false;
        [SerializeField] private bool forcePrimitiveDebugMarker = false;
        [SerializeField] private GameObject markerPrefab;
        [SerializeField] private Transform markerParent;
        [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 2f, 0f);
        [Tooltip("Waypoint konumunu geo->world ile normalize eder; zoom/pan sonrasi kaymayi azaltir.")]
        [SerializeField] private bool normalizeWaypointOnCurrentMap = true;

        [Header("Raycast")]
        [SerializeField] private LayerMask mapLayerMask = ~0;
        [Tooltip("AbstractMap atanmissa tik konumu her zaman harita duzleminden alinir (map Root). Boylece mapteki yerler dogru gelir.")]
        [SerializeField] private bool useMapPlaneWhenAvailable = true;
        [SerializeField] private bool useFallbackPlane = true;
        [SerializeField] private float fallbackPlaneHeight = 0f;

        [Header("Waypoint altitude")]
        [Tooltip("AltitudeInputProvider yoksa bu deger kullanilir (m).")]
        [SerializeField] private float defaultWaypointAltitude = 10f;

        [Header("Duplicate spawn ongleme")]
        [Tooltip("Bir tiklamadan sonra bu sure boyunca yeni tik kabul edilmez.")]
        [SerializeField] private float clickCooldownSeconds = 0.25f;

        [Header("Haritadan waypoint ekleme")]
        [Tooltip("true: Waypoint menüsünden 'Haritadan ekle' açılmadan sol tık rota eklemez. false: eski davranış (her tıklama ekler).")]
        [SerializeField] private bool requirePlacementModeFromMenu = true;

        [Header("Debug")]
        [SerializeField] private bool logRaycastHits = false;
        [SerializeField] private bool clampWaypointToMapBounds = true;

        private float _lastSpawnTime = -999f;

        private void Awake()
        {
            if (mapCamera == null) mapCamera = Camera.main;
            ResolveBestRouteManager();
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (mapCameraController == null) mapCameraController = FindObjectOfType<MapCameraController>();
            if (markerParent == null) markerParent = transform;
            if (altitudeInputProvider == null) altitudeInputProvider = FindObjectOfType<AltitudeInputProvider>();
        }

        private void Update()
        {
            if (!Input.GetMouseButtonDown(0)) return;

            if (requirePlacementModeFromMenu && !WaypointMapEditState.IsMapClickPlacementEnabled)
            {
                if (logRaycastHits) Debug.Log("[MapClickSpawner] Placement mode kapalı, yok sayılıyor.");
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            if (Time.unscaledTime - _lastSpawnTime < clickCooldownSeconds)
            {
                if (logRaycastHits) Debug.Log("[MapClickSpawner] Cooldown, ignoring.");
                return;
            }

            if (mapCamera == null)
            {
                if (logRaycastHits) Debug.LogWarning("[MapClickSpawner] No mapCamera.");
                return;
            }

            Ray ray = mapCamera.ScreenPointToRay(Input.mousePosition);
            Vector3 worldPos;

            if (useMapPlaneWhenAvailable && abstractMap != null && abstractMap.Root != null)
            {
                Vector3 mapOrigin = abstractMap.Root.position;
                Plane mapPlane = new Plane(Vector3.up, mapOrigin);
                if (mapPlane.Raycast(ray, out float enter))
                {
                    worldPos = ray.GetPoint(enter);
                    if (logRaycastHits) Debug.Log($"[MapClickSpawner] Map plane at {worldPos}");
                }
                else
                {
                    if (logRaycastHits) Debug.Log("[MapClickSpawner] Map plane kesmedi, atlanıyor.");
                    return;
                }
            }
            else
            {
                bool hit = Physics.Raycast(ray, out RaycastHit hitInfo, 10000f, mapLayerMask);
                if (hit)
                {
                    worldPos = hitInfo.point;
                    if (logRaycastHits)
                        Debug.Log($"[MapClickSpawner] Hit '{hitInfo.collider.name}' at {worldPos}");
                }
                else
                {
                    if (!useFallbackPlane)
                    {
                        if (logRaycastHits) Debug.Log("[MapClickSpawner] Raycast did not hit anything.");
                        return;
                    }
                    Vector3 fallbackOrigin = abstractMap != null && abstractMap.Root != null
                        ? abstractMap.Root.position
                        : new Vector3(0, fallbackPlaneHeight, 0);
                    Plane ground = new Plane(Vector3.up, fallbackOrigin);
                    if (!ground.Raycast(ray, out float enter))
                    {
                        if (logRaycastHits) Debug.Log("[MapClickSpawner] Fallback plane kesmedi.");
                        return;
                    }
                    worldPos = ray.GetPoint(enter);
                    if (logRaycastHits) Debug.Log($"[MapClickSpawner] Fallback at {worldPos}");
                }
            }

            if (routeManager != null)
            {
                if (clampWaypointToMapBounds && mapCameraController != null && mapCameraController.UseBounds)
                    worldPos = mapCameraController.ClampToBounds(worldPos);

                Vector2d? waypointGeo = null;
                if (normalizeWaypointOnCurrentMap && abstractMap != null)
                {
                    var geoPos = abstractMap.WorldToGeoPosition(worldPos);
                    worldPos = abstractMap.GeoToWorldPosition(geoPos, true);
                    if (abstractMap.Root != null)
                        worldPos.y = abstractMap.Root.position.y;
                    waypointGeo = geoPos;
                }
                else if (abstractMap != null)
                {
                    waypointGeo = abstractMap.WorldToGeoPosition(worldPos);
                }

                float altitude = defaultWaypointAltitude;
                if (altitudeInputProvider != null)
                    altitude = altitudeInputProvider.GetAltitude();
                if (waypointGeo.HasValue)
                    routeManager.AddWaypoint(worldPos, altitude, null, null, waypointGeo.Value.x, waypointGeo.Value.y);
                else
                    routeManager.AddWaypoint(worldPos, altitude);
                if (logRaycastHits)
                {
                    var count = routeManager.GetRouteData() != null ? routeManager.GetRouteData().Count : -1;
                    Debug.Log($"[MapClickSpawner] Waypoint added. RouteManager={routeManager.name}, count={count}");
                }
                _lastSpawnTime = Time.unscaledTime;
            }

            if (spawnMarkerDirectly && markerPrefab != null && !forcePrimitiveDebugMarker)
            {
                Vector3 spawnPos = worldPos + spawnOffset;
                var marker = Instantiate(markerPrefab, spawnPos, Quaternion.identity, null);
                marker.name = "WaypointMarker_" + Time.frameCount;
            }
            else if (spawnMarkerDirectly)
            {
                Vector3 spawnPos = worldPos + spawnOffset;
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.transform.SetParent(null, true);
                marker.transform.position = spawnPos;
                marker.transform.localScale = Vector3.one * 18f;
                marker.name = "WaypointDebugMarker_" + Time.frameCount;

                var col = marker.GetComponent<Collider>();
                if (col != null) Destroy(col);

                var r = marker.GetComponent<Renderer>();
                if (r != null)
                {
                    var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
                    if (shader != null)
                    {
                        var mat = new Material(shader);
                        mat.color = new Color(1f, 0f, 0f, 1f);
                        r.material = mat;
                    }
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
                if (logRaycastHits)
                    Debug.Log($"[MapClickSpawner] Debug marker spawned at {spawnPos}");
            }

            if (abstractMap != null && logRaycastHits)
            {
                var geo = abstractMap.WorldToGeoPosition(worldPos);
                Debug.Log($"[MapClickSpawner] Geo: lat {geo.x}, lon {geo.y}");
            }
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
            routeManager = best != null ? best : managers[0];
        }
    }
}
