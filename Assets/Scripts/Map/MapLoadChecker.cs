using UnityEngine;
using Mapbox.Unity.Map;

namespace GroundStation.Map
{
    /// <summary>
    /// Map objesine eklenebilir. Play'de harita yuklenmediyse Console'da nedenini gosterir.
    /// </summary>
    public class MapLoadChecker : MonoBehaviour
    {
        [SerializeField] private AbstractMap abstractMap;
        [SerializeField] private float checkDelay = 2f;

        private void Start()
        {
            if (abstractMap == null) abstractMap = GetComponent<AbstractMap>();
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (abstractMap == null) return;
            Invoke(nameof(Check), checkDelay);
        }

        private void Check()
        {
            if (abstractMap == null) return;

            if (abstractMap.TileProvider == null)
            {
                Debug.LogWarning("[MapLoadChecker] Tile Provider yok! Map objesi > Abstract Map > Extent Options > Extent Type degerini 'Range Around Center' veya 'Camera Bounds' yap. 'Custom' ise harita tile yuklemez.");
                return;
            }

            if (abstractMap.ImageLayer == null)
            {
                Debug.LogWarning("[MapLoadChecker] Image Layer (Imagery) yok. Harita goruntusu cikmaz.");
                return;
            }
        }
    }
}
