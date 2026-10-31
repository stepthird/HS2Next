# Fitted URP shaders (characters and Studio items)

URP replacements for HS2's Built-in character shaders, for the shaders of Studio items and maps and for the common mod
shaders. Each replacement keeps the original's property names, types and
defaults, so `ShaderMap.ApplyUrp` only swaps `material.shader` and the values the game wrote at load stay valid (and the
Studio item setters write by the same names). Register new pairs in `Runtime/Rendering/ShaderMap.cs` (`Fitted`).

Everything else falls back to URP/Lit (`ShaderMap.Fallback`): `_MainTex` with its tiling / offset → `_BaseMap`, `_Color`
(or `_BaseColor`) rgb → `_BaseColor` with alpha 1, `_BumpMap` (+ `_NORMALMAP`), `_Smoothness` 0.2, alpha clipping at 0.5
when the original declares `_Cutoff` or sits in a queue ≥ 2450, the queue kept; the original-named `_MainTex` and
`_Color` keep their values (URP/Lit hides `_MainTex`, so a plain shader switch would leave it empty). Studio items mirror
their colour writes onto such slots (`_Color` → `_BaseColor`, `_Glossiness` → `_Smoothness`, `_EMISSION` with a
non-black emission); alpha, patterns and colours 2 / 3 have no equivalent there.

Behaviour is inferred by black-box probing only (clean room): render a quad with controlled inputs
under the original in Built-in, write the replacement, compare. No compiled shader code is read.

