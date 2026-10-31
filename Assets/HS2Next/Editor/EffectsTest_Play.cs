// EffectsTest.play (Phase 3.3 package B): play mode, the app's URP asset.
//   P1 map 10 load: MapHooks.Loaded re-applies blocks 1–10 (the applied count rises by 10) with the map scene active: its
//      RenderSettings get the block's fog colour / density, RenderSettings.fog stays off, the map's fog mode is kept
//   P2 map unload: MapHooks.Unloaded re-applies into the runtime's own scene (fog values, fog off)
//   P3 a scene with every one of the 45 values away from its default: save → new (defaults realized) → load: the 12
//      blocks equal (canonical JSON) and the realized state follows (bloom, vignette, self shadow, probe, grading strip)
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static partial class EffectsTest
{
    static string PlayReport => Path.Combine(OutDir, "effects_play_report.txt");

    public static void RunPlay()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(PlayReport, "SUMMARY: 0 PASS, 1 FAIL\nEffectsTest.play: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(EffectsTest) + "." + nameof(PlayMain), PlayReport, 30);
    }

    /// <summary>Every value of the 12 blocks away from ScreenEffectDefine's defaults.</summary>
    public static ScreenEffects AllChanged() => new ScreenEffects
    {
        ColorGrading = new ColorGradingEffect { Lut = ContentId.Vanilla(5).ToString(), Blend = 0.7f, Saturation = 33, Brightness = -10, Contrast = 25 },
        AmbientOcclusion = new AmbientOcclusionEffect { Enabled = false, Intensity = 0.6f, Thickness = 4f, Color = new Color4(0.1f, 0.2f, 0.3f, 1f) },
        Bloom = new BloomEffect { Enabled = true, Intensity = 4f, Threshold = 0.8f, SoftKnee = 0.5f, Clamp = false, Diffusion = 5f, Color = new Color4(0.9f, 0.8f, 0.7f, 1f) },
        DepthOfField = new DepthOfFieldEffect { Enabled = true, Focus = null, FocalSize = 0.7f, Aperture = 0.4f },
        Vignette = new VignetteEffect { Enabled = true, Intensity = 0.5f },
        Ssr = new SsrEffect { Enabled = false },
        ReflectionProbe = new ReflectionProbeEffect { Enabled = true, Cubemap = ContentId.Vanilla(3).ToString(), Intensity = 1.5f },
        Fog = new FogEffect { Enabled = true, ExcludeFarPixels = true, Height = 30f, HeightDensity = 0.05f, Color = new Color4(0.3f, 0.5f, 0.7f, 1f), Density = 0.0003f },
        SunShafts = new SunShaftsEffect { Enabled = true, Caster = null, ThresholdColor = new Color4(0.4f, 0.4f, 0.4f, 1f), Color = new Color4(1f, 0.9f, 0.6f, 1f), DistanceFalloff = 0.5f, BlurSize = 3f, Intensity = 2f },
        Shadows = new ShadowsEffect { Enabled = false },
        Ambient = new AmbientLightEffect { Sky = new Color4(0.5f, 0.6f, 0.9f, 1f), Equator = new Color4(0.6f, 0.6f, 0.6f, 1f), Ground = new Color4(0.4f, 0.3f, 0.2f, 1f) },
        Sky = new SkyEffect { Enabled = true, Pattern = ContentId.Vanilla(2).ToString() },
    };

    static IEnumerator PlayMain()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"EffectsTest.play (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
        var pipe = new HS2Next.App.AppPipeline();
        pipe.Apply();
        var s = Services();
        rs = SceneRuntimeTest.NewSession(s);
        rr = new SceneRuntime(rs, s, null, true).AddStandardParts();
        IEnumerator body = null;
        try { body = PlayBody(); }
        catch (Exception e) { Check("play body started", false, e.ToString()); }
        if (body != null)
            while (true)
            {
                object cur;
                try
                {
                    if (!body.MoveNext()) break;
                    cur = body.Current;
                }
                catch (Exception e) { Check("play body ran without an exception", false, e.ToString()); break; }
                yield return cur;
            }
        var map = rr.GetPart<MapRealization>();
        if (map != null && map.State == MapState.Loaded)
        {
            rs.Set(null, "map.ref", null);
            bool[] ok = { false };
            yield return PlayModeHarness.WaitUntil(() => !map.IsBusy && map.PendingUnloads == 0, 60, ok);
        }
        rr.Dispose();
        pipe.Restore();
        rep.Insert(0, $"SUMMARY: {pass} PASS, {fail} FAIL\nEFFECTS PLAY TEST ({sw.Elapsed.TotalSeconds:F0} s)\n");
        File.WriteAllText(PlayReport, rep.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        Debug.Log($"EFFECTS PLAY TEST: {pass} PASS, {fail} FAIL");
        PlayModeHarness.ExitCode = fail > 0 ? 1 : 0;
    }

    static IEnumerator PlayBody()
    {
        yield return PlayModeHarness.WaitFrames(3);
        var fx = Fx;
        var map = rr.GetPart<MapRealization>();
        Check("runtime with SceneEffects and MapRealization", fx != null && map != null);
        var fogColor = new Color4(0.3f, 0.5f, 0.7f, 1f);
        using (var tx = rs.Commands.BeginOrJoin("fog", default))
        {
            tx.Set(null, "effects.fog.enabled", true);
            tx.Set(null, "effects.fog.color", fogColor);
            tx.Set(null, "effects.fog.density", 0.0007f);
            tx.Commit();
        }
        yield return PlayModeHarness.WaitFrames(2);
        Section("P1 map 10 load");
        var appScene = SceneManager.GetActiveScene();
        int before = fx.AppliedCount;
        rs.Set(null, "map.ref", ContentId.Vanilla(10).ToString());
        bool[] ok = { false };
        yield return PlayModeHarness.WaitUntil(() => map.State == MapState.Loaded && !map.IsBusy, 300, ok);
        Check("map 10 loaded", ok[0] && map.State == MapState.Loaded, map.State.ToString());
        yield return PlayModeHarness.WaitFrames(3);
        var active = SceneManager.GetActiveScene();
        Check("the map scene is active", active != appScene, active.name);
        Check($"Loaded re-applied blocks 1–10 ({fx.AppliedCount - before} applications)", fx.AppliedCount - before >= 10);
        Check("the map scene's RenderSettings: fog colour / density written, fog off",
            RenderSettings.fogColor == new Color(0.3f, 0.5f, 0.7f, 1f) && Mathf.Abs(RenderSettings.fogDensity - 0.0007f) < 1e-8f && !RenderSettings.fog,
            $"{RenderSettings.fogColor} {RenderSettings.fogDensity} fog {RenderSettings.fog} mode {RenderSettings.fogMode}");
        Info("map 10 fog mode kept: " + RenderSettings.fogMode + " (HS2: Exp2, H9)");
        Check("fog mode stays the map's (Exp2 on map 10)", RenderSettings.fogMode == FogMode.ExponentialSquared, RenderSettings.fogMode.ToString());
        Check("the effect volume and the strip survive the map load", fx.Volume?.Volume && fx.Strip && fx.Volume.Lookup.contribution.value == 1f);

        Section("P2 map unload");
        int b2 = fx.AppliedCount;
        rs.Set(null, "effects.fog.density", 0.0004f);
        rs.Set(null, "map.ref", null);
        yield return PlayModeHarness.WaitUntil(() => !map.IsBusy && map.PendingUnloads == 0 && map.State != MapState.Loaded, 120, ok);
        yield return PlayModeHarness.WaitFrames(3);
        Check("the runtime's scene is active again", SceneManager.GetActiveScene() == appScene, SceneManager.GetActiveScene().name);
        Check($"Unloaded re-applied ({fx.AppliedCount - b2} applications)", fx.AppliedCount - b2 >= 10);
        Check("the runtime scene's RenderSettings: density 0.0004, fog off", Mathf.Abs(RenderSettings.fogDensity - 0.0004f) < 1e-8f && !RenderSettings.fog && RenderSettings.fogColor == new Color(0.3f, 0.5f, 0.7f, 1f));

        Section("P3 save → new → load of the 45 values");
        var want = AllChanged();
        var r = rs.Set(null, "effects", want);
        Check("effects set as a whole", r != null && r.Status != CommandStatus.Error, r?.ToString());
        yield return PlayModeHarness.WaitFrames(2);
        string text0 = FieldJson.ToText(rs.Model.Settings.Effects);
        var dflt = new ScreenEffects();
        int changed = 0;
        foreach (var b in SceneEffects.BlockNames)
        {
            var a = Newtonsoft.Json.Linq.JObject.Parse(FieldJson.ToText(typeof(ScreenEffects).GetFields().First(f => char.ToLowerInvariant(f.Name[0]) + f.Name.Substring(1) == b).GetValue(want)));
            var d = Newtonsoft.Json.Linq.JObject.Parse(FieldJson.ToText(typeof(ScreenEffects).GetFields().First(f => char.ToLowerInvariant(f.Name[0]) + f.Name.Substring(1) == b).GetValue(dflt)));
            changed += a.Properties().Count(p => !Newtonsoft.Json.Linq.JToken.DeepEquals(p.Value, d[p.Name]));
        }
        Check("41 of the 45 values differ from the defaults (bloom / vignette stay enabled, focus / caster null)", changed == 41, changed.ToString());
        string path = Path.Combine(OutDir, "effects_play_scene.hs2nscene");
        var save = rr.Ops.Save(path);
        Check("scene saved", save != null && save.Ok && File.Exists(path), save?.ToString());
        rs.NewScene();
        yield return PlayModeHarness.WaitFrames(3);
        Check("new scene: effects at the defaults", FieldJson.ToText(rs.Model.Settings.Effects) == FieldJson.ToText(new ScreenEffects()));
        var vu = rr.GetPart<SceneViewCamera>();
        Check("… realized: self shadow on, probe off", vu.Setup.RenderShadows && !fx.Probe.Active);
        var load = rr.Ops.Load(path);
        yield return PlayModeHarness.WaitFrames(5);
        Check("scene loaded", load != null && load.Ok, load?.Error?.Message ?? "");
        string text1 = FieldJson.ToText(rs.Model.Settings.Effects);
        Check("the 12 blocks (45 values) equal after the round trip", text1 == text0, text1.Length + " / " + text0.Length);
        var cam = vu.Camera;
        var bp = BloomMapping.Map(true, 4f, 0.8f, 0.5f, false, 5f, new Color(0.9f, 0.8f, 0.7f, 1f), cam.pixelWidth, cam.pixelHeight);
        Check("realized after load: bloom (clamp off → 65472), vignette 0.5, self shadow off, probe row 3 active at 1.5, strip baked",
            Mathf.Abs(fx.Volume.Bloom.intensity.value - bp.Intensity) < 1e-5f && fx.Volume.Bloom.clamp.value == 65472f && Mathf.Abs(fx.Volume.Vignette.intensity.value - 0.5f) < 1e-6f
            && !vu.Setup.RenderShadows && fx.Probe.Active && Mathf.Abs(fx.Probe.Probe.intensity - 1.5f) < 1e-6f && fx.Strip && fx.LutRef == ContentId.Vanilla(5).ToString(),
            $"bloom {fx.Volume.Bloom.intensity.value} vs {bp.Intensity}, vignette {fx.Volume.Vignette.intensity.value}, shadows {vu.Setup.RenderShadows}, probe {fx.Probe.Active}");
        Check("realized after load: DOF inputs (enabled, focal 0.7, aperture 0.4, the pivot)", fx.Dof.Enabled && Mathf.Abs(fx.Dof.FocalSize - 0.7f) < 1e-6f && Mathf.Abs(fx.Dof.Aperture - 0.4f) < 1e-6f && fx.Dof.FocusKind == "pivot");
        try { File.Delete(path); } catch (IOException) { }
    }
}
