// HS2 Studio's height fog over the opaque image (drawn by HS2FogFeature / HS2FogPass as one full-screen triangle at
// AfterRenderingSkybox, before the opaque-image copy and the transparents). Per pixel: the linear 01 depth and the world position from the depth texture and the camera's
// inverse view-projection; the opaque factor fA * fB of HS2NextFog.hlsl (fA the post-processing stack's distance fog, skipped
// on far pixels; fB the image-effect fog: radial distance and the half-space height term, far pixels kept when excludeFar);
// the output is the fog colour with alpha = the factor, blended as colour * (1 - factor) + scene * factor, so the camera
// colour becomes lerp(fog colour, scene, factor); its alpha is kept.
// Parameters (per draw): _HS2FogColor (linear rgb), _HS2FogMode (mode 1 linear / 2 exponential / 3 exponential squared,
// excludeFar, near, far), _HS2FogParams (density, start, end), _HS2FogHeight (fog top, height density).
Shader "HS2Next/Hidden/HS2Fog"
{
    Properties { }
    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "HS2NextFog.hlsl"

        float4 _HS2FogColor;
        float4 _HS2FogMode;
        float4 _HS2FogParams;
        float4 _HS2FogHeight;

        float4 FragFog(Varyings i) : SV_Target
        {
            float2 uv = i.texcoord;
            float raw = SampleSceneDepth(uv);
            float depth01 = Linear01Depth(raw, _ZBufferParams);
            float3 positionWS = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
            float f = HS2FogOpaqueFactor(depth01, _WorldSpaceCameraPos, positionWS, _HS2FogMode, _HS2FogParams, _HS2FogHeight);
            return float4(_HS2FogColor.rgb, f);
        }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Blend OneMinusSrcAlpha SrcAlpha, Zero One

        // The light mode only lets the player's shader variant collection list the pass (PlayerBuild.WriteShaderSet);
        // the feature draws it by index.
        Pass { Name "HS2Fog" Tags { "LightMode"="HS2NextFullscreen" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragFog
            ENDHLSL }
    }
}
