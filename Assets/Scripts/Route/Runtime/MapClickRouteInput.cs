using UnityEngine;
using GroundStation.Routes;

namespace GroundStation.Inputs
{
    /// <summary>
    /// Mouse týklamasýyla harita yüzeyine waypoint ekler.
    /// Map objesinde collider olmalý (MeshCollider / BoxCollider).
    /// </summary>
    public class MapClickRouteInput : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera mapCamera;
        [SerializeField] private RouteManager routeManager;
        [SerializeField] private LayerMask mapLayerMask = ~0; // varsayýlan: her þeyi vur

        private void Awake()
        {
            if (mapCamera == null)
                mapCamera = Camera.main;

            if (routeManager == null)
                routeManager = FindObjectOfType<RouteManager>();
        }

        private void Update()
        {
            if (GroundStation.DigitalTwin.DigitalTwinUIController.SiteWorkspaceOpen || GroundStation.Map.MapImageContinuity.IsUpdating) return;
            // Sol mouse tuþu
            if (!Input.GetMouseButtonDown(0))
                return;

            if (!WaypointMapEditState.IsMapClickPlacementEnabled || GroundStation.UI.HudInputBlocker.IsPointerOverUI())
                return;

            if (mapCamera == null || routeManager == null)
                return;

            Ray ray = mapCamera.ScreenPointToRay(Input.mousePosition);

            // Eðer mask ayarlanmýþsa onu kullan, yoksa her þeyi tarar
            bool hit;
            RaycastHit hitInfo;

            if (mapLayerMask == 0)
                hit = Physics.Raycast(ray, out hitInfo, 10000f);              // mask yok
            else
                hit = Physics.Raycast(ray, out hitInfo, 10000f, mapLayerMask); // seçili layer

            if (hit)
            {
                Vector3 worldPos = hitInfo.point;
                routeManager.AddWaypoint(worldPos);
                // Debug.Log("Waypoint eklendi: " + worldPos);
            }
            else
            {
                // Debug.Log("MapClickRouteInput: Raycast hiçbir þeye çarpmadý.");
            }
        }
    }
}