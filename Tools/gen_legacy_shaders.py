# Writes the URP stand-ins for Unity's legacy, mobile, unlit and legacy particle shaders (Assets/HS2Next/Shaders/Legacy_*.shader,
# Unlit_*.shader, Particles_*.shader) from one table; the shading code is in HS2NextLegacy.hlsl / HS2NextUnlit.hlsl.
# Run: python -I Tools/gen_legacy_shaders.py   (from the project root; rewrites the generated files only)
import os, sys
sys.stdout.reconfigure(encoding='utf-8')
OUT = os.path.join('Assets', 'HS2Next', 'Shaders')

# The rows that stand in for a shader of a mod or a third-party asset pack are HS2Next's compatible shaders: their names begin
# with HS2Next/Compatible/ (ShaderMap.CompatibleFamilies); the rows for Unity's own shaders and the game's are HS2Next/<name>.
COMPATIBLE = {'simple_two_side', 'Bunker/Rim', 'Effects/FPS_Pack/AlphaBlended', 'Effects/FPS_Pack/GlowAdditiveNoFade',
              'Quantum Theory/Particles/HDR Alpha Blended', 'Hovl/Particles/Add_CenterGlow', 'Hovl/Particles/Blend_CenterGlow',
              'Hovl/Particles/Blend_TwoSides', 'FireWing/Wing_Main', 'FireWing/Wing_Sub',
              'GAPH Custom Shader/Integrated Particle/Integrated Particle (ValueControl)'}


def shader_name(name):
    return ('HS2Next/Compatible/' if name in COMPATIBLE else 'HS2Next/') + name

P = {
    'color': '_Color ("Main Color", Color) = (1,1,1,1)',
    'spec': '_SpecColor ("Specular Color", Color) = (0.5,0.5,0.5,1)',
    'shin': '_Shininess ("Shininess", Range(0.03, 1)) = 0.078125',
    'shin01': '_Shininess ("Shininess", Range(0.01, 1)) = 0.078125',
    'main': '_MainTex ("Base (RGB)", 2D) = "white" {}',
    'bump': '_BumpMap ("Normalmap", 2D) = "bump" {}',
    'cut': '_Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5',
    'illum': '_Illum ("Illumin (A)", 2D) = "white" {}',
    'emis': '_Emission ("Emission (Lightmapper)", Float) = 1',
    'reflc': '_ReflectColor ("Reflection Color", Color) = (1,1,1,0.5)',
    'cube': '_Cube ("Reflection Cubemap", Cube) = "_Skybox" {}',
    'tint': '_TintColor ("Tint Color", Color) = (0.5,0.5,0.5,0.5)',
    'fade': '_InvFade ("Soft Particles Factor", Range(0.01, 3)) = 1',
    # the Effects/* particle shaders of vanilla Studio items and some mod effect packs (bundle property lists)
    'tint1': '_TintColor ("Tint Color", Color) = (0.5,0.5,0.5,1)',
    'ctint': '_Color ("Tint Color", Color) = (0.5,0.5,0.5,0.5)',
    'strength': '_ColorStrength ("Color strength", Float) = 1',
    'fade7': '_InvFade ("Soft Particles Factor", Range(0.01, 7)) = 1',
    'fadef': '_InvFade ("Soft Particles Factor", Float) = 0.5',
    # Bunker/Rim (Phase 3.7 package L): the bundle shader's own descriptions and defaults
    'shinrim': '_MainTex ("Base (RGB) Gloss (A)", 2D) = "white" {}',
    'rimc': '_RimColor ("Rim Color", Color) = (0.26,0.19,0.16,0)',
    'rimp': '_RimPower ("Rim Power", Range(0.5, 8)) = 3',
    # Phase 5.2 package S: an HDR tint (Quantum Theory's HDR particle)
    'tintH': '[HDR] _TintColor ("Tint Color", Color) = (0.5,0.5,0.5,0.5)',
    # Phase 5.R: the fog modes the material's original was compiled for (set when a material is switched; 0 = no forward fog)
    'fogmodes': '[HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0',
}

