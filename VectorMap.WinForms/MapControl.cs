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
        private bool _isDragging;
        private bool _isRotating;
        private PointF _lastMousePos;
        
        // Simple diagnostics
        private Stopwatch _stopwatch = new Stopwatch();
        private int _frameCount;
        private double _fps;
        private long _lastFpsUpdate;
        private bool _isInitialized = false;

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
            
            _stopwatch.Start();

            // Re-render as fast as possible
            Application.Idle += (s, args) => {
                if (!IsDisposed && IsHandleCreated && !DesignMode) Invalidate();
            };
            
            _isInitialized = true;
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

            // Update Tiles
            _tileManager.UpdateViewport(_camera.GetBounds(), (int)_camera.Zoom);

            // Render
            var tiles = _tileManager.GetRenderableTiles();
            _renderer.Render(_camera, tiles, new HashSet<string>());

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
            }
            else if (_isRotating)
            {
                float dx = e.X - _lastMousePos.X;
                float dy = e.Y - _lastMousePos.Y;
                _camera.Bearing += dx * 0.5f;
                _camera.Pitch = Math.Clamp(_camera.Pitch + dy * 0.5f, 0, 85);
                _lastMousePos = e.Location;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            float delta = e.Delta / 120.0f;
            _camera.ZoomAt(delta * 0.5f, e.X, e.Y, ClientSize.Width, ClientSize.Height);
        }
    }
}
