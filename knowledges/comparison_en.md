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

