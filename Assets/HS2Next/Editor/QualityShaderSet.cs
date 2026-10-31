// Package S's part of the players' shader variant collection (Phase 3.3 package S),
// called by name from Editor/EffectShaderSet.cs (package C's line in PlayerBuild.WriteShaderSet): the MotionVectors passes of
// the fitted opaque / cutout shaders (TAA; PassType.MotionVectors is a pass type of its own, so the collection names them
// separately from the SRP passes PlayerBuild lists already). The sky uses the bundle's own AIOsky/V2 (U3: it draws under
// URP with the sun feed), so no sky shader of the project is needed.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

public static class QualityShaderSet
{
    /// <summary>Adds the MotionVectors variants; returns one report line.</summary>
    public static List<string> Add(ShaderVariantCollection svc, List<string> errors)
    {
        var lines = new List<string>();
        int shaders = 0, listed = 0;
        foreach (var g in AssetDatabase.FindAssets("t:Shader", new[] { "Assets/HS2Next/Shaders" }))
        {
            var s = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(g));
            if (!s || !s.name.StartsWith("HS2Next/", StringComparison.Ordinal)) continue;
            var data = ShaderUtil.GetShaderData(s);
            if (data == null || data.SubshaderCount == 0) continue;
            var sub = data.GetSubshader(0);
            bool mv = false;
            for (int i = 0; i < sub.PassCount; i++)
                mv |= string.Equals(sub.GetPass(i).FindTagValue(new ShaderTagId("LightMode")).name, "MotionVectors", StringComparison.OrdinalIgnoreCase);
            if (!mv) continue;
            shaders++;
            try
            {
                var v = new ShaderVariantCollection.ShaderVariant(s, PassType.MotionVectors, new string[0]);
                svc.Add(v);
                if (svc.Contains(v)) listed++;
            }
            catch (Exception e) { errors.Add("MotionVectors variant of " + s.name + ": " + e.Message); }
        }
        lines.Add($"QualityShaderSet: MotionVectors passes of {shaders} fitted shaders, {listed} listed (TAA)");
        if (shaders > 0 && listed == 0) errors.Add("no MotionVectors variant of the fitted shaders could be listed");
        return lines;
    }
}
