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
    /// Matches the JavaScript gl-matrix implementation exactly
    /// </summary>
    public double Pitch { get; set; } = 0; // Degrees (0-60)
    public double Bearing { get; set; } = 0; // Degrees (0-360)

    /// <summary>
    /// Get the view-projection matrix for rendering (Float for GPU)
    /// </summary>
    public Matrix4 GetViewProjectionMatrix()
    {
        var m = GetViewProjectionMatrixDouble();
        
        // Convert Double Matrix to Float Matrix
        return new Matrix4(
            (float)m.Row0.X, (float)m.Row0.Y, (float)m.Row0.Z, (float)m.Row0.W,
            (float)m.Row1.X, (float)m.Row1.Y, (float)m.Row1.Z, (float)m.Row1.W,
            (float)m.Row2.X, (float)m.Row2.Y, (float)m.Row2.Z, (float)m.Row2.W,
            (float)m.Row3.X, (float)m.Row3.Y, (float)m.Row3.Z, (float)m.Row3.W
        );
    }

    /// <summary>
    /// High-precision ViewProjection Matrix calculation
    /// </summary>
    public Matrix4d GetViewProjectionMatrixDouble()
    {
        double worldSize = TileSize * Math.Pow(2, Zoom);
        
        double fovVal = MathHelper.DegreesToRadians(60.0);
        double altitude = (ViewportHeight / 2.0 / Math.Tan(fovVal / 2.0));
        
        Matrix4d projection = Matrix4d.CreatePerspectiveFieldOfView(
            fovVal, 
            (double)ViewportWidth / ViewportHeight, 
            0.1, 
            altitude * 100.0
        );
        
        // View Matrix
        Matrix4d view = Matrix4d.Identity;
        view *= Matrix4d.CreateRotationZ(MathHelper.DegreesToRadians(Bearing));
        view *= Matrix4d.CreateRotationX(MathHelper.DegreesToRadians(-Pitch));
        view *= Matrix4d.CreateTranslation(0, 0, -altitude);
        
        // Model Matrix: Translate then Scale
        // Note: Mercator Y (0..1) is inverted compared to GL Y
        Matrix4d worldTransform = Matrix4d.CreateTranslation(-X, -Y, 0);
        worldTransform *= Matrix4d.CreateScale(worldSize, -worldSize, 1.0);
        
        return worldTransform * view * projection;
    }
    
    /// <summary>
    /// Pan the camera by a delta in screen pixels
    /// </summary>
    public void Pan(float deltaX, float deltaY, int screenWidth, int screenHeight)
    {
        double worldSize = TileSize * Math.Pow(2, Zoom);
        // Dragging moves the world, so camera moves opposite
        X -= deltaX / worldSize;
        // Invert Y delta because Mercator Y goes Down, but screen Y goes Down.
        // Drag Down (+Y) -> Map moves Down. Camera moves Up (Decrease Y).
        Y -= deltaY / worldSize; 
    }
    
    /// <summary>
    /// Zoom at a specific screen position
    /// </summary>
    public void ZoomAt(float zoomDelta, float screenX, float screenY, int screenWidth, int screenHeight)
    {
        // 1. Get world coordinate under mouse BEFORE zoom
        var (wx1, wy1) = ScreenToWorld(screenX, screenY, screenWidth, screenHeight);
        
        // 2. Apply Zoom
        Zoom = Math.Clamp(Zoom + zoomDelta, MinZoom, MaxZoom);
        
        // 3. Get world coordinate under mouse AFTER zoom (using old camera pos)
        var (wx2, wy2) = ScreenToWorld(screenX, screenY, screenWidth, screenHeight);
        
        // 4. Shift Camera to put the world point back under the mouse
        X += wx1 - wx2;
        Y += wy1 - wy2;
    }
    
    /// <summary>
    /// Convert screen coordinates to world coordinates (Ray-Plane Intersection Z=0)
    /// Uses Double Precision Matrix
    /// </summary>
    public (double x, double y) ScreenToWorld(float screenX, float screenY, int screenWidth, int screenHeight)
    {
        // 1. NDC
        double ndcX = (screenX / (double)screenWidth) * 2.0 - 1.0;
        double ndcY = 1.0 - (screenY / (double)screenHeight) * 2.0;

        // 2. Inverse Matrix
        Matrix4d vp = GetViewProjectionMatrixDouble();
        Matrix4d invVP = Matrix4d.Invert(vp);

        // 3. Unproject Near (clip Z=-1) and Far (clip Z=1)
        Vector4d nearClip = new Vector4d(ndcX, ndcY, -1.0, 1.0);
        Vector4d farClip = new Vector4d(ndcX, ndcY, 1.0, 1.0);
        
        Vector4d nearWorld = Vector4d.TransformRow(nearClip, invVP);
        Vector4d farWorld = Vector4d.TransformRow(farClip, invVP);
        
        // Perspective Divide
        Vector3d near = nearWorld.Xyz / nearWorld.W;
        Vector3d far = farWorld.Xyz / farWorld.W;
        
        // 4. Ray-Plane Intersection (Z=0)
        Vector3d dir = far - near;
        
        if (Math.Abs(dir.Z) < 1e-9) return (X, Y); // Parallel
        
        double t = -near.Z / dir.Z;
        Vector3d intersection = near + dir * t;
        
        return (intersection.X, intersection.Y);
    }

    /// <summary>
    /// Convert world coordinates back to screen coordinates (Verification)
    /// </summary>
    public (double x, double y) WorldToScreen(double worldX, double worldY)
    {
        // 1. Get Double Precision Matrix
        Matrix4d vp = GetViewProjectionMatrixDouble();
        
        // 2. Transform World to Clip
        Vector4d worldPos = new Vector4d(worldX, worldY, 0, 1.0);
        Vector4d clipPos = Vector4d.TransformRow(worldPos, vp);
        
        if (Math.Abs(clipPos.W) < 1e-9) return (-1, -1);
        
        // 3. NDC
        Vector3d ndc = clipPos.Xyz / clipPos.W;
        
        // 4. Screen
        // ndc.x = (x / w) * 2 - 1  => x = (ndc.x + 1) * 0.5 * w
        // ndc.y = 1 - (y / h) * 2  => y = (1 - ndc.y) * 0.5 * h
        
        double screenX = (ndc.X + 1.0) * 0.5 * ViewportWidth;
        double screenY = (1.0 - ndc.Y) * 0.5 * ViewportHeight;
        
        return (screenX, screenY);
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
        // Get LngLat bounding box
        double minLng = MercatorCoordinate.LngFromMercatorX(x1);
        double minLat = MercatorCoordinate.LatFromMercatorY(y1); // y1 is bottom (larger y) -> min lat
        double maxLng = MercatorCoordinate.LngFromMercatorX(x2);
        double maxLat = MercatorCoordinate.LatFromMercatorY(y2); // y2 is top (smaller y) -> max lat
        
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
