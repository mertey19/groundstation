using Mapbox.Map;
using Mapbox.Platform;
using Mapbox.Unity.MeshGeneration.Data;
using Mapbox.Unity.MeshGeneration.Enums;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ImageDataFetcher : DataFetcher
{
    public Action<UnityTile, RasterTile> DataRecieved = (t, s) => { };
    public Action<UnityTile, RasterTile, TileErrorEventArgs> FetchingError = (t, r, s) => { };
    private const int MaxAttempts = 4;
    private const float RequestTimeout = 15f;
    private const int CacheLimit = 128;
    private const int CacheByteLimit = 32 * 1024 * 1024;
    private IFileSource _source;

    private sealed class Request
    {
        public UnityTile Tile;
        public CanonicalTileId Id;
        public string Source;
        public bool Retina;
        public Coroutine Routine;
        public RasterTile Raster;
        public bool ParentApplied;
    }
    private sealed class CachedImage
    {
        public byte[] Data;
        public long Used;
    }
    private sealed class ParentRequest
    {
        public RasterTile Raster;
        public bool Done;
        public float Started;
    }
    private readonly Dictionary<UnityTile, Request> _requests = new Dictionary<UnityTile, Request>();
    private readonly Dictionary<string, CachedImage> _cache = new Dictionary<string, CachedImage>();
    private readonly Dictionary<string, ParentRequest> _parents = new Dictionary<string, ParentRequest>();
    private long _stamp;
    private int _cacheBytes;

    public void Initialize(IFileSource source) { _source = source; }
    private static string Key(string source, bool retina, CanonicalTileId id)
        => source + (retina ? "@2x/" : "@1x/") + id;
    private static CanonicalTileId Parent(CanonicalTileId id, int levels)
        => new CanonicalTileId(id.Z - levels, id.X >> levels, id.Y >> levels);

    public override void FetchData(DataFetcherParameters parameters)
    {
        var p = parameters as ImageDataFetcherParameters;
        if (p == null || p.tile == null || string.IsNullOrEmpty(p.tilesetId)) return;
        Cancel(p.tile);
        var request = new Request { Tile = p.tile, Id = p.tile.CanonicalTileId,
            Source = p.tilesetId, Retina = p.useRetina };
        p.tile.PrepareRaster(Key(p.tilesetId, p.useRetina, request.Id));
        string key = Key(request.Source, request.Retina, request.Id);
        if (TryCache(key, out byte[] data))
        {
            p.tile.SetRasterData(data);
            if (p.tile.RasterDataState == TilePropertyState.Loaded) return;
            RemoveCached(key);
        }
        p.tile.RasterDataState = TilePropertyState.Loading;
        TryFallback(request);
        _requests[p.tile] = request;
        // Editor map previews cannot run MonoBehaviour coroutines.
        if (!Application.isPlaying)
        {
            request.Raster = NewRaster(request.Source, request.Retina);
            request.Raster.Initialize(_source ?? _fileSource, request.Id, request.Source, () =>
            {
                if (!IsCurrent(request)) return;
                if (!request.Raster.HasError) DataRecieved(request.Tile, request.Raster);
                else ReportError(request);
            });
            return;
        }
        TrimParents();
        request.Routine = p.tile.StartCoroutine(Load(request));
    }

    private IEnumerator Load(Request request)
    {
        ParentRequest parent = null;
        int parentLevels = Math.Min(2, request.Id.Z);
        CanonicalTileId parentId = Parent(request.Id, parentLevels);
        if (!request.Tile.HasRasterVisual && parentLevels > 0)
        {
            string parentKey = Key(request.Source, request.Retina, parentId);
            if (!_parents.TryGetValue(parentKey, out parent))
            {
                parent = new ParentRequest { Raster = NewRaster(request.Source, request.Retina), Started = Time.realtimeSinceStartup };
                _parents[parentKey] = parent;
                var pending = parent;
                pending.Raster.Initialize(_source ?? _fileSource, parentId, request.Source, () => pending.Done = true);
            }
        }
        for (int attempt = 0; attempt < MaxAttempts && IsCurrent(request); attempt++)
        {
            request.Tile.RasterDataState = TilePropertyState.Loading;
            bool done = false;
            var raster = NewRaster(request.Source, request.Retina);
            request.Raster = raster;
            raster.Initialize(_source ?? _fileSource, request.Id, request.Source, () => done = true);
            float deadline = Time.realtimeSinceStartup + RequestTimeout;
            while (!done && IsCurrent(request) && Time.realtimeSinceStartup < deadline)
            {
                ApplyParent(request, parent, parentId);
                yield return null;
            }
            if (!IsCurrent(request)) yield break;
            ApplyParent(request, parent, parentId);
            if (done && !raster.HasError && raster.Data != null && raster.Data.Length > 0)
            {
                DataRecieved(request.Tile, raster);
                if (request.Tile.RasterDataState == TilePropertyState.Loaded)
                {
                    StoreCached(Key(request.Source, request.Retina, request.Id), raster.Data);
                    _requests.Remove(request.Tile);
                    yield break;
                }
            }
            raster.Cancel();
            if (attempt + 1 < MaxAttempts)
            {
                request.Tile.RasterDataState = TilePropertyState.Loading;
                yield return new WaitForSecondsRealtime(1 << attempt);
            }
        }
        if (IsCurrent(request))
        {
            ReportError(request);
            _requests.Remove(request.Tile);
        }
    }

    private void ApplyParent(Request request, ParentRequest parent, CanonicalTileId id)
    {
        if (request.ParentApplied || parent == null || !parent.Done || parent.Raster.HasError || !IsCurrent(request)) return;
        request.ParentApplied = true;
        if (request.Tile.SetRasterFallback(parent.Raster.Data, id))
            StoreCached(Key(request.Source, request.Retina, id), parent.Raster.Data);
    }

    private void TryFallback(Request request)
    {
        for (int levels = 1; levels <= Math.Min(6, request.Id.Z); levels++)
        {
            var parent = Parent(request.Id, levels);
            string key = Key(request.Source, request.Retina, parent);
            if (TryCache(key, out byte[] data) && request.Tile.SetRasterFallback(data, parent)) return;
        }
    }

    private bool IsCurrent(Request request)
        => request.Tile != null && !request.Tile.IsRecycled && request.Tile.CanonicalTileId == request.Id
        && _requests.TryGetValue(request.Tile, out Request current) && ReferenceEquals(current, request);

    private void ReportError(Request request)
    {
        var errors = request.Raster.Exceptions ?? new List<Exception> {
            new InvalidOperationException("Map image unavailable after bounded retries.") }.AsReadOnly();
        FetchingError(request.Tile, request.Raster,
            new TileErrorEventArgs(request.Id, request.Raster.GetType(), request.Tile, errors));
    }

    private static RasterTile NewRaster(string source, bool retina)
    {
        if (source.StartsWith("mapbox://", StringComparison.Ordinal))
            return retina ? new RetinaRasterTile() : new RasterTile();
        return retina ? new ClassicRetinaRasterTile() : new ClassicRasterTile();
    }

    public void Cancel(UnityTile tile)
    {
        if (tile == null || !_requests.TryGetValue(tile, out Request request)) return;
        _requests.Remove(tile); // Invalidate callbacks before cancelling the transport.
        if (request.Routine != null) tile.StopCoroutine(request.Routine);
        if (request.Raster != null) request.Raster.Cancel();
    }

    private bool TryCache(string key, out byte[] data)
    {
        if (_cache.TryGetValue(key, out CachedImage image))
        {
            image.Used = ++_stamp;
            data = image.Data;
            return true;
        }
        data = null;
        return false;
    }
    private void StoreCached(string key, byte[] data)
    {
        if (data == null || data.Length == 0 || data.Length > CacheByteLimit) return;
        RemoveCached(key);
        _cache[key] = new CachedImage { Data = data, Used = ++_stamp };
        _cacheBytes += data.Length;
        while (_cache.Count > CacheLimit || _cacheBytes > CacheByteLimit)
        {
            string oldest = null;
            long stamp = long.MaxValue;
            foreach (var item in _cache)
                if (item.Value.Used < stamp) { oldest = item.Key; stamp = item.Value.Used; }
            RemoveCached(oldest);
        }
    }
    private void RemoveCached(string key)
    {
        if (key != null && _cache.TryGetValue(key, out CachedImage old))
        { _cacheBytes -= old.Data.Length; _cache.Remove(key); }
    }
    private void TrimParents()
    {
        var expired = new List<string>();
        foreach (var item in _parents)
            if (Time.realtimeSinceStartup - item.Value.Started > RequestTimeout)
            { item.Value.Raster.Cancel(); expired.Add(item.Key); }
        foreach (string key in expired) _parents.Remove(key);
    }
    private void OnDestroy()
    {
        foreach (var tile in new List<UnityTile>(_requests.Keys)) Cancel(tile);
        foreach (var parent in _parents.Values) parent.Raster.Cancel();
        _parents.Clear(); _cache.Clear(); _cacheBytes = 0;
    }
}
