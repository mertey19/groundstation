using UnityEngine;
using Mapbox.Unity.Map;

namespace GroundStation.Map
{
    /// <summary>
    /// Map objesine eklenir. Mapbox vector "Buildings" katmanini acar ve 3B extrude uygular
    /// (duz poligon yerine kabarmis bina). Extrude HER cagrida guncellenir; _done sadece
    /// gereksiz UpdateMap tekrarini onler.
    /// </summary>
    public class MapboxBuildingsEnabler : MonoBehaviour
    {
        [SerializeField] private AbstractMap abstractMap;
        [Tooltip("True = baslangicta otomatik bina katmani ekle.")]
        [SerializeField] private bool addOnStart = true;
        [Tooltip("Bina extrude yontemi. Absolute = tum binalar ayni yukseklik (garantili 3B). " +
                 "Property = OSM 'height' alanindan (gercekci ama bazi binalarda veri yok).")]
        [SerializeField] private bool useRealHeights = false;
        [Tooltip("Absolute modda bina yuksekligi (m). Property modda taban/minimum.")]
        [SerializeField] private float buildingHeight = 18f;

        private bool _updateQueued;

        private void Start()
        {
            if (addOnStart) EnsureBuildingsLayer();
        }

        public void EnsureBuildingsLayer()
        {
            if (abstractMap == null) abstractMap = GetComponent<AbstractMap>();
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (abstractMap == null)
            {
                Debug.LogWarning("[MapboxBuildingsEnabler] AbstractMap bulunamadi.");
                return;
            }

            var vectorData = abstractMap.VectorData;
            if (vectorData == null) return;

            if (!vectorData.IsLayerActive)
                vectorData.SetLayerSource(VectorSourceType.MapboxStreets);

            var buildingLayer = vectorData.FindFeatureSubLayerWithName("Buildings");
            if (buildingLayer == null)
            {
                vectorData.AddPolygonFeatureSubLayer("Buildings", "building");
                buildingLayer = vectorData.FindFeatureSubLayerWithName("Buildings");
            }

            // 3B extrude (her cagrida guncelle).
            if (buildingLayer != null)
            {
                try
                {
                    var ext = buildingLayer.extrusionOptions;
                    ext.extrusionGeometryType = ExtrusionGeometryType.RoofAndSide;
                    if (useRealHeights)
                    {
                        ext.extrusionType = ExtrusionType.PropertyHeight;
                        ext.propertyName = "height";
                        ext.minimumHeight = buildingHeight;   // height yoksa taban
                    }
                    else
                    {
                        ext.extrusionType = ExtrusionType.AbsoluteHeight;
                        ext.maximumHeight = buildingHeight;   // tum binalar sabit (garantili 3B)
                    }
                    ext.extrusionScaleFactor = 1f;
                    Debug.Log($"[MapboxBuildingsEnabler] Bina extrude ayarlandi (mode={(useRealHeights ? "Property" : "Absolute")}, h={buildingHeight}).");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[MapboxBuildingsEnabler] extrude ayarlanamadi: " + e.Message);
                }
            }

            if (!_updateQueued)
            {
                _updateQueued = true;
                StartCoroutine(UpdateMapDelayed());
            }
        }

        private System.Collections.IEnumerator UpdateMapDelayed()
        {
            yield return null;
            _updateQueued = false;
            if (abstractMap == null) yield break;
            try { abstractMap.UpdateMap(); }
            catch (System.Exception e) { Debug.LogWarning("[MapboxBuildingsEnabler] UpdateMap: " + e.Message); }
        }
    }
}
