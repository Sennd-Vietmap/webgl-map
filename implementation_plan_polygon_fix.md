# Implementation Plan: Fix Polygon Rendering Issues

The objective is to eliminate visual artifacts (slivers/strips) and fix incorrect layer overlapping by improving the triangulation pipeline and enforcing drawing order.

## Problem Analysis
- **Ordering Issues**: Layers are currently rendered tile-by-tile in random dictionary order. This causes Z-fighting and "wrong" overlaps (e.g., water on top of buildings).
- **Geometric Slivers**: The "strips" in the screenshot are caused by:
    - **Duplicate Points**: MVT `ClosePath` adds a point identical to the first. `LibTessDotNet` can struggle with these zero-length edges.
    - **Degenerate Rings**: Zero-area rings or rings with < 3 points can cause triangulation failure or stray triangles.
- **Rendering Efficiency**: Current tile-first rendering is less efficient than layer-first rendering.

## Proposed Changes

### 1. Deterministic "Layer-First" Rendering (`MapRenderer.cs`)
- Define a `GlobalLayerOrder` (e.g., land, water, roads, buildings).
- Change the rendering loop to:
    1. Group all `FeatureSet` objects from all currently loaded tiles by `LayerName`.
    2. Iterate through the `GlobalLayerOrder`.
    3. Render all features for that layer across all tiles in one batch.
    4. Render any unknown layers at the end.

### 2. Robust Triangulation (`GeometryConverter.cs`)
- **Cleaning Pass**: 
    - Remove consecutive duplicate points.
    - Detect and remove trailing duplicates (ClosePath artifacts).
    - Filter out rings with fewer than 3 unique points.
- **Tessellation**: Use `EvenOdd` winding (most robust for overlapping MVT rings).
- **Safety**: Add basic bounds checks for vertex indices.

## Step-by-Step Implementation

### Step 1: Update `MapRenderer.cs`
- Define `GlobalLayerOrder`.
- Refactor `Render` to group features by layer across all tiles.
- Implement `RenderFeatureSets` helper.

### Step 2: Update `GeometryConverter.cs`
- Add `CleanRing` helper.
- Update `PolygonToVertices` to use the cleaning pass.
- Improve `LineToVertices` to skip degenerate segments.

### Step 3: Verification
- Build and run `VectorMap.WinForms`.
- Verify the "white strip" artifact is gone.
- Verify that roads/buildings correctly overlap the ground and water.
