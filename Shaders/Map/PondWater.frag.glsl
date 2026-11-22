#version 450

layout(location = 0) in vec4 vColor;
layout(location = 1) in vec2 vUvObj;  // object-space XZ
layout(location = 2) in vec4 vClip;   // clip-space position

layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _Color;                          // @color @default = vec4(1.0, 1.0, 1.0, 1.0)
    vec4 _WaterCastVectSun;               // .xyz cast vector (x,0,z), .w sun intensity; global, set by SkyManager
    vec4 _WaterCastVectMoon;              // .xyz cast vector (x,0,z), .w moon intensity; global, set by SkyManager
    vec4 _MainCameraScreenParams;         // (w, h, 1/w, 1/h). global, set by WaterInfo.SetTextures
    vec2 _MapSize;
    float _DayPercent;                    // global, set by SkyManager
    float _GameSeconds;                   // global, set by TickManager
    float _LightsourceShineSizeReduction; // global, set by SkyManager, an authored default would shadow it
    float _LightsourceShineIntensity;     // global, set by SkyManager
} u;

layout(set = 2, binding = 1) uniform sampler2D _MainTex;            // water lighting ramp (diffuse x specular)
layout(set = 2, binding = 2) uniform sampler2D _AlphaAddTex;        // noise for the rough terrain-edge fade
layout(set = 2, binding = 3) uniform sampler2D _WaterOutputTex;     // global: ripple height field (screen space)
layout(set = 2, binding = 4) uniform sampler2D _WaterReflectionTex; // global: cloud reflection
layout(set = 2, binding = 5) uniform sampler2D _NoiseTex;           // global: surface noise

vec2 flipV(vec2 uv) { return vec2(uv.x, 1.0 - uv.y); } 

const float WaterRippleIntensity = 0.002;
const float WaterDiffuseExaggerationNeg = 100.0;
const float WaterDiffuseExaggerationPos = 5.0;
const float PretendFov = 0.2;
const float SunDistanceReduction = 50.0;
const float ParallaxFactor = 0.2;
const float ReflectionIntensityDay = 0.04;
const float ReflectionIntensityNight = 0.06;
const float ReflectionDistance = 10.0;
const float ReflectionScale = 3.0;
const float ReflectionSpeed = 0.003;

float waterDiffuseIntensity(vec3 normal, vec3 lightDirection)
{
    float current = dot(-lightDirection, normal);
    float vertical = dot(-lightDirection, vec3(0.0, 0.0, 1.0));
    if (current < vertical)
        return pow(current / vertical, WaterDiffuseExaggerationNeg) * 0.5;            // 0 -> 0, vertical -> 0.5
    return (1.0 - pow(1.0 - (current - vertical) / (1.0 - vertical), WaterDiffuseExaggerationPos)) * 0.5 + 0.5; // vertical -> 0.5, 1 -> 1
}

float waterSpecularIntensity(vec3 normal, vec3 lightDirection, vec3 viewDirection)
{
    return pow(max(0.0, dot(reflect(-lightDirection, normal), viewDirection)), u._LightsourceShineSizeReduction) * u._LightsourceShineIntensity;
}

vec3 calculateViewDirection(vec2 pos)
{
    float fovPerPixel = PretendFov / max(u._MainCameraScreenParams.x, u._MainCameraScreenParams.y);
    return normalize(vec3(pos.x * u._MainCameraScreenParams.x * -fovPerPixel, pos.y * u._MainCameraScreenParams.y * -fovPerPixel, -1.0));
}

vec4 calculateWaterColor(float diffuse, float specular)
{
    // Sample on both axes so mipmapping does some of the heavy lifting
    return texture(_MainTex, vec2(diffuse, 1.0 - specular));
}

float roughFade(vec2 position, float rawAlpha)
{
    const float noiseImportance = 0.6;
    const float exPart = 1.0 - noiseImportance;
    float noise = texture(_AlphaAddTex, flipV(position * 2.0)).r * 0.333
                + texture(_AlphaAddTex, flipV(position * 5.0)).g * 0.333
                + texture(_AlphaAddTex, flipV(position * 10.0)).b * 0.333;
    float alpha = mix(rawAlpha, noise, noiseImportance);
    alpha -= (1.0 - exPart) * 0.5;
    alpha /= exPart;
    const float clampSpeed = 1.5;
    alpha = clamp(alpha, max(rawAlpha * clampSpeed - clampSpeed + 1.0, 0.0), min(rawAlpha * clampSpeed, 1.0));
    return alpha;
}

