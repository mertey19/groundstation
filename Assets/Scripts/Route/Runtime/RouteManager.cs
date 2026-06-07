using System;
using System.Collections.Generic;
using UnityEngine;

namespace GroundStation.Routes
{
    /// <summary>
    /// Rota verisinin merkezi yöneticisi. RouteData'yı tutar; add/remove/clear/replace/update ve
    /// pathfinding çıktısını uygulama işlemlerini yönetir. Görselleştirici ve diğer sistemler bu veriyi kullanır.
    /// </summary>
    public class RouteManager : MonoBehaviour
    {
        [Header("Route Identity")]
        [SerializeField] private string routeId = "";
        [SerializeField] private string description = "";

        [Header("Conversion Defaults (Pathfinding -> Route)")]
        [SerializeField] private float minDuplicateDistance = 0.5f;
        [SerializeField] private float simplifyThreshold = 0f;

        /// <summary>
        /// Tekil rota verisi; sahnede gösterilen çizgi ve telemetry aynı kaynağı kullanır.
        /// </summary>
        private RouteData _routeData;

        /// <summary>
        /// Rota değiştiğinde (her türlü mutasyon). Parametre: güncel RouteData.
        /// </summary>
        public event Action<RouteData> OnRouteChanged;

        /// <summary>
        /// Mevcut rota verisi (read-only referans). Değişiklik yapmak için manager metodlarını kullanın.
        /// </summary>
        public RouteData RouteData => _routeData;

        private void Awake()
        {
            if (_routeData == null)
                _routeData = new RouteData(routeId, description);
            _routeData.OnChanged += RaiseRouteChanged;
        }

        private void OnDestroy()
        {
            if (_routeData != null)
                _routeData.OnChanged -= RaiseRouteChanged;
        }

        private void RaiseRouteChanged(RouteData data)
        {
            OnRouteChanged?.Invoke(data);
        }

        /// <summary>
        /// Dış sistemler için: mevcut RouteData referansı (değişiklik yapmadan okuma).
        /// </summary>
        public RouteData GetRouteData()
        {
            return _routeData;
        }

        /// <summary>
        /// Rotayı tamamen değiştirir (pathfinding sonucu uygulanırken kullanılır).
        /// </summary>
        public void ReplaceRoute(IList<WaypointData> newWaypoints)
        {
            if (_routeData == null) _routeData = new RouteData(routeId, description);
            _routeData.ReplaceWith(newWaypoints);
        }

        /// <summary>
        /// Pathfinding çıktısını (Vector3 listesi) temizleyip rotaya uygular.
        /// </summary>
        public void SetRouteFromPath(IList<Vector3> path)
        {
            var waypoints = RouteConverter.PathToWaypoints(path, minDuplicateDistance, simplifyThreshold);
            ReplaceRoute(waypoints);
        }

        /// <summary>
        /// Pathfinding çıktısını (array) rotaya uygular.
        /// </summary>
        public void SetRouteFromPath(Vector3[] path)
        {
            if (path == null) { ClearRoute(); return; }
            var list = new List<Vector3>(path);
            SetRouteFromPath(list);
        }

        /// <summary>
        /// Sona waypoint ekler. worldPosition XZ + zemin Y; altitudeMeters drone uçuş yüksekliği (boş UI için default 10).
        /// </summary>
        public void AddWaypoint(
            Vector3 worldPosition,
            float altitudeMeters = 10f,
            Vector3Int? gridPosition = null,
            WaypointMetadata metadata = null,
            double? latitude = null,
            double? longitude = null)
        {
            EnsureRoute();
            _routeData.AddWaypoint(worldPosition, Mathf.Max(0f, altitudeMeters), gridPosition, metadata, latitude, longitude);
        }

        /// <summary>
        /// İndekse waypoint ekler.
        /// </summary>
        public void InsertWaypoint(
            int index,
            Vector3 worldPosition,
            float altitudeMeters = 10f,
            Vector3Int? gridPosition = null,
            WaypointMetadata metadata = null,
            double? latitude = null,
            double? longitude = null)
        {
            EnsureRoute();
            _routeData.InsertWaypoint(index, worldPosition, Mathf.Max(0f, altitudeMeters), gridPosition, metadata, latitude, longitude);
        }

        /// <summary>
        /// İndeksteki waypoint'i kaldırır.
        /// </summary>
        public void RemoveWaypoint(int index)
        {
            _routeData?.RemoveWaypoint(index);
        }

        /// <summary>
        /// İndeksteki waypoint'i günceller. altitudeMeters null ise mevcut değer korunur.
        /// </summary>
        public void UpdateWaypoint(int index, Vector3 worldPosition, float? altitudeMeters = null, Vector3Int? gridPosition = null, WaypointMetadata metadata = null)
        {
            _routeData?.UpdateWaypoint(index, worldPosition, altitudeMeters, gridPosition, metadata);
        }

        /// <summary>
        /// Rotayı temizler.
        /// </summary>
        public void ClearRoute()
        {
            _routeData?.Clear();
        }

        /// <summary>
        /// Dışarıdan yüklene JSON ile rotayı değiştirir (Import).
        /// </summary>
        public bool LoadFromJson(string json)
        {
            var loaded = RouteSerializer.FromJson(json);
            if (loaded == null) return false;
            if (_routeData == null) _routeData = new RouteData(routeId, description);
            _routeData.routeId = loaded.routeId;
            _routeData.description = loaded.description;
            _routeData.ReplaceWith(loaded.waypoints);
            return true;
        }

        /// <summary>
        /// Rota ID ve açıklamasını günceller (veri değişmez).
        /// </summary>
        public void SetRouteIdentity(string id, string desc)
        {
            routeId = id ?? routeId;
            description = desc ?? description;
            if (_routeData != null)
            {
                _routeData.routeId = routeId;
                _routeData.description = description;
            }
        }

        private void EnsureRoute()
        {
            if (_routeData == null)
            {
                _routeData = new RouteData(routeId, description);
                _routeData.OnChanged += RaiseRouteChanged;
            }
        }

        // ----- Inspector test helpers -----

        [Header("Inspector Test")]
        [SerializeField] [Tooltip("Test için: bu pozisyona waypoint ekle")]
        private Vector3 testAddPosition;
        [SerializeField] [Tooltip("Tıklandığında test waypoint ekler")]
        private bool addTestWaypoint;
        [SerializeField] [Tooltip("Tıklandığında rotayı temizler")]
        private bool clearRoute;

        private void OnValidate()
        {
            if (addTestWaypoint)
            {
                addTestWaypoint = false;
                if (Application.isPlaying)
                    AddWaypoint(testAddPosition);
            }
            if (clearRoute)
            {
                clearRoute = false;
                if (Application.isPlaying)
                    ClearRoute();
            }
        }
    }
}
