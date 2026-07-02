using System;
using UnityEngine;
using Mapbox.Unity.Map;
using Mapbox.Utils;
using GroundStation.Drone;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// Operasyon sahasi (geofence) — PDF 3.3 fail-safe tetikleyicilerinden
    /// "operasyon sahasi disina cikma" icin tanim + izleme + gorsel sinir.
    /// Dairesel saha: merkez (ilk gecerli poz / harita merkezi / elle) + yaricap (m).
    /// Ihlalde MissionEngine olay gunlugune "geofence_breach" duser, RemoteState uyarisi
    /// basilir ve FlightSafetyPanel KIRMIZI "SAHA DIŞI" gosterir. Sinir haritada
    /// LineRenderer halkasi olarak cizilir (icerde yesilimsi, ihlalde kirmizi).
    /// </summary>
    public class DigitalTwinGeofence : MonoBehaviour
    {
        [Header("Saha tanimi")]
        [Tooltip("Acik ise merkez ilk gecerli arac pozundan (yoksa harita merkezinden) alinir.")]
        [SerializeField] private bool autoCenterOnFirstFix = true;
        [SerializeField] private double centerLat;
        [SerializeField] private double centerLon;
        [SerializeField] private float radiusMeters = 300f;

        [Header("Izleme")]
        [SerializeField] private float checkIntervalSeconds = 0.5f;
        [SerializeField] private bool watchUav = true;
        [SerializeField] private bool watchRover = true;

        [Header("Gorsel")]
        [SerializeField] private bool drawBoundary = true;
        [SerializeField] private int circleSegments = 72;
        [SerializeField] private float lineWidth = 2.2f;
        [SerializeField] private float yOffset = 4f;

        [Header("Bagimliliklar (otomatik bulunur)")]
        [SerializeField] private AbstractMap abstractMap;
        [SerializeField] private DigitalTwinMissionEngine missionEngine;
        [SerializeField] private DigitalTwinRemoteState remoteState;

        public bool HasCenter { get; private set; }
        public bool UavBreached { get; private set; }
        public bool RoverBreached { get; private set; }
        public bool AnyBreached => UavBreached || RoverBreached;
        public float RadiusMeters => radiusMeters;
        /// <summary>(aracAdi, ihlalDurumu) — durum degisiminde tetiklenir.</summary>
        public event Action<string, bool> OnBreachChanged;

        private DroneWaypointFollower _drone;
        private DigitalTwinRoverAdapter _rover;
        private LineRenderer _circle;
        private float _nextCheckAt;
        private float _nextResolveAt;
        private bool _circleDirty = true;

        private void Awake()
        {
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (missionEngine == null) missionEngine = FindObjectOfType<DigitalTwinMissionEngine>();
            if (remoteState == null) remoteState = FindObjectOfType<DigitalTwinRemoteState>();
            HasCenter = !autoCenterOnFirstFix && (Math.Abs(centerLat) > 0.0001 || Math.Abs(centerLon) > 0.0001);
        }

        private void OnEnable()
        {
            if (abstractMap != null) abstractMap.OnMapRedrawn += MarkCircleDirty;
        }

        private void OnDisable()
        {
            if (abstractMap != null) abstractMap.OnMapRedrawn -= MarkCircleDirty;
        }

        private void OnDestroy()
        {
            if (_circle != null && _circle.material != null) Destroy(_circle.material);
        }

        private void MarkCircleDirty() { _circleDirty = true; }

        /// <summary>Sahayi elle tanimla (or. gorev yukleme aninda).</summary>
        public void SetFence(double lat, double lon, float radiusM)
        {
            centerLat = lat; centerLon = lon;
            radiusMeters = Mathf.Max(10f, radiusM);
            HasCenter = true;
            _circleDirty = true;
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextResolveAt)
            {
                _nextResolveAt = Time.unscaledTime + 2f;
                if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
                if (_drone == null) _drone = FindObjectOfType<DroneWaypointFollower>();
                if (_rover == null) _rover = FindObjectOfType<DigitalTwinRoverAdapter>();
                if (missionEngine == null) missionEngine = FindObjectOfType<DigitalTwinMissionEngine>();
                if (remoteState == null) remoteState = FindObjectOfType<DigitalTwinRemoteState>();
            }
            if (abstractMap == null) return;

            if (!HasCenter && autoCenterOnFirstFix)
                TryAutoCenter();
            if (!HasCenter) return;

            if (drawBoundary && (_circleDirty || _circle == null))
                RebuildCircle();

            if (Time.unscaledTime < _nextCheckAt) return;
            _nextCheckAt = Time.unscaledTime + Mathf.Max(0.1f, checkIntervalSeconds);

            if (watchUav && _drone != null)
                CheckVehicle("IHA", _drone.transform, ref _uavState, v => UavBreached = v);
            if (watchRover && _rover != null && _rover.HasRover)
                CheckVehicle("Rover", _rover.RoverTransform, ref _roverState, v => RoverBreached = v);
        }

        private bool _uavState, _roverState;

        private void CheckVehicle(string label, Transform t, ref bool state, Action<bool> setter)
        {
            if (t == null) return;
            Vector2d geo;
            try { geo = abstractMap.WorldToGeoPosition(t.position); }
            catch { return; }

            double dist = HaversineMeters(centerLat, centerLon, geo.x, geo.y);
            bool breached = dist > radiusMeters;
            if (breached == state) { setter(state); return; }

            state = breached;
            setter(breached);
            _circleDirty = true;   // renk degissin
            OnBreachChanged?.Invoke(label, breached);
            if (missionEngine != null)
                missionEngine.PushExternalEvent("geofence_breach",
                    label + (breached ? " SAHA DISI" : " sahaya dondu") + string.Format(" (mesafe {0:F0} m / sinir {1:F0} m)", dist, radiusMeters));
            if (remoteState != null && breached)
                remoteState.SetWarning(label + " OPERASYON SAHASI DIŞINA ÇIKTI!");
        }

        private void TryAutoCenter()
        {
            // Oncelik: aracin gercek pozu; yoksa harita merkezi.
            if (_drone != null)
            {
                try
                {
                    var geo = abstractMap.WorldToGeoPosition(_drone.transform.position);
                    if (Math.Abs(geo.x) > 0.0001 || Math.Abs(geo.y) > 0.0001)
                    {
                        centerLat = geo.x; centerLon = geo.y;
                        HasCenter = true; _circleDirty = true;
                        return;
                    }
                }
                catch { }
            }
            var c = abstractMap.CenterLatitudeLongitude;
            if (!double.IsNaN(c.x) && !double.IsNaN(c.y) && (c.x != 0.0 || c.y != 0.0))
            {
                centerLat = c.x; centerLon = c.y;
                HasCenter = true; _circleDirty = true;
            }
        }

        private void RebuildCircle()
        {
            _circleDirty = false;
            if (_circle == null)
            {
                var go = new GameObject("GeofenceBoundary");
                go.transform.SetParent(transform, false);
                _circle = go.AddComponent<LineRenderer>();
                _circle.useWorldSpace = true;
                _circle.loop = true;
                _circle.widthMultiplier = lineWidth;
                _circle.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _circle.receiveShadows = false;
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                _circle.material = new Material(shader);
            }

            Color col = AnyBreached ? new Color(1f, 0.30f, 0.25f, 0.95f) : new Color(0.35f, 0.95f, 0.55f, 0.55f);
            _circle.startColor = col; _circle.endColor = col;

            // Yaricapi derece ofsetine cevirip cember noktalarini geo -> world projekte et.
            double dLat = radiusMeters / 111320.0;
            double dLon = radiusMeters / (111320.0 * Math.Max(0.2, Math.Cos(centerLat * Math.PI / 180.0)));
            int n = Mathf.Clamp(circleSegments, 16, 256);
            _circle.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                double a = (double)i / n * Math.PI * 2.0;
                var geo = new Vector2d(centerLat + Math.Sin(a) * dLat, centerLon + Math.Cos(a) * dLon);
                Vector3 w;
                try { w = abstractMap.GeoToWorldPosition(geo, true); }
                catch { continue; }
                w.y += yOffset;
                _circle.SetPosition(i, w);
            }
        }

        private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000.0, D2R = Math.PI / 180.0;
            double dLat = (lat2 - lat1) * D2R, dLon = (lon2 - lon1) * D2R;
            double la1 = lat1 * D2R, la2 = lat2 * D2R;
            double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(la1) * Math.Cos(la2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2 * R * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
        }
    }
}
