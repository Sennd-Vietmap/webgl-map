using VectorMap.Core.Projection;

namespace VectorMap.Core.Tiles;

/// <summary>
/// Manages tile loading, caching, and viewport calculations
/// </summary>
public class TileManager
{
    private readonly Dictionary<string, TileData> _tileCache = new();
    private readonly object _cacheLock = new object();
    private readonly VectorTileParser _parser;
    private readonly HttpClient _httpClient;
    private readonly string _tileServerUrl;
    private readonly int _maxTileZoom;
    private readonly int _tileBuffer;
    
    public List<TileCoordinate> TilesInView { get; private set; } = new();
    public event Action<TileCoordinate, TileData>? TileLoaded;
    
    public TileManager(
        string tileServerUrl,
        IEnumerable<string> layers,
        int maxTileZoom = 14,
        int tileBuffer = 1)
    {
        _tileServerUrl = tileServerUrl;
        _maxTileZoom = maxTileZoom;
        _tileBuffer = tileBuffer;
        _parser = new VectorTileParser(layers);
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "VectorMap.Desktop/1.0");
    }
    
    /// <summary>
    /// Update the viewport and determine which tiles to load
    /// </summary>
    public void UpdateViewport(BoundingBox bounds, double zoom)
    {
        int z = Math.Min((int)Math.Truncate(zoom), _maxTileZoom);
        
        var minTile = TileCoordinate.FromLngLat(bounds.MinLng, bounds.MaxLat, z);
        var maxTile = TileCoordinate.FromLngLat(bounds.MaxLng, bounds.MinLat, z);
        
        // Calculate tiles visible in viewport
        // Check usage of TilesInView to see if it needs locking (it is only used on main thread in UpdateViewport and GetRenderableTiles? 
        // UpdateViewport is called from MapWindow.OnRenderFrame (Main Thread).
        // GetRenderableTiles is called from MapWindow.OnRenderFrame (Main Thread).
        // So TilesInView is safe.
        
        TilesInView.Clear();
        int minX = Math.Max(minTile.X, 0);
        int maxX = maxTile.X;
        int minY = Math.Max(minTile.Y, 0);
        int maxY = maxTile.Y;
        
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                TilesInView.Add(new TileCoordinate(x, y, z));
            }
        }
        
        // Get tiles to load including buffer
        var tilesToLoad = new HashSet<string>();
        int n = 1 << z;
        
        for (int x = minX - _tileBuffer; x <= maxX + _tileBuffer; x++)
        {
            for (int y = minY - _tileBuffer; y <= maxY + _tileBuffer; y++)
            {
                if (x >= 0 && x < n && y >= 0 && y < n)
                {
                    var tile = new TileCoordinate(x, y, z);
                    tilesToLoad.Add(tile.ToKey());
                    
                    // Also get parent tiles for fallback
                    var parent = tile.GetParent();
                    if (parent.IsValid())
                    {
                        tilesToLoad.Add(parent.ToKey());
                        var grandparent = parent.GetParent();
                        if (grandparent.IsValid())
                        {
                            tilesToLoad.Add(grandparent.ToKey());
                        }
                    }
                }
            }
        }
        
        // Load tiles that aren't cached
        // Load tiles that aren't cached
        foreach (var key in tilesToLoad)
        {
            bool isCached;
            lock (_cacheLock)
            {
                isCached = _tileCache.ContainsKey(key);
            }
            
            if (!isCached)
            {
                var parts = key.Split('/');
                var tile = new TileCoordinate(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
                _ = LoadTileAsync(tile);
            }
        }
    }
    
    /// <summary>
    /// Load a tile asynchronously from the tile server
    /// </summary>
    public async Task LoadTileAsync(TileCoordinate tile)
    {
        string key = tile.ToKey();
        
        // Mark as loading
        _tileCache[key] = new TileData
        {
            Coordinate = tile,
            IsLoaded = false
        };
        
        try
        {
            string url = _tileServerUrl
                .Replace("{x}", tile.X.ToString())
                .Replace("{y}", tile.Y.ToString())
                .Replace("{z}", tile.Z.ToString());
                
            var response = await _httpClient.GetByteArrayAsync(url);
            var featureSets = _parser.Parse(response, tile);
            
            var tileData = new TileData
            {
                Coordinate = tile,
                FeatureSets = featureSets,
                IsLoaded = true,
                LoadedAt = DateTime.UtcNow
            };
            
            lock (_cacheLock)
            {
                _tileCache[key] = tileData;
            }
            TileLoaded?.Invoke(tile, tileData);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load tile {key}: {ex.Message}");
            _tileCache.Remove(key);
        }
    }
    
    /// <summary>
    /// Get tile data from cache
    /// </summary>
    public TileData? GetTile(TileCoordinate tile)
    {
        string key = tile.ToKey();
        TileData? data;
        lock (_cacheLock)
        {
            _tileCache.TryGetValue(key, out data);
        }
        return data;
    }
    
    /// <summary>
    /// Get a placeholder tile (parent or children) if the requested tile isn't loaded
    /// </summary>
    public TileData? GetPlaceholderTile(TileCoordinate tile)
    {
        // Try parent
        var parent = tile.GetParent();
        if (GetTile(parent) is { IsLoaded: true } parentData)
        {
            return parentData;
        }
        
        // Try children
        foreach (var child in tile.GetChildren())
        {
            if (GetTile(child) is { IsLoaded: true } childData)
            {
                return childData;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Get tiles that should be rendered for the current view
    /// Support over-zooming by falling back to parent tiles
    /// </summary>
    public IEnumerable<TileData> GetRenderableTiles()
    {
        // Keep track of which areas are covered to avoid drawing multiple layers of tiles
        // We actually just yield the best available tile for each slot.
        // Since we iterate X/Y/Z tiles in view, we need to map them to possibly parent tiles. 
        // Use a HashSet to avoid yielding the same parent tile multiple times for different children.
        var yieldedTiles = new HashSet<string>();
        
        foreach (var tile in TilesInView)
        {
            TileData? tileData = null;
            
            // 1. Try exact match
            tileData = GetTile(tile);
            
            // 2. If not found or not loaded, try simple parent (standard logic) -> actually standard logic is just exact match
            // But for over-zooming (e.g. at Z16 looking for Z14), we want to walk UP.
            
            var current = tile;
            while ((tileData == null || !tileData.IsLoaded) && current.Z > 0)
            {
                // Try to get data for current
                tileData = GetTile(current);
                
                if (tileData != null && tileData.IsLoaded)
                {
                    // Found a loaded tile (either exact or parent)
                    break;
                }
                
                // Not found, move up
                current = current.GetParent();
            }
            
            if (tileData != null && tileData.IsLoaded)
            {
                string key = tileData.Coordinate.ToKey();
                if (!yieldedTiles.Contains(key))
                {
                    yieldedTiles.Add(key);
                    yield return tileData;
                }
            }
        }
    }
    
    /// <summary>
    /// Clear old tiles from cache to prevent memory bloat
    /// </summary>
    public void PruneCache(int maxAge = 300)
    {
        var now = DateTime.UtcNow;
        lock (_cacheLock)
        {
            var keysToRemove = _tileCache.Where(kvp => (now - kvp.Value.LoadedAt).TotalSeconds > maxAge).Select(kvp => kvp.Key).ToList();
            
            foreach (var key in keysToRemove)
            {
                _tileCache.Remove(key);
            }
        }
    }
}
