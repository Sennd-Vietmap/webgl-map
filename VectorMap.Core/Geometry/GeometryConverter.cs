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
    public static (float[] Vertices, uint[] Indices) PolygonToVertices(double[][][] coordinates)
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

            // 2. Remove trailing duplicate
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
            tess.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3, (Vec3 pos, object[] data, float[] weights) => null);
            
            // Output unique vertices
            float[] vertices = new float[tess.VertexCount * 2];
            for (int i = 0; i < tess.VertexCount; i++)
            {
                vertices[i * 2] = tess.Vertices[i].Position.X;
                vertices[i * 2 + 1] = tess.Vertices[i].Position.Y;
            }

            // Output indices
            uint[] indices = new uint[tess.ElementCount * 3];
            for (int i = 0; i < tess.ElementCount * 3; i++)
            {
                indices[i] = (uint)tess.Elements[i];
            }

            return (vertices, indices);
        }
        catch
        {
            return (Array.Empty<float>(), Array.Empty<uint>());
        }
    }
    
    public static (float[] Vertices, uint[] Indices) LineToVertices(double[][] coordinates)
    {
        if (coordinates.Length < 2) return (Array.Empty<float>(), Array.Empty<uint>());
        
        var vertices = new List<float>();
        var indices = new List<uint>();
        
        for (int i = 0; i < coordinates.Length; i++)
        {
            var (x, y) = MercatorCoordinate.FromLngLat(coordinates[i][0], coordinates[i][1]);
            vertices.Add((float)x);
            vertices.Add((float)y);
            
            if (i > 0)
            {
                indices.Add((uint)(vertices.Count / 2 - 2));
                indices.Add((uint)(vertices.Count / 2 - 1));
            }
        }
        
        return (vertices.ToArray(), indices.ToArray());
    }
    
    public static float[] PointToVertices(double[] coordinates)
    {
        var (x, y) = MercatorCoordinate.FromLngLat(coordinates[0], coordinates[1]);
        return new[] { (float)x, (float)y };
    }
}
