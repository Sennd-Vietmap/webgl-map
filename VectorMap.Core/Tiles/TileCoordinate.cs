namespace VectorMap.Core.Tiles;

/// <summary>
/// Represents a tile coordinate in the XYZ tile scheme
/// </summary>
public readonly struct TileCoordinate : IEquatable<TileCoordinate>
{
    public int X { get; }
    public int Y { get; }
    public int Z { get; }

    public TileCoordinate(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>
    /// Create tile coordinate from longitude/latitude at a given zoom level
    /// </summary>
    public static TileCoordinate FromLngLat(double lng, double lat, int zoom)
    {
        int n = 1 << zoom;
        int x = (int)Math.Floor((lng + 180.0) / 360.0 * n);
        int y = (int)Math.Floor((1.0 - Math.Log(Math.Tan(lat * Math.PI / 180.0) + 1.0 / Math.Cos(lat * Math.PI / 180.0)) / Math.PI) / 2.0 * n);
        
        // Clamp values
        x = Math.Max(0, Math.Min(n - 1, x));
        y = Math.Max(0, Math.Min(n - 1, y));
        
        return new TileCoordinate(x, y, zoom);
    }

    /// <summary>
    /// Get the parent tile at zoom level Z-1
    /// </summary>
    public TileCoordinate GetParent()
    {
        if (Z == 0) return this;
        return new TileCoordinate(X / 2, Y / 2, Z - 1);
    }

    /// <summary>
    /// Get the 4 child tiles at zoom level Z+1
    /// </summary>
    public TileCoordinate[] GetChildren()
    {
        int childX = X * 2;
        int childY = Y * 2;
        int childZ = Z + 1;
        
        return new[]
        {
            new TileCoordinate(childX, childY, childZ),
            new TileCoordinate(childX + 1, childY, childZ),
            new TileCoordinate(childX, childY + 1, childZ),
            new TileCoordinate(childX + 1, childY + 1, childZ)
        };
    }

    /// <summary>
    /// Get the bounding box of this tile in longitude/latitude
    /// </summary>
    public BoundingBox ToBoundingBox()
    {
        int n = 1 << Z;
        double minLng = X / (double)n * 360.0 - 180.0;
        double maxLng = (X + 1) / (double)n * 360.0 - 180.0;
        double minLat = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * (Y + 1) / (double)n))) * 180.0 / Math.PI;
        double maxLat = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * Y / (double)n))) * 180.0 / Math.PI;
        
        return new BoundingBox(minLng, minLat, maxLng, maxLat);
    }

    /// <summary>
    /// Check if this tile is valid (within bounds for its zoom level)
    /// </summary>
    public bool IsValid()
    {
        int n = 1 << Z;
        return X >= 0 && X < n && Y >= 0 && Y < n && Z >= 0;
    }

    public override string ToString() => $"{X}/{Y}/{Z}";
    
    public string ToKey() => $"{X}/{Y}/{Z}";

    public bool Equals(TileCoordinate other) => X == other.X && Y == other.Y && Z == other.Z;

    public override bool Equals(object? obj) => obj is TileCoordinate other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    public static bool operator ==(TileCoordinate left, TileCoordinate right) => left.Equals(right);

    public static bool operator !=(TileCoordinate left, TileCoordinate right) => !left.Equals(right);
}

/// <summary>
/// Represents a geographic bounding box
/// </summary>
public readonly struct BoundingBox
{
    public double MinLng { get; }
    public double MinLat { get; }
    public double MaxLng { get; }
    public double MaxLat { get; }

    public BoundingBox(double minLng, double minLat, double maxLng, double maxLat)
    {
        MinLng = minLng;
        MinLat = minLat;
        MaxLng = maxLng;
        MaxLat = maxLat;
    }

    public double Width => MaxLng - MinLng;
    public double Height => MaxLat - MinLat;
    public double CenterLng => (MinLng + MaxLng) / 2;
    public double CenterLat => (MinLat + MaxLat) / 2;
}
