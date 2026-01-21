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
    private int _matrixLocation;
    private int _colorLocation;
    private int _scaleLocation;
    private int _offsetLocation;
    private int _depthLocation;
    
    private readonly Dictionary<string, Color4> _layerColors;
    private bool _isInitialized;
    
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
        
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        
        // Set up vertex attributes
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
        
        GL.BindVertexArray(0);
        
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
        
        // Render Tile-by-Tile to maintain high precision
        foreach (var tile in tiles)
        {
            // Compute a high-precision Tile-to-Clip matrix
            // This subtraction is done in DOUBLE to avoid jitter
            double worldSize = 512 * Math.Pow(2, camera.Zoom);
            double tileScale = 1.0 / Math.Pow(2, tile.Coordinate.Z);
            
            // Relative position of tile origin from camera center
            double relX = (tile.Coordinate.X - camera.X * Math.Pow(2, tile.Coordinate.Z)) * tileScale;
            double relY = (tile.Coordinate.Y - camera.Y * Math.Pow(2, tile.Coordinate.Z)) * tileScale;
            
            // Base matrix for this tile: local (0..1) -> world-relative -> clip
            // We reuse the Camera's View and Projection but replace the World translation
            Matrix4d tileMatrix = Matrix4d.CreateScale(tileScale * worldSize, -tileScale * worldSize, 1.0);
            tileMatrix *= Matrix4d.CreateTranslation((tile.Coordinate.X * tileScale - camera.X) * worldSize, (camera.Y - tile.Coordinate.Y * tileScale) * worldSize, 0);
            tileMatrix *= Matrix4d.CreateRotationZ(MathHelper.DegreesToRadians(camera.Bearing));
            tileMatrix *= Matrix4d.CreateRotationX(MathHelper.DegreesToRadians(-camera.Pitch));
            
            // Perspective / View Altitude part
            double fovVal = MathHelper.DegreesToRadians(60.0);
            double altitude = (camera.ViewportHeight / 2.0 / Math.Tan(fovVal / 2.0));
            tileMatrix *= Matrix4d.CreateTranslation(0, 0, -altitude);
            tileMatrix *= Matrix4d.CreatePerspectiveFieldOfView(fovVal, (double)camera.ViewportWidth / camera.ViewportHeight, 0.1, altitude * 100.0);

            Matrix4 floatMatrix = new Matrix4(
                (float)tileMatrix.Row0.X, (float)tileMatrix.Row0.Y, (float)tileMatrix.Row0.Z, (float)tileMatrix.Row0.W,
                (float)tileMatrix.Row1.X, (float)tileMatrix.Row1.Y, (float)tileMatrix.Row1.Z, (float)tileMatrix.Row1.W,
                (float)tileMatrix.Row2.X, (float)tileMatrix.Row2.Y, (float)tileMatrix.Row2.Z, (float)tileMatrix.Row2.W,
                (float)tileMatrix.Row3.X, (float)tileMatrix.Row3.Y, (float)tileMatrix.Row3.Z, (float)tileMatrix.Row3.W
            );

            GL.UniformMatrix4(_matrixLocation, false, ref floatMatrix);
            GL.Uniform1(_scaleLocation, 1.0f);
            GL.Uniform2(_offsetLocation, 0.0f, 0.0f);

            // Group features by layer WITHIN the tile
            var layerGroups = tile.FeatureSets.GroupBy(f => f.LayerName);
            float currentDepth = 0.0f;
            const float depthStep = 0.000001f;

            foreach (var orderLayer in GlobalLayerOrder)
            {
                var group = layerGroups.FirstOrDefault(g => g.Key == orderLayer);
                if (group != null)
                {
                    GL.Uniform1(_depthLocation, currentDepth);
                    RenderGroup(group.ToList(), disabledLayers);
                    currentDepth += depthStep;
                }
            }
        }
        
        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }

    private void RenderGroup(List<FeatureSet> featureSets, HashSet<string>? disabledLayers)
    {
        foreach (var featureSet in featureSets)
        {
            if (disabledLayers?.Contains(featureSet.LayerName) == true) continue;
            if (!_layerColors.TryGetValue(featureSet.LayerName, out var color)) continue;
            if (featureSet.Vertices.Length == 0) continue;
            
            GL.Uniform4(_colorLocation, color.R, color.G, color.B, color.A);
            
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, featureSet.Vertices.Length * sizeof(float), 
                featureSet.Vertices, BufferUsageHint.DynamicDraw);
            
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            
            PrimitiveType primitiveType = featureSet.Type switch
            {
                GeometryType.Point => PrimitiveType.Points,
                GeometryType.Line => PrimitiveType.Lines,
                _ => PrimitiveType.Triangles
            };
            
            GL.DrawArrays(primitiveType, 0, featureSet.Vertices.Length / 2);
        }
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
            GL.DeleteProgram(_shaderProgram);
            _isInitialized = false;
        }
    }
}
