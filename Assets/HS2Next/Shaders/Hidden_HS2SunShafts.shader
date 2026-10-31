// HS2 Studio's sun shafts under URP. Drawn by HS2SunShaftsFeature / HS2SunShaftsPass (Runtime/Rendering/Passes) as
// full-screen triangles after URP's post-processing (the image the effect reads is graded, bloomed, vignetted and
// anti-aliased); the passes in order:
//   0 Prefilter  full res, 8-bit sRGB target: far pixels only (linear 0..1 depth > 0.99: sky, background, clear colour)
//                give (sum over r, g, b of max(c - threshold, 0)) x saturate(maxRadius - |sun - uv|); the one-pixel frame
//                border is 0
//   1 Radial     full res, 8-bit sRGB target: the average of 6 bilinear taps from the pixel towards the sun, tap k at
//                uv + k (sun - uv) step (four draws with growing steps, ping-ponging two targets)
//   2 Composite  the scene colour + saturate(shaft x colour x intensity) per channel (add; alpha kept)
//   3 Debug      the shaft buffer as grey (tests, HS2SunShaftsFeature.DebugView)
// The 8-bit sRGB targets reproduce the precision of the original effect's temporaries (clamped to 0..1, 8 bits after
// the sRGB encoding). UV units make the look independent of the frame size. Parameters: _HS2ShaftSun (sun u, sun v,
// maxRadius, step), _HS2ShaftThreshold (rgb), _HS2ShaftColor (colour x intensity, rgba), _HS2ShaftSize (w, h).
Shader "HS2Next/Hidden/HS2SunShafts"
{
    Properties { }
    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        TEXTURE2D(_HS2ShaftTex);
        float4 _HS2ShaftSun;
        float4 _HS2ShaftThreshold;
        float4 _HS2ShaftColor;
        float4 _HS2ShaftSize;

        float4 FragPrefilter(Varyings i) : SV_Target
        {
            uint2 p = (uint2)i.positionCS.xy;
            uint2 size = (uint2)_HS2ShaftSize.xy;
            if (p.x == 0 || p.y == 0 || p.x + 1 >= size.x || p.y + 1 >= size.y) return 0;
            float d01 = Linear01Depth(SampleSceneDepth(i.texcoord), _ZBufferParams);
            if (!(d01 > 0.99)) return 0;
            float3 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, i.texcoord, 0).rgb;
            float dist = saturate(_HS2ShaftSun.z - length(_HS2ShaftSun.xy - i.texcoord));
            float v = dot(max(c - _HS2ShaftThreshold.rgb, 0), float3(1, 1, 1)) * dist;
            return float4(v, v, v, v);
        }

        float4 FragRadial(Varyings i) : SV_Target
        {
            float2 uv = i.texcoord;
            float2 delta = (_HS2ShaftSun.xy - uv) * _HS2ShaftSun.w;
            float4 sum = 0;
            [unroll] for (int k = 0; k < 6; k++)
                sum += SAMPLE_TEXTURE2D_LOD(_HS2ShaftTex, sampler_LinearClamp, uv + k * delta, 0);
            return sum / 6.0;
        }

        float4 FragComposite(Varyings i) : SV_Target
        {
            float4 scene = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, i.texcoord, 0);
            float4 shaft = SAMPLE_TEXTURE2D_LOD(_HS2ShaftTex, sampler_PointClamp, i.texcoord, 0);
            return float4(scene.rgb + saturate(shaft.rgb * _HS2ShaftColor.rgb), scene.a);
        }

        float4 FragDebug(Varyings i) : SV_Target
        {
            float s = SAMPLE_TEXTURE2D_LOD(_HS2ShaftTex, sampler_PointClamp, i.texcoord, 0).r;
            return float4(s, s, s, 1);
        }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        // The light mode only lets the player's shader variant collection list the passes (PlayerBuild.WriteShaderSet);
        // the feature draws them by index.
        Pass { Name "HS2ShaftsPrefilter" Tags { "LightMode"="HS2NextFullscreen" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragPrefilter
            ENDHLSL }
        Pass { Name "HS2ShaftsRadial" Tags { "LightMode"="HS2NextFullscreen" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragRadial
            ENDHLSL }
        Pass { Name "HS2ShaftsComposite" Tags { "LightMode"="HS2NextFullscreen" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL }
        Pass { Name "HS2ShaftsDebug" Tags { "LightMode"="HS2NextFullscreen" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDebug
            ENDHLSL }
    }
}
