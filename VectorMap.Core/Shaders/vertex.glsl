#version 330 core

layout (location = 0) in vec2 aPosition;

uniform mat4 uMatrix;
uniform float uScale; 
uniform vec2 uOffset; 
uniform float uDepth; // Small Z offset to prevent Z-fighting

void main()
{
    vec2 pos = (aPosition - uOffset) * uScale;
    
    gl_PointSize = 3.0;
    gl_Position = uMatrix * vec4(pos, uDepth, 1.0);
}
