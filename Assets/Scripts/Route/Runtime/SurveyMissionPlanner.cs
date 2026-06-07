using System.Collections.Generic;
using UnityEngine;
using GroundStation.UI;
using Mapbox.Unity.Map;
using Mapbox.Utils;

namespace GroundStation.Routes
{
    public enum SurveyQgcPreset
    {
        GenericMap,
        CorridorHighDetail,
        FastCoverage
    }

    public struct SurveyPlanStats
    {
        public float areaSquareMeters;
        public int waypointCount;
        public float triggerDistanceMeters;
        public float estimatedPhotoIntervalSeconds;
        public float groundResolutionCmPerPixel;
        public float effectiveAltitudeMeters;
        public float transectAngleDeg;
        public float turnaroundDistanceM;
    }

    public class SurveyMissionPlanner : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RouteManager routeManager;
        [SerializeField] private MapCameraController mapCameraController;
        [SerializeField] private AltitudeInputProvider altitudeInputProvider;
        [SerializeField] private AbstractMap abstractMap;

        [Header("Survey Area (Map Bounds)")]
        [SerializeField] private bool useMapBounds = true;
        [SerializeField] private bool useCustomSelectedArea = false;
        [SerializeField] private float minX = -300f;
        [SerializeField] private float maxX = 300f;
        [SerializeField] private float minZ = -300f;
        [SerializeField] private float maxZ = 300f;
        [SerializeField] private float edgeInset = 10f;
        private float _customMinX, _customMaxX, _customMinZ, _customMaxZ;
        private readonly List<Vector3> _customPolygon = new List<Vector3>();
        [SerializeField] private bool useCustomPolygonArea = false;

        [Header("Overlap Settings")]
        [Range(0f, 95f)] [SerializeField] private float frontOverlapPercent = 75f;
        [Range(0f, 95f)] [SerializeField] private float sideOverlapPercent = 65f;

        [Header("Camera Footprint (meters at survey altitude)")]
        [Tooltip("Tek fotograf karesinin ileri-geri boyutu (ucus yonu).")]
        [SerializeField] private float footprintForwardMeters = 30f;
        [Tooltip("Tek fotograf karesinin sag-sol boyutu.")]
        [SerializeField] private float footprintSideMeters = 22f;
        [Header("Camera Angular Settings (minimal mapping)")]
        [SerializeField] private bool useCameraAnglesForFootprint = true;
        [SerializeField] private float cameraHFovDeg = 78f;
        [SerializeField] private float cameraVFovDeg = 52f;
        [Tooltip("Nadirden sapma (derece). 0 = tam asagi bakis.")]
        [SerializeField] private float cameraTiltFromNadirDeg = 0f;

        [Header("Flight")]
        [SerializeField] private float defaultSurveyAltitude = 35f;
        [SerializeField] private float speedOverride = 8f;
        [SerializeField] private bool startFromNearCornerToOrigin = true;

        [Header("QGC-like Transects")]
        [Tooltip("Tarama cizgilerinin yatay eksene gore acisi (derece).")]
        [SerializeField] private float transectAngleDeg = 0f;
        [Tooltip("Hat sonunda donus icin alan disina uzatma (m).")]
        [SerializeField] private float turnaroundDistanceM = 10f;

        [Header("Altitude vs Ground resolution (GSD)")]
        [Tooltip("True: yukseklik footprint hesabi icin kullanilir. False: GSD (cm/px) hedefinden yukseklik turetilir.")]
        [SerializeField] private bool useAltitudeForFootprint = true;
        [Tooltip("Hedef yer cozunurlugu (cm/piksel). Sadece useAltitudeForFootprint kapali iken.")]
        [SerializeField] private float groundResolutionCmPerPx = 3f;
        [Tooltip("GSD hesabi icin referans goruntu genisligi (piksel).")]
        [SerializeField] private int referenceImageWidthPx = 4000;

