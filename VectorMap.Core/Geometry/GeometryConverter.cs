using LibTessDotNet;
using VectorMap.Core.Projection;

namespace VectorMap.Core.Geometry;

/// <summary>
/// Converts GeoJSON-like geometry to WebGL-ready vertices
/// </summary>
public static class GeometryConverter
{
    /// <summary>
    /// Convert polygon coordinates to triangulated vertices
    /// </summary>
    public static float[] PolygonToVertices(double[][][] coordinates)
    {
        var tess = new Tess();

        foreach (var ring in coordinates)
        {
            if (ring.Length < 3) continue;

            // 1. Clean ring: remove consecutive duplicate points
            var cleaned = new List<double[]>();
            for (int i = 0; i < ring.Length; i++)
            {
                var pt = ring[i];
                if (cleaned.Count > 0)
                {
                    var last = cleaned[^1];
                    if (Math.Abs(pt[0] - last[0]) < 1e-9 && Math.Abs(pt[1] - last[1]) < 1e-9)
                        continue;
                }
                cleaned.Add(pt);
            }

            // 2. Remove trailing duplicate if it's the same as first
            if (cleaned.Count > 2)
            {
                var first = cleaned[0];
                var last = cleaned[^1];
                if (Math.Abs(first[0] - last[0]) < 1e-9 && Math.Abs(first[1] - last[1]) < 1e-9)
                {
                    cleaned.RemoveAt(cleaned.Count - 1);
                }
            }

            if (cleaned.Count < 3) continue;

            // 3. Convert to Mercator and add to tessellator
            var contour = new ContourVertex[cleaned.Count];
            for (int i = 0; i < cleaned.Count; i++)
            {
                var (x, y) = MercatorCoordinate.FromLngLat(cleaned[i][0], cleaned[i][1]);
                contour[i] = new ContourVertex { Position = new Vec3 { X = (float)x, Y = (float)y, Z = 0 } };
            }
            tess.AddContour(contour);
        }
        
        try
        {
            // Tessellate with a combine callback to handle intersections
            tess.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3, (Vec3 pos, object[] data, float[] weights) => null);
            
            var vertices = new List<float>();
            for (int i = 0; i < tess.ElementCount; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    int index = tess.Elements[i * 3 + j];
                    vertices.Add(tess.Vertices[index].Position.X);
                    vertices.Add(tess.Vertices[index].Position.Y);
                }
            }
            return vertices.ToArray();
        }
        catch
        {
            return Array.Empty<float>();
        }
    }
    
    /// <summary>
    /// Convert line coordinates to vertex pairs for GL.LINES
    /// </summary>
    public static float[] LineToVertices(double[][] coordinates)
    {
        if (coordinates.Length < 2) return Array.Empty<float>();
        
        var vertices = new List<float>();
        
        // Use a small threshold to skip duplicate/tiny segments
        const double epsilon = 1e-9;
        
        var (xPrev, yPrev) = MercatorCoordinate.FromLngLat(coordinates[0][0], coordinates[0][1]);

        for (int i = 1; i < coordinates.Length; i++)
        {
            var (xCurr, yCurr) = MercatorCoordinate.FromLngLat(coordinates[i][0], coordinates[i][1]);
            
            // Skip if segment is effectively zero length
            if (Math.Abs(xCurr - xPrev) < epsilon && Math.Abs(yCurr - yPrev) < epsilon)
                continue;

            // Add segment points
            vertices.Add((float)xPrev);
            vertices.Add((float)yPrev);
            vertices.Add((float)xCurr);
            vertices.Add((float)yCurr);
            
            xPrev = xCurr;
            yPrev = yCurr;
        }
        
        return vertices.ToArray();
    }
    
    /// <summary>
    /// Convert point coordinates to vertices
    /// </summary>
    public static float[] PointToVertices(double[] coordinates)
    {
        var (x, y) = MercatorCoordinate.FromLngLat(coordinates[0], coordinates[1]);
        return new[] { (float)x, (float)y };
    }
}
