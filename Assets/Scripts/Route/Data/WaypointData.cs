using System;
using UnityEngine;

namespace GroundStation.Routes
{
    /// <summary>
    /// Saf veri modeli: tek bir waypoint. MonoBehaviour'dan bağımsız, serileştirilebilir.
    /// Rota veri alışverişi ve telemetry için merkez yapı taşı.
    /// </summary>
    [Serializable]
    public class WaypointData
    {
        [Tooltip("Sıralı indeks (0-based).")]
        public int index;

        [Tooltip("Dünya uzayında pozisyon (XZ = harita konumu; Y = zemin/marker yüksekliği).")]
        public Vector3 worldPosition;

        [Tooltip("Harita uzerinde kalici enlem (lat). Zoom/pan sonrasinda yeniden projeksiyon icin kullanilir.")]
        public double latitude;

        [Tooltip("Harita uzerinde kalici boylam (lon). Zoom/pan sonrasinda yeniden projeksiyon icin kullanilir.")]
        public double longitude;

        [Tooltip("True ise latitude/longitude degeri gecerli olarak set edilmis demektir.")]
        public bool hasGeoPosition;

        [Tooltip("Drone'un bu waypoint'te uçacağı yükseklik (metre). Marker yerde kalır, drone bu Y'ye gider.")]
        public float targetAltitude = 10f;

        [Tooltip("İsteğe bağlı: grid/harita hücre koordinatı.")]
        public Vector3Int gridPosition;

        [Tooltip("İsteğe bağlı: hız, bekleme süresi vb. için metadata.")]
        public WaypointMetadata metadata;

        public WaypointData()
        {
            index = 0;
            worldPosition = Vector3.zero;
            latitude = 0d;
            longitude = 0d;
            hasGeoPosition = false;
            targetAltitude = 10f;
            gridPosition = Vector3Int.zero;
            metadata = new WaypointMetadata();
        }

        public WaypointData(int index, Vector3 worldPosition, float altitudeMeters = 10f, Vector3Int? gridPosition = null, WaypointMetadata metadata = null)
        {
            this.index = index;
            this.worldPosition = worldPosition;
            this.targetAltitude = Mathf.Max(0f, altitudeMeters);
            this.gridPosition = gridPosition ?? Vector3Int.zero;
            this.metadata = metadata ?? new WaypointMetadata();
        }

        public WaypointData(
            int index,
            Vector3 worldPosition,
            double latitude,
            double longitude,
            float altitudeMeters = 10f,
            Vector3Int? gridPosition = null,
            WaypointMetadata metadata = null)
        {
            this.index = index;
            this.worldPosition = worldPosition;
            this.latitude = latitude;
            this.longitude = longitude;
            this.hasGeoPosition = true;
            this.targetAltitude = Mathf.Max(0f, altitudeMeters);
            this.gridPosition = gridPosition ?? Vector3Int.zero;
            this.metadata = metadata ?? new WaypointMetadata();
        }

        /// <summary>
        /// Yeni indeks ile kopya oluşturur (liste sırası değişince kullanılır).
        /// </summary>
        public WaypointData CloneWithIndex(int newIndex)
        {
            var clone = new WaypointData(newIndex, worldPosition, targetAltitude, gridPosition, metadata?.Clone());
            clone.latitude = latitude;
            clone.longitude = longitude;
            clone.hasGeoPosition = hasGeoPosition;
            return clone;
        }

        /// <summary>
        /// Drone hedef pozisyonu: XZ harita konumu, Y = targetAltitude.
        /// </summary>
        public Vector3 GetDroneTargetPosition()
        {
            return new Vector3(worldPosition.x, targetAltitude, worldPosition.z);
        }

        public override string ToString()
        {
            return $"[WP{index}] {worldPosition} alt={targetAltitude}";
        }
    }

    /// <summary>
    /// Waypoint'e ait isteğe bağlı metadata. Telemetry ve drone komutları için genişletilebilir.
    /// </summary>
    [Serializable]
    public class WaypointMetadata
    {
        public float speedOverride = -1f;      // -1 = kullanma
        public float holdTimeSeconds;          // waypoint'te bekleme süresi
        public string actionId;                // örn: "photo", "scan", "land"
        public string label;                   // kullanıcı etiketi

        public WaypointMetadata Clone()
        {
            return new WaypointMetadata
            {
                speedOverride = speedOverride,
                holdTimeSeconds = holdTimeSeconds,
                actionId = actionId,
                label = label
            };
        }
    }
}