# Phase 5.R: the transparent rows apply the scene's height fog in the shader (HS2NextFog.hlsl, a uniform branch, no keyword):
# toward the fog colour, or toward black for additive and premultiplied blending (as the engine's forward fog of the originals).
def fog_defs(prefix, blend):
    black = blend in ('Blend SrcAlpha One', 'Blend One OneMinusSrcAlpha', 'Blend One OneMinusSrcColor')
    return [f'{prefix}FOG'] + ([f'{prefix}FOG_BLACK'] if black else [])

# (original name, file stem, defines, properties, kind) kind: opaque / cutout / fade
LEGACY = [
    ('Legacy Shaders/Diffuse', 'Diffuse', [], ['color', 'main'], 'opaque'),
    ('Legacy Shaders/Specular', 'Specular', ['SPEC'], ['color', 'spec', 'shin', 'main'], 'opaque'),
    ('Legacy Shaders/Bumped Diffuse', 'BumpedDiffuse', ['BUMP'], ['color', 'main', 'bump'], 'opaque'),
    ('Legacy Shaders/Bumped Specular', 'BumpedSpecular', ['SPEC', 'BUMP'], ['color', 'spec', 'shin', 'main', 'bump'], 'opaque'),
    ('Legacy Shaders/Transparent/Diffuse', 'TransparentDiffuse', ['FADE'], ['color', 'main'], 'fade'),
    ('Legacy Shaders/Transparent/Specular', 'TransparentSpecular', ['FADE', 'SPEC'], ['color', 'spec', 'shin', 'main'], 'fade'),
    ('Legacy Shaders/Transparent/Bumped Diffuse', 'TransparentBumpedDiffuse', ['FADE', 'BUMP'], ['color', 'main', 'bump'], 'fade'),
    ('Legacy Shaders/Transparent/Bumped Specular', 'TransparentBumpedSpecular', ['FADE', 'SPEC', 'BUMP'], ['color', 'spec', 'shin', 'main', 'bump'], 'fade'),
    ('Legacy Shaders/Transparent/Cutout/Diffuse', 'CutoutDiffuse', ['CUTOUT'], ['color', 'main', 'cut'], 'cutout'),
    ('Legacy Shaders/Transparent/Cutout/Specular', 'CutoutSpecular', ['CUTOUT', 'SPEC'], ['color', 'spec', 'shin', 'main', 'cut'], 'cutout'),
    ('Legacy Shaders/Transparent/Cutout/Bumped Diffuse', 'CutoutBumpedDiffuse', ['CUTOUT', 'BUMP'], ['color', 'main', 'bump', 'cut'], 'cutout'),
    ('Legacy Shaders/Transparent/Cutout/Bumped Specular', 'CutoutBumpedSpecular', ['CUTOUT', 'SPEC', 'BUMP'], ['color', 'spec', 'shin', 'main', 'bump', 'cut'], 'cutout'),
    ('Legacy Shaders/Self-Illumin/Diffuse', 'SelfIllumDiffuse', ['ILLUM'], ['color', 'main', 'illum', 'emis'], 'opaque'),
    ('Legacy Shaders/Self-Illumin/Bumped Diffuse', 'SelfIllumBumpedDiffuse', ['ILLUM', 'BUMP'], ['color', 'main', 'illum', 'bump', 'emis'], 'opaque'),   # maps (Phase 3.2)
    ('Legacy Shaders/Self-Illumin/Bumped Specular', 'SelfIllumBumpedSpecular', ['ILLUM', 'SPEC', 'BUMP'], ['color', 'spec', 'shin01', 'main', 'illum', 'bump', 'emis'], 'opaque'),
    ('Legacy Shaders/Reflective/Specular', 'ReflectiveSpecular', ['REFL', 'SPEC'], ['color', 'spec', 'shin01', 'reflc', 'main', 'cube'], 'opaque'),
    ('Legacy Shaders/Reflective/Bumped Diffuse', 'ReflectiveBumpedDiffuse', ['REFL', 'BUMP'], ['color', 'reflc', 'main', 'cube', 'bump'], 'opaque'),
    ('Legacy Shaders/Reflective/Bumped Specular', 'ReflectiveBumpedSpecular', ['REFL', 'SPEC', 'BUMP'], ['color', 'spec', 'shin01', 'reflc', 'main', 'cube', 'bump'], 'opaque'),
    ('Mobile/Diffuse', 'MobileDiffuse', ['MOBILE'], ['main'], 'opaque'),
    ('Mobile/Bumped Diffuse', 'MobileBumpedDiffuse', ['MOBILE', 'BUMP', 'BUMP_MAINUV'], ['main', 'bump'], 'opaque'),
    ('Mobile/Bumped Specular', 'MobileBumpedSpecular', ['MOBILE', 'SPEC', 'BUMP', 'BUMP_MAINUV', 'HALFASVIEW'], ['shin', 'main', 'bump'], 'opaque'),
    # Mod shader measured to be Transparent/Cutout/Diffuse with Cull Off (back faces keep the front normal).
    ('simple_two_side', 'SimpleTwoSide', ['CUTOUT', 'CULLOFF'], ['color', 'main', 'cut'], 'cutout'),
    # Phase 3.7 package L: mod shaders measured to be legacy rows (black-box probes, Shaders/README.md "Lightmap paths").
    # Bunker/Rim: Bumped Specular without _Color, the highlight 5 x BlinnPhong's, plus Unity's documented rim emission.
    ('Bunker/Rim', 'BunkerRim', ['SPEC', 'SPECGAIN 5.0', 'BUMP', 'NOCOLOR', 'RIM'], ['shinrim', 'bump', 'spec', 'shin', 'rimc', 'rimp'], 'opaque'),
]

