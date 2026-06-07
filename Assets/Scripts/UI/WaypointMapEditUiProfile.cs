using UnityEngine;

namespace GroundStation.UI
{
    [CreateAssetMenu(
        fileName = "WaypointMapEditUiProfile",
        menuName = "GroundStation/UI/Waypoint Map Edit UI Profile")]
    public class WaypointMapEditUiProfile : ScriptableObject
    {
        [SerializeField] private WaypointMapEditUiSettings settings = new WaypointMapEditUiSettings();

        public WaypointMapEditUiSettings CreateSettingsSnapshot()
        {
            return settings != null ? settings.Clone() : new WaypointMapEditUiSettings();
        }
    }
}
