#version 330 core

layout (location = 0) in vec2 aPosition;

uniform mat4 uMatrix;
uniform float uScale; // For over-zooming: scale factor (e.g., 2.0, 4.0)
uniform vec2 uOffset; // For over-zooming: offset within the tile (0.0-1.0)

void main()
{
    // Apply sub-tile transformation
    // Original pos is 0..1 relative to tile
    // New pos is (pos - offset) * scale
    // Wait, logic: if we zoom in to top-left quadrant (scale 2), we want 0..0.5 to become 0..1
    // So: newPos = (pos - offset) * scale
    vec2 pos = (aPosition - uOffset) * uScale;
    
    gl_PointSize = 3.0;
    gl_Position = uMatrix * vec4(pos, 0.0, 1.0);
}
