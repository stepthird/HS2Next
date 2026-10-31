// Name-based replacement of HS2's Built-in shaders for URP. Fitted replacements keep the
// original property names, so switching the shader keeps the material's values. Shaders
// without a fitted replacement fall back to URP/Lit with the common inputs copied.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next
{
    public static class ShaderMap
    {
        // original shader name -> replacement shader name (fitted, same property names)
        public static readonly Dictionary<string, string> Fitted = new Dictionary<string, string>
        {
            { "AIT/Clothes True", "HS2Next/AIT Clothes True" },
            { "AIT/Clothes", "HS2Next/AIT Clothes" },
            { "AIT/Clothes Alpha", "HS2Next/AIT Clothes Alpha" },
            { "AIT/Clothes Alpha True", "HS2Next/AIT Clothes Alpha True" },
            { "AIT/Clothes Alpha True BackCull", "HS2Next/AIT Clothes Alpha True BackCull" },
            { "AIT/Item", "HS2Next/AIT Item" },
            { "AIT/Item Meka studio", "HS2Next/AIT Item Meka studio" },
            { "AIT/main Item Glass", "HS2Next/AIT main Item Glass" },
            { "AIT/Item studio", "HS2Next/AIT Item studio" },
            { "AIT/main standard2 studio", "HS2Next/AIT main standard2 studio" },
            { "AIT/main standard2 studio alpha", "HS2Next/AIT main standard2 studio alpha" },
            { "AIT/main standard2", "HS2Next/AIT main standard2" },
            { "AIT/main texture studio unlit", "HS2Next/AIT main texture studio unlit" },
            { "Standard", "HS2Next/Standard" },
            { "Standard (Specular setup)", "HS2Next/Standard (Specular setup)" },
            { "Standard (Roughness setup)", "HS2Next/Standard (Roughness setup)" },
            { "Standard(Two Sided)", "HS2Next/Standard(Two Sided)" },
            // Mod copies of the Standard family and Unity built-ins with the same semantics (probed, Shaders/README.md).
            { "Standard Double Sided", "HS2Next/Standard(Two Sided)" },
            { "Azerilo/Double Sided Standard", "HS2Next/Standard(Two Sided)" },
            { "StandardDoubleSide", "HS2Next/Standard(Two Sided)" },
            { "Standard 2-Sided", "HS2Next/Standard(Two Sided)" },
            { "Standard(Two Side)", "HS2Next/Standard(Two Sided)" },
            { "DoubleSided/Standard/Standard DS", "HS2Next/Compatible/Standard DS (flipped back faces)" },
            { "Bakery/Standard", "HS2Next/Standard" },
            { "Autodesk Interactive", "HS2Next/Standard (Roughness setup)" },
            // Unity's legacy, mobile, unlit and legacy particle shaders (Tools/gen_legacy_shaders.py); the Unity 5 particle
            // names are the same shaders.
            { "Legacy Shaders/Diffuse", "HS2Next/Legacy Shaders/Diffuse" },
            { "Legacy Shaders/Specular", "HS2Next/Legacy Shaders/Specular" },
            { "Legacy Shaders/Bumped Diffuse", "HS2Next/Legacy Shaders/Bumped Diffuse" },
            { "Legacy Shaders/Bumped Specular", "HS2Next/Legacy Shaders/Bumped Specular" },
            { "Legacy Shaders/Transparent/Diffuse", "HS2Next/Legacy Shaders/Transparent/Diffuse" },
            { "Legacy Shaders/Transparent/Specular", "HS2Next/Legacy Shaders/Transparent/Specular" },
            { "Legacy Shaders/Transparent/Bumped Diffuse", "HS2Next/Legacy Shaders/Transparent/Bumped Diffuse" },
            { "Legacy Shaders/Transparent/Bumped Specular", "HS2Next/Legacy Shaders/Transparent/Bumped Specular" },
            { "Legacy Shaders/Transparent/Cutout/Diffuse", "HS2Next/Legacy Shaders/Transparent/Cutout/Diffuse" },
            { "Legacy Shaders/Transparent/Cutout/Specular", "HS2Next/Legacy Shaders/Transparent/Cutout/Specular" },
            { "Legacy Shaders/Transparent/Cutout/Bumped Diffuse", "HS2Next/Legacy Shaders/Transparent/Cutout/Bumped Diffuse" },
            { "Legacy Shaders/Transparent/Cutout/Bumped Specular", "HS2Next/Legacy Shaders/Transparent/Cutout/Bumped Specular" },
            { "Legacy Shaders/Self-Illumin/Diffuse", "HS2Next/Legacy Shaders/Self-Illumin/Diffuse" },
            { "Legacy Shaders/Self-Illumin/Bumped Specular", "HS2Next/Legacy Shaders/Self-Illumin/Bumped Specular" },
            { "Legacy Shaders/Reflective/Specular", "HS2Next/Legacy Shaders/Reflective/Specular" },
            { "Legacy Shaders/Reflective/Bumped Diffuse", "HS2Next/Legacy Shaders/Reflective/Bumped Diffuse" },
            { "Legacy Shaders/Reflective/Bumped Specular", "HS2Next/Legacy Shaders/Reflective/Bumped Specular" },
            { "Mobile/Diffuse", "HS2Next/Mobile/Diffuse" },
            { "Mobile/Bumped Diffuse", "HS2Next/Mobile/Bumped Diffuse" },
            { "Mobile/Bumped Specular", "HS2Next/Mobile/Bumped Specular" },
            { "Unlit/Color", "HS2Next/Unlit/Color" },
            { "Unlit/Texture", "HS2Next/Unlit/Texture" },
            { "Unlit/Transparent", "HS2Next/Unlit/Transparent" },
            { "Unlit/Transparent Cutout", "HS2Next/Unlit/Transparent Cutout" },
            { "Legacy Shaders/Particles/Additive", "HS2Next/Legacy Shaders/Particles/Additive" },
            { "Legacy Shaders/Particles/Alpha Blended", "HS2Next/Legacy Shaders/Particles/Alpha Blended" },
            { "Legacy Shaders/Particles/Alpha Blended Premultiply", "HS2Next/Legacy Shaders/Particles/Alpha Blended Premultiply" },
            { "Legacy Shaders/Particles/Additive (Soft)", "HS2Next/Legacy Shaders/Particles/Additive (Soft)" },
            { "Particles/Additive", "HS2Next/Legacy Shaders/Particles/Additive" },
            { "Particles/Alpha Blended", "HS2Next/Legacy Shaders/Particles/Alpha Blended" },
            { "Particles/Alpha Blended Premultiply", "HS2Next/Legacy Shaders/Particles/Alpha Blended Premultiply" },
            { "Particles/Additive (Soft)", "HS2Next/Legacy Shaders/Particles/Additive (Soft)" },
            { "Mobile/Particles/Additive", "HS2Next/Mobile/Particles/Additive" },
            { "Mobile/Particles/Alpha Blended", "HS2Next/Mobile/Particles/Alpha Blended" },
            // Effects/* particle shaders of vanilla Studio effects (campfires, muzzle flash, fog, dragon fire) and the Aizen
            // effect packs (probed against the bundle copies, Phase 3.1 F).
            { "Effects/ParticleAdd", "HS2Next/Effects/ParticleAdd" },
            { "Effects/ParticleAdd C", "HS2Next/Effects/ParticleAdd C" },
            { "Effects/AlphaBlended", "HS2Next/Effects/AlphaBlended" },
            { "Effects/FPS_Pack/AlphaBlended", "HS2Next/Compatible/Effects/FPS_Pack/AlphaBlended" },
            { "Effects/FPS_Pack/GlowAdditiveNoFade", "HS2Next/Compatible/Effects/FPS_Pack/GlowAdditiveNoFade" },
            { "AIT/Additive", "HS2Next/AIT Additive" },
            { "simple_two_side", "HS2Next/Compatible/simple_two_side" },
            { "Shader Forge/main_item_studio_add", "HS2Next/Shader Forge/main_item_studio_add" },
            // Gimmick icons (helpers, never captured): unlit straight-alpha texture x vertex colour (Phase 3.1 Q13).
            { "Sprites/Default", "HS2Next/Mobile/Particles/Alpha Blended" },
            { "AIT/Skin True", "HS2Next/AIT Skin True" },
            { "AIT/Skin True Face", "HS2Next/AIT Skin True Face" },
            { "AIT/Skin Translucency simple", "HS2Next/AIT Skin Translucency simple" },
            { "AIT/Eye Translucency", "HS2Next/AIT Eye Translucency" },
            { "AIT/eyelashes", "HS2Next/AIT eyelashes" },
            { "AIT/main eyeshadow lambert", "HS2Next/AIT main eyeshadow lambert" },
            { "AIT/main namida", "HS2Next/AIT main namida" },
            { "AIT/Liquid", "HS2Next/AIT Liquid" },
            { "AIT/mnpb", "HS2Next/Mosaic" },   // the mosaic of the genital-area parts (5.M; never remapped, see Supply)
            { "AIT/main Light", "HS2Next/AIT main Light" },
            { "Shader Forge/hair_07", "HS2Next/Shader Forge/hair_07" },
            { "Shader Forge/hair_08", "HS2Next/Shader Forge/hair_08" },
            { "Shader Forge/studio_hair_07", "HS2Next/Shader Forge/studio_hair_07" },

            // ---- maps (3.2 package B): the map shader families (Shaders/README.md "Map
            // shaders"). Package / built-in targets are in the players through Editor/MapShaderSet.cs.
            // SpeedTree 7 (M7): URP's own port, same property names and keywords as the 2018 shaders; billboards through
            // MapShaderHooks (BillboardAsset materials are not renderer slots).
            { "Nature/SpeedTree", "Universal Render Pipeline/Nature/SpeedTree7" },
            { "Nature/SpeedTree Billboard", "Universal Render Pipeline/Nature/SpeedTree7 Billboard" },
            // Water (M11): one approximate replacement; MapShaderHooks fills its inputs from the original's properties.
            { "Lux Water/WaterSurface", "HS2Next/Compatible/Water" },
            { "Lux Water/WaterSurface Tessellation", "HS2Next/Compatible/Water" },
            { "NatureManufacture Shaders/Water/Water River Mobile", "HS2Next/Compatible/Water" },
            { "NatureManufacture Shaders/Water/Water River Tesseled Vertex Color", "HS2Next/Compatible/Water" },
            { "NatureManufacture Shaders/Water/Water Swamp Tesseled Vertex Color Flow", "HS2Next/Compatible/Water" },
            { "NatureManufacture Shaders/Water/Water Swamp Offset Vertex Color Flow", "HS2Next/Compatible/Water" },
            { "NatureManufacture Shaders/Water/Water River", "HS2Next/Compatible/Water" },
            { "AQUAS/Desktop and Web/Double-Sided/Triple-Textured Bumpy", "HS2Next/Compatible/Water" },
            { "Water/Desktop and Web/Double-Sided/Triple-Textured Bumpy", "HS2Next/Compatible/Water" },
            { "ASESampleShaders/Water/SimpleWater", "HS2Next/Compatible/Water" },
            { "Ciconia Studio/Effects/Water And Caustic/Water/River (Real Reflection)", "HS2Next/Compatible/Water" },
            { "Ciconia Studio/Effects/Water And Caustic/Water/Fast", "HS2Next/Compatible/Water" },
            { "FX/Water", "HS2Next/Compatible/Water" },
            { "RealisticWater/WaterPC", "HS2Next/Compatible/Water" },
            { "BFW Shaders/03.Water/01.SimpleWater", "HS2Next/Compatible/Water" },
            { "DLNK/FX/DLNKWater01", "HS2Next/Compatible/Water" },
            { "Custom/SeansWater", "HS2Next/Compatible/Water" },
            { "NatureManufacture Shaders/Water/Water Particles Foam", "HS2Next/Compatible/Water Particles Foam" },
            // Mirrors (M10): planar reflections rendered by MapMirrors into _ReflectionTex.
            { "FX/MirrorReflection_Transparent", "HS2Next/Compatible/FX/MirrorReflection_Transparent" },
            { "FX/MirrorReflection", "HS2Next/Compatible/FX/MirrorReflection" },
            // KriptoFX (M8): particles with the material's own blend factors (the distortion: block "effects" below).
            { "KriptoFX/ME/Particle", "HS2Next/Compatible/KriptoFX/ME/Particle" },
            // The train crowds of maps 35 / 36 (fitted).
            { "Custom/Silhouette", "HS2Next/Custom/Silhouette" },
            // Two-sided copies of Standard in vanilla maps (23 / 25 properties, Cull Off).
            { "Standard_culloff", "HS2Next/Standard(Two Sided)" },
            { "Standard (Specular setup)_culloff", "HS2Next/Standard (Specular setup)(Two Sided)" },
            // Legacy / particle tails (Tools/gen_legacy_shaders.py) and effect shaders of vanilla maps (approximations).
            { "Legacy Shaders/Self-Illumin/Bumped Diffuse", "HS2Next/Legacy Shaders/Self-Illumin/Bumped Diffuse" },
            { "Legacy Shaders/Transparent/Cutout/Soft Edge Unlit", "HS2Next/Legacy Shaders/Transparent/Cutout/Soft Edge Unlit" },
            { "Transparent/Cutout/Soft Edge Unlit", "HS2Next/Legacy Shaders/Transparent/Cutout/Soft Edge Unlit" },
            { "Particles/Standard Unlit", "HS2Next/Particles/Standard Unlit" },
            { "Effects/Ice/IceFront", "HS2Next/Effects/Ice/IceFront" },
            { "Effects/SlimeCutOut", "HS2Next/Effects/SlimeCutOut" },
            { "Hidden/TerrainEngine/Splatmap/Specular-Base", "HS2Next/Legacy Shaders/Specular" },
            // Tree Creator (mod maps): legacy approximations (leaves: cutout bumped diffuse, bark: bumped specular).
            { "Hidden/Nature/Tree Creator Leaves Optimized", "HS2Next/Nature/Tree Creator Leaves" },
            { "Hidden/Nature/Tree Creator Leaves Fast Optimized", "HS2Next/Nature/Tree Creator Leaves" },
            { "Hidden/Nature/Tree Creator Bark Optimized", "HS2Next/Nature/Tree Creator Bark" },
            { "Nature/Tree Creator Leaves", "HS2Next/Nature/Tree Creator Leaves" },
            { "Nature/Tree Creator Leaves Fast", "HS2Next/Nature/Tree Creator Leaves" },
            { "Nature/Tree Creator Bark", "HS2Next/Nature/Tree Creator Bark" },
            // NatureManufacture vegetation (5-6 mod maps each; _MainTex / _BumpMap / _Cutoff as Unity's): two-sided cutout
            // leaves and grass, bumped diffuse bark (approximations: wind, colour noise, the packed metallic / AO maps ignored).
            { "NatureManufacture Shaders/Trees/Tree Leaves Metalic", "HS2Next/Compatible/simple_two_side" },
            { "NatureManufacture Shaders/Trees/Tree_Leaves_Specular", "HS2Next/Compatible/simple_two_side" },
            { "NatureManufacture Shaders/Trees/Cross Model Shader", "HS2Next/Compatible/simple_two_side" },
            { "NatureManufacture Shaders/Grass/Advanced Grass Specular", "HS2Next/Compatible/simple_two_side" },
            { "NatureManufacture Shaders/Trees/Tree Bark Metalic", "HS2Next/Legacy Shaders/Bumped Diffuse" },
            // Skyboxes (renderer slots and RenderSettings.skybox): Unity's own shaders of the same name.
            { "Skybox/Procedural", "Skybox/Procedural" },
            { "Skybox/Cubemap", "Skybox/Cubemap" },
            { "Skybox/Panoramic", "Skybox/Panoramic" },
            { "Skybox/6 Sided", "Skybox/6 Sided" },
            // ---- end of maps (3.2 package B)
            // ---- effects (3.3 package C): shaders that read the scene textures
            // KriptoFX's grab-pass distortion (vanilla effect items and map 0) from URP's opaque texture (approximation; M8).
            { "KriptoFX/ME/Distortion", "HS2Next/Compatible/KriptoFX/ME/Distortion" },
            // ---- end of effects (3.3 package C)
            // ---- 3.7 F2: the Hanmen "replica" clothes shaders and hooh/* (own Amplify families, not AIT copies; probed,
            // Shaders/README.md "Method notes from the replica clothes and hooh shaders")
            { "Hanmen/Clothes True Replica Opaque", "HS2Next/Compatible/Hanmen/Clothes True Replica Opaque" },
            { "Hanmen/Clothes True Cutoff", "HS2Next/Compatible/Hanmen/Clothes True Cutoff" },
            { "Hanmen/Clothes Cutoff v0.9", "HS2Next/Compatible/Hanmen/Clothes Cutoff v0.9" },
            { "hooh/StudioItemStandard", "HS2Next/Compatible/hooh/StudioItemStandard" },
            { "hooh/ClotheReplicaOpaque", "HS2Next/Compatible/hooh/ClotheReplicaOpaque" },
            { "hooh/ClotheReplicaAlpha", "HS2Next/Compatible/hooh/ClotheReplicaAlpha" },
            { "hooh/ClotheReplicaAlphaCullOff", "HS2Next/Compatible/hooh/ClotheReplicaAlphaCullOff" },
            { "hooh/ColorableObjectReplica", "HS2Next/Compatible/hooh/ColorableObjectReplica" },
            { "hooh/Accessory/RMA/Opqaue", "HS2Next/Compatible/hooh/Accessory/RMA/Opqaue" },
            { "hooh/Accessory/RMA/Cutout", "HS2Next/Compatible/hooh/Accessory/RMA/Cutout" },
            { "hooh/Clothing/RMA/Opqaue", "HS2Next/Compatible/hooh/Clothing/RMA/Opqaue" },
            { "hooh/Clothing/RMA/Transparent", "HS2Next/Compatible/hooh/Clothing/RMA/Transparent" },
            { "hooh/Clothing/RMA/MaskedTransparent", "HS2Next/Compatible/hooh/Clothing/RMA/MaskedTransparent" },
            // round 2 (F2r2): the blended and "v3" Hanmen clothes shaders (one bundle copy each)
            { "Hanmen/Clothes True Pantyhose", "HS2Next/Compatible/Hanmen/Clothes True Pantyhose" },
            { "Hanmen/Clothes True Transparent", "HS2Next/Compatible/Hanmen/Clothes True Transparent" },
            { "GDC/Clothes True Cutoff", "HS2Next/Compatible/GDC/Clothes True Cutoff" },
            // ---- end of 3.7 F2
            // ---- 3.7 F1: the Hanmen item shaders (own Amplify family; probed, Shaders/README.md "Method notes from the Hanmen
            // item shaders"). Several bundle copies share these names: only the main copy is mapped (CopyAccepts).
            { "Hanmen/Item Cutoff", "HS2Next/Compatible/Hanmen/Item Cutoff" },
            { "Hanmen/Item Alpha", "HS2Next/Compatible/Hanmen/Item Alpha" },
            // GDC/Item Cutoff (character accessories; one bundle copy) = the gen-2 Hanmen item model (3.7 F1r2)
            { "GDC/Item Cutoff", "HS2Next/Compatible/GDC/Item Cutoff" },
            // ---- end of 3.7 F1
            // ---- 3.7 L: mod shaders of the lightmap package (black-box probed; Shaders/README.md "Lightmap paths (Phase 3.7,
            // package L)"): every replacement here has a lightmap path or needs none.
            { "Bunker/Rim", "HS2Next/Compatible/Bunker/Rim" },
            { "ASESampleShaders/Translucency", "HS2Next/Compatible/ASESampleShaders/Translucency" },
            // ---- end of 3.7 L
            // ---- 3.7 F3: Standard aliases (probed), the RMA family, Mochie / UBER approximations and CalmWater (Shaders/README.md
            // "Mod shaders round 2 (Phase 3.7, package F3)"); Unreal/* materials take ShaderMap.UnrealInputs in Fallback.
            { "RC/StandardCustomSmoothness", "HS2Next/Standard" },
            { "RC/StandardTwoSided", "HS2Next/Standard(Two Sided)" },
            { "GarFey/Standard(Two Side)", "HS2Next/Standard(Two Sided)" },
            { "Custom/SurfaceShader", "HS2Next/Compatible/Custom/SurfaceShader" },
            { "RMAShaderOpaque", "HS2Next/Compatible/RMAShaderOpaque" },
            { "RMAShaderCutout", "HS2Next/Compatible/RMAShaderCutout" },
            { "RMAShaderCutoutAccessory", "HS2Next/Compatible/RMAShaderCutoutAccessory" },
            { "RMAShaderDither", "HS2Next/Compatible/RMAShaderDither" },
            { "RMAShaderOpaqueCullOff", "HS2Next/Compatible/RMAShaderOpaqueCullOff" },
            { "Mochie/Standard", "HS2Next/Compatible/Mochie/Standard" },
            { "UBER - Metallic Setup/ Core", "HS2Next/Compatible/UBER - Metallic Setup/ Core" },
            { "UBER - Metallic Setup/2 Sided/ Core", "HS2Next/Compatible/UBER - Metallic Setup/2 Sided/ Core" },
            // CalmWater (4 names, the water items of the user's scenes): the existing water approximation (inputs from the
            // original through MapShaderHooks.WaterInputs, also for items since 3.7 F3).
            { "CalmWater/Calm Water [DX11]", "HS2Next/Compatible/Water" },
            { "CalmWater/Calm Water [DX11] [Double Sided]", "HS2Next/Compatible/Water" },
            { "CalmWater/Calm Water [Double Sided]", "HS2Next/Compatible/Water" },
            { "CalmWater/Calm Water [Single Sided]", "HS2Next/Compatible/Water" },
            // ---- end of 3.7 F3
            // ---- 5.2 Sb: the Standard-like router (Shaders/README.md "The substitute-shader tail (Phase 5.2)"). Mod shaders that
            // declare Unity Standard's inputs take the Standard replacement of their workflow (metallic or specular, by the inputs
            // they declare) and of their cull (the original pass's, as the bundle's shader object states it; a cull taken from a
            // material property gives the two-sided variant when that property is Off: ResolveFor); CopyAccepts asks every
            // loaded copy for those inputs, and Swap completes what an original without Standard's own blend properties leaves
            // unset (StandardRouted). Approximations: what these shaders add to Standard (detail layers, colour masks, refraction,
            // snow, wetness) is not drawn.
            { "Standard-DoubleSided", "HS2Next/Standard(Two Sided)" },
            { "StandardDoublesided", "HS2Next/Standard(Two Sided)" },
            { "standard_specular", "HS2Next/Standard(Two Sided)" },
            { "Unity/Standard (Specular CullOff)", "HS2Next/Standard (Specular setup)(Two Sided)" },
            { "DoubleSided/Standard/Standard DS (Specular setup)", "HS2Next/Compatible/Standard DS (Specular setup)(flipped back faces)" },
            { "Lux Standard (Metallic setup)", "HS2Next/Standard" },
            { "Lux Standard (Specular setup)", "HS2Next/Standard (Specular setup)" },
            { "Tirgames/TirgamesStandard", "HS2Next/Standard" },
            { "Standard Plus/Standard Plus Glass Full", "HS2Next/Standard" },
            { "Bakery/Standard Specular", "HS2Next/Standard (Specular setup)" },
            { "Ciconia Studio/Standard (Double Sided)/Specular Gloss/Advanced (Both Sides)/Cutout", "HS2Next/Standard (Specular setup)(Two Sided)" },
            { "Ciconia Studio/Standard (Double Sided)/Specular Gloss/Advanced (Both Sides)/Opaque", "HS2Next/Standard (Specular setup)(Two Sided)" },
            { "Ciconia Studio/Standard (Double Sided)/Specular Gloss/Standard/Opaque", "HS2Next/Standard (Specular setup)(Two Sided)" },
            // ---- end of 5.2 Sb
            // ---- 5.2 Sc: the particle router. Particle and effect shaders take a stand-in generated by Tools/gen_legacy_shaders.py
            // (table ROUTED) with the original's property list and pass state (blend, cull, depth write, colour mask), whose
            // colour model is chosen by the original's blend and inputs (texture, tint, emission scale); Unity's lit particle
            // surface shader takes the unlit Standard particle replacement (lighting not drawn).
            { "Particles/Standard Surface", "HS2Next/Particles/Standard Unlit" },
            { "Hovl/Particles/Add_CenterGlow", "HS2Next/Compatible/Hovl/Particles/Add_CenterGlow" },
            { "Hovl/Particles/Blend_CenterGlow", "HS2Next/Compatible/Hovl/Particles/Blend_CenterGlow" },
            { "Hovl/Particles/Blend_TwoSides", "HS2Next/Compatible/Hovl/Particles/Blend_TwoSides" },
            { "FireWing/Wing_Main", "HS2Next/Compatible/FireWing/Wing_Main" },
            { "FireWing/Wing_Sub", "HS2Next/Compatible/FireWing/Wing_Sub" },
            // the RFX4 particle (KriptoFX/ME/Particle's model, Cull Off, a flip-book tiling not drawn)
            { "KriptoFX/RFX4/Particle", "HS2Next/Compatible/KriptoFX/ME/Particle" },
            { "GAPH Custom Shader/Integrated Particle/Integrated Particle (ValueControl)", "HS2Next/Compatible/GAPH Custom Shader/Integrated Particle/Integrated Particle (ValueControl)" },
            { "Quantum Theory/Particles/HDR Alpha Blended", "HS2Next/Compatible/Quantum Theory/Particles/HDR Alpha Blended" },
            // ---- end of 5.2 Sc
            // ---- 5.2 Sa: aliases: originals whose property list and pass state equal those of a replacement's original, or a
            // subset of it whose missing inputs MissingInputs sets to the value that switches their term off (probed)
            { "Particles/Additive_Custom", "HS2Next/Legacy Shaders/Particles/Additive" },
            { "KriptoFX/RFX1/Particle", "HS2Next/Compatible/KriptoFX/ME/Particle" },
            // the game's deferred-capable build of Clothes True (53 of its 60 properties: no translucency, no transmission)
            { "AIT/Clothes True Deferred", "HS2Next/AIT Clothes True" },
            // ---- end of 5.2 Sa
            // ---- 5.2 Sd: black-box fits (Shaders/README.md "The substitute-shader tail")
            { "Disolve GDC", "HS2Next/Compatible/Disolve GDC" },
            // the older Hanmen/Item Cutoff copies' v0.9 surface, blended (49 of their inputs)
            { "Hanmen/Item Transparent", "HS2Next/Compatible/Hanmen/Item Transparent" },
            // ---- end of 5.2 Sd
        };

        /// <summary>
        /// Originals mapped onto a replacement with inputs they do not declare (the Fitted blocks "5.2 Sa" and "5.2 Sc"): Swap
        /// sets each listed input the original lacks to the value that matches what the original draws (a term switched off,
        /// its fixed cull).
        /// </summary>
        public static readonly Dictionary<string, (string property, float value)[]> MissingInputs = new Dictionary<string, (string, float)[]>(StringComparer.Ordinal)
        {
            { "AIT/Clothes True Deferred", new[] { ("_Translucency", 0f), ("_TransmissionStrength", 0f) } },
            { "KriptoFX/RFX4/Particle", new[] { ("CullMode", 0f) } },
        };

        /// <summary>
        /// The originals of the Standard-like router (the Fitted block "5.2 Sb"). Such an original that declares no _SrcBlend
        /// (it has none of Standard's inspector state: no Standard keywords, no blend floats on its materials) gets on its
        /// Standard replacement what its own pass did with the same inputs: the keywords of the maps and the emission it has
        /// (_NORMALMAP, _SPECGLOSSMAP, _METALLICGLOSSMAP, _EMISSION) and alpha clipping (_ALPHATEST_ON) when the original is a
        /// cutout (it declares _Cutoff and is tagged TransparentCutout or sits in the alpha-test queues).
        /// </summary>
        public static readonly HashSet<string> StandardRouted = new HashSet<string>(StringComparer.Ordinal)
        {
            "Standard-DoubleSided", "StandardDoublesided", "standard_specular", "Unity/Standard (Specular CullOff)",
            "DoubleSided/Standard/Standard DS (Specular setup)", "Lux Standard (Metallic setup)", "Lux Standard (Specular setup)",
            "Tirgames/TirgamesStandard", "Standard Plus/Standard Plus Glass Full", "Bakery/Standard Specular",
            "Ciconia Studio/Standard (Double Sided)/Specular Gloss/Advanced (Both Sides)/Cutout",
            "Ciconia Studio/Standard (Double Sided)/Specular Gloss/Advanced (Both Sides)/Opaque",
            "Ciconia Studio/Standard (Double Sided)/Specular Gloss/Standard/Opaque",
        };

        /// <summary>
        /// Per-copy rules for original names that several different bundle copies share (W5):
        /// a name listed here maps to its <see cref="Fitted"/> replacement only when the loaded original passes the test (by
        /// property presence); other copies fall back to URP/Lit like an unmapped shader.
        /// </summary>
        public static readonly Dictionary<string, System.Func<Shader, bool>> CopyAccepts = new Dictionary<string, System.Func<Shader, bool>>
        {
            // ---- 3.7 F1: the 76-property main copy of Hanmen/Item Cutoff (about 90 % of its materials); the older copies (52-68
            // properties) and the 91-property pattern copy of the road maps shade differently (Shaders/README.md).
            { "Hanmen/Item Cutoff", s => Declares(s, "_TRANSMISSION") && Declares(s, "_Iridiscent") && Declares(s, "_DitherBluenoise") },
            // the 75-property main copy of Hanmen/Item Alpha (car windows); the other copies (54-91 properties) stay on the fallback
            { "Hanmen/Item Alpha", s => Declares(s, "_METALLICGLOSSMAP") && Declares(s, "_Iridiscent") && Declares(s, "_DitherBluenoise") },
            // ---- end of 3.7 F1
            // ---- 3.7 F2: the main copy of Hanmen/Clothes True Cutoff (64 properties; a 71-property clothing copy with _BodyColor
            // and _Wet* differs); hooh/ClotheReplicaAlphaCullOff with its _DetailMainTex1 (a 29-property copy lacks it)
            { "Hanmen/Clothes True Cutoff", s => Declares(s, "_WetnessMap") && Declares(s, "_NoiseTexture") },
            { "hooh/ClotheReplicaAlphaCullOff", s => Declares(s, "_DetailMainTex1") },
            // the main copy of hooh/ColorableObjectReplica (_MainTex, _MetallicSmoothness); copies with _Diffuse / _Metalic or
            // _MetallicSmoothness1 have other inputs
            { "hooh/ColorableObjectReplica", s => Declares(s, "_MetallicSmoothness") && Declares(s, "_Metallic2") && Declares(s, "_MainTex") },
            // ---- end of 3.7 F2
            // ---- 5.2 Sb: a copy takes the Standard-like router only when it declares the inputs its replacement reads: albedo
            // (_MainTex, _Color), smoothness (_Glossiness) and the workflow's own (_Metallic, or _SpecColor / _SpecGlossMap)
            { "Standard-DoubleSided", s => Declares(s, "_MainTex") && Declares(s, "_Color") && Declares(s, "_Glossiness") && Declares(s, "_Metallic") },
            { "StandardDoublesided", s => Declares(s, "_MainTex") && Declares(s, "_Color") && Declares(s, "_Glossiness") && Declares(s, "_Metallic") },
            { "standard_specular", s => Declares(s, "_MainTex") && Declares(s, "_Color") && Declares(s, "_Glossiness") && Declares(s, "_Metallic") },
            { "Unity/Standard (Specular CullOff)", s => Declares(s, "_MainTex") && Declares(s, "_Color") && Declares(s, "_Glossiness") && (Declares(s, "_SpecColor") || Declares(s, "_SpecGlossMap")) },
            { "DoubleSided/Standard/Standard DS (Specular setup)", s => Declares(s, "_MainTex") && Declares(s, "_Color") && Declares(s, "_Glossiness") && (Declares(s, "_SpecColor") || Declares(s, "_SpecGlossMap")) },
            { "Lux Standard (Metallic setup)", s => Declares(s, "_MainTex") && Declares(s, "_Color") && Declares(s, "_Glossiness") && Declares(s, "_Metallic") },
            { "Lux Standard (Specular setup)", s => Declares(s, "_MainTex") && Declares(s, "_Color") && Declares(s, "_Glossiness") && (Declares(s, "_SpecColor") || Declares(s, "_SpecGlossMap")) },
            { "Tirgames/TirgamesStandard", s => Declares(s, "_MainTex") && Declares(s, "_Color") && Declares(s, "_Glossiness") && Declares(s, "_Metallic") },
            { "Standard Plus/Standard Plus Glass Full", s => Declares(s, "_MainTex") && Declares(s, "_Color") && Declares(s, "_Glossiness") && Declares(s, "_Metallic") },
            { "Bakery/Standard Specular", s => Declares(s, "_MainTex") && Declares(s, "_Color") && Declares(s, "_Glossiness") && (Declares(s, "_SpecColor") || Declares(s, "_SpecGlossMap")) },
            { "Ciconia Studio/Standard (Double Sided)/Specular Gloss/Advanced (Both Sides)/Cutout", s => Declares(s, "_MainTex") && Declares(s, "_Color") && Declares(s, "_Glossiness") && (Declares(s, "_SpecColor") || Declares(s, "_SpecGlossMap")) },
            { "Ciconia Studio/Standard (Double Sided)/Specular Gloss/Advanced (Both Sides)/Opaque", s => Declares(s, "_MainTex") && Declares(s, "_Color") && Declares(s, "_Glossiness") && (Declares(s, "_SpecColor") || Declares(s, "_SpecGlossMap")) },
            { "Ciconia Studio/Standard (Double Sided)/Specular Gloss/Standard/Opaque", s => Declares(s, "_MainTex") && Declares(s, "_Color") && Declares(s, "_Glossiness") && (Declares(s, "_SpecColor") || Declares(s, "_SpecGlossMap")) },
            // ---- end of 5.2 Sb
        };

        /// <summary>
        /// One original name, several replacements by bundle copy: the routes of a name are tried
        /// in order before <see cref="Fitted"/> / <see cref="CopyAccepts"/>; the first whose test passes on the loaded original
        /// gives the replacement. A route's target may also be another name's replacement (an older copy that is an alias of
        /// another family).
        /// </summary>
        public static readonly Dictionary<string, List<(System.Func<Shader, bool> accepts, string replacement)>> CopyRoutes =
            new Dictionary<string, List<(System.Func<Shader, bool>, string)>>
        {
            // ---- 3.7 F1r2: the older Hanmen/Item Cutoff copies (52 / 54 / 58 / 68 properties: _BumpScale, _OcclusionStrength,
            // _EmissionColor1..3) are F2's Clothes Cutoff v0.9 model with other colour names (Shaders/README.md, F1 notes)
            { "Hanmen/Item Cutoff", new List<(System.Func<Shader, bool>, string)>
                {
                    (s => Declares(s, "_BumpScale") && Declares(s, "_OcclusionStrength") && Declares(s, "_EmissionColor1"), "HS2Next/Compatible/Hanmen/Item Cutoff (v0.9 copies)"),
                    // the 68-property gen-2 copy (Rose Historical swords, Ekan) = GDC/Item Cutoff's model and property set (the
                    // 67-property copy with _EmissionColor1 of a GDC bag differs and keeps the fallback)
                    (s => Declares(s, "_Float0") && Declares(s, "_EmissionColor2") && Declares(s, "_DitherBlueNoise") && !Declares(s, "_TRANSMISSION") && !Declares(s, "_EmissionColor1"), "HS2Next/Compatible/GDC/Item Cutoff"),
                    // the 91-property copy with Studio patterns (four road maps, rifle slings)
                    (s => Declares(s, "_SHADERTYPE_ITEM") && Declares(s, "_PatternMask1"), "HS2Next/Compatible/Hanmen/Item Cutoff (pattern copy)"),
                } },
            // ---- end of 3.7 F1r2
            // ---- 3.7 F2: hooh/ClotheReplicaOpaque copies with _GlossMetalMap (+ _SmoothnessMultiply; hooh clothes, shoes) add that
            // map to metal / smoothness; the main copy (_DetailGlossMap) keeps its Fitted replacement
            { "hooh/ClotheReplicaOpaque", new List<(System.Func<Shader, bool>, string)> {
                (s => Declares(s, "_GlossMetalMap"), "HS2Next/Compatible/hooh/ClotheReplicaOpaque (GlossMetalMap copy)") } },
            // the 71-property Hanmen/Clothes True Cutoff copy ("v2": _CullMode, detail scales, no translucency); the 81-property
            // ROC copy (_Translucency) shades differently and keeps the fallback
            { "Hanmen/Clothes True Cutoff", new List<(System.Func<Shader, bool>, string)> {
                (s => Declares(s, "_CullMode") && Declares(s, "_DetailMetallicScale") && !Declares(s, "_Translucency"), "HS2Next/Compatible/Hanmen/Clothes True Cutoff (v2 copy)") } },
            // the 87-property Hanmen/Clothes True Alpha copy (the user's watch glass; blend properties, alpha fresnel, no
            // _NormalBackDirInvert); the 89-property copy of the shader pack (mod_shader_props.json's main copy) is not fitted
            { "Hanmen/Clothes True Alpha", new List<(System.Func<Shader, bool>, string)> {
                (s => Declares(s, "_BlendSCR") && Declares(s, "_AlphaMaster") && !Declares(s, "_NormalBackDirInvert"), "HS2Next/Compatible/Hanmen/Clothes True Alpha (v2 copy)") } },
            // the hooh/ColorableObjectReplica copy whose packed map is _MetallicSmoothness1 (whinny tools)
            { "hooh/ColorableObjectReplica", new List<(System.Func<Shader, bool>, string)> {
                (s => Declares(s, "_MetallicSmoothness1") && Declares(s, "_Metallic2"), "HS2Next/Compatible/hooh/ColorableObjectReplica (MetallicSmoothness1 copy)") } },
            // ---- end of 3.7 F2
            // ---- 5.2 S: the hooh/ClotheReplicaAlphaCullOff copy without _DetailMainTex1 (29 properties: the plain replica metal /
            // smoothness rule, probed)
            { "hooh/ClotheReplicaAlphaCullOff", new List<(System.Func<Shader, bool>, string)> {
                (s => !Declares(s, "_DetailMainTex1") && Declares(s, "_DetailMainTex"), "HS2Next/Compatible/hooh/ClotheReplicaAlphaCullOff (29-property copy)") } },
            // ---- end of 5.2 S
        };

        static bool Declares(Shader s, string property) => s.FindPropertyIndex(property) >= 0;

        /// <summary>The beginning of the names of HS2Next's compatible shaders.</summary>
        public const string CompatiblePrefix = "HS2Next/Compatible/";

        // The families of the compatible shaders: the label as THIRD_PARTY.md names it, then the names after CompatiblePrefix that
        // belong to it (a trailing '*' matches any ending). ModShaderTest checks that every compatible shader belongs to exactly one.
        static readonly (string label, string[] names)[] CompatibleFamilyNames =
        {
            ("Amplify Shader Editor samples", new[] { "ASESampleShaders/*" }),
            ("FPS Pack", new[] { "Effects/FPS_Pack/*" }),
            ("FireWing", new[] { "FireWing/*" }),
            ("GAPH", new[] { "GAPH Custom Shader/*" }),
            ("GDC", new[] { "GDC/*", "Disolve GDC" }),
            ("GMCC", new[] { "Bunker/*" }),
            ("Hanmen", new[] { "Hanmen/*" }),
            ("Hovl", new[] { "Hovl/*" }),
            ("KriptoFX", new[] { "KriptoFX/*" }),
            ("Mochie", new[] { "Mochie/*" }),
            ("NatureManufacture", new[] { "Water Particles Foam" }),
            ("Quantum Theory", new[] { "Quantum Theory/*" }),
            ("Standard DS", new[] { "Standard DS *" }),
            ("UBER", new[] { "UBER - Metallic Setup/*" }),
            ("Unify wiki mirror", new[] { "FX/MirrorReflection*" }),
            ("Water assets", new[] { "Water" }),
            ("hooh", new[] { "hooh/*", "RMAShader*", "Custom/SurfaceShader" }),
            ("simple_two_side", new[] { "simple_two_side" }),
        };

        /// <summary>The labels of the compatible-shader families whose names match <paramref name="shaderName"/> (empty for a
        /// name that does not begin with <see cref="CompatiblePrefix"/>).</summary>
        public static List<string> CompatibleFamiliesOf(string shaderName)
        {
            var labels = new List<string>();
            if (shaderName == null || !shaderName.StartsWith(CompatiblePrefix, StringComparison.Ordinal)) return labels;
            string rest = shaderName.Substring(CompatiblePrefix.Length);
            foreach (var (label, names) in CompatibleFamilyNames)
                if (names.Any(n => n.EndsWith("*", StringComparison.Ordinal) ? rest.StartsWith(n.Substring(0, n.Length - 1), StringComparison.Ordinal) : rest == n))
                    labels.Add(label);
            return labels;
        }

        /// <summary>
        /// The families of HS2Next's compatible shaders: the replacements for the shaders of mods and third-party asset packs,
        /// named HS2Next/Compatible/…, written from measurements of rendered output (not the original authors' work). Each
        /// family's label with the number of its compatible shaders among the replacement targets of <see cref="Fitted"/> and
        /// <see cref="CopyRoutes"/> as built in (before any plugin supply), ordinal by label.
        /// </summary>
        public static IReadOnlyList<KeyValuePair<string, int>> CompatibleFamilies { get; } = CountCompatibleFamilies();

        static IReadOnlyList<KeyValuePair<string, int>> CountCompatibleFamilies()
        {
            var targets = new HashSet<string>(Fitted.Values, StringComparer.Ordinal);
            foreach (var routes in CopyRoutes.Values)
                foreach (var (_, replacement) in routes) targets.Add(replacement);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (label, _) in CompatibleFamilyNames) counts[label] = 0;
            foreach (var t in targets)
                foreach (var label in CompatibleFamiliesOf(t)) counts[label]++;
            return counts.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList().AsReadOnly();
        }

        // The two-sided variant of a Standard-like router target, for the originals that take their cull from a material
        // property (Lux Standard's _Cull, Standard Plus's _CullMode).
        static readonly Dictionary<string, string> TwoSidedOf = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "HS2Next/Standard", "HS2Next/Standard(Two Sided)" },
            { "HS2Next/Standard (Specular setup)", "HS2Next/Standard (Specular setup)(Two Sided)" },
        };

        /// <summary>The replacement name for a loaded material: <see cref="TryResolve(Shader, out string)"/> of its shader; for a
        /// Standard-like router original whose cull is a material property (_Cull or _CullMode) set to 0 (Cull Off), the
        /// two-sided variant of the target. Null when there is none.</summary>
        public static string ResolveFor(Material m)
        {
            if (!m || !m.shader || !TryResolve(m.shader, out var r)) return null;
            if (StandardRouted.Contains(m.shader.name) && TwoSidedOf.TryGetValue(r, out var two))
                foreach (var p in CullProperties)
                    if (Declares(m.shader, p) && m.HasProperty(p) && m.GetFloat(p) < 0.5f) return two;
            return r;
        }

        static readonly string[] CullProperties = { "_Cull", "_CullMode" };

        /// <summary>The replacement name for a loaded original: <see cref="CopyRoutes"/>, then <see cref="Fitted"/> subject to
        /// <see cref="CopyAccepts"/>. Every lookup of a replacement for a loaded material goes through here.</summary>
        public static bool TryResolve(Shader original, out string replacement) => TryResolve(original ? original.name : null, original, out replacement);

        /// <summary>As <see cref="TryResolve(Shader, out string)"/> for callers that know the name and maybe the loaded original
        /// (MaterialEditor shader names): without an original only the <see cref="Fitted"/> entry (the main copy's replacement)
        /// can be given.</summary>
        public static bool TryResolve(string name, Shader original, out string replacement)
        {
            replacement = null;
            if (string.IsNullOrEmpty(name)) return false;
            if (original && CopyRoutes.TryGetValue(name, out var routes))
                foreach (var (accepts, target) in routes)
                    if (accepts(original)) { replacement = target; return true; }
            if (!Fitted.TryGetValue(name, out var r)) return false;
            if (original && CopyAccepts.TryGetValue(name, out var acc) && !acc(original)) return false;
            replacement = r;
            return true;
        }

        /// <summary>True when <paramref name="shaderName"/> is a replacement target of <see cref="Fitted"/> or <see cref="CopyRoutes"/>.</summary>
        public static bool IsReplacementName(string shaderName) =>
            Fitted.ContainsValue(shaderName) || CopyRoutes.Values.Any(l => l.Any(x => x.replacement == shaderName));

        /// <summary>
        /// Shader supply: maps a game shader name to a URP replacement shader name for every later load (Core callers; it
        /// overwrites the mapping and keeps no record — plugins go through <see cref="SupplyOwned"/>). Refused (false,
        /// <paramref name="refusal"/> says why) for the mosaic: HS2's AIT/mnpb and the mosaic shaders are never remapped and
        /// never a supplied replacement.
        /// </summary>
        public static bool Supply(string original, string replacement, out string refusal)
        {
            if (!SupplyAllowed(original, replacement, out refusal)) return false;
            Fitted[original] = replacement;
            return true;
        }

        static bool SupplyAllowed(string original, string replacement, out string refusal)
        {
            refusal = null;
            if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(replacement)) { refusal = "original and replacement names are required"; return false; }
            if (HS2Next.Chara.Mosaic.MosaicSet.IsMosaicShaderName(original) || HS2Next.Chara.Mosaic.MosaicSet.IsMosaicShaderName(replacement))
            {
                refusal = HS2Next.Chara.Mosaic.MosaicSet.ProtectedCode + ": the mosaic shader is not remapped";
                return false;
            }
            return true;
        }

        // Plugin supplies: per original, the owners' entries in supply order (the last one is in Fitted) and the Fitted value
        // before the first of them (or its absence), so a withdrawal restores what was mapped below it.
        sealed class SupplyStack
        {
            public bool HadBase;
            public string Base;
            public readonly List<(string owner, string replacement)> Entries = new List<(string, string)>();
        }

        static readonly Dictionary<string, SupplyStack> supplies = new Dictionary<string, SupplyStack>(System.StringComparer.Ordinal);

        /// <summary>
        /// A supply on behalf of a plugin (<paramref name="owner"/> = its GUID): the same refusals as <see cref="Supply"/>; the
        /// supply is put on top of the original's stack (an earlier supply of the same owner moves to the top), so a second
        /// plugin supplying the same original wins, and <see cref="Withdraw"/> of the top one restores the one below it, or the
        /// mapping from before the first supply.
        /// </summary>
        public static bool SupplyOwned(string owner, string original, string replacement, out string refusal)
        {
            if (!SupplyAllowed(original, replacement, out refusal)) return false;
            if (string.IsNullOrEmpty(owner)) { refusal = "an owned supply needs its owner"; return false; }
            if (!supplies.TryGetValue(original, out var st))
            {
                st = new SupplyStack { HadBase = Fitted.TryGetValue(original, out var b), Base = b };
                supplies[original] = st;
            }
            st.Entries.RemoveAll(e => e.owner == owner);
            st.Entries.Add((owner, replacement));
            Fitted[original] = replacement;
            return true;
        }

        /// <summary>Removes <paramref name="owner"/>'s supply of <paramref name="original"/>: the mapping becomes the next supply
        /// below (or what was mapped before the first supply; nothing when there was nothing). False when it had none.</summary>
        public static bool Withdraw(string owner, string original)
        {
            if (owner == null || original == null || !supplies.TryGetValue(original, out var st)) return false;
            int i = st.Entries.FindLastIndex(e => e.owner == owner);
            if (i < 0) return false;
            bool top = i == st.Entries.Count - 1;
            st.Entries.RemoveAt(i);
            if (st.Entries.Count > 0)
            {
                if (top) Fitted[original] = st.Entries[st.Entries.Count - 1].replacement;
                return true;
            }
            if (st.HadBase) Fitted[original] = st.Base;
            else Fitted.Remove(original);
            supplies.Remove(original);
            return true;
        }

        /// <summary>Withdraws every supply of <paramref name="owner"/> (a plugin's unload); returns how many.</summary>
        public static int WithdrawAll(string owner)
        {
            int n = 0;
            foreach (var kv in SuppliedBy(owner)) if (Withdraw(owner, kv.Key)) n++;
            return n;
        }

        /// <summary>The supplies of <paramref name="owner"/> still in place (original, replacement), originals in ordinal order.</summary>
        public static List<KeyValuePair<string, string>> SuppliedBy(string owner)
        {
            var l = new List<KeyValuePair<string, string>>();
            foreach (var kv in supplies)
                foreach (var e in kv.Value.Entries)
                    if (e.owner == owner) l.Add(new KeyValuePair<string, string>(kv.Key, e.replacement));
            l.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return l;
        }

        /// <summary>
        /// Maps every material under <paramref name="root"/> to its URP replacement. <paramref name="copy"/> true (default):
        /// the renderers get copies first (bundle materials are shared); false: the materials already are the caller's own
        /// (Studio items' per-slot copies) and are swapped in place. Also gives every
        /// renderer under the root its rendering layers (RenderingLayers.ApplyToRenderers: 1 &lt;&lt; layer, Phase 3.2).
        /// </summary>
        public static int ApplyUrp(GameObject root, bool copy = true)
        {
            int n = 0;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var done = new HashSet<Material>();
            if (copy)
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    // Work on copies: bundle materials are shared by every character using the asset (MaterialCopies: the
                    // copies keep the loaded keywords).
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] && !done.Contains(mats[i])) { mats[i] = MaterialCopies.Copy(mats[i]); done.Add(mats[i]); }
                    r.sharedMaterials = mats;
                }
                done.Clear();
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (!m || !m.shader || !done.Add(m)) continue;
                    if (ResolveFor(m) is string rn && Shader.Find(rn) is Shader s)
                    {
                        // 3.7 F3: HS2Next/Compatible/Water takes generic inputs filled from the original's properties (WaterInputs, set by
                        // Scenes.Runtime.MapShaderHooks at start-up); maps fill them again in their post-load step, items only here.
                        var src = WaterInputs != null && rn == WaterShader ? MaterialCopies.Copy(m) : null;
                        Swap(m, s); n++;
                        if (src) { WaterInputs(m, src); DestroyCopy(src); }
                        if (rn == HS2Next.Chara.Mosaic.MosaicSet.ShaderName) HS2Next.Chara.Mosaic.MosaicSet.Track(r);   // per-camera cell, wide mosaic
                        continue;
                    }
                    if (m.shader.name.StartsWith("Universal Render Pipeline/") || m.shader.name.StartsWith("HS2Next/")) continue;
                    bool broken = m.shader.name == EngineErrorShader;
                    Fallback(m, lit); n++;
                    if (broken) { brokenInBundle.Remove(m); brokenInBundle.Add(m, BrokenMark); }
                }
            RenderingLayers.ApplyToRenderers(root);
            return n;
        }

        /// <summary>The shader the engine gives a material whose own shader could not be loaded (and, in a player, the default
        /// material a renderer slot without a material is filled with).</summary>
        public const string EngineErrorShader = "Hidden/InternalErrorShader";

        // Materials that arrived with the engine's error shader and were switched to the URP/Lit fallback by ApplyUrp (weak keys).
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Material, object> brokenInBundle = new System.Runtime.CompilerServices.ConditionalWeakTable<Material, object>();
        static readonly object BrokenMark = new object();

        /// <summary>True when <paramref name="m"/> arrived with the engine's error shader (its own shader did not load, or an empty
        /// slot was filled with the error material) and ApplyUrp drew it with the URP/Lit fallback; finding shader.brokenInBundle.</summary>
        public static bool IsBrokenInBundle(Material m) => m && brokenInBundle.TryGetValue(m, out _) && IsFallback(m);

        /// <summary>
        /// True when <paramref name="s"/> has a lightmap path (declares LIGHTMAP_ON): the fitted Standard family
        /// (HS2NextStandard.hlsl) and URP's own lit shaders; the other fitted shaders take SH / probes only.
        /// </summary>
        public static bool SupportsLightmaps(Shader s) => s && s.keywordSpace.FindKeyword("LIGHTMAP_ON").isValid;

        /// <summary>
        /// True when <paramref name="s"/> draws without lighting: it declares no additional-light keyword (every lit forward
        /// pass of the fitted shaders and URP's lit shaders declares _ADDITIONAL_LIGHTS; the unlit, particle and effect
        /// replacements and URP's Unlit do not). Built-in's unlit shaders ignore lightmaps, so such a shader needs no lightmap
        /// path and keeps its slot on a lightmapped renderer (Phase 3.7 package L; before, RouteLightmapped drew them lit with
        /// URP/Lit).
        /// </summary>
        public static bool NeedsNoLightmap(Shader s) => s && !s.keywordSpace.FindKeyword("_ADDITIONAL_LIGHTS").isValid;

        /// <summary>True when the renderer samples a baked lightmap (lightmapIndex 0..65533 and that lightmap exists).</summary>
        public static bool IsLightmapped(Renderer r) =>
            r && r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE && r.lightmapIndex < LightmapSettings.lightmaps.Length;

        /// <summary>
        /// The lightmap routing rule (Phase 3.2): on a lightmapped renderer a slot keeps
        /// its mapped shader when that shader has a lightmap path (<see cref="SupportsLightmaps"/>) or draws unlit
        /// (<see cref="NeedsNoLightmap"/>, Phase 3.7 package L); the other slots get
        /// their own URP/Lit copy (Fallback rules, from the mapped material), so non-lightmapped renderers that share the
        /// material keep the fitted shader. Call after ApplyUrp and after the renderer's lightmap index is final (map load,
        /// LightmapPrefab / Bakery data). Returns the slots moved to URP/Lit (0 for a renderer without a lightmap).
        /// </summary>
        public static int RouteLightmapped(Renderer r) => RouteLightmapped(r, null);

        /// <summary>
        /// <see cref="RouteLightmapped(Renderer)"/> with the slots' original (bundle) shaders known (<paramref name="originalOf"/>,
        /// null when unknown): a slot whose original's bundle copy compiles no LIGHTMAP_ON variant also keeps its replacement.
        /// Built-in draws such a slot on a lightmapped renderer without the lightmap and without SH (direct light only), and so
        /// does a replacement without a lightmap path in URP (both measured equal, Phase 3.7 package L, l_nolmvariant*.txt);
        /// a URP/Lit copy would add the lightmap HS2 never shows (F3: Custom/SurfaceShader, RMAShader*, Mochie, UBER copies).
        /// </summary>
        public static int RouteLightmapped(Renderer r, System.Func<Material, Shader> originalOf)
        {
            if (!IsLightmapped(r)) return 0;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (!lit) return 0;
            var mats = r.sharedMaterials;
            int n = 0;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (!m || !m.shader || SupportsLightmaps(m.shader) || NeedsNoLightmap(m.shader)) continue;
                if (originalOf != null && originalOf(m) is Shader o && o && !o.keywordSpace.FindKeyword("LIGHTMAP_ON").isValid) continue;
                var c = MaterialCopies.Copy(m);
                Fallback(c, lit);
                mats[i] = c;
                n++;
            }
            if (n > 0) r.sharedMaterials = mats;
            return n;
        }

        // Switch a loaded material to its replacement: keep the original's effective render queue, and drop the
        // material keywords the original's (bundle) copy does not declare. Bundle copies are compiled with only the
        // variants their materials used, so such keywords have no effect in the game. The other keywords are kept as they
        // are (MaterialCopies.SetShader: the Editor's property drawers of the replacement do not switch any).
        public static void Swap(Material m, Shader replacement)
        {
            var orig = m.shader;
            int q = m.renderQueue;
            var drop = new List<string>();
            foreach (var k in m.shaderKeywords)
                if (!orig.keywordSpace.FindKeyword(k).isValid) drop.Add(k);
            bool mapped = ResolveFor(m) == replacement.name;
            bool routed = mapped && StandardRouted.Contains(orig.name) && orig.FindPropertyIndex("_SrcBlend") < 0;
            bool cutout = routed && IsCutout(orig, q);
            var off = mapped && MissingInputs.TryGetValue(orig.name, out var a) ? a.Where(p => orig.FindPropertyIndex(p.property) < 0).ToArray() : null;
            MaterialCopies.SetShader(m, replacement);
            if (off != null) foreach (var (p, v) in off) if (m.HasProperty(p)) m.SetFloat(p, v);
            // the mosaic draws in the opaque range (in the depth and opaque textures), not at the original's queue
            if (replacement.name == HS2Next.Chara.Mosaic.MosaicSet.ShaderName) q = HS2Next.Chara.Mosaic.MosaicSet.Queue;
            m.renderQueue = q;
            // the fog modes the original was compiled for: a transparent replacement draws the scene's fog only in those
            HS2FogFeature.StampModes(m, orig, q);
            foreach (var k in drop) m.DisableKeyword(k);
            if (routed) StandardInputs(m, cutout);
            // Built-in forward rendering applies no realtime shadows to materials in the transparent queues (> 2500);
            // replacements that follow it declare _RECEIVE_SHADOWS_OFF (the Standard family, the legacy shaders).
            if (q > 2500 && replacement.keywordSpace.FindKeyword(ReceiveShadowsOff).isValid) m.EnableKeyword(ReceiveShadowsOff);
        }

        public const string ReceiveShadowsOff = "_RECEIVE_SHADOWS_OFF";

        // A cutout original: it declares _Cutoff and its sub-shader is tagged TransparentCutout or the material sits in the
        // alpha-test queues (2450-2999).
        static bool IsCutout(Shader orig, int queue) =>
            orig.FindPropertyIndex("_Cutoff") >= 0
            && (orig.FindSubshaderTagValue(0, new ShaderTagId("RenderType")).name == "TransparentCutout" || (queue >= 2450 && queue < 3000));

        // The Standard keywords of the inputs a routed material has (see StandardRouted), set on its Standard replacement.
        static void StandardInputs(Material m, bool cutout)
        {
            void On(string keyword, bool when) { if (when && m.shader.keywordSpace.FindKeyword(keyword).isValid) m.EnableKeyword(keyword); }
            bool Tex(string p) => m.HasProperty(p) && m.GetTexture(p);
            On("_NORMALMAP", Tex("_BumpMap"));
            On("_SPECGLOSSMAP", Tex("_SpecGlossMap"));
            On("_METALLICGLOSSMAP", Tex("_MetallicGlossMap"));
            On("_EMISSION", m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0f);
            On("_ALPHATEST_ON", cutout);
        }

        // URP/Lit with the common inputs: _MainTex (texture, tiling, offset) → _BaseMap, _Color (or _BaseColor) rgb →
        // _BaseColor, _BumpMap. The original-named _MainTex and _Color keep their values too (URP/Lit declares both as
        // hidden properties but the shader switch leaves _MainTex empty), so code and reports that read the Built-in names
        // (Studio panels, item.materials) see what the bundle material had.
        // Materials switched to the URP/Lit fallback (the edit units mirror _MainTex /
        // _Color writes onto _BaseMap / _BaseColor for them). Weak keys: a destroyed material leaves the table with its wrapper.
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Material, object> fallbacks = new System.Runtime.CompilerServices.ConditionalWeakTable<Material, object>();
        static readonly object fallbackFromColor = new object(), fallbackFromBaseColor = new object();

        /// <summary>True when <paramref name="m"/> was switched to the URP/Lit fallback by this class and still uses URP/Lit.</summary>
        public static bool IsFallback(Material m) => m && fallbacks.TryGetValue(m, out _) && m.shader && m.shader.name == "Universal Render Pipeline/Lit";

        /// <summary>True when the fallback took _BaseColor from the original's _Color (the original declared no _BaseColor).</summary>
        public static bool FallbackColorFromColor(Material m) => IsFallback(m) && fallbacks.TryGetValue(m, out var v) && v == fallbackFromColor;

        static void Fallback(Material m, Shader lit)
        {
            fallbacks.Remove(m);
            fallbacks.Add(m, m.HasProperty("_BaseColor") ? fallbackFromBaseColor : fallbackFromColor);
            bool hasTex = m.HasProperty("_MainTex");
            Texture tex = hasTex ? m.GetTexture("_MainTex") : null;
            Vector2 st = hasTex ? m.GetTextureScale("_MainTex") : Vector2.one, so = hasTex ? m.GetTextureOffset("_MainTex") : Vector2.zero;
            bool hasColor = m.HasProperty("_Color");
            Color color = hasColor ? m.GetColor("_Color") : Color.white;
            Color col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : color;
            Texture nrm = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
            int queue = m.renderQueue;
            bool cutout = m.HasProperty("_Cutoff");
            var unreal = UnrealInputs(m);   // 3.7 F3: read before the shader switch (the original's Material_Texture2D_*)
            MaterialCopies.SetShader(m, lit);
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", st);
            m.SetTextureOffset("_BaseMap", so);
            if (hasTex && m.HasProperty("_MainTex"))
            {
                m.SetTexture("_MainTex", tex);
                m.SetTextureScale("_MainTex", st);
                m.SetTextureOffset("_MainTex", so);
            }
            if (hasColor && m.HasProperty("_Color")) m.SetColor("_Color", color);
            m.SetColor("_BaseColor", new Color(col.r, col.g, col.b, 1));
            if (nrm) { m.SetTexture("_BumpMap", nrm); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Smoothness", 0.2f);
            if (cutout || queue >= 2450) { m.SetFloat("_AlphaClip", 1); m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_Cutoff", 0.5f); }
            if (unreal != null) ApplyUnreal(m, unreal);
            m.renderQueue = queue;
        }

        /// <summary>Switches <paramref name="m"/> to the URP/Lit fallback as ApplyUrp does for an unmapped shader (tools and
        /// tests; 3.7 F3).</summary>
        public static void ApplyFallback(Material m)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (m && lit) Fallback(m, lit);
        }

        public const string WaterShader = "HS2Next/Compatible/Water";

        /// <summary>Fills HS2Next/Compatible/Water's generic inputs on a swapped material from a copy of the original (set by
        /// Scenes.Runtime.MapShaderHooks at start-up: its name table per water family); null: the water keeps its defaults.</summary>
        public static Func<Material, Material, bool> WaterInputs;

        static void DestroyCopy(Material m) { if (Application.isPlaying) Object.Destroy(m); else Object.DestroyImmediate(m); }

    
        // ---- 3.7 F3: Unreal/* input heuristic (W2), called from Fallback.

        /// <summary>The textures the URP/Lit fallback takes from a UE-converted material (<see cref="UnrealInputs"/>).</summary>
        public sealed class UnrealTextures
        {
            public Texture Albedo, Normal;
            public string AlbedoProperty, NormalProperty;
            public Vector2 AlbedoScale = Vector2.one, AlbedoOffset = Vector2.zero;
        }

        static readonly Regex UnrealAlbedoName = new Regex(@"(_|^)(d|diff|diffuse|basecolor|basecolour|bc|alb|albedo|color|colour|col|base)(_?\d+)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex UnrealNormalName = new Regex(@"(_|^)(n|nrm|nor|norm|normal|normals|normalmap)(_?\d+)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        // last name tokens that are never the colour texture (placeholders, masks, packed / single-channel maps, emission)
        static readonly HashSet<string> UnrealNotAlbedo = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "white", "black", "gray", "grey", "placeholder", "blackplaceholder", "whiteplaceholder", "noise", "mask", "msk", "emissive",
            "emission", "emm", "glow", "e", "opacity", "alpha", "height", "metallik", "metallic", "metalness", "rough", "roughness", "ao",
            "r", "m", "o", "orm", "rma", "ram", "arm", "dpr", "ordp", "reflection", "macrovariation", "specular", "spec", "gloss", "cavity",
            "disp", "displacement",
        };

        /// <summary>
        /// Inputs for a material of a UE-converted shader ("Unreal/&lt;material&gt;": one generated shader per UE material, its
        /// textures bound to Material_Texture2D_0..N in a material-specific order and _MainTex left empty, so the plain
        /// fallback draws it untextured). Read before the shader switch. Normal map: the first Material_Texture2D_k (in k order)
        /// that is BC5 or whose name ends in _N / _Nrm / _Normal (or contains "normal"; not UE's "DefaultNormal"); albedo:
        /// the first whose name ends in _D / _BaseColor / _BC / _ALB / _Albedo / _Diffuse / _Color (any colour space), else
        /// the first other sRGB texture whose last name token is not a placeholder, mask, packed or emission map. Null when
        /// <paramref name="m"/> is not such a material (another shader, or _MainTex set: the plain copy applies).
        /// </summary>
        public static UnrealTextures UnrealInputs(Material m)
        {
            if (!m || !m.shader || !m.shader.name.StartsWith("Unreal/", StringComparison.Ordinal)) return null;
            if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex")) return null;
            var slots = new List<(int k, string prop, Texture tex)>();
            foreach (var p in m.GetTexturePropertyNames())
            {
                if (!p.StartsWith("Material_Texture2D_", StringComparison.Ordinal)) continue;
                if (!int.TryParse(p.Substring("Material_Texture2D_".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int k)) continue;
                if (!m.HasProperty(p)) continue;
                var t = m.GetTexture(p);
                if (t && t.dimension == TextureDimension.Tex2D) slots.Add((k, p, t));
            }
            if (slots.Count == 0) return null;
            slots.Sort((a, b) => a.k.CompareTo(b.k));
            var r = new UnrealTextures();
            foreach (var (_, p, t) in slots)
            {
                var gf = t.graphicsFormat;
                bool bc5 = gf == GraphicsFormat.RG_BC5_UNorm || gf == GraphicsFormat.RG_BC5_SNorm;
                string n = t.name ?? "";
                bool named = UnrealNormalName.IsMatch(n) || n.IndexOf("normal", StringComparison.OrdinalIgnoreCase) >= 0;
                if ((bc5 || named) && n.IndexOf("defaultnormal", StringComparison.OrdinalIgnoreCase) < 0) { r.Normal = t; r.NormalProperty = p; break; }
            }
            foreach (var (_, p, t) in slots)
                if (t != r.Normal && UnrealAlbedoName.IsMatch(t.name ?? "")) { r.Albedo = t; r.AlbedoProperty = p; break; }
            if (!r.Albedo)
                foreach (var (_, p, t) in slots)
                {
                    if (t == r.Normal || !GraphicsFormatUtility.IsSRGBFormat(t.graphicsFormat)) continue;
                    string n = t.name ?? "";
                    if (n.IndexOf("normal", StringComparison.OrdinalIgnoreCase) >= 0 || UnrealNotAlbedo.Contains(LastToken(n))) continue;
                    r.Albedo = t; r.AlbedoProperty = p; break;
                }
            if (r.AlbedoProperty != null) { r.AlbedoScale = m.GetTextureScale(r.AlbedoProperty); r.AlbedoOffset = m.GetTextureOffset(r.AlbedoProperty); }
            return r;
        }

        // last name token without digits ("T_Stripes_Normal_4" -> "normal", "T_Ind_Jun_Fan_Metal_Rusty_01_D" -> "d")
        static string LastToken(string n)
        {
            var toks = n.Split(new[] { '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = toks.Length - 1; i >= 0; i--)
            {
                string t = toks[i].TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                if (t.Length > 0) return t.ToLowerInvariant();
            }
            return "";
        }

        // URP/Lit inputs of an Unreal material (W2): albedo -> _BaseMap (with its slot's tiling / offset), normal -> _BumpMap
        // (BC5 or DXT5nm: URP unpacks both), smoothness 0.3, metallic 0, Cull Off. The UE material graph (packed masks, emission,
        // opacity, UV maths) is not reproduced.
        static void ApplyUnreal(Material m, UnrealTextures u)
        {
            if (u.Albedo)
            {
                m.SetTexture("_BaseMap", u.Albedo);
                m.SetTextureScale("_BaseMap", u.AlbedoScale);
                m.SetTextureOffset("_BaseMap", u.AlbedoOffset);
            }
            if (u.Normal) { m.SetTexture("_BumpMap", u.Normal); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Smoothness", 0.3f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Cull", 0f);
        }
        // ---- end of 3.7 F3 (Unreal)
    }
}
