#version 450

layout(location = 0) in vec3 inPosition; // @vertex Position required
layout(location = 2) in vec4 inColor;    // @vertex Color optional @default = vec4(1.0, 1.0, 1.0, 1.0)

layout(push_constant) uniform Push { mat4 modelMatrix; } push;

layout(location = 0) out vec4 vColor;
layout(location = 1) out vec2 vUv; // object-space XZ; fragment divides by the per-shader tiling size

layout(set = 0, binding = 0) uniform VertexUniforms
{
    mat4 ViewProj;
} uVertex;

void main()
{
    gl_Position = uVertex.ViewProj * push.modelMatrix * vec4(inPosition, 1.0);
    vColor = inColor;
    vUv = inPosition.xz;
}
