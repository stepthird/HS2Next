// The Phase 3.3 effect shaders in the players' shader variant collection (package C),
// and since Phase 5.R the sun-shaft and height-fog pass shaders.
// PlayerBuild.WriteShaderSet lists every shader under Assets/HS2Next/Shaders already; this set checks that the shaders of
// the screen effects and of the scene-texture readers resolve and list at least one variant (the DOF passes have the light
// mode HS2NextFullscreen so that the collection can name them), and adds the shader sets of the other 3.3 packages that live
// outside that folder: package S's QualityShaderSet (sky port, TAA / pass includes), called by name when it exists so that
// this file compiles before and after S's commit. URP's own effect shaders (post-processing, SSAO) come with the renderer's
// PostProcessData and SSAO feature (both referenced by HS2NextURP_Renderer); stripping is checked in the build log (U5).
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

public static class EffectShaderSet
{
    public sealed class Entry
    {
        public string Shader;
        public PassType[] Passes;
        public string Why;
    }

    static readonly PassType[] Srp = { PassType.ScriptableRenderPipeline };

    public static readonly Entry[] Entries =
    {
        new Entry { Shader = "HS2Next/Hidden/HS2Dof", Passes = Srp, Why = "item-focus DOF (HS2DofFeature)" },
        new Entry { Shader = "HS2Next/Hidden/HS2SunShafts", Passes = Srp, Why = "sun shafts (HS2SunShaftsFeature, Phase 5.R)" },
        new Entry { Shader = "HS2Next/Hidden/HS2Fog", Passes = Srp, Why = "height fog over the opaque image (HS2FogFeature)" },
        new Entry { Shader = "HS2Next/Compatible/KriptoFX/ME/Distortion", Passes = Srp, Why = "KriptoFX distortion from the opaque texture" },
        new Entry { Shader = "HS2Next/AIT main namida", Passes = Srp, Why = "tears refraction" },
        new Entry { Shader = "HS2Next/Compatible/Water", Passes = Srp, Why = "water depth fade and refraction" },
        new Entry { Shader = "HS2Next/Effects/SlimeCutOut", Passes = Srp, Why = "slime distortion" },
    };

    /// <summary>Adds the entries (and package S's set when present); returns one report line per shader.</summary>
    public static List<string> Add(ShaderVariantCollection svc, List<string> errors)
    {
        var lines = new List<string>();
        foreach (var e in Entries)
        {
            var s = Shader.Find(e.Shader);
            if (!s) { errors.Add("effect shader not found: " + e.Shader); continue; }
            int listed = 0;
            foreach (var pt in e.Passes)
            {
                try
                {
                    var v = new ShaderVariantCollection.ShaderVariant(s, pt, new string[0]);
                    svc.Add(v);
                    if (svc.Contains(v)) listed++;
                }
                catch (Exception) { }
            }
            lines.Add($"{e.Shader}: {(listed > 0 ? "listed" : "NOT listed")} ({e.Why})");
            if (listed == 0) errors.Add("no variant of effect shader " + e.Shader + " could be listed");
        }
        lines.AddRange(AddOptional("QualityShaderSet", svc, errors));
        return lines;
    }

    /// <summary>Calls <c>{type}.Add(svc, errors)</c> of another package's set when the class exists.</summary>
    static IEnumerable<string> AddOptional(string typeName, ShaderVariantCollection svc, List<string> errors)
    {
        var t = typeof(EffectShaderSet).Assembly.GetType(typeName);
        var m = t?.GetMethod("Add", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ShaderVariantCollection), typeof(List<string>) }, null);
        if (m == null) return new[] { typeName + ": not present" };
        return m.Invoke(null, new object[] { svc, errors }) as IEnumerable<string> ?? new string[0];
    }
}
