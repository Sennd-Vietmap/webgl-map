namespace VectorMap.Core.Projection;

/// <summary>
/// Web Mercator projection utilities for converting between geographic coordinates and clip space.
/// Based on the Mapbox GL implementation.
/// </summary>
public static class MercatorCoordinate
{
    private const double PI = Math.PI;
    
    /// <summary>
    /// Convert longitude to Mercator X coordinate (0-1 range)
    /// </summary>
    public static double MercatorXFromLng(double lng)
    {
        return (180 + lng) / 360;
    }

    /// <summary>
    /// Convert latitude to Mercator Y coordinate (0-1 range)
    /// </summary>
    public static double MercatorYFromLat(double lat)
    {
        return (180 - (180 / PI * Math.Log(Math.Tan(PI / 4 + lat * PI / 360)))) / 360;
    }

    /// <summary>
    /// Convert longitude/latitude to clip space coordinates (-1 to 1 range)
    /// </summary>
    public static (double x, double y) FromLngLat(double lng, double lat)
    {
        double x = MercatorXFromLng(lng);
        double y = MercatorYFromLat(lat);

        // Adjust to origin at center of viewport, instead of top-left
        x = -1 + (x * 2);
        y = 1 - (y * 2);

        return (x, y);
    }

    /// <summary>
    /// Convert Mercator X (0-1) to longitude
    /// </summary>
    public static double LngFromMercatorX(double x)
    {
        return x * 360 - 180;
    }

    /// <summary>
    /// Convert Mercator Y (0-1) to latitude
    /// </summary>
    public static double LatFromMercatorY(double y)
    {
        double y2 = 180 - y * 360;
        return 360 / PI * Math.Atan(Math.Exp(y2 * PI / 180)) - 90;
    }

    /// <summary>
    /// Convert clip space coordinates to longitude/latitude
    /// </summary>
    public static (double lng, double lat) ToLngLat(double x, double y)
    {
        double lng = LngFromMercatorX((1 + x) / 2);
        double lat = LatFromMercatorY((1 - y) / 2);
        return (lng, lat);
    }
}
