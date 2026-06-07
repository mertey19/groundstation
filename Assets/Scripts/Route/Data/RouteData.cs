using System;
using System.Collections.Generic;
using UnityEngine;

namespace GroundStation.Routes
{
    /// <summary>
    /// Saf veri modeli: sıralı waypoint listesi ile rota. MonoBehaviour'dan bağımsız.
    /// Tüm rota işlemleri bu veri üzerinden yapılır; görsel ve telemetry aynı kaynağı kullanır.
    /// </summary>
    [Serializable]
    public class RouteData
    {
        [Tooltip("Rota tanımlayıcı ID (export/networking için).")]
        public string routeId;

        [Tooltip("İsteğe bağlı açıklama.")]
        public string description;

        [Tooltip("Sıralı waypoint listesi. Sahnede gösterilen rota ile bire bir aynıdır.")]
        public List<WaypointData> waypoints = new List<WaypointData>();

        /// <summary>
        /// Rota değiştiğinde tetiklenir (indeks, ekleme, silme, replace). Görselleştirici ve dinleyiciler buna abone olur.
        /// </summary>
        [NonSerialized]
        public Action<RouteData> OnChanged;

        public RouteData()
        {
            routeId = System.Guid.NewGuid().ToString("N").Substring(0, 8);
            description = string.Empty;
        }

        public RouteData(string id, string desc = "")
        {
            routeId = string.IsNullOrEmpty(id) ? System.Guid.NewGuid().ToString("N").Substring(0, 8) : id;
            description = desc ?? string.Empty;
        }

        /// <summary>
        /// Waypoint sayısı.
        /// </summary>
        public int Count => waypoints?.Count ?? 0;

        /// <summary>
        /// İndekse göre waypoint döner. Geçersiz indeks için null.
        /// </summary>
        public WaypointData GetWaypoint(int index)
        {
            if (waypoints == null || index < 0 || index >= waypoints.Count)
                return null;
            return waypoints[index];
        }

        /// <summary>
        /// Sıralı dünya pozisyonları (marker/çizgi için; Y = zemin).
        /// </summary>
        public List<Vector3> GetWorldPositions()
        {
            var list = new List<Vector3>(Count);
            if (waypoints == null) return list;
            foreach (var wp in waypoints)
                list.Add(wp.worldPosition);
            return list;
        }

        /// <summary>
        /// Drone hedef pozisyonları (Y = targetAltitude).
        /// </summary>
        public List<Vector3> GetDroneTargetPositions()
        {
            var list = new List<Vector3>(Count);
            if (waypoints == null) return list;
            foreach (var wp in waypoints)
                list.Add(wp.GetDroneTargetPosition());
            return list;
        }

        /// <summary>
        /// Sona waypoint ekler; indeksleri günceller ve OnChanged tetikler.
        /// worldPosition: XZ harita konumu, Y zemin/marker; altitudeMeters drone uçuş yüksekliği.
        /// </summary>
        public void AddWaypoint(
            Vector3 worldPosition,
            float altitudeMeters = 10f,
            Vector3Int? gridPosition = null,
            WaypointMetadata metadata = null,
            double? latitude = null,
            double? longitude = null)
        {
            if (waypoints == null) waypoints = new List<WaypointData>();
            int idx = waypoints.Count;
            if (latitude.HasValue && longitude.HasValue)
                waypoints.Add(new WaypointData(idx, worldPosition, latitude.Value, longitude.Value, altitudeMeters, gridPosition, metadata));
            else
                waypoints.Add(new WaypointData(idx, worldPosition, altitudeMeters, gridPosition, metadata));
            NotifyChanged();
        }

