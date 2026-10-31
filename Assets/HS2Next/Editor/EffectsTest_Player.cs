// EffectsTest.BuildPlayerFx (Phase 3.3 package B, U4 / U5 / U6 in the player): the app player built to Builds/AppFx (not the
// shared Builds/App) with the production renderer completed IN MEMORY for this build only — URP's default PostProcessData
// (the renderer asset has none until package C sets it) and an active SSAO feature (package C's entry) — so that
// Tools/effects_player_check.py can measure the screen effects in a player before package C lands. Trap: the player build
// SAVES in-memory edits of loaded assets — restore HS2NextURP_Renderer.asset from git right after this build (that one file,
// when no one else changed it). The SSAO feature must be active at build time: URP strips its resources otherwise and the
// player cannot create its renderer (all captures black).
//   & Tools\unity_batch.ps1 -Method EffectsTest.BuildPlayerFx -MethodArgs @('-buildOut','Builds\AppFx\HS2Next.exe')   (relative to the project folder)
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static partial class EffectsTest
{
    public static void BuildPlayerFx()
    {
        var urd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/HS2Next/Settings/HS2NextURP_Renderer.asset");
        if (!urd) { Debug.LogError("BuildPlayerFx: renderer asset not found"); EditorApplication.Exit(2); return; }
        bool pp = false, ao = false;
        if (!urd.postProcessData)
        {
            urd.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(UrpPostProcessData);
            pp = true;
        }
        ScreenSpaceAmbientOcclusion f = null;
        bool activated = false;
        if (urd.TryGetRendererFeature<ScreenSpaceAmbientOcclusion>(out var existing))
        {
            if (!existing.isActive) { existing.SetActive(true); activated = true; }
        }
        else
        {
            f = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            f.name = "ScreenSpaceAmbientOcclusion";
            f.SetActive(true);    // URP strips the SSAO resources when no SSAO feature is active at build time (the player then fails to create its renderer)
            AssetDatabase.AddObjectToAsset(f, urd);   // a sub-object so that the build serializes it
            urd.rendererFeatures.Add(f);
            ao = true;
        }
        urd.SetDirty();
        Debug.Log($"BuildPlayerFx: in memory for this build: postProcessData {(pp ? "assigned" : "already set")}, SSAO feature {(ao ? "added (active)" : activated ? "activated" : "already active")}");
        PlayerBuild.BuildApp();
        // the build has written the edits above into the renderer asset: the caller restores it from git
    }
}
