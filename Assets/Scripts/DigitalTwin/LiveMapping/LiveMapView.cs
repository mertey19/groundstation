using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GroundStation.DigitalTwin
{
    public sealed class LiveMapView : MonoBehaviour
    {
        private const int Layer = 29;
        private static readonly Vector3 SceneOffset = new Vector3(20000, 0, 0);
        private Transform _root;
        private Mesh _points, _grid;
        private Material _pointMaterial, _gridMaterial;
        private LiveMapReceiver _receiver;
        private DigitalTwin3DView _view;
        private Camera _camera;
        private MapCameraController _mapControl;
        private bool _mapWasEnabled;
        private int _mask;
        private string _shownMap;
        public SimurghOrbitController Orbit { get; private set; }
        public string Error { get; private set; }
        public bool IsVisible => _root != null;
        public int RenderedPointCount => _points != null ? _points.vertexCount / 4 : 0;

        public void Show(LiveMapReceiver receiver)
        {
            if (IsVisible) return;
            Error = "";
            try
            {
                _receiver = receiver; _view = GetComponent<DigitalTwin3DView>();
                _camera = _view.RenderCamera; _mask = _camera.cullingMask;
                _view.SetSiteView(true); _camera.cullingMask = 1 << Layer;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(0.025f, 0.043f, 0.064f);
                _camera.nearClipPlane = 0.05f; _camera.farClipPlane = 30000;
                _camera.fieldOfView = 46; _camera.orthographic = false;
                _mapControl = FindObjectOfType<MapCameraController>();
                if (_mapControl != null) { _mapWasEnabled = _mapControl.enabled; _mapControl.enabled = false; }
                _root = new GameObject("MeasuredLiveMap").transform;
                _root.position = SceneOffset;
                var shader = Resources.Load<Shader>("LiveMapPoints");
                if (shader == null) throw new InvalidOperationException("Canlı harita gölgelendiricisi bulunamadı.");
                _pointMaterial = new Material(shader);
                _gridMaterial = new Material(shader); _gridMaterial.SetFloat("_PointSize", 0);
                _points = new Mesh { name = "Measured points", indexFormat = IndexFormat.UInt32 };
                _points.MarkDynamic();
                AddMesh("Measured geometry", _points, _pointMaterial);
                BuildGrid();
                Orbit = _camera.GetComponent<SimurghOrbitController>() ?? _camera.gameObject.AddComponent<SimurghOrbitController>();
                Orbit.enabled = true; Orbit.Init(SceneOffset, 36, -25, 48);
                if (_receiver != null) { _receiver.Changed += Apply; Apply(_receiver.Current); }
            }
            catch (Exception ex) { Hide(); Error = ex.Message; }
        }
        private void AddMesh(string title, Mesh mesh, Material material)
        {
            var go = new GameObject(title) { layer = Layer };
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        }
        private void BuildGrid()
        {
            var vertices = new System.Collections.Generic.List<Vector3>();
            var colors = new System.Collections.Generic.List<Color32>();
            for (int i = -10; i <= 10; i += 2)
            {
                vertices.Add(new Vector3(i, -0.02f, -10)); vertices.Add(new Vector3(i, -0.02f, 10));
                vertices.Add(new Vector3(-10, -0.02f, i)); vertices.Add(new Vector3(10, -0.02f, i));
                for (int k = 0; k < 4; k++) colors.Add(new Color32(28, 49, 66, 255));
            }
            vertices.Add(Vector3.zero); vertices.Add(Vector3.right * 4); colors.Add(new Color32(234, 107, 111, 255)); colors.Add(colors[colors.Count - 1]);
            vertices.Add(Vector3.zero); vertices.Add(Vector3.forward * 4); colors.Add(new Color32(76, 207, 172, 255)); colors.Add(colors[colors.Count - 1]);
            vertices.Add(Vector3.zero); vertices.Add(Vector3.up * 4); colors.Add(new Color32(95, 158, 246, 255)); colors.Add(colors[colors.Count - 1]);
            _grid = new Mesh { name = "Reference axes, not measured terrain" };
            _grid.SetVertices(vertices); _grid.SetColors(colors);
            var indices = new int[vertices.Count]; for (int i = 0; i < indices.Length; i++) indices[i] = i;
            _grid.SetIndices(indices, MeshTopology.Lines, 0); AddMesh("2 m reference grid", _grid, _gridMaterial);
        }
        private void Apply(LiveMapSnapshot snapshot)
        {
            if (_points == null) return;
            _points.Clear();
            if (snapshot == null) { _shownMap = null; return; }
            int count = snapshot.Positions.Length;
            var vertices = new Vector3[count * 4]; var uv = new Vector2[count * 4];
            var colors = new Color32[count * 4]; var triangles = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                int v = i * 4, t = i * 6;
                for (int k = 0; k < 4; k++) { vertices[v + k] = snapshot.Positions[i]; colors[v + k] = snapshot.Colors[i]; }
                uv[v] = new Vector2(-1, -1); uv[v+1] = new Vector2(-1, 1); uv[v+2] = new Vector2(1, 1); uv[v+3] = new Vector2(1, -1);
                triangles[t] = v; triangles[t+1] = v+1; triangles[t+2] = v+2;
                triangles[t+3] = v; triangles[t+4] = v+2; triangles[t+5] = v+3;
            }
            _points.vertices = vertices; _points.colors32 = colors; _points.uv = uv; _points.triangles = triangles;
            var bounds = snapshot.Bounds; bounds.Expand(snapshot.Header.voxelSizeM * 3); _points.bounds = bounds;
            _pointMaterial.SetFloat("_PointSize", snapshot.Header.voxelSizeM * 0.9f);
            if (_shownMap != snapshot.Header.mapId) { _shownMap = snapshot.Header.mapId; Fit(); }
        }
        public void Fit()
        {
            var snapshot = _receiver != null ? _receiver.Current : null;
            if (Orbit == null) return;
            if (snapshot == null) Orbit.Init(SceneOffset, 36, -25, 48);
            else Orbit.Init(SceneOffset + snapshot.Bounds.center, Mathf.Max(15, snapshot.Bounds.size.magnitude * 1.7f), -25, 48);
        }
        public void Hide()
        {
            if (_receiver != null) _receiver.Changed -= Apply;
            if (Orbit != null) Orbit.enabled = false;
            if (_camera != null) _camera.cullingMask = _mask;
            if (_view != null) _view.SetSiteView(false);
            if (_mapControl != null) _mapControl.enabled = _mapWasEnabled;
            if (_root != null) Destroy(_root.gameObject);
            if (_points != null) Destroy(_points); if (_grid != null) Destroy(_grid);
            if (_pointMaterial != null) Destroy(_pointMaterial); if (_gridMaterial != null) Destroy(_gridMaterial);
            _root = null; _points = _grid = null; _pointMaterial = _gridMaterial = null;
            _receiver = null; _view = null; _camera = null; _mapControl = null; Orbit = null; _shownMap = null;
        }
        private void OnDisable() => Hide();
        private void OnDestroy() => Hide();
    }
}
