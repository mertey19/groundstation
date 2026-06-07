using UnityEngine;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// Digital Twin viewport icinde drone ikonunun pozisyon ve rotasyonunu gunceller.
    /// Viewport'un child'i olmali; anchor pivot (0.5, 0.5). Icon Scale ile buyutulur.
    /// </summary>
    public class DigitalTwinDroneView : MonoBehaviour
    {
        [Header("Boyut")]
        [Tooltip("Drone ikonunun ekranda gorunen buyuklugu (1 = sahne boyutu).")]
        [SerializeField] private float iconScale = 2.5f;

        private RectTransform _rect;

        private void Awake()
        {
            _rect = transform as RectTransform;
            if (_rect == null)
                _rect = GetComponent<RectTransform>();
            if (iconScale > 0.01f)
                transform.localScale = Vector3.one * iconScale;
        }

        /// <summary>
        /// Viewport icindeki lokal pozisyon (viewport merkezi 0,0).
        /// </summary>
        public void SetPosition(Vector2 localPosition)
        {
            if (_rect == null) _rect = transform as RectTransform;
            if (_rect != null)
                _rect.anchoredPosition = localPosition;
        }

        /// <summary>
        /// 2D top-down icin Y ekseni etrafinda donus (derece).
        /// </summary>
        public void SetRotation(float angleDegrees)
        {
            if (_rect == null) _rect = transform as RectTransform;
            if (_rect != null)
                _rect.localEulerAngles = new Vector3(0f, 0f, -angleDegrees);
        }
    }
}
