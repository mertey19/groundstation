using GroundStation.Routes;
using System.Collections;
using UnityEngine;

namespace GroundStation.Drone
{
    /// <summary>
    /// Sadece ANA DRONE objesinde olmali. Waypoint marker prefab'larina bu script EKLENMEMELI (eklenirse Start'ta devre disi birakilir).
    /// Drone objesini RouteManager'daki waypoint'ler arasinda gezdirir.
    /// </summary>
    public class DroneWaypointFollower : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RouteManager routeManager;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 10f;
        [SerializeField] private float minMoveSpeed = 1f;
        [SerializeField] private float maxMoveSpeed = 50f;
        [SerializeField] private float rotateSpeed = 5f;
        [SerializeField] private float arriveDistance = 1f;

        [Header("Display")]
        [Tooltip("Drone'un haritada gorunen buyuklugu (1 = sahne boyutu).")]
        [SerializeField] private float displayScale = 4f;

        [Header("State (read-only)")]
        [SerializeField] private bool isRunning;
        [SerializeField] private int currentWaypointIndex;
        [SerializeField] private float currentSpeed;

        private Coroutine _followRoutine;
        private float _routeStartTime;
        private bool _externalPause;

        public bool IsRunning => isRunning;
        public int CurrentWaypointIndex => currentWaypointIndex;
        public float CurrentSpeed => currentSpeed;
        /// <summary>Ucus basladigindan beri gecen sure (saniye). Ucus yokken 0.</summary>
        public float FlightDurationSeconds => isRunning ? (Time.time - _routeStartTime) : 0f;

        /// <summary>Hiz (m/s). Panel ile artirip azaltmak icin.</summary>
        public float MoveSpeed { get => moveSpeed; set => moveSpeed = Mathf.Clamp(value, minMoveSpeed, maxMoveSpeed); }
        /// <summary>Anlik yukseklik (world Y).</summary>
        public float Altitude => transform.position.y;
        /// <summary>Yuksekligi delta kadar degistir (yukari/asagi ucus).</summary>
        public void AddAltitude(float delta) { var p = transform.position; p.y = Mathf.Max(0f, p.y + delta); transform.position = p; }
        /// <summary>Dis sistem zoom/redraw aninda drone hareketini gecici durdurabilir.</summary>
        public bool ExternalPause { get => _externalPause; set => _externalPause = value; }

        private void Awake()
        {
            if (routeManager == null)
                routeManager = FindObjectOfType<RouteManager>();
        }

        private void Start()
        {
            if (gameObject.name.StartsWith("WaypointMarker"))
            {
                enabled = false;
                return;
            }
            if (displayScale > 0.01f)
                transform.localScale = Vector3.one * displayScale;
        }

        public void StartRoute()
        {
            var data = routeManager != null ? routeManager.GetRouteData() : null;
            if (data == null || data.Count == 0)
            {
                Debug.LogWarning("[DroneWaypointFollower] No route to follow.");
                return;
            }

            StopRouteInternal();
            _followRoutine = StartCoroutine(FollowRoutine());
        }

        public void StopRoute()
        {
            StopRouteInternal();
        }

        public void ResetToFirstWaypoint()
        {
            var data = routeManager != null ? routeManager.GetRouteData() : null;
            if (data == null || data.Count == 0) return;

            transform.position = data.waypoints[0].GetDroneTargetPosition();
            currentWaypointIndex = 0;
        }

        private IEnumerator FollowRoutine()
        {
            _routeStartTime = Time.time;
            isRunning = true;
            var data = routeManager.GetRouteData();
            if (data == null || data.Count == 0)
            {
                isRunning = false;
                yield break;
            }

            // Baslangic: ilk waypoint'in 3D hedefine (XZ + targetAltitude) git
            Vector3 firstTarget = data.waypoints[0].GetDroneTargetPosition();
            transform.position = firstTarget;
            currentWaypointIndex = 0;

            while (true)
            {
                data = routeManager.GetRouteData();
                if (data == null || data.Count == 0)
                    break;

                if (currentWaypointIndex >= data.Count)
                    break;

                var wp = data.waypoints[currentWaypointIndex];
                // 3D hedef: XZ harita konumu + waypoint targetAltitude (Y)
                Vector3 target = wp.GetDroneTargetPosition();

                float speed = moveSpeed;
                if (wp.metadata != null && wp.metadata.speedOverride > 0f)
                    speed = wp.metadata.speedOverride;

                while (true)
                {
                    if (_externalPause)
                    {
                        currentSpeed = 0f;
                        yield return null;
                        continue;
                    }

                    data = routeManager.GetRouteData();
                    if (data == null || currentWaypointIndex >= data.Count)
                        break;

                    wp = data.waypoints[currentWaypointIndex];
                    target = wp.GetDroneTargetPosition();
                    if (Vector3.Distance(transform.position, target) <= arriveDistance)
                        break;

                    Vector3 dir = (target - transform.position).normalized;
                    currentSpeed = speed;

                    transform.position += dir * speed * Time.deltaTime;

                    if (dir.sqrMagnitude > 0.001f)
                    {
                        // Yatay + dikey hareket icin yon; yukari asagi bakis icin up = world up
                        Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
                        transform.rotation = Quaternion.Slerp(
                            transform.rotation, targetRot, rotateSpeed * Time.deltaTime);
                    }

                    yield return null;
                }

                transform.position = target;
                currentSpeed = 0f;

                float hold = (wp.metadata != null) ? wp.metadata.holdTimeSeconds : 0f;
                if (hold > 0f)
                    yield return new WaitForSeconds(hold);

                currentWaypointIndex++;
            }

            isRunning = false;
            currentSpeed = 0f;
            _followRoutine = null;
        }

        private void StopRouteInternal()
        {
            if (_followRoutine != null)
                StopCoroutine(_followRoutine);

            _followRoutine = null;
            isRunning = false;
            currentSpeed = 0f;
        }
    }
}