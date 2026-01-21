namespace VectorMap.Core.Tiles;

/// <summary>
/// Represents parsed data from a vector tile
/// </summary>
public class TileData
{
    public TileCoordinate Coordinate { get; set; }
    public List<FeatureSet> FeatureSets { get; set; } = new();
    public bool IsLoaded { get; set; }
    public bool IsLoading { get; set; }
    public DateTime LoadedAt { get; set; }
}

/// <summary>
/// A set of vertices for a specific layer and geometry type
/// </summary>
public class FeatureSet
{
    public string LayerName { get; set; } = string.Empty;
    public GeometryType Type { get; set; }
    public float[] Vertices { get; set; } = Array.Empty<float>();
}

/// <summary>
/// Types of geometry in vector tiles
/// </summary>
public enum GeometryType
{
    Unknown = 0,
    Point = 1,
    Line = 2,
    Polygon = 3
}
