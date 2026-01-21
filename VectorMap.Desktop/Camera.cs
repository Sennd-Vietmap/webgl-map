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
    /// Get the view-projection matrix for rendering
    /// Updated for 3D perspective support
    /// </summary>
    public Matrix4 GetViewProjectionMatrix()
    {
        // 1. World scale (Total pixels at this zoom level)
        double worldSize = TileSize * Math.Pow(2, Zoom);
        
        // 2. Camera Altitude (to match 1:1 pixel scale at rendering plane)
        // Standard Mapbox FOV is ~36.87 degrees (Math.Atan(0.5) * 2)? Or 60?
        // Let's use 60 degrees (Pi/3) for dramatic effect, or stick to simple standard.
        // If we want scale to verify, we align altitude.
        // Altitude = (ViewportHeight / 2) / tan(FOV / 2)
        float fovVal = MathHelper.DegreesToRadians(60f);
        float altitude = (float)(ViewportHeight / 2.0 / Math.Tan(fovVal / 2.0));
        
        // 3. Projection Matrix (Perspective)
        // Near plane must be > 0. Far plane large enough.
        // Aspect ratio = width/height
        Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(
            fovVal, 
            (float)ViewportWidth / ViewportHeight, 
            0.1f, // Near
            altitude * 100f // Far
        );
        
        // 4. Transform from Lat/Lng (Mercator 0-1) to World Pixels relative to Camera Center
        // We use double precision for the initial offset to prevent jitter
        // But matrices are float. So we compute the translation vector in double, 
        // convert to float, then build the matrix.
        
        // NOTE: Vertices are in 0..1 range (Global Mercator)? 
        // No, TileManager passes tile-relative coordinates?
        // Wait, checking GeometryConverter... 
        // VectorTileParser calls GeometryConverter.PolygonToVertices.
        // GeometryConverter calls MercatorCoordinate.FromLngLat -> returns 0..1 global mercator.
        // MapRenderer passes these directly.
        // SO Vertices are 0..1 Global Mercator.
        
        // Matrix Chain (Reverse order for Row-Major multiplication):
        // 1. Translate(-CameraX, -CameraY) -> Centers world on camera
        // 2. Scale(WorldSize) -> Converts 0..1 to World Pixels
        // 3. RotateZ(Bearing)
        // 4. RotateX(Pitch)
        // 5. Translate(0, 0, -Altitude) -> Move camera back
        // 6. Projection
        
        // Step 1: Translation vector (Model center relative to camera)
        // Since we are applying this to vertices V:
        // V_local = (V - CameraPos) * WorldSize
        // This effectively translates then scales? No.
        // (V * WorldSize) - (CameraPos * WorldSize)
        
        // Let's stick to standard MVP: View * Model
        // Model Matrix: Just Scale? Vertices are 0..1.
        // We handle the large number precision by translating BEFORE scaling??
        
        // Precision issues: 0..1 coordinates are "doubles" but GPU uses floats.
        // We rely on "uScale" and "uOffset" in shader for tiling?
        // The implementation plan used global coords.
        // MapRenderer handles uScale/uOffset as 1.0/0.0 by default.
        // We must ensure the matrix doesn't lose precision.
        
        // To avoid jitter, the translation (-CameraX, -CameraY) should effectively be handled.
        
        // Constructing the full matrix:
        // V_clip = V_world * [Translate(-CamX, -CamY) * Scale(WorldSize)] * [RotateZ * RotateX * TranslateZ] * Proj
        
        // 3a. View Matrix Construction
        // Camera is at (0, 0, Altitude) looking at (0, 0, 0)
        // We rotate the WORLD, not the camera.
        Matrix4 view = Matrix4.Identity;
        view *= Matrix4.CreateRotationZ(MathHelper.DegreesToRadians((float)Bearing));
        view *= Matrix4.CreateRotationX(MathHelper.DegreesToRadians((float)Pitch));
        view *= Matrix4.CreateTranslation(0, 0, -altitude); // Move world back from camera
        
        // 3b. Model Transformation (Coordinate -> WorldPixels centered at 0,0)
        // Standard transform: (V - C) * S
        // = V*S - C*S
        
        // Since we can't create a translation matrix with doubles in OpenTK (it takes floats),
        // we have to be careful.
        // Ideally we pass a "CameraCenter" uniform to shader and subtract there?
        // But for now, let's try constructing the matrix components manually for best precision fitting in floats.
        
        // For Scale * Translate:
        // [ S  0  0  0 ]
        // [ 0  S  0  0 ]
        // [ 0  0  S  0 ]
        // [ -Cx*S -Cy*S 0 1 ]
        
        double s = worldSize;
        double tx = -X * s;
        double ty = Y * s; // Invert Y for OpenGL?
        // Wait, Mercator Y is Top-Down (0..1). OpenGL is Bottom-Up.
        // Existing logic (Camera.cs lines 40-70) had:
        // double py = (1 - Y) / pixelRatio; -> Inverting Y.
        
        // Let's stick to the previous working standard 2D logic but projected.
        // Previous logic: Mercator(0..1) -> Clip(-1..1).
        
        // Recalculating:
        // Mercator Y (0 top, 1 bottom). OpenGL Y (1 top, -1 bottom).
        // Correct conversion: Y_gl = (0.5 - Y_merc) * Scale ??
        
        // Let's trust the standard Mapbox-style matrix:
        // Scale = WorldSize
        // Translate = -CameraX*Scale, -CameraY*Scale
        
        // WE MUST FLIP Y somewhere because Mercator Y goes DOWN, OpenGL Y goes UP.
        // Scale Y by -1 ?
        
        Matrix4 model = Matrix4.Identity;
        model *= Matrix4.CreateScale((float)s, -(float)s, 1f); // Flip Y here?
        model *= Matrix4.CreateTranslation((float)tx, -(float)ty, 0f); // And negate ty?
        
        // Actually, simple way:
        // Translate world center to 0,0
        // (V - Cam)
        
        // Let's go with the matrix that matches gl-matrix behavior:
        // 1. Translate (-CamX, -CamY)
        // 2. Scale (WorldSize)
        // 3. Rotate ...
        
        // But we have to handle the Y-flip.
        // Mercator: Y increases Down.
        // OpenGL: Y increases Up.
        
        // So:
        // V.y_gl = -(V.y_merc - Cam.y_merc) * WorldSize
        
        Matrix4 worldTransform = Matrix4.CreateTranslation(-(float)X, -(float)Y, 0);
        worldTransform *= Matrix4.CreateScale((float)worldSize, -(float)worldSize, 1.0f); // Flip Y
        
        return worldTransform * view * projection;
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
