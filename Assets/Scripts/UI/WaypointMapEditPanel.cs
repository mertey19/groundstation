using GroundStation.Inputs;
using GroundStation.Routes;
using UnityEngine;
using UnityEngine.UI;

namespace GroundStation.UI
{
    /// <summary>
    /// Separate panel for map-click waypoint placement (<see cref="MapClickSpawner"/>).
    /// When the toggle is off, left-clicks on the map do not add waypoints.
    /// </summary>
    public class WaypointMapEditPanel : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private Toggle enableMapClickPlacementToggle;
        [Tooltip("Optional short hint shown while placement mode is on.")]
        [SerializeField] private GameObject placementHintRoot;

        /// <summary>
        /// Runtime olu?turulan UI i�in (�r. <see cref="WaypointMapEditUi"/>); Inspector atamas?ndan sonra da �a?r?labilir.
        /// </summary>
        public void Configure(Toggle toggle, GameObject hintRoot = null)
        {
            UnsubscribeToggle();
            enableMapClickPlacementToggle = toggle;
            placementHintRoot = hintRoot;
            SubscribeToggle();
        }

        private void Awake()
        {
            SubscribeToggle();
        }

        private void SubscribeToggle()
        {
            if (enableMapClickPlacementToggle == null) return;
            enableMapClickPlacementToggle.isOn = WaypointMapEditState.IsMapClickPlacementEnabled;
            enableMapClickPlacementToggle.onValueChanged.RemoveListener(OnPlacementToggleChanged);
            enableMapClickPlacementToggle.onValueChanged.AddListener(OnPlacementToggleChanged);
            RefreshHint();
        }

        private void UnsubscribeToggle()
        {
            if (enableMapClickPlacementToggle != null)
                enableMapClickPlacementToggle.onValueChanged.RemoveListener(OnPlacementToggleChanged);
        }

        private void OnDestroy()
        {
            UnsubscribeToggle();
        }

        private void OnPlacementToggleChanged(bool enabled)
        {
            WaypointMapEditState.IsMapClickPlacementEnabled = enabled;
            RefreshHint();
        }

        private void RefreshHint()
        {
            if (placementHintRoot != null)
                placementHintRoot.SetActive(WaypointMapEditState.IsMapClickPlacementEnabled);
        }
    }
}
