using UnityEngine;
using Mapbox.Unity.Map;
using GroundStation.DigitalTwin;

namespace GroundStation.Map
{
    /// <summary>
    /// Mapbox AbstractMap stilini butonlarla degistirir. Satellite/Street sadece stil;
    /// DigitalTwin butonu ayri view acip kapattigi icin DigitalTwinUIController'a yonlendirilir.
    /// </summary>
    public class MapStyleSwitcher : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AbstractMap abstractMap;
        [SerializeField] private MapboxBuildingsEnabler buildingsEnabler;
        [Tooltip("Atanirsa DigitalTwinButton bu controller'i ac/kapa yapar; harita stili degismez.")]
        [SerializeField] private DigitalTwinUIController digitalTwinController;
        [Header("3D Map Settings")]
        [SerializeField] private float terrainExaggeration = 2.2f;
        [SerializeField] private bool useBuildingsIn3DMap = false;
        [SerializeField] private bool forceDisableBuildingsForStability = true;

        private void Awake()
        {
            if (abstractMap == null)
                abstractMap = FindObjectOfType<AbstractMap>();
            if (buildingsEnabler == null)
                buildingsEnabler = FindObjectOfType<MapboxBuildingsEnabler>();
            if (digitalTwinController == null)
            {
                var controllers = FindObjectsOfType<DigitalTwinUIController>(true);
                if (controllers != null && controllers.Length > 0)
                    digitalTwinController = controllers[0];
            }
        }

        /// <summary>
        /// Uydu g�r�nt�s� (Satellite).
        /// </summary>
        public void SetSatellite()
        {
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (abstractMap == null || abstractMap.ImageLayer == null) return;
            abstractMap.ImageLayer.SetLayerSource(ImagerySourceType.MapboxSatellite);
            SafeUpdateMap();
            StartCoroutine(RefreshTilesAfterFrame());
        }

        /// <summary>
        /// Sokak / yol g�r�nt�s� (Streets).
        /// </summary>
        public void SetStreet()
        {
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (abstractMap == null || abstractMap.ImageLayer == null) return;
            abstractMap.ImageLayer.SetLayerSource(ImagerySourceType.MapboxStreets);
            SafeUpdateMap();
            StartCoroutine(RefreshTilesAfterFrame());
        }

        /// <summary>
        /// Haritayi normal ekranda da 3D yapar (terrain + bina extrusion).
        /// Bunu ayri bir butona baglayabilirsin.
        /// </summary>
        public void SetMap3D()
        {
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (abstractMap == null) return;

            if (abstractMap.ImageLayer != null)
                abstractMap.ImageLayer.SetLayerSource(ImagerySourceType.MapboxSatelliteStreet);

            if (abstractMap.Terrain != null)
            {
                abstractMap.Terrain.SetLayerSource(ElevationSourceType.MapboxTerrain);
                abstractMap.Terrain.SetElevationType(ElevationLayerType.TerrainWithElevation);
                abstractMap.Terrain.SetExaggerationFactor(terrainExaggeration);
            }

            bool enableBuildings = useBuildingsIn3DMap && !forceDisableBuildingsForStability;
            if (enableBuildings)
            {
                if (buildingsEnabler == null) buildingsEnabler = FindObjectOfType<MapboxBuildingsEnabler>();
                if (buildingsEnabler != null)
                    buildingsEnabler.EnsureBuildingsLayer();
            }
            SafeUpdateMap();
            StartCoroutine(RefreshTilesAfterFrame());
        }

        /// <summary>
        /// Dijital Twin modu � �imdilik yine uydu bazl�.
        /// �stersen burada farkl� bir stil (Custom) kullanabilirsin.
        /// </summary>
        public void SetDigitalTwin()
        {
            if (digitalTwinController == null)
            {
                var controllers = FindObjectsOfType<DigitalTwinUIController>(true);
                if (controllers != null && controllers.Length > 0)
                    digitalTwinController = controllers[0];
            }
            if (digitalTwinController != null)
            {
                digitalTwinController.ToggleView();
                return;
            }
            if (abstractMap == null) return;
            if (abstractMap.ImageLayer == null) return;

            // Fallback: uydu stili�r�m�nde MapboxSatelliteStreets yok; uyduyu baz al�yoruz.
            abstractMap.ImageLayer.SetLayerSource(ImagerySourceType.MapboxSatellite);
            SafeUpdateMap();
            StartCoroutine(RefreshTilesAfterFrame());
        }

        private System.Collections.IEnumerator RefreshTilesAfterFrame()
        {
            yield return null;
            if (abstractMap == null) yield break;
            if (abstractMap.TileProvider != null)
                abstractMap.TileProvider.UpdateTileExtent();
        }

        

        private void SafeUpdateMap()
        {
            if (abstractMap == null) return;
            try
            {
                if (abstractMap.Options?.scalingOptions?.scalingStrategy != null)
                    abstractMap.UpdateMap();
            }
            catch (System.Exception)
            {
                // scaling/options null olabilir; tile yenileme yine RefreshTilesAfterFrame ile yapilir
            }
        }
    }
}