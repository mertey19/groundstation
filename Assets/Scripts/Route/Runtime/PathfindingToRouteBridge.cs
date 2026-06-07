using System.Collections.Generic;
using UnityEngine;

namespace GroundStation.Routes
{
    /// <summary>
    /// Pathfinding çıktısını RouteManager'a bağlar. Kendi pathfinding motorunuzun (A*, NavMesh, grid)
    /// sonucunu bu sınıfa verin; rota verisi ve görsel güncellenir.
    /// Örnek: sahnede test path ile deneme.
    /// </summary>
    public class PathfindingToRouteBridge : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RouteManager routeManager;

        [Header("Test Path (Inspector / Runtime)")]
        [Tooltip("Pathfinding simülasyonu: bu noktalar path olarak uygulanır")]
        [SerializeField] private List<Vector3> testPathPositions = new List<Vector3>();

        private void Awake()
        {
            if (routeManager == null)
                routeManager = GetComponent<RouteManager>();
        }

        /// <summary>
        /// Pathfinding sonucunu rotaya uygular (waypoint listesi güncellenir, görsel senkron).
        /// </summary>
        public void ApplyPath(IList<Vector3> path)
        {
            if (routeManager == null) return;
            if (path == null || path.Count == 0)
            {
                routeManager.ClearRoute();
                return;
            }
            routeManager.SetRouteFromPath(path);
        }

        /// <summary>
        /// Pathfinding sonucunu (array) rotaya uygular.
        /// </summary>
        public void ApplyPath(Vector3[] path)
        {
            if (path == null) { ApplyPath((IList<Vector3>)null); return; }
            ApplyPath(new List<Vector3>(path));
        }

        /// <summary>
        /// Inspector'daki test path'ini rotaya uygular (Play modunda test için).
        /// </summary>
        [ContextMenu("Apply Test Path to Route")]
        public void ApplyTestPath()
        {
            if (testPathPositions != null && testPathPositions.Count > 0)
                ApplyPath(testPathPositions);
            else
                Debug.LogWarning("[PathfindingToRouteBridge] Test path is empty. Add positions in Inspector.");
        }

        [Header("Inspector Test")]
        [SerializeField] [Tooltip("Play modunda işaretlenirse bir kere test path uygulanır")]
        private bool applyTestPathOnStart;

        private void Start()
        {
            if (applyTestPathOnStart && testPathPositions != null && testPathPositions.Count > 0)
                ApplyTestPath();
        }
    }
}
