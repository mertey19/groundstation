using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using GroundStation.Drone;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// 3D Digital Twin: sahneyi (harita + drone) ikinci bir kamerayla RenderTexture'e cizer,
    /// paneldeki RawImage'da gosterir. Kamera drone'u arkadan-yukaridan takip eder.
    /// Bu script DigitalTwinPanel uzerinde olabilir; kamera SAHNE KOKUNDE olusturulur (Canvas altinda DEGIL).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class DigitalTwin3DView : MonoBehaviour
    {
        [Header("3D Kamera")]
        [Tooltip("Atanmazsa runtime'da sahnede olusturulur. Sahnedeki harita ve drone'u gormeli.")]
        [SerializeField] private Camera twinCamera;
        [Tooltip("Atanmazsa runtime'da olusturulur. 1024x768 varsayilan.")]
        [SerializeField] private RenderTexture renderTexture;
        [SerializeField] private int textureWidth = 1024;
        [SerializeField] private int textureHeight = 768;
        [Tooltip("Ana kamera ortografik olsa bile Twin kamerayi perspektifte zorlar.")]
        [SerializeField] private bool forcePerspectiveCamera = true;
        [Tooltip("Harita tile yuklenmesi bitmeden once gorunen arka plan rengi.")]
        [SerializeField] private Color cameraClearColor = new Color(0.08f, 0.10f, 0.14f, 1f);

        [Header("Goruntu hedefi")]
        [Tooltip("3D goruntunun gosterilecegi RawImage. Bos birakilirsa TwinViewPort icinde aranir/olusturulur.")]
        [SerializeField] private RawImage targetRawImage;

        [Header("Kamera konumu (drone takip)")]
        [Tooltip("Drone'un arkasinda ne kadar geride (XZ).")]
        [SerializeField] private float followDistance = 16f;
        [Tooltip("True ise kamera yuksekligi drone yuksekligiyle ayni olur (+ followHeight offset).")]
        [SerializeField] private bool lockCameraHeightToDrone = false;
        [Tooltip("Drone'un Y konumuna eklenen kamera yukseklik ofseti (metre).")]
        [SerializeField] private float followHeight = 35f;
        [SerializeField] private float smoothSpeed = 7f;

        [Header("Referanslar")]
        [SerializeField] private DroneWaypointFollower drone;
        [Tooltip("3D mod aktifken 2D rota/ikon overlay'i gizle.")]
        [SerializeField] private bool hide2DOverlayWhenActive = true;

        private Transform _droneTransform;
        private DigitalTwinRouteView _routeView;
        private DigitalTwinDroneView _droneView;
        private Vector3 _currentCameraPosition;
        private bool _initialized;
        private bool _cameraCreatedByUs;
        private bool _textureCreatedByUs;

        private void Awake()
        {
            if (drone == null) drone = FindObjectOfType<DroneWaypointFollower>();
            if (drone != null) _droneTransform = drone.transform;
        }

        private void OnEnable()
        {
            EnsureCameraAndTexture();

            if (twinCamera != null)
            {
                if (forcePerspectiveCamera)
                {
                    twinCamera.orthographic = false;
                    twinCamera.fieldOfView = 46f;
                }
                twinCamera.enabled = true;

                if (targetRawImage != null && renderTexture != null)
                {
                    targetRawImage.texture = renderTexture;
                    targetRawImage.color = Color.white;
                    targetRawImage.enabled = true;
                    targetRawImage.raycastTarget = false;
                }
            }

            if (hide2DOverlayWhenActive)
                Set2DOverlayVisible(false);
        }

        private void OnDisable()
        {
            if (twinCamera != null)
                twinCamera.enabled = false;

            if (targetRawImage != null)
            {
                targetRawImage.texture = null;
                targetRawImage.color = new Color(1f, 1f, 1f, 0f);
                targetRawImage.enabled = false;
                targetRawImage.raycastTarget = false;
            }

            if (hide2DOverlayWhenActive)
                Set2DOverlayVisible(true);
        }

        private void OnDestroy()
        {
            // Bu component tarafindan olusturulan kamera ve texture'u temizle.
            if (_cameraCreatedByUs && twinCamera != null)
            {
                twinCamera.enabled = false;
                Destroy(twinCamera.gameObject);
                twinCamera = null;
            }

            if (_textureCreatedByUs && renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }

            // Sonraki OnEnable'da yeniden initialize edilebilsin.
            _initialized = false;
        }

        private void Set2DOverlayVisible(bool visible)
        {
            if (_routeView == null) _routeView = GetComponentInChildren<DigitalTwinRouteView>(true);
            if (_droneView == null) _droneView = GetComponentInChildren<DigitalTwinDroneView>(true);
            if (_routeView != null) _routeView.gameObject.SetActive(visible);
            if (_droneView != null) _droneView.gameObject.SetActive(visible);
        }

        private void LateUpdate()
        {
            if (twinCamera == null) return;

            // Drone referansi kaybolmussa yeniden bulmaya calis.
            if (_droneTransform == null)
            {
                if (drone != null)
                {
                    _droneTransform = drone.transform;
                }
                else
                {
                    var found = FindObjectOfType<DroneWaypointFollower>();
                    if (found != null) { drone = found; _droneTransform = found.transform; }
                }

                if (_droneTransform == null)
                {
                    SetFallbackCameraPosition();
                    return;
                }
            }

            Vector3 dronePos = _droneTransform.position;
            Vector3 forward = _droneTransform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 desiredPos = dronePos - forward * followDistance;
            float targetY = lockCameraHeightToDrone
                ? (dronePos.y + followHeight)
                : (desiredPos.y + followHeight);

            // Haritanin kadraja girmesi icin minimum yukseklik koru.
            desiredPos.y = Mathf.Max(targetY, dronePos.y + 18f);

            float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, smoothSpeed) * Time.deltaTime);
            _currentCameraPosition = Vector3.Lerp(_currentCameraPosition, desiredPos, t);
            twinCamera.transform.position = _currentCameraPosition;
            twinCamera.transform.LookAt(dronePos + Vector3.up * 5f);
        }

        private void SetFallbackCameraPosition()
        {
            if (twinCamera == null) return;
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                _currentCameraPosition = mainCam.transform.position;
                twinCamera.transform.position = _currentCameraPosition;
                twinCamera.transform.rotation = mainCam.transform.rotation;
            }
            else
            {
                _currentCameraPosition = new Vector3(0f, 150f, -150f);
                twinCamera.transform.position = _currentCameraPosition;
                twinCamera.transform.LookAt(Vector3.zero + Vector3.up * 20f);
            }
        }

        private void EnsureCameraAndTexture()
        {
            if (_initialized) return;

            if (drone == null) drone = FindObjectOfType<DroneWaypointFollower>();
            if (drone != null) _droneTransform = drone.transform;

            if (twinCamera == null)
            {
                CreateTwinCamera();
                _cameraCreatedByUs = true;
            }

            if (twinCamera != null && renderTexture == null)
            {
                renderTexture = new RenderTexture(textureWidth, textureHeight, 24);
                renderTexture.name = "DigitalTwin3D_RT";
                renderTexture.Create();
                twinCamera.targetTexture = renderTexture;
                _textureCreatedByUs = true;
            }
            else if (twinCamera != null && renderTexture != null)
            {
                if (!renderTexture.IsCreated()) renderTexture.Create();
                twinCamera.targetTexture = renderTexture;
            }

            if (targetRawImage == null)
                ResolveRawImage();

            // Kameranin baslangic konumunu drone pozisyonuna gore ayarla.
            if (twinCamera != null)
            {
                if (_droneTransform != null)
                {
                    _currentCameraPosition = _droneTransform.position
                        - _droneTransform.forward * followDistance
                        + Vector3.up * followHeight;
                }
                else
                {
                    SetFallbackCameraPosition();
                }

                twinCamera.transform.position = _currentCameraPosition;

                if (_droneTransform != null)
                    twinCamera.transform.LookAt(_droneTransform.position + Vector3.up * 5f);
                else if (Camera.main != null)
                    twinCamera.transform.rotation = Camera.main.transform.rotation;
            }

            _initialized = true;
        }

        private void CreateTwinCamera()
        {
            Camera mainCam = Camera.main;

            GameObject camGo = new GameObject("DigitalTwin3DCamera");

            // MoveGameObjectToScene oncesinde objenin sahne koku olmasi gerekiyor.
            // Yeni olusturulan objeler zaten root durumunda, tasinmak guvenli.
            SceneManager.MoveGameObjectToScene(camGo, gameObject.scene);

            // KRITIK: Canvas veya RectTransform altina ekleme.
            // Sadece non-UI camera rig varsa o parent'a ekle; yoksa sahne kokunde kal.
            if (mainCam != null && mainCam.transform.parent != null)
            {
                Transform candidateParent = mainCam.transform.parent;
                bool isUIElement = candidateParent.GetComponent<RectTransform>() != null
                                || candidateParent.GetComponent<Canvas>() != null;
                if (!isUIElement)
                    camGo.transform.SetParent(candidateParent);
                // UI parent ise: SetParent cagrilmaz, sahne kokunde kalir.
            }
            // mainCam.parent == null veya mainCam == null ise: sahne kokunde kal.

            twinCamera = camGo.AddComponent<Camera>();

            if (mainCam != null)
            {
                twinCamera.CopyFrom(mainCam);
                twinCamera.depth = mainCam.depth - 1;
            }
            else
            {
                twinCamera.cullingMask = ~0;
                twinCamera.depth = -2;
            }

            // CopyFrom sonrasi override: koyu gri clear color ile kendi arka planini kullan.
            // Harita tile'lari yuklenmeden once beyaz/siyah degil, koyu gri gosterir.
            twinCamera.clearFlags = CameraClearFlags.SolidColor;
            twinCamera.backgroundColor = cameraClearColor;

            // CopyFrom ana kameranin targetTexture'ini kopyalar (genellikle null).
            // EnsureCameraAndTexture'da RenderTexture atanacagi icin burada temizle.
            twinCamera.targetTexture = null;

            if (forcePerspectiveCamera)
            {
                twinCamera.orthographic = false;
                twinCamera.fieldOfView = 60f;
            }

            if (mainCam != null)
            {
                twinCamera.transform.position = mainCam.transform.position;
                twinCamera.transform.rotation = mainCam.transform.rotation;
            }
            else
            {
                twinCamera.transform.position = Vector3.up * 100f;
                twinCamera.transform.rotation = Quaternion.identity;
            }

            _currentCameraPosition = twinCamera.transform.position;
            twinCamera.name = "DigitalTwin3DCamera";
            twinCamera.enabled = false;
        }

        private void ResolveRawImage()
        {
            // Usteki parent'larda TwinViewPort ara.
            Transform searchRoot = transform;
            for (int i = 0; i < 4 && searchRoot != null; i++)
            {
                var t = searchRoot.Find("TwinViewPort");
                if (t == null) t = searchRoot.Find("TwinViewport");

                if (t != null)
                {
                    var r = t.GetComponent<RawImage>();
                    if (r != null) { targetRawImage = r; return; }

                    r = t.GetComponentInChildren<RawImage>(true);
                    if (r != null) { targetRawImage = r; return; }

                    // TwinViewPort var ama RawImage yok: olustur.
                    var go = new GameObject("Twin3DRawImage");
                    go.transform.SetParent(t, false);
                    var rt = go.AddComponent<RectTransform>();
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    targetRawImage = go.AddComponent<RawImage>();
                    targetRawImage.color = Color.white;
                    targetRawImage.raycastTarget = false;
                    go.transform.SetAsFirstSibling();
                    return;
                }

                searchRoot = searchRoot.parent;
            }

            // Son care: bu component'in altindaki herhangi bir RawImage.
            var anyRaw = GetComponentInChildren<RawImage>(true);
            if (anyRaw != null) targetRawImage = anyRaw;
        }
    }
}