# (original name, file stem, defines, properties, blend, tags) for HS2NextUnlit.hlsl
UNLIT = [
    ('Unlit/Color', 'Unlit_Color', ['COLOR'], ['color'], None, 'opaque'),
    ('Unlit/Texture', 'Unlit_Texture', ['TEX'], ['main'], None, 'opaque'),
    ('Unlit/Transparent', 'Unlit_Transparent', ['TEX'], ['main'], 'Blend SrcAlpha OneMinusSrcAlpha', 'fade'),
    ('Unlit/Transparent Cutout', 'Unlit_TransparentCutout', ['TEX', 'CUTOUT'], ['main', 'cut'], None, 'cutout'),
]
ADD = 'Blend SrcAlpha One'
ALPHA = 'Blend SrcAlpha OneMinusSrcAlpha'
PREM = 'Blend One OneMinusSrcAlpha'
SMOOTH = 'Blend One OneMinusSrcColor'
PARTICLES = []
# The Unity 5 names (Particles/Additive, ...) are the same shaders; ShaderMap maps them to these.
for prefix, stem in (('Legacy Shaders/Particles/', 'Particles_Legacy_'),):
    PARTICLES += [
        (prefix + 'Additive', stem + 'Additive', ['TEX', 'VCOL', 'TINT2X', 'SOFT'], ['tint', 'main', 'fade'], ADD, 'particle'),
        (prefix + 'Alpha Blended', stem + 'AlphaBlended', ['TEX', 'VCOL', 'TINT2X', 'SOFT'], ['tint', 'main', 'fade'], ALPHA, 'particle'),
        (prefix + 'Alpha Blended Premultiply', stem + 'AlphaBlendedPremultiply', ['TEX', 'VCOL', 'PREMUL', 'SOFT'], ['main', 'fade'], PREM, 'particle'),
        (prefix + 'Additive (Soft)', stem + 'AdditiveSoft', ['TEX', 'VCOL', 'ADDSMOOTH', 'SOFT'], ['main', 'fade'], SMOOTH, 'particle'),
    ]
