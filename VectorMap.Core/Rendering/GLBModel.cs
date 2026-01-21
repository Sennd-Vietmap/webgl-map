using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SharpGLTF.Schema2;
using SharpGLTF.Runtime;

namespace VectorMap.Core.Rendering;

public class ModelMesh
{
    public float[] Vertices { get; set; } = Array.Empty<float>(); // x, y, z, nx, ny, nz
    public uint[] Indices { get; set; } = Array.Empty<uint>();
}

public class GLBModel
{
    public List<ModelMesh> Meshes { get; } = new();

    public static async Task<GLBModel> LoadAsync(string path)
    {
        return await Task.Run(() => Load(path));
    }

    public static GLBModel Load(string path)
    {
        var model = new GLBModel();
        var root = ModelRoot.Load(path);
        
        foreach (var mesh in root.LogicalMeshes)
        {
            foreach (var primitive in mesh.Primitives)
            {
                var meshData = new ModelMesh();
                
                // Get accessors
                var positions = primitive.GetVertexAccessor("POSITION").AsVector3Array();
                var normalAccessor = primitive.GetVertexAccessor("NORMAL");
                var normals = normalAccessor?.AsVector3Array();
                var colorAccessor = primitive.GetVertexAccessor("COLOR_0");
                var colors = colorAccessor?.AsVector4Array();
                
                // Get material base color for fallback
                var baseColor = new Vector4(1, 1, 1, 1);
                var material = primitive.Material;
                if (material != null)
                {
                    var baseColorChannel = material.FindChannel("BaseColor");
                    if (baseColorChannel != null)
                    {
                        baseColor = baseColorChannel.Value.Color;
                    }
                }
                
                var vertices = new List<float>();
                for (int i = 0; i < positions.Count; i++)
                {
                    // Position
                    vertices.Add(positions[i].X);
                    vertices.Add(positions[i].Y);
                    vertices.Add(positions[i].Z);
                    
                    // Normal
                    if (normals != null && i < normals.Count)
                    {
                        vertices.Add(normals[i].X);
                        vertices.Add(normals[i].Y);
                        vertices.Add(normals[i].Z);
                    }
                    else
                    {
                        vertices.Add(0); vertices.Add(0); vertices.Add(1);
                    }

                    // Color
                    if (colors != null && i < colors.Count)
                    {
                        vertices.Add(colors[i].X);
                        vertices.Add(colors[i].Y);
                        vertices.Add(colors[i].Z);
                        vertices.Add(colors[i].W);
                    }
                    else
                    {
                        vertices.Add(baseColor.X);
                        vertices.Add(baseColor.Y);
                        vertices.Add(baseColor.Z);
                        vertices.Add(baseColor.W);
                    }
                }
                
                meshData.Vertices = vertices.ToArray();
                meshData.Indices = primitive.GetIndices().ToArray();
                model.Meshes.Add(meshData);
            }
        }
        
        return model;
    }

    public static GLBModel CreateCube()
    {
        var model = new GLBModel();
        var mesh = new ModelMesh();
        
        // Simple 1x1x1 cube centered at origin
        mesh.Vertices = new float[] {
            // Front face (Red)
            -0.5f, -0.5f,  0.5f,  0.0f,  0.0f,  1.0f,  1.0f, 0.0f, 0.0f, 1.0f,
             0.5f, -0.5f,  0.5f,  0.0f,  0.0f,  1.0f,  1.0f, 0.0f, 0.0f, 1.0f,
             0.5f,  0.5f,  0.5f,  0.0f,  0.0f,  1.0f,  1.0f, 0.0f, 0.0f, 1.0f,
            -0.5f,  0.5f,  0.5f,  0.0f,  0.0f,  1.0f,  1.0f, 0.0f, 0.0f, 1.0f,
            // Back face (Green)
            -0.5f, -0.5f, -0.5f,  0.0f,  0.0f, -1.0f,  0.0f, 1.0f, 0.0f, 1.0f,
             0.5f, -0.5f, -0.5f,  0.0f,  0.0f, -1.0f,  0.0f, 1.0f, 0.0f, 1.0f,
             0.5f,  0.5f, -0.5f,  0.0f,  0.0f, -1.0f,  0.0f, 1.0f, 0.0f, 1.0f,
            -0.5f,  0.5f, -0.5f,  0.0f,  0.0f, -1.0f,  0.0f, 1.0f, 0.0f, 1.0f,
            // Left face (Blue)
            -0.5f, -0.5f, -0.5f, -1.0f,  0.0f,  0.0f,  0.0f, 0.0f, 1.0f, 1.0f,
            -0.5f, -0.5f,  0.5f, -1.0f,  0.0f,  0.0f,  0.0f, 0.0f, 1.0f, 1.0f,
            -0.5f,  0.5f,  0.5f, -1.0f,  0.0f,  0.0f,  0.0f, 0.0f, 1.0f, 1.0f,
            -0.5f,  0.5f, -0.5f, -1.0f,  0.0f,  0.0f,  0.0f, 0.0f, 1.0f, 1.0f,
            // Right face (Yellow)
             0.5f, -0.5f, -0.5f,  1.0f,  0.0f,  0.0f,  1.0f, 1.0f, 0.0f, 1.0f,
             0.5f, -0.5f,  0.5f,  1.0f,  0.0f,  0.0f,  1.0f, 1.0f, 0.0f, 1.0f,
             0.5f,  0.5f,  0.5f,  1.0f,  0.0f,  0.0f,  1.0f, 1.0f, 0.0f, 1.0f,
             0.5f,  0.5f, -0.5f,  1.0f,  0.0f,  0.0f,  1.0f, 1.0f, 0.0f, 1.0f,
            // Top face (Magenta)
            -0.5f,  0.5f,  0.5f,  0.0f,  1.0f,  0.0f,  1.0f, 0.0f, 1.0f, 1.0f,
             0.5f,  0.5f,  0.5f,  0.0f,  1.0f,  0.0f,  1.0f, 0.0f, 1.0f, 1.0f,
             0.5f,  0.5f, -0.5f,  0.0f,  1.0f,  0.0f,  1.0f, 0.0f, 1.0f, 1.0f,
            -0.5f,  0.5f, -0.5f,  0.0f,  1.0f,  0.0f,  1.0f, 0.0f, 1.0f, 1.0f,
            // Bottom face (Cyan)
            -0.5f, -0.5f,  0.5f,  0.0f, -1.0f,  0.0f,  0.0f, 1.0f, 1.0f, 1.0f,
             0.5f, -0.5f,  0.5f,  0.0f, -1.0f,  0.0f,  0.0f, 1.0f, 1.0f, 1.0f,
             0.5f, -0.5f, -0.5f,  0.0f, -1.0f,  0.0f,  0.0f, 1.0f, 1.0f, 1.0f,
            -0.5f, -0.5f, -0.5f,  0.0f, -1.0f,  0.0f,  0.0f, 1.0f, 1.0f, 1.0f,
        };
        
        mesh.Indices = new uint[] {
            0, 1, 2,  2, 3, 0,       // Front
            4, 5, 6,  6, 7, 4,       // Back
            8, 9, 10, 10, 11, 8,    // Left
            12, 13, 14, 14, 15, 12, // Right
            16, 17, 18, 18, 19, 16, // Top
            20, 21, 22, 22, 23, 20  // Bottom
        };
        
        model.Meshes.Add(mesh);
        return model;
    }
}
