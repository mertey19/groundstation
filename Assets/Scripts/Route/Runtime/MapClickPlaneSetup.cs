using UnityEngine;

namespace GroundStation.Inputs
{
    /// <summary>
    /// Map objesine tıklanabilir bir düzlem ekler. Mapbox tile'larında collider yoksa
    /// raycast hiçbir şeye çarpmaz; bu script child bir Quad + MeshCollider oluşturur.
    /// Bu sayede "Raycast did not hit anything" hatası çözülür.
    /// </summary>
    public class MapClickPlaneSetup : MonoBehaviour
    {
        [Header("Plane Settings")]
        [Tooltip("Quad'ın X/Z boyutu (harita alanını kaplasın)")]
        [SerializeField] private float planeSize = 800f;
        [Tooltip("Quad'ın Y pozisyonu (local). Harita zeminine göre ayarla.")]
        [SerializeField] private float planeHeight = 0f;
        [Tooltip("Oluşturulacak child objenin layer'ı. MapClickSpawner mask'inde bu layer seçili olmalı.")]
        [SerializeField] private string planeLayerName = "Map";

        private GameObject _planeChild;

        private void Awake()
        {
            CreateClickPlaneIfNeeded();
        }

        /// <summary>
        /// Child "MapClickPlane" yoksa oluşturur: Quad + MeshCollider + Layer.
        /// </summary>
        public void CreateClickPlaneIfNeeded()
        {
            const string childName = "MapClickPlane";
            Transform existing = transform.Find(childName);
            if (existing != null)
            {
                _planeChild = existing.gameObject;
                return;
            }

            // Quad mesh Unity'de built-in
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = childName;
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = new Vector3(0f, planeHeight, 0f);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // yatay düzlem
            quad.transform.localScale = new Vector3(planeSize, planeSize, 1f);

            // Renderer'ı kapat (görünmez olsun, sadece raycast için)
            var renderer = quad.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.enabled = false;

            // MeshCollider Quad'da zaten var
            var col = quad.GetComponent<MeshCollider>();
            if (col != null)
                col.convex = false;

            int layer = LayerMask.NameToLayer(planeLayerName);
            if (layer >= 0)
                quad.layer = layer;
            else
                Debug.LogWarning($"[MapClickPlaneSetup] Layer '{planeLayerName}' bulunamadı. Edit > Project Settings > Tags and Layers'dan ekle.");

            _planeChild = quad;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_planeChild != null && Application.isPlaying == false)
            {
                _planeChild.transform.localScale = new Vector3(planeSize, planeSize, 1f);
                _planeChild.transform.localPosition = new Vector3(0f, planeHeight, 0f);
            }
        }
#endif
    }
}
