using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using VectorMap.Core.Tiles;

namespace VectorMap.Core.Rendering;

/// <summary>
/// OpenGL renderer for vector tile map layers
/// </summary>
public class MapRenderer : IDisposable
{
    private int _shaderProgram;
    private int _vao;
    private int _vbo;
    private int _ebo;
    private int _matrixLocation;
    private int _colorLocation;
    private int _scaleLocation;
    private int _offsetLocation;
    private int _depthLocation;
    
    private readonly Dictionary<string, Color4> _layerColors;
    private bool _isInitialized;
    private LabelRenderer? _labelRenderer;
    
    public MapRenderer(Dictionary<string, Color4>? layerColors = null)
    {
        _layerColors = layerColors ?? new Dictionary<string, Color4>
        {
            { "water", new Color4(180/255f, 240/255f, 250/255f, 1f) },
            { "landcover", new Color4(202/255f, 246/255f, 193/255f, 1f) },
            { "park", new Color4(202/255f, 255/255f, 193/255f, 1f) },
            { "building", new Color4(185/255f, 175/255f, 139/255f, 0.75f) },
            { "transportation", new Color4(255/255f, 255/255f, 255/255f, 1f) },
            { "housenumber", new Color4(50/255f, 50/255f, 50/255f, 1f) }
        };
    }
    
