// The scene's background and frame images under URP (R17; Phase 3.6 package C). Drawn
// by HS2BackgroundFeature and HS2FrameFeature (Runtime/Rendering/Passes) as one full-screen triangle each:
//   0 Background  after the skybox: the image stretched over the camera's whole view (HS2's quad fills the frustum, so
//                 the stretch is the same at any FOV and aspect) at the depth of view z = 0.98 × far (HS2's farRate),
//                 tested against the opaques' depth (the camera depth texture: a nearer opaque keeps its pixel; the sky,
//                 which writes depth at its dome under URP, does not count) and written (the transparents are sorted
//                 against it); unlit, opaque (the image's alpha is ignored, as HS2's opaque quad)
//   1 Frame       after post-processing: the frame image over the whole target, alpha-blended (PNG alpha shows the scene)
// _HS2ImageTex: the sRGB texture (sampled to linear); _HS2ImageDepth: the NDC depth of the background plane (from the GPU
// projection; reversed-Z aware); _BlitScaleBias as URP's Blit.hlsl.
Shader "HS2Next/Hidden/HS2SceneImage"
{
    Properties { }
    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        TEXTURE2D(_HS2ImageTex);
        SAMPLER(sampler_HS2ImageTex);
        float _HS2ImageDepth;

        Varyings VertAtDepth(Attributes input)
        {
            Varyings o;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID, _HS2ImageDepth);
            float2 uv = GetFullScreenTriangleTexCoord(input.vertexID);
            o.texcoord = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;
            return o;
        }

        float4 FragBackground(Varyings i) : SV_Target
        {
            // an opaque nearer than the plane keeps its pixel (relative margin: the plane's own depth is exact)
            float scene = SampleSceneDepth(i.texcoord);
        #if UNITY_REVERSED_Z
            if (scene > _HS2ImageDepth * 1.0001 + 1e-9) discard;
        #else
            if (scene < _HS2ImageDepth - (1 - _HS2ImageDepth) * 0.0001 - 1e-9) discard;
        #endif
            return float4(SAMPLE_TEXTURE2D_LOD(_HS2ImageTex, sampler_HS2ImageTex, i.texcoord, 0).rgb, 1);
        }

        float4 FragFrame(Varyings i) : SV_Target
        {
            return SAMPLE_TEXTURE2D_LOD(_HS2ImageTex, sampler_HS2ImageTex, i.texcoord, 0);
        }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off

        // The light mode only lets the player's shader variant collection list the passes (PlayerBuild.WriteShaderSet);
        // the features draw them by index.
        Pass { Name "HS2Background" Tags { "LightMode"="HS2NextFullscreen" }
            ZWrite On ZTest Always Blend Off
            HLSLPROGRAM
            #pragma vertex VertAtDepth
            #pragma fragment FragBackground
            ENDHLSL }
        Pass { Name "HS2Frame" Tags { "LightMode"="HS2NextFullscreen" }
            ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha, Zero One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragFrame
            ENDHLSL }
    }
}