PARTICLES += [
    ('Mobile/Particles/Additive', 'Particles_MobileAdditive', ['TEX', 'VCOL'], ['main'], ADD, 'particle'),
    ('Mobile/Particles/Alpha Blended', 'Particles_MobileAlphaBlended', ['TEX', 'VCOL'], ['main'], ALPHA, 'particle'),
]
# Effects/* particle shaders (vanilla Studio effects, Aizen effect packs): the legacy particle model x _ColorStrength, colour
# mask RGBA (the bundle passes write alpha), alpha saturated except in ParticleAdd / ParticleAdd C, the strength on rgb only in FPS_Pack/AlphaBlended; "C" takes its tint from _Color. Black-box probed against the bundle copies
# (Shaders/README.md).
PARTICLES += [
    ('Effects/ParticleAdd', 'Particles_Effects_ParticleAdd', ['TEX', 'VCOL', 'TINT2X', 'STRENGTH', 'SOFT'], ['tint', 'main', 'strength', 'fade'], ADD, 'particle_rgba'),
    ('Effects/ParticleAdd C', 'Particles_Effects_ParticleAddC', ['TEX', 'VCOL', 'TINT2X', 'TINT_COLOR', 'STRENGTH', 'SOFT'], ['ctint', 'main', 'strength', 'fade'], ADD, 'particle_rgba'),
    ('Effects/AlphaBlended', 'Particles_Effects_AlphaBlended', ['TEX', 'VCOL', 'TINT2X', 'STRENGTH', 'ALPHA_SAT', 'SOFT'], ['tint1', 'strength', 'main', 'fade7'], ALPHA, 'particle_rgba'),
    ('Effects/FPS_Pack/AlphaBlended', 'Particles_Effects_FpsAlphaBlended', ['TEX', 'VCOL', 'TINT2X', 'STRENGTH', 'STRENGTH_RGB', 'ALPHA_SAT', 'SOFT'], ['tint', 'strength', 'main', 'fadef'], ALPHA, 'particle_rgba'),
    ('Effects/FPS_Pack/GlowAdditiveNoFade', 'Particles_Effects_FpsGlowAdditiveNoFade', ['TEX', 'VCOL', 'TINT2X', 'STRENGTH', 'ALPHA_SAT'], ['tint', 'strength', 'main'], ADD, 'particle_rgba'),
]
# Phase 5.2 package S (the particle router): mod shaders measured to be a legacy particle row with other property flags.
PARTICLES += [
    ('Quantum Theory/Particles/HDR Alpha Blended', 'Particles_QuantumTheory_HDRAlphaBlended', ['TEX', 'VCOL', 'TINT2X', 'SOFT'], ['tintH', 'main', 'fade'], ALPHA, 'particle'),
]

HEAD = '// Generated by Tools/gen_legacy_shaders.py (do not edit; change the table there).\n'

def props(keys):
    return '\n'.join('        ' + P[k] for k in keys)

# Phase 3.3 package S: the MotionVectors and DepthNormals passes of the opaque /
# cutout shaders (TAA, the depth-normals prepass), made from the shader's DepthOnly pass: the family's depth vertex /
# fragment functions, the same defines and render state, then Shaders/HS2NextPasses.hlsl with the family's macros.
# Also used for the hand-written fitted shaders (Tools/add_taa_passes.py; their DepthOnly passes name the functions below).
TAA_FAMILY = {
    # (depth vertex, depth fragment): (attributes struct, varyings struct, position of an attribute, its normal or None)
    ('LegVert', 'LegDepthFrag'): ('LegAttributes', 'LegVaryings', '(a).pos.xyz', '(a).n'),
    ('UlVert', 'UlDepthFrag'): ('UlAttributes', 'UlVaryings', '(a).pos.xyz', None),
    ('HS2ClothesVert', 'HS2ClothesDepthFrag'): ('HS2ClothesAttributes', 'HS2ClothesVaryings', '(a).pos.xyz', '(a).n'),
    ('HS2MS2Vert', 'HS2MS2DepthFrag'): ('HS2MS2Attributes', 'HS2MS2Varyings', '(a).pos.xyz', '(a).n'),
    ('HS2SkinDepthVert', 'HS2SkinClipFrag'): ('HS2SkinSA', 'HS2SkinSV', '(a).pos.xyz', '(a).n'),
    ('HS2HairVert', 'HS2HairDepthFrag'): ('HS2HairAttributes', 'HS2HairVaryings', '(a).positionOS.xyz', '(a).normalOS'),
    ('StdVert', 'StdDepthFrag'): ('StdAttributes', 'StdVaryings', '(a).pos.xyz', '(a).n'),
    ('Vert', 'FragDepth'): ('A', 'V', '(a).pos.xyz', None),           # Legacy Shaders/Transparent/Cutout/Soft Edge Unlit
    ('Vert', 'DepthFrag'): ('A', 'V', '(a).pos.xyz', '(a).n'),        # the Tree Creator approximations
    ('vert', 'frag'): None,                                           # bare-mesh depth passes: HS2_PASS_SIMPLE
    ('Vert', 'Frag'): None,
}


