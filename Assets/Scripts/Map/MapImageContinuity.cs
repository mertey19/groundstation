using GroundStation.DigitalTwin;
using Mapbox.Unity.Map;
using Mapbox.Unity.MeshGeneration.Enums;
using UnityEngine;

namespace GroundStation.Map
{
    // Buffer the map camera only; HUD panels remain responsive during image downloads.
    [RequireComponent(typeof(Camera))]
    public sealed class MapImageContinuity : MonoBehaviour
    {
        private static MapImageContinuity _active;
        private AbstractMap _map;
        private Camera _camera;
        private RenderTexture _lastComplete;
        private Texture2D _empty;
        private Matrix4x4 _lastViewProjection;
        private GUIStyle _statusStyle;
        private readonly Plane[] _planes = new Plane[6];
        public bool IsHoldingLastFrame { get; private set; }
        public bool HasCompleteFrame => _lastComplete != null;
        public static bool IsUpdating => _active != null && _active.isActiveAndEnabled
            && !DigitalTwinUIController.SiteWorkspaceOpen && (_active.IsHoldingLastFrame || !_active.HasCoverage());

        public void SetMap(AbstractMap map) { _map = map; }
        private void Awake()
        {
            _active = this;
            _camera = GetComponent<Camera>();
            _empty = new Texture2D(1,1);
            _empty.SetPixel(0,0,new Color(.035f,.05f,.075f));
            _empty.Apply();
        }
        private void OnPreCull()
        {
            var view = _camera.projectionMatrix * _camera.worldToCameraMatrix;
            if (view == _lastViewProjection) return;
            _lastViewProjection = view;
            // Camera movement may run after the map's Update in the same frame.
            if (_map != null && _map.TileProvider != null) _map.TileProvider.UpdateTileExtent();
        }
        private bool HasCoverage()
        {
            if (_map == null || _map.MapVisualizer == null || _map.CurrentExtent == null) return false;
            var tiles = _map.MapVisualizer.ActiveTiles;
            if (_map.CurrentExtent.Count == 0) return false;
            GeometryUtility.CalculateFrustumPlanes(_camera, _planes);
            foreach (var id in _map.CurrentExtent)
            {
                // A tile can have its image loaded but no terrain mesh yet. Empty
                // bounds cannot be culled safely: that would certify a frame with holes.
                if (!tiles.TryGetValue(id, out var tile) || tile == null || tile.IsRecycled) return false;
                var mesh = tile.MeshFilter.sharedMesh;
                if (mesh == null || mesh.vertexCount < 3 || mesh.subMeshCount == 0 || mesh.GetIndexCount(0) < 3) return false;
                if (!GeometryUtility.TestPlanesAABB(_planes, tile.MeshRenderer.bounds)) continue;
                if (!tile.gameObject.activeInHierarchy || !tile.MeshRenderer.enabled) return false;
                if (tile.RasterDataState != TilePropertyState.None && (!tile.HasRasterVisual
                    || tile.GetRasterData() == null || tile.MeshRenderer.sharedMaterial == null
                    || tile.MeshRenderer.sharedMaterial.mainTexture != tile.GetRasterData())) return false;
            }
            return true;
        }
        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (HasCoverage())
            {
                if (_lastComplete == null || _lastComplete.width != source.width || _lastComplete.height != source.height)
                {
                    ReleaseFrame();
                    _lastComplete = new RenderTexture(source.width, source.height, 0, source.format)
                        { name = "Last complete map image", hideFlags = HideFlags.DontSave };
                    _lastComplete.Create();
                }
                Graphics.Blit(source, _lastComplete);
                Graphics.Blit(source, destination);
                IsHoldingLastFrame = false;
            }
            else
            {
                Graphics.Blit(_lastComplete != null ? (Texture)_lastComplete : _empty, destination);
                IsHoldingLastFrame = true;
            }
        }
        private void OnGUI()
        {
            if (!IsHoldingLastFrame || DigitalTwinUIController.SiteWorkspaceOpen) return;
            TwinHudTheme.BeginScaledHud();
            try
            {
                var rect = new Rect((TwinHudTheme.ScreenW - 380f) * .5f, 94f, 380f, 30f);
                TwinHudTheme.Fill(rect, new Color(.055f,.078f,.11f,.96f), 8f);
                if (_statusStyle == null)
                    _statusStyle = new GUIStyle(TwinHudTheme.Label) { alignment = TextAnchor.MiddleCenter };
                bool failed = false;
                if (_map != null)
                    foreach (var tile in _map.MapVisualizer.ActiveTiles.Values)
                        if (tile != null && !tile.HasRasterVisual && tile.RasterDataState == TilePropertyState.Error)
                            failed = true;
                string text = failed ? "Harita yüklenemedi · Görünümü yeniden seçin"
                    : _lastComplete == null ? "Harita görüntüsü yükleniyor…"
                    : "Harita yükleniyor · Son görüntü korunuyor";
                GUI.Label(rect, text, _statusStyle);
            }
            finally { TwinHudTheme.EndScaledHud(); }
        }
        private void ReleaseFrame()
        {
            if (_lastComplete == null) return;
            _lastComplete.Release(); Destroy(_lastComplete); _lastComplete = null;
        }
        private void OnDestroy()
        {
            if (_active == this) _active = null;
            ReleaseFrame();
            if (_empty != null) Destroy(_empty);
        }
    }
}