        [Header("Area scale")]
        [Tooltip("Planar alan hesabinda world biriminin metreye orani (Mapbox genelde ~1).")]
        [SerializeField] private float metersPerWorldUnit = 1f;

        public SurveyPlanStats LastPlanStats { get; private set; }

        private void Awake()
        {
            if (routeManager == null) routeManager = FindObjectOfType<RouteManager>();
            if (mapCameraController == null) mapCameraController = FindObjectOfType<MapCameraController>();
            if (altitudeInputProvider == null) altitudeInputProvider = FindObjectOfType<AltitudeInputProvider>();
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
        }

        public void SetOverlap(float frontPercent, float sidePercent)
        {
            frontOverlapPercent = Mathf.Clamp(frontPercent, 0f, 95f);
            sideOverlapPercent = Mathf.Clamp(sidePercent, 0f, 95f);
        }

        public void SetCameraAngles(float hFovDeg, float vFovDeg, float tiltFromNadirDeg)
        {
            cameraHFovDeg = Mathf.Clamp(hFovDeg, 5f, 170f);
            cameraVFovDeg = Mathf.Clamp(vFovDeg, 5f, 170f);
            cameraTiltFromNadirDeg = Mathf.Clamp(tiltFromNadirDeg, 0f, 80f);
            useCameraAnglesForFootprint = true;
        }

        public void SetTransects(float angleDeg, float turnaroundMeters)
        {
            transectAngleDeg = angleDeg;
            turnaroundDistanceM = Mathf.Max(0f, turnaroundMeters);
        }

        public void SetAltitudeFootprintMode(bool useAltitude, float groundResCmPerPx = 3f)
        {
            useAltitudeForFootprint = useAltitude;
            groundResolutionCmPerPx = Mathf.Clamp(groundResCmPerPx, 0.05f, 500f);
        }

        public void SetReferenceImageWidth(int widthPx)
        {
            referenceImageWidthPx = Mathf.Clamp(widthPx, 320, 32000);
        }

        public void ApplyQgcPreset(SurveyQgcPreset preset)
        {
            switch (preset)
            {
                case SurveyQgcPreset.CorridorHighDetail:
                    frontOverlapPercent = 80f;
                    sideOverlapPercent = 75f;
                    speedOverride = 6f;
                    groundResolutionCmPerPx = 2.0f;
                    turnaroundDistanceM = 15f;
                    break;
                case SurveyQgcPreset.FastCoverage:
                    frontOverlapPercent = 65f;
                    sideOverlapPercent = 55f;
                    speedOverride = 10f;
                    groundResolutionCmPerPx = 5.0f;
                    turnaroundDistanceM = 8f;
                    break;
                default:
                    frontOverlapPercent = 75f;
                    sideOverlapPercent = 65f;
                    speedOverride = 8f;
                    groundResolutionCmPerPx = 3.0f;
                    turnaroundDistanceM = 10f;
                    break;
            }
            useAltitudeForFootprint = false;
        }

        public void RotateSurveyEntryPoint()
        {
            if (routeManager == null) return;
            var data = routeManager.GetRouteData();
            if (data == null || data.waypoints == null || data.waypoints.Count < 2) return;
            bool anySurvey = false;
            for (int i = 0; i < data.waypoints.Count; i++)
            {
                var md = data.waypoints[i].metadata;
                if (md != null && md.label != null && md.label.Equals("survey", System.StringComparison.OrdinalIgnoreCase))
                {
                    anySurvey = true;
                    break;
                }
            }
            if (!anySurvey) return;

            var list = new List<WaypointData>(data.waypoints.Count);
            for (int i = 0; i < data.waypoints.Count; i++)
            {
                int src = data.waypoints.Count - 1 - i;
                list.Add(data.waypoints[src].CloneWithIndex(i));
            }
            routeManager.ReplaceRoute(list);
        }

