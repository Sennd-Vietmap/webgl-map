namespace VectorMap.Core.Tiles;

/// <summary>
/// Represents parsed data from a vector tile
/// </summary>
public class TileData
{
    public TileCoordinate Coordinate { get; set; }
    public List<FeatureSet> FeatureSets { get; set; } = new();
    public List<LabelInfo> Labels { get; set; } = new();
    public bool IsLoaded { get; set; }
    public bool IsLoading { get; set; }
    public DateTime LoadedAt { get; set; }
}

/// <summary>
/// Information for a text label
/// </summary>
public class LabelInfo
{
    public string Text { get; set; } = string.Empty;
    public double X { get; set; } // Global Mercator
    public double Y { get; set; } // Global Mercator
    public string LayerName { get; set; } = string.Empty;
    public float Priority { get; set; } = 0.0f;
}

/// <summary>
/// A set of vertices for a specific layer and geometry type
/// </summary>
public class FeatureSet
{
    public TileCoordinate Coordinate { get; set; } // The tile this feature set belongs to
    public string LayerName { get; set; } = string.Empty;
    public GeometryType Type { get; set; }
    public float[] Vertices { get; set; } = Array.Empty<float>();
    public uint[] Indices { get; set; } = Array.Empty<uint>();
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
