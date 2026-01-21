# Graphics Programming Bridge: WebGL to OpenTK
**Version: 0.2.0**

## A Deep Dive for Future Graphics Engineers

This document serves as a bridge for developers moving between WebGL (JavaScript) and OpenTK (.NET), while providing foundational knowledge for modern graphics programming. It details *why* we do things, not just *how*.

---

## 1. The Rendering Pipeline: From Code to Pixels

Understanding the pipeline is crucial for debugging. Whether in WebGL or OpenTK, the flow is identical:

1.  **Vertex Processing (Vertex Shader)**:
    *   **Input**: Raw coordinates from your VBO (e.g., Lat/Lng or simple X/Y).
    *   **Operation**: Multiply by Matrices (Model $\rightarrow$ View $\rightarrow$ Projection) to transform world coordinates into **Clip Space** $(-1.0 \text{ to } 1.0)$.
    *   **Output**: `gl_Position` (The final position on screen).

2.  **Rasterization (Fixed Function)**:
    *   The GPU takes the triangle defined by 3 clip-space vertices and figures out which pixels covers.
    *   It interpolates "varyings" (colors, texture coords) across the pixel surface.

3.  **Fragment Processing (Fragment Shader)**:
    *   **Input**: Interpolated data for *one specific pixel*.
    *   **Output**: The final RGBA color.

---

## 2. Memory: CPU vs GPU (Buffers)

One of the hardest concepts for beginners is that **OpenGL is a Client-Server architecture**.
*   **CPU (Client)**: Your C# or JS code.
*   **GPU (Server)**: The graphics card.

You cannot just "read" a C# array in a Shader. You must transfer it.

### The "VAO/VBO Dance"
1.  **VBO (Vertex Buffer Object)**: A distinct chunk of memory on the GPU RAM. You usage `GL.BufferData` to copy bytes from CPU $\rightarrow$ GPU.
2.  **VAO (Vertex Array Object)**: A "Save State" object. It remembers the configuration of *how* to read the VBO.
    *   *Without a VAO*, you would have to configure pointers (`GL.VertexAttribPointer`) every single frame.
    *   *With a VAO*: You configure once during setup. During render, you just bind the VAO, and OpenGL remembers "Attribute 0 is 2 floats, starting at byte 0".

**Difference**:
*   **WebGL**: Often abstracts some VAO complexity (OES_vertex_array_object extension in WebGL 1, standard in WebGL 2).
*   **OpenTK**: You usually manage this manually. **Always bind your VAO before drawing.**

---

## 3. The Mathematics of Matrices (MVP)

This is where 90% of graphics bugs happen (like our blank screen issue).

### Concept: The Transformations
$$ \text{Clip Position} = \text{Projection} \times \text{View} \times \text{Model} \times \text{Local Position} $$

1.  **Model Matrix**: Moves an object from local space $(0,0)$ to its world position.
2.  **View Matrix**: Moves the *entire world* so the camera ends up at $(0,0)$ looking forward.
    *   *Tip*: If the camera moves Right $(+10)$, the world must move Left $(-10)$. Thus, View Matrix is the **Inverse** of the Camera Matrix.
3.  **Projection Matrix**: Squishes the 3D world into the 2D box $[-1, 1]$ (Perspective or Orthographic).

### The Critical Difference: Component Layout
Computer memory is linear (1D array). A 4x4 Matrix (16 numbers) maps to this array differently.

| System | Layout in Memory | Vector Math | Operation Order (Code) |
|--------|------------------|-------------|-------------------------|
| **WebGL (gl-matrix)** | **Column-Major** | Column Vectors ($M \times v$) | `Use functions (typically right-to-left effect)` |
| **OpenTK (Matrix4)** | **Row-Major** | Row Vectors ($v \times M$) | `Scale * Translation` (Left-to-Right) |

**Visualization:**
If you want to **Scale** (S) an object then **Translate** (T) it:
*   **Math**: $P' = T \cdot S \cdot P$ (Translation happens "after" scale transformation applied to point)
*   **OpenTK Code**: `Matrix4 final = Matrix4.CreateScale(S) * Matrix4.CreateTranslation(T);` matches the reading order (Scale then Translate).

**Pro-Tip**: When porting JS code, **never assume `Matrix.Multiply` works the same way.** Always verify the logical order: "Do I want to rotate the object around its own center (Rotate then Translate), or orbit around the world origin (Translate then Rotate)?"

---

## 4. Coordinate Systems: Mercator vs OpenGL

Graphics cards don't understand "Latitude/Longitude" or "Meters". They only understand **Clip Space** $(-1 \text{ to } 1)$.

**The Challenge**:
*   **Geographic**: Longitude $(-180 \text{ to } +180)$, Latitude $(\approx -85 \text{ to } +85)$.
*   **Target**: $X [-1, 1], Y [-1, 1]$.

