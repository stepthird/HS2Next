// URP replacement for HS2 "Shader Forge/hair_07" (hair shader type 0 "dithering", material hair_dithering in
// chara/hair_shader_mat.unity3d, swapped onto every hair part at load with the part's renderQueue kept).
// Same property names, types and defaults as the original. Behaviour inferred by black-box probing only
// (Assets/HS2Next/Editor/Fit/SkinFit.cs, Tests/FitCases/hair07.txt and hair_ring_profiles.txt,
// Spike/out/fit/Shader_Forge_hair_07_*.txt); the math lives in HS2NextHair.hlsl. "A" below is in output units
// (what the original writes under a flat white ambient with metallic 0, equal to its response to a unit light along N).
//  Coverage    opaque 2x2 screen door on _MainTex.a: a >= 0.2 / 0.4 / 0.6 / 0.8 keeps 1 / 2 / 3 / 4 pixels of each
//              block (fixed screen positions, also in the shadow caster); _Cutoff is unused. Two-sided: back faces flip
//              the normal but keep the front tangent frame (seen on the ring).
//  Colour      top weight wT = _ColorMask.b ^ _TopGradation (_TopColor), under weight wU = _ColorMask.g ^ _UnderGradation
//              (_UnderColor), M = wT + wU (not clamped); col = lerp(lerp(C, Top, wT), Under, wU); O = lerp(_Occlusion, 1, M);
//              base = 0.7555 C (0.5 + 0.5 O) + O lerp(lerp(0, Fb + 0.129 C, tex.b), Fg + 0.129 C, tex.g) + ring + rim;
//              A = max(0, (1 - M) base + M 0.5069 col O). Fg = (0.0358, 0.0336, 0.0336) and Fb = (0.0426, 0.0248, 0.0248)
//              are constants of the shader; _MainTex.r is unused. _ColorMask_A0B1 = 1 makes _ColorMask_B recolour the
//              base region instead (approximate in between).
//  Ring        band b = saturate(1 - 2 |V.y - N.y|) (world up, N bumped). Lit part, added to the base:
//              _Specular min(0.1948 + 0.3557 V, 0.3548) (1 - 0.745 C) _Ring_onwoffb (1 - _Ringoff) b^max(2.7, HL + 3 - 6 V)
//              (V = max channel of C, HL = _HighLight). The band b^max(2.7, HL + 1 - 4 V) is also the environment of the
//              indirect specular (x surfaceReduction x F0, no Fresnel lerp).
//  Rim         2 (1 - N.V)^_Rimpower joins both ring terms (lit like the ring, reflected like its band).
//  Lighting    diffuse A (1 - m) (SH + light (0.5 Disney sat(N.L) + 0.5 |N.L| + 0.1) / 1.1); Built-in GGX without the
//              0.002 roughness clamp and with smoothness x (N.V)^2; F0 = lerp(0.04, A / 2.112, m); the metallic m fades
//              out at grazing view, m = _Metallic saturate(2 N.V - 0.1). Shadows attenuate all direct terms.
//  Normal      normalize(lerp(_BumpMap, _DetailMask sampled at uv * 16, _DetailScale)) (both DXT5nm, x = R * A).
//  Mesh        C = lerp(_Color, _MeshColor_01, _Mesh_onoff _MeshColorMask_B.r _MeshColorMask.r), both masks sampled on
//              uv1 with their own scale / offset (exact, UH1 of the 4.3 hair spec).
//  Not exact:  _ExGloss (wetness, game default 0) is approximated (metallic >= 0.5 w, smoothness >= 0.8 w); the ring's
//              small extra with metallic; very sharp highlights (smoothness > 0.7) within a few percent.
Shader "HS2Next/Shader Forge/hair_07"
{
    Properties
    {
        _Color ("Color", Color) = (0.585,0.317,0.317,1)
        _Specular ("Specular", Color) = (0.155,0.641,0.764,1)
        _TopColor ("Top Color", Color) = (1,0,0,1)
        _UnderColor ("Under Color", Color) = (0,0.104,1,1)
        _TopGradation ("Top Gradation", Range(1,5)) = 1
        _UnderGradation ("Under Gradation", Range(1,5)) = 1
        _Metallic ("Metallic", Range(0,1)) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0
        _DetailScale ("Detail Scale", Range(0,1)) = 0
        _HighLight ("HighLight", Range(0,20)) = 8.9
        _MainTex ("MainTex", 2D) = "white" {}
        _BumpMap ("BumpMap", 2D) = "bump" {}
        _ColorMask_A0B1 ("ColorMask_A(0)/B(1)", Range(0,1)) = 0
        _ColorMask ("ColorMask", 2D) = "white" {}
        _ColorMask_B ("ColorMask_B", 2D) = "white" {}
        _DetailMask ("Detail Mask", 2D) = "bump" {}
        _Occlusion ("Occlusion", 2D) = "white" {}
        _Rimpower ("Rimpower", Range(2.7,20)) = 2.7
        _ExGloss ("Ex Gloss", Range(0,1)) = 0
        _Ringoff ("Ringoff", Range(0,1)) = 0
        [MaterialToggle] _Ringcontrolr_onoff ("Ringcontrolr_on/off", Float) = 1
        _Ring_onwoffb ("Ring_on(w)/off(b)", 2D) = "white" {}
        [MaterialToggle] _Mesh_onoff ("Mesh_on/off", Float) = 0.585
        _MeshColorMask ("MeshColorMask", 2D) = "white" {}
        _MeshColorMask_B ("MeshColorMask_B", 2D) = "black" {}
        _MeshColor_01 ("MeshColor_01", Color) = (1,0.605,0,1)
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        Cull Off
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HS2HairVert
            #pragma fragment HS2HairFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "HS2NextHair.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HS2HairShadowVert
            #pragma fragment HS2HairShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "HS2NextHair.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HS2HairVert
            #pragma fragment HS2HairDepthFrag
            #include "HS2NextHair.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode"="MotionVectors" }
            ZWrite On ColorMask RG
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HS2MotionVert
            #pragma fragment HS2MotionFrag
            #include "HS2NextHair.hlsl"
            #define HS2_PASS_A HS2HairAttributes
            #define HS2_PASS_V HS2HairVaryings
            #define HS2_PASS_VERT HS2HairVert
            #define HS2_PASS_CLIP HS2HairDepthFrag
            #define HS2_PASS_POS(a) (a).positionOS.xyz
            #define HS2_PASS_NORMAL(a) (a).normalOS
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On ColorMask RGBA
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HS2DepthNormalsVert
            #pragma fragment HS2DepthNormalsFrag
            #include "HS2NextHair.hlsl"
            #define HS2_PASS_A HS2HairAttributes
            #define HS2_PASS_V HS2HairVaryings
            #define HS2_PASS_VERT HS2HairVert
            #define HS2_PASS_CLIP HS2HairDepthFrag
            #define HS2_PASS_POS(a) (a).positionOS.xyz
            #define HS2_PASS_NORMAL(a) (a).normalOS
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