def taa_passes(depth_pass):
    """The MotionVectors and DepthNormals passes for the text of a `Pass { Name "DepthOnly" ... }` block."""
    import re
    mv = re.search(r'#pragma vertex (\w+)', depth_pass)
    mf = re.search(r'#pragma fragment (\w+)', depth_pass)
    key = (mv.group(1) if mv else '?', mf.group(1) if mf else '?')
    if key not in TAA_FAMILY:
        raise ValueError('no TAA_FAMILY entry for the depth pass functions %s / %s' % key)
    fam = TAA_FAMILY[key]
    ind = re.search(r'\n([ \t]*)ENDHLSL', depth_pass).group(1)
    if fam is None:
        macros = [ind + '#define HS2_PASS_SIMPLE']
    else:
        a, v, pos, nrm = fam
        macros = [ind + '#define HS2_PASS_A ' + a, ind + '#define HS2_PASS_V ' + v, ind + '#define HS2_PASS_VERT ' + key[0],
                  ind + '#define HS2_PASS_CLIP ' + key[1], ind + '#define HS2_PASS_POS(a) ' + pos]
        if nrm:
            macros.append(ind + '#define HS2_PASS_NORMAL(a) ' + nrm)
    macros.append(ind + '#include "HS2NextPasses.hlsl"')
    out = ''
    for name, vert, frag, mask in (('MotionVectors', 'HS2MotionVert', 'HS2MotionFrag', 'RG'),
                                   ('DepthNormals', 'HS2DepthNormalsVert', 'HS2DepthNormalsFrag', 'RGBA')):
        t = depth_pass.replace('Name "DepthOnly"', 'Name "%s"' % name).replace('"LightMode"="DepthOnly"', '"LightMode"="%s"' % name)
        t = re.sub(r'ColorMask R\b', 'ColorMask ' + mask, t)
        t = t.replace('#pragma vertex ' + key[0], '#pragma vertex ' + vert).replace('#pragma fragment ' + key[1], '#pragma fragment ' + frag)
        i = t.index('\n' + ind + 'ENDHLSL')
        t = t[:i] + '\n' + '\n'.join(macros) + t[i:]
        out += t
    return out


def legacy(name, defs, keys, kind):
    if kind == 'fade':
        defs = defs + ['FOG']
        keys = keys + ['fogmodes']
    d = '\n'.join(f'            #define HS2_LEG_{x}' for x in defs)
    tags = {'opaque': '"RenderType"="Opaque" "Queue"="Geometry"', 'cutout': '"RenderType"="TransparentCutout" "Queue"="AlphaTest"',
            'fade': '"RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True"'}[kind]
    state = '            Blend SrcAlpha OneMinusSrcAlpha\n            ZWrite Off\n' if kind == 'fade' else ''
    cull = '            Cull Off\n' if 'CULLOFF' in defs or 'FLIPBACK' in defs else ''
    state += cull
    passes = f'''        Pass
        {{
            Name "ForwardLit"
            Tags {{ "LightMode"="UniversalForward" }}
{state}            HLSLPROGRAM
            #pragma vertex LegVert
            #pragma fragment LegForwardFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma dynamic_branch_local _RECEIVE_SHADOWS_OFF
            #define HS2_LEG_FORWARD
{d}
            #include "HS2NextLegacy.hlsl"
            ENDHLSL
        }}
'''
    if kind != 'fade':
        for pname, lm, vert, frag, extra, mask in (('ShadowCaster', 'ShadowCaster', 'LegShadowVert', 'LegShadowFrag', '            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW\n', '0'),
                                                    ('DepthOnly', 'DepthOnly', 'LegVert', 'LegDepthFrag', '', 'R')):
            pdef = 'HS2_LEG_SHADOW' if pname == 'ShadowCaster' else 'HS2_LEG_DEPTH'
            block = f'''        Pass
        {{
            Name "{pname}"
            Tags {{ "LightMode"="{lm}" }}
            ColorMask {mask}
            ZWrite On
{cull}            HLSLPROGRAM
            #pragma vertex {vert}
            #pragma fragment {frag}
{extra}            #define {pdef}
{d}
            #include "HS2NextLegacy.hlsl"
            ENDHLSL
        }}
'''
            passes += block
            if pname == 'DepthOnly':
                passes += taa_passes(block)
    origin = 'a mod shader measured to match' if 'CULLOFF' in defs or not name.startswith(('Legacy Shaders/', 'Mobile/')) else "Unity's built-in"
    return f'''{HEAD}// URP stand-in for {origin} "{name}" (published legacy surface shader semantics, HS2NextLegacy.hlsl).
Shader "{shader_name(name)}"
{{
    Properties
    {{
{props(keys)}
    }}
    SubShader
    {{
        Tags {{ {tags} "RenderPipeline"="UniversalPipeline" }}
{passes}    }}
}}
'''

