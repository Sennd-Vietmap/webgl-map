using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Drawing;
using VectorMap.Core.Tiles;

namespace VectorMap.Core.Rendering;

/// <summary>
/// Renders text labels with overlap detection (Collision System)
/// </summary>
public class LabelRenderer : IDisposable
{
    private int _shaderProgram;
    private int _vao, _vbo;
    private int _projectionLocation, _colorLocation, _depthLocation;
    private readonly List<RectangleF> _placedBoxes = new();
    private readonly FontAtlas _fontAtlas;

    public LabelRenderer()
    {
        Initialize();
        _fontAtlas = new FontAtlas();
    }

    private void Initialize()
    {
        string vSource = @"#version 410 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aTexCoord;
uniform mat4 uProjection;
uniform float uDepth;
out vec2 vTexCoord;
void main() {
    gl_Position = uProjection * vec4(aPos.x, aPos.y, uDepth, 1.0);
    vTexCoord = aTexCoord;
}";
        string fSource = @"#version 410 core
in vec2 vTexCoord;
out vec4 FragColor;
uniform sampler2D uTexture;
uniform vec4 uColor;
void main() {
    float alpha = texture(uTexture, vTexCoord).a;
    if (alpha < 0.1) discard;
    FragColor = vec4(uColor.rgb, alpha * uColor.a);
}";

        int vShader = CompileShader(ShaderType.VertexShader, vSource);
        int fShader = CompileShader(ShaderType.FragmentShader, fSource);
        _shaderProgram = GL.CreateProgram();
        GL.AttachShader(_shaderProgram, vShader);
        GL.AttachShader(_shaderProgram, fShader);
        GL.LinkProgram(_shaderProgram);

        _projectionLocation = GL.GetUniformLocation(_shaderProgram, "uProjection");
        _colorLocation = GL.GetUniformLocation(_shaderProgram, "uColor");
        _depthLocation = GL.GetUniformLocation(_shaderProgram, "uDepth");

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
    }

    private int CompileShader(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        return shader;
    }

    public void Render(Camera camera, IEnumerable<TileData> visibleTiles)
    {
        _placedBoxes.Clear();
        var vertices = new List<float>();

        // 1. Filter and Collect Labels across all tiles
        var allLabels = visibleTiles
            .SelectMany(t => t.Labels)
            .OrderByDescending(l => l.Priority);

        foreach (var label in allLabels)
        {
            // Project world to screen
            var (sx, sy) = camera.WorldToScreen(label.X, label.Y);
            
            // Basic viewport culling
            if (sx < 0 || sx > camera.ViewportWidth || 
                sy < 0 || sy > camera.ViewportHeight) continue;

            // Simple collision check (AABB)
            float h = 14;
            float w = label.Text.Length * 7.5f; 
            var box = new RectangleF((float)sx, (float)sy - h, w, h);

            bool overlaps = false;
            foreach (var placed in _placedBoxes)
            {
                if (placed.IntersectsWith(box))
                {
                    overlaps = true;
                    break;
                }
            }

            if (!overlaps)
            {
                _placedBoxes.Add(box);
                // Center text horizontally
                AddTextToBuffer(vertices, label.Text, (float)sx - (w/2), (float)sy, camera);
            }
        }

        if (vertices.Count == 0) return;

        // 2. Draw
        GL.UseProgram(_shaderProgram);
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.StreamDraw);

        var ortho = Matrix4.CreateOrthographicOffCenter(0, camera.ViewportWidth, camera.ViewportHeight, 0, -1, 1);
        GL.UniformMatrix4(_projectionLocation, false, ref ortho);
        GL.Uniform4(_colorLocation, new Color4(0.2f, 0.2f, 0.2f, 1.0f));
        GL.Uniform1(_depthLocation, 0.0f);

        GL.BindTexture(TextureTarget.Texture2D, _fontAtlas.TextureId);
        GL.DrawArrays(PrimitiveType.Triangles, 0, vertices.Count / 4);
    }

    private void AddTextToBuffer(List<float> buffer, string text, float x, float y, Camera camera)
    {
        float curX = x;
        foreach (char c in text)
        {
            if (!_fontAtlas.Glyphs.TryGetValue(c, out var glyph)) continue;

            float w = glyph.Width * 0.5f; // Scale down for rendering
            float h = glyph.Height * 0.5f;

            // Vertices: Pos(X,Y), UV(U,V)
            // Triangle 1
            buffer.AddRange(new[] { curX, y, glyph.U1, glyph.V1 });
            buffer.AddRange(new[] { curX + w, y, glyph.U2, glyph.V1 });
            buffer.AddRange(new[] { curX, y + h, glyph.U1, glyph.V2 });

            // Triangle 2
            buffer.AddRange(new[] { curX + w, y, glyph.U2, glyph.V1 });
            buffer.AddRange(new[] { curX + w, y + h, glyph.U2, glyph.V2 });
            buffer.AddRange(new[] { curX, y + h, glyph.U1, glyph.V2 });

            curX += w * 0.8f; // Kerning
        }
    }

    public void Dispose()
    {
        GL.DeleteVertexArray(_vao);
        GL.DeleteBuffer(_vbo);
        if (_shaderProgram != 0) GL.DeleteProgram(_shaderProgram);
        _fontAtlas.Dispose();
    }
}
