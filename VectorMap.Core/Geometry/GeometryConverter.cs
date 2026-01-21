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
                    if (Math.Abs(pt[0] - last[0]) < 1e-12 && Math.Abs(pt[1] - last[1]) < 1e-12)
                        continue;
                }
                cleaned.Add(pt);
            }

            // 2. Remove trailing duplicate
            if (cleaned.Count > 2)
            {
                var first = cleaned[0];
                var last = cleaned[^1];
                if (Math.Abs(first[0] - last[0]) < 1e-12 && Math.Abs(first[1] - last[1]) < 1e-12)
                {
                    cleaned.RemoveAt(cleaned.Count - 1);
                }
            }

            if (cleaned.Count < 3) continue;

            // 3. Add to tessellator (Input is already normalized 0..1 rel to tile)
            var contour = new ContourVertex[cleaned.Count];
            for (int i = 0; i < cleaned.Count; i++)
            {
                contour[i] = new ContourVertex { Position = new Vec3 { X = (float)cleaned[i][0], Y = (float)cleaned[i][1], Z = 0 } };
            }
            tess.AddContour(contour);
        }
        
        try
        {
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
    
    public static float[] LineToVertices(double[][] coordinates)
    {
        if (coordinates.Length < 2) return Array.Empty<float>();
        var vertices = new List<float>();
        const double epsilon = 1e-12;
        
        var xPrev = coordinates[0][0];
        var yPrev = coordinates[0][1];

        for (int i = 1; i < coordinates.Length; i++)
        {
            var xCurr = coordinates[i][0];
            var yCurr = coordinates[i][1];
            
            if (Math.Abs(xCurr - xPrev) < epsilon && Math.Abs(yCurr - yPrev) < epsilon)
                continue;

            vertices.Add((float)xPrev);
            vertices.Add((float)yPrev);
            vertices.Add((float)xCurr);
            vertices.Add((float)yCurr);
            
            xPrev = xCurr;
            yPrev = yCurr;
        }
        return vertices.ToArray();
    }
    
    public static float[] PointToVertices(double[] coordinates)
    {
        return new[] { (float)coordinates[0], (float)coordinates[1] };
    }
}