    /// <summary>
    /// Initialize OpenGL resources
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized) return;
        
        // Load and compile shaders - fall back to defaults if files not found
        string vertexSource = LoadShaderSource("Shaders/vertex.glsl");
        string fragmentSource = LoadShaderSource("Shaders/fragment.glsl");
        
        int vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
        int fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);
        
        // Create shader program
        _shaderProgram = GL.CreateProgram();
        GL.AttachShader(_shaderProgram, vertexShader);
        GL.AttachShader(_shaderProgram, fragmentShader);
        GL.LinkProgram(_shaderProgram);
        
        // Check for linking errors
        GL.GetProgram(_shaderProgram, GetProgramParameterName.LinkStatus, out int success);
        if (success == 0)
        {
            string infoLog = GL.GetProgramInfoLog(_shaderProgram);
            throw new Exception($"Shader program linking failed: {infoLog}");
        }
        
        // Clean up shaders (they're linked into the program now)
        GL.DetachShader(_shaderProgram, vertexShader);
        GL.DetachShader(_shaderProgram, fragmentShader);
        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);
        
        // Get uniform locations
        _matrixLocation = GL.GetUniformLocation(_shaderProgram, "uMatrix");
        _colorLocation = GL.GetUniformLocation(_shaderProgram, "uColor");
        _scaleLocation = GL.GetUniformLocation(_shaderProgram, "uScale");
        _offsetLocation = GL.GetUniformLocation(_shaderProgram, "uOffset");
        _depthLocation = GL.GetUniformLocation(_shaderProgram, "uDepth");
        
        // Create VAO and VBO
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        _ebo = GL.GenBuffer();
        
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        
        // Set up vertex attributes
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
        
        GL.BindVertexArray(0);
        
        _labelRenderer = new LabelRenderer();
        _isInitialized = true;
    }
    
    // Standard drawing order for map layers (bottom to top)
    private static readonly string[] GlobalLayerOrder = new[]
    {
        "background",
        "landcover",
        "park",
        "landuse",
        "water",
        "boundary",
        "transportation",
        "building",
        "housenumber",
        "label"
    };

    /// <summary>
    /// Render all tiles with the given camera
    /// </summary>
    public void Render(Camera camera, IEnumerable<TileData> tiles, HashSet<string>? disabledLayers = null)
    {
        if (!_isInitialized)
        {
            Initialize();
        }
        
        GL.Disable(EnableCap.DepthTest); // Map is 2D-stacked by uDepth
        GL.Enable(EnableCap.Multisample);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        
        GL.UseProgram(_shaderProgram);
        GL.BindVertexArray(_vao);
        
        // Set matrix uniform - use camera's view projection matrix
        Matrix4 matrix = camera.GetViewProjectionMatrix();
        GL.UniformMatrix4(_matrixLocation, false, ref matrix);
        
        // Set default scale/offset (1.0, 0.0) as vertices are already global
        GL.Uniform1(_scaleLocation, 1.0f);
        GL.Uniform2(_offsetLocation, 0.0f, 0.0f);
        
        // group all feature sets by layer across all tiles for batching
        var layerGroups = new Dictionary<string, List<FeatureSet>>();
        foreach (var tile in tiles)
        {
            foreach (var featureSet in tile.FeatureSets)
            {
                if (!layerGroups.TryGetValue(featureSet.LayerName, out var list))
                {
                    list = new List<FeatureSet>();
                    layerGroups[featureSet.LayerName] = list;
                }
                list.Add(featureSet);
            }
        }

        // Render layers in the predefined order
        float currentDepth = 0.0f;
        const float depthStep = 0.000001f;

        foreach (var layerName in GlobalLayerOrder)
        {
            if (layerGroups.TryGetValue(layerName, out var group))
            {
                GL.Uniform1(_depthLocation, currentDepth);
                RenderLayer(layerName, group, disabledLayers);
                layerGroups.Remove(layerName);
                currentDepth += depthStep;
            }
        }

        // 3. Render any remaining layers (unrecognized)
        foreach (var entry in layerGroups)
        {
            GL.Uniform1(_depthLocation, currentDepth);
            RenderLayer(entry.Key, entry.Value, disabledLayers);
            currentDepth += depthStep;
        }

        GL.BindVertexArray(0);
        GL.UseProgram(0);

        // 4. Render Labels on top
        if (_labelRenderer != null)
        {
            _labelRenderer.Render(camera, tiles);
        }

        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Less);
    }

    private void RenderLayer(string layerName, List<FeatureSet> featureSets, HashSet<string>? disabledLayers)
    {
        if (disabledLayers?.Contains(layerName) == true) return;
        if (!_layerColors.TryGetValue(layerName, out var color)) return;

        // Bucket for each primitive type
        var polyV = new List<float>();
        var polyI = new List<uint>();
        
        var lineV = new List<float>();
        var lineI = new List<uint>();
        
        var pointV = new List<float>();

        foreach (var fs in featureSets)
        {
            if (fs.Vertices.Length == 0) continue;
            
            uint baseIdx;
            switch (fs.Type)
            {
                case GeometryType.Polygon:
                    baseIdx = (uint)(polyV.Count / 2);
                    polyV.AddRange(fs.Vertices);
                    foreach (var idx in fs.Indices) polyI.Add(idx + baseIdx);
                    break;
                case GeometryType.Line:
                    baseIdx = (uint)(lineV.Count / 2);
                    lineV.AddRange(fs.Vertices);
                    foreach (var idx in fs.Indices) lineI.Add(idx + baseIdx);
                    break;
                case GeometryType.Point:
                    pointV.AddRange(fs.Vertices);
                    break;
            }
        }

        GL.Uniform4(_colorLocation, color.R, color.G, color.B, color.A);

        if (polyI.Count > 0) DrawIndexed(polyV, polyI, PrimitiveType.Triangles);
        if (lineI.Count > 0) DrawIndexed(lineV, lineI, PrimitiveType.Lines);
        if (pointV.Count > 0) DrawPoints(pointV);
    }

    private void DrawIndexed(List<float> v, List<uint> i, PrimitiveType type)
    {
        float[] vArr = v.ToArray();
        uint[] iArr = i.ToArray();
        
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vArr.Length * sizeof(float), vArr, BufferUsageHint.StreamDraw);
        
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, iArr.Length * sizeof(uint), iArr, BufferUsageHint.StreamDraw);
        
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        
        GL.DrawElements(type, iArr.Length, DrawElementsType.UnsignedInt, 0);
    }

    private void DrawPoints(List<float> v)
    {
        float[] vArr = v.ToArray();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vArr.Length * sizeof(float), vArr, BufferUsageHint.StreamDraw);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.DrawArrays(PrimitiveType.Points, 0, vArr.Length / 2);
    }
    
    /// <summary>
    /// Set color for a layer
    /// </summary>
    public void SetLayerColor(string layer, Color4 color)
    {
        _layerColors[layer] = color;
    }
    
    private static string LoadShaderSource(string path)
    {
        // Try to load from file first
        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }
        
        // Try relative to executable
        string exePath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
        string fullPath = Path.Combine(exePath, path);
        if (File.Exists(fullPath))
        {
            return File.ReadAllText(fullPath);
        }
        
        // Fallback to embedded shaders
        return path.Contains("vertex") ? 
            @"#version 330 core
layout (location = 0) in vec2 aPosition;
uniform mat4 uMatrix;
uniform float uScale;
uniform vec2 uOffset;
uniform float uDepth;
void main()
{
    vec2 pos = (aPosition - uOffset) * uScale;
    gl_PointSize = 3.0;
    gl_Position = uMatrix * vec4(pos, uDepth, 1.0);
}" :
            @"#version 330 core
out vec4 FragColor;
uniform vec4 uColor;
void main()
{
    FragColor = uColor;
}";
    }
    
    private static int CompileShader(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
        if (success == 0)
        {
            string infoLog = GL.GetShaderInfoLog(shader);
            throw new Exception($"Shader compilation failed ({type}): {infoLog}");
        }
        
        return shader;
    }
    
    public void Dispose()
    {
        if (_isInitialized)
        {
            GL.DeleteVertexArray(_vao);
            GL.DeleteBuffer(_vbo);
            GL.DeleteBuffer(_ebo);
            GL.DeleteProgram(_shaderProgram);
            _labelRenderer?.Dispose();
            _isInitialized = false;
        }
    }
}
