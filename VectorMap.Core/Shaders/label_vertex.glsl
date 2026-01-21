#version 410 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aTexCoord;

uniform mat4 uProjection;
uniform vec2 uOffset; // World pixel offset
uniform float uDepth;

out vec2 vTexCoord;

void main() {
    gl_Position = uProjection * vec4(aPos.x, aPos.y, uDepth, 1.0);
    vTexCoord = aTexCoord;
}
