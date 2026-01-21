using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using OpenTK.Mathematics;
using VectorMap.Core.Tiles;
using ImGuiNET;

namespace VectorMap.Desktop;

/// <summary>
/// Main map window using OpenTK
/// </summary>
public class MapWindow : GameWindow
{
    private readonly MapOptions _options;
    private Camera _camera = null!;
    private TileManager _tileManager = null!;
    private MapRenderer _renderer = null!;
    
    // Mouse state for panning
    private bool _isDragging;
    private bool _isRotating;
    private Vector2 _lastMousePos;
    
    // ImGui / Diagnostics
    private ImGuiController _imGuiController = null!;
    private Queue<float> _fpsHistory = new Queue<float>();
    private bool _showDiagnostics = true;
    private const int MaxFpsHistory = 100;
    private float[] _fpsArray = new float[MaxFpsHistory];
    
    public MapWindow(MapOptions options) 
        : base(
            GameWindowSettings.Default,
            new NativeWindowSettings
            {
                ClientSize = new Vector2i(options.Width, options.Height),
                Title = options.Title,
                API = ContextAPI.OpenGL,
                APIVersion = new Version(3, 3),
                Profile = ContextProfile.Core
            })
    {
        _options = options;
    }
    
    protected override void OnLoad()
    {
        base.OnLoad();
        
        // Set up OpenGL state
        GL.ClearColor(0.9f, 0.9f, 0.9f, 1.0f);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Enable(EnableCap.ProgramPointSize);
        
        // Initialize ImGui
        _imGuiController = new ImGuiController(ClientSize.X, ClientSize.Y);
        
        // Initialize camera
        _camera = new Camera(
            _options.CenterLng,
            _options.CenterLat,
            _options.Zoom,
            Size.X,
            Size.Y
        )
        {
            MinZoom = _options.MinZoom,
            MaxZoom = _options.MaxZoom
        };
        
        // Initialize tile manager
        _tileManager = new TileManager(
            _options.TileServerUrl,
            _options.Layers.Keys,
            _options.MaxTileZoom,
            _options.TileBuffer
        );
        
        // Initialize renderer with layer colors
        var colors = new Dictionary<string, Color4>();
        foreach (var kvp in _options.Layers)
        {
            colors[kvp.Key] = new Color4(kvp.Value[0]/255f, kvp.Value[1]/255f, kvp.Value[2]/255f, kvp.Value[3]/255f);
        }
        _renderer = new MapRenderer(colors);
        _renderer.Initialize();
        
        // Initial tile load
        UpdateTiles();
        
        Console.WriteLine($"Map initialized at [{_options.CenterLng}, {_options.CenterLat}] zoom {_options.Zoom}");
    }
    
    protected override void OnRenderFrame(FrameEventArgs e)
    {
        base.OnRenderFrame(e);
        
        GL.Clear(ClearBufferMask.ColorBufferBit);
        
        // Render tiles
        var tiles = _tileManager.GetRenderableTiles();
        _renderer.Render(_camera, tiles, _options.DisabledLayers);
        
        // Render UI
        _imGuiController.Update(this, (float)e.Time);
        
        if (_showDiagnostics)
        {
            float fps = (float)(1.0 / e.Time);
            _fpsHistory.Enqueue(fps);
            if (_fpsHistory.Count > MaxFpsHistory) _fpsHistory.Dequeue();
            
            // Copy to array for plotting
            int i = 0;
            foreach(var f in _fpsHistory) _fpsArray[i++] = f;
            
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowBgAlpha(0.7f);
            if (ImGui.Begin("Diagnostics", ref _showDiagnostics, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text($"FPS: {fps:F0}");
                ImGui.Text($"Zoom: {_camera.Zoom:F2}");
                ImGui.Text($"Pitch: {_camera.Pitch:F0} deg");
                ImGui.Text($"Bearing: {_camera.Bearing:F0} deg");
                ImGui.Separator();
                ImGui.PlotLines("##fps", ref _fpsArray[0], i, 0, $"Avg: {_fpsArray.Take(i).Average():F0}", 0, 120, new System.Numerics.Vector2(200, 50));
                
                ImGui.End();
            }
        }
        
        _imGuiController.Render();
        
        SwapBuffers();
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, Size.X, Size.Y);
        _imGuiController.WindowResized(Size.X, Size.Y);
        _camera.ViewportWidth = Size.X;
        _camera.ViewportHeight = Size.Y;
    }
    
