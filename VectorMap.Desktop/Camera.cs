using OpenTK.Mathematics;
using VectorMap.Core.Projection;
using VectorMap.Core.Tiles;

namespace VectorMap.Desktop;

/// <summary>
/// Camera for 2D map navigation with pan and zoom
/// </summary>
public class Camera
{
    private const double TileSize = 512;
    
    public double X { get; set; }
    public double Y { get; set; }
    public double Zoom { get; set; }
    public double MinZoom { get; set; } = 0;
    public double MaxZoom { get; set; } = 18;
    public int ViewportWidth { get; set; }
    public int ViewportHeight { get; set; }
    
    public Camera(double lng, double lat, double zoom, int width, int height)
    {
        var (x, y) = MercatorCoordinate.FromLngLat(lng, lat);
        X = x;
        Y = y;
        Zoom = zoom;
        ViewportWidth = width;
        ViewportHeight = height;
    }
    
    /// <summary>
    /// Get the view-projection matrix for rendering
    /// </summary>
    public Matrix4 GetViewProjectionMatrix()
    {
        double zoomScale = 1.0 / Math.Pow(2, Zoom);
        double widthScale = TileSize / ViewportWidth;
        double heightScale = TileSize / ViewportHeight;
        
        float scaleX = (float)(zoomScale / widthScale);
        float scaleY = (float)(zoomScale / heightScale);
        
        // Build camera matrix matching JS: mat3.translate then mat3.scale
        // In OpenTK: Matrix4.CreateScale * Matrix4.CreateTranslation (reverse order)
        var cameraMat = Matrix4.CreateScale(scaleX, scaleY, 1f) * 
                        Matrix4.CreateTranslation((float)X, (float)Y, 0);
        
        // Invert to get view matrix
        var viewMat = Matrix4.Invert(cameraMat);
        
        return viewMat;
    }
    
    /// <summary>
    /// Pan the camera by a delta in screen pixels
    /// </summary>
    public void Pan(float deltaX, float deltaY, int screenWidth, int screenHeight)
    {
        // Convert screen delta to world delta
        double zoomScale = 1.0 / Math.Pow(2, Zoom);
        double widthScale = TileSize / screenWidth;
        double heightScale = TileSize / screenHeight;
        
        // Normalize to clip space and scale by zoom
        double worldDeltaX = (deltaX / screenWidth * 2) * (zoomScale / widthScale);
        double worldDeltaY = (-deltaY / screenHeight * 2) * (zoomScale / heightScale);
        
        X += worldDeltaX;
        Y += worldDeltaY;
    }
    
    /// <summary>
    /// Zoom at a specific screen position
    /// </summary>
    public void ZoomAt(float zoomDelta, float screenX, float screenY, int screenWidth, int screenHeight)
    {
        // Get world position before zoom
        var (preZoomWorldX, preZoomWorldY) = ScreenToWorld(screenX, screenY, screenWidth, screenHeight);
        
        // Update zoom
        double prevZoom = Zoom;
        Zoom = Math.Clamp(Zoom + zoomDelta, MinZoom, MaxZoom);
        
        // Get world position after zoom
        var (postZoomWorldX, postZoomWorldY) = ScreenToWorld(screenX, screenY, screenWidth, screenHeight);
        
        // Adjust camera position to keep the zoom point stable
        X += preZoomWorldX - postZoomWorldX;
        Y += preZoomWorldY - postZoomWorldY;
    }
    
    /// <summary>
    /// Convert screen coordinates to world coordinates
    /// </summary>
    public (double x, double y) ScreenToWorld(float screenX, float screenY, int screenWidth, int screenHeight)
    {
        // Convert to clip space (-1 to 1)
        double clipX = (screenX / screenWidth) * 2 - 1;
        double clipY = (1 - screenY / screenHeight) * 2 - 1;
        
        // Invert the view-projection matrix
        var invMat = Matrix4.Invert(GetViewProjectionMatrix());
        var clipPos = new Vector4((float)clipX, (float)clipY, 0, 1);
        var worldPos = Vector4.TransformRow(clipPos, invMat);
        
        return (worldPos.X, worldPos.Y);
    }
    
    /// <summary>
    /// Get the bounding box of the current view in lat/lng
    /// </summary>
    public BoundingBox GetBounds()
    {
        double zoomScale = Math.Pow(2, Zoom);
        double pixelRatio = 2;
        
        // Undo clip-space
        double px = (1 + X) / pixelRatio;
        double py = (1 - Y) / pixelRatio;
        
        // Get world coord in px
        double wx = px * TileSize;
        double wy = py * TileSize;
        
        // Get zoom px
        double zx = wx * zoomScale;
        double zy = wy * zoomScale;
        
        // Get corners
        double x1 = zx - (ViewportWidth / 2.0);
        double y1 = zy + (ViewportHeight / 2.0);
        double x2 = zx + (ViewportWidth / 2.0);
        double y2 = zy - (ViewportHeight / 2.0);
        
        // Convert to world coords
        x1 = x1 / zoomScale / TileSize;
        y1 = y1 / zoomScale / TileSize;
        x2 = x2 / zoomScale / TileSize;
        y2 = y2 / zoomScale / TileSize;
        
        // Get LngLat bounding box
        double minLng = MercatorCoordinate.LngFromMercatorX(x1);
        double maxLat = MercatorCoordinate.LatFromMercatorY(y1);
        double maxLng = MercatorCoordinate.LngFromMercatorX(x2);
        double minLat = MercatorCoordinate.LatFromMercatorY(y2);
        
        return new BoundingBox(minLng, minLat, maxLng, maxLat);
    }
    
    /// <summary>
    /// Check if the camera is at world limits
    /// </summary>
    public bool IsAtLimits()
    {
        var bounds = GetBounds();
        return bounds.MinLng <= -180 || bounds.MinLat <= -85.05 || 
               bounds.MaxLng >= 180 || bounds.MaxLat >= 85.05;
    }
}
