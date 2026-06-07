using UnityEngine;
using Mapbox.Unity.Map;
using System.Collections;
using System.Collections.Generic;

namespace GroundStation.Map
{
    /// <summary>
    /// Harita mavi kalmasin diye: Extent Type Custom ise Range Around Center yapar,
    /// boylece tile provider olusur ve tile'lar yuklenir. Map objesine veya sahneye ekle.
    /// Awake'te calisir (Map'in Start'undan once).
    /// Ayrica: Camera Viewport Rect tam ekran degilse (ince harita seridi) duzeltir;
    /// Camera Bounds tile provider'a dogru harita kamerasini baglar.
    /// </summary>
    public class MapForceLoadFix : MonoBehaviour
    {
        [SerializeField] private AbstractMap abstractMap;
        [Header("Ince harita seridi / gri alan duzeltmesi")]
        [SerializeField] private bool fixMapCameraViewportRect = true;
        [SerializeField] private bool fixCameraBoundsMapCamera = true;

        [Header("Senkron / Refresh")]
        [SerializeField] private bool continuousEnforceViewport = true;
        [SerializeField] private float tileRefreshIntervalSeconds = 0.5f;

        private float _nextTileRefreshTime;

        private void Awake()
        {
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (abstractMap == null) return;
            if (abstractMap.Options == null || abstractMap.Options.extentOptions == null) return;

            if (abstractMap.Options.extentOptions.extentType == MapExtentType.Custom)
            {
                abstractMap.Options.extentOptions.extentType = MapExtentType.RangeAroundCenter;
                Debug.Log("[MapForceLoadFix] Extent Type Custom idi; Range Around Center yapildi. Harita tile yukleyebilir.");
            }

            abstractMap.OnInitialized += OnMapInitialized;
        }

        private void OnDestroy()
        {
            if (abstractMap != null)
                abstractMap.OnInitialized -= OnMapInitialized;
        }

        private void Start()
        {
            StartCoroutine(ApplyViewportAndBoundsDelayed());
        }

        private IEnumerator ApplyViewportAndBoundsDelayed()
        {
            yield return null;
            yield return null;
            ApplyViewportAndCameraBoundsFix();
        }

        private void OnMapInitialized()
        {
            ApplyViewportAndCameraBoundsFix();
        }

        private void ApplyViewportAndCameraBoundsFix()
        {
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (abstractMap == null) return;

            Camera mapCam = ResolveMapCamera();

            CameraBoundsTileProviderOptions cb = null;
            if (fixCameraBoundsMapCamera && abstractMap.Options?.extentOptions != null &&
                abstractMap.Options.extentOptions.extentType == MapExtentType.CameraBounds && mapCam != null)
            {
                cb = abstractMap.Options.extentOptions.defaultExtents.cameraBoundsOptions;
                if (cb != null && cb.camera != mapCam)
                {
                    cb.camera = mapCam;
                    Debug.Log($"[MapForceLoadFix] Camera Bounds tile provider kamerasi '{mapCam.name}' olarak ayarlandi.");
                }
            }

            if (fixMapCameraViewportRect)
            {
                var toNormalize = new HashSet<Camera>();
                foreach (var cam in FindObjectsOfType<Camera>())
                {
                    if (cam == null || !cam.enabled) continue;
                    if (cam.CompareTag("MainCamera") || cam.GetComponent<MapCameraController>() != null)
                        toNormalize.Add(cam);
                }
                if (cb != null && cb.camera != null)
                    toNormalize.Add(cb.camera);

                foreach (var cam in toNormalize)
                {
                    var r = cam.rect;
                    if (r.x > 0.001f || r.y > 0.001f || r.width < 0.999f || r.height < 0.999f)
                        Debug.LogWarning($"[MapForceLoadFix] Kamera viewport tam ekran degildi ({cam.name} rect={r}). Tam ekrana cekildi.");
                    cam.rect = new Rect(0f, 0f, 1f, 1f);
                    cam.transform.hasChanged = true;
                }
            }

            if (cb != null && abstractMap.TileProvider != null)
                abstractMap.TileProvider.UpdateTileExtent();
        }

        private void Update()
        {
            if (abstractMap == null || abstractMap.Options == null || abstractMap.Options.extentOptions == null)
                return;

            if (continuousEnforceViewport && fixMapCameraViewportRect)
                ForceFullScreenViewports();

            if (Time.unscaledTime >= _nextTileRefreshTime)
            {
                _nextTileRefreshTime = Time.unscaledTime + Mathf.Max(0.1f, tileRefreshIntervalSeconds);
                if (abstractMap.TileProvider != null)
                    abstractMap.TileProvider.UpdateTileExtent();
            }
        }

        private void ForceFullScreenViewports()
        {
            var toNormalize = new HashSet<Camera>();
            foreach (var cam in FindObjectsOfType<Camera>())
            {
                if (cam == null || !cam.enabled) continue;
                if (cam.CompareTag("MainCamera") || cam.GetComponent<MapCameraController>() != null)
                    toNormalize.Add(cam);
            }

            if (fixCameraBoundsMapCamera && abstractMap.Options?.extentOptions != null)
            {
                if (abstractMap.Options.extentOptions.extentType == MapExtentType.CameraBounds)
                {
                    var cb = abstractMap.Options.extentOptions.defaultExtents.cameraBoundsOptions;
                    if (cb != null && cb.camera != null)
                        toNormalize.Add(cb.camera);
                }
            }

            foreach (var cam in toNormalize)
            {
                cam.rect = new Rect(0f, 0f, 1f, 1f);
                cam.transform.hasChanged = true;
            }
        }

        private static Camera ResolveMapCamera()
        {
            foreach (var cam in FindObjectsOfType<Camera>())
            {
                if (cam != null && cam.enabled && cam.GetComponent<MapCameraController>() != null)
                    return cam;
            }
            return Camera.main;
        }
    }
}
