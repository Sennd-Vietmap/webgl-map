using VectorMap.Core.Projection;

namespace VectorMap.Core.Tiles;

/// <summary>
/// Manages tile loading, caching, and viewport calculations
/// </summary>
public class TileManager
{
    private readonly Dictionary<string, TileData> _tileCache = new();
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
        foreach (var key in tilesToLoad)
        {
            if (!_tileCache.ContainsKey(key))
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
            
            _tileCache[key] = tileData;
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
        return _tileCache.TryGetValue(tile.ToKey(), out var data) ? data : null;
    }
    
    /// <summary>
    /// Get a placeholder tile (parent or children) if the requested tile isn't loaded
    /// </summary>
    public TileData? GetPlaceholderTile(TileCoordinate tile)
    {
        // Try parent first
        var parent = tile.GetParent();
        if (_tileCache.TryGetValue(parent.ToKey(), out var parentData) && parentData.IsLoaded)
        {
            return parentData;
        }
        
        // Try children
        foreach (var child in tile.GetChildren())
        {
            if (_tileCache.TryGetValue(child.ToKey(), out var childData) && childData.IsLoaded)
            {
                return childData;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Get tiles that should be rendered for the current view
    /// </summary>
    public IEnumerable<TileData> GetRenderableTiles()
    {
        foreach (var tile in TilesInView)
        {
            var tileData = GetTile(tile);
            if (tileData != null && tileData.IsLoaded)
            {
                yield return tileData;
            }
            else
            {
                // Use placeholder
                var placeholder = GetPlaceholderTile(tile);
                if (placeholder != null)
                {
                    yield return placeholder;
                }
            }
        }
    }
    
    /// <summary>
    /// Clear old tiles from cache to prevent memory bloat
    /// </summary>
    public void PruneCache(int maxAge = 300)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-maxAge);
        var keysToRemove = _tileCache
            .Where(kvp => kvp.Value.LoadedAt < cutoff && !TilesInView.Any(t => t.ToKey() == kvp.Key))
            .Select(kvp => kvp.Key)
            .ToList();
            
        foreach (var key in keysToRemove)
        {
            _tileCache.Remove(key);
        }
    }
}