def unlit(name, defs, keys, blend, kind):
    if kind in ('fade', 'particle', 'particle_rgba'):
        defs = defs + [x[len('HS2_UL_'):] for x in fog_defs('HS2_UL_', blend)]
        keys = keys + ['fogmodes']
    d = '\n'.join(f'            #define HS2_UL_{x}' for x in defs)
    tags = {'opaque': '"RenderType"="Opaque" "Queue"="Geometry"', 'cutout': '"RenderType"="TransparentCutout" "Queue"="AlphaTest"',
            'fade': '"RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True"',
            'particle': '"RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane"',
            'particle_rgba': '"RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane"'}[kind]
    state = ''
    if kind == 'particle': state = f'            {blend}\n            ColorMask RGB\n            Cull Off\n            ZWrite Off\n'
    elif kind == 'particle_rgba': state = f'            {blend}\n            Cull Off\n            ZWrite Off\n'
    elif kind == 'fade': state = f'            {blend}\n            ZWrite Off\n'
    what = 'legacy particle' if kind == 'particle' else 'particle' if kind == 'particle_rgba' else 'unlit'
    passes = f'''        Pass
        {{
            Name "Unlit"
            Tags {{ "LightMode"="UniversalForward" }}
{state}            HLSLPROGRAM
            #pragma vertex UlVert
            #pragma fragment UlFrag
{d}
            #include "HS2NextUnlit.hlsl"
            ENDHLSL
        }}
'''
    if kind in ('opaque', 'cutout'):
        block = f'''        Pass
        {{
            Name "DepthOnly"
            Tags {{ "LightMode"="DepthOnly" }}
            ColorMask R
            ZWrite On
            HLSLPROGRAM
            #pragma vertex UlVert
            #pragma fragment UlDepthFrag
{d}
            #include "HS2NextUnlit.hlsl"
            ENDHLSL
        }}
'''
        passes += block + taa_passes(block)
    origin = (f'the effect shader "{name}" (bundle property list and pass state; black-box probed, HS2NextUnlit.hlsl)' if kind == 'particle_rgba'
              else f'Unity\'s built-in "{name}" (published {what} shader semantics, HS2NextUnlit.hlsl)')
    return f'''{HEAD}// URP stand-in for {origin}.
Shader "{shader_name(name)}"
{{
    Properties
    {{
{props(keys)}
    }}
    SubShader
    {{
        Tags {{ {tags} "RenderPipeline"="UniversalPipeline" }}
{passes}    }}
}}
'''

