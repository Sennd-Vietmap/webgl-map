using System.Collections.Generic;

namespace VectorMap.Core;

/// <summary>
/// Configuration options for the map window
/// </summary>
public class MapOptions
{
    public int Width { get; set; } = 800;
    public int Height { get; set; } = 600;
    public string Title { get; set; } = "Vector Map";
    public double CenterLng { get; set; } = -73.9834558; // Brooklyn
    public double CenterLat { get; set; } = 40.6932723;
    public double Zoom { get; set; } = 13;
    public double MinZoom { get; set; } = 0;
    public double MaxZoom { get; set; } = 18;
    public int MaxTileZoom { get; set; } = 18;
    public int TileBuffer { get; set; } = 1;
    public string TileServerUrl { get; set; } = "https://maps.ckochis.com/data/v3/{z}/{x}/{y}.pbf";
    public HashSet<string> DisabledLayers { get; set; } = new();
    
    public Dictionary<string, byte[]> Layers { get; set; } = new()
    {
        { "water", new byte[] { 180, 240, 250, 255 } },
        { "landcover", new byte[] { 202, 246, 193, 255 } },
        { "park", new byte[] { 202, 255, 193, 255 } },
        { "transportation", new byte[] { 202, 0, 193, 255 } },
        { "housenumber", new byte[] { 100, 100, 100, 255 } },
        { "building", new byte[] { 185, 175, 139, 191 } }
    };
}
