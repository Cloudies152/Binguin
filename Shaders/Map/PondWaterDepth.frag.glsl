#version 450

layout(location = 0) in vec4 vColor;   // .a = water depth weight
layout(location = 1) in vec2 vUv;      // object-space (== world) XZ

layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec2 _MapSize;              // map size in cells (x, z); global, set by WaterInfo.SetTextures
    float _GameSeconds;         // animation clock; global
    float _WaterDepthIntensity; // @default = 1.0
    float _WaterRippleDensity;  // @default = 1.0
    float _UseWaterOffset;      // @default = 0.0 - rivers set this to 1 to read the flow map
} u;

layout(set = 2, binding = 1) uniform sampler2D _RippleTex;      // global (TexGame: Other/Ripples)
layout(set = 2, binding = 2) uniform sampler2D _WaterOffsetTex; // global (WaterInfo: river flow map)

const float WaterMovementSpeed = 0.00055;

float sampleHeight(vec2 originUv, vec2 flow, vec2 phaseOffsets, float rotate, float cycleOffset)
{
    mat2 rot = mat2(cos(rotate), sin(rotate), -sin(rotate), cos(rotate));
    vec2 uvRotated = rot * originUv + u._GameSeconds * 0.3333 * phaseOffsets * WaterMovementSpeed;

    float timeLoopScale = 4.0;
    float time = mod(u._GameSeconds * 0.3333 * 0.75, timeLoopScale);

    float phase0 = time + cycleOffset;
    float phase1 = time + cycleOffset - timeLoopScale;

    float heighta = textureLod(_RippleTex, uvRotated * u._WaterRippleDensity * 15.0 - flow * phase0 * 0.05, 0.0).r;
    float heightb = textureLod(_RippleTex, uvRotated * u._WaterRippleDensity * 15.0 - flow * phase1 * 0.05, 0.0).r;

    float flowLerp = smoothstep(0.0, 1.0, abs(phase0 / timeLoopScale));
    return mix(heighta, heightb, flowLerp);
}

void main()
{
    vec2 origin = vUv / u._MapSize;

    vec2 flow = vec2(0.0);
    if (u._UseWaterOffset > 0.5)
        flow = textureLod(_WaterOffsetTex, vec2(origin.x, 1.0 - origin.y), 0.0).xy; // set from unity ordered pixel rows, stored bottom row last

    float cycle = textureLod(_RippleTex, origin, 0.0).x * 2.0 - 1.0;

    float heighta = sampleHeight(origin, flow, vec2(0.0, 5.0), 0.3, cycle);
    float heightb = sampleHeight(origin, flow, vec2(2.1, 3.333), -0.35, cycle);
    float heightc = sampleHeight(origin, flow, vec2(-2.3, 1.877), -0.8, cycle);
    float heightd = sampleHeight(origin, flow, vec2(3.312, 0.444), 0.65, cycle);

    float height = (heighta * heightb * heightc * heightd - 0.0625) * u._WaterDepthIntensity;

    float depthWeight = vColor.a;
    outColor = vec4(height, 0.0, 0.0, depthWeight * depthWeight);
}
