using UnityEngine;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using GroundStation.Routes;
using GroundStation.Drone;
using System.Collections.Generic;

/// <summary>
/// Kamera WASD + scroll ile haritada gezinir. Map sinirlari disina cikmayi engeller (pan/zoom clamp).
/// </summary>
public class MapCameraController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 100f;
        [SerializeField] private float zoomSpeed = 2000f;
        [SerializeField] private float minHeight = 1.5f;
        [SerializeField] private float maxHeight = 1200f;

        [Header("Map bounds (world XZ)")]
        [Tooltip("Kamera bu dikdortgenin disina cikamaz. Mapbox harita merkezine gore ayarla.")]
        [SerializeField] private bool useBounds = true;
        [SerializeField] private float boundsMinX = -500f;
        [SerializeField] private float boundsMaxX = 500f;
        [SerializeField] private float boundsMinZ = -500f;
        [SerializeField] private float boundsMaxZ = 500f;
        [Header("UI Zoom Buttons")]
        [Tooltip("UI +/- butonlarinda her tiklamadaki yukseklik adimi.")]
        [SerializeField] private float zoomStep = 150f;
        [Header("Zoom Mode")]
        [Tooltip("True ise zoom sadece FOV ile yapilir (kamera konumu degismez).")]
        [SerializeField] private bool useFovZoom = true;
        [Tooltip("True ise zoom FOV yerine dogrudan Mapbox zoom level ile yapilir (harita buyur/kuculur).")]
        [SerializeField] private bool useMapZoomLevel = true;
        [SerializeField] private AbstractMap abstractMap;
        [SerializeField] private float mapZoomStep = 0.35f;
        [SerializeField] private float minMapZoom = 12f;
        [SerializeField] private float maxMapZoom = 22f;
        [Tooltip("True ise zoomda drone/waypoint world pozisyonlari oldugu gibi kalir (hic remap edilmez).")]
        [SerializeField] private bool keepActorsFixedOnMapZoom = false;
        [Tooltip("True ise zoomda aktorler daima geo tabanli restore edilir; kayma sorununu engeller.")]
        [SerializeField] private bool forceGeoRestoreOnMapZoom = true;
        [SerializeField] private float fovZoomSpeed = 45f;
        [SerializeField] private float fovStep = 6f;
        [SerializeField] private float minFov = 8f;
        [SerializeField] private float maxFov = 80f;
        [Tooltip("FOV zoom acikken kamera Y eksenini sabitler (yukari-asagi oynamayi engeller).")]
        [SerializeField] private bool lockYPositionInFovZoom = true;

        [Header("Harita gorunumu")]
        [Tooltip("Bazi projelerde Viewport Rect kuculuyor; harita sadece ustte ince serit gibi gorunur. Acik tut.")]
        [SerializeField] private bool enforceFullScreenViewport = true;

        private Camera _camera;
        private float _lockedY;

        private struct WaypointGeoCache
        {
            public WaypointData wp;
            public Vector2d geo;
        }

        private List<WaypointGeoCache> _pendingRouteGeo;
        private Vector2d _pendingDroneGeo;
        private float _pendingDroneY;
        private bool _hasPendingDroneGeo;
        private DroneWaypointFollower _pendingDroneFollower;
        private bool _restoreInProgress;
        private int _pendingMapRedrawEvents = 0;
        private float _queuedMapZoomDelta;
        private float _restoreStartedAt = -1f;
        [Header("Restore Stabilization")]
        [SerializeField] private int requiredMapRedrawEvents = 1;
        [SerializeField] private float restoreTimeoutSeconds = 1.25f;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _lockedY = transform.position.y;
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
        }

        private void OnEnable()
        {
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (abstractMap != null)
                abstractMap.OnMapRedrawn += OnMapRedrawn;
        }

        private void OnDisable()
        {
            if (abstractMap != null)
                abstractMap.OnMapRedrawn -= OnMapRedrawn;
        }

        private void LateUpdate()
        {
            if (_camera == null) return;

            if (useFovZoom && lockYPositionInFovZoom)
            {
                var p = transform.position;
                p.y = _lockedY;
                transform.position = p;
            }

            if (!enforceFullScreenViewport) return;

            var r = _camera.rect;
            bool notFull =
                Mathf.Abs(r.x) > 0.0001f ||
                Mathf.Abs(r.y) > 0.0001f ||
                Mathf.Abs(r.width - 1f) > 0.0001f ||
                Mathf.Abs(r.height - 1f) > 0.0001f;

            if (notFull)
            {
                _camera.rect = new Rect(0f, 0f, 1f, 1f);
                // QuadTreeTileProvider sadece transform.hasChanged ile extent guncelliyor; rect degisince de tetikle.
                transform.hasChanged = true;
            }
        }

        private void Update()
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector3 move = new Vector3(h, 0f, v).normalized * moveSpeed * Time.deltaTime;
            transform.Translate(move, Space.World);

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                ApplyZoomInput(scroll);
            }

            if (useBounds)
            {
                Vector3 pos = transform.position;
                pos.x = Mathf.Clamp(pos.x, boundsMinX, boundsMaxX);
                pos.z = Mathf.Clamp(pos.z, boundsMinZ, boundsMaxZ);
                if ((useFovZoom || useMapZoomLevel) && lockYPositionInFovZoom) pos.y = _lockedY;
                transform.position = pos;
            }

            if (_restoreInProgress && _restoreStartedAt > 0f && restoreTimeoutSeconds > 0f)
            {
                if (Time.unscaledTime - _restoreStartedAt >= restoreTimeoutSeconds)
                    CompleteRestoreNow();
            }
        }

        public void ZoomIn()
        {
            if (useMapZoomLevel)
            {
                AddMapZoomDelta(mapZoomStep);
                return;
            }
            if (useFovZoom)
            {
                AddFovDelta(-fovStep);
                return;
            }
            float dynamicStep = Mathf.Max(zoomStep, transform.position.y * 0.35f);
            AddZoomDelta(-dynamicStep);
        }

        public void ZoomOut()
        {
            if (useMapZoomLevel)
            {
                AddMapZoomDelta(-mapZoomStep);
                return;
            }
            if (useFovZoom)
            {
                AddFovDelta(fovStep);
                return;
            }
            float dynamicStep = Mathf.Max(zoomStep, transform.position.y * 0.35f);
            AddZoomDelta(dynamicStep);
        }

        private void ApplyZoomInput(float scroll)
        {
            if (useMapZoomLevel)
            {
                AddMapZoomDelta(scroll * mapZoomStep * 3f);
                return;
            }
            if (useFovZoom)
            {
                AddFovDelta(-scroll * fovZoomSpeed);
                return;
            }

            float scaleByHeight = Mathf.Max(1f, transform.position.y * 0.02f);
            AddZoomDelta(-scroll * zoomSpeed * scaleByHeight);
        }

        private void AddFovDelta(float delta)
        {
            if (_camera == null) return;
            _camera.fieldOfView = Mathf.Clamp(_camera.fieldOfView + delta, minFov, maxFov);
            // Camera bounds tile provider extent'inin yenilenmesi icin.
            transform.hasChanged = true;
        }

        private void AddMapZoomDelta(float deltaZoom)
        {
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (abstractMap == null) return;

            if (_restoreInProgress)
            {
                _queuedMapZoomDelta += deltaZoom;
                return;
            }

            float newZoom = Mathf.Clamp(abstractMap.Zoom + deltaZoom, minMapZoom, maxMapZoom);
            if (Mathf.Abs(newZoom - abstractMap.Zoom) < 0.0001f) return;

            if (keepActorsFixedOnMapZoom && !forceGeoRestoreOnMapZoom)
            {
                abstractMap.UpdateMap(newZoom);
                return;
            }

            // Zoom degismeden once geo'lari cachele; zoomdan sonra yeniden world'e projekte et.
            var routeGeo = CacheRouteGeo();
            var droneFollower = FindObjectOfType<DroneWaypointFollower>();
            Vector2d droneGeo = default;
            bool hasDroneGeo = false;
            float droneY = 0f;
            if (droneFollower != null)
            {
                try
                {
                    droneGeo = abstractMap.WorldToGeoPosition(droneFollower.transform.position);
                    droneY = droneFollower.transform.position.y;
                    hasDroneGeo = true;
                }
                catch { }
            }

            // Konum sabit, yalnizca map zoom level degisir -> harita buyur/kuculur.
            abstractMap.UpdateMap(newZoom);
            ScheduleRestoreAfterMapRedraw(routeGeo, droneFollower, hasDroneGeo, droneGeo, droneY);
        }

        private List<WaypointGeoCache> CacheRouteGeo()
        {
            var result = new List<WaypointGeoCache>();
            var routeManager = FindObjectOfType<RouteManager>();
            if (routeManager == null || abstractMap == null) return result;
            var data = routeManager.GetRouteData();
            if (data == null || data.waypoints == null) return result;

            for (int i = 0; i < data.waypoints.Count; i++)
            {
                var wp = data.waypoints[i];
                try
                {
                    var geo = wp.hasGeoPosition
                        ? new Vector2d(wp.latitude, wp.longitude)
                        : abstractMap.WorldToGeoPosition(wp.worldPosition);
                    result.Add(new WaypointGeoCache { wp = wp, geo = geo });
                }
                catch { }
            }
            return result;
        }

        private void RestoreRouteFromGeo(List<WaypointGeoCache> cached)
        {
            if (cached == null || cached.Count == 0 || abstractMap == null) return;
            var routeManager = FindObjectOfType<RouteManager>();
            if (routeManager == null) return;

            var newWaypoints = new List<WaypointData>(cached.Count);
            for (int i = 0; i < cached.Count; i++)
            {
                var c = cached[i];
                Vector3 world = c.wp.worldPosition;
                try
                {
                    var reproj = abstractMap.GeoToWorldPosition(c.geo, true);
                    // Y kaymasini onlemek icin waypoint'in mevcut world Y degerini koru.
                    reproj.y = c.wp.worldPosition.y;
                    world = reproj;
                }
                catch { }

                var md = c.wp.metadata != null ? c.wp.metadata.Clone() : null;
                newWaypoints.Add(new WaypointData(
                    i,
                    world,
                    c.geo.x,
                    c.geo.y,
                    c.wp.targetAltitude,
                    c.wp.gridPosition,
                    md));
            }

            routeManager.ReplaceRoute(newWaypoints);
        }

        private void ScheduleRestoreAfterMapRedraw(
            List<WaypointGeoCache> routeGeo,
            DroneWaypointFollower droneFollower,
            bool hasDroneGeo,
            Vector2d droneGeo,
            float droneY)
        {
            _pendingRouteGeo = routeGeo;
            _pendingDroneFollower = droneFollower;
            _hasPendingDroneGeo = hasDroneGeo;
            _pendingDroneGeo = droneGeo;
            _pendingDroneY = droneY;
            _pendingMapRedrawEvents = 0;
            _restoreStartedAt = Time.unscaledTime;

            if (_pendingDroneFollower != null)
                _pendingDroneFollower.ExternalPause = true;
            _restoreInProgress = true;
        }

        private void OnMapRedrawn()
        {
            if (!_restoreInProgress) return;
            _pendingMapRedrawEvents++;
            if (_pendingMapRedrawEvents < Mathf.Max(1, requiredMapRedrawEvents)) return;
            CompleteRestoreNow();
        }

        private void CompleteRestoreNow()
        {
            RestoreRouteFromGeo(_pendingRouteGeo);

            if (_hasPendingDroneGeo && _pendingDroneFollower != null && abstractMap != null)
            {
                try
                {
                    var newWorld = abstractMap.GeoToWorldPosition(_pendingDroneGeo, true);
                    newWorld.y = _pendingDroneY;
                    _pendingDroneFollower.transform.position = newWorld;
                }
                catch { }
            }

            if (_pendingDroneFollower != null)
                _pendingDroneFollower.ExternalPause = false;

            _pendingRouteGeo = null;
            _pendingDroneFollower = null;
            _hasPendingDroneGeo = false;
            _pendingMapRedrawEvents = 0;
            _restoreInProgress = false;
            _restoreStartedAt = -1f;

            if (Mathf.Abs(_queuedMapZoomDelta) > 0.0001f)
            {
                float queued = _queuedMapZoomDelta;
                _queuedMapZoomDelta = 0f;
                AddMapZoomDelta(queued);
            }
        }

        private void AddZoomDelta(float deltaY)
        {
            Vector3 pos = transform.position;
            pos.y = Mathf.Clamp(pos.y + deltaY, minHeight, maxHeight);
            transform.position = pos;
        }

        /// <summary>
        /// Kamerayi verilen dunya noktasinin ustune kusbakisi yerlestirir ve
        /// _lockedY + bounds degerlerini de gunceller. Aksi halde Update/LateUpdate
        /// kamerayi eski konuma/yukseklige geri ceker (lockY + bounds clamp).
        /// SimurghSiteImporter saha merkezine odaklamak icin cagirir.
        /// </summary>
        public void FocusOnWorld(Vector3 center, float height)
        {
            height = Mathf.Clamp(height, minHeight, maxHeight);
            transform.position = new Vector3(center.x, height, center.z);
            transform.rotation = Quaternion.Euler(80f, 0f, 0f);
            _lockedY = height;
            float half = Mathf.Max(300f, height * 2f);
            boundsMinX = center.x - half; boundsMaxX = center.x + half;
            boundsMinZ = center.z - half; boundsMaxZ = center.z + half;
            transform.hasChanged = true;
        }

        public bool UseBounds => useBounds;
        public float BoundsMinX => boundsMinX;
        public float BoundsMaxX => boundsMaxX;
        public float BoundsMinZ => boundsMinZ;
        public float BoundsMaxZ => boundsMaxZ;

        public Vector3 ClampToBounds(Vector3 worldPos)
        {
            if (!useBounds) return worldPos;
            worldPos.x = Mathf.Clamp(worldPos.x, boundsMinX, boundsMaxX);
            worldPos.z = Mathf.Clamp(worldPos.z, boundsMinZ, boundsMaxZ);
            return worldPos;
        }

        public bool TryGetBounds(out float minX, out float maxX, out float minZ, out float maxZ)
        {
            minX = boundsMinX; maxX = boundsMaxX; minZ = boundsMinZ; maxZ = boundsMaxZ;
            return useBounds;
        }
}