        public void SetSurveyArea(float areaMinX, float areaMaxX, float areaMinZ, float areaMaxZ)
        {
            _customMinX = Mathf.Min(areaMinX, areaMaxX);
            _customMaxX = Mathf.Max(areaMinX, areaMaxX);
            _customMinZ = Mathf.Min(areaMinZ, areaMaxZ);
            _customMaxZ = Mathf.Max(areaMinZ, areaMaxZ);
            useCustomSelectedArea = true;
        }

        public void ClearCustomSurveyArea()
        {
            useCustomSelectedArea = false;
            useCustomPolygonArea = false;
            _customPolygon.Clear();
        }

        public void SetSurveyPolygon(IList<Vector3> polygonWorldPoints)
        {
            _customPolygon.Clear();
            if (polygonWorldPoints != null)
            {
                for (int i = 0; i < polygonWorldPoints.Count; i++)
                    _customPolygon.Add(polygonWorldPoints[i]);
            }
            useCustomPolygonArea = _customPolygon.Count >= 3;
            if (useCustomPolygonArea)
                useCustomSelectedArea = false;
        }

        public void GenerateSurveyRoute()
        {
            if (routeManager == null) return;

            float bx0, bx1, bz0, bz1;
            if (!ResolveBounds(out bx0, out bx1, out bz0, out bz1))
            {
                Debug.LogWarning("[SurveyMissionPlanner] Geçerli survey bounds bulunamadı.");
                return;
            }

            float altitude = altitudeInputProvider != null ? altitudeInputProvider.GetAltitude() : defaultSurveyAltitude;
            if (!useAltitudeForFootprint)
                altitude = ComputeAltitudeFromGroundResolutionCmPx(groundResolutionCmPerPx);

            float footprintF = footprintForwardMeters;
            float footprintS = footprintSideMeters;
            if (useCameraAnglesForFootprint)
                ComputeFootprintFromAngles(altitude, out footprintF, out footprintS);

            float forwardStep = Mathf.Max(2f, footprintF * (1f - frontOverlapPercent / 100f));
            float laneSpacing = Mathf.Max(2f, footprintS * (1f - sideOverlapPercent / 100f));

            float theta = Mathf.Deg2Rad * transectAngleDeg;
            var points = BuildLawnmowerPointsRotated(bx0, bx1, bz0, bz1, forwardStep, laneSpacing, theta, turnaroundDistanceM);
            if (useCustomPolygonArea && _customPolygon.Count >= 3)
                points = FilterPointsInsidePolygon(points, _customPolygon);
            if (startFromNearCornerToOrigin && points.Count > 1)
                ReorderStartNearOrigin(points);

            float areaM = ComputeSurveyAreaSquareMeters(bx0, bx1, bz0, bz1);
            float gsdCm = ComputeGroundResolutionCmPerPixel(altitude);

            var waypoints = new List<WaypointData>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                var md = new WaypointMetadata
                {
                    speedOverride = speedOverride,
                    actionId = "photo",
                    label = "survey"
                };
                Vector3 p = points[i];
                if (abstractMap != null && abstractMap.Root != null)
                    p.y = abstractMap.Root.position.y;

                if (abstractMap != null)
                {
                    try
                    {
                        var geo = abstractMap.WorldToGeoPosition(p);
                        waypoints.Add(new WaypointData(i, p, geo.x, geo.y, altitude, null, md));
                    }
                    catch
                    {
                        waypoints.Add(new WaypointData(i, p, altitude, null, md));
                    }
                }
                else
                    waypoints.Add(new WaypointData(i, p, altitude, null, md));
            }

            routeManager.ReplaceRoute(waypoints);

            float intervalEst = speedOverride > 0.01f ? forwardStep / speedOverride : 0f;
            LastPlanStats = new SurveyPlanStats
            {
                areaSquareMeters = areaM,
                waypointCount = waypoints.Count,
                triggerDistanceMeters = forwardStep,
                estimatedPhotoIntervalSeconds = intervalEst,
                groundResolutionCmPerPixel = gsdCm,
                effectiveAltitudeMeters = altitude,
                transectAngleDeg = transectAngleDeg,
                turnaroundDistanceM = turnaroundDistanceM
            };

