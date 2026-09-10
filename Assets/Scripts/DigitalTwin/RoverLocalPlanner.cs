using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GroundStation.DigitalTwin
{
    public struct LocalMeterPoint
    {
        public double East, North;
        public LocalMeterPoint(double east, double north) { East = east; North = north; }
    }

    public struct DiskObstacle
    {
        public double East, North, RadiusM;
        public DiskObstacle(double east, double north, double radiusM)
        {
            East = east; North = north; RadiusM = radiusM;
        }
    }

    public sealed class RoverPlanRequest
    {
        public LocalMeterPoint Start, Goal;
        public DiskObstacle[] Obstacles;
        public double RoverRadiusM = 0.4;
        public double SafetyMarginM = 0.5;
        public double? BoundsMinEast, BoundsMinNorth, BoundsMaxEast, BoundsMaxNorth;
        public bool CircleConfigured;
        public double CircleEast, CircleNorth, CircleRadiusM;
        public int FenceRevision;
        public int MapGeneration;
        public double CellSizeM = 0.5;
        public int MaxCells = 12000;
        public int MaxMilliseconds = 80;
    }

    public enum RoverPlanStatus { None, Clear, Detour, Blocked }

    public sealed class RoverPlanResult
    {
        public bool Success;
        public RoverPlanStatus Status = RoverPlanStatus.Blocked;
        public string Error = "";
        public LocalMeterPoint[] Path = Array.Empty<LocalMeterPoint>();
        public int MapGeneration;
        public int FenceRevision;
    }

    /// <summary>
    /// Bounded 2D grid search in explicit local metres. Unknown cells outside the
    /// declared workspace are blocked. A failed search never returns a straight
    /// line through obstacles. Geometric clearance is not field drivability.
    /// </summary>
    public static class RoverLocalPlanner
    {
        const double Eps = 1e-9;
        static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };

        public static RoverPlanResult Plan(RoverPlanRequest request)
        {
            var result = new RoverPlanResult
            {
                MapGeneration = request != null ? request.MapGeneration : 0,
                FenceRevision = request != null ? request.FenceRevision : 0
            };
            if (request == null) return Fail(result, "Plan isteği yok");
            if (!Finite(request.Start) || !Finite(request.Goal) || request.RoverRadiusM < 0 || request.SafetyMarginM < 0)
                return Fail(result, "Başlangıç, hedef veya rover boyutu geçersiz");
            if (request.CircleConfigured)
            {
                if (!DigitalTwinMessageValidation.Finite(request.CircleEast) || !DigitalTwinMessageValidation.Finite(request.CircleNorth)
                    || !DigitalTwinMessageValidation.Finite(request.CircleRadiusM) || request.CircleRadiusM <= 0)
                    return Fail(result, "Etkin saha yarıçapı geçersiz");
                if (!PointInCircle(request.Start, request)) return Fail(result, "Başlangıç saha dışında");
                if (!PointInCircle(request.Goal, request)) return Fail(result, "Hedef saha dışında");
            }
            var obstacles = request.Obstacles ?? Array.Empty<DiskObstacle>();
            foreach (var o in obstacles)
                if (!DigitalTwinMessageValidation.Finite(o.East) || !DigitalTwinMessageValidation.Finite(o.North)
                    || !DigitalTwinMessageValidation.Finite(o.RadiusM) || o.RadiusM < 0)
                    return Fail(result, "Engel yarıçapı veya konumu geçersiz");

            double inflate = request.RoverRadiusM + request.SafetyMarginM;
            if (Hits(request.Start, obstacles, inflate)) return Fail(result, "Başlangıç şişkin engelin içinde");
            if (Hits(request.Goal, obstacles, inflate)) return Fail(result, "Hedef şişkin engelin içinde");
            if (Clear(request.Start, request.Goal, obstacles, inflate)
                && InsideBounds(request.Start, request) && InsideBounds(request.Goal, request)
                && PathInCircle(new[] { request.Start, request.Goal }, request))
            {
                result.Success = true;
                result.Status = RoverPlanStatus.Clear;
                result.Path = new[] { request.Start, request.Goal };
                return result;
            }

            if (!TryWorkspace(request, obstacles, inflate, out double minE, out double minN, out double maxE, out double maxN))
                return Fail(result, "Çalışma alanı sınırları geçersiz");

            double cell = request.CellSizeM > 0.05 ? request.CellSizeM : 0.5;
            int width, height;
            while (true)
            {
                width = Math.Max(1, (int)Math.Ceiling((maxE - minE) / cell));
                height = Math.Max(1, (int)Math.Ceiling((maxN - minN) / cell));
                if (width * height <= Math.Max(16, request.MaxCells) || cell >= 2) break;
                cell *= 1.5;
            }
            if (width * height > Math.Max(16, request.MaxCells)) return Fail(result, "Çalışma alanı arama sınırını aşıyor");

            bool[] blocked = new bool[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    double x0 = minE + x * cell, y0 = minN + y * cell;
                    blocked[y * width + x] = CellHits(x0, y0, cell, obstacles, inflate)
                        || CellOutsideCircle(x0, y0, cell, request);
                }

            if (!TryIndex(request.Start, minE, minN, cell, width, height, blocked, out int start)
                || !TryIndex(request.Goal, minE, minN, cell, width, height, blocked, out int goal))
                return Fail(result, "Başlangıç veya hedef tarama ızgarasında değil");

            var clock = Stopwatch.StartNew();
            int limitMs = request.MaxMilliseconds < 1 ? 80 : request.MaxMilliseconds;
            var came = new int[blocked.Length];
            var gScore = new double[blocked.Length];
            for (int i = 0; i < came.Length; i++) { came[i] = -1; gScore[i] = double.PositiveInfinity; }
            gScore[start] = 0;
            var open = new MinHeap();
            open.Push(Heuristic(start, goal, width, cell), start);
            bool found = false;
            while (open.TryPop(out int current))
            {
                if (clock.ElapsedMilliseconds > limitMs) return Fail(result, "Rota araması zaman aşımı");
                if (current == goal) { found = true; break; }
                int cx = current % width, cy = current / width;
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + Dx[d], ny = cy + Dy[d];
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    int next = ny * width + nx;
                    if (blocked[next]) continue;
                    if (d >= 4 && (blocked[cy * width + nx] || blocked[ny * width + cx])) continue;
                    double step = d < 4 ? cell : cell * Math.Sqrt(2);
                    double tentative = gScore[current] + step;
                    if (tentative + Eps >= gScore[next]) continue;
                    came[next] = current;
                    gScore[next] = tentative;
                    open.Push(tentative + Heuristic(next, goal, width, cell), next);
                }
            }
            if (!found) return Fail(result, "Geçerli rover rotası bulunamadı");

            var cells = new List<LocalMeterPoint>();
            for (int at = goal; at >= 0; at = came[at])
            {
                cells.Add(Center(at, minE, minN, cell, width));
                if (at == start) break;
            }
            cells.Reverse();
            if (cells.Count == 0) return Fail(result, "Geçerli rover rotası bulunamadı");
            cells[0] = request.Start;
            cells[cells.Count - 1] = request.Goal;
            if (!PathClear(cells, obstacles, inflate) || !PathInCircle(cells, request))
                return Fail(result, "Izgara yolu sadeleştirme öncesi çarpışıyor veya saha dışında");
            var pulled = StringPull(cells, obstacles, inflate, request);
            if (!PathClear(pulled, obstacles, inflate) || !PathInCircle(pulled, request)) pulled = cells;
            if (!PathClear(pulled, obstacles, inflate) || !PathInCircle(pulled, request))
                return Fail(result, "Sadeleştirilmiş rota saha dışına çıkıyor");
            result.Success = true;
            result.Status = RoverPlanStatus.Detour;
            result.Path = pulled.ToArray();
            return result;
        }

        public static double EffectiveCircleRadius(double fenceRadiusM, double roverRadiusM, double safetyMarginM)
        {
            if (!DigitalTwinMessageValidation.Finite(fenceRadiusM) || !DigitalTwinMessageValidation.Finite(roverRadiusM)
                || !DigitalTwinMessageValidation.Finite(safetyMarginM))
                return double.NaN;
            return fenceRadiusM - roverRadiusM - safetyMarginM;
        }

        public static bool PointInCircle(LocalMeterPoint p, double east, double north, double radiusM)
        {
            if (!DigitalTwinMessageValidation.Finite(radiusM) || radiusM <= 0) return false;
            double dx = p.East - east, dy = p.North - north;
            return dx * dx + dy * dy <= radiusM * radiusM + 1e-9;
        }

        public static bool SegmentHits(LocalMeterPoint a, LocalMeterPoint b, DiskObstacle obstacle, double inflate)
        {
            double radius = obstacle.RadiusM + inflate;
            if (radius < 0) return false;
            double abx = b.East - a.East, aby = b.North - a.North;
            double acx = obstacle.East - a.East, acy = obstacle.North - a.North;
            double ab2 = abx * abx + aby * aby;
            double t = ab2 < Eps ? 0 : Math.Max(0, Math.Min(1, (acx * abx + acy * aby) / ab2));
            double dx = a.East + t * abx - obstacle.East, dy = a.North + t * aby - obstacle.North;
            return dx * dx + dy * dy < radius * radius - 1e-12;
        }

        public static bool PathClear(IList<LocalMeterPoint> path, DiskObstacle[] obstacles, double inflate)
        {
            if (path == null || path.Count < 2) return false;
            for (int i = 1; i < path.Count; i++)
                if (!Clear(path[i - 1], path[i], obstacles, inflate)) return false;
            return true;
        }

        static RoverPlanResult Fail(RoverPlanResult result, string error)
        {
            result.Success = false;
            result.Status = RoverPlanStatus.Blocked;
            result.Error = error;
            result.Path = Array.Empty<LocalMeterPoint>();
            return result;
        }

        static bool Finite(LocalMeterPoint p) => DigitalTwinMessageValidation.Finite(p.East) && DigitalTwinMessageValidation.Finite(p.North);

        static bool Hits(LocalMeterPoint p, DiskObstacle[] obstacles, double inflate)
        {
            for (int i = 0; i < obstacles.Length; i++)
            {
                double r = obstacles[i].RadiusM + inflate;
                double dx = p.East - obstacles[i].East, dy = p.North - obstacles[i].North;
                if (dx * dx + dy * dy < r * r - 1e-12) return true;
            }
            return false;
        }

        static bool Clear(LocalMeterPoint a, LocalMeterPoint b, DiskObstacle[] obstacles, double inflate)
        {
            for (int i = 0; i < obstacles.Length; i++)
                if (SegmentHits(a, b, obstacles[i], inflate)) return false;
            return true;
        }

        static bool InsideBounds(LocalMeterPoint p, RoverPlanRequest request)
        {
            if (!request.BoundsMinEast.HasValue) return true;
            return p.East >= request.BoundsMinEast.Value - 1e-6 && p.East <= request.BoundsMaxEast.Value + 1e-6
                && p.North >= request.BoundsMinNorth.Value - 1e-6 && p.North <= request.BoundsMaxNorth.Value + 1e-6;
        }

        static bool PointInCircle(LocalMeterPoint p, RoverPlanRequest request)
        {
            if (request == null || !request.CircleConfigured) return true;
            return PointInCircle(p, request.CircleEast, request.CircleNorth, request.CircleRadiusM);
        }

        public static bool PathInCircle(IList<LocalMeterPoint> path, RoverPlanRequest request)
        {
            if (request == null || !request.CircleConfigured) return true;
            if (path == null || path.Count == 0) return false;
            for (int i = 0; i < path.Count; i++)
            {
                if (!PointInCircle(path[i], request)) return false;
                if (i > 0 && !SegmentInCircle(path[i - 1], path[i], request)) return false;
            }
            return true;
        }

        static bool SegmentInCircle(LocalMeterPoint a, LocalMeterPoint b, RoverPlanRequest request)
        {
            if (!request.CircleConfigured) return true;
            // A disk is convex, so endpoints inside imply the chord stays inside.
            // Still reject if either end is out so a bounding box cannot sneak a diagonal through.
            return PointInCircle(a, request) && PointInCircle(b, request);
        }

        static bool CellOutsideCircle(double x0, double y0, double cell, RoverPlanRequest request)
        {
            if (request == null || !request.CircleConfigured) return false;
            double r = request.CircleRadiusM;
            if (r <= 0) return true;
            // Vehicle-center cell is unsafe if any corner sits outside the allowed circle.
            if (!PointInCircle(new LocalMeterPoint(x0, y0), request)) return true;
            if (!PointInCircle(new LocalMeterPoint(x0 + cell, y0), request)) return true;
            if (!PointInCircle(new LocalMeterPoint(x0, y0 + cell), request)) return true;
            if (!PointInCircle(new LocalMeterPoint(x0 + cell, y0 + cell), request)) return true;
            return false;
        }

        static bool TryWorkspace(RoverPlanRequest request, DiskObstacle[] obstacles, double inflate,
            out double minE, out double minN, out double maxE, out double maxN)
        {
            minE = Math.Min(request.Start.East, request.Goal.East);
            maxE = Math.Max(request.Start.East, request.Goal.East);
            minN = Math.Min(request.Start.North, request.Goal.North);
            maxN = Math.Max(request.Start.North, request.Goal.North);
            double pad = Math.Max(4, inflate + 2);
            minE -= pad; maxE += pad; minN -= pad; maxN += pad;
            foreach (var o in obstacles)
            {
                double r = o.RadiusM + inflate + 1;
                minE = Math.Min(minE, o.East - r);
                maxE = Math.Max(maxE, o.East + r);
                minN = Math.Min(minN, o.North - r);
                maxN = Math.Max(maxN, o.North + r);
            }
            if (request.CircleConfigured)
            {
                minE = Math.Max(minE, request.CircleEast - request.CircleRadiusM);
                maxE = Math.Min(maxE, request.CircleEast + request.CircleRadiusM);
                minN = Math.Max(minN, request.CircleNorth - request.CircleRadiusM);
                maxN = Math.Min(maxN, request.CircleNorth + request.CircleRadiusM);
            }
            if (request.BoundsMinEast.HasValue)
            {
                minE = Math.Max(minE, request.BoundsMinEast.Value);
                minN = Math.Max(minN, request.BoundsMinNorth.Value);
                maxE = Math.Min(maxE, request.BoundsMaxEast.Value);
                maxN = Math.Min(maxN, request.BoundsMaxNorth.Value);
            }
            return maxE - minE > 0.2 && maxN - minN > 0.2;
        }

        static bool CellHits(double x0, double y0, double cell, DiskObstacle[] obstacles, double inflate)
        {
            double x1 = x0 + cell, y1 = y0 + cell;
            for (int i = 0; i < obstacles.Length; i++)
            {
                double r = obstacles[i].RadiusM + inflate;
                double cx = Math.Max(x0, Math.Min(obstacles[i].East, x1));
                double cy = Math.Max(y0, Math.Min(obstacles[i].North, y1));
                double dx = obstacles[i].East - cx, dy = obstacles[i].North - cy;
                if (dx * dx + dy * dy < r * r - 1e-12) return true;
            }
            return false;
        }

        static bool TryIndex(LocalMeterPoint p, double minE, double minN, double cell, int width, int height, bool[] blocked, out int index)
        {
            int x = (int)Math.Floor((p.East - minE) / cell);
            int y = (int)Math.Floor((p.North - minN) / cell);
            index = 0;
            int best = -1;
            double bestD = double.MaxValue;
            for (int ny = Math.Max(0, y - 1); ny <= Math.Min(height - 1, y + 1); ny++)
                for (int nx = Math.Max(0, x - 1); nx <= Math.Min(width - 1, x + 1); nx++)
                {
                    int i = ny * width + nx;
                    if (blocked[i]) continue;
                    var c = Center(i, minE, minN, cell, width);
                    double d = (c.East - p.East) * (c.East - p.East) + (c.North - p.North) * (c.North - p.North);
                    if (d < bestD) { bestD = d; best = i; }
                }
            if (best < 0) return false;
            index = best;
            return true;
        }

        static LocalMeterPoint Center(int index, double minE, double minN, double cell, int width)
        {
            int x = index % width, y = index / width;
            return new LocalMeterPoint(minE + (x + 0.5) * cell, minN + (y + 0.5) * cell);
        }

        static double Heuristic(int a, int b, int width, double cell)
        {
            int ax = a % width, ay = a / width, bx = b % width, by = b / width;
            double dx = ax - bx, dy = ay - by;
            return Math.Sqrt(dx * dx + dy * dy) * cell;
        }

        static List<LocalMeterPoint> StringPull(List<LocalMeterPoint> path, DiskObstacle[] obstacles, double inflate,
            RoverPlanRequest request)
        {
            var pulled = new List<LocalMeterPoint> { path[0] };
            int i = 0;
            while (i < path.Count - 1)
            {
                int best = i + 1;
                for (int j = path.Count - 1; j > i + 1; j--)
                    if (Clear(path[i], path[j], obstacles, inflate) && SegmentInCircle(path[i], path[j], request))
                    { best = j; break; }
                pulled.Add(path[best]);
                i = best;
            }
            return pulled;
        }

        sealed class MinHeap
        {
            readonly List<Entry> _items = new List<Entry>();
            struct Entry { public double F; public int Index; }
            public void Push(double f, int index)
            {
                _items.Add(new Entry { F = f, Index = index });
                int i = _items.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (_items[p].F <= _items[i].F) break;
                    var t = _items[p]; _items[p] = _items[i]; _items[i] = t;
                    i = p;
                }
            }
            public bool TryPop(out int index)
            {
                if (_items.Count == 0) { index = 0; return false; }
                index = _items[0].Index;
                _items[0] = _items[_items.Count - 1];
                _items.RemoveAt(_items.Count - 1);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, s = i;
                    if (l < _items.Count && _items[l].F < _items[s].F) s = l;
                    if (r < _items.Count && _items[r].F < _items[s].F) s = r;
                    if (s == i) break;
                    var t = _items[i]; _items[i] = _items[s]; _items[s] = t;
                    i = s;
                }
                return true;
            }
        }
    }
}
