// HS2Next's height fog, shader side (CPU reference: Runtime/Scene/Runtime/Effects/FogMath.cs).
//  - The forward fog of the fitted transparent shaders: HS2ApplyFog / HS2ApplyFogAdditive at the end of the fragment
//    function. It reads HS2Next's own globals (set per camera by HS2FogFeature; all zero when no camera draws the fog) and
//    is a uniform branch, not a shader keyword: no variant is added and the player cannot strip a fog variant. With the fog
//    off the colour is returned unchanged.
//    Distance = the clip-space form of the view depth, (depth - near) * far / (far - near); factor of the scene's fog mode
//    (1 linear, 2 exponential, 3 exponential squared, through exp2 as the engine's forward fog); colour toward the fog colour,
//    toward black for additive blending.
//    The term is gated per material by the material's own _HS2FogModes (bit 1 linear, 2 exponential, 4 exponential
//    squared): the fog modes its original was compiled for (an original without the variant of the scene's mode draws no
//    forward fog in HS2). The material switch sets it; 0 (the default) draws no forward fog. Each shader declares
//    _HS2FogModes in its own per-material block and passes it in.
//  - The opaque factor of the fog pass (HS2Next/Hidden/HS2Fog): HS2FogOpaqueFactor.
#ifndef HS2NEXT_FOG_INCLUDED
#define HS2NEXT_FOG_INCLUDED

// x: 1 = on, y: the scene's mode bit (1, 2, 4), z: near, w: far
float4 _HS2NextFogOn;
// x: density / sqrt(ln 2), y: density / ln 2, z: -1 / (end - start), w: end / (end - start)
float4 _HS2NextFogParams;
// rgb: the fog colour (linear)
float4 _HS2NextFogColor;

// the fog is drawn for this camera and the scene's mode is one of the material's modes
bool HS2FogActive(float materialModes)
{
    return _HS2NextFogOn.x > 0.5 && (((uint)_HS2NextFogOn.y) & (uint)materialModes) != 0u;
}

// the engine's forward fog factor of distance x for the mode bit
float HS2FogFactorOf(float x, uint modeBit)
{
    float f;
    if (modeBit == 1u) f = x * _HS2NextFogParams.z + _HS2NextFogParams.w;
    else if (modeBit == 2u) f = exp2(-_HS2NextFogParams.y * x);
    else { float t = _HS2NextFogParams.x * x; f = exp2(-t * t); }
    return saturate(f);
}

// the forward fog factor of a fragment at view depth eyeDepth (1 = no fog)
float HS2FogFactorDepth(float eyeDepth)
{
    float n = _HS2NextFogOn.z, fa = _HS2NextFogOn.w;
    float x = max((eyeDepth - n) * fa / max(fa - n, 1e-6), 0.0);
    return HS2FogFactorOf(x, (uint)_HS2NextFogOn.y);
}

// the forward fog factor of a fragment at world position positionWS
float HS2FogFactor(float3 positionWS)
{
    return HS2FogFactorDepth(-mul(UNITY_MATRIX_V, float4(positionWS, 1.0)).z);
}

// the forward fog factor of a fragment from its SV_Position (perspective cameras: the view depth from the depth value)
float HS2FogFactorCS(float4 positionCS)
{
    return HS2FogFactorDepth(LinearEyeDepth(positionCS.z, _ZBufferParams));
}

// colour toward the fog colour (alpha-blended and opaque-looking transparent passes)
float3 HS2ApplyFog(float3 color, float3 positionWS, float materialModes)
{
    UNITY_BRANCH
    if (HS2FogActive(materialModes))
        color = lerp(_HS2NextFogColor.rgb, color, HS2FogFactor(positionWS));
    return color;
}

// colour toward black (additive and premultiplied particle passes: the fog fades what they add)
float3 HS2ApplyFogAdditive(float3 color, float3 positionWS, float materialModes)
{
    UNITY_BRANCH
    if (HS2FogActive(materialModes))
        color = color * HS2FogFactor(positionWS);
    return color;
}

