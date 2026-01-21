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
        // Invert Pitch to make the top of the map go "back" into negative Z (Perspective depth)
        view *= Matrix4.CreateRotationX(MathHelper.DegreesToRadians(-(float)Pitch));
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
    /// Convert screen coordinates to world coordinates using Double Precision Ray Casting
    /// </summary>
    public (double x, double y) ScreenToWorld(float screenX, float screenY, int screenWidth, int screenHeight)
    {
        // 1. Viewport Parameters (Double Precision)
        double viewportW = screenWidth;
        double viewportH = screenHeight;
        
        // 2. Constants matching GetViewProjectionMatrix
        // FOV = 60 degrees
        double fovRad = MathHelper.DegreesToRadians(60.0);
        double fovTan = Math.Tan(fovRad / 2.0);
        
        // Camera Altitude in "World Pixels" (Constant for a given Viewport Height + FOV)
        double altitude = (viewportH / 2.0) / fovTan;
        
        // 3. NDC Coordinates
        // Y is inverted (0 at top -> 1 at bottom? No, Screen Y is 0 top. NDC Y is 1 top.)
        // So Screen(0) -> NDC(1). Screen(H) -> NDC(-1).
        double ndcX = (screenX / viewportW) * 2.0 - 1.0;
        double ndcY = 1.0 - (screenY / viewportH) * 2.0;
        
        // 4. Ray Direction in View Space
        // Camera is at (0,0,0) look at (0,0,-1)
        // Aspect Ratio logic: x needs to be wider
        double aspect = viewportW / viewportH;
        
        double dirX = ndcX * aspect * fovTan;
        double dirY = ndcY * fovTan;
        double dirZ = -1.0; // Pointing forward into screen
        
        // 5. Un-Rotate Ray (Inverse View Matrix)
        // Forward: RotZ(Bearing) * RotX(-Pitch)
        // Inverse: RotX(Pitch) * RotZ(-Bearing)
        
        double pitchRad = MathHelper.DegreesToRadians(Pitch);
        double bearingRad = MathHelper.DegreesToRadians(Bearing);
        
        // 5a. Rotate X (Pitch)
        // y' = y*cos(P) - z*sin(P)
        // z' = y*sin(P) + z*cos(P)
        double cosP = Math.Cos(pitchRad);
        double sinP = Math.Sin(pitchRad);
        
        double dy_p = dirY * cosP - dirZ * sinP;
        double dz_p = dirY * sinP + dirZ * cosP;
        double dx_p = dirX; // Unchanged
        
        // 5b. Rotate Z (-Bearing)
        // Angle = -Bearing
        // x' = x*cos(A) - y*sin(A)
        // y' = x*sin(A) + y*cos(A)
        double negBearing = -bearingRad;
        double cosB = Math.Cos(negBearing);
        double sinB = Math.Sin(negBearing);
        
        double dx_w = dx_p * cosB - dy_p * sinB;
        double dy_w = dx_p * sinB + dy_p * cosB;
        double dz_w = dz_p; // Unchanged
        
        // Ray Direction World
        double rayX = dx_w;
        double rayY = dy_w;
        double rayZ = dz_w;
        
        // 6. Intersection with Plane Z = 0
        // Ray Origin (Ro) is Camera Position in Pivot-Space.
        // ScreenToWorld logic effectively puts the "Pivot" (Center of Screen) at (0,0,0).
        // The "World Plane" is shifted by -Altitude relative to Camera?
        // No, in View Space, Camera is at (0,0,0). World is translated by (0,0,-Alt).
        // So World Plane is at Z = -Altitude?
        // Let's re-verify GetViewProjection logic:
        // view *= Translate(0, 0, -altitude).
        // This MOVES THE WORLD Z by -altitude.
        // So if Camera is at 0, World Surface is at -Altitude.
        // Yes.
        
        // We want to intersect Ray (Origin=0, Dir) with Plane Z = -Altitude.
        // rayZ * t = -Altitude
        // t = -Altitude / rayZ
        
        if (Math.Abs(rayZ) < 1e-9) return (X, Y); // Parallel to horizon
        
        double t = -altitude / rayZ;
        
        // If t < 0, intersection is behind camera (shouldn't happen for ground plane unless pitched up)
        // But with Pitch < 90, rayZ should be negative (pointing down).
        // -Alt is negative. Neg/Neg = Pos.
        
        double intersectX = rayX * t;
        double intersectY = rayY * t;
        
        // 7. Convert Intersection to Global Mercator
        // The "Pivot" (0,0) in this space corresponds to the Camera Center (Camera.X, Camera.Y).
        // The World Transformation scaled Global(0..1) by WorldSize(pixels).
        // And flipped Y?
        // model *= Scale(S, -S, 1);
        // model *= Translate(-Cx*S, -Cy*S, 0); -> No, that was my manual derivation.
        // Code Is: 
        // worldTransform = Translate(-X, -Y, 0)
        // worldTransform *= Scale(S, -S, 1)
        
        // So converting from Global to Pivot-Pixels:
        // P_pix = (P_global - C_global) * ScaleVect
        // P_pix.x = (P.x - C.x) * S
        // P_pix.y = (P.y - C.y) * -S
        
        // We have P_pix (intersectX, intersectY). We want P.x.
        // intersectX / S = P.x - C.x  => P.x = C.x + intersectX/S
        // intersectY / -S = P.y - C.y => P.y = C.y - intersectY/S
        
        double worldSize = TileSize * Math.Pow(2, Zoom);
        
        double finalX = X + (intersectX / worldSize);
        double finalY = Y - (intersectY / worldSize);
        
        return (finalX, finalY);
    }

    /// <summary>
    /// Convert world coordinates back to screen coordinates (Verification)
    /// </summary>
    public (double x, double y) WorldToScreen(double worldX, double worldY)
    {
        // 1. Convert to Global Mercator relative to Camera
        double worldSize = TileSize * Math.Pow(2, Zoom);
        
        // Pivot-Space World Coordinates
        // P_pix.x = (P.x - C.x) * S
        // P_pix.y = (P.y - C.y) * -S
        double wx = (worldX - X) * worldSize;
        double wy = (worldY - Y) * -worldSize; // Flip Y
        
        // 2. Apply View Matrix
        // view = Translate(0,0,-Alt) * RotX(Pitch) * RotZ(Bearing)
        // Order: P_view = View * P_world
        // But OpenTK is Row-Major: P_view = P_world * View
        
        // Let's rely on the actual Matrix for this direction to be safe
        // Or build it manually to verify match?
        // Let's use the matrix to see where the GPU puts it.
        
        // Problem: We need the exact matrix components or use Vector4 transform
        
        // Full Transform used in GetViewProjectionMatrix:
        // worldTransform (Translate/Scale) -> View -> Proj
        
        /* 
           Matrix4 worldTransform = Matrix4.CreateTranslation(-(float)X, -(float)Y, 0);
           worldTransform *= Matrix4.CreateScale((float)worldSize, -(float)worldSize, 1.0f);
           return worldTransform * view * projection;
        */
        
        // We can just construct the vector (worldX, worldY, 0, 1) and multiply by VP
        // BUT: Floating point precision issues with raw matrix mult!
        // worldX is 0..1 (Small).
        
        Matrix4 vp = GetViewProjectionMatrix();
        Vector4 worldPos = new Vector4((float)worldX, (float)worldY, 0, 1);
        Vector4 clipPos = Vector4.TransformRow(worldPos, vp);
        
        if (Math.Abs(clipPos.W) < 1e-5) return (-1, -1);
        
        // NDC
        Vector3 ndc = clipPos.Xyz / clipPos.W;
        
        // Screen
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
