using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

using OpenTK.Windowing.Common;
using VectorMap.Core.Tiles;
using VectorMap.Core.Projection;
using VectorMap.Core;
using VectorMap.Core.Rendering;
using OpenTK.GLControl;

namespace VectorMap.WinForms
{
    public class MapControl : GLControl
    {
        private Camera _camera;
        private TileManager _tileManager;
        private MapRenderer _renderer;
        private ModelRenderer _modelRenderer;
        private Vector3 _modelPosition;
        private bool _isDragging;
        private bool _isRotating;
        private PointF _lastMousePos;
        
        // Simple diagnostics
        private Stopwatch _stopwatch = new Stopwatch();
        private int _frameCount;
        private double _fps;
        private long _lastFpsUpdate;
        private bool _isInitialized = false;
        private bool _modelLoaded = false;
        private bool _isModelLoading = false;
        private GLBModel? _pendingModel;
        private DateTime _lastViewportUpdate = DateTime.MinValue;
        private System.Windows.Forms.Timer _debounceTimer;
        private double _lastUpdateX, _lastUpdateY, _lastUpdateZoom, _lastUpdateBearing, _lastUpdatePitch;
        private Label _infoLabel;

        public MapControl() : base(new GLControlSettings { NumberOfSamples = 4 })
        {
            this.Dock = DockStyle.Fill;
        }

        protected override void OnLoad(EventArgs e)
        {
            if (DesignMode) return;
            base.OnLoad(e);
            
            // Critical check: Ensure handle is created
            if (!IsHandleCreated) return;
            
            _camera = new Camera(-73.9834558, 40.6932723, 13, ClientSize.Width, ClientSize.Height);
            
            // Tile Manager
            _tileManager = new TileManager(
                "https://maps.ckochis.com/data/v3/{z}/{x}/{y}.pbf",
                new string[] { "water", "landcover", "park", "transportation", "housenumber", "building" },
                14,
                1
            );

            // Set colors, but don't initialize OpenGL yet
            var colors = new Dictionary<string, Color4>
            {
                { "water", new Color4(170/255f, 211/255f, 223/255f, 1f) },
                { "landuse", new Color4(224/255f, 242/255f, 217/255f, 1f) },
                { "transportation", new Color4(255/255f, 150/255f, 255/255f, 1f) },
                { "building", new Color4(200/255f, 190/255f, 170/255f, 1f) },
                { "housenumber", new Color4(150/255f, 20/255f, 150/255f, 1f) }
            };
            _renderer = new MapRenderer(colors);
            
            // 3D Model initialization
            _modelRenderer = new ModelRenderer();
            var merc = MercatorCoordinate.FromLngLat(-73.9834558, 40.6932723);
            _modelPosition = new Vector3((float)merc.x, (float)merc.y, 0.000005f); // Slightly above ground
            
            _stopwatch.Start();

            // Start loading 3D model in background
            _ = LoadModelAsync();

            // Invalidate whenever a tile is loaded in the background
            _tileManager.TileLoaded += (coord, data) => {
                if (!IsDisposed && IsHandleCreated) Invalidate();
            };
            
            _isInitialized = true;

            // Info Label
            _infoLabel = new Label
            {
                AutoSize = true,
                BackColor = Color.FromArgb(180, Color.White),
                ForeColor = Color.Black,
                Location = new Point(10, 10),
                Font = new Font("Consolas", 10, FontStyle.Bold),
                Padding = new Padding(5)
            };
            this.Controls.Add(_infoLabel);

            // Debounce timer for viewport updates (500ms delay after interaction stops)
            _debounceTimer = new System.Windows.Forms.Timer();
            _debounceTimer.Interval = 500;
            _debounceTimer.Tick += (s, args) => {
                _debounceTimer.Stop();
                UpdateViewportImmediate();
            };
        }

