using UnityEngine;

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

        public void Init(Vector3 tgt, float dist, float initYaw, float initPitch)
        {
            target = tgt; distance = Mathf.Clamp(dist, minDist, maxDist);
            yaw = initYaw; pitch = Mathf.Clamp(initPitch, minPitch, maxPitch);
            Apply();
        }

        private void LateUpdate()
        {
            // sol surukle -> orbit
            if (Input.GetMouseButton(0))
            {
                yaw += Input.GetAxis("Mouse X") * rotSpeed;
                pitch -= Input.GetAxis("Mouse Y") * rotSpeed;
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            }
            // sag surukle -> pan (hedefi kaydir)
            if (Input.GetMouseButton(1))
            {
                float ps = panSpeed * distance * 0.0015f;
                target -= transform.right * (Input.GetAxis("Mouse X") * ps);
                target -= transform.up * (Input.GetAxis("Mouse Y") * ps);
            }
            // tekerlek -> zoom
            float sc = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(sc) > 0.0001f)
                distance = Mathf.Clamp(distance * (1f - sc * zoomFactor * 10f), minDist, maxDist);

            Apply();
        }

        private void Apply()
        {
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.rotation = rot;
            transform.position = target - rot * Vector3.forward * distance;
        }
    }
}
