// The map shaders that are not under Assets/HS2Next/Shaders but must be in the players (
// package B of Phase 3.2): URP's own SpeedTree7 / SpeedTree7 Billboard (M7) and Terrain Lit, and Unity's built-in
// skybox shaders that ShaderMap maps map skyboxes to. PlayerBuild.WriteShaderSet adds them to the shader
// variant collection (Assets/HS2Next/Resources/HS2NextShaders.shadervariants): a package or built-in shader is in a player
// only when something in the build references it, and its shader_feature keyword sets only through such a list.
// Keyword sets: SpeedTree7 the census sets of every map material (Tools/survey/map_shader_census.tsv; C's expectation file:
// EFFECT_BUMP EFFECT_HUE_VARIATION with each GEOM_TYPE_*), the billboard and terrain shaders every combination of their
// shader_feature keywords (no map material lists them: billboard materials come from BillboardAssets, terrain materials
// are templates). Skyboxes have only multi_compile keywords (all variants are kept) and draw in the Normal pass type.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public static class MapShaderSet
{
    public sealed class Entry
    {
        public string Shader;
        public string[][] Keywords;
        public PassType[] Passes;
        public string Why;
    }

    static readonly PassType[] Srp = { PassType.ScriptableRenderPipeline, PassType.ShadowCaster };
    static readonly PassType[] Sky = { PassType.Normal, PassType.ScriptableRenderPipeline };

    static string[][] Subsets(params string[] kw) =>
        Enumerable.Range(0, 1 << kw.Length).Select(m => kw.Where((k, i) => (m & (1 << i)) != 0).ToArray()).ToArray();

    static string[][] SpeedTreeSets()
    {
        var sets = new List<string[]>();
        foreach (var geom in new[] { "GEOM_TYPE_BRANCH", "GEOM_TYPE_BRANCH_DETAIL", "GEOM_TYPE_FROND", "GEOM_TYPE_LEAF" })
        {
            sets.Add(new[] { "EFFECT_BUMP", "EFFECT_HUE_VARIATION", geom });
            sets.Add(new[] { "EFFECT_BUMP", geom });
            sets.Add(new[] { geom });
        }
        return sets.ToArray();
    }

    public static readonly Entry[] Entries =
    {
        new Entry { Shader = "Universal Render Pipeline/Nature/SpeedTree7", Keywords = SpeedTreeSets(), Passes = Srp, Why = "Nature/SpeedTree (M7)" },
        new Entry { Shader = "Universal Render Pipeline/Nature/SpeedTree7 Billboard", Keywords = Subsets("EFFECT_BUMP", "EFFECT_HUE_VARIATION"), Passes = Srp, Why = "Nature/SpeedTree Billboard (M7)" },
        new Entry { Shader = "Universal Render Pipeline/Terrain/Lit", Keywords = Subsets("_NORMALMAP", "_MASKMAP"), Passes = Srp, Why = "terrain templates" },
        new Entry { Shader = "Skybox/Procedural", Keywords = new[] { new string[0] }, Passes = Sky, Why = "map skyboxes" },
        new Entry { Shader = "Skybox/Cubemap", Keywords = new[] { new string[0] }, Passes = Sky, Why = "map skyboxes" },
        new Entry { Shader = "Skybox/Panoramic", Keywords = new[] { new string[0] }, Passes = Sky, Why = "map skyboxes" },
        new Entry { Shader = "Skybox/6 Sided", Keywords = new[] { new string[0] }, Passes = Sky, Why = "map skyboxes" },
    };

    /// <summary>Adds every entry to <paramref name="svc"/>; returns one report line per shader. A shader that cannot be found
    /// or of which no variant can be listed is an error.</summary>
    public static List<string> Add(ShaderVariantCollection svc, List<string> errors)
    {
        var lines = new List<string>();
        foreach (var e in Entries)
        {
            var s = Shader.Find(e.Shader);
            if (!s) { errors.Add("map shader not found: " + e.Shader); continue; }
            int n = 0, rejected = 0;
            foreach (var pt in e.Passes)
                foreach (var kw in e.Keywords)
                {
                    // multi_compile keywords without an "off" state (SpeedTree7's LOD_FADE_PERCENTAGE) belong to every variant
                    bool added = false;
                    foreach (var k in new[] { kw, kw.Concat(new[] { "LOD_FADE_PERCENTAGE" }).ToArray() })
                    {
                        try { if (svc.Add(new ShaderVariantCollection.ShaderVariant(s, pt, k))) { n++; added = true; break; } }
                        catch (Exception) { }
                    }
                    if (!added) rejected++;
                }
            lines.Add($"{e.Shader}: {n} variants ({e.Why}; {rejected} pass / keyword combinations not in the shader)");
            if (n == 0) errors.Add("no variant of map shader " + e.Shader + " could be listed");
        }
        return lines;
    }
}
