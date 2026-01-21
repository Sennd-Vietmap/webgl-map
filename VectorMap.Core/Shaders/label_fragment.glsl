#version 410 core
in vec2 vTexCoord;
out vec4 FragColor;

uniform sampler2D uTexture;
uniform vec4 uColor;

void main() {
    float alpha = texture(uTexture, vTexCoord).a;
    if (alpha < 0.1) discard;
    FragColor = vec4(uColor.rgb, alpha * uColor.a);
}
