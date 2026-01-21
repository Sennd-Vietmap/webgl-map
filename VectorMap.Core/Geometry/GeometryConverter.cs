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
        
        // Add outer ring
        if (coordinates.Length > 0)
        {
            var contour = new ContourVertex[coordinates[0].Length];
            for (int i = 0; i < coordinates[0].Length; i++)
            {
                var coord = coordinates[0][i];
                var (x, y) = MercatorCoordinate.FromLngLat(coord[0], coord[1]);
                contour[i] = new ContourVertex { Position = new Vec3 { X = (float)x, Y = (float)y, Z = 0 } };
            }
            
            tess.AddContour(contour);
        }
        
        // Add holes
        for (int h = 1; h < coordinates.Length; h++)
        {
            var hole = coordinates[h];
            var contour = new ContourVertex[hole.Length];
            for (int i = 0; i < hole.Length; i++)
            {
                var (x, y) = MercatorCoordinate.FromLngLat(hole[i][0], hole[i][1]);
                contour[i] = new ContourVertex { Position = new Vec3 { X = (float)x, Y = (float)y, Z = 0 } };
            }
            tess.AddContour(contour);
        }
        
        // Tessellate
        tess.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3);
        
        // Extract vertices
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
    
    /// <summary>
    /// Convert line coordinates to vertex pairs for GL.LINES
    /// </summary>
    public static float[] LineToVertices(double[][] coordinates)
    {
        if (coordinates.Length < 2) return Array.Empty<float>();
        
        var vertices = new List<float>();
        
        // First segment
        var (x0, y0) = MercatorCoordinate.FromLngLat(coordinates[0][0], coordinates[0][1]);
        var (x1, y1) = MercatorCoordinate.FromLngLat(coordinates[1][0], coordinates[1][1]);
        vertices.Add((float)x0);
        vertices.Add((float)y0);
        vertices.Add((float)x1);
        vertices.Add((float)y1);
        
        // Subsequent segments - duplicate last point
        for (int i = 2; i < coordinates.Length; i++)
        {
            // Duplicate previous end point
            vertices.Add((float)x1);
            vertices.Add((float)y1);
            
            // Add new end point
            (x1, y1) = MercatorCoordinate.FromLngLat(coordinates[i][0], coordinates[i][1]);
            vertices.Add((float)x1);
            vertices.Add((float)y1);
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