**Our Solution**:
1.  **Normalize**: Convert Lat/Lng to $0.0 \rightarrow 1.0$ (Web Mercator Projection).
2.  **Center**: Adjust to $-0.5 \rightarrow 0.5$ (relative to center).
3.  **Scale**: Multiply by Zoom Factor.
4.  **Result**: If the value is inside $[-1, 1]$, it's visible.

**Warning: Floating Point Precision**
At Zoom Level 20, world coordinates are huge, but differences are tiny $(0.000001)$. 32-bit Floats lose precision here, causing "jitter" (shaking vertices).
*   *Fix*: Keep coordinates relative to the Camera or Tile Center on the CPU (using Doubles), and only send small, relative values (Floats) to the GPU.

---

## 5. Debugging "Blank Screen" (The Black Arts)

When you see a blank screen, the GPU fails silently. Use this checklist:

1.  **The "Red Triangle" Test**: Ignore your complex data. Can you draw a single hard-coded red triangle?
    *   *Yes*: Your Shader/Window setup is good. The issue is your Data or Matrix.
    *   *No*: Your Shader didn't compile or context is invalid.
2.  **Coordinate Range**: Are your vertices inside $[-1, 1]$?
    *   Log the calculated `gl_Position` in the CPU before sending it.
    *   *Issue Found*: We found our coordinates were valid but tiny $(0.001)$. We needed to zoom in (fix Camera Matrix).
3.  **Backface Culling**: Is your triangle facing away from you?
    *   OpenGL defaults to counter-clockwise winding. If vertex order is flipped, the triangle is invisible.
    *   *Try*: `GL.Disable(EnableCap.CullFace)` to see if it appears.
4.  **Z-Testing**: Is it behind the camera?
    *   *Try*: `GL.Disable(EnableCap.DepthTest)` for 2D maps.

---

## Summary for the Developer

*   **WebGL** is about flexibility and browser integration.
*   **OpenTK** offers raw power and strict control.
*   **The Math** is universal, but **API Syntax** changes.
*   **Always** draw a debug triangle first.

---

## 6. Advanced Topics: UI and 3D Camera

### Integration UI (ImGui)
Adding a User Interface (buttons, charts) to a raw OpenGL context is difficult. We integrated **ImGui.NET** because it uses **Immediate Mode** rendering:
- **Retained Mode (WPF/WinForms)**: You create a Button object, and the framework remembers it.
- **Immediate Mode (ImGui)**: You say `if (Button("Click Me")) { ... }` every frame. The UI is rebuilt and drawn from scratch 60 times a second.
- **Benefit**: Extremely easy to sync with game state. No "event listeners" needed for simple debug tools.

### 3D Map Rendering
Moving from 2D to 3D tiles involves a Perspective Projection:
- **Pitch (Tilt)**: Rotating around the X-axis. We intentionally limit this to ~85° because viewing a flat map at 90° (edge-on) causes Z-fighting and geometry disappearance.
- **Matrix Order**: In OpenTK (Row Vectors), the order is critical:
  `World = Scale * Translate`
  `View = RotateZ(Bearing) * RotateX(Pitch) * Translate(0, 0, -Altitude)`
  This effectively "moves the world away" from the static camera.

### Interaction: Ray Casting (The "Drift" Fix)
When zooming in 2D, we simply unproject $(X, Y)$. In 3D (Pitched View), this fails because the distance from the camera to the "ground" varies across the screen.
*   **The Problem**: A naive unprojection assumes a constant Z (depth).
*   **The Solution**: **Ray-Plane Intersection**.
    1.  Convert mouse $(X, Y)$ to a **Ray** in world space (Near Point $\rightarrow$ Far Point).
    2.  Define the Map Plane as $Z = 0$.
    3.  Calculate the exact intersection $T$ where the Ray hits the Plane.
    4.  This gives the precise World Coordinate under the cursor, ensuring the map doesn't "slide" when zooming while tilted.

### Precision & Coordinate Systems
*   **Viewport Size**: Always use `ClientSize` (OpenTK) or `InnerWidth/Height` (Web). Using the full Window `Size` includes title bars and borders, causing a coordinate offset (e.g., mouse at perceived (0,0) is actually (0, 30)).
*   **Double vs Float**: GPU rendering works fine with `float` (Matrix4) because vertices are relative to the camera. However, for **Picking** (ScreenToWorld) at Zoom 20, the coordinate differences are smaller than `float` precision ($10^{-7}$).
    *   **Solution**: Implement a shadow `Matrix4d` (Double Precision) pipeline for CPU-side calculations (Picking/Panning). Do not rely on valid `Matrix4` inversion at high zoom levels.

---

## 7. WinForms Integration: Embedding the Map
In enterprise scenarios, the map often needs to be a **Control** inside a larger Dashboard, rather than a standalone window.

