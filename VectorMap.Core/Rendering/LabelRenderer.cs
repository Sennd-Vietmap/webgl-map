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
    private readonly bool[] _collisionGrid = new bool[256 * 256]; // 64k grid cells (approx 10x10 px each)
    private readonly float[] _vertexBuffer = new float[200000]; // 50k vertices (enough for ~8000 characters)
    private readonly FontAtlas _fontAtlas;
    private (double X, double Y, double Zoom, double Bearing, double Pitch) _lastCameraState;

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

    private int _lastVertexCount = 0;

    public void Render(Camera camera, IEnumerable<TileData> visibleTiles)
    {
        // 1. Check if we can skip processing
        bool cameraChanged = camera.X != _lastCameraState.X || camera.Y != _lastCameraState.Y || 
                            camera.Zoom != _lastCameraState.Zoom || camera.Bearing != _lastCameraState.Bearing ||
                            camera.Pitch != _lastCameraState.Pitch;

        if (cameraChanged)
        {
            Array.Clear(_collisionGrid, 0, _collisionGrid.Length);
            _lastCameraState = (camera.X, camera.Y, camera.Zoom, camera.Bearing, camera.Pitch);
            _lastVertexCount = 0;

            const int gridCols = 120;
            const int gridRows = 100;
            float vWidth = (float)camera.ViewportWidth;
            float vHeight = (float)camera.ViewportHeight;

            if (vWidth <= 0 || vHeight <= 0) return;

            // PERFORMANCE: Limit total labels to process to avoid frame spikes
            int processedCount = 0;
            const int maxLabelsToProcess = 2000;

            foreach (var tile in visibleTiles)
            {
                var labels = tile.Labels;
                int labelCount = labels.Count;
                
                for (int i = 0; i < labelCount; i++)
                {
                    var label = labels[i];
                    
                    // Fast Viewport Cull (Approximate screen projection check before heavy math)
                    var (sx, sy) = camera.WorldToScreen(label.X, label.Y);
                    if (sx < -20 || sx > vWidth + 20 || sy < -20 || sy > vHeight + 20) continue;

                    float h = 14;
                    float w = label.Text.Length * 7.5f; 
                    
                    // Grid mapping
                    int startCol = (int)((sx - w * 0.5f) * gridCols / vWidth);
                    int endCol = (int)((sx + w * 0.5f) * gridCols / vWidth);
                    int startRow = (int)((sy - h) * gridRows / vHeight);
                    int endRow = (int)(sy * gridRows / vHeight);

                    // Clamp
                    if (startCol < 0) startCol = 0; if (endCol >= gridCols) endCol = gridCols - 1;
                    if (startRow < 0) startRow = 0; if (endRow >= gridRows) endRow = gridRows - 1;

                    bool overlaps = false;
                    for (int r = startRow; r <= endRow; r++)
                    {
                        int rowOffset = r * gridCols;
                        for (int c = startCol; c <= endCol; c++)
                        {
                            if (_collisionGrid[rowOffset + c]) { overlaps = true; break; }
                        }
                        if (overlaps) break;
                    }

                    if (!overlaps)
                    {
                        for (int r = startRow; r <= endRow; r++)
                        {
                            int rowOffset = r * gridCols;
                            for (int c = startCol; c <= endCol; c++)
                                _collisionGrid[rowOffset + c] = true;
                        }

                        _lastVertexCount += AddTextToBuffer(label.Text, (float)sx - (w * 0.5f), (float)sy, _lastVertexCount);
                        
                        if (++processedCount >= maxLabelsToProcess) break;
                    }
                }
                if (processedCount >= maxLabelsToProcess) break;
            }

            if (_lastVertexCount > 0)
            {
                GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
                GL.BufferData(BufferTarget.ArrayBuffer, _lastVertexCount * sizeof(float), _vertexBuffer, BufferUsageHint.StreamDraw);
            }
        }

        if (_lastVertexCount == 0) return;

        // 3. Draw
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        
        GL.UseProgram(_shaderProgram);
        GL.BindVertexArray(_vao);

        var ortho = Matrix4.CreateOrthographicOffCenter(0, camera.ViewportWidth, camera.ViewportHeight, 0, -1, 1);
        GL.UniformMatrix4(_projectionLocation, false, ref ortho);
        GL.Uniform4(_colorLocation, new Color4(0.1f, 0.1f, 0.1f, 1.0f));
        GL.Uniform1(_depthLocation, 0.0f);

        GL.BindTexture(TextureTarget.Texture2D, _fontAtlas.TextureId);
        GL.DrawArrays(PrimitiveType.Triangles, 0, _lastVertexCount / 4);
    }

    private int AddTextToBuffer(string labelText, float x, float y, int offset)
    {
        int initialOffset = offset;
        float curX = x;
        foreach (char c in labelText)
        {
            if (!_fontAtlas.Glyphs.TryGetValue(c, out var glyph)) continue;

            float w = glyph.Width * 0.5f;
            float h = glyph.Height * 0.5f;

            if (offset + 24 >= _vertexBuffer.Length) break;

            // Vertices: Pos(X,Y), UV(U,V)
            // Triangle 1
            _vertexBuffer[offset++] = curX;      _vertexBuffer[offset++] = y;     _vertexBuffer[offset++] = glyph.U1; _vertexBuffer[offset++] = glyph.V1;
            _vertexBuffer[offset++] = curX + w;  _vertexBuffer[offset++] = y;     _vertexBuffer[offset++] = glyph.U2; _vertexBuffer[offset++] = glyph.V1;
            _vertexBuffer[offset++] = curX;      _vertexBuffer[offset++] = y + h; _vertexBuffer[offset++] = glyph.U1; _vertexBuffer[offset++] = glyph.V2;

            // Triangle 2
            _vertexBuffer[offset++] = curX + w;  _vertexBuffer[offset++] = y;     _vertexBuffer[offset++] = glyph.U2; _vertexBuffer[offset++] = glyph.V1;
            _vertexBuffer[offset++] = curX + w;  _vertexBuffer[offset++] = y + h; _vertexBuffer[offset++] = glyph.U2; _vertexBuffer[offset++] = glyph.V2;
            _vertexBuffer[offset++] = curX;      _vertexBuffer[offset++] = y + h; _vertexBuffer[offset++] = glyph.U1; _vertexBuffer[offset++] = glyph.V2;

            curX += w * 0.8f; 
        }
        return offset - initialOffset;
    }

    public void Dispose()
    {
        GL.DeleteVertexArray(_vao);
        GL.DeleteBuffer(_vbo);
        if (_shaderProgram != 0) GL.DeleteProgram(_shaderProgram);
        _fontAtlas.Dispose();
    }
}