# Phase 5.2 package S: the particle router's stand-ins (ShaderMap.Fitted block "5.2 Sc"; HS2NextFx.hlsl). Each row: the
# original's name, the file stem, the HS2NextFx defines (the colour model), the original's property list as declared in its
# bundle (names, types, defaults, flags: ModShaderTest M2), and its pass state (blend, cull, depth write, colour mask).
FX_TAGS = '"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane"'
HOVL_GLOW = [
    '_MainTex ("MainTex", 2D) = "white" {}',
    '_Noise ("Noise", 2D) = "white" {}',
    '_Flow ("Flow", 2D) = "white" {}',
    '_Mask ("Mask", 2D) = "white" {}',
    '_SpeedMainTexUVNoiseZW ("Speed MainTex U/V + Noise Z/W", Vector) = (0,0,0,0)',
    '_DistortionSpeedXYPowerZ ("Distortion Speed XY Power Z", Vector) = (0,0,0,0)',
    '_Emission ("Emission", Float) = 2',
    '_Color ("Color", Color) = (0.5,0.5,0.5,1)',
]
ROUTED = [
    ('Hovl/Particles/Add_CenterGlow', 'Fx_Hovl_AddCenterGlow', ['HOVL', 'PREMUL'], HOVL_GLOW + [
        '_Usecenterglow ("Use center glow?", Float) = 0',
        '_Usedepth ("Use depth?", Float) = 0',
        '_Usecustomrandom ("Use Custom Random?", Float) = 0',
        '_Depthpower ("Depth power", Float) = 1',
        '_CullMode ("Culling", Float) = 0',
        '_Blend2 ("Blend mode subset", Float) = 1',
        '[HideInInspector] _texcoord ("_texcoord", 2D) = "white" {}',
     ], {'blend': 'Blend One [_Blend2]', 'cull': 'Cull [_CullMode]', 'zwrite': 'ZWrite Off', 'mask': 'ColorMask RGB'}),
    ('Hovl/Particles/Blend_CenterGlow', 'Fx_Hovl_BlendCenterGlow', ['HOVL', 'OPACITY'], HOVL_GLOW + [
        '_Opacity ("Opacity", Range(0, 3)) = 1',
        '_Usecenterglow ("Use center glow?", Float) = 0',
        '_Usedepth ("Use depth?", Float) = 0',
        '_Depthpower ("Depth power", Float) = 1',
        '_CullMode ("Culling", Float) = 0',
        '[HideInInspector] _texcoord ("_texcoord", 2D) = "white" {}',
     ], {'blend': 'Blend SrcAlpha OneMinusSrcAlpha', 'cull': 'Cull [_CullMode]', 'zwrite': 'ZWrite Off', 'mask': 'ColorMask RGB'}),
    ('Hovl/Particles/Blend_TwoSides', 'Fx_Hovl_BlendTwoSides', ['HOVL_TWOSIDES'], [
        '_Cutoff ("Mask Clip Value", Float) = 0.5',
        '_MainTex ("Main Tex", 2D) = "white" {}',
        '_Mask ("Mask", 2D) = "white" {}',
        '_Noise ("Noise", 2D) = "white" {}',
        '_SpeedMainTexUVNoiseZW ("Speed MainTex U/V + Noise Z/W", Vector) = (0,0,0,0)',
        '_FrontFacesColor ("Front Faces Color", Color) = (0,0.2313726,1,1)',
        '_BackFacesColor ("Back Faces Color", Color) = (0.1098039,0.4235294,1,1)',
        '_Emission ("Emission", Float) = 2',
        '_UseFresnel ("Use Fresnel?", Float) = 1',
        '_SeparateFresnel ("SeparateFresnel", Float) = 0',
        '_SeparateEmission ("Separate Emission", Float) = 2',
        '_FresnelColor ("Fresnel Color", Color) = (1,1,1,1)',
        '_Fresnel ("Fresnel", Float) = 1',
        '_FresnelEmission ("Fresnel Emission", Float) = 1',
        '_UseCustomData ("Use Custom Data?", Float) = 0',
        '[HideInInspector] _texcoord ("_texcoord", 2D) = "white" {}',
        '[HideInInspector] _tex4coord ("_tex4coord", 2D) = "white" {}',
        '[HideInInspector] __dirty ("__dirty", Float) = 1',
     ], {'blend': 'Blend SrcAlpha OneMinusSrcAlpha', 'cull': 'Cull Off', 'zwrite': 'ZWrite On', 'mask': ''}),
    ('FireWing/Wing_Main', 'Fx_FireWing_WingMain', ['FIREWING_MAIN'], [
        '_MainTex ("MainTex", 2D) = "black" {}',
        '_TintColor ("Color", Color) = (0.5,0.5,0.5,1)',
        '_SubTex ("SubTex", 2D) = "white" {}',
        '_Mask ("Mask", 2D) = "white" {}',
        '_Transparency ("Transparency", Range(0, 6)) = 3.971232',
        '_Noise ("Noise", 2D) = "black" {}',
     ], {'blend': 'Blend One One', 'cull': 'Cull Off', 'zwrite': 'ZWrite Off', 'mask': ''}),
    ('FireWing/Wing_Sub', 'Fx_FireWing_WingSub', ['FIREWING_SUB'], [
        '_MainTex ("MainTex", 2D) = "white" {}',
        '_TintColor ("Color", Color) = (0.5,0.5,0.5,1)',
        '_Mask ("Mask", 2D) = "white" {}',
        '_MaskNoice ("MaskNoice", 2D) = "black" {}',
        '_Transparency ("Transparency", Range(0, 6)) = 3.692308',
        '_Noice ("Noice", 2D) = "black" {}',
        '_Reflective ("Reflective", 2D) = "white" {}',
     ], {'blend': 'Blend One One', 'cull': 'Cull Off', 'zwrite': 'ZWrite Off', 'mask': ''}),
    ('GAPH Custom Shader/Integrated Particle/Integrated Particle (ValueControl)', 'Fx_GAPH_IntegratedParticleValueControl', ['GAPH'], [
        '[HDR] _TintColor ("Tint Color", Color) = (0.5,0.5,0.5,0.5)',
        '_AllColorFactor ("All Color Factor", Float) = 1',
        '_ColorRedFactor ("Color Red Factor", Float) = 1',
        '_ColorGreenFactor ("Color Green Factor", Float) = 1',
        '_ColorBlueFactor ("Color Blue Factor", Float) = 1',
        '_MainTex ("Particle Texture", 2D) = "white" {}',
        '_BlendSrc ("BlendSrc", Float) = 1',
        '_BlendDst ("BlendDst", Float) = 1',
        '_InvFade ("Soft Particle Factor", Range(0.01, 3)) = 1',
     ], {'blend': 'Blend [_BlendSrc] [_BlendDst]', 'cull': 'Cull Off', 'zwrite': 'ZWrite Off', 'mask': 'ColorMask RGB'}),
]