        /// <summary>
        /// Belirtilen indekse waypoint ekler; sonrasındaki indeksler kayar.
        /// </summary>
        public void InsertWaypoint(
            int atIndex,
            Vector3 worldPosition,
            float altitudeMeters = 10f,
            Vector3Int? gridPosition = null,
            WaypointMetadata metadata = null,
            double? latitude = null,
            double? longitude = null)
        {
            if (waypoints == null) waypoints = new List<WaypointData>();
            atIndex = Mathf.Clamp(atIndex, 0, waypoints.Count);
            if (latitude.HasValue && longitude.HasValue)
                waypoints.Insert(atIndex, new WaypointData(atIndex, worldPosition, latitude.Value, longitude.Value, altitudeMeters, gridPosition, metadata));
            else
                waypoints.Insert(atIndex, new WaypointData(atIndex, worldPosition, altitudeMeters, gridPosition, metadata));
            ReindexFrom(atIndex);
            NotifyChanged();
        }

        /// <summary>
        /// İndeksteki waypoint'i kaldırır ve indeksleri yeniler.
        /// </summary>
        public void RemoveWaypoint(int index)
        {
            if (waypoints == null || index < 0 || index >= waypoints.Count) return;
            waypoints.RemoveAt(index);
            ReindexFrom(index);
            NotifyChanged();
        }

        /// <summary>
        /// İndeksteki waypoint'in pozisyonunu ve/veya yüksekliğini günceller.
        /// </summary>
        public void UpdateWaypoint(int index, Vector3 worldPosition, float? altitudeMeters = null, Vector3Int? gridPosition = null, WaypointMetadata metadata = null)
        {
            if (waypoints == null || index < 0 || index >= waypoints.Count) return;
            var wp = waypoints[index];
            wp.worldPosition = worldPosition;
            if (altitudeMeters.HasValue) wp.targetAltitude = Mathf.Max(0f, altitudeMeters.Value);
            if (gridPosition.HasValue) wp.gridPosition = gridPosition.Value;
            if (metadata != null) wp.metadata = metadata;
            NotifyChanged();
        }

        /// <summary>
        /// Tüm waypoint'leri temizler.
        /// </summary>
        public void Clear()
        {
            if (waypoints != null)
            {
                waypoints.Clear();
                NotifyChanged();
            }
        }

        /// <summary>
        /// Rotayı verilen waypoint listesi ile tamamen değiştirir. İndeksler yeniden atanır.
        /// Pathfinding çıktısı bu yöntemle uygulanır.
        /// </summary>
        public void ReplaceWith(IList<WaypointData> newWaypoints)
        {
            waypoints = new List<WaypointData>();
            if (newWaypoints != null)
            {
                for (int i = 0; i < newWaypoints.Count; i++)
                {
                    var w = newWaypoints[i];
                    waypoints.Add(w.CloneWithIndex(i));
                }
            }
            NotifyChanged();
        }

        /// <summary>
        /// Rotayı Vector3 listesi (pathfinding çıktısı) ile değiştirir. Grid/metadata boş kalır.
        /// </summary>
        public void ReplaceWithPositions(IList<Vector3> positions)
        {
            waypoints = new List<WaypointData>();
            if (positions != null)
            {
                for (int i = 0; i < positions.Count; i++)
                    waypoints.Add(new WaypointData(i, positions[i]));
            }
            NotifyChanged();
        }

        /// <summary>
        /// fromIndex'ten itibaren waypoint indekslerini 0-based sıraya çeker.
        /// </summary>
        private void ReindexFrom(int fromIndex)
        {
            if (waypoints == null) return;
            for (int i = fromIndex; i < waypoints.Count; i++)
                waypoints[i].index = i;
        }

        private void NotifyChanged()
        {
            OnChanged?.Invoke(this);
        }

        /// <summary>
        /// Derin kopya; başka sistemlere gönderirken orijinali değiştirmemek için.
        /// </summary>
        public RouteData Clone()
        {
            var clone = new RouteData(routeId, description);
            if (waypoints != null)
            {
                foreach (var wp in waypoints)
                    clone.waypoints.Add(wp.CloneWithIndex(wp.index));
            }
            return clone;
        }
    }
}
