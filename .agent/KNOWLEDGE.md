# Developer Knowledge Base: Vietmap WebGL Render Optimization

This document preserves critical architectural decisions, "lessons learned," and implementation details for the Vietmap WebGL Map project.

## 1. Coordinate Systems & Projections

### Global Mercator vs. Tile-Space
- **Decision**: We use **Global Mercator Coordinates** for the internal geometry and rendering pipeline.
- **Rationale**: While tile-space (0..4096) is efficient for parsing, it introduces floating-point precision issues and "seam" artifacts when translating between tiles at high zoom levels. By keeping all coordinates in global Mercator space and using **Double-Precision Matrix Math (`Matrix4d`)** on the CPU, we achieve pixel-perfect stability without jitter.

## 2. Geometry Conversion ("The Optimized Algorithm")

### Indexed Bucket Batching
- **Efficiency**: Instead of drawing features one by one, we aggregate features into **Typed Buckets** (Polygon, Line, Point) per layer.
- **Indexing**: We use `LibTessDotNet` to triangulate polygons into unique vertices and index buffers. 
- **Offsetting**: When adding multiple features to a single bucket, the feature indices are automatically offset by the current bucket vertex count: `indices[i] = localIndex + (totalVertices / 2)`.
- **Cleaning**: Raw geometry from PBF must be cleaned (deduplicate consecutive points and ensure ring closure) before triangulation to prevent shader artifacts or "shattering."

## 3. High-Fidelity Interaction (The "Human Behavior" Logic)

### Ray-Casting Panning
- **Mechanism**: The map does not move by simple mouse deltas. It uses **Ray-Casting** to ensure the world point under the cursor stays fixed to that cursor.
- **Rotation/Pitch Resilience**: By using `ScreenToWorld` projection, panning remains 100% accurate even when the map is rotated (Bearing) or tilted (Pitch).
- **Inertia**: Implemented a velocity-based momentum system. On mouse release, the map continues to "slide" using recorded mouse velocity and simulated friction.

### Label Rendering & Collision Detection
- **Mechanism**: Labels (like `housenumber` and `name`) are extracted during MVT parsing and stored as `LabelInfo` with global Mercator coordinates.
- **Font Atlas**: A dynamic texture atlas is generated on-the-fly using `System.Drawing`. It captures glyph metrics (UVs, width, height) to allow batch rendering of text quads.
- **Collision Avoidance (AABB)**: To prevent map clutter, a **Spatial Collision Grid** (implemented via AABB list) checks each label's bounding box against already-placed labels on the screen.
- **Priority System**: Labels are rendered in order of priority (e.g., house numbers > park names) so that more important information is never hidden by less important labels.
- **Performance**: Text is batched into a single primitive stream and rendered using an **Orthographic Projection** overlay, keeping labels crisp regardless of camera pitch.

## 4. OpenGL Resource Management
- **VBO/EBO Strategy**: Use `BufferUsageHint.StreamDraw` for batch buffers. These are cleared and refilled every frame during interaction.
- **VAO State**: A single VAO is used for the map, managing the binding of both the VBO (vertices) and EBO (indices).
- **Depth Stacking**: Disable `DepthTest` for the map layers and use a `uDepth` uniform. This avoids Z-fighting between overlapping tiles or layers.

## 5. Viewport & Resource Loading
- **Debouncing**: Never update tile loading immediately during rotation or pitching. Use a **Debounce Timer (500ms)** to wait for the user to stop interacting before making expensive network requests for new tiles.
- **Caching**: The `TileManager` should prioritize rendering currently visible tiles while purging tiles outside the viewport buffer to maintain memory stability.
