#version 330 core

layout (location = 0) in vec2 aPosition;

uniform mat4 uMatrix;

void main()
{
    gl_PointSize = 3.0;
    gl_Position = uMatrix * vec4(aPosition, 0.0, 1.0);
}
