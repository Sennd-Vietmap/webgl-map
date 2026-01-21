using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Collections.Generic;

namespace VectorMap.Core.Rendering;

public class ModelRenderer : IDisposable
{
    private int _shaderProgram;
    private int _matrixLocation;
    private int _modelMatrixLocation;
    private int _colorLocation;
    private int _lightPosLocation;
    
    private bool _isInitialized;
    private readonly List<(int vao, int vbo, int ebo, int count)> _glMeshes = new();

    public void Initialize()
    {
        if (_isInitialized) return;

        string vertexSource = @"#version 330 core
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aNormal;

uniform mat4 uMatrix;      // VP
uniform mat4 uModelMatrix; // M

out vec3 vNormal;
out vec3 vFragPos;

void main() {
    vNormal = mat3(transpose(inverse(uModelMatrix))) * aNormal;
    vFragPos = vec3(uModelMatrix * vec4(aPosition, 1.0));
    gl_Position = uMatrix * vec4(vFragPos, 1.0);
}";

        string fragmentSource = @"#version 330 core
in vec3 vNormal;
in vec3 vFragPos;
out vec4 FragColor;

uniform vec4 uColor;
uniform vec3 uLightPos;

void main() {
    vec3 norm = normalize(vNormal);
    vec3 lightDir = normalize(uLightPos - vFragPos);
    float diff = max(dot(norm, lightDir), 0.2); // 0.2 ambient
    FragColor = uColor * diff;
    FragColor.a = uColor.a;
}";

        _shaderProgram = CompileProgram(vertexSource, fragmentSource);
        _matrixLocation = GL.GetUniformLocation(_shaderProgram, "uMatrix");
        _modelMatrixLocation = GL.GetUniformLocation(_shaderProgram, "uModelMatrix");
        _colorLocation = GL.GetUniformLocation(_shaderProgram, "uColor");
        _lightPosLocation = GL.GetUniformLocation(_shaderProgram, "uLightPos");

        _isInitialized = true;
    }

    public void LoadModel(GLBModel model)
    {
        Initialize();
        foreach (var mesh in model.Meshes)
        {
            int vao = GL.GenVertexArray();
            int vbo = GL.GenBuffer();
            int ebo = GL.GenBuffer();

            GL.BindVertexArray(vao);
            
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, mesh.Vertices.Length * sizeof(float), mesh.Vertices, BufferUsageHint.StaticDraw);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, mesh.Indices.Length * sizeof(uint), mesh.Indices, BufferUsageHint.StaticDraw);

            // Position (x, y, z)
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
            
            // Normal (nx, ny, nz)
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));

            _glMeshes.Add((vao, vbo, ebo, mesh.Indices.Length));
        }
        GL.BindVertexArray(0);
    }

    public void Render(Camera camera, Vector3 position, float scaleMeters, Color4 color)
    {
        if (!_isInitialized || _glMeshes.Count == 0) return;

        GL.UseProgram(_shaderProgram);
        GL.Enable(EnableCap.DepthTest);

        Matrix4 vp = camera.GetViewProjectionMatrix();
        GL.UniformMatrix4(_matrixLocation, false, ref vp);

        // Calculate scale in Mercator units
        // Earth circum / 360 = ~111km per deg at equator.
        // But our world is 0..1, which is 40,075,017 meters.
        double latRad = MathHelper.DegreesToRadians((float)camera.Lat);
        float scaleFactor = (float)(scaleMeters / (40075017.0 * Math.Cos(latRad)));
        
        Matrix4 modelMatrix = Matrix4.CreateScale(scaleFactor) * Matrix4.CreateTranslation(position);
        GL.UniformMatrix4(_modelMatrixLocation, false, ref modelMatrix);
        
        GL.Uniform4(_colorLocation, color);
        GL.Uniform3(_lightPosLocation, position + new Vector3(0.01f, 0.01f, 0.1f));

        foreach (var mesh in _glMeshes)
        {
            GL.BindVertexArray(mesh.vao);
            GL.DrawElements(PrimitiveType.Triangles, mesh.count, DrawElementsType.UnsignedInt, 0);
        }

        GL.BindVertexArray(0);
        GL.Disable(EnableCap.DepthTest);
    }

    private int CompileProgram(string vs, string fs)
    {
        int v = CompileShader(ShaderType.VertexShader, vs);
        int f = CompileShader(ShaderType.FragmentShader, fs);
        int p = GL.CreateProgram();
        GL.AttachShader(p, v);
        GL.AttachShader(p, f);
        GL.LinkProgram(p);
        return p;
    }

    private int CompileShader(ShaderType type, string src)
    {
        int s = GL.CreateShader(type);
        GL.ShaderSource(s, src);
        GL.CompileShader(s);
        return s;
    }

    public void Dispose()
    {
        foreach (var mesh in _glMeshes)
        {
            GL.DeleteVertexArray(mesh.vao);
            GL.DeleteBuffer(mesh.vbo);
            GL.DeleteBuffer(mesh.ebo);
        }
        GL.DeleteProgram(_shaderProgram);
    }
}
