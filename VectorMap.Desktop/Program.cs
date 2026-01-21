namespace VectorMap.Desktop;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Starting Vector Map...");
        
        var options = new MapOptions
        {
            Width = 1024,
            Height = 768,
            Title = "Vector Tile Map - OpenTK",
            CenterLng = -73.9834558,  // Brooklyn, NY
            CenterLat = 40.6932723,
            Zoom = 13,
            TileServerUrl = "https://maps.ckochis.com/data/v3/{z}/{x}/{y}.pbf",
            Layers = new Dictionary<string, byte[]>
            {
                { "water", new byte[] { 180, 240, 250, 255 } },
                { "landcover", new byte[] { 202, 246, 193, 255 } },
                { "park", new byte[] { 202, 255, 193, 255 } },
                { "building", new byte[] { 185, 175, 139, 191 } }
            }
        };
        
        using var window = new MapWindow(options);
        window.Run();
    }
}