// the same from the fragment's SV_Position (for passes without a world position)
float3 HS2ApplyFogCS(float3 color, float4 positionCS, float materialModes)
{
    UNITY_BRANCH
    if (HS2FogActive(materialModes))
        color = lerp(_HS2NextFogColor.rgb, color, HS2FogFactorCS(positionCS));
    return color;
}

float3 HS2ApplyFogAdditiveCS(float3 color, float4 positionCS, float materialModes)
{
    UNITY_BRANCH
    if (HS2FogActive(materialModes))
        color = color * HS2FogFactorCS(positionCS);
    return color;
}

float3 HS2ApplyFogEitherCS(float3 color, float4 positionCS, float materialModes, bool towardBlack)
{
    UNITY_BRANCH
    if (HS2FogActive(materialModes))
        color = lerp(towardBlack ? float3(0, 0, 0) : _HS2NextFogColor.rgb, color, HS2FogFactorCS(positionCS));
    return color;
}

// toward the fog colour, or toward black when towardBlack (property-blend passes decide per material)
float3 HS2ApplyFogEither(float3 color, float3 positionWS, float materialModes, bool towardBlack)
{
    UNITY_BRANCH
    if (HS2FogActive(materialModes))
        color = lerp(towardBlack ? float3(0, 0, 0) : _HS2NextFogColor.rgb, color, HS2FogFactor(positionWS));
    return color;
}

// ---- the opaque pass (two terms, see FogMath): mode = (mode, excludeFar, near, far), p = (density, start, end),
// h = (height, height density)
float HS2FogFactorUnscaled(uint mode, float density, float start, float end, float x)   // the post-processing stack's form
{
    float f;
    if (mode == 1u) f = abs(end - start) > 1e-6 ? (end - x) / (end - start) : 0.0;
    else if (mode == 2u) f = exp2(-density * x);
    else { float t = density * x; f = exp2(-t * t); }
    return saturate(f);
}

float HS2FogFactorScaled(uint mode, float density, float start, float end, float x)     // the engine's form
{
    float f;
    if (mode == 1u)
    {
        float diff = end - start;
        float inv = abs(diff) > 0.0001 ? 1.0 / diff : 0.0;
        f = x * -inv + end * inv;
    }
    else if (mode == 2u) f = exp2(-(density * 1.4426950408) * x);
    else { float t = density * 1.2011224087 * x; f = exp2(-t * t); }
    return saturate(f);
}

float HS2FogOpaqueFactor(float depth01, float3 cameraWS, float3 positionWS, float4 mode, float4 p, float4 h)
{
    uint m = (uint)mode.x;
    float n = mode.z, fa = mode.w;
    // A: distance fog of the post-processing stack, sky / far pixels skipped; exponential squared also on linear scenes (as
    // HS2 draws it), exponential on exponential scenes
    uint mA = m == 2u ? 2u : 3u;
    float fA = depth01 < 0.9999 ? HS2FogFactorUnscaled(mA, p.x, p.y, p.z, depth01 * fa - n) : 1.0;
    // B: radial distance + half-space height fog; excludeFar keeps far pixels
    float3 V = positionWS - cameraWS;
    float g = length(V) - n;
    float fdotC = cameraWS.y - h.x;
    float k = fdotC <= 0.0 ? 1.0 : 0.0;
    float fdotP = positionWS.y - h.x;
    float c1 = k * (fdotP + fdotC);
    float c2 = (1.0 - 2.0 * k) * fdotP;
    float mm = min(c2, 0.0);
    g += -abs(h.y * 0.5) * length(V) * (c1 - mm * mm / abs(V.y + 1.0e-5));
    float fB = HS2FogFactorScaled(m, p.x, p.y, p.z, max(0.0, g));
    if (mode.y > 0.5 && depth01 >= 1.0) fB = 1.0;
    return fA * fB;
}

#endif
