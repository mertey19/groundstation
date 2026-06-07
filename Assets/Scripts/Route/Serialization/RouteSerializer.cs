using System;
using UnityEngine;

namespace GroundStation.Routes
{
    /// <summary>
    /// RouteData ↔ JSON dönüşümü. Saf yardımcı sınıf; MonoBehaviour değil.
    /// JsonUtility kullanır (Unity yerel, hafif; nested List/Vector3 destekler). Dictionary desteklemez.
    /// </summary>
    public static class RouteSerializer
    {
        /// <summary>
        /// RouteData'yı JSON string'e çevirir.
        /// </summary>
        public static string ToJson(RouteData route, bool prettyPrint = true)
        {
            if (route == null) return "{}";
            try
            {
                return JsonUtility.ToJson(route, prettyPrint);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RouteSerializer] ToJson failed: {e.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// JSON string'den RouteData oluşturur. Hatalı JSON'da null döner.
        /// </summary>
        public static RouteData FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var route = JsonUtility.FromJson<RouteData>(json);
                if (route != null && route.waypoints == null)
                    route.waypoints = new System.Collections.Generic.List<WaypointData>();
                return route;
            }
            catch (Exception e)
            {
                Debug.LogError($"[RouteSerializer] FromJson failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Waypoint listesini JSON'a çevirir (sadece waypoint array export için).
        /// </summary>
        public static string WaypointsToJson(System.Collections.Generic.List<WaypointData> waypoints, bool prettyPrint = true)
        {
            if (waypoints == null) return "[]";
            var wrapper = new WaypointListWrapper { waypoints = waypoints };
            return JsonUtility.ToJson(wrapper, prettyPrint);
        }

        [Serializable]
        private class WaypointListWrapper
        {
            public System.Collections.Generic.List<WaypointData> waypoints;
        }
    }
}
