using System;
using System.Collections.Generic;
using UnityEngine;

namespace GroundStation.Routes
{
    /// <summary>Clips complete transects and connects them through an interior visibility graph.</summary>
    public static class PolygonSurveyPath
    {
        private const float Epsilon = 0.001f;
        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static Vector2 Xz(Vector3 p) => new Vector2(p.x, p.z);
        public static bool Contains(Vector2 point, IList<Vector2> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 a = polygon[j], b = polygon[i], ab = b - a;
                float t = ab.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude) : 0;
                if ((point - (a + t * ab)).sqrMagnitude <= Epsilon * Epsilon) return true;
                if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }
        private static void AddCuts(Vector2 a, Vector2 b, Vector2 c, Vector2 d, List<float> cuts)
        {
            Vector2 r = b - a, s = d - c;
            float cross = Cross(r, s);
            if (Mathf.Abs(cross) > 0.000001f)
            {
                float t = Cross(c - a, s) / cross, u = Cross(c - a, r) / cross;
                if (t >= 0 && t <= 1 && u >= 0 && u <= 1) cuts.Add(t);
            }
            else if (Mathf.Abs(Cross(c - a, r)) < Epsilon && r.sqrMagnitude > Epsilon * Epsilon)
            {
                float t0 = Vector2.Dot(c - a, r) / r.sqrMagnitude, t1 = Vector2.Dot(d - a, r) / r.sqrMagnitude;
                if (Mathf.Max(t0, t1) >= 0 && Mathf.Min(t0, t1) <= 1)
                { cuts.Add(Mathf.Clamp01(t0)); cuts.Add(Mathf.Clamp01(t1)); }
            }
        }
        private static List<float> Cuts(Vector2 a, Vector2 b, IList<Vector2> polygon)
        {
            var cuts = new List<float> { 0, 1 };
            for (int i = 0; i < polygon.Count; i++) AddCuts(a, b, polygon[i], polygon[(i + 1) % polygon.Count], cuts);
            cuts.Sort(); return cuts;
        }
        public static bool SegmentInside(Vector2 a, Vector2 b, IList<Vector2> polygon)
        {
            if (!Contains(a, polygon) || !Contains(b, polygon)) return false;
            var cuts = Cuts(a, b, polygon);
            for (int i = 1; i < cuts.Count; i++)
                if (cuts[i] - cuts[i - 1] > 0.000001f && !Contains(Vector2.Lerp(a, b, (cuts[i] + cuts[i - 1]) * 0.5f), polygon)) return false;
            return true;
        }
        private static bool ValidPolygon(List<Vector2> polygon)
        {
            if (polygon.Count < 3 || polygon.Count > 128) return false;
            double area = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
                if (!GroundStation.DigitalTwin.DigitalTwinMessageValidation.Finite(a.x) || !GroundStation.DigitalTwin.DigitalTwinMessageValidation.Finite(a.y)
                    || (a - b).sqrMagnitude < Epsilon * Epsilon) return false;
                area += Cross(a, b);
                for (int j = i + 1; j < polygon.Count; j++)
                {
                    if (j == i + 1 || (i == 0 && j == polygon.Count - 1)) continue;
                    var cuts = new List<float>(); AddCuts(a, b, polygon[j], polygon[(j + 1) % polygon.Count], cuts);
                    if (cuts.Count > 0) return false;
                }
            }
            return Math.Abs(area) > Epsilon;
        }
        private static bool Connect(List<Vector2> path, Vector2 target, List<Vector2> polygon)
        {
            if (path.Count == 0) { path.Add(target); return true; }
            Vector2 from = path[path.Count - 1];
            if (SegmentInside(from, target, polygon)) { if ((from - target).sqrMagnitude > Epsilon * Epsilon) path.Add(target); return true; }
            var nodes = new List<Vector2> { from, target }; nodes.AddRange(polygon);
            int n = nodes.Count;
            var distance = new float[n]; var previous = new int[n]; var visited = new bool[n];
            for (int i = 0; i < n; i++) { distance[i] = float.PositiveInfinity; previous[i] = -1; }
            distance[0] = 0;
            for (int step = 0; step < n; step++)
            {
                int current = -1;
                for (int i = 0; i < n; i++) if (!visited[i] && (current < 0 || distance[i] < distance[current])) current = i;
                if (current < 0 || float.IsInfinity(distance[current])) return false;
                if (current == 1) break;
                visited[current] = true;
                for (int i = 0; i < n; i++)
                {
                    float candidate = distance[current] + Vector2.Distance(nodes[current], nodes[i]);
                    if (!visited[i] && candidate < distance[i] && SegmentInside(nodes[current], nodes[i], polygon))
                    { distance[i] = candidate; previous[i] = current; }
                }
            }
            if (previous[1] < 0) return false;
            var connector = new List<Vector2>();
            for (int i = 1; i != 0; i = previous[i]) connector.Add(nodes[i]);
            connector.Reverse(); path.AddRange(connector); return true;
        }
        public static bool TryBuild(IList<Vector3> polygonWorld, float angle, float forwardStep, float laneSpacing,
            out List<Vector3> path, out HashSet<Vector3> transit, out string error)
        {
            path = new List<Vector3>(); transit = new HashSet<Vector3>(); error = "Geçersiz veya kendini kesen survey alanı";
            if (polygonWorld == null || forwardStep <= 0 || laneSpacing <= 0 || !GroundStation.DigitalTwin.DigitalTwinMessageValidation.Finite(angle)
                || !GroundStation.DigitalTwin.DigitalTwinMessageValidation.Finite(forwardStep) || !GroundStation.DigitalTwin.DigitalTwinMessageValidation.Finite(laneSpacing)) return false;
            var polygon = new List<Vector2>(); foreach (var p in polygonWorld) polygon.Add(Xz(p));
            if (polygon.Count > 3 && (polygon[0] - polygon[polygon.Count - 1]).sqrMagnitude < Epsilon * Epsilon) polygon.RemoveAt(polygon.Count - 1);
            if (!ValidPolygon(polygon)) return false;
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            Vector2 along = new Vector2(c, s), across = new Vector2(-s, c);
            float lo = float.MaxValue, hi = float.MinValue, bottom = float.MaxValue, top = float.MinValue;
            foreach (var p in polygon)
            { lo = Mathf.Min(lo, Vector2.Dot(p, along)); hi = Mathf.Max(hi, Vector2.Dot(p, along)); bottom = Mathf.Min(bottom, Vector2.Dot(p, across)); top = Mathf.Max(top, Vector2.Dot(p, across)); }
            int lanes = Mathf.Max(1, Mathf.CeilToInt((top - bottom) / laneSpacing));
            if (lanes > 2000) { error = "Survey çok yoğun; hat aralığını artırın"; return false; }
            var route = new List<Vector2>(); var photo = new HashSet<Vector2>();
            for (int lane = 0; lane < lanes; lane++)
            {
                // Half-spacing inset includes narrow areas without relying on vertex-only sampling.
                float v = Mathf.Lerp(bottom, top, (lane + 0.5f) / lanes);
                Vector2 a = along * lo + across * v, b = along * hi + across * v;
                if ((lane & 1) != 0) { var swap = a; a = b; b = swap; }
                var cuts = Cuts(a, b, polygon);
                for (int k = 1; k < cuts.Count; k++)
                {
                    if (cuts[k] - cuts[k - 1] < 0.000001f || !Contains(Vector2.Lerp(a, b, (cuts[k] + cuts[k - 1]) * 0.5f), polygon)) continue;
                    Vector2 start = Vector2.Lerp(a, b, cuts[k - 1]), end = Vector2.Lerp(a, b, cuts[k]);
                    if (!Connect(route, start, polygon)) { error = "Alan içinde bağlantı rotası bulunamadı"; return false; }
                    photo.Add(start);
                    int count = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(start, end) / forwardStep));
                    if (route.Count + count > 20000) { error = "Survey waypoint sınırını aşıyor"; return false; }
                    for (int i = 1; i <= count; i++) { var point = Vector2.Lerp(start, end, (float)i / count); route.Add(point); photo.Add(point); }
                }
            }
            if (route.Count < 2) { error = "Alan içinde tarama hattı üretilemedi"; return false; }
            for (int i = 0; i < route.Count; i++)
            {
                if (i > 0 && !SegmentInside(route[i - 1], route[i], polygon)) { error = "Rota sınır kontrolünü geçemedi"; return false; }
                var p = new Vector3(route[i].x, 0, route[i].y); path.Add(p);
                if (!photo.Contains(route[i])) transit.Add(p);
            }
            error = ""; return true;
        }
    }
}