    protected override void OnUpdateFrame(FrameEventArgs e)
    {
        base.OnUpdateFrame(e);
        
        // Handle keyboard input
        if (KeyboardState.IsKeyDown(Keys.Escape))
        {
            Close();
        }
        
        // Prune old tiles periodically
        _tileManager.PruneCache();
    }
    

    
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        
        if (e.Button == MouseButton.Left)
        {
            _isDragging = true;
            _lastMousePos = MousePosition;
        }
        else if (e.Button == MouseButton.Right)
        {
            _isRotating = true;
            _lastMousePos = MousePosition;
        }
    }
    
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        
        if (e.Button == MouseButton.Left)
        {
            _isDragging = false;
        }
        else if (e.Button == MouseButton.Right)
        {
            _isRotating = false;
        }
    }
    
    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        base.OnMouseMove(e);
        
        if (_isDragging)
        {
            float deltaX = e.X - _lastMousePos.X;
            float deltaY = e.Y - _lastMousePos.Y;
            
            // Save current position
            double prevX = _camera.X;
            double prevY = _camera.Y;
            
            _camera.Pan(-deltaX, deltaY, Size.X, Size.Y);
            
            // Undo if at limits
            if (_camera.IsAtLimits())
            {
                _camera.X = prevX;
                _camera.Y = prevY;
            }
            else
            {
                UpdateTiles();
            }
            
            _lastMousePos = MousePosition;
        }
        else if (_isRotating)
        {
            float deltaX = e.X - _lastMousePos.X;
            float deltaY = e.Y - _lastMousePos.Y;
            
            // Bearing (X delta)
            _camera.Bearing += deltaX * 0.5f;
            if (_camera.Bearing >= 360) _camera.Bearing -= 360;
            if (_camera.Bearing < 0) _camera.Bearing += 360;
            
            // Pitch (Y delta)
            _camera.Pitch += deltaY * 0.5f;
            
            // Clamp Pitch to 0-60 degrees
            _camera.Pitch = Math.Max(0, Math.Min(60, _camera.Pitch));
            
            UpdateTiles();
            _lastMousePos = MousePosition;
        }
    }
    
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        
        double prevZoom = _camera.Zoom;
        double prevX = _camera.X;
        double prevY = _camera.Y;
        
        _camera.ZoomAt(e.OffsetY * 0.5f, MousePosition.X, MousePosition.Y, Size.X, Size.Y);
        
        // Undo if at limits
        if (_camera.IsAtLimits())
        {
            _camera.Zoom = prevZoom;
            _camera.X = prevX;
            _camera.Y = prevY;
        }
        else
        {
            UpdateTiles();
        }
    }
    
    private void UpdateTiles()
    {
        var bounds = _camera.GetBounds();
        _tileManager.UpdateViewport(bounds, _camera.Zoom);
    }
    
    protected override void OnUnload()
    {
        _renderer.Dispose();
        _imGuiController.Dispose();
        base.OnUnload();
    }
}

/// <summary>
/// Configuration options for the map window
/// </summary>
public class MapOptions
{
    public int Width { get; set; } = 800;
    public int Height { get; set; } = 600;
    public string Title { get; set; } = "Vector Map";
    public double CenterLng { get; set; } = -73.9834558; // Brooklyn
    public double CenterLat { get; set; } = 40.6932723;
    public double Zoom { get; set; } = 13;
    public double MinZoom { get; set; } = 0;
    public double MaxZoom { get; set; } = 18;
    public int MaxTileZoom { get; set; } = 18;
    public int TileBuffer { get; set; } = 1;
    public string TileServerUrl { get; set; } = "https://maps.ckochis.com/data/v3/{z}/{x}/{y}.pbf";
    public HashSet<string> DisabledLayers { get; set; } = new();
    
    public Dictionary<string, byte[]> Layers { get; set; } = new()
    {
        { "water", new byte[] { 180, 240, 250, 255 } },
        { "landcover", new byte[] { 202, 246, 193, 255 } },
        { "park", new byte[] { 202, 255, 193, 255 } },
        { "transportation", new byte[] { 202, 0, 193, 255 } },
        { "housenumber", new byte[] { 100, 100, 100, 255 } },
        { "building", new byte[] { 185, 175, 139, 191 } }
    };
}
