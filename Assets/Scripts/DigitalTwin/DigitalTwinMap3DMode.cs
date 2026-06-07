using UnityEngine;
using Mapbox.Unity.Map;
using GroundStation.Map;
using System.Collections;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// Digital Twin acikken haritayi ayarlar.
    /// DIKKAT: Terrain modu degisikligi tum tile'lari destroy edip yeniden yukler;
    /// bu nedenle terrain degisikligi KAPALI birakilmistir. Sadece imagery kaynak degisimi yapilir.
    /// Terrain mode istiyorsaniz useSafeTerrain3D = true yapin ve haritanin yeniden yuklenmesini bekleyin.
    /// </summary>
    public class DigitalTwinMap3DMode : MonoBehaviour
    {
        [SerializeField] private AbstractMap abstractMap;
        [SerializeField] private MapboxBuildingsEnabler buildingsEnabler;

        [Header("Imagery")]
        [Tooltip("Twin acikken satellite+street gorunumu kullan. True ise sadece imagery degisir, tile'lar destroy edilmez.")]
        [SerializeField] private bool useSatelliteStreetsInTwin = true;

        [Header("Terrain 3D (DIKKAT: tile yeniden yuklenmesine neden olur)")]
        [Tooltip("False birakin (varsayilan). True yaparsaniz harita yeniden yuklenecek ve gri ekran olusabilir.")]
        [SerializeField] private bool useSafeTerrain3D = false;
        [SerializeField] private float terrainExaggeration = 2.5f;
        [SerializeField] private bool useBuildingsInTwin = false;
        [Tooltip("Kapanista map ayarlarini eskiye dondur. Hata aliyorsan kapali tut.")]
        [SerializeField] private bool restoreMapStateOnClose = false;

        private bool _is3DModeActive;
        private ImagerySourceType _prevImagerySource;
        private bool _cachedImagery;

        private void Awake()
        {
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (buildingsEnabler == null) buildingsEnabler = FindObjectOfType<MapboxBuildingsEnabler>();
        }

        public void Enable3DForTwin()
        {
            if (_is3DModeActive) return;
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (abstractMap == null) return;

            // Mevcut imagery kaynagini kaydet.
            if (!_cachedImagery && abstractMap.ImageLayer != null)
            {
                _prevImagerySource = abstractMap.ImageLayer.LayerSource;
                _cachedImagery = true;
            }

            // Sadece imagery degistir — UpdateMap cagirmadan, tile'lar destroy edilmeden.
            if (useSatelliteStreetsInTwin && abstractMap.ImageLayer != null)
            {
                try
                {
                    abstractMap.ImageLayer.SetLayerSource(ImagerySourceType.MapboxSatelliteStreet);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[DigitalTwinMap3DMode] Imagery source change failed: " + e.Message);
                }
            }

            // Terrain modu: sadece useSafeTerrain3D = true ise ve kullanici riskleri kabul ediyorsa.
            if (useSafeTerrain3D)
            {
                StartCoroutine(ApplyTerrainSafely());
            }

            // Bina katmani: tile destroy etmeden pas gec.
            if (useBuildingsInTwin && !useSafeTerrain3D)
            {
                if (buildingsEnabler == null) buildingsEnabler = FindObjectOfType<MapboxBuildingsEnabler>();
                if (buildingsEnabler != null)
                {
                    try { buildingsEnabler.EnsureBuildingsLayer(); }
                    catch (System.Exception e) { Debug.LogWarning("[DigitalTwinMap3DMode] Buildings: " + e.Message); }
                }
            }

            _is3DModeActive = true;

            // Drone ile taranan saha: net orthophoto + low-poly agac + bina (HTML twin kalitesi).
            // (Taranmamis bolgede Mapbox ham uydusu guzel twin vermedigi icin sabit saha kullaniyoruz.)
            var simurghSite = FindObjectOfType<SimurghSiteImporter>();
            if (simurghSite == null)
                simurghSite = new GameObject("SimurghSite").AddComponent<SimurghSiteImporter>();
            simurghSite.Show();
        }

        public void Disable3DForTwin()
        {
            if (!_is3DModeActive) return;

            if (restoreMapStateOnClose && _cachedImagery && abstractMap != null && abstractMap.ImageLayer != null)
            {
                try
                {
                    abstractMap.ImageLayer.SetLayerSource(_prevImagerySource);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[DigitalTwinMap3DMode] Restore imagery failed: " + e.Message);
                }
            }

            _is3DModeActive = false;

            var simurghSite = FindObjectOfType<SimurghSiteImporter>();
            if (simurghSite != null) simurghSite.Hide();
            var trees = FindObjectOfType<MapboxTreeScatter>();
            if (trees != null) trees.Clear();
        }

        private IEnumerator ApplyTerrainSafely()
        {
            // Bir frame bekle, sonra terrain degistir (tile'lar baska nedenle zaten reloading ise bekle).
            yield return null;
            if (!_is3DModeActive) yield break;
            if (abstractMap == null || abstractMap.Terrain == null) yield break;

            try
            {
                abstractMap.Terrain.SetLayerSource(ElevationSourceType.MapboxTerrain);
                abstractMap.Terrain.SetElevationType(ElevationLayerType.TerrainWithElevation);
                abstractMap.Terrain.SetExaggerationFactor(terrainExaggeration);

                // TEK SEFERLIK UpdateMap — dongude cagirmak tile destroy/rebuild dongusune girer.
                if (abstractMap.Options?.scalingOptions?.scalingStrategy != null)
                    abstractMap.UpdateMap();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[DigitalTwinMap3DMode] Terrain update failed: " + e.Message);
            }
        }
    }
}