def fx(name, defs, props_, state):
    d = '\n'.join(f'            #define HS2_FX_{x}' for x in defs)
    st = '\n'.join('            ' + state[k] for k in ('blend', 'cull', 'zwrite', 'mask') if state.get(k))
    pl = '\n'.join('        ' + x for x in props_ + [P['fogmodes']])
    return f'''{HEAD}// URP stand-in for the effect shader "{name}" (the particle router: the bundle's property list and pass state, the colour
// model of HS2NextFx.hlsl chosen by its blend and inputs; an approximation, see Shaders/README.md).
Shader "{shader_name(name)}"
{{
    Properties
    {{
{pl}
    }}
    SubShader
    {{
        Tags {{ {FX_TAGS} "RenderPipeline"="UniversalPipeline" }}
        Pass
        {{
            Name "Unlit"
            Tags {{ "LightMode"="UniversalForward" }}
{st}
            HLSLPROGRAM
            #pragma vertex FxVert
            #pragma fragment FxFrag
{d}
            #include "HS2NextFx.hlsl"
            ENDHLSL
        }}
    }}
}}
'''


written = []
for name, stem, defs, props_, state in ROUTED:
    written.append((stem + '_URP.shader', fx(name, defs, props_, state), name))
for name, stem, defs, keys, kind in LEGACY:
    written.append(('Legacy_' + stem + '_URP.shader', legacy(name, defs, keys, kind), name))
for name, stem, defs, keys, blend, kind in UNLIT + PARTICLES:
    written.append((stem + '_URP.shader', unlit(name, defs, keys, blend, kind), name))
for fn, text, name in written:
    with open(os.path.join(OUT, fn), 'w', encoding='utf-8', newline='\n') as f: f.write(text)
print(f'{len(written)} shaders written')
print('ShaderMap entries:')
for fn, text, name in written: print(f'            {{ "{name}", "{shader_name(name)}" }},')