| Original | Replacement | Used by | Probe RMS (linear) | Real asset mean \|d\| (linear) | Notes |
|---|---|---|---|---|---|
| `AIT/Clothes True` | `HS2Next/AIT Clothes True` | most clothes (colour / gloss composited into `_MainTex` / `_DetailMainTex`) | 0.0001 over 4164 values (only the back-face diffuse at 0.0006) | 0.0000 on all 3 cards (front and back light); 0.0000-0.0132 with shadows | refitted: metal / gloss from `_DetailMainTex` R / B, wet smoothness max(gloss, 0.6 ExGloss), detail maps on `uv * _UVScalePattern.xy`, inner-clothes masks on uv1, translucency 0.4 x on back faces; liquid overlay (`_Weathering*`) not reproduced |
| `AIT/Clothes` | `HS2Next/AIT Clothes` | colour-mask accessories (collars, bracelets, necklaces) | 0.0000 over 4257 values | 0.0000 (3 renderers on 3 cards) | AIT/Item's model plus the `_DITHERSWITCH_ON` (alpha as 4x4 dither) and `_EMISSIONCOLORTEXBASE_ON` (albedo emission) keywords |
| `AIT/Clothes Alpha True` | `HS2Next/AIT Clothes Alpha True` | stockings, garters, see-through clothes | 0.0000 over 5859 values | 0.0000 (garter, socks, pantyhose on 2 cards); 0.0000 with shadows | Clothes True blended (SrcAlpha / OneMinusSrcAlpha, ZWrite Off, Cull Off), wet smoothness max(gloss, ExGloss), tearing alpha `(B^(12(1-AlphaEx)) - 0.03) / 0.97` |
| `AIT/Clothes Alpha True BackCull` | `HS2Next/AIT Clothes Alpha True BackCull` | lace tops / dresses (not on the test cards) | 0.0000 over 6834 values | 0.0096 front / 0.0008 back on the item `fo_top_30:p_cf_top_babd` (bind pose) | Cull Back, colour x `lerp(1, saturate(F.x + F.y (1 - N.V)^F.z), F.w)` (`_FresnelMultiple`), unlit rim `_BitangentRim * (S.x + S.y (1 - N.V)^S.z) * S.w * smoothness * occlusion`; the item keeps bands where the surface faces the camera (source not found) |
| `AIT/Clothes Alpha` | `HS2Next/AIT Clothes Alpha` | blended colour-mask accessories (not on the test cards) | 0.0000 over 5244 values | 0.0003 front / 0.0002 back on the item `ao_back_00:p_acs_fairywing00` | AIT/Item blended; `_AlphaMask` (min with R / G by `_alpha_a` / `_alpha_b`, uv0 / uv1), tiled pattern UVs, emission falloff by `_EmissionRim` on the vertex normal |
| `AIT/Item` | `HS2Next/AIT Item` | accessories (earrings, glasses frames, chokers) | 0.0000 over 4287 values | 0.0000 (4 renderers on 2 cards); 0.0005-0.0416 with shadows (back-light shadow edges on thin metal parts) | colour-mask regions as a lerp chain R/G/B with pattern presence `saturate((M.a - 0.9) * 10)`, metal / gloss per region, detail maps, translucency + transmission (`0.25 x`), wet dither coverage from Occlusion.A |
| `AIT/Item Meka studio` | `HS2Next/AIT Item Meka studio` | mechanical accessories (headgear) | 0.0000 over 4878 values | 0.0000; 0.0011-0.0013 with shadows | Item without translucency / transmission / tearing; pattern UVs `rotate((uv S + p.zw - 0.5) p.xy + 0.5)`, `_EffectMap.R` emission mask, emission `saturate(x) (lerp(albedo, colour, colour.a) x + 0.1 smoothness occlusion)` with `x = strength G`, cutout on `min(MainTex.a, _alpha)` |
| `AIT/main Item Glass` | `HS2Next/AIT main Item Glass` | glasses lenses | 0.0000 over 300 values | 0.0000 (lens + frame) | albedo MainTex x Color, alpha MainTex.a x Color.a, `_Metallic4` / `_Glossiness4`, normal map with `_NormalScale`, SrcAlpha blend, Cull Off |
| `AIT/main standard2 studio` | `HS2Next/AIT main standard2 studio` | Studio props: furniture, rooms and walls, lamps, primitives (通常 / キャラ), weapons (411 vanilla items, 796 slots) | 0.0000 over 3309 values (incl. real textures, parallax, dither patterns) | 0.0000 on 16 items (front and back light, item lights off); 0.0012 with shadows (0.0000-0.0106 per view, shadow edges) | Standard-like BRDF1 surface: main maps on uv0 x `_UV`, detail maps on uv0 x `_UVSec`; colour-mask regions combined with a per-channel max ("lighten"), metal / gloss as weighted sums; patterns `rotate((uv + q.zw - 0.5) q.xy + 0.5)` with the pattern colour's alpha as opacity; detail albedo lerps towards the detail colour; occlusion R; parallax from occlusion A; emission lerp(`_EmissionColor` [HDR], albedo, `_EmissionMainColorBase`) x strength x `_EmissonMap`.r; `_DITHERSWITCH_ON`: step(bayer4, min(MainTex.a, `_alpha`)) against `_Cutoff`; Cull Back |
| `AIT/main standard2 studio alpha` | `HS2Next/AIT main standard2 studio alpha` | transparent primitives (透明), effects (beams), veils, snowflakes (101 items, 102 slots) | 0.0000 over 3375 values (two backgrounds) | 0.0001 on 13 items (front and back; 0.0113 on one beam: draw order of overlapping parts); same with shadows | the opaque variant blended SrcAlpha / OneMinusSrcAlpha with ZWrite On (measured on overlapping real geometry), alpha min(MainTex.a, region alpha, `_alpha`) with region alpha saturate(saturate(`_Color`.a - (R+G+B)) + `_Color2`.a R + `_Color3`.a G + `_Color4`.a B), clip(a - `_Cutoff`), no dither, no received shadows |
| `AIT/Item studio` | `HS2Next/AIT Item studio` | accessory-type Studio items: watches, bags, hats, glasses, helmets, headphones, pets (188 items, 201 slots) | 0.0104 over 5772 values; 0.0000 except the four blocks of the bumped-normal rim gap | 0.0001 on 18 items (cat 0.0011); 0.0019 with shadows (back-light shadow edges on thin parts up to 0.056) | AIT/Item's surface (translucency, transmission, Cull Off) with Item Meka studio's pattern UVs and `_alpha` cutout; emission lerp(albedo, `_EmissionColor`, `_EmissionColor`.a) x strength x G without `_EMISSIONCOLORTEXBASE_ON`; unlit rim `_BitangentRim` x max(0, (S.x + S.y (1 - N.V)^S.z) S.w) x smoothness x occlusion (S = `_FresnelSetting`); dither: min(step(bayer4, MainTex.a), `_alpha`) against `_Cutoff`. Gap: with a bumped normal tilted towards +x the original's rim also changes with the main light (not reproduced) |
| `AIT/main standard2` | `HS2Next/AIT main standard2` | the older Studio shader: rooms, floors, walls, ceilings, stairs, a few lamps / shelves (43 vanilla items, 112 slots) | 0.0000 over 804 values (with the floor material id:256; with the shelf id:161 0.0404, all from one first-render artefact cell, see notes) | 0.0000 on 12 items; 0.0006 with shadows | `HS2NextStudioStandard.hlsl` `HS2_MS2_OLD`: lerp-chain R / G / B regions, one metallic / glossiness, pattern UV uv0 x q.xy + q.zw (no rotation / clamp), `_Color3_3`, alpha min(MainTex.a, `_AlphaEx`, `_Color`.a x saturate(w1 + `_Color2`.a R + `_Color3`.a G + `_Color4`.a B)), emission by the compiled keywords `_EMISSIONMAINCOLORBASE_ON` (albedo x colour x map.r) / `_EMISSION_ON` (colour x map.rgb), `_EmissionPower` unused; otherwise as `main standard2 studio` |
| `AIT/main texture studio unlit` | `HS2Next/AIT main texture studio unlit` | the image panel (vanilla item 975) | 0.0000 over 219 values | 0.0000 (front / back, shadows) | unlit MainTex.rgb x `_Color`.rgb, alpha MainTex.a (the colour's alpha unused), SrcAlpha / OneMinusSrcAlpha, clip(MainTex.a - `_Cutoff`), Cull Off, main UV = the pattern-1 transform rotate((uv + q.zw - 0.5) q.xy + 0.5) with clamp; `_UVScroll1` showed no effect; ZWrite not measurable (On) |
| `Standard` | `HS2Next/Standard` | mod items using Unity's Standard | 0.0000 over 549 values against Unity's own Standard (opaque, cutout, fade, transparent, detail, metallic map, highlights off); parallax 0.0000 over 66 | 0.0006 (card 3 hair part); 0.0000 on 3 items | not fitted: Unity's published Standard semantics with dynamic-branch keywords (`HS2NextStandard.hlsl`); `_Metallic` is `[Gamma]` as in Unity's; `ShaderMap.Swap` drops keywords the bundle copy does not declare. Also `Bakery/Standard` (0.0000 over 192 values on a bundle copy; its Bakery lightmap keywords are ignored) |
| `Standard (Specular setup)` | `HS2Next/Standard (Specular setup)` | mod items (Unity's own built-in shader in the bundles) | 0.0000 over 603 values against the Editor's built-in; parallax 0.0000 over 66 | 0.0000 on 5 items; with shadows 0.0002-0.0025 | specular RGB / smoothness from `_SpecColor` / `_Glossiness` or `_SpecGlossMap` (x `_GlossMapScale`), albedo-alpha smoothness, diffuse x (1 - max(specular)) (`HS2SurfaceSpecular`) |
| `Standard (Roughness setup)` | `HS2Next/Standard (Roughness setup)` | mod items (Unity's built-in, StandardRoughnessShaderGUI; not in the Unity 6 Editor) | 0.0000 over 387 + 126 values against bundle copies | 0.0000-0.0001 on 4 items | metallic R of `_MetallicGlossMap` or `_Metallic` (no `[Gamma]`), smoothness 1 - (R of `_SpecGlossMap` or `_Glossiness`). Also `Autodesk Interactive` (0.0000 over 513 values against the Editor's built-in; 0.0000 on an item) |
| `Standard(Two Sided)` | `HS2Next/Standard(Two Sided)` | mod items (a mod copy of Standard with Cull Off) | 0.0003 over 219 values (back faces keep the front normal; a grazing back-face highlight 0.003) | 0.0000-0.0001 on 4 items | Standard with Cull Off in every pass. Same behaviour (0.0000-0.0007 over 192 values each, front / back / grazing): `Standard Double Sided`, `Azerilo/Double Sided Standard`, `StandardDoubleSide` (older Standard, 23 properties), `Standard 2-Sided`, `Standard(Two Side)` |
| `DoubleSided/Standard/Standard DS` | `HS2Next/Compatible/Standard DS (flipped back faces)` | mod items | 0.0000 over 192 values (was 0.50 without the flip) | 0.0002 on an item | Standard(Two Sided) whose back faces use the flipped vertex normal (`HS2_STD_FLIPBACK`) |
| Unity's legacy shaders: `Legacy Shaders/{Diffuse, Specular, Bumped Diffuse, Bumped Specular}`, `.../Transparent/*`, `.../Transparent/Cutout/*`, `.../Self-Illumin/{Diffuse, Bumped Specular}`, `.../Reflective/{Specular, Bumped Diffuse, Bumped Specular}`, `Mobile/{Diffuse, Bumped Diffuse, Bumped Specular}` | `HS2Next/<same name>` (20 shaders, generated by `Tools/gen_legacy_shaders.py`) | mod items and maps | 0.0000 over 408 values each against the Editor's built-ins (lighting, specular, gloss, tilt, bump, illumination, reflection, cutout, fade, back face); bundle copies 0.0000; on real textures 0.0000 | 0.0000 on 4 items, 0.0014 reflective; LOD-group items differ in the harness (all LODs drawn) | Lambert / BlinnPhong of Built-in's surface shaders (`HS2NextLegacy.hlsl`), surface-shader tangent frame; Mobile/Bumped Specular: the main light's half vector per vertex (halfasview), renormalized per pixel |
| `Unlit/{Color, Texture, Transparent, Transparent Cutout}`, `Legacy Shaders/Particles/{Additive, Alpha Blended, Alpha Blended Premultiply, Additive (Soft)}` (and their Unity 5 names `Particles/*`), `Mobile/Particles/{Additive, Alpha Blended}` | `HS2Next/<same name>` (10 shaders, `HS2NextUnlit.hlsl`) | mod and vanilla effects | 0.0000 over 87 values each against the Editor's built-ins and the bundle copies of the Unity 5 names (texture, vertex colour, tint, two backgrounds) | particle systems are not simulated in the batch harness | soft particles (`_InvFade`) need the scene depth before the transparents: applied only when `_HS2NextSceneDepthValid` is 1; fog not applied. `Sprites/Default` (gimmick icons, helpers never captured) maps to `HS2Next/Mobile/Particles/Alpha Blended`: equal at the default white `_Color` (0.0000 on the texture / vertex-colour cases), the `_Color` tint is ignored |
| `Effects/ParticleAdd`, `Effects/ParticleAdd C`, `Effects/AlphaBlended`, `Effects/FPS_Pack/AlphaBlended`, `Effects/FPS_Pack/GlowAdditiveNoFade` | `HS2Next/<same name>`, for the two `FPS_Pack` ones `HS2Next/Compatible/<same name>` (5 shaders, `HS2NextUnlit.hlsl` `HS2_UL_STRENGTH`) | vanilla Studio effects (campfire flame billboards, muzzle flash, fog, dragon fire: 19 vanilla materials) and the Aizen effect packs | 0.0000 over 171 values each against the bundle copies (texture, vertex colour, tint, `_ColorStrength` 0-5, alpha 0.1-1, two backgrounds, back face) | 0.0000 (campfire LineRenderer billboards of items 121 and 1849, front and back) | not Unity shaders (no published source): property lists and pass state read from the bundle shader objects (`SrcAlpha One` or `SrcAlpha OneMinusSrcAlpha`, Cull Off, ZWrite Off, colour mask RGBA), the colour model probed: `2 vertex colour x tint x texture x _ColorStrength` (tint `_TintColor`, `_Color` in "ParticleAdd C"); alpha saturated afterwards except in the two ParticleAdd shaders; in FPS_Pack/AlphaBlended the strength scales rgb only. Soft particles (`_InvFade`) as the legacy particles (not probed: the bundle copies were not compiled with `SOFTPARTICLES_ON` in the probe). `Effects/GlowAdditiveTwoColor` (2 mod materials, core colour and light-core cutout) and `Effects/Ice/IceFront` (refraction) stay on the fallback |
| `AIT/Additive` | `HS2Next/AIT Additive` | vanilla glow parts (mech beam poles) | 0.0000 over 270 values | 0.0000 on 2 items | unlit, One / OneMinusSrcAlpha, Cull Off: colour `_Color.rgb _Color.a k`, alpha `saturate(_Color.a^2 k)`, `k = _Emissive + 9 _Glossiness`; `_MainTex` alpha 0 discards; time mask and UV panner have no measurable effect |
| `Shader Forge/main_item_studio_add` | `HS2Next/Shader Forge/main_item_studio_add` | vanilla Studio effects (wave rings, pillars, magic circles; 22 materials, all Studio-colour slots) | 0.0000 over 99 + 54 values | 0.0000 (wave ring) | unlit, One One, Cull Back: colour mask with priority B > G > R combined by a per-channel max, `max(saturate(R - max(G, B)^8) _Color, saturate(G - B^8) _Color2, B _Color3) x _alpha` (found with orthogonal colours, then overlapping ones) |
| `simple_two_side` | `HS2Next/Compatible/simple_two_side` (generated) | mod plants | 0.0000 over 408 values (legacy cases) | 0.0000-0.0003 on 2 items | Legacy Transparent/Cutout/Diffuse with Cull Off; back faces keep the front normal |
| `Hanmen/Item Cutoff`, `Hanmen/Item Alpha` (main bundle copies only) | `HS2Next/Compatible/Hanmen/Item Cutoff`, `HS2Next/Compatible/Hanmen/Item Alpha` (`HS2NextHanmenItem.hlsl`, `HS2_HM_ALPHA`) | mod Studio items (Grove Street buildings and cars, Sekiro weapons, jungle plants and rocks: about 3,840 of 4,250 materials, 3,450 flagged slots) and one map of the reference scenes (506 slots) | 0.0001 over 5,643 values (`hmi_fit.txt`); Item Alpha 0.0000 over 1,557 (`hmi_alpha_fit.txt`) | 0.0004 on 10 items (front and back light; 0.0000 on 8, a cliff 0.0034 and a banana 0.0019 from the detail height normal); 0.0015 with shadows | an Amplify colour-mask family of its own (not an AIT copy): weighted-sum regions (unsaturated) for colour, alpha, metallic x lerp(1, MG.B, MetallicMask) and glossiness x lerp(1, MG.R, Roughness); occlusion G also on the direct specular; N.V saturated (not abs); emission x MainTex; ambient translucency on by default (saturated), direct translucency unsaturated (Item Alpha: none); wet smoothness lerp(s, 1, ExGloss WetnessPower); detail maps on R (metal / gloss / smoothstep(0, 0.8) occlusion) with repeating UVs. ShaderMap.CopyAccepts maps only the main copies (10 / 6 copies share these names); approximate: detail height normal, tearing threshold, detail gloss / alpha interplay |
| Other bundle copies of `Hanmen/Item Cutoff` (`ShaderMap.CopyRoutes`); `GDC/Item Cutoff` | `HS2Next/Compatible/Hanmen/Item Cutoff (v0.9 copies)` (`HS2NextHanmen.hlsl` `HS2_HANMEN_V09` with the `HS2_V09_*` colour names), `HS2Next/Compatible/GDC/Item Cutoff` (`HS2NextHanmenItem.hlsl` `HS2_HM_GEN2`), `HS2Next/Compatible/Hanmen/Item Cutoff (pattern copy)` (`HS2_HM_P91`) | 52 / 54 / 58 / 68-property copies: Rose and Asslie weapon / vehicle packs, the Japanese Streets map (852 slots), the parts of a character-parts mod in the reference scenes; the 68-property gen-2 copy and GDC/Item Cutoff: Rose Historical swords, GDC character accessories (79 materials, 117 card slots); the 91-property copy: four road maps (17,190 slots), rifle slings | v0.9 copies 0.0000 except the blocks on properties they lack (`hmi_v09c_fit.txt`); gen-2 0.0001, GDC 0.0002, pattern copy 0.0002 over 5,643 values (`hmi_fit.txt` manifest lines); pattern decode / dither 0.0000 over 1,092 (`hmi_p91_fit.txt`) | v0.9 copies 0.0000 on 6 items / parts (0.0009 with shadows); slings 0.0000; swords 0.0000-0.0005; GDC parts as items 0.0019 (cyber arm 0.0147 front from the detail height) | routed by property presence (first passing route wins, before Fitted / CopyAccepts). v0.9 copies: occlusion from G in the 54+ copies, the 68-property copy's plain clip and weaker detail height, the 52-property copy's `_EMISSIONCOLOR2_ON` not reproduced (the v0.9 model reads R / dithers); gen-2: no specular occlusion, emission without MainTex, abs(N.V), translucency T saturate(T) with the ambient part lit by the main light, 8x8 Bayer dither while Cutoff > 0; pattern copy: lerp-chain regions with Studio patterns, MG = metal mask / emission / translucency mask / gloss, emission x EffectMap, interleaved-gradient-noise alpha clip. Still on the fallback: the 64-property stub, a GDC bag's 67-property copy, the older Item Alpha copies |
| `Hanmen/Clothes True Replica Opaque` | `HS2Next/Compatible/Hanmen/Clothes True Replica Opaque` (`HS2NextHanmen.hlsl` `HS2_HANMEN_REPLICA`) | mod Studio items (950 materials, 541 flagged slots: weapons, instruments, props) and mod clothes | 0.0000 over 3,135 values (`hcr_fit.txt`); the `_EMISSIONCOLOR1_ON` copy 0.0000 over 171 (`hcr_emkw.txt`) | 0.0000-0.0002 on 8 items (front and back light); 0.0010 with shadows (a guitar 0.0126, a shotgun from behind 0.0105: shadow edges) | an Amplify colour-mask family of its own (not an AIT copy): weighted sums w = (1 - R - G - B, R, G, B), not clamped, for colour, metallic x lerp(1, MGM.B, `_MetallicMask`k) and glossiness (x MGM.R x DetailMainTex.B; above 1 allowed); occlusion lerp(1, AO.R, strength) plus two detail occlusions added on DetailMask R / G; emission `_EmissionColor` x strength x MGM.G (keyword copy: 5 `_Color` MGM.G); normal normalize(N.xy + g1 + g2, 0.5) (the map's z replaced by 0.5) with detail height gradients; clip(MainTex.a x saturate(AO.B + `_AlphaEx`) - `_Cutoff`) (AlphaToMask only as the clip); Cull Off |
| `Hanmen/Clothes True Cutoff` (main bundle copy), `Hanmen/Clothes Cutoff v0.9` | `HS2Next/Compatible/<same name>` (`HS2NextHanmen.hlsl` `HS2_HANMEN_CUTOFF` / `HS2_HANMEN_V09`) | mod Studio items (270 materials, 164 flagged) and mod clothes | True Cutoff 0.0001 over 2,646 values (`hct_fit.txt`); v0.9 0.0000 over 984 (`hcv9_fit.txt`) | True Cutoff 0.0000 on 7 items, 0.0006 with shadows; v0.9 0.0000-0.0001 on 5 items, 0.0036 with shadows | True Cutoff: the AIT Clothes True-like composite (albedo MainTex x BaseColor x Color, metal DetailMainTex.R / gloss B, region 4 by ColorMask.B with lerp(1, MGM, mask) factors), wetness lerp(m or s, 1, saturate(ExGloss x WetnessPower(2) x saturate(WM.R + WM.G))); v0.9: weighted-sum colours `_Color1/3/4/5` and per-region metal / gloss, lerp(w1 x1 + R x2 + G x3, x4, B); both: Amplify's 8x8 ordered dither of the alpha, switched on by any `_Cutoff` > 0, tearing clip(AO.B + `_AlphaEx` - 1), normal UnpackScaleNormal plus detail height gradients (gain 2). The 71-property clothing copy of True Cutoff keeps the URP/Lit fallback (`ShaderMap.CopyAccepts`) |
| `hooh/StudioItemStandard`, `hooh/ClotheReplicaOpaque`, `hooh/ClotheReplicaAlpha`, `hooh/ClotheReplicaAlphaCullOff` | `HS2Next/Compatible/<same name>` (`HS2NextHooh.hlsl`) | mod Studio items (1,284 materials, 935 flagged slots: furniture, vehicles, plants, glass) and mod clothes (ClotheReplicaOpaque) | 0.0000-0.0004 over 1,587 values each (`hooh_fit.txt`; a grazing back-face highlight 0.006); ClotheReplicaOpaque 0.0003 over 1,938 (`hooh_opaque_fit.txt`) | 0.0000 on 5-6 items or card parts per shader (front and back light); 0.0000-0.0016 with shadows | one hooh property set whose colours, patterns and emission have **no visible effect** (HS2's Studio colour writes do nothing on these, and here neither): albedo MainTex x BaseColor; metallic `_Metallic` x `_MetalicGlossMap`.R / smoothness `_Glossiness` (StudioItemStandard), DetailMainTex R / B (ClotheReplica; Opaque adds `_GlossMetalMap` R / A x `_SmoothnessMultiply`, saturated; AlphaCullOff multiplies by DetailMainTex1 R / A); three normal maps by BlendNormals; Alpha: SrcAlpha blending with ZWrite On and clip(`_CutoutMap`.R - `_Cutoff`); AlphaCullOff: clip(MainTex.a BaseColor.a - `_Cutoff`), Cull Off |
| `Hanmen/Clothes True Pantyhose`, `Hanmen/Clothes True Transparent` | `HS2Next/Compatible/<same name>` (`HS2NextHanmen.hlsl` `HS2_HANMEN_BLEND`, `HS2_HANMEN_PH`) | mod legwear (12 parts in the reference scenes), mod items / map parts (Transparent) | Pantyhose 0.0007 over 1,650 values, Transparent 0.0000 (`hph_fit.txt`) | Pantyhose 0.0017 on 2 parts, Transparent 0.0000 on 3 items (also with shadows) | True Cutoff's surface blended SrcAlpha / OneMinusSrcAlpha (colour mask RGB, ZWrite Off, Cull Off). Transparent: alpha MainTex.a (its fresnel / alpha-mask toggles are compiled by no copy). Pantyhose: each detail map pulls the alpha towards `_DetailAlpha1/2` by mask x Luminance(detail) (Unity's linear-space luminance weights, unclamped, in turn), then `lerp(alpha, 1, saturate(bias + scale (1 - N.V)^power))`; alpha 0 stays 0; Item-Cutoff-style detail height normals |
| `Hanmen/Clothes True Alpha` (87-property copy), `Hanmen/Clothes True Cutoff` (71-property copy) (`ShaderMap.CopyRoutes`) | `HS2Next/Compatible/Hanmen/Clothes True Alpha (v2 copy)`, `HS2Next/Compatible/Hanmen/Clothes True Cutoff (v2 copy)` (`HS2_HANMEN_V2`) | a watch glass (25 parts in the reference scenes); office / dress clothing parts | True Alpha 0.0000, True Cutoff copy 0.0012 over 2,325 values | True Alpha 0.0000; the True Cutoff copy 0.0001 on 5 parts (0.0027 with shadows) | "v2" Hanmen clothes copies: True Cutoff's composite with the metallic masks on MGM.R, occlusion from Occlusion.G, Item-Cutoff-style detail UVs / factors / height normals, tearing with AlphaEx saturated, Cull `[_CullMode]`; wet albedo lerp(1, `_WetAlbedoOffset`, `_ExGloss`) and smoothness towards `_WetGlossBase` (approximate). True Alpha: blend `[_BlendSCR]` / `[_BlendDST]`, alpha `smoothstep(0, 0.75, a) x lerp(a AlphaMaster, 1, saturate(fresnel))` (the watch's fresnel bias 5 makes it opaque); translucency not reproduced (the material's is 0) |
| `GDC/Clothes True Cutoff` | `HS2Next/Compatible/GDC/Clothes True Cutoff` (`HS2NextHanmenV3.hlsl`) | GDC mod clothes (28 installed-card slots) | 0.0007 over 2,034 values (`hv3_fit.txt`) | 0.0232 on 6 parts (0.0180 with shadows; back faces without the normal flip up to 0.13) | "v3" Hanmen clothes copy: composited colour / metal / gloss with masks on MGM.R, Item Cutoff's lighting (N.V saturated, translucency x MGM.B, transmission) with the occlusion off the direct specular, 8x8 dither and tearing only while `_Cutoff` > 0 (tearing threshold of AlphaEx, 0.925 at AlphaEx <= 0); not reproduced: back faces with `_NormalBackDirInvert` 0, `_AlphaMaster` < 1, the wet splats / streaks, blue-noise screen mapping. The 81-property ROC copy of Hanmen/Clothes True Cutoff shades differently and keeps the fallback |
| `hooh/ColorableObjectReplica` (main bundle copy) | `HS2Next/Compatible/hooh/ColorableObjectReplica` (`HS2NextHoohColorable.hlsl`) | mod Studio items (135 of 173 materials, 107 of 129 flagged slots: seats, boards, signs) | 0.0000 over 1,155 values (`hooh_cor_fit.txt`) and 126 (`hooh_cor_decode2.txt`) | 0.0000 on 7 items (front and back light); 0.0010 with shadows (a snowboard 0.0045) | colours `saturate(1 - R - G - B) Color + R Color2 + G Color3 + B Color4`; metallic / smoothness saturate(lerp(`_Metallic`, 1, R (`_Metallic2` + `_Metallic3` + `_Metallic4`)) + MS.R / MS.A) (only the mask's R mixes them); occlusion lerp(1, AO.R, strength); emission texture x `_EmissionBoost`; three normal maps by sequential whiteout blends; Cull Back. Three other copies (`_Diffuse` / `_Metalic`, `_MetallicSmoothness1`) keep the fallback |
| `hooh/Accessory/RMA/{Opqaue, Cutout}`, `hooh/Clothing/RMA/{Opqaue, Transparent, MaskedTransparent}` (the names are spelled so) | `HS2Next/Compatible/<same name>` (`HS2NextHoohRma.hlsl`) | mod Studio maps and items (277 materials, 1 flagged slot: map props and plants, see-through clothing parts) | 0.0000 over 1,479 values each (`hooh_rma_fit.txt`); Accessory colour-mask metal / gloss 0.0000 over 183 (`hooh_rma_acc.txt`) | 0.0000 on 11 items, map parts and clothing parts (front and back light; the 5 shaders); 0.0000-0.0005 with shadows (a small camping prop 0.0070 on 1,952 px) | a packed map "Gloss, Metal, AO" (`_MetallicGlossMap` R / G / B): metallic = saturate(sliders + G), smoothness = saturate(sliders + R), occlusion = B x `_OcclusionStrength` (a product: strength 0 removes the ambient); Accessory: albedo MainTex x lerp(1, `_Color`k, mask k) per channel, sliders weighted by the mask (no base region); Clothing: albedo MainTex x BaseColor, sliders = DetailMainTex R / B (+ `_ExGloss`); three normal maps by whiteout, each scaled by its `_BumpMapMask` channel; `[_CullMode]` taken raw (Clothing Opqaue culls Back); Cutout clip(MainTex.a `_alpha` - `_Cutoff`), Transparent / MaskedTransparent SrcAlpha blending (alpha MainTex.a BaseColor.a; Masked also clips at `_Cutoff`) |
| `Nature/SpeedTree`, `Nature/SpeedTree Billboard` | URP's `Universal Render Pipeline/Nature/SpeedTree7` / `... SpeedTree7 Billboard` | vanilla trees and bamboo (1,558 vanilla item materials) and maps | - | not measured: the Built-in reference renders the trees nearly black in the batch harness (wind / LOD data) | see "Map shaders": same properties and keywords, in the players through the shader variant collection |
| `AIT/Skin True Face` | `HS2Next/AIT Skin True Face` | face `o_head` | 0.0155 over 5958 values (0.0000-0.0031 per block except the eyebrow highlight at gloss 0.9: 0.07); beard set 0.0001 over 1485 values | 0.0002-0.0012 (3 cards, front/back light), 0.0006-0.0015 with shadows; beard on 8 male cards: footprint IoU 0.94-0.98 (was 0-0.43), 0.0011-0.0021 inside the footprint where the pipelines agree without the beard | eyebrow UV/rotation decoded with ramp textures; beard (`_Texture5`) on mirrored UV0 (abs(x u + z - 0.5), y v + w), weight `saturate(2a) pow(R, 4 - 3a)`, G = height map (forward differences at 0.0055 UV, strength 1.5 a); liquid (`_Weathering*`) not reproduced |
| `AIT/Skin True` | `HS2Next/AIT Skin True` | body `o_body_cf` slot 0 | 0.0013 over 951 values | 0.0003-0.0017, 0.0025-0.015 with shadows (back light) | clothes alpha masks, nipple radial UV warp, nails, underhair |
| `AIT/Skin Translucency simple` | `HS2Next/AIT Skin Translucency simple` | tongue `o_tang`, teeth `o_tooth` | 0.0000 over 528 values | 0.0000-0.0011, up to 0.007 with shadows | matcap lift; occlusion map ignores its tiling; detail-normal (`_DetailNormalMapScale`) not reproduced |
| `AIT/Skin Translucency` | (none, URP/Lit fallback) | prefab placeholder `cf_m_skin_body_00_create` only | - | - | replaced by the body draw material at load, never visible |
| `AIT/Eye Translucency` | `HS2Next/AIT Eye Translucency` | eyeballs `o_eyebase_L/R` | 0.0027 over 4005 values (frontal iris lift and every layer/UV case 0.0000; tilted-view lift up to 0.035) | eye region with face skin 0.0012-0.0027; eyeball alone 0.006-0.013 without highlights, 0.014-0.021 with | layered sclera/iris/pupil/highlight with decoded UV transforms; parallax and the iris lift's view dependence are the gaps (see header) |
| `AIT/eyelashes` | `HS2Next/AIT eyelashes` | `o_eyelashes` | 0.0263 over 780 values (all from a sub-cutoff 4x4 screen door in synthetic clip cases; game-value cases <= 0.002) | adds nothing measurable over the face skin (0.0006-0.0012) | premultiplied, coverage pow(R, CutoutScale + 2(1 - Color.a)), two-sided Lambert |
| `AIT/main eyeshadow lambert` | `HS2Next/AIT main eyeshadow lambert` | eye-socket shadow `o_eyeshadow` | 0.0000 over 348 values | adds nothing measurable over the face skin | multiply blend, unlit: dst * lerp(1, Color, Color.a * pow(MainTex.a, ShadowScale)) |
| `AIT/main namida` | `HS2Next/AIT main namida` | tears `o_namida` | 0.0000 over 942 values; grab-pass refraction cases (`namida_refraction.txt`) 0.0001 over 93 with `-urpAsset production` (0.0029 under the measurement asset, which has no opaque texture) | invisible at the game default (tearsRate 0); +0.0003-0.0005 over the face skin at `_NamidaScale` 0.5 / 1 | tear mask growth pow(G, 8 - 7s) * saturate(s / 0.3); refraction from URP's opaque texture while `_HS2NextSceneOpaqueValid` is 1 (section "Scene textures") |
| `AIT/Liquid` | `HS2Next/AIT Liquid` | fluids overlay, body `o_body_cf` slot 1 | 0.0009 over 1314 values | invisible at the default (0 levels, identical to body only); +0.0014-0.0022 at siru level 1, +0.0034-0.0048 at level 2 (over the body skin) | region mask by colour, drip map turned 180 degrees, premultiplied Standard with F0 0.002 highlights |
| `AIT/mnpb` | `HS2Next/Mosaic` (the name `HS2Next/AIT mnpb` is refused by `ShaderMap.Supply`) | the mosaic set: censor patches `o_mnpa/o_mnpb` (`N_mnpb`, always shown), `cm_o_dan00` / `cm_o_dan_f`, every slot whose original shader is `AIT/mnpb` | not a fit: HS2's look is a procedural pattern | MosaicTest MX1-MX6, MosaicTest.render MR1-MR5, MR7 | the original: opaque, unlit, texture-less screen-aligned cells of 1 / d in aspect NDC (d = the camera's distance to the patch's origin) with a hashed skin shade (the +0.045 G/B spread of the flat fit). Replacement: two modes per member set by the mosaic unit - texture (the body composite at transferred body UVs, or a part's own main texture, sampled at block-quantized UVs at the mip of the block, lit by the skin BRDF with the smooth vertex normal) and procedural (HS2's cell look with HS2Next's own hash); block / cell per camera through a property block: at least HS2's cell for that camera and max(4 px, ceil(L / 100)), power-of-two UV steps (Runtime/Chara/Mosaic/MosaicCamera.cs); queue 2500, a constant-only polygon offset 0 / -1 (the member wins a tie with the skin under it, never against clothes over it: MR7), no shadow caster, MotionVectors / DepthNormals |
| `AIT/main Light` | `HS2Next/AIT main Light` | glowing accessory parts (waist lamp `chara/00/ao_waist_00` glow sphere `pSphere_light`, material `Light`; also in `ao_hand_00` and `studio/00`) | 0.0000 over 291 values | lamp alone 0.0000 (front and back light; a renormalized normal gives 0.0038); whole character (2 cards, full view) 0.0656 / 0.0650 on the URP/Lit fallback, 0.0160 / 0.0159 fitted (the rest is other parts) | unlit Amplify glow, queue Overlay+501, blend One OneMinusSrcColor (screen), ZTest LEqual; colour `_Color (1 - max(M.rgb)) + M.r _Color2 + M.g _Color3 + M.b _Color4` (`_ColorMask` on uv0 with `_texcoord_ST` then `_ColorMask_ST`), x `saturate(1 - 1.05 (1 - N.V)^_Float0)` with the interpolated, not renormalized normal, x `_Float1`; the original's soft-particle depth fade (`saturate(\|scene - eye depth\| / 1)`) is applied only when the global `_HS2NextSceneDepthValid` is 1 (the project's URP renderer has no depth texture before transparents); ZWrite and fog not measured |
| `Shader Forge/hair_07` | `HS2Next/Shader Forge/hair_07` | every hair part with hair shader type 0 (game default; 971 materials) | 0.0200 over 7260 values; 0.0034 without the wetness (`_ExGloss`) cases, 0.0015 also without highlight peaks above 1.5 | hair alone 0.0029-0.0067 front, 0.0022-0.0052 back (3 cards; same with a 1-pixel eroded mask); 0.0040-0.0115 with shadows | 2x2 screen door (0.2/0.4/0.6/0.8), top/under/base regions with shader constants, angel ring in V.y - N.y, rim, wrap + back-side diffuse, grazing-view smoothness and metallic fade; mesh pattern (both mesh masks on uv1 with their own scale / offset; UH1 mesh set 0.0002 over 1071 values); wetness approximate |
| `Shader Forge/hair_08` | `HS2Next/Shader Forge/hair_08` | hair shader type 1 ("cutout") | 0.0199 over 7380 values (same model) | 0.0028-0.0066 front, 0.0022-0.0052 back (3 cards built with `-hairType 1`) | hair_07 with a fixed `clip(alpha - 0.5)`; `_Cutoff` has no effect |
| `Shader Forge/studio_hair_07` | `HS2Next/Shader Forge/studio_hair_07` | hair-type studio items (e.g. `studio/00.unity3d` `p_acs_hair_odangostu`) | 0.1235 over 5967 values; 0.0081 without the wetness cases | 0.0032 (front and back, the item alone) | hair_07 with `_Color4/_Color2/_Color3`, ring always on, albedo x 1.25, no back-side diffuse; metallic at grazing view and wetness not exact |

Studio item coverage (materials of every vanilla and installed-mod Studio item of the reference install): the item
families above took the share on fitted or mapped shaders from 58.5 % to 74.1 % (vanilla 43.9 → 63.3 %, mods 58.9 →
74.3 %; the `Effects/*` row and the mod families came on top). The denominators include null material slots and one mod pack with
37,500 `Nature Shaders/Tree Leaves` materials. Studio-flagged slots (the ones the item setters write) on a fallback:
vanilla 11 of 1,412, mods 7,383 of 35,787. The largest remaining fallbacks with flagged slots are mod MaterialEditor-style
shaders with their own property sets — `Hanmen/Item Cutoff` (4,225 materials, 3,748 flagged; fitted for its main
bundle copy: 3,441 of those flagged slots take every Studio write — colours 1-3 with metallic / glossiness, colour 4,
emission colour and power — as in HS2; its older copies stay on the fallback, and `_alpha` / pattern writes, which the
original does not declare either, have no effect), the replica clothes and hooh shaders (fitted:
2,913 of 2,954 materials and 1,745 of 1,770 flagged slots on their accepted copies; the Studio colour writes act as
in HS2: on `Hanmen/Clothes True Replica Opaque` and `Hanmen/Clothes True Cutoff` / `Cutoff v0.9` they change the colours
(checked with `HanmenItemCheck`; `hooh/ColorableObjectReplica` and the RMA accessory shaders likewise by the probes), on
`hooh/StudioItemStandard` and `hooh/ClotheReplica*` they have no visible effect, because the originals ignore those
properties),
`Custom/SurfaceShader`, `RMAShaderCutout` / `RMAShaderOpaque` — and, with few or no flags, `Nature/SpeedTree` (mapped),
`KriptoFX/ME/Distortion` (82 vanilla materials: a grab-pass distortion that needs the URP opaque texture) and the water
shaders of the sea items (AQUAS / Lux / NatureManufacture; the fallback ignores `USINGWATERVOLUME`).

Shared code: `HS2NextLights.hlsl` (light loop, light layers; see "URP pipeline features"), `HS2NextBRDF.hlsl` (Built-in BRDF1 in URP: Disney diffuse + GGX, SH ambient,
Amplify-style translucency with `transColor`, `transShadow`, and a direct-light-only albedo `lift`),
`HS2NextSkin.hlsl` (skin surface for body and face, `HS2_SKIN_BODY` / `HS2_SKIN_FACE`),
`HS2NextHair.hlsl` (the three Shader Forge hair shaders, `HS2_HAIR_CUTOUT` / `HS2_HAIR_STUDIO`),
`HS2NextClothes.hlsl` (the AIT clothes / item family: `HS2_CLOTHES_TRUE`, `HS2_CLOTHES_ITEM`, `HS2_CLOTHES_BLEND`,
`HS2_CLOTHES_MEKA`, `HS2_CLOTHES_FRESNEL_MULT`, `HS2_CLOTHES_CALPHA`, `HS2_CLOTHES_STUDIO`; its header lists every measured
formula), `HS2NextStudioStandard.hlsl` (the two `AIT/main standard2 studio` shaders and the older `AIT/main standard2`,
`HS2_MS2_ALPHA`, `HS2_MS2_OLD`).
`HS2NextBRDF.hlsl` also has an Amplify-style `transmission` term and the Standard toggles `specularHighlights` /
`glossyReflections`. `ShaderMap.Swap` keeps the original's effective render queue and drops material keywords that the
original's bundle copy does not declare (bundle copies are compiled with only the variants their materials used).
Material keywords are the ones the bundle material was loaded with, as in HS2 and in a player: the Unity Editor switches
`[Toggle]` / `[MaterialToggle]` keywords to the float values when a loaded material's properties are first read, on
`new Material(source)` and on a shader assignment, which neither HS2 nor a player does; copies and swaps go through
`Runtime/Rendering/MaterialCopies.cs`, which keeps the loaded keywords (otherwise Editor copies would carry the switched
ones, for example `_DITHERSWITCH_ON` on a mod glove with `_DitherSwitch = 1` but without the keyword). The probe tools
take the loaded keywords too (`MaterialCopies.KeywordsOf`; `SkinFit` `-drawerKw` gives the drawer behaviour). Every
regression set above and every real comparison was measured with the loaded keywords; with the drawer keywords no
Built-in reference value and no table number differs. The fit sources' switched keywords
were only ones their bundle shaders do not compile (`_MESH_ONOFF_ON` / `_RINGCONTROLR_ONOFF_ON` on hair, `_ALPHA_A/B/D_ON`
on skin and clothes); a probe material keeps its keywords through SetFloat of the toggle floats, property reads and
renders (only a plain shader assignment runs the drawers again).
Built-in's `UnpackScaleNormal` scales xy before rebuilding z; URP's `UnpackNormalScale` does not, so
scaled normals need the Built-in form (`HS2UnpackNormalScale`).

## Map shaders

The map shader families. Entries in `ShaderMap.Fitted`'s block for maps; package and built-in targets (URP's SpeedTree7 / SpeedTree7 Billboard / Terrain Lit, Unity's skyboxes) are in
the players through `Editor/MapShaderSet.cs` (the shader variant collection with their keyword sets; `Tools/map_player_check.py`
checks the player). What a plain material swap cannot do runs in `MapShaderHooks` (post-load step 11): billboard materials
(BillboardAsset, not renderer slots), the water inputs, same-name skybox targets; mirrors in `MapMirrors`, terrain templates
and terrain prototypes in `TerrainMap`. Original property lists: `Tools/survey/map_shader_props.json` (bundle metadata,
`Tools/survey/map_shader_props.py`); `MapShaderTest` S2 checks every replacement against them. Probes as above (`SkinFit`'s
rig, Built-in vs the measurement URP asset (`Assets/HS2Next/Settings/Measure/MeasureURP.asset`), centre means, linear): `MapShaderTest` S8 for the Editor's built-ins, `MapShaderTest.probe`
(play mode) for originals that exist only in map bundles (the bundle copy taken from a loaded map through `MapMaterials`).

| Original | Replacement | Used by | Probe RMS (linear) | Real asset | Notes |
|---|---|---|---|---|---|
| `Nature/SpeedTree`, `Nature/SpeedTree Billboard` | URP's `Universal Render Pipeline/Nature/SpeedTree7`, `... SpeedTree7 Billboard` | 15 vanilla maps (5,081 active slots, 47 % of vanilla), 1 mod map; 940 vanilla billboard renderers | not measurable: the Built-in reference renders the trees nearly black in batch | `MapRenderTest`: the trees alone are lit (mean luminance 0.13–0.33), mean colour over the tree pixels vs HS2's capture of the same view (map 17: HS2Next (86, 85, 66) / HS2 (82, 83, 66); the rest of the difference is fog / Studio ambient) | same property names and keywords as the 2018 shaders (S2: every property of `Nature/SpeedTree` exists with its type and default); keyword sets of the census (`EFFECT_BUMP EFFECT_HUE_VARIATION` + one `GEOM_TYPE_*`) in the shader set; billboards through `MapShaderHooks`; wind: Unity 6 animates the 2018 `Tree` components' wind (V3, maps with a `WindZone`); no lightmap path (lightmapped slots go to URP/Lit through `RouteLightmapped`) |
| `FX/MirrorReflection_Transparent` | `HS2Next/Compatible/FX/MirrorReflection_Transparent` | mirrors of 10 vanilla maps | 0.0000 over 63 values (constant textures, two backgrounds) | `MapRenderTest` map 29 mirror view; `MapShaderTest` S6 synthetic mirror (the red quad's reflection lands where the mirrored point projects) | rgb = `_MainTex` × `_ReflectionTex` × `_Color`, alpha = `_MainTex`.a × `_Color`.a (the reflection's alpha unused), SrcAlpha / OneMinusSrcAlpha, ZWrite On; the reflection is sampled at the screen position, rendered by `MapMirrors` |
| `FX/MirrorReflection` | `HS2Next/Compatible/FX/MirrorReflection` | 1 mod map | not probed (no Built-in copy outside that map) | - | the published Unify-wiki mirror: opaque texture × reflection |
| `Custom/Silhouette` | `HS2Next/Custom/Silhouette` | the train crowds of maps 35 / 36 (53 active slots) | 0.0000 over 60 values | `MapRenderTest` map 36 | depth-only pass, then unlit `_Color` blended with alpha `_Color`.a (SRPDefaultUnlit before UniversalForward) |
| `KriptoFX/ME/Particle` | `HS2Next/Compatible/KriptoFX/ME/Particle` | effects of 5 vanilla maps (63 active slots) | 0.0000 over 48 values (additive and alpha factors, tint, texture, vertex colour, two backgrounds) | - | `Blend [SrcMode] [DstMode]` from the material (the BlendAdd / BlendAlpha keyword's factors), `2 × vertex colour × _TintColor × texture`, alpha saturated; `Clip_ON` clips at `_Cutout`; frame blending, soft particles, Fresnel fade not reproduced |
| `KriptoFX/ME/Distortion` | `HS2Next/Compatible/KriptoFX/ME/Distortion` (approximation; ShaderMap block "effects") | grab-pass distortion of 5 vanilla maps (88 active slots) and 82 vanilla item materials (heat haze) | not probed | `SceneTexturesTest` T7 (draws the shifted background, nothing without an opaque texture), `.play` P3 | the opaque texture shifted by the normal map's xy x `_Distortion` texels x vertex alpha (x soft-particle fade), x `_MainColor`; main texture / Fresnel / alpha clipping / cutout toggles read as floats; height and refractive terms not reproduced; draws nothing without the opaque texture |
| `Lux Water/WaterSurface*`, `NatureManufacture Shaders/Water/*`, AQUAS, `FX/Water`, ASE / Ciconia / misc water | `HS2Next/Compatible/Water` (approximation) | 4 vanilla maps with sea / rivers (123 active slots), 33 mod slots | not a fit | `MapRenderTest` maps 0, 14, 32, 34 (not black, no magenta) | transparent surface: tint × (SH + diffuse lights) under GGX highlights, two scrolled normal-map layers on world XZ, reflection probe weighted by Fresnel, alpha lerp(0.55, 1, F); inputs filled at load from the original's properties (`MapShaderHooks.WaterInputs`); with the scene textures the bottom comes from the opaque texture with a refraction shift (`_HS2WaterRefraction`) and the surface fades out in shallow water (`_HS2WaterEdgeFade`); no tessellation or caustics; `USINGWATERVOLUME` ignored |
| `NatureManufacture Shaders/Water/Water Particles Foam` | `HS2Next/Compatible/Water Particles Foam` (approximation) | map 14's foam particles (15 slots) | - | - | `_ParticleTexture` × `_Color` × vertex colour lit by SH and the lights, alpha texture × `_Opacity` × vertex alpha |
| `Standard_culloff`, `Standard (Specular setup)_culloff` | `HS2Next/Standard(Two Sided)`, `HS2Next/Standard (Specular setup)(Two Sided)` (new) | 3 vanilla maps (7 slots) | as the Standard rows | - | two-sided copies of the older Standard (23 / 25 properties, all present: S2) |
| `Legacy Shaders/Self-Illumin/Bumped Diffuse` | `HS2Next/<same name>` (generated) | 3 mod maps | 0.0000 over 126 values against the Editor's built-in | - | `Tools/gen_legacy_shaders.py` row (ILLUM + BUMP) |
| `Legacy Shaders/Transparent/Cutout/Soft Edge Unlit` (and `Transparent/Cutout/Soft Edge Unlit`) | `HS2Next/<same name>` | 1 mod map | 0.0000 over 66 values against the Editor's built-in | - | unlit `_Color` × texture, Cull Off; opaque pass at or above `_Cutoff` (SRPDefaultUnlit), then the soft edge below it blended (UniversalForward) |
| `Particles/Standard Unlit` | `HS2Next/Particles/Standard Unlit` | vanilla map 15, 6 mod maps, 790 mod item materials | 0.0000 over 72 values against the Editor's built-in (fade, premultiply, additive, opaque, emission, vertex colour, two backgrounds) | - | blend / ZWrite / cull from the material's floats; measured: alpha output only with a blend keyword (else 1), `_ALPHAPREMULTIPLY_ON` leaves rgb as is; colour modes, camera fading, soft particles (with `_HS2NextSceneDepthValid`) as published; distortion and frame blending not reproduced |
| `Effects/Ice/IceFront`, `Effects/SlimeCutOut` | `HS2Next/Effects/Ice/IceFront`, `HS2Next/Effects/SlimeCutOut` (approximations) | map 34 (13 slots) | not probed | `MapRenderTest` map 34 | lit / unlit surfaces with cube reflection and Fresnel; the slime's grab-pass distortion from the opaque texture (composited over the background shifted by its bump x `_BumpAmt` texels); the ice has no grab pass in the original and draws as before |
| `Hidden/Nature/Tree Creator Leaves (Fast) Optimized`, `Nature/Tree Creator Leaves (Fast)` | `HS2Next/Nature/Tree Creator Leaves` (approximation) | 7 mod maps (44,000 slots) | not a fit (no Built-in reference of the optimized shaders in batch) | `MapRenderTest` `[Fouetty]Military Base` | cutout bumped diffuse; normal from `_BumpSpecMap` G / A (Tree Creator's packing), vertex alpha = baked AO; translucency and shadow texture ignored |
| `Hidden/Nature/Tree Creator Bark Optimized`, `Nature/Tree Creator Bark` | `HS2Next/Nature/Tree Creator Bark` (approximation) | 4 mod maps | not a fit | - | bumped specular: `_SpecColor` × pow(N.H, 128 × `_BumpSpecMap`.r) × `_TranslucencyMap`.a × `_Color`.r |
| NatureManufacture vegetation (`Trees/Tree Leaves Metalic`, `Tree_Leaves_Specular`, `Cross Model Shader`, `Grass/Advanced Grass Specular`; `Trees/Tree Bark Metalic`) | `HS2Next/Compatible/simple_two_side`; `HS2Next/Legacy Shaders/Bumped Diffuse` (approximations) | 5–6 mod maps (about 70,000 slots) | - | - | `_MainTex` / `_BumpMap` / `_Cutoff` as Unity's; wind, colour noise, packed metallic / AO maps ignored |
| `Hidden/TerrainEngine/Splatmap/Specular-Base` (on mesh renderers) | `HS2Next/Legacy Shaders/Specular` (approximation) | 3 mod maps | - | - | the terrain's base-map shader used on meshes |
| `Skybox/Procedural`, `Skybox/Cubemap`, `Skybox/Panoramic`, `Skybox/6 Sided` | Unity's shaders of the same name | mod map skyboxes (`RenderSettings.skybox`, 31 renderer slots) | Unity's own | - | resolved before any bundle loads (`MapShaderHooks.Resolve`), since `Shader.Find` can return a bundle's shader of that name |
| terrain templates (`Nature/Terrain/*`, none) and terrain tree / detail-mesh prototypes | `Universal Render Pipeline/Terrain/Lit`; prototypes mapped like scene materials (`TerrainMap`) | 13 mod maps | URP's own | `MapRenderTest` `hooh.map.school#1`: no magenta (11,198 magenta pixels before the prototypes were mapped) | `_NORMALMAP` when a layer has a normal map |

Map coverage of active non-null material slots (`MapShaderTest` S4, reference install): vanilla 42.2 % (4 of 31 maps
fully covered) → 100 % (31 / 31) with the map families; mods 55.1 % (418 of 650 maps fully covered, 509 ≥ 95 %) → 68.3 %
(451 fully, 520 ≥ 95 %, 570 ≥ 80 %). The rest are single-map shader sets (`Unreal/*` 1 map with 66,552 + … slots,
`Hanmen/*`, vegetation packs), on the URP/Lit fallback.

## URP pipeline features

The app renders with `Assets/HS2Next/Settings/HS2NextURP.asset` (Forward+, rendering layers, main-light shadows 4096 / 4
cascades, additional-light shadows, soft shadows, probe blending and box projection); the fit tools and the Editor render
tests keep `Assets/HS2Next/Settings/Measure/MeasureURP.asset` (Forward, no rendering layers) as their measurement baseline (`-urpAsset
production` on `SkinFit` and `CharaRenderTest` renders with the app's asset). Every lit forward pass declares:

- `_CLUSTER_LIGHT_LOOP` (Forward+) and `_LIGHT_LAYERS`, `_ADDITIONAL_LIGHT_SHADOWS` (multi_compile), and where the shader samples
  reflection probes `_REFLECTION_PROBE_BLENDING` / `_REFLECTION_PROBE_BOX_PROJECTION` (multi_compile_fragment like URP/Lit:
  as `dynamic_branch` keywords URP's build stripping removes every variant of the pass, because it strips the variants
  where an enabled feature's keyword is off and a dynamic keyword is never on in a variant). `_REFLECTION_PROBE_ATLAS` is not declared: under Forward+ with blending the shaders sample the
  probe atlas, so the asset keeps the atlas on.
- The light loops go through `HS2NextLights.hlsl`: `HS2LightIterInit` / `HS2LightNext` (Forward: the per-object list;
  Forward+: the additional directional lights, then the cluster), `HS2LightReaches` (light layers; Forward+ subtractive
  lightmaps) and `HS2LightLayerWeight` for the main light. With only a main light the result is bit for bit the earlier
  loop (all fit regressions unchanged, `RenderPipelineTest` P3: production = measurement asset, max |d| 0); 8 point lights under
  Forward+ equal Forward with a per-object limit of 8 (P4).
- Lightmaps: only the Standard family (`HS2NextStandard.hlsl`) has a lightmap path (`LIGHTMAP_ON`, `DIRLIGHTMAP_COMBINED`,
  `LIGHTMAP_SHADOW_MIXING`, `SHADOWS_SHADOWMASK`; URP's `SampleLightmap` × occlusion instead of SH, subtractive mixing, the shadow
  mask in the main and additional lights). On a lightmapped renderer `ShaderMap.RouteLightmapped` moves slots whose shader has
  no lightmap path (`ShaderMap.SupportsLightmaps`) to URP/Lit. The generated legacy family and two mod rows have the path
  too, and unlit replacements are kept (section "Lightmap paths").
- Rendering layers: `Runtime/Rendering/RenderingLayers.cs` (renderer `1 << layer`, light layers = culling mask; called by
  `ShaderMap.ApplyUrp` and `BundleLights.Prepare`). A light that skips it lights only the Default layer under the app's asset.

## Scene textures and the effect shaders

The production renderer copies depth right after the opaques and keeps the opaque texture at full resolution. Shaders do not test the asset themselves: `Runtime/Rendering/SceneTextures.cs`
sets two globals before every camera renders, `_HS2NextSceneDepthValid` (scene depth before the transparents) and
`_HS2NextSceneOpaqueValid` (`_CameraOpaqueTexture`), 1 / 1 under the app's asset and 0 / 0 under the measurement asset of the fit
tools. Every texture read is behind them, so every fit and regression measured with the measurement asset is unchanged, and
`-urpAsset production` measures the new paths:

| Shader | Uses | Measured (production asset) |
|---|---|---|
| `AIT/main Light`, legacy / `Effects/*` / `Particles/Standard Unlit` soft particles | depth (existing code paths, now live) | `mainlight_depth.txt` (Built-in's depth fade) RMS 0.0000 over 66 values; soft particles = saturate(`_InvFade` dz) within 0.0005 (`SceneTexturesTest` T4) |
| `HS2Next/AIT main namida` | opaque: background shifted by (`_RefractionScale` - 1) x view-space normal x (1 - N.V) / (clip z + 1), per channel with `_ChromaticAberration`; only the shift is added to the premultiplied blend | `namida_refraction.txt` RMS 0.0001 over 93 values (target 0.001) |
| `HS2Next/Compatible/KriptoFX/ME/Distortion` | opaque (+ depth for soft particles) | approximation (T7, play P3) |
| `HS2Next/Compatible/Water`, `HS2Next/Effects/SlimeCutOut` | opaque + depth (water), opaque (slime) | approximations (T8, play P4: maps 0, 14, 32, 34) |
| `HS2Next/Hidden/HS2Dof` | depth (renderer feature `HS2DofFeature`, after post-processing) | `DofTest.render`: CoC = HS2's formula within 0.10 / 255 |
| `HS2Next/Hidden/HS2MosaicWide` | depth (renderer feature `MosaicWideFeature`, after `HS2DofFeature`; option `render.mosaicWide`, default on): the visible mosaic-set members drawn into a mask (depth-tested against the camera's depth buffer with its own jittered view-projection, so a member under clothes leaves no mask; a multisampled camera compares with the depth texture), blocks of max(4, ceil(L / 100), round(D / 12)) px anchored per character, the mask grown by 1.5 blocks, each covered block its mean | `MosaicTest.render` MR3: coverage contains the dilated footprint, runs >= B - 1, option off = byte-identical; MR7: members under clothes give 0 coverage and 0 changed pixels |

Regression: with the measurement asset every sweep cell (94,449 in 50 sets) and every real comparison is unchanged by the
scene-texture paths (0.0000), and the `CharaRenderTest` URP images are byte-identical. With `-urpAsset production` only
`AIT/main Light`'s regression set changes (`mainlight_fit.txt`, RMS 0.0000 -> 0.0568, all of it the `ztest` case at `_BGZ`
-0.3): the Built-in reference draws its backdrop without a shadow-caster pass, so the backdrop is missing from Built-in's
depth texture and the original's depth fade sees nothing, while URP fades against the backdrop's real depth as HS2's
deferred camera would (the depth cases with the backdrop in both depth textures, `mainlight_depth.txt`, match at 0.0000).

## Point / spot falloff, TAA passes and the hair dither

- **Falloff** (`HS2NextLights.hlsl`): HS2 renders point and spot lights with Built-in's falloff texture, URP with 1 / d²
  and a range window. While the global `_HS2NextLegacyFalloff` is 1 (`Runtime/Rendering/LightFalloff.cs`: app option
  `render.lightFalloff = hs2`, the default; `urp` = 0; batch tools `-lightFalloff urp|hs2`) every `GetAdditionalLight(...)`
  after the include returns Built-in's attenuation (a macro at the end of the file redirects the name to
  `HS2GetAdditionalLight`; URP's own functions above it keep URP's): f(u) = 1 / (1 + 25 u) with a linear fade from u = 0.64
  to the range, u = (distance / range)², read like Built-in's 1024-texel texture (f(u - 1/2048)); spots times the default
  cookie's radial profile at tan(off-axis) / tan(spotAngle / 2) (33-entry table, within 0.0095 of the cookie). Measured on
  Built-in's own textures (`SkyAmbientTest.MeasureFalloff`); the falloff fit `SkyAmbientTest.render` R3 (a white
  Lambert floor, 1024 px, URP vs the Editor's Built-in): points 0.00000 mean / 0.0001 max, spots 0.0001-0.0007 mean /
  0.001-0.005 max (with `urp`: 0.035-0.29 mean); HS2 floor captures within 0.63-0.76 / 255. URP-native shaders keep
  URP's falloff.
- **MotionVectors / DepthNormals passes** (`HS2NextPasses.hlsl`): built on the family's DepthOnly vertex / fragment functions
  (`HS2_PASS_A`, `_V`, `_VERT`, `_CLIP`, `_POS`, `_NORMAL` defined by the pass; `HS2_PASS_SIMPLE` for bare-mesh depth passes),
  so they clip like the depth pass and keep the material buffer; in the 44 fitted opaque / cutout shaders (24 hand-written ones
  inserted once with the table `TAA_FAMILY` of `Tools/gen_legacy_shaders.py`, the 20 generated ones by the generator). Needed by URP's
  TAA (render.aa = taa, experimental) and by features that request the depth-normals prepass.
- **Hair dither** (`HS2NextHair.hlsl`): `_HS2NextDitherFrame` (0-3, `Runtime/Rendering/TemporalAA.cs`) shifts the 2 × 2
  screen door per frame for a TAA camera; 0 for every other camera = the fitted pattern.

Regression (`SkinFit.Jobs` 36 jobs, the item jobs 29, `CharaRenderTest -targets -urp -diff`; both URP assets): all 50
output files per asset are byte-identical with and without the falloff, the new passes and the dither, and the
`CharaRenderTest` reports and URP images are identical. The fits light with directional lights only (the falloff does
not enter them); the new passes and dither frame 0 leave the forward output unchanged.

## How to fit a new shader

All tools are editor batch methods (see the file headers for arguments):

1. `SkinFit.Inventory` lists the materials, values, keywords, passes and mesh channels of the shaders
   on assembled characters (`Spike/out/fit/skin_inventory.txt`). Extend the name filter for other families.
2. `SkinFit.Probe -fitShader <name>` varies every property alone around the real material
   (textures W/R/G/B/K/A0, colours, floats at range ends) under four lights
   (`Spike/out/fit/<name>_probe.txt`); read it by keeping the lines that differ from the baseline.
3. `SkinFit.Sweep -fitShader <name> -sweepFile <cases>` runs targeted cases
   (`label | set | sweep | sweep2 | lights | tilt`). Texture values are linear constants `r:g:b:a`
   or ramps `ramp:u:x/0/1/1`; `_VCOL=`, `_UV0..3=` override the probe quad's vertex colour / UVs.
   Ramps plus constant UVs decode UV transforms exactly.
4. `-fitPipe both` renders the same cases with the replacement under URP and prints per-block RMS;
   the case files used for the skin family are in `Tests/FitCases/`.
5. `SkinFit.Real [-fitShader "A;B"] [-limit 3] [-shadows]` assembles cards, draws only the target
   renderers and compares Built-in vs URP on the real textures (`Spike/out/fit/skin_real*.txt`).
6. `CharaRenderTest.Run -urp -diff` gives the whole-character diff and the list of shaders still on
   the fallback.

Probe extensions added for the face parts and overlays (all in `SkinFit`):

- Background: `_BG=r:g:b` (linear clear colour) and `_BGTEX=<texture value>` (unlit backdrop quad behind the
  probe). Blended shaders are read off two backgrounds: out = colour + bg * transmission. `SkinFit.Probe` takes
  several shaders (`-fitShader "A;B"`) and `-probeBgs "0:0:0;1:1:1"`.
- Texture values `src` (the real material's texture) and `rampr:u:...` (a ramp with Repeat wrap, for textures sampled
  with negative or large coordinates; the liquid drips use `-(uv * scale + offset)`).
- Lights by name: any `L<yaw>`, `M<yaw>`, `L<yaw>p<pitch>` (degrees) is built on the fly, so specular lobes can be
  scanned in 0.5 degree steps. `_TANGENT=x:y:z:w` overrides the probe quad's tangents and `_CAMDIST=d` moves the
  probe camera (same framing).
- `SkinFit.Real`: views `eyes` (eye region with the face) and `mnpb` (metrics and dominant colours only),
  `-realSet "a=v"` with `-realSetFor "Shader"` (e.g. siru levels on the liquid only), `-bakeStatic` (posed
  static copies instead of skinned renderers), `-realOut <name>`.
- `SkinFit.DumpTex` writes a material's textures (RGB and A) with channel statistics; `SkinFit.SkinInfo -renderer
  <name>` prints bone scales and skinned normal / tangent lengths. `SkinFit.Inventory -invShaders "A;B" -invOut <file>`
  inventories another family (`Spike/out/fit/face_parts_inventory.txt`).

Method notes from the face parts: sharp highlights (smoothness above about 0.85) are undersampled by the 16x16
probe average, so a single value cannot be compared with a guessed smoothness; scan the lobe over light angles and
fit smoothness and F0 together with an offline BRDF1 model of the probe geometry (camera at 2, FOV 2*atan(0.125),
centre half of a 32x32 target), which reproduces the probe values to four decimals. The liquid's highlight has the
lobe of smoothness 0.9 but 1/20 of the 0.04 F0, and the eye highlight's smoothness also depends on `_Color4.r`; both
were only visible this way. The eye's `_Parallax` matches the tangent-space `ParallaxOffset(0, p, V)` exactly on
flat probes of any tilt, tangent orientation and distance, yet the original shows almost no iris shift on the real
eyeball (also as a static mesh, so not skinning); the replacement keeps the code with weight 0. Case files:
`Tests/FitCases/{eye,eye_parallax,eyelashes,eyeshadow,namida,namida_refraction,liquid,mnpb}.txt`.

Method notes from the beard (`_Texture5`, male faces; `Tests/FitCases/beard_fit.txt`, run with `-card <bearded male
card>` so that `src` is a real `mt_beard` texture): constant UVs plus ramps showed that only uv0 matters and that the
u axis is folded by an `abs` (the texture holds one half of the face). Constant textures hid a second term: on the real
texture the lit cells differed while ambient matched, and white skin with a white `_Color5` (albedo unchanged) isolated
it as a normal tilt driven by the G gradient. That tilt was independent of `_Texture5UV`, of constant UVs, of the
texture's texel count and of the camera distance, i.e. finite differences at a fixed offset in texture space; the offset
(0.0055) came from the width of the band where the difference straddles a sawtooth wrap (`-fitDump`, UV stretched with
`_Texture5UV`). `CharaRenderTest`'s beard footprint must take the face material again after `ShaderMap.ApplyUrp` (the
swap copies materials); without that the URP no-beard render kept the beard and the URP footprint was mostly empty.

Probe extensions added for the glow (`AIT/main Light`, all in `SkinFit.SetProp`): `_FOVT=t` (field of view 2 atan(t)), `_CAMPOS=x:y:z` (camera position, same orientation), `_NEARFAR=n:f` (clip planes), `_DEPTHTEX=1` (the probe camera renders `_CameraDepthTexture`; in URP it is requested per camera), `_BGDEPTH=1` before `_BGTEX` (Built-in backdrop with a shadow-caster pass, so that it is in Built-in's depth texture) and `_BGZ=z` after it (backdrop distance). Method note: a term that changed with the camera distance but not with the quad's distance (`_QUADOFF`) first looked like a function of the field of view; separating `_FOVT`, `_CAMPOS` and `_NEARFAR` showed two terms, a view-angle falloff and a depth fade whose reference depth is a constant raw depth (0.216: Unity's grey default texture bound as `_CameraDepthTexture` when Built-in renders none). The URP renderer copies depth after transparents, so a transparent shader there cannot read the scene depth of its own frame. Case files: `Tests/FitCases/mainlight_{decode,f0,f0b,f0c,depth,urpdepth,fit}.txt`.

Pitfalls found: the probe camera must be perspective (Built-in derives ortho view vectors from the
camera position, URP does not); disable async shader compilation in batch renders; HS2 material
colours are sRGB-converted like any Color property, Vector properties are not; a property that the
source material does not have (e.g. a fit-only `_HS2Fit`) can come through `CopyPropertiesFromMaterial`
as garbage, so do not rely on extra properties in shaders that replace bundle materials.

Probe extensions added for the hair (all in `SkinFit`):

- `-fitPattern` (coverage of the centre 8x8 pixels per cell, `urp` pattern printed when it differs), `-fitStats`
  (min / max and corner values), `-fitDump` (the whole 32x32 R channel per cell in `<shader>_<suffix>_dump.txt`, for
  offline fits of spatial terms such as the hair ring band), `_QUADOFF=x:y:z` (move the probe quad, camera fixed).
- `-hairType 0|1` overrides the cards' hair shader type (hair_08 materials come from type 1); `-fitItem "bundle:prefab"`
  takes the original material from a studio item (Inventory, Probe, Sweep, Real); the item is hidden during probes
  because it sits at the origin inside the probe view.
- `SkinFit.MatProps` lists a material's saved properties (also those the shader does not declare);
  `SkinFit.ScanStudioItems` lists vanilla studio items named like hair with their renderers' shaders.
- `SkinFit.Real` views `hair` (renderers with a Shader Forge shader) and `head` (hair + face parts, for the hair
  shadows on the face); every view also prints mean |d| over a 1-pixel erosion of the common coverage.

Method notes from the hair: the Shader Forge hair has no hidden properties but several baked constants (texture
floors, region gains); decode them with grey and single-channel sweeps under the flat white ambient, where the output
is the albedo, then fit the lighting separately. Spatial terms (the ring band) were found from the per-pixel dumps:
the band varied along image rows only, moved with the probe camera distance but not with the quad position or tilt,
i.e. a function of the view direction; tilting the normal map then gave the normal's share. Two traps: a UV scale read
from `frac` of a ramp at a few constant UVs is ambiguous (uv x -4 and uv x 16 give the same values at multiples of
0.05; step in 0.01), and the real asset showed it as noisier normals; and properties that only matter at grazing
view (here smoothness x (N.V)^2 and a metallic fade) are invisible on untilted probes, so always scan lights at tilts
of 60-75 degrees. Back faces flip the normal but not the tangent frame, which only the ring revealed. Remaining Real
offsets of about 1.5 % brighter in URP persist with constant textures and no ring, so they come from the lighting setup
rather than the surface model; the shadowed views add 0.004-0.006, mostly self-shadow acne and filtering.
Case files: `Tests/FitCases/{hair07,hair08,studio_hair07,hair_ring_profiles}.txt`.

Probe extensions added for the clothes and items (all in `SkinFit`):

- Texture values `ramp@N:` (ramp of N x N texels), channel `xN` in ramps (sawtooth `frac(v N)`, slope N), `mips` /
  `mipsb` / `mipsa` / `mipsc` (256 x 256 texture whose mip level k is the constant G = k / 16; trilinear, bilinear,
  anisotropic, clamp) to read the level of detail of every sample, `_Tex_ST=sx:sy:ox:oy` (texture tiling / offset),
  `_KW=+NAME` / `_KW=-NAME` (material keywords), `_G_Name=v` (global float for debug switches).
- Sweep `-fitPix` (with `-fitPipe both`: per-pixel mean |d| between the pipelines), `-origFind` (reference = the editor's
  shader of that name, e.g. Unity's full Standard instead of a stripped bundle copy), `-fitMaterial "<name> (Instance)"`
  to probe one real material (with `src` textures).
- `SkinFit.Real` frames one view per clothes kind (`ct_*`) or accessory slot (`ca_slot*`) and swaps shaders through
  `ShaderMap.Swap`; `ScanBundles -scanShader "A;B"`; `MatProps` prints the textures' format and sampler state.

Method notes from the clothes and items: set every input of a case explicitly. A material value left at the probe
material's value can fake a behaviour: `_UVScalePattern = (1, 0.5)` on the stocking material made the detail maps look
as if they were read with (u, u), which disappeared once the property was swept. Flat-probe averages hide per-pixel
effects, so probe each shader once with a real material's textures and values (`-fitMaterial`, `src`, `-fitPix`); that
is how the 0.4 x back-face translucency of Clothes True and the tangent frame showed up. The Built-in surface-shader
tangent frame (interpolated vertex tangent, binormal built per vertex, normal; normalized after the transform) is
needed on real meshes: with a per-pixel re-orthonormalized frame, glossy accessories differed by 0.004-0.011, with
the Built-in frame by 0.0000. ZWrite of the blended variants was only measurable on overlapping real geometry
(stockings: ZWrite Off). Bundle copies of the originals differ in their compiled keywords (a stocking material's copy of
Clothes True dithers by `_DITHERSWITCH_ON`, another copy has no such keyword), so keywords are honoured per material and
filtered by the original's keyword space. Case files: `Tests/FitCases/{item_*,ct_*,cat_*,bc_*,ca_*,meka*,clothes_*,
glass,standard,uvs_*,trans_back}.txt`; the `*_fit.txt` files are the full regression sets per shader.

Method notes from the Studio item tail (Standard family, legacy, unlit, particles):

- What a bundle shader is: the parsed shader object (property list with flags and defaults, subshader and pass names,
  pass render state, custom editor) tells Unity's own built-ins (same 27 properties and passes as the Editor's, custom
  editor `StandardShaderGUI` / `StandardRoughnessShaderGUI`) from mod copies (`Standard(Two Sided)`: Standard's
  properties with Cull Off). An item scan tool of the development tree (not published) listed Studio item materials by
  shader with `-fitItem` specs, mesh channels and a property / pass comparison with the Editor's shader of the same name,
  and ran SkinFit methods on zipmod items; `SkinFit.Jobs` and `SkinFit.LoadItem` cover zipmod items here. Without a material of the shader
  (`-fitItem` naming nothing) `-origFind` probes the Editor's built-in at its defaults.
- Property flags matter: Unity's Standard declares `_Metallic` `[Gamma]` (the float is sRGB-converted at upload in linear
  space); a replacement without the flag gave 0.25 RMS on metallic 0.5. The roughness copy has no such flag.
- Built-in builds the parallax view vector in object space (object-space view vector in the object-space tangent frame),
  so non-uniformly scaled objects (the tilted probe quad) differ from a world-space frame (0.004).
- **The shadow rule (described here once).** Built-in forward rendering applies no realtime shadows to materials in
  queues above 2500 (a glass with shadows: 0.0123 -> 0.0025 after the change). Replacements that follow it declare
  `_RECEIVE_SHADOWS_OFF` (`#pragma dynamic_branch_local`) and set `HS2ShadowReceive` (`HS2NextBRDF.hlsl`, default 1) to 0
  under it: the Standard family, the legacy shaders and the generated `simple_two_side`; `ShaderMap.Swap` enables the
  keyword for those queues. `AIT/main standard2 studio alpha` always sits in a transparent queue and sets
  `HS2ShadowReceive = 0` itself (`HS2NextStudioStandard.hlsl`). The other AIT fits do not declare the keyword: an AIT
  material that a bundle puts above 2500 would still receive shadows (none measured). Code that moves a material's queue
  across 2500 must set or clear the keyword (`ShaderMap.ReceiveShadowsOff`).
- `SkinFit.Real` on items with LOD groups draws every LOD and is not a shader comparison there (a bat with three LODs:
  0.009 while its material on real textures probes 0.0000); particle systems are not simulated in the batch harness.
- Case files: `Tests/FitCases/{std_specular,std_roughness,std_roughness_trans,std_twosided,std_parallax,std_generic,
  legacy,legacy_real,particles,ait_additive,sf_item_add,sf_item_add_dec}.txt`.

Method notes from the Studio AIT item shaders (`AIT/main standard2 studio`, `... alpha`, `AIT/Item studio`):

- Tools: `SkinFit.Jobs -jobFile <txt>` runs several SkinFit methods in one Unity session (one line per job, arguments as on
  the command line); `-fitItem` takes several items (`a;b`) and `id:<no>` (a vanilla Studio item by list number, loaded
  through the catalog like `ItemRealization`); `ScanItems` lists every vanilla Studio item with its slots' shaders and
  loaded keywords; `KwCheck` shows what the probe tools see of a material's keywords; `MatProps -mpOut`; Sweep
  `_SETCOLOR_<Name>=r:g:b:a` calls Material.SetColor on any property (Studio writes the glass colour into the Range
  properties `_Metallic4` / `_Glossiness4` this way: no effect in the original); `DescribeShader` prints property flags;
  `Real` switches off lights inside the compared objects (`-keepLights` keeps them: a lamp item's own point light made a
  0.47 "shader" difference).
- Region and alpha rules that a lerp-chain guess gets wrong were separated with one-channel masks and colours below 1:
  colour regions combine with a per-channel max (c1 weight `1 - R - G - B`), metallic / glossiness with a weighted sum, the
  alpha variant's region alpha with yet another rule (region 1's alpha minus the mask sum, the others weighted); the pattern
  colour's alpha is an opacity `saturate(a - p)`. The detail albedo lerps the colour towards the detail map (seen only with
  a non-white main texture and emission base on).
- `_EmissionColor` of the two `main standard2 studio` shaders has the HDR flag: the original uses the serialized value as
  is, a replacement without `[HDR]` gamma-converts it (a jukebox with colour (9.1, 8.3, 0.5): 16.7 mean difference). Probes
  that set colours with SetVector / SetColor do not show this; only the real material did. `AIT/Item studio`'s colour has
  no flag.
- Transparent ZWrite was only visible on overlapping real geometry (snowflake, veils: 0.058 / 0.015 with ZWrite Off, 0.0000
  / 0.0004 with ZWrite On).
- `Item studio` is AIT/Item plus Meka's pattern system, but three terms differ from AIT/Item and only showed on a real
  material with non-default values (the cat: `_BitangentRim` 0.15, `_FresnelSetting` (-2, 12, 2, 0.6), dither): the rim,
  the emission colour's alpha and the dither / `_alpha` order. The AIT/Item regression set passed on Item studio before
  they were found; run each new shader once with real materials of several items.
- `AIT/main standard2` (older) differs from the studio version in almost every rule (lerp-chain regions, plain tiling
  pattern UVs, keyword-driven emission, `_AlphaEx`); its alpha joins the colours' alphas as a weighted sum times
  `_Color`.a. Probe artefact: the first render of a keyword variant the replacement has not used yet can come out without
  that keyword's term (the `m_em` block's first `_EMISSION_ON` cell); the same case later in the run matches.
- Case files: `Tests/FitCases/{ms2_decode,ms2_decode2,ms2_decode3,ms2_decode4,ms2_uv,ms2_extra,ms2_real,ms2a_decode,
  ms2a_decode3,ist_decode2,ist_decode3,ist_cat,ist_real,glass_setcolor}.txt`; regression sets `ms2_fit.txt` (-fitItem
  id:400), `ms2a_fit.txt` (id:591), `ist_fit.txt` (id:776; also run with id:1026 for the rim), `m2_fit.txt` (id:161,
  id:256), `panel_fit.txt` (id:975); decode files `m2_decode.txt`, `panel_decode.txt`.

## Method notes from the Hanmen item shaders

- **A name is not a shader.** `Hanmen/Item Cutoff` exists as 10 different bundle copies (52-91 properties, three
  generations of the author's Amplify graph plus a 64-property stub), `Hanmen/Item Alpha` as 6. List the copies first
  (parsed shader objects: property list with flags / defaults, pass state, custom editor; UnityPy on the zipmod bundles,
  `Tools/survey/mod_shader_props.json` `copies`), count the materials per copy (the main copy carries about 90 %), fit that
  one, then probe each other copy with the same case file: here every other copy differed (0.43-0.47 RMS over `hmi_decode1`:
  lerp-chain regions and different channel packing in the 91-property copy of four road maps, no specular occlusion and
  other emission / translucency in the 52-68-property copies), so `ShaderMap.CopyAccepts` maps only the main copies (by
  property presence) and the rest keep the URP/Lit fallback (most of them are routed: next section). A check tool of the
  development tree showed per item which shader every slot ends on and ran the Studio colour writes on them.
- Decoded with one neutral base that sets every input (`gen_cases.py`-style generated case files) and one-channel masks:
  the regions are a weighted sum `c1 (1 - R - G - B) + c2 R + c3 G + c4 B`, not saturated (overlapping masks give negative
  weights) - the same rule for the colours, their alphas, metallic and glossiness; `_MetallicMask` / `_Roughness` only show
  with `MetallicGloss.B` / `.R` below 1 (lerp(1, map, mask)); the occlusion (G) also scales the direct specular.
- **N.V**: normals that a normal map turns away from the view shaded differently from Unity's BRDF1 (abs(N.V)) and from its
  `UNITY_HANDLE_CORRECTLY_NEGATIVE_NDOTV` shift; saturate(N.V) matched (0.0002, from 0.09-0.17), and it also fixed the back
  faces drawn without the normal flip. Found from real items only (silhouette rims under a back light, 0.003-0.005), then
  isolated with front-facing probes tilted 60-85 degrees and normal maps of x = -0.9 / +0.9 (only the sign facing away
  differed). Real items reveal what flat probes hide, again.
- Detail maps repeat whatever the texture's wrap mode (a clamped ramp read as `frac`): sample at `frac(t)` with the
  derivatives of `t`. Detail occlusion goes through smoothstep(0, 0.8, R) (fit on 5 points, residual 3e-7). The detail
  height normal is approximate (forward differences at 0.0027 as AIT/Item: 0.015-0.023 RMS on steep synthetic ramps; the
  replica clothes use 0.0125, which was worse here; a screen-space height-to-normal was far off).
- Probe artefact: the first render of a new keyword variant (`_TRANSMISSION_ON`) came out as garbage (1e33) in the URP
  probe, the same case later in the run matched (the transmission block is kept in `hmi_gaps.txt` for that reason).
- `Hanmen/Item Alpha` (car windows): the same surface blended SrcAlpha / OneMinusSrcAlpha with colour mask RGB and ZWrite
  Off, no direct translucency, no realtime shadows received (queue 3000); probed on the real window material over black and
  white backgrounds.
- Not reproduced (no material of the samples uses them): `_SPECULARSETUP_ON`, `_IRIDISCENT_ON`, `_DITHERBLUENOISE_ON`
  (compiled by the copy), the tearing threshold beyond 0.02 in Occlusion.B (`AlphaEx` < 1), the detail gloss / detail
  alpha-channel interplay (0.06-0.10 on those blocks), the back-face lighting without the normal flip under a bumped normal
  (0.007). Material usage over 3,330 sampled materials: MainTex, Cutoff (0.3), BumpMap, Color, Glossiness, Metallic,
  MetallicGlossMap, Roughness, a DetailGlossMap with scale 0 (its height normal is still on: DetailMask white, scale 1).
- Case files: `Tests/FitCases/hmi_{decode1,decode2,decode3,decode5,decode6,decode7,tear,real}.txt`; regression sets
  `hmi_fit.txt` (`-fitItem vivio_sekiro/vivio_sekiro/data_prefab_000.unity3d:Kusabimaru`, threshold 0.0005) and
  `hmi_alpha_fit.txt` (`-fitItem [vivio]grove_street/[vivio]grove_street/data_prefab_000.unity3d:savanna -fitMaterial
  window`); known gaps `hmi_gaps.txt`. Zipmod items load through plain `SkinFit.Jobs` (`SkinFit.LoadItem` falls back to
  the shipping zipmod); a job that rebuilds characters must not be mixed with `Inventory` in one session (it replaces the
  catalog whose preloaded bundle stays loaded, and every later load of that bundle fails).

## Bundle copies routed by ShaderMap.CopyRoutes

- **One name, several replacements.** `ShaderMap.CopyRoutes` maps an original name to an ordered list of (test on the loaded
  original, replacement); `ShaderMap.TryResolve(shader)` / `TryResolve(name, loadedOriginal)` tries those first, then
  `Fitted` with `CopyAccepts`. Every lookup of a replacement for a loaded material goes through `TryResolve` (map hooks,
  terrain, skybox, MaterialEditor, PlayerSmoke, the test fallbacks, SkinFit.Real); `IsReplacementName` answers "is this one
  of ours". A route's target can be another family's model (the older copies run the Hanmen clothes' v0.9 surface). Tests are property
  presence (`FindPropertyIndex`), the cheapest stable signature of a copy at run time; pick properties that no other copy
  of the name declares and check the list against every copy in `mod_shader_props.json` `copies`.
- **Compare copies with the other family's case file first.** The 52 / 54 / 58 / 68-property `Hanmen/Item Cutoff` copies
  matched `Hanmen/Clothes Cutoff v0.9` once its case file was renamed to their colour names (`hmi_v09eq.txt`, Built-in
  against Built-in): no new fit, an indirection of the colour names (`HS2_V09_BASE`, `HS2_V09_C1..C4`). Differences left:
  occlusion from G in the 54+ copies (0.15 on the synthetic occlusion block; real materials 0.0000), the 68-property
  copy's plain clip (0.19-0.27 on the alpha blocks) and weaker detail height (0.03), the 52-property copy's
  `_EMISSIONCOLOR2_ON` (one material). `GDC/Item Cutoff` and the 68-property gen-2 `Hanmen/Item Cutoff` copy share one
  property set and gave identical diff lists on `hmi_fit.txt`: one replacement for both.
- **Variants of one surface file.** The gen-2 and pattern copies are defines of `HS2NextHanmenItem.hlsl` (`HS2_HM_GEN2`,
  `HS2_HM_P91`): only the differing rules (region weights, channel packing, emission, translucency, alpha) are switched.
  The gen-2 detail height kept the main copy's 0.0027 forward difference: the replica's 0.0125 rule measured 0.149 on the height
  blocks against 0.0225.
- **Dither patterns from dumps.** The pattern copy's alpha clip was read off a 64 x 48 pixel dump of graded alphas (case
  `pd_alpha`, `-fitDump`): interleaved gradient noise `frac(52.9829189 frac(0.06711056 x + 0.00583715 y))` on integer
  pixels with y from the bottom matched all 3,072 pixels; the gen-2 copies use the 8x8 Bayer matrix (as the Hanmen clothes).
- **The ambient translucency is lit.** In the gen-2 and pattern copies the SH x `_TransAmbient` term is multiplied by the
  main light's colour, distance and shadow (`_TransShadow`), unlike the main copy's saturated ambient term; a probe with the
  light off isolates it.
- Regression sets: `hmi_fit.txt` (manifest lines for the main copy, the gen-2 copy, GDC/Item Cutoff and the pattern copy),
  `hmi_p91_fit.txt`, `hmi_v09c_fit.txt` (the 52-property copy on the v0.9 cases without tear / wet, which the copies lack).

## Method notes from the replica clothes and hooh shaders

- **Identify before fitting.** None of the 13 shaders is an alias or a copy of an AIT shader, although several reuse AIT
  property names: the parsed shader objects (property lists with defaults, pass state, custom editor ASEMaterialInspector)
  and one probe each gave five families of their own (Hanmen Replica, Hanmen Cutoff with v0.9, hooh ClotheReplica, hooh
  ColorableObjectReplica, hooh RMA). The hooh ClotheReplica family declares colours, patterns and emission that change
  nothing; a probe sweep over those properties settled it before any fit, and a Studio colour check on real items
  confirmed that the writes land but leave the items as they are, as in HS2.
- **Replica's normal: z replaced by 0.5.** Flat-probe normal cases and the detail normals fitted, but a tilted normal map
  shaded about twice as steep as Unity's. Scanning a debug gain on the tangent-space xy per tilt showed a constant z of
  0.5: `normalize(N.xy + g, 0.5)`. Probe normal maps over several tilts, not one.
- **Grayscale-bump detail normals** (Replica, Cutoff, v0.9): forward differences of the detail map's G at 0.0125 in
  detail texture space, x scale x mask x gain (Replica 0.5 / 1 for the two maps, Cutoff and v0.9 2 / 2). Sawtooth ramps
  pin the offset (the response is nonlinear in it), linear ramps the gain.
- **Weighted sums stay unclamped** (a white mask gives metallic 3 and a negative base weight; smoothness 1.2 shades like
  0.8). Clamping looked harmless on plain values and broke the overlapping-mask cases.
- **Dither from a full dump.** The Cutoff alpha is Amplify's 8x8 ordered dither (thresholds 1..64 / 64, y from the
  bottom), on whenever `_Cutoff` > 0. The 8x8 centre window of `-fitPattern` misled at first; a `-fitDump` of 64 alpha
  steps read off the matrix directly.
- **Debug globals persist.** `SkinFit` sets `-extra "_G_Name=v"` with `Shader.SetGlobalFloat`, which survives into later
  lines of the same `Jobs` session: set every debug global in every job line. `_G_Name` sets `_Name` (one underscore).
- **Copies and keywords.** `hooh/ClotheReplicaOpaque` exists as an item copy (`_GlossMetalMap`, `_SmoothnessMultiply`) and
  a clothes copy (`_DetailGlossMap`): one replacement declares the union, the defaults reproduce the plain rule. Keyword
  variants (`_EMISSIONCOLOR1..3_ON`, `_DETAILOCCLUSION_ON`, `_BLUENOISEDITHER_ON`, `_ENABLEALPHAMASK*_ON`, `_ISROUGHNESS1_ON`)
  are compiled by almost no bundle copy, so a probe on a copy without them shows no effect; only Replica's
  `_EMISSIONCOLOR1_ON` was found compiled (one copy) and fitted. `ShaderMap.CopyAccepts` keeps the 71-property clothing
  copy of True Cutoff and three ColorableObjectReplica copies with other inputs on the fallback.
- **Card parts as probe objects.** The clothes copies were probed and compared on mod clothing parts loaded as
  `-fitItem "<chara bundle>:<prefab>"` (bind pose, source textures, no compositing); body parts of the cards were not used.
- **RMA:** the occlusion is a product (B x strength, so strength 0 removes the ambient), not Unity's lerp; `[_CullMode]` is
  Unity's raw value whatever the inspector's labels say; `Clothing/RMA/Opqaue` declares it but culls Back. ColorableObject
  Replica mixes metal / gloss with the mask's R only (G and B leave them at region 1).
- Not reproduced (no sampled material uses them, or no copy compiles them): `_WeatheringAll` (Cutoff / v0.9),
  AlphaToMask beyond the clip (Replica), the keyword variants above, a grazing back-face highlight of the hooh shaders
  (0.006 at 120 degrees tilt). Not fitted: `Replica Pantyhose`, the 89-property shader-pack copy of `True Alpha`, the
  ROC copy of `True Cutoff`, the older 22-property `hooh/ColorableObjectReplica` copies, `hooh/StudioItemStandardCutout*`
  (one minimod of sexual items; 11 materials).
- **Bundle copies.** Most names hide several bundle copies of different generations: list them per copy (parsed shader
  objects of the item census and of the character parts the cards use) before choosing materials. `ShaderMap.CopyRoutes`
  sends each copy to its own replacement (`hooh/ClotheReplicaOpaque` with `_GlossMetalMap`, the 71-property True Cutoff
  copy, the 87-property True Alpha copy, the `_MetallicSmoothness1` ColorableObjectReplica copy); ModShaderTest's M2 compares
  a Fitted replacement with `mod_shader_props.json`'s main copy, so a copy-specific replacement goes through CopyRoutes.
- **A dither switch can gate more than the dither.** In GDC/Clothes True Cutoff `_Cutoff > 0` also switches the tearing
  test on: probe cases with `_Cutoff` 0.5 showed a tearing threshold that discarded 97 % of real jeans (Cutoff 0) until a
  `-fitPix` run on the real material isolated the occlusion map (`gdc_real.txt`, one line per input reset to neutral).
- **Alpha laws read off with one background.** With a white surface over a 0.2 / 0.4 / 0.8 background, alpha = (r - 0.2)
  / 0.76 per cell; sweeping MainTex.a against each alpha input gave the Pantyhose and True Alpha laws directly (smoothstep
  values 0.259 / 0.741, Unity's luminance weights 0.0397 / 0.458 / 0.0061).
- Case files: `Tests/FitCases/hcr_*.txt`, `hct_*.txt`, `hcv9_*.txt`, `hooh_*.txt`, `hooh_cor_*.txt`, `hooh_rma_*.txt`;
  regression sets `hcr_fit.txt`, `hct_fit.txt`, `hcv9_fit.txt`, `hooh_fit.txt`, `hooh_opaque_fit.txt`, `hooh_cor_fit.txt`,
  `hooh_rma_fit.txt` (run each against a bundle item of its shader with `SkinFit.Jobs`); copies: `hph_*`, `hv2_*`,
  `hv3_*`, `hooh_cor_ms1_fit.txt`, `gdc_real.txt`.

## Lightmap paths

Which replacements draw a lightmapped renderer, and how.

| Original | Replacement | Used by | Probe RMS (linear) | Real asset | Notes |
|---|---|---|---|---|---|
| the generated legacy family (every `Legacy Shaders/*` / `Mobile/*` row and `simple_two_side`) on lightmapped renderers | the same `HS2Next/<name>` shaders (`HS2Next/Compatible/simple_two_side`), now with a lightmap path | 2,548 lightmapped mod-map slots (before: URP/Lit copies) | `l_legacy_lm_fit.txt` 0.0000 over 225 values each (Diffuse, Bumped Specular, Transparent/Bumped Specular, Mobile/Diffuse) against the Editor's built-ins: plain, directional, tilted, bumped, specular, fade, shadow mask, subtractive | - | forward pass declares `LIGHTMAP_ON`, `DIRLIGHTMAP_COMBINED`, `LIGHTMAP_SHADOW_MIXING`, `SHADOWS_SHADOWMASK`: albedo × URP's `SampleLightmap` (uv1 × `unity_LightmapST`) replaces albedo × SH, `MixRealtimeAndBakedGI` for subtractive mode, the shadow mask in the main and additional lights. Measured quirk: a lightmapped alpha:fade row leaves a mixed-Shadowmask main light out entirely (URP marks it by `_MainLightOcclusionProbes`); the opaque rows keep it with the baked mask. Without the keywords the code is the earlier one |
| `Standard` family, transparent modes | `HS2Next/Standard` (`HS2NextStandard.hlsl`) | the bushitsu window / clock / cabinet glass (`_ALPHAPREMULTIPLY_ON`, 23 lightmapped slots) | `l_std_lm_fit.txt` 0.0000 over 204 values against the Editor's Standard (opaque, transparent, fade; plain, directional, shadow mask) | the bushitsu glass material on its bundle copy: equal except the variants that copy does not compile (fade, directional, shadow mask) and the play-mode environment (0.005) | the fade / transparent path already sampled the lightmap; fixed: Built-in keeps the **baked** shadow mask in transparent queues while dropping realtime shadows, so under `_RECEIVE_SHADOWS_OFF` `HS2Shade` now applies the mask alone (`HS2NextBRDF.hlsl`, lightmap variants only) |
| `ASESampleShaders/Translucency` | `HS2Next/Compatible/ASESampleShaders/Translucency` | the bushitsu curtains (6 lightmapped slots) and one classroom variant map (4); before: URP/Lit drew them untextured white (its albedo is `_Albedo`, not `_MainTex`) | `l_ase_decode.txt` 0.0000 over 642 values on the bundle material (play mode, `LightmapPaths.RunProbe`) | bushitsu capture, see below | Standard Specular surface (`HS2NextBRDF`) with smoothness 0 (the roughness-1 GGX lobe), specular grey `_Specular`, albedo `_Albedo` × `_Tint` (alpha unused), Built-in normal unpack, Cull Off without a back-face flip; Amplify translucency × `_Translucency`, colour `_Color0` × `_Depth`, direct / ambient (lightmap or SH) / scattering / distortion / shadow from the `_Trans*` floats; UV = uv0 × `_texcoord_ST` then each texture's `_ST`; lightmap path non-directional only (the bundle copy draws a directional lightmap as a plain one) |
| `Bunker/Rim` | `HS2Next/Compatible/Bunker/Rim` (generated row) | The Bunker (`GMCC.The Bunker`, 400 + slots of one item) | `l_bunker_fit.txt` 0.0000 over 237 values | the item 0.0000 (front / back, with shadows 0.0000) | Bumped Specular without `_Color` (`HS2_LEG_NOCOLOR`), the highlight 5 × BlinnPhong's (`HS2_LEG_SPECGAIN 5.0`), emission `_RimColor` × (1 − saturate(dot(normalize(viewDir), N)))^`_RimPower` with the view vector in the interpolated surface-shader tangent frame (`HS2_LEG_RIM`) |

- **Routing** (`ShaderMap.RouteLightmapped`): a slot on a lightmapped renderer keeps its replacement when it has a lightmap path
  (`SupportsLightmaps`: declares `LIGHTMAP_ON`) or draws unlit (`NeedsNoLightmap`: declares no `_ADDITIONAL_LIGHTS`, i.e. no
  lit forward pass: the unlit, particle and effect replacements, URP's Unlit; Built-in's unlit shaders ignore lightmaps). Only a
  lit replacement without a lightmap path still gets its own URP/Lit copy (AIT, hair, Hanmen / hooh families, water and
  vegetation approximations). Without this rule `HS2Next/Unlit/Transparent` on 817 lightmapped mod-map slots would be drawn lit by URP/Lit.
  `Editor/LightmapPaths.cs` M7 checks the rule and the legacy probes (`LightmapPaths.Run`, also called by ModShaderTest M7).
- **Coverage** (lightmapped mod-map slots on a replacement with a lightmap path or without lighting; `Tools/survey/
  mod_shader_census.tsv`, the Translucency slots from the map census): 92.1 % before → 94.8 % (legacy +2,548, unlit +817,
  curtains +10); the Standard aliases (`RC/*`, `GarFey/*`) bring it to 96 %.
- **Records** (no code): Bakery vertex lightmaps — the `BAKERY_VERTEXLM*` keywords sit on 4 bushitsu slots (and `…MASK` on
  13,530 Japanese Downtown slots), but no map ships vertex-lightmap data (`ftLightmapsStorage` vertex-colour meshes 0 on every
  map) and the bushitsu bundle copy of `Bakery/Standard` draws the same with any vertex colour and without those keywords
  (`l_bakery_decode.txt`): report only, the texture lightmap path of `HS2Next/Standard` applies. `DYNAMICLIGHTMAP_ON`: 38 mod
  map scenes carry Enlighten realtime-GI data and Unity 6 assigns realtime lightmap indices at load (`[hooh] Japanese
  Alleyway`: 52 of 52 renderers), but `GraphicsSettings` keeps no dynamic-lightmap variants in the player and no fitted shader
  declares the keyword: not declared. `LIGHTMAP_BICUBIC_SAMPLING`: off in the URP global settings
  (`m_UseBicubicLightmapSampling: 0`), Built-in 2018 sampled bilinearly: not declared. `_LIGHTMAPPING_DYNAMIC_LIGHTMAPS` (a
  Standard GUI leftover on 8 bushitsu materials) is not declared by the bundle copy, so `ShaderMap.Swap` drops it.
- **Not a legacy row**: `Ciconia Studio/Double Sided/Standard/Diffuse Bump` (176 item materials, 0 lightmapped, in none of the reference scenes) is
  a Shader Forge PBL shader, not a surface-shader BlinnPhong: Disney-style diffuse (×1.031 at 60°), a normalized highlight that
  grows non-linearly with `_SpecularIntensity` (energy conservation) and explodes at grazing angles for `_Glossiness` 0.75,
  `_NormalIntensity` lerps the normal, back faces flip the normal (`l_ciconia_decode.txt`); left on the URP/Lit fallback.
- **bushitsu** (`ambient_bushitsu_default`, HS2 99.3): HS2Next 129.0 → 127.35 with the curtain fit (mean |d| 30.37 → 28.73).
  Attribution with renderer groups switched off through `LightmapPaths.Serve` (`dev.mapRenderers`) and the Studio lights
  toggled: the curtains (23 % of the pixels) held 40 % of |d|, the room seen through the window glass (30 %) 37 %, the other
  Standard surfaces (47 %) 23 %. 77 % of the remaining |d| is explained per pixel by the Studio "全体光" directional: HS2
  shadows it (Soft) on the map, HS2Next cannot (URP shadows the main light only, here the map's own sun), so it lights the curtains, the room and the balcony floor through walls and railing — the implied HS2
  shadow factor is a clean shadow map (curtains 0.15, room 0.42, the railing's shadow pattern on the floor). The shader paths
  themselves (window glass, lightmapped Standard) probe equal to Built-in.
- Probe tools: SkinFit set keys `_LM`, `_LMDIR`, `_LMMASK`, `_LMSUB`, `_LMSUBCOL`, `_QUEUE` (a lightmapped probe quad; uv1 = uv0);
  `LightmapPaths.RunProbe` for originals that exist only in map bundles. Case files `Tests/FitCases/l_*.txt`.

## More mod shaders

Entries in a block of their own in `ShaderMap.Fitted`; the property lists of the
new replacements are the originals' main copies in `Tools/survey/mod_shader_props.json` (`ModShaderTest` M2). Probes: SkinFit,
Built-in vs the measurement URP asset, the item's own bundle copy as reference (`-fitItem`); "real" = `SkinFit.Real` on the whole
item (front / back light) and `-fitPix -fitMaterial` sweeps of the real textures on the probe quad.

| Original | Replacement | Used by | Probe RMS (linear) | Real asset mean \|d\| (linear) | Notes |
|---|---|---|---|---|---|
| `RMAShaderOpaque`, `RMAShaderCutout`, `RMAShaderCutoutAccessory` | `HS2Next/Compatible/RMAShaderOpaque`, `.../RMAShaderCutout`, `.../RMAShaderCutoutAccessory` (`HS2NextRma.hlsl`) | hooh prop packs (studio props, beach, niche stuff, autumn), Island / map items: 568 mod item materials, 562 flagged | `f3_rma_fit.txt` 0.0000 over 1068 values (Assist Monitor A; and the beach pack's 12-property copy, an unused float `_BumpMap`); `f3_rmac_fit.txt` 0.0000 over 195 | 0.0000 on 4 items (monitor, folding table, fan, volleyball); probe-quad reals 0.0000 on 5 materials; with shadows 0.0008 over all views (fan back view 0.057 on 7,215 px: cutout shadow edges) | Amplify, packed `_RMA`: smoothness saturate((1 − R) `_Glossiness`), metallic saturate(G `_Metallic`) (sliders 0..2), occlusion B (ambient only), A unused; emission `_EmissionTex` × `_EmissionColor` × `_EmissionStrength` × 10; Built-in normal unpack of `_NormalMap` without scale; uv0 × `_texcoord_ST`, then each texture's tiling; `_Color`.a has no effect (cutout on MainTex.a); the originals' "white" defaults of `_NormalMap` / `_EmissionTex` in the Cutout copy are kept (an unassigned white normal map faces no light, as in HS2) |
| `RMAShaderDither` | `HS2Next/Compatible/RMAShaderDither` | glass of the hooh compass / diving mask (3 materials) | `f3_rmad_fit.txt` 0.0000 over 249 values (black and white backgrounds) | 0.0000 (compass) | not a dither: SrcAlpha / OneMinusSrcAlpha, ZWrite Off, alpha MainTex.a × `_alpha`; Transparent queue, no realtime shadows received (by construction) |
| `RMAShaderOpaqueCullOff` | `HS2Next/Compatible/RMAShaderOpaqueCullOff` | beach parasol, Japan Festival stalls (33 materials), 3 Mas75 maps | `f3_rmaco_fit.txt` 0.0000 over 612 values | 0.0000 (parasol; stall material on the quad 0.0000); with shadows 0.0097 / 0.0018 front / back | other channel roles: smoothness saturate(G × `_Metallic`), metallic saturate(1 − R × `_Glossiness`), occlusion B; emission `_Emission` × `_EmissionColor` (no strength); Cull Off, back faces keep the front normal |
| `Custom/SurfaceShader` | `HS2Next/Compatible/Custom/SurfaceShader` (`HS2_RMA_CUSTOM`) | hooh architect utility (baseboards, columns, floors, wallpapers: 320 materials, all flagged), hooh gymnasium / mall maps | `f3_cus_fit.txt` 0.0000 over 1314 values (incl. quads tilted 25 / 50 / 70°) | 0.0000 on 2 items (baseboard, column); with shadows 0.0001 | world-space triplanar (Amplify's node: weights \|N\| / sum of the interpolated normal, planes zy / xz / xy signed by N, × `_TextureScale.xy` or `_DetailScale.xy`; zw, the mesh UVs and the textures' tiling are not used); albedo lerp(MainTex × Color, 1, DetailTex) (screen); RMA as above; normals per plane: the unpacked xy × scale added to the surface normal's in-plane components (the map's z unused), main and detail combined by Unity's BlendNormals in the surface tangent frame. No lightmap variant in the bundle copy |
| `RC/StandardCustomSmoothness` | `HS2Next/Standard` (alias) | strata facility props and maps (4 maps, 2,262 lightmapped slots) | `f3_std_alias_fit.txt` 0.0000 over 192 values on two materials (one with `_GlossMapScale` 1.33) | 0.0000-0.0001 (bench, supercomputer); with shadows 0.0027 / 0.0164 front / back on the bench | Standard's 27 properties with `_Glossiness` / `_GlossMapScale` as Float (no range: values above 1 behave as in Standard); the lightmap path of the Standard family |
| `RC/StandardTwoSided`, `GarFey/Standard(Two Side)` | `HS2Next/Standard(Two Sided)` (alias) | strata props (earth, flasks, grass), Garnet bedroom, 3 GMCC maps | 0.0007 over 192 values each (the grazing back-face highlight of the existing alias) | 0.0000-0.0001 (earth, grass, the bedroom's plants and gong); with shadows 0.0002-0.0004 | GarFey's additional-light pass culls back faces (Cull Back in FORWARD_DELTA): back faces get no point / spot light in the original; not reproduced (the probe rig has one directional light) |
| `Mochie/Standard` | `HS2Next/Compatible/Mochie/Standard` (approximation, `HS2NextStdLike.hlsl`) | the Xoa junk-yard pack (4 materials on 14 items; 3,600 slots in 5 of the reference scenes) | `f3_mochie_fit.txt` 0.0000 over 858 values (the Standard-workflow paths of the bundle copy) | 0.0000 on 3 items (tyres, scrap metal, junkyard 1); probe-quad reals 0.0000 on all 4 materials; with shadows 0.0007 | the bundle copy compiles `_METALLICGLOSSMAP` / `_SPECGLOSSMAP` / `_NORMALMAP` only: metallic map R × `_Metallic`, roughness map R × `_Glossiness` (or the sliders; `_MetallicMult` / `_RoughnessMult` have no effect), occlusion G by strength, saturation with weights (0.3, 0.59, 0.11), `_SpecularStrength` on the direct specular; `_Workflow` (packed maps), triplanar / stochastic sampling, parallax, detail blends, subsurface, SSR and emission are not compiled in the copy or not reproduced; blend / ZWrite / ZTest / cull from the material's floats |
| `UBER - Metallic Setup/ Core`, `UBER - Metallic Setup/2 Sided/ Core` | `HS2Next/Compatible/UBER - Metallic Setup/ Core`, `.../2 Sided/ Core` (approximations) | Xoa props (vehicles, daggers, animals, NPC figures, greenhouse): 1,112 mod item materials, in none of the reference scenes | not probed: the bundle copies draw a constant 0.040 under the Editor's Built-in in the probe rig (no usable black box) | - | Standard's metallic model on UBER's inputs: albedo MainTex × `_Color` [HDR], metallic map R × `_Metallic` [Gamma], smoothness map A (or albedo A with `_Smoothness_from_albedo_alpha`) × `_Glossiness`, occlusion G (or albedo A) by `_OcclusionStrength` (default 0), normal map × `_BumpScale` always, emission map × `_EmissionColor`; Cull Back / Off; parallax, wetness, snow, glitter, translucency, decals, tessellation not reproduced. The 206-property lists are complete (M2) |
| `CalmWater/Calm Water [DX11]`, `[DX11] [Double Sided]`, `[Double Sided]`, `[Single Sided]` | `HS2Next/Compatible/Water` (approximation, see "Map shaders") | the MakotoYuki90 water items (20 flagged slots in 4 of the reference scenes) and map pieces | not a fit | - | the water's generic inputs are filled from the original also for items now (`ShaderMap.ApplyUrp` calls `ShaderMap.WaterInputs` = `MapShaderHooks.WaterInputs` on a copy of the original); tint lerp(`_Color`, `_DepthColor`, 0.5), `_BumpMap` and its tiling, `_Smoothness` clamped. Studio colour writes to `_Color` have no effect on the replacement |

- **`Unreal/*`**: one generated shader per UE material with its textures in `Material_Texture2D_k`
  and `_MainTex` empty. `ShaderMap.Fallback` takes `ShaderMap.UnrealInputs` (read before the shader switch): normal = the first
  slot that is BC5 or named `*_N` / `*_Nrm` / `*_Normal` (not UE's `DefaultNormal`), albedo = the first named `*_D` /
  `*_BaseColor` / `*_BC` / `*_ALB` / `*_Albedo` / `*_Diffuse` / `*_Color`, else the first other sRGB texture whose last name token
  is not a placeholder, mask, packed or emission map; URP/Lit with smoothness 0.3, metallic 0, Cull Off. Survey of 505 Unreal
  materials in 6 bundles: 464 get an albedo (453 also a normal map), the other 38 have no colour texture (the UE grid, glass,
  fog, flat walls). `ModShaderTest` M6: the junk-yard pack 22 of 22. Real (`SkinFit.Real -realFallback`, 3 junk-yard items):
  mean |d| 0.018 over all views (front 0.018-0.117: the UE graphs' packed masks, tints and UV maths are not reproduced).
- **Coverage** (`ModShaderTest` M4 on `Tools/survey/mod_shader_census.tsv`, names mapped by `ShaderMap.Fitted`; before this group → after): mod item materials 79.2 → 80.5 % (81.8 % counting the Unreal heuristic), Studio-flagged mod
  slots 95.4 → 98.1 %, mod map active slots 70.1 → 71.0 % (94.2 % with the heuristic); the reference scenes: item slots 37.1 →
  54.6 % (100 % with the heuristic), flagged item slots 95.4 → 100 %, map slots 72.1 → 99.5 % with the heuristic.
- **Method notes**: the probe rig's quad is the only geometry in view, so a world-space mapping looks constant (a symmetric
  sawtooth averages to 0.5) and moving the quad (`_QUADOFF`) changes nothing; moving the camera (`_CAMPOS`) and tilting the
  quad separated the planes and the triplanar weights. A float property that shares a texture's name (`_BumpMap` in the beach
  copy of `RMAShaderOpaque`) marks a different bundle copy with the same behaviour. The UBER copies need another reference
  (a play-mode probe or HS2 captures) if they are ever fitted. Case files `Tests/FitCases/f3_*.txt` (decode, real, and the
  regression sets `f3_*_fit.txt` with `# fit:` manifest lines read by `ModShaderTest` M5).

## The substitute-shader tail

Entries in `ShaderMap.Fitted`'s blocks of aliases, the Standard-like router, the particle router and fits; the work
list was weighted by the reference scenes, then cards, then the install. `ModShaderTest` M2 (property lists; router parity), M8 (the route
of every bundle copy), M5 (`Tests/FitCases/s52_*_fit.txt`, Built-in against URP on each item's own bundle copy) and M9 (the
MaterialEditor parity below) check them. "Real" = `SkinFit.Real` on whole items, front / back light, mean |d| (linear).

| Original | Replacement | Used by | Probe RMS (linear) | Real mean \|d\| | Notes |
|---|---|---|---|---|---|
| `Disolve GDC` | `HS2Next/Compatible/Disolve GDC` (`HS2NextHooh.hlsl` `HS2_HOOH_GMM`, its 37 properties) | a diving-suit mod (2 cards) | probe 0.0003 (head set), 0.0084 (suit: one grazing back-face block, 0.057) | 0.0000 on both items; 0.0003 with shadows | the hooh replica surface with `_GlossMetalMap` (decoded with the Built-in probe: metal DetailMainTex.R + GMM.R, smoothness DetailMainTex.B, occlusion R, three normal maps; the colours and `_Emission` have no visible effect). The dissolve (a procedural noise against a gradient along `_DissolveDirection` between `_LowerLimit` / `_UpperLimit`, `_Float0` the amount, an edge of `_Color3` [HDR] x `_Edgethickness`) is not drawn: the materials of this install keep the surface whole (Real 0.0000) |
| `Hanmen/Item Transparent` | `HS2Next/Compatible/Hanmen/Item Transparent` (`HS2_HANMEN_V09` + `HS2_HANMEN_V09_BLEND`) | earrings (2 cards) | `s52_itrans_fit.txt` 0.0000 over 909 values | 0.0000 (two earrings) | the older Hanmen/Item Cutoff copies' v0.9 surface (49 of their inputs, the same names) blended SrcAlpha / OneMinusSrcAlpha, ZWrite Off, Cull Off; alpha MainTex.a x the regions' weighted colour alpha, not saturated; no dither, `_Cutoff` has no effect |
| `AIT/Clothes True Deferred` | `HS2Next/AIT Clothes True` (alias) | the game's own `chara/31` clothes (7 cards) | `s52_deferred_fit.txt` 0.0000 over 3,480 values (ct_fit.txt without its translucency cases) | 0.0000 (shoes, gloves) | Clothes True's 60 properties minus the 7 translucency / transmission ones; `ShaderMap.MissingInputs` sets `_Translucency` and `_TransmissionStrength` to 0 at the swap |
| `Particles/Additive_Custom`, `KriptoFX/RFX1/Particle` | `HS2Next/Legacy Shaders/Particles/Additive`, `HS2Next/Compatible/KriptoFX/ME/Particle` (aliases) | map candles; Aizen effects | RFX1 `s52_kfx_fit.txt` 0.0000 | - | the same property lists and pass state as the replacements' originals |
| `Hovl/Particles/Add_CenterGlow`, `Blend_CenterGlow` | stand-ins (`HS2NextFx.hlsl` `HS2_FX_HOVL`) | the diving-suit mod, Hovl effect items | 0.0000 / 0.0000 | - | rgb = tex x noise x `_Color` x vertex colour x `_Emission` x lerp(1, glow, `_Usecenterglow`), alpha = the alphas' product (x `_Opacity`, not saturated, in the blended one); the additive one premultiplies (blend One [`_Blend2`]); glow = `_Mask`.r x the particle's custom data (uv0.z, 0 on a mesh without it). Not drawn: the flow-map distortion, the depth fade |
| `Hovl/Particles/Blend_TwoSides` | stand-in (`HS2_FX_HOVL_TWOSIDES`) | the diving-suit mod | 0.0000 over 162 values | - | unlit, alpha 1: tex x face colour x vertex colour (rgb x a) x `_Emission`, front faces x lerp(1, `_FresnelColor` x `_FresnelEmission`, (1 - N.V)^`_Fresnel`); clipped at `_Cutoff` on lerp(1, `_Mask`.r x uv0.z, `_UseCustomData`) (with custom data on, a mesh without custom data is clipped, as in the original). Not reproduced: `_SeparateFresnel` (0 in every material of the install) |
| `FireWing/Wing_Main`, `Wing_Sub` | stand-ins (`HS2_FX_FIREWING_*`) | a wing accessory (1 card) | 0.0000 / 0.0000 | - | additive: Main 2 x `_TintColor` x `_SubTex` x `_Mask`.a x `_Transparency` x vertex colour (`_MainTex`, `_Noise` no visible effect); Sub 2 x `_MainTex` x `_TintColor` x `_Mask`.rgb x `_Transparency` x vertex colour |
| `GAPH ... Integrated Particle (ValueControl)` | stand-in (`HS2_FX_GAPH`) | Aizen effects pack 3 | 0.0000 | - | rgb = 0.75 x tex x vertex colour x `_TintColor` ([HDR], as stored) x `_AllColorFactor` x the three channel factors; alpha x `_AllColorFactor`; blend from `_BlendSrc` / `_BlendDst` |
| `Quantum Theory/Particles/HDR Alpha Blended` | generated legacy row with an [HDR] tint | Rome props fire | 0.0000 | - | the legacy alpha-blended particle |
| `KriptoFX/RFX4/Particle` | `HS2Next/Compatible/KriptoFX/ME/Particle` (router) | Aizen effects pack 2 | 0.0000 | - | Cull Off through `ShaderMap.MissingInputs` (`CullMode` 0); the flip-book `_Tiling` is not drawn |
| `Particles/Standard Surface` | `HS2Next/Particles/Standard Unlit` (router) | effect items, 4 maps | 0.0189 | - | Unity's lit particle surface drawn unlit (its lighting is not drawn) |
| Standard-like router: `Standard-DoubleSided`, `StandardDoublesided`, `standard_specular`, `Unity/Standard (Specular CullOff)`, `Bakery/Standard Specular`, `Tirgames/TirgamesStandard`, `Lux Standard (Metallic / Specular setup)`, `Standard Plus/Standard Plus Glass Full`, `DoubleSided/Standard/Standard DS (Specular setup)`, three Ciconia "Standard (Double Sided)/Specular Gloss" names | `HS2Next/Standard`, its specular, two-sided and flipped-back-face variants (new: `HS2Next/Compatible/Standard DS (Specular setup)(flipped back faces)`) | mod items and maps; Lux: 4 item slots of the reference scenes; Standard-DoubleSided and Standard Plus: cards | Std-DS 0.0007, Unity Spec CullOff 0.0007, Bakery 0.0000, DS Specular 0.0000, standard_specular 0.0081 (back faces on real textures), Lux Specular 0.0007, Lux Metallic 0.62 (the highlight at the material's high smoothness), Standard Plus 0.0286 | Std-DS, Bakery, standard_specular, Unity Spec CullOff 0.0000; DS Specular 0.0011; Lux 0.003-0.031; Standard Plus glasses 0.088 / 0.014; Ciconia 0.009-0.094 | the router picks the target by the inputs the original declares (metallic: `_Metallic`; specular: `_SpecColor` / `_SpecGlossMap`) and its cull: the original pass's, per name from the census (a loaded `Shader` exposes no pass render state), or, for Lux (`_Cull`) and Standard Plus (`_CullMode`), the material's value (0 = Off: the two-sided variant; `ShaderMap.ResolveFor`). An original without Standard's blend properties gets the keywords of its assigned maps and emission and alpha clipping if it is a cutout (`ShaderMap.StandardRouted`). Not drawn: what these shaders add to Standard (Lux wetness / snow / scattering and its own highlight, Tirgames colour masks, Ciconia detail layers / cubemaps, Standard Plus refraction and custom lightmaps). The Ciconia sweeps are not meaningful (the probe's base material copies the properties the originals lack as garbage); Real measures them |

- **Left on the fallback**: Ciconia's legacy-like "Double Sided/Standard/Diffuse Bump" and
  "Double Sided/Transparent/Diffuse Bump Cutout" (Real 0.71 / 0.94 when routed to Standard: their `_SpecColor` / `_SpecularIntensity`
  model is not Standard's), the 29-property `hooh/ClotheReplicaAlphaCullOff` copy (0.65 against its name's replacement: other
  detail-map rules), `hooh/ClotheReplicaSimple` (0.21 against `hooh/StudioItemStandard`), the 22-property `hooh/ColorableObjectReplica`
  copies and `hooh/ColorableObjectReplicaATest`, ROC's 81-property `Hanmen/Clothes True Cutoff` copy, `Alloy/*` (no Standard inputs),
  the map-only vegetation and environment packs, UBER copies without a reference, `Nature Shaders/Tree Leaves`, `Hanmen/Next-Gen *`
  (reported as `me.shaderUnavailable`), `Shader Forge/fx_wing_a`, single-card shaders (`sifx/misc/gemstone`, `D2 EnemyRace Shader 1.0.1`).
- **MaterialEditor parity.** A shader edit whose name neither the plugin itself nor a loaded zipmod
  manifest declares is skipped (the current shader kept, "me.shader" missing content), as in HS2. A swap to one of the plugin's
  own copies (`MaterialEditorApplier.PluginCopies`: the game shaders the plugin carries, with the material keywords each copy's
  variants declare) keeps only those keywords on the material: the copy has no variant for the others. The plugin's
  Shader Optimization side effect is not reproduced.
- `AIT/mnpb` maps to `HS2Next/Mosaic`; the name `HS2Next/AIT mnpb` is refused by `ShaderMap.Supply`.
