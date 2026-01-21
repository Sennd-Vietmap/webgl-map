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

    public static GLBModel Load(string path)
    {
        var model = new GLBModel();
        var root = ModelRoot.Load(path);
        
        foreach (var mesh in root.LogicalMeshes)
        {
            foreach (var primitive in mesh.Primitives)
            {
                var meshData = new ModelMesh();
                
                // Get positions
                var positions = primitive.GetVertexAccessor("POSITION").AsVector3Array();
                var normalAccessor = primitive.GetVertexAccessor("NORMAL");
                var normals = normalAccessor?.AsVector3Array();
                
                var vertices = new List<float>();
                for (int i = 0; i < positions.Count; i++)
                {
                    vertices.Add(positions[i].X);
                    vertices.Add(positions[i].Y);
                    vertices.Add(positions[i].Z);
                    
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
            // Front face
            -0.5f, -0.5f,  0.5f,  0.0f,  0.0f,  1.0f,
             0.5f, -0.5f,  0.5f,  0.0f,  0.0f,  1.0f,
             0.5f,  0.5f,  0.5f,  0.0f,  0.0f,  1.0f,
            -0.5f,  0.5f,  0.5f,  0.0f,  0.0f,  1.0f,
            // Back face
            -0.5f, -0.5f, -0.5f,  0.0f,  0.0f, -1.0f,
             0.5f, -0.5f, -0.5f,  0.0f,  0.0f, -1.0f,
             0.5f,  0.5f, -0.5f,  0.0f,  0.0f, -1.0f,
            -0.5f,  0.5f, -0.5f,  0.0f,  0.0f, -1.0f,
        };
        
        mesh.Indices = new uint[] {
            0, 1, 2,  2, 3, 0, // Front
            4, 5, 6,  6, 7, 4, // Back
            0, 4, 7,  7, 3, 0, // Left
            1, 5, 6,  6, 2, 1, // Right
            3, 2, 6,  6, 7, 3, // Top
            0, 1, 5,  5, 4, 0  // Bottom
        };
        
        model.Meshes.Add(mesh);
        return model;
    }
}
