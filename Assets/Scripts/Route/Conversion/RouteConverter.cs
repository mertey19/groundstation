using System.Collections.Generic;
using UnityEngine;

namespace GroundStation.Routes
{
    /// <summary>
    /// Pathfinding çıktısını waypoint tabanlı rota verisine dönüştürür.
    /// Tekrarlayan / çok yakın waypoint'leri temizler; isteğe bağlı distance threshold ile sadeleştirir.
    /// Saf statik yardımcı; MonoBehaviour değil.
    /// </summary>
    public static class RouteConverter
    {
        /// <summary>
        /// Vector3 listesini (pathfinding çıktısı) sıralı WaypointData listesine çevirir.
        /// Grid pozisyonları verilmez; metadata boş.
        /// </summary>
        public static List<WaypointData> FromWorldPositions(IList<Vector3> positions)
        {
            var list = new List<WaypointData>();
            if (positions == null) return list;
            for (int i = 0; i < positions.Count; i++)
                list.Add(new WaypointData(i, positions[i]));
            return list;
        }

        /// <summary>
        /// Vector3 array'ini waypoint listesine çevirir.
        /// </summary>
        public static List<WaypointData> FromWorldPositions(Vector3[] positions)
        {
            var list = new List<WaypointData>();
            if (positions == null) return list;
            for (int i = 0; i < positions.Length; i++)
                list.Add(new WaypointData(i, positions[i]));
            return list;
        }

        /// <summary>
        /// Grid hücre koordinatlarını (Vector3Int) world pozisyona çevirip waypoint listesi yapar.
        /// worldPositionFromGrid: (gridCoord) => worldPosition dönüşümü (harita/grid sisteminize göre).
        /// </summary>
        public static List<WaypointData> FromGridCells(
            IList<Vector3Int> gridCells,
            System.Func<Vector3Int, Vector3> worldPositionFromGrid)
        {
            var list = new List<WaypointData>();
            if (gridCells == null || worldPositionFromGrid == null) return list;
            for (int i = 0; i < gridCells.Count; i++)
            {
                var cell = gridCells[i];
                var worldPos = worldPositionFromGrid(cell);
                list.Add(new WaypointData(i, worldPos, 10f, cell));
            }
            return list;
        }

        /// <summary>
        /// Ardışık tekrarları ve minDistance'tan daha yakın waypoint'leri kaldırır.
        /// İlk waypoint her zaman kalır. İndeksler yeniden atanır.
        /// </summary>
        /// <param name="waypoints">Girdi listesi (değiştirilmez; yeni liste döner)</param>
        /// <param name="minDistance">Bu mesafeden yakın ardışık noktalar birleştirilir (birisi çıkarılır)</param>
        public static List<WaypointData> RemoveDuplicateOrTooClose(IList<WaypointData> waypoints, float minDistance = 0.5f)
        {
            var result = new List<WaypointData>();
            if (waypoints == null || waypoints.Count == 0) return result;
            if (minDistance <= 0f) minDistance = 0.001f;

            result.Add(waypoints[0].CloneWithIndex(0));
            for (int i = 1; i < waypoints.Count; i++)
            {
                var prev = result[result.Count - 1];
                var curr = waypoints[i];
                float dist = Vector3.Distance(prev.worldPosition, curr.worldPosition);
                if (dist >= minDistance)
                    result.Add(curr.CloneWithIndex(result.Count));
            }

            for (int i = 0; i < result.Count; i++)
                result[i].index = i;
            return result;
        }

        /// <summary>
        /// Douglas-Peucker benzeri sadeleştirme: ardışık noktalar arası mesafe threshold'dan küçükse
        /// ara noktayı atlar. Çizgiyi bozmadan waypoint sayısını azaltır.
        /// </summary>
        /// <param name="waypoints">Girdi listesi</param>
        /// <param name="distanceThreshold">Bu mesafeden kısa segmentler atlanır (basitleştirme)</param>
        public static List<WaypointData> SimplifyByDistance(IList<WaypointData> waypoints, float distanceThreshold = 2f)
        {
            var result = new List<WaypointData>();
            if (waypoints == null || waypoints.Count <= 2) return waypoints != null ? new List<WaypointData>(waypoints) : result;
            if (distanceThreshold <= 0f) distanceThreshold = 0.5f;

            result.Add(waypoints[0].CloneWithIndex(0));
            int lastKept = 0;
            for (int i = 1; i < waypoints.Count; i++)
            {
                float segLength = Vector3.Distance(waypoints[lastKept].worldPosition, waypoints[i].worldPosition);
                if (segLength >= distanceThreshold)
                {
                    lastKept = i;
                    result.Add(waypoints[i].CloneWithIndex(result.Count));
                }
            }
            // Son nokta her zaman eklenmeli (pathfinding'de hedef)
            if (lastKept < waypoints.Count - 1)
                result.Add(waypoints[waypoints.Count - 1].CloneWithIndex(result.Count));

            for (int i = 0; i < result.Count; i++)
                result[i].index = i;
            return result;
        }

        /// <summary>
        /// Pathfinding çıktısını (Vector3 listesi) alır, temizler, isteğe bağlı sadeleştirir ve
        /// RouteData'ya uygun waypoint listesi döner. RouteManager.ReplaceWith veya RouteData.ReplaceWith ile kullanılır.
        /// </summary>
        public static List<WaypointData> PathToWaypoints(
            IList<Vector3> path,
            float minDuplicateDistance = 0.5f,
            float simplifyThreshold = 0f)
        {
            var list = FromWorldPositions(path);
            list = RemoveDuplicateOrTooClose(list, minDuplicateDistance);
            if (simplifyThreshold > 0f)
                list = SimplifyByDistance(list, simplifyThreshold);
            return list;
        }
    }
}