### Choice of Package
*   **Package**: `OpenTK.GLControl` (Version 4.0.2+).
*   *Note*: Avoid older `OpenTK.WinForms` prereleases if you encounter "WGL Failed" errors. `GLControl` is the current standard for embedding.

### Survival Guide for the WinForms Designer
The Visual Studio Designer **will crash** if your control tries to execute OpenGL code (it has no GPU context inside the designer process).

1.  **Safety Guards**: Always check `if (DesignMode) return;` at the start of `OnLoad`, `OnResize`, and `OnPaint`.
2.  **Lazy Initialization**: Do not call `renderer.Initialize()` in the constructor or `OnLoad`. Instead, initialize on the **first paint call** when the Window Handle is actually created and visible.
3.  **Handle Creation**: WinForms creates handles lazily. Wait for `IsHandleCreated == true` before calling `MakeCurrent()`.

### Modular Architecture (Core Project)
When moving from a standalone app to a Control, **Refactor Your Logic**:
- move `Camera`, `TileManager`, `MapRenderer`, and `MapOptions` into a `Core` class library.
- This allows both `VectorMap.Desktop` (OpenTK GameWindow) and `VectorMap.WinForms` (GLControl) to share the exact same rendering logic, ensuring a consistent user experience.

---

## 8. Polygon Triangulation & Rendering Order
Rendering complex map data (polygons with holes, overlapping layers) requires precision beyond simply "drawing triangles."

### The "Sliver" Artifact & Triangulation
When vector tiles are clipped, they often contain duplicate points, zero-length edges, or MVT `ClosePath` artifacts.
- **The Problem**: Passing "dirty" coordinates to a tessellator (like `LibTessDotNet`) causes degenerate triangles, resulting in "white strips" across the screen.
- **The Fix**: 
    1. **Clean the Data**: Remove consecutive duplicate points using an epsilon threshold.
    2. **Clip Loop Closure**: Explicitly detect and remove the trailing duplicate point from MVT rings before triangulation.
    3. **Robust Math**: Use `EvenOdd` winding and a **Combiner callback** to handle self-intersecting polygons gracefully.

### Layer-First Bathing (The Overlap Fix)
If you render tiles one-by-one, a building in Tile A might be drawn *before* the water in Tile B, leading to visual errors.
- **Old Strategy**: `foreach(tile) { foreach(layer) { draw } }` (Tile-First)
- **New Strategy**: `foreach(GlobalLayerOrder) { foreach(tile) { draw layer } }` (Layer-First)
- **Result**: Ensures that "Water" is drawn across the entire viewport before "Buildings" start, eliminating Z-fighting and incorrect overlaps without needing complex depth buffer management.

### Edge Smoothing (MSAA)
To remove "staircase" aliasing on building edges:
- Initialize `GLControl` with `GLControlSettings { NumberOfSamples = 4 }`.
## 9. 3D Model Integration & Realism
Rendering 3D models (GLB) on a 2D map requires bridging the gap between geographic coordinates and local 3D space.

### World-Scale 3D Positioning
In a Web Mercator world (0..1), 1 unit represents ~40 million meters.
- **The Problem**: A 1-meter 3D model would be invisible (1/40,000,000 of the world).
- **The Fix**: Calculate a local scale factor based on the **Cosine of the Latitude**. 
    - `scale = scaleMeters / (EarthCircumference * cos(latitude))`.
    - This ensures a house stays the same physical size whether it's in New York or London.

### Achieving Visual Realism (The PBR Lite Approach)
To make 3D models look "real" and move away from flat coloring:
1. **Blinn-Phong Lighting**: Add Specular highlights. By tracking the `uViewPos` (Camera position), the model's reflections shift naturally as the user rotates the map.
2. **sRGB Gamma Correction**: Most GLB models are authored in sRGB space. If you render them directly, they look "burnt" or "dark."
    - **Rule**: Convert `vColor` to Linear space (`pow(color, 2.2)`), calculate lighting, then convert back to sRGB (`pow(lighting, 1.0/2.2)`) before output.
3. **Depth Buffering**: Maps are usually 2.5D. When adding true 3D models, ensure `GL.Enable(EnableCap.DepthTest)` is toggled only for the 3D pass to prevent the model from getting "eaten" by the ground plane (or use a tiny Z-offset).

---

## 10. Performance & Async Architecture
Map rendering is CPU-intensive (parsing/tessellating) and IO-intensive (fetching tiles).

### Preventing UI Freezes
- **Offload Heavy Math**: Parsing MVTs and triangulating polygons should never happen on the UI thread. Use `await Task.Run(() => parser.Parse(data))` to keep the map panning smooth.
- **Redundant Request Prevention**: Maintain an `IsLoading` state in your tile cache. If the user pans the map quickly, this prevents firing 10 duplicate HTTP requests for the same tile before the first one finishes.
- **Async Asset Loading**: 3D models can be several megabytes. Load the geometry in the background and only upload to the GPU (VAO/VBO) on the Main thread once the data is ready.


