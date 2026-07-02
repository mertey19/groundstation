using System.Collections.Generic;
using UnityEngine;
using Mapbox.Unity.Map;
using Mapbox.Utils;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// ROVER rotasi gorsellestirme — PDF 3.4.1 "IHA ve Rover icin ortak ara nokta yonetimi".
    /// Onceden JSON'dan gelen rota YALNIZCA IHA payload'inda uygulanirdi; artik
    /// vehicleType=rover mesajindaki route blogu bu bilesene yonlenir ve haritada
    /// IHA rotasindan ayirt edilebilir TURUNCU kesik cizgi + kucuk kare isaretcilerle cizilir.
    /// Veri kaynagi: DigitalTwinJsonPoseBridge (rover route) veya SetRoute cagrisi.
    /// </summary>
    public class DigitalTwinRoverRouteView : MonoBehaviour
    {
        [SerializeField] private AbstractMap abstractMap;
        [SerializeField] private Color routeColor = new Color(1f, 0.62f, 0.18f, 0.95f);
        [SerializeField] private float lineWidth = 2.4f;
        [SerializeField] private float yOffset = 2.2f;
        [SerializeField] private float markerSize = 3.2f;

        private readonly List<Vector2d> _geoPoints = new List<Vector2d>();
        private readonly List<GameObject> _markers = new List<GameObject>();
        private LineRenderer _line;
        private Material _markerMat;

        public int WaypointCount => _geoPoints.Count;

        private void Awake()
        {
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
        }

        private void OnEnable()
        {
            if (abstractMap == null) abstractMap = FindObjectOfType<AbstractMap>();
            if (abstractMap != null) abstractMap.OnMapRedrawn += Rebuild;
        }

        private void OnDisable()
        {
            if (abstractMap != null) abstractMap.OnMapRedrawn -= Rebuild;
        }

        private void OnDestroy()
        {
            if (_line != null && _line.material != null) Destroy(_line.material);
            if (_markerMat != null) Destroy(_markerMat);
        }

        /// <summary>Rover rotasini gunceller (enlem/boylam listesi).</summary>
        public void SetRoute(IList<Vector2d> geoPoints)
        {
            _geoPoints.Clear();
            if (geoPoints != null)
                for (int i = 0; i < geoPoints.Count; i++)
                    _geoPoints.Add(geoPoints[i]);
            Rebuild();
        }

        public void Clear()
        {
            _geoPoints.Clear();
            Rebuild();
        }

        private void Rebuild()
        {
            EnsureLine();
            if (abstractMap == null) { abstractMap = FindObjectOfType<AbstractMap>(); if (abstractMap == null) return; }

            var valid = new List<Vector3>(_geoPoints.Count);
            for (int i = 0; i < _geoPoints.Count; i++)
            {
                Vector3 w;
                try { w = abstractMap.GeoToWorldPosition(_geoPoints[i], true); }
                catch { continue; }
                w.y += yOffset;
                valid.Add(w);
            }

            _line.positionCount = valid.Count;
            if (valid.Count > 0)
                _line.SetPositions(valid.ToArray());

            // Kucuk kare isaretciler (havuzlanmis).
            while (_markers.Count < valid.Count)
            {
                var m = GameObject.CreatePrimitive(PrimitiveType.Cube);
                m.name = "RoverWp";
                var col = m.GetComponent<Collider>(); if (col != null) Destroy(col);
                var mr = m.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    if (_markerMat == null)
                    {
                        var sh = Shader.Find("Sprites/Default");
                        if (sh == null) sh = Shader.Find("Unlit/Color");
                        _markerMat = new Material(sh) { color = routeColor };
                    }
                    mr.sharedMaterial = _markerMat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }
                m.transform.SetParent(transform, false);
                _markers.Add(m);
            }
            for (int i = 0; i < _markers.Count; i++)
            {
                bool on = i < valid.Count;
                _markers[i].SetActive(on);
                if (on)
                {
                    _markers[i].transform.position = valid[i];
                    _markers[i].transform.localScale = Vector3.one * markerSize;
                }
            }
        }

        private void EnsureLine()
        {
            if (_line != null) return;
            var go = new GameObject("RoverRouteLine");
            go.transform.SetParent(transform, false);
            _line = go.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.widthMultiplier = lineWidth;
            _line.numCornerVertices = 2;
            _line.numCapVertices = 2;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            _line.material = new Material(shader);
            _line.startColor = routeColor;
            _line.endColor = routeColor;
            _line.positionCount = 0;
        }
    }
}