void main()
{
    vec2 screenBase = (vClip.xy / vClip.w) * 0.5 + 0.5;  // [0,1] height-field UV, vulkan y down like the ripple target it samples
    vec2 postex = vec2(vClip.x, -vClip.y) / vClip.w;      // unity's clip y is up, the view and parallax maths below assume it
    vec2 uv = vUvObj / 16.0;

    vec3 viewDirection = calculateViewDirection(postex);

    // Reconstruct the surface normal from the ripple height-field gradient (4-tap central difference).
    vec2 texelSize = 1.0 / vec2(textureSize(_WaterOutputTex, 0));
    float heighttl = texture(_WaterOutputTex, vec2(screenBase.x - texelSize.x * 0.5, screenBase.y - texelSize.y * 0.5)).r - 0.5;
    float heighttr = texture(_WaterOutputTex, vec2(screenBase.x + texelSize.x * 0.5, screenBase.y - texelSize.y * 0.5)).r - 0.5;
    float heightbl = texture(_WaterOutputTex, vec2(screenBase.x - texelSize.x * 0.5, screenBase.y + texelSize.y * 0.5)).r - 0.5;
    float heightbr = texture(_WaterOutputTex, vec2(screenBase.x + texelSize.x * 0.5, screenBase.y + texelSize.y * 0.5)).r - 0.5;

    float hdx = (heighttr + heightbr) - (heighttl + heightbl);
    float hdy = (heightbl + heightbr) - (heighttl + heighttr);

    float dist = length(vec2(dFdx(uv.x), dFdy(uv.y)));
    float colorA = max(vColor.a, 1e-4);
    vec3 normal = normalize(vec3(hdx, hdy, dist / colorA / WaterRippleIntensity));

    vec3 castVectorSun = normalize(vec3(-u._WaterCastVectSun.x, -u._WaterCastVectSun.z, -SunDistanceReduction));
    float specularIntensitySun = waterSpecularIntensity(normal, castVectorSun, viewDirection);
    float diffuseIntensitySun = waterDiffuseIntensity(normal, castVectorSun);

    vec3 castVectorMoon = normalize(vec3(-u._WaterCastVectMoon.x, -u._WaterCastVectMoon.z, -SunDistanceReduction));
    float specularIntensityMoon = waterSpecularIntensity(normal, castVectorMoon, viewDirection);
    float diffuseIntensityMoon = waterDiffuseIntensity(normal, castVectorMoon);

    float moonW = u._WaterCastVectMoon.w;
    float sunW = u._WaterCastVectSun.w;
    float lightDenom = max(moonW + sunW, 1e-4);
    vec4 litCol = mix(calculateWaterColor(diffuseIntensitySun, specularIntensitySun),
                      calculateWaterColor(diffuseIntensityMoon, specularIntensityMoon),
                      moonW / lightDenom);
    float sunAmount = length(vec2(moonW, sunW));
    vec4 finalCol = mix(calculateWaterColor(0.0, 0.0), litCol, sunAmount) * vColor;

    // Cloud reflections.
    float adjustedDay = 2.0 * mix(u._DayPercent, 1.0 - u._DayPercent, step(u._DayPercent, 0.5));
    vec2 aspect2 = vec2(1.0, u._MainCameraScreenParams.y / u._MainCameraScreenParams.x);
    float reflectionIntensity = mix(ReflectionIntensityNight, ReflectionIntensityDay, adjustedDay);
    vec2 reflectionUV = (mix(uv / 16.0, postex * aspect2 * 0.5 + 0.5, ParallaxFactor) + u._GameSeconds * ReflectionSpeed) * ReflectionScale + normal.xz * ReflectionDistance;
    finalCol.rgb += (1.0 - 2.0 * texture(_WaterReflectionTex, flipV(reflectionUV)).rgb) * reflectionIntensity;

    finalCol.a = roughFade(uv, finalCol.a);                                          // border fade
    finalCol.xyz = mix(finalCol.xyz, finalCol.xyz + texture(_NoiseTex, flipV(uv)).xyz - vec3(0.5), 0.025); // added noise

    outColor = finalCol * u._Color;
}
