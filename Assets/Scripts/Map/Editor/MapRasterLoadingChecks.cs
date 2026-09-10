#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Mapbox.Map;
using Mapbox.Platform;
using Mapbox.Unity.Map;
using Mapbox.Unity.MeshGeneration.Data;
using Mapbox.Unity.MeshGeneration.Enums;
using Mapbox.Unity.MeshGeneration.Factories.TerrainStrategies;
using Mapbox.Unity.Utilities;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MapRasterLoadingChecks
{
    private static IEnumerator _run;
    private static double _next;
    private static int _count;
    private static readonly StringBuilder Report = new StringBuilder();
    private static ImageDataFetcher _fetcher;
    private static UnityTile _tile;
    private static Material _material;
    private static FakeSource _source;
    private static byte[] _image;
    private static UnityTile _heldTile;
    private static byte[] _heldImage;
    private static readonly BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string Satellite = "mapbox://styles/mapbox/satellite-v9";
    private const string Streets = "mapbox://styles/mapbox/streets-v11";

    [MenuItem("Tools/Simurgh/Harita Yukleme Kontrolu (Play)")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying || _run != null) return;
        _count = 0; Report.Clear(); _next = 0;
        _run = Checks(); EditorApplication.update += Tick;
    }
    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < _next) return;
        try
        {
            if (!EditorApplication.isPlaying) throw new Exception("Play stopped during checks.");
            if (!_run.MoveNext()) { Finish("PASS"); return; }
            _next = EditorApplication.timeSinceStartup + (_run.Current is float seconds ? seconds : .1f);
        }
        catch (Exception ex) { Report.AppendLine(ex.ToString()); Finish("FAIL"); Debug.LogException(ex); }
    }
    private static void Finish(string status)
    {
        EditorApplication.update -= Tick; _run = null;
        if (_heldTile != null && _heldImage != null) _heldTile.SetRasterData(_heldImage);
        _heldTile = null; _heldImage = null;
        if (_fetcher != null) Object.DestroyImmediate(_fetcher);
        if (_tile != null) Object.DestroyImmediate(_tile.gameObject);
        if (_material != null) Object.DestroyImmediate(_material);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/map-raster-loading-checks.txt", status + ": " + _count + " checks\n" + Report);
        Debug.Log("[MapRasterLoadingChecks] " + status + ": " + _count + " checks.");
    }
    private static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); _count++; Report.AppendLine("PASS: " + message); }

    private static void InitializeTile(AbstractMap map, CanonicalTileId id)
    {
        typeof(UnityTile).GetMethod("Initialize", Hidden).Invoke(_tile, new object[] {
            map, new UnwrappedTileId(id.Z,id.X,id.Y), map.WorldRelativeScale, map.AbsoluteZoom, null });
        _tile.transform.position = new Vector3(0, -100000, 0);
        typeof(UnityTile).GetProperty("RasterDataState").SetValue(_tile, TilePropertyState.Loading);
    }
    private static void Fetch(string style = Satellite)
    {
        typeof(UnityTile).GetProperty("RasterDataState").SetValue(_tile, TilePropertyState.Loading);
        _fetcher.FetchData(new ImageDataFetcherParameters { tile = _tile,
            canonicalTileId = _tile.CanonicalTileId, tilesetId = style, useRetina = false });
    }
    private static IEnumerator Checks()
    {
        var map = Object.FindObjectOfType<AbstractMap>();
        Check(map != null, "Real scene map found");
        var texture = new Texture2D(8,8);
        for (int y=0;y<8;y++) for(int x=0;x<8;x++)
            texture.SetPixel(x,y, y<4 ? (x<4 ? Color.red : Color.green) : (x<4 ? Color.blue : Color.yellow));
        texture.Apply(); _image = texture.EncodeToPNG(); Object.DestroyImmediate(texture);
        _tile = new GameObject("Raster loading test (temporary)").AddComponent<UnityTile>();
        _material = new Material(Shader.Find("Unlit/Texture"));
        _tile.MeshRenderer.sharedMaterial = _material;
        _fetcher = ScriptableObject.CreateInstance<ImageDataFetcher>();
        _source = new FakeSource(); _fetcher.Initialize(_source);
        _fetcher.DataRecieved += (tile, raster) => tile.SetRasterData(raster.Data);
        _fetcher.FetchingError += (tile, raster, error) =>
            typeof(UnityTile).GetProperty("RasterDataState").SetValue(tile, TilePropertyState.Error);
        var id = new CanonicalTileId(6,13,10);
        InitializeTile(map,id);
        _source.Reply = (tid, style, count) => tid.Z < id.Z ? Response.FromCache(_image) : null;
        Fetch(); yield return .25f;
        Check(_tile.HasRasterVisual && _tile.MeshRenderer.enabled, "Delayed detail uses actual parent imagery");
        Check(_tile.RasterDataState == TilePropertyState.Loading, "Fallback is not reported as full detail");
        Check(_tile.RasterVisualZoom == 4 && _material.mainTextureScale == new Vector2(.25f,.25f), "Parent resolution and UV scale correct");
        Check(_material.mainTextureOffset == new Vector2(.25f,.5f), "Parent crop matches XYZ tile location");
        var before = _tile.GetRasterData();
        _tile.SetRasterData(new byte[0]);
        Check(_tile.GetRasterData() == before && _tile.HasRasterVisual, "Invalid image cannot erase a valid fallback");
        _fetcher.Cancel(_tile);
        _source.Reset(); _source.Reply = (tid, style, count) =>
            tid.Z < id.Z || count >= 3 ? Response.FromCache(_image) : Failure();
        Fetch(); yield return 3.7f;
        Check(_tile.RasterDataState == TilePropertyState.Loaded && _source.Count(id,Satellite) == 3, "Two failed downloads recover automatically on third attempt");
        Check(_material.mainTextureScale == Vector2.one && _material.mainTextureOffset == Vector2.zero, "Full detail resets parent UV crop");
        CheckTerrainFallback(map);
        int sent = _source.Total;
        Fetch(); yield return .15f;
        Check(_tile.RasterDataState == TilePropertyState.Loaded && _source.Total == sent, "Repeated tile uses validated session cache without a new request");

        _source.Reset(); _source.Reply = (tid, style, count) => null;
        Fetch(Streets); yield return .15f;
        Check(!_tile.HasRasterVisual && !_tile.MeshRenderer.enabled, "New style never displays old style or bare terrain");
        var late = _source.Pending.ToArray();
        Fetch(Satellite); yield return .15f;
        Check(_tile.RasterDataState == TilePropertyState.Loaded, "Switching back restores cached imagery immediately");
        var current = _tile.GetRasterData();
        foreach (var pending in late) pending.Callback(Response.FromCache(_image));
        yield return .15f;
        Check(_tile.GetRasterData() == current, "Late response from cancelled style cannot overwrite current imagery");

        _fetcher.Cancel(_tile);
        typeof(UnityTile).GetMethod("Recycle",Hidden).Invoke(_tile,null);
        InitializeTile(map,new CanonicalTileId(6,40,40));
        _source.Reset(); _source.Reply = (tid, style, count) => Failure();
        Fetch(); yield return 7.8f;
        Check(_source.Count(_tile.CanonicalTileId,Satellite) == 4, "Persistent failure stops after four attempts");
        Check(_tile.RasterDataState == TilePropertyState.Error, "Exhausted retries expose a real error state");
        Check(!_tile.HasRasterVisual && !_tile.MeshRenderer.enabled, "Recycled tile never shows an unrelated image or brown placeholder");

        _fetcher.Cancel(_tile);
        InitializeTile(map,new CanonicalTileId(6,50,40));
        _source.Reset(); _source.Reply = (tid, style, count) => tid.Z < 6 || count > 1 ? Response.FromCache(_image) : null;
        Fetch(); yield return 16.8f;
        Check(_source.Count(_tile.CanonicalTileId,Satellite) == 2 && _tile.RasterDataState == TilePropertyState.Loaded,
            "Hung download times out and recovers on the next attempt");
        Check(_source.Pending.Any(p => p.Cancelled), "Timed-out network request is cancelled");

        float deadline = Time.realtimeSinceStartup + 30f;
        while(map.MapVisualizer.ActiveTiles.Values.Any(t => t.RasterDataState == TilePropertyState.Loading)
            && Time.realtimeSinceStartup < deadline) yield return .5f;
        var active = map.MapVisualizer.ActiveTiles.Values.ToArray();
        Check(active.Length > 0, "Live map has active tiles");
        int blank = active.Count(t => t.MeshRenderer.enabled && !t.HasRasterVisual && t.RasterDataState != TilePropertyState.None);
        Check(blank == 0, "Live map has no visible untextured tiles");
        Check(active.All(t => t.MeshFilter.sharedMesh.vertexCount >= 3 && t.MeshFilter.sharedMesh.GetIndexCount(0) >= 3),
            "Every live tile has drawable geometry, including tiles whose elevation download failed");
        Report.AppendLine("Live map: " + active.Length + " tiles, " + active.Count(t=>t.RasterDataState==TilePropertyState.Loaded)
            + " full detail, " + active.Count(t=>t.HasRasterVisual&&t.RasterDataState!=TilePropertyState.Loaded) + " fallback.");
        var continuity = Object.FindObjectOfType<GroundStation.Map.MapImageContinuity>();
        Check(continuity != null && continuity.HasCompleteFrame && !continuity.IsHoldingLastFrame,
            "Map camera retains a complete image for transitions");
        var camera = continuity.GetComponent<Camera>();
        var planes = GeometryUtility.CalculateFrustumPlanes(camera);
        var visible = active.First(t => map.CurrentExtent.Contains(t.UnwrappedTileId) && t.HasRasterVisual && GeometryUtility.TestPlanesAABB(planes,t.MeshRenderer.bounds));
        var savedMesh = visible.MeshFilter.sharedMesh;
        var emptyMesh = new Mesh();
        try
        {
            visible.MeshFilter.sharedMesh = emptyMesh;
            yield return .2f;
            Check(continuity.IsHoldingLastFrame, "Loaded imagery with an empty terrain mesh cannot replace the last complete frame");
        }
        finally { visible.MeshFilter.sharedMesh = savedMesh; Object.DestroyImmediate(emptyMesh); }
        try
        {
            if (!map.MapVisualizer.ActiveTiles.Remove(visible.UnwrappedTileId)) throw new Exception("Missing-tile fixture was not installed");
            // Render immediately: the periodic extent refresh can otherwise repair
            // this deliberately missing entry from cache during the yielded frame.
            camera.Render();
            Check(continuity.IsHoldingLastFrame, "A requested tile absent from the active dictionary is detected as missing coverage");
        }
        finally { map.MapVisualizer.ActiveTiles[visible.UnwrappedTileId] = visible; }
        try
        {
            visible.MeshRenderer.enabled = false;
            yield return .2f;
            Check(continuity.IsHoldingLastFrame, "A disabled tile renderer cannot certify a complete map image");
        }
        finally { visible.MeshRenderer.enabled = true; }
        try
        {
            visible.MeshRenderer.sharedMaterial.mainTexture = null;
            yield return .2f;
            Check(continuity.IsHoldingLastFrame, "A missing material texture is detected even when the download flag says loaded");
        }
        finally { visible.MeshRenderer.sharedMaterial.mainTexture = visible.GetRasterData(); }
        yield return .3f;
        _heldTile = active.First(t => t.HasRasterVisual && GeometryUtility.TestPlanesAABB(planes,t.MeshRenderer.bounds));
        _heldImage = _heldTile.GetRasterData().EncodeToPNG();
        _heldTile.PrepareRaster("temporary missing image test");
        typeof(UnityTile).GetProperty("RasterDataState").SetValue(_heldTile,TilePropertyState.Loading);
        yield return .3f;
        Check(continuity.IsHoldingLastFrame, "A missing visible tile retains the complete frame instead of exposing the brown ground");
        Check(GroundStation.Map.MapImageContinuity.IsUpdating, "Map picking is blocked while its displayed image is waiting");
        ScreenCapture.CaptureScreenshot("Logs/map-raster-loading-held-preview.png");
        yield return .2f;
        _heldTile.SetRasterData(_heldImage); _heldTile = null; _heldImage = null;
        yield return .3f;
        Check(!continuity.IsHoldingLastFrame && !GroundStation.Map.MapImageContinuity.IsUpdating,
            "Complete imagery restores map display and interaction automatically");
        ScreenCapture.CaptureScreenshot("Logs/map-raster-loading-preview.png");
        yield return .3f;
    }
    private static void CheckTerrainFallback(AbstractMap map)
    {
        var options = new ElevationLayerProperties();
        options.modificationOptions.sampleCount = 10;
        options.colliderOptions.addCollider = true;
        var terrain = new ElevatedTerrainStrategy(); terrain.Initialize(options);
        typeof(UnityTile).GetProperty("HeightDataState").SetValue(_tile, TilePropertyState.Loading);
        terrain.PrepareTile(_tile);
        Check(_tile.MeshFilter.sharedMesh.vertexCount == 100 && _tile.HeightDataState == TilePropertyState.Loading,
            "Pending elevation has a drawable flat grid without claiming elevation is loaded");
        _tile.MeshFilter.sharedMesh.Clear();
        terrain.DataErrorOccurred(_tile, null); // Reproduce failure before the first mesh was built.
        var mesh = _tile.MeshFilter.sharedMesh;
        Check(mesh.vertexCount == 100 && mesh.GetIndexCount(0) == 486 && mesh.uv.Length == 100,
            "Failed first elevation download assigns actual geometry and imagery UVs");
        Check(_tile.GetComponent<MeshCollider>().sharedMesh == mesh && mesh.bounds.size.x > 0 && mesh.bounds.size.z > 0,
            "Elevation fallback has valid bounds and the matching picking collider");
        CheckFallbackPixels();
        for (int pass = 0; pass < 3; pass++)
        {
            _tile.HeightData = Enumerable.Repeat(12f, 256 * 256).ToArray();
            typeof(UnityTile).GetProperty("HeightDataState").SetValue(_tile, TilePropertyState.Loaded);
            terrain.RegisterTile(_tile);
            Check(_tile.ElevationType == TileTerrainType.Elevated &&
                Mathf.Abs(_tile.MeshFilter.sharedMesh.vertices[0].y - 12f * _tile.TileScale) < .01f,
                "Elevation data replaces the flat fallback after reuse " + pass);
            terrain.UnregisterTile(_tile);
            typeof(UnityTile).GetMethod("Recycle", Hidden).Invoke(_tile, null);
            InitializeTile(map, new CanonicalTileId(6, 14 + pass, 10));
            typeof(UnityTile).GetProperty("HeightDataState").SetValue(_tile, TilePropertyState.Loading);
            terrain.PrepareTile(_tile);
            Check(_tile.MeshFilter.sharedMesh.vertices.All(v => Mathf.Approximately(v.y, 0)) && _tile.HeightData.All(h => h == 0),
                "Recycled tile does not reuse elevation from the previous location " + pass);
        }
        InitializeTile(map, new CanonicalTileId(6,13,10));
        _tile.SetRasterData(_image);
    }
    private static void CheckFallbackPixels()
    {
        var go = new GameObject("Terrain fallback render check (temporary)");
        var target = new RenderTexture(64, 64, 24);
        var pixels = new Texture2D(64, 64, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        int layer = _tile.gameObject.layer;
        try
        {
            _tile.gameObject.layer = 31;
            var bounds = _tile.MeshRenderer.bounds;
            float size = Mathf.Max(bounds.size.x, bounds.size.z);
            var camera = go.AddComponent<Camera>(); camera.enabled = false;
            camera.orthographic = true; camera.orthographicSize = size * .5f;
            camera.transform.position = bounds.center + Vector3.up * (size * 2);
            camera.transform.rotation = Quaternion.Euler(90,0,0);
            camera.nearClipPlane = .1f; camera.farClipPlane = size * 4;
            camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.4f,.38f,.36f); camera.targetTexture = target;
            camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0,0,64,64),0,0); pixels.Apply();
            bool colored = true;
            foreach (int x in new[] { 12, 48 }) foreach (int y in new[] { 12, 48 })
            {
                Color pixel = pixels.GetPixel(x,y);
                colored &= Mathf.Max(pixel.r,pixel.g,pixel.b) > .8f && Mathf.Min(pixel.r,pixel.g,pixel.b) < .15f;
            }
            Check(colored, "Rendered pixels show imagery in all four quadrants after an elevation failure");
            File.WriteAllBytes("Logs/map-terrain-fallback-render.png", pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; _tile.gameObject.layer = layer;
            Object.DestroyImmediate(go); target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
        }
    }
    private static Response Failure()
    { var r = Response.FromCache(null); r.AddException(new IOException("Simulated connection reset")); return r; }
    private sealed class FakeRequest : IAsyncRequest
    {
        public bool Cancelled;
        public bool IsCompleted { get; set; }
        public HttpRequestType RequestType => HttpRequestType.Get;
        public Action<Response> Callback;
        public void Cancel() { Cancelled = true; }
    }
    private sealed class FakeSource : IFileSource
    {
        public Func<CanonicalTileId,string,int,Response> Reply;
        public readonly List<FakeRequest> Pending = new List<FakeRequest>();
        private readonly Dictionary<string,int> _counts = new Dictionary<string,int>();
        public int Total;
        public int Count(CanonicalTileId id,string style) => _counts.TryGetValue(style+id,out int n)?n:0;
        public void Reset() { Total=0; _counts.Clear(); Pending.Clear(); }
        public IAsyncRequest Request(string uri, Action<Response> callback, int timeout=10,
            CanonicalTileId tileId=default, string tilesetId=null)
        {
            int count=Count(tileId,tilesetId)+1;_counts[tilesetId+tileId]=count;Total++;
            var request=new FakeRequest{Callback=callback};
            var response=Reply(tileId,tilesetId,count);
            if(response==null) Pending.Add(request);
            else {request.IsCompleted=true;callback(response);}
            return request;
        }
    }
}
#endif