            Debug.Log($"[SurveyMissionPlanner] Survey rota oluşturuldu. WP: {waypoints.Count}, alan≈{areaM:F0} m², tetik≈{forwardStep:F1} m, GSD≈{gsdCm:F2} cm/px, alt≈{altitude:F1} m");
        }

        private float ComputeAltitudeFromGroundResolutionCmPx(float cmPerPx)
        {
            float gsdM = Mathf.Max(0.0005f, cmPerPx / 100f);
            float wPx = Mathf.Max(320, referenceImageWidthPx);
            float hFov = Mathf.Deg2Rad * Mathf.Clamp(cameraHFovDeg, 5f, 170f);
            float swath = 2f * Mathf.Tan(hFov * 0.5f);
            if (swath < 0.001f) swath = 0.001f;
            float h = gsdM * wPx / swath;
            return Mathf.Clamp(h, 1f, 500f);
        }

        private float ComputeGroundResolutionCmPerPixel(float altitudeMeters)
        {
            float h = Mathf.Max(1f, altitudeMeters);
            float hFov = Mathf.Deg2Rad * Mathf.Clamp(cameraHFovDeg, 5f, 170f);
            float wPx = Mathf.Max(320, referenceImageWidthPx);
            float swathM = 2f * h * Mathf.Tan(hFov * 0.5f);
            float gsdM = swathM / wPx;
            return gsdM * 100f;
        }

        private float ComputeSurveyAreaSquareMeters(float bx0, float bx1, float bz0, float bz1)
        {
            if (useCustomPolygonArea && _customPolygon.Count >= 3)
                return PolygonAreaSquareMeters(_customPolygon);
            float w = (bx1 - bx0) * metersPerWorldUnit;
            float h = (bz1 - bz0) * metersPerWorldUnit;
            return Mathf.Max(0f, w * h);
        }

        private float PolygonAreaSquareMeters(List<Vector3> poly)
        {
            if (poly == null || poly.Count < 3) return 0f;
            if (abstractMap != null)
            {
                try
                {
                    double sum = 0d;
                    int n = poly.Count;
                    for (int i = 0; i < n; i++)
                    {
                        var gi = abstractMap.WorldToGeoPosition(poly[i]);
                        var gj = abstractMap.WorldToGeoPosition(poly[(i + 1) % n]);
                        sum += gi.x * gj.y - gj.x * gi.y;
                    }
                    float approx = (float)(System.Math.Abs(sum) * 0.5d);
                    const double mPerDegLat = 111320d;
                    double latRad = System.Math.PI / 180d * abstractMap.CenterLatitudeLongitude.x;
                    double mPerDegLon = 111320d * System.Math.Cos(latRad);
                    return (float)(approx * mPerDegLat * mPerDegLon);
                }
                catch { }
            }

            double shoelace = 0d;
            int nv = poly.Count;
            for (int i = 0; i < nv; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % nv];
                shoelace += a.x * b.z - b.x * a.z;
            }
            float k = metersPerWorldUnit * metersPerWorldUnit;
            return (float)(System.Math.Abs(shoelace) * 0.5d * k);
        }

        private static void GetUvBoundsRect(float bx0, float bx1, float bz0, float bz1, float theta,
            out float uMin, out float uMax, out float vMin, out float vMax)
        {
            float c = Mathf.Cos(theta);
            float s = Mathf.Sin(theta);
            uMin = float.MaxValue; uMax = float.MinValue; vMin = float.MaxValue; vMax = float.MinValue;
            ProjectUv(bx0, bz0, c, s, ref uMin, ref uMax, ref vMin, ref vMax);
            ProjectUv(bx1, bz0, c, s, ref uMin, ref uMax, ref vMin, ref vMax);
            ProjectUv(bx1, bz1, c, s, ref uMin, ref uMax, ref vMin, ref vMax);
            ProjectUv(bx0, bz1, c, s, ref uMin, ref uMax, ref vMin, ref vMax);
        }

        private static void GetUvBoundsPolygon(List<Vector3> poly, float theta,
            out float uMin, out float uMax, out float vMin, out float vMax)
        {
            float c = Mathf.Cos(theta);
            float s = Mathf.Sin(theta);
            uMin = float.MaxValue; uMax = float.MinValue; vMin = float.MaxValue; vMax = float.MinValue;
            for (int i = 0; i < poly.Count; i++)
                ProjectUv(poly[i].x, poly[i].z, c, s, ref uMin, ref uMax, ref vMin, ref vMax);
        }

        private static void ProjectUv(float x, float z, float c, float s,
            ref float uMin, ref float uMax, ref float vMin, ref float vMax)
        {
            float u = x * c + z * s;
            float v = -x * s + z * c;
            if (u < uMin) uMin = u;
            if (u > uMax) uMax = u;
            if (v < vMin) vMin = v;
            if (v > vMax) vMax = v;
        }

        private static Vector3 UvToWorld(float u, float v, float c, float s)
        {
            float x = u * c - v * s;
            float z = u * s + v * c;
            return new Vector3(x, 0f, z);
        }

        private List<Vector3> BuildLawnmowerPointsRotated(
            float bx0, float bx1, float bz0, float bz1,
            float forwardStep, float laneSpacing, float theta, float turnaround)
        {
            float c = Mathf.Cos(theta);
            float s = Mathf.Sin(theta);
            float uMin, uMax, vMin, vMax;
            if (useCustomPolygonArea && _customPolygon.Count >= 3)
                GetUvBoundsPolygon(_customPolygon, theta, out uMin, out uMax, out vMin, out vMax);
            else
                GetUvBoundsRect(bx0, bx1, bz0, bz1, theta, out uMin, out uMax, out vMin, out vMax);

            float uSpan = uMax - uMin;
            float vSpan = vMax - vMin;
            bool sweepAlongU = uSpan >= vSpan;

            var points = new List<Vector3>();
            float ta = Mathf.Max(0f, turnaround);

            if (sweepAlongU)
            {
                uMin -= ta;
                uMax += ta;
                bool flip = false;
                for (float v = vMin; v <= vMax + 0.001f; v += laneSpacing)
                {
                    float uA = flip ? uMax : uMin;
                    float uB = flip ? uMin : uMax;
                    AddSegmentUv(points, uA, uB, v, c, s, forwardStep);
                    flip = !flip;
                }
            }
            else
            {
                vMin -= ta;
                vMax += ta;
                bool flip = false;
                for (float u = uMin; u <= uMax + 0.001f; u += laneSpacing)
                {
                    float vA = flip ? vMax : vMin;
                    float vB = flip ? vMin : vMax;
                    AddSegmentUvVertical(points, u, vA, vB, c, s, forwardStep);
                    flip = !flip;
                }
            }

            return points;
        }

        private static void AddSegmentUv(List<Vector3> points, float uStart, float uEnd, float v, float c, float s, float step)
        {
            float dir = Mathf.Sign(uEnd - uStart);
            if (Mathf.Approximately(dir, 0f)) dir = 1f;
            float u = uStart;
            while ((dir > 0f && u <= uEnd) || (dir < 0f && u >= uEnd))
            {
                points.Add(UvToWorld(u, v, c, s));
                u += dir * step;
            }
            var last = UvToWorld(uEnd, v, c, s);
            if (points.Count == 0 || Vector3.Distance(points[points.Count - 1], last) > 0.1f)
                points.Add(last);
        }

        private static void AddSegmentUvVertical(List<Vector3> points, float u, float vStart, float vEnd, float c, float s, float step)
        {
            float dir = Mathf.Sign(vEnd - vStart);
            if (Mathf.Approximately(dir, 0f)) dir = 1f;
            float v = vStart;
            while ((dir > 0f && v <= vEnd) || (dir < 0f && v >= vEnd))
            {
                points.Add(UvToWorld(u, v, c, s));
                v += dir * step;
            }
            var last = UvToWorld(u, vEnd, c, s);
            if (points.Count == 0 || Vector3.Distance(points[points.Count - 1], last) > 0.1f)
                points.Add(last);
        }

        private bool ResolveBounds(out float bx0, out float bx1, out float bz0, out float bz1)
        {
            bx0 = minX; bx1 = maxX; bz0 = minZ; bz1 = maxZ;

            if (useCustomPolygonArea && _customPolygon.Count >= 3)
            {
                bx0 = float.MaxValue; bx1 = float.MinValue; bz0 = float.MaxValue; bz1 = float.MinValue;
                for (int i = 0; i < _customPolygon.Count; i++)
                {
                    var p = _customPolygon[i];
                    if (p.x < bx0) bx0 = p.x;
                    if (p.x > bx1) bx1 = p.x;
                    if (p.z < bz0) bz0 = p.z;
                    if (p.z > bz1) bz1 = p.z;
                }
            }
            else if (useCustomSelectedArea)
            {
                bx0 = _customMinX; bx1 = _customMaxX; bz0 = _customMinZ; bz1 = _customMaxZ;
            }
            else if (useMapBounds && mapCameraController != null)
            {
                float mx0, mx1, mz0, mz1;
                if (mapCameraController.TryGetBounds(out mx0, out mx1, out mz0, out mz1))
                {
                    bx0 = mx0; bx1 = mx1; bz0 = mz0; bz1 = mz1;
                }
            }

            bx0 += edgeInset; bx1 -= edgeInset; bz0 += edgeInset; bz1 -= edgeInset;
            if (bx1 <= bx0 || bz1 <= bz0) return false;
            return true;
        }

        private void ComputeFootprintFromAngles(float altitude, out float forwardMeters, out float sideMeters)
        {
            float h = Mathf.Max(1f, altitude);
            float hFov = Mathf.Deg2Rad * Mathf.Clamp(cameraHFovDeg, 5f, 170f);
            float vFov = Mathf.Deg2Rad * Mathf.Clamp(cameraVFovDeg, 5f, 170f);
            sideMeters = 2f * h * Mathf.Tan(hFov * 0.5f);
            forwardMeters = 2f * h * Mathf.Tan(vFov * 0.5f);

            float tiltRad = Mathf.Deg2Rad * Mathf.Clamp(cameraTiltFromNadirDeg, 0f, 80f);
            float tiltFactor = 1f / Mathf.Max(0.2f, Mathf.Cos(tiltRad));
            forwardMeters *= tiltFactor;
        }

        private static void ReorderStartNearOrigin(List<Vector3> points)
        {
            if (points.Count < 2) return;
            float dStart = points[0].sqrMagnitude;
            float dEnd = points[points.Count - 1].sqrMagnitude;
            if (dEnd < dStart) points.Reverse();
        }

        private static List<Vector3> FilterPointsInsidePolygon(List<Vector3> source, List<Vector3> polygon)
        {
            var result = new List<Vector3>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                if (IsPointInPolygonXZ(source[i], polygon))
                    result.Add(source[i]);
            }
            return result;
        }

        private static bool IsPointInPolygonXZ(Vector3 point, List<Vector3> polygon)
        {
            bool inside = false;
            float px = point.x, pz = point.z;
            int j = polygon.Count - 1;
            for (int i = 0; i < polygon.Count; j = i++)
            {
                float ix = polygon[i].x, iz = polygon[i].z;
                float jx = polygon[j].x, jz = polygon[j].z;
                bool intersect = ((iz > pz) != (jz > pz)) &&
                                 (px < (jx - ix) * (pz - iz) / Mathf.Max(0.00001f, (jz - iz)) + ix);
                if (intersect) inside = !inside;
            }
            return inside;
        }
    }
}
