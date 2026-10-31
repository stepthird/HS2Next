// Writes the Phase 3.3 settings of the production URP asset and its renderer (
// package C). Idempotent; run once by `unity_batch.ps1 -Method SceneTexturesAssets.Configure` after a change of the
// intended settings, and checked by SceneTexturesTest (row SceneTexturesTest, check A1):
//   HS2NextURP.asset           depth texture on, opaque texture on, opaque downsampling None (tears refraction needs full
//                              resolution); everything else as Phase 3.2 left it
//   HS2NextURP_Renderer.asset  copy depth after the opaques; post-processing on (URP's default PostProcessData: without it
//                              the renderer runs no Volume post-processing at all); renderer features in order:
//                              URP's ScreenSpaceAmbientOcclusion (active with intensity 0, After Opaque, Depth source:
//                              URP skips its pass at intensity 0, so renders without the effect part, e.g. the fit tools with
//                              -urpAsset production, get no AO; it must be active at build time or the player's build strips
//                              its shader / blue-noise resources and the renderer cannot be created (package B, measured);
//                              package B's AmbientOcclusionControl writes its settings at run time and never saves them),
//                              HS2DofFeature (active; draws only while the scene's DOF is on; references its shader so the
//                              player keeps it), MosaicWideFeature (5.M: active; the wide mosaic after the DOF, drawn while
//                              the option render.mosaicWide is on and mosaic-set members are in view; references its shader)
// The features are sub-assets of the renderer asset (as the renderer inspector adds them); the feature map is rebuilt.
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HS2Next;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class SceneTexturesAssets
{
    public const string AssetPath = "Assets/HS2Next/Settings/HS2NextURP.asset";
    public const string RendererPath = "Assets/HS2Next/Settings/HS2NextURP_Renderer.asset";
    public const string PostProcessDataPath = "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";
    public const string DofShaderPath = "Assets/HS2Next/Shaders/Hidden_HS2Dof.shader";
    public const string SsaoName = "ScreenSpaceAmbientOcclusion";
    public const string DofName = "HS2DofFeature";
    public const string MosaicWideShaderPath = "Assets/HS2Next/Shaders/Hidden_HS2MosaicWide.shader";
    public const string MosaicWideName = "MosaicWideFeature";

    /// <summary>Batch entry: applies the settings and saves both assets; logs what changed.</summary>
    public static void Configure()
    {
        var log = new StringBuilder();
        bool ok = Apply(log);
        Debug.Log("[SceneTexturesAssets] " + (ok ? "OK" : "FAILED") + "\n" + log);
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }

    public static bool Apply(StringBuilder log)
    {
        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
        var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (!asset || !data) { log.AppendLine("asset or renderer not found"); return false; }

        var so = new SerializedObject(asset);
        SetBool(so, "m_RequireDepthTexture", true, log);
        SetBool(so, "m_RequireOpaqueTexture", true, log);
        SetInt(so, "m_OpaqueDownsampling", (int)Downsampling.None, log);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);

        var ppd = AssetDatabase.LoadAssetAtPath<PostProcessData>(PostProcessDataPath);
        if (!ppd) { log.AppendLine("URP PostProcessData not found: " + PostProcessDataPath); return false; }
        var rso = new SerializedObject(data);
        SetInt(rso, "m_CopyDepthMode", (int)CopyDepthMode.AfterOpaques, log);
        var pp = rso.FindProperty("postProcessData");
        if (pp.objectReferenceValue != ppd) { pp.objectReferenceValue = ppd; log.AppendLine("postProcessData = URP default"); }
        rso.ApplyModifiedPropertiesWithoutUndo();

        // features: SSAO (inactive) then HS2DofFeature (active)
        var features = data.rendererFeatures;
        var ssao = features.OfType<ScreenSpaceAmbientOcclusion>().FirstOrDefault();
        if (!ssao)
        {
            ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            ssao.name = SsaoName;
            AssetDatabase.AddObjectToAsset(ssao, data);
            log.AppendLine("added ScreenSpaceAmbientOcclusion");
        }
        if (!ssao.isActive) { ssao.SetActive(true); log.AppendLine("ScreenSpaceAmbientOcclusion active"); }
        var aso = new SerializedObject(ssao);
        SetFloat(aso, "m_Settings.Intensity", 0f, log);
        SetBool(aso, "m_Settings.AfterOpaque", true, log);
        SetInt(aso, "m_Settings.Source", 0, log);         // Depth (normals reconstructed: no DepthNormals prepass)
        SetFloat(aso, "m_Settings.Radius", 0.5f, log);
        aso.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ssao);
        var dof = features.OfType<HS2DofFeature>().FirstOrDefault();
        if (!dof)
        {
            dof = ScriptableObject.CreateInstance<HS2DofFeature>();
            dof.name = DofName;
            AssetDatabase.AddObjectToAsset(dof, data);
            log.AppendLine("added HS2DofFeature");
        }
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(DofShaderPath);
        if (!shader) { log.AppendLine("DOF shader not found: " + DofShaderPath); return false; }
        if (dof.Shader != shader) { dof.Shader = shader; EditorUtility.SetDirty(dof); log.AppendLine("HS2DofFeature shader set"); }
        if (!dof.isActive) { dof.SetActive(true); log.AppendLine("HS2DofFeature active"); }
        // 5.M: the wide mosaic right after the DOF (both draw after post-processing; the list order is their order)
        var wide = features.OfType<MosaicWideFeature>().FirstOrDefault();
        if (!wide)
        {
            wide = ScriptableObject.CreateInstance<MosaicWideFeature>();
            wide.name = MosaicWideName;
            AssetDatabase.AddObjectToAsset(wide, data);
            log.AppendLine("added MosaicWideFeature");
        }
        var wideShader = AssetDatabase.LoadAssetAtPath<Shader>(MosaicWideShaderPath);
        if (!wideShader) { log.AppendLine("wide mosaic shader not found: " + MosaicWideShaderPath); return false; }
        if (wide.Shader != wideShader) { wide.Shader = wideShader; EditorUtility.SetDirty(wide); log.AppendLine("MosaicWideFeature shader set"); }
        if (!wide.isActive) { wide.SetActive(true); log.AppendLine("MosaicWideFeature active"); }

        // the fog pass (when the renderer has it) stays right after the SSAO: it draws before the transparents
        var fog = features.OfType<HS2FogFeature>().FirstOrDefault();
        var order = new List<ScriptableRendererFeature> { ssao };
        if (fog) order.Add(fog);
        order.Add(dof);
        order.Add(wide);
        order.AddRange(features.Where(f => f && f != ssao && f != fog && f != dof && f != wide));
        rso.Update();
        var list = rso.FindProperty("m_RendererFeatures");
        var map = rso.FindProperty("m_RendererFeatureMap");
        bool same = list.arraySize == order.Count && Enumerable.Range(0, order.Count).All(i => list.GetArrayElementAtIndex(i).objectReferenceValue == order[i]);
        list.arraySize = order.Count;
        map.arraySize = order.Count;
        for (int i = 0; i < order.Count; i++)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = order[i];
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(order[i], out string _, out long localId);
            map.GetArrayElementAtIndex(i).longValue = localId;
        }
        rso.ApplyModifiedPropertiesWithoutUndo();
        if (!same) log.AppendLine("features: " + string.Join(", ", order.Select(f => f.name)));
        data.SetDirty();
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        // the local ids exist only after the save: write the map again if any was 0
        rso.Update();
        bool fixedIds = false;
        for (int i = 0; i < order.Count; i++)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(order[i], out string _, out long localId);
            if (rso.FindProperty("m_RendererFeatureMap").GetArrayElementAtIndex(i).longValue != localId)
            {
                rso.FindProperty("m_RendererFeatureMap").GetArrayElementAtIndex(i).longValue = localId;
                fixedIds = true;
            }
        }
        if (fixedIds)
        {
            rso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }
        log.AppendLine(Describe());
        return true;
    }

    /// <summary>The settings as they are now (for reports).</summary>
    public static string Describe()
    {
        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
        var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (!asset || !data) return "asset or renderer not found";
        return $"depth texture {asset.supportsCameraDepthTexture}, opaque texture {asset.supportsCameraOpaqueTexture}, " +
               $"opaque downsampling {asset.opaqueDownsampling}, copy depth {data.copyDepthMode}, " +
               $"postProcessData {(data.postProcessData ? data.postProcessData.name : "none")}, features " +
               string.Join(", ", data.rendererFeatures.Select(f => f ? $"{f.GetType().Name}({(f.isActive ? "active" : "inactive")})" : "null")) +
               SsaoDescription(data);
    }

    /// <summary>The SSAO feature's stored settings (", SSAO intensity 0 ...").</summary>
    public static string SsaoDescription(UniversalRendererData data)
    {
        var f = data ? data.rendererFeatures.OfType<ScreenSpaceAmbientOcclusion>().FirstOrDefault() : null;
        if (!f) return "";
        var so = new SerializedObject(f);
        return $"; SSAO stored intensity {so.FindProperty("m_Settings.Intensity").floatValue}, after opaque {so.FindProperty("m_Settings.AfterOpaque").boolValue}, " +
               $"source {so.FindProperty("m_Settings.Source").intValue}, radius {so.FindProperty("m_Settings.Radius").floatValue}";
    }

    static void SetBool(SerializedObject so, string name, bool v, StringBuilder log)
    {
        var p = so.FindProperty(name);
        if (p.boolValue != v) { p.boolValue = v; log.AppendLine($"{name} = {v}"); }
    }

    static void SetFloat(SerializedObject so, string name, float v, StringBuilder log)
    {
        var p = so.FindProperty(name);
        if (p.floatValue != v) { p.floatValue = v; log.AppendLine($"{name} = {v}"); }
    }

    static void SetInt(SerializedObject so, string name, int v, StringBuilder log)
    {
        var p = so.FindProperty(name);
        if (p.intValue != v) { p.intValue = v; log.AppendLine($"{name} = {v}"); }
    }
}