        protected override void OnResize(EventArgs e)
        {
            if (DesignMode) return;
            base.OnResize(e);
            if (_camera != null)
            {
                _camera.ViewportWidth = ClientSize.Width;
                _camera.ViewportHeight = ClientSize.Height;
                if (IsHandleCreated) {
                    try { MakeCurrent(); GL.Viewport(0, 0, ClientSize.Width, ClientSize.Height); } catch {}
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (DesignMode || IsDisposed || !IsHandleCreated || !_isInitialized) return;
            
            base.OnPaint(e);
            
            try 
            {
                MakeCurrent();
            }
            catch (Exception)
            {
                return;
            }

            GL.ClearColor(Color.FromArgb(240, 240, 240));
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Intelligent Viewport Management
            double zoomDiff = Math.Abs(_camera.Zoom - _lastUpdateZoom);
            double distSq = Math.Pow(_camera.X - _lastUpdateX, 2) + Math.Pow(_camera.Y - _lastUpdateY, 2);
            double threshold = 0.5 / Math.Pow(2, _camera.Zoom); 
            
            bool isOutOfSync = zoomDiff > 0.01 || distSq > 1e-12 || 
                             Math.Abs(_camera.Bearing - _lastUpdateBearing) > 0.1 ||
                             Math.Abs(_camera.Pitch - _lastUpdatePitch) > 0.1;

            if (isOutOfSync)
            {
                // To avoid lag, we NEVER update tiles immediately during rotation or pitch.
                // We only force an immediate update for large Panning or Zooming changes.
                if (!_isRotating && (zoomDiff > 0.5 || distSq > threshold * threshold))
                {
                    UpdateViewportImmediate();
                    _debounceTimer.Stop(); 
                }
                else
                {
                    // Debounce (Wait for 0.5s stop)
                    _debounceTimer.Stop();
                    _debounceTimer.Start();
                }
            }
            else 
            {
                _debounceTimer.Stop();
            }

            // Render Map
            var tiles = _tileManager.GetRenderableTiles();
            _renderer.Render(_camera, tiles, new HashSet<string>());

            // Handle 3D Model logic
            if (!_modelLoaded) 
            {
                if (_pendingModel != null)
                {
                    // Upload to GPU (must be on GL thread)
                    _modelRenderer.LoadModel(_pendingModel);
                    _pendingModel = null;
                    _modelLoaded = true;
                }
            }

            _modelRenderer.Render(_camera, _modelPosition, 50.0f); // 50 meters wide

            // Update Info UI
            _infoLabel.Text = $"Zoom: {_camera.Zoom:F2}\nLat:  {_camera.Lat:F6}\nLng:  {_camera.Lng:F6}";

            // FPS Counter
            _frameCount++;
            long now = _stopwatch.ElapsedMilliseconds;
            if (now - _lastFpsUpdate >= 1000)
            {
                _fps = _frameCount * 1000.0 / (now - _lastFpsUpdate);
                _frameCount = 0;
                _lastFpsUpdate = now;
                Debug.WriteLine($"WinForms FPS: {_fps:F1}");
            }

            SwapBuffers();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) _isDragging = true;
            else if (e.Button == MouseButtons.Right) _isRotating = true;
            _lastMousePos = e.Location;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _isDragging = false;
            _isRotating = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isDragging)
            {
                float dx = e.X - _lastMousePos.X;
                float dy = e.Y - _lastMousePos.Y;
                _camera.Pan(-dx, dy, ClientSize.Width, ClientSize.Height);
                _lastMousePos = e.Location;
                Invalidate();
            }
            else if (_isRotating)
            {
                float dx = e.X - _lastMousePos.X;
                float dy = e.Y - _lastMousePos.Y;
                _camera.Bearing += dx * 0.5f;
                _camera.Pitch = Math.Clamp(_camera.Pitch + dy * 0.5f, 0, 85);
                _lastMousePos = e.Location;
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            float delta = e.Delta / 120.0f;
            _camera.ZoomAt(delta * 0.5f, e.X, e.Y, ClientSize.Width, ClientSize.Height);
            Invalidate();
        }
        private async Task LoadModelAsync()
        {
            if (_isModelLoading) return;
            _isModelLoading = true;

            try 
            {
                string glbPath = "D:\\RepoVietmap\\maps-api-group\\traning\\webgl-map\\VectorMapOpenTK\\VectorMap.Core\\Assets\\Models\\box.glb";
                if (System.IO.File.Exists(glbPath)) 
                {
                    _pendingModel = await GLBModel.LoadAsync(glbPath);
                } 
                else 
                {
                    _pendingModel = GLBModel.CreateCube();
                }
            } 
            catch 
            {
                _pendingModel = GLBModel.CreateCube();
            }
            finally
            {
                _isModelLoading = false;
            }
        }

        private void UpdateViewportImmediate()
        {
            if (_camera == null || _tileManager == null) return;
            
            _tileManager.UpdateViewport(_camera.GetBounds(), (int)_camera.Zoom);
            _lastUpdateX = _camera.X;
            _lastUpdateY = _camera.Y;
            _lastUpdateZoom = _camera.Zoom;
            _lastUpdateBearing = _camera.Bearing;
            _lastUpdatePitch = _camera.Pitch;
            _lastViewportUpdate = DateTime.Now;
            Invalidate(); 
        }
    }
}
