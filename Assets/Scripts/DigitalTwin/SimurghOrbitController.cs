using UnityEngine;
using GroundStation.UI;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// HTML twin'deki OrbitControls gibi mouse kamera kontrolu (twin paneli kamerasi icin):
    ///   Sol surukle  -> dondur (orbit)
    ///   Tekerlek     -> yakinlas / uzaklas (zoom)
    ///   Sag surukle  -> kaydir (pan)
    /// SimurghSiteImporter, panel kamerasina ekler ve Init ile baslatir.
    /// </summary>
    public class SimurghOrbitController : MonoBehaviour
    {
        public Vector3 target;
        public float distance = 300f;
        public float yaw = 0f;
        public float pitch = 60f;

        [SerializeField] private float rotSpeed = 4.5f;
        [SerializeField] private float zoomFactor = 0.12f;
        [SerializeField] private float panSpeed = 1.0f;
        [SerializeField] private float minPitch = 15f, maxPitch = 87f;
        [SerializeField] private float minDist = 15f, maxDist = 3000f;
        private Vector3 _homeTarget;
        private float _homeDistance, _homeYaw, _homePitch;
        private bool _orbitDrag, _panDrag;
        public bool IsTopView { get; private set; }

        public void ResetView() { Init(_homeTarget, _homeDistance, _homeYaw, _homePitch); }
        public void TopView() { pitch = 87f; yaw = 0f; IsTopView = true; Apply(); }
        public void Zoom(float factor) { distance = Mathf.Clamp(distance * factor, minDist, maxDist); Apply(); }
        public void Focus(Vector3 point, float dist) { target = point; distance = Mathf.Clamp(dist,minDist,maxDist); Apply(); }

        public void Init(Vector3 tgt, float dist, float initYaw, float initPitch)
        {
            _homeTarget = tgt; _homeDistance = dist; _homeYaw = initYaw; _homePitch = initPitch;
            IsTopView = false;
            target = tgt; distance = Mathf.Clamp(dist, minDist, maxDist);
            yaw = initYaw; pitch = Mathf.Clamp(initPitch, minPitch, maxPitch);
            Apply();
        }

        private void LateUpdate()
        {
            bool blocked = HudInputBlocker.IsPointerOverUI() || HudInputBlocker.IsEditingText;
            if (Input.GetMouseButtonDown(0)) _orbitDrag = !blocked;
            if (Input.GetMouseButtonDown(1)) _panDrag = !blocked;
            if (!Input.GetMouseButton(0)) _orbitDrag = false;
            if (!Input.GetMouseButton(1)) _panDrag = false;
            // sol surukle -> orbit
            if (_orbitDrag)
            {
                IsTopView = false;
                yaw += Input.GetAxis("Mouse X") * rotSpeed;
                pitch -= Input.GetAxis("Mouse Y") * rotSpeed;
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            }
            // sag surukle -> pan (hedefi kaydir)
            if (_panDrag)
            {
                float ps = panSpeed * distance * 0.0015f;
                target -= transform.right * (Input.GetAxis("Mouse X") * ps);
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                target -= forward * (Input.GetAxis("Mouse Y") * ps);
            }
            // tekerlek -> zoom
            float sc = Input.GetAxis("Mouse ScrollWheel");
            if (!blocked && Mathf.Abs(sc) > 0.0001f)
                distance = Mathf.Clamp(distance * (1f - sc * zoomFactor * 10f), minDist, maxDist);

            Apply();
        }

        private void OnDisable() { _orbitDrag = _panDrag = false; }
        private void OnApplicationFocus(bool focus) { if (!focus) _orbitDrag = _panDrag = false; }

        private void Apply()
        {
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.rotation = rot;
            transform.position = target - rot * Vector3.forward * distance;
        }
    }
}
