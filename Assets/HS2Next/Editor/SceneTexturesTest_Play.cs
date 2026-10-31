// SceneTexturesTest.play (
// Phase 3.3 package C): play mode, the app's URP asset.
//   P1 V1: a vanilla particle item's prefab instantiated in play mode: its Play On Awake systems on active objects already
//      play right after Instantiate (HS2's collection rule relies on it)
//   P2 particle play / pause by visibility: item 184 (wall torch) collects its playing systems; hidden -> every system
//      paused, shown -> playing again
//   P3 KriptoFX distortion in the app: the item's KriptoFX/ME/Distortion materials map to HS2Next/Compatible/KriptoFX/ME/Distortion
//      and its particles change the image behind them (distortion drawn, not black)
//   P4 maps 0, 14, 32, 34: the water / slime / distortion renderers of the map captured with the scene textures on and off
//      (camera overrides): images in Spike/out/effects/textures/map<n>_{on,off}.png, no NaN, the textures change the
//      refracting surfaces; also from the comparison stage's camera (map<n>_stage_{on,off}.png, against package H's HS2
//      captures of the same pose)
// Run: Tools/unity_batch.ps1 -Method SceneTexturesTest.RunPlay -NoQuit [-MethodArgs '-maps','0,32']. Report:
// Spike/out/effects/scene_textures_play_report.txt.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.Content;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static partial class SceneTexturesTest
{
    static string PlayReport => Path.Combine(OutDir, "scene_textures_play_report.txt");
    static readonly string[] Refracting = { "HS2Next/Compatible/Water", "HS2Next/Effects/SlimeCutOut", "HS2Next/Compatible/KriptoFX/ME/Distortion", "HS2Next/Effects/Ice/IceFront" };

    public static void RunPlay()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(PlayReport, "SUMMARY: 0 PASS, 1 FAIL\nSceneTexturesTest.play: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(SceneTexturesTest) + "." + nameof(PlayMain), PlayReport, 30);
    }

    static IEnumerator PlayMain()
    {
        sb.Clear(); pass = fail = 0;
        var t0 = DateTime.Now;
        sb.AppendLine($"SceneTexturesTest.play (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
        var pipe = new HS2Next.App.AppPipeline();
        pipe.Apply();
        var log = new StringBuilder();
        var s = SceneRuntimeTest.NewServices(log);
        var rs = SceneRuntimeTest.NewSession(s);
        var rr = new SceneRuntime(rs, s, null, true).AddStandardParts();
        IEnumerator body = null;
        try { body = PlayBody(s, rs, rr); }
        catch (Exception e) { Check("play body", false, e.ToString()); }
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
        Cleanup();
        rr.Dispose();
        pipe.Restore();
        string summary = $"SUMMARY: {pass} PASS, {fail} FAIL";
        File.WriteAllText(PlayReport, (summary + $"\nSCENE TEXTURES PLAY TEST ({(DateTime.Now - t0).TotalSeconds:F0} s)\n" + sb + summary + "\n").Replace("\r\n", "\n"), new UTF8Encoding(false));
        Debug.Log("SCENE TEXTURES PLAY TEST " + summary);
        PlayModeHarness.ExitCode = fail > 0 ? 1 : 0;
    }

    static Camera ViewLike()
    {
        var go = Keep(new GameObject("st_play_cam"));
        var cam = go.AddComponent<Camera>();
        cam.nearClipPlane = 0.08f; cam.farClipPlane = 15000f; cam.fieldOfView = 40f;
        new ViewSetup().ApplyTo(cam, true);
        cam.enabled = false;
        return cam;
    }

    static void Frame(Camera cam, Bounds b, float distScale = 2.2f)
    {
        float r = Mathf.Max(b.extents.magnitude, 0.2f);
        var dir = new Vector3(0.0f, 0.45f, -1f).normalized;
        cam.transform.position = b.center + dir * (r * distScale);
        cam.transform.LookAt(b.center);
    }

    static IEnumerator PlayBody(RuntimeServices s, SceneSession rs, SceneRuntime rr)
    {
        yield return PlayModeHarness.WaitFrames(3);
        var row = s.Db.Items.Get(ContentId.Vanilla(184));
        // P1 V1
        if (row != null)
        {
            GameObject prefab = null;
            try { prefab = s.Catalog.LoadAsset<GameObject>(row); } catch (Exception e) { Info("prefab load threw " + e.Message); }
            if (prefab)
            {
                var inst = Object.Instantiate(prefab);
                var systems = inst.GetComponentsInChildren<ParticleSystem>(true).Where(p => p.gameObject.activeInHierarchy && p.main.playOnAwake).ToList();
                int playing = systems.Count(p => p.isPlaying);
                Object.Destroy(inst);
                Check("P1.V1", systems.Count > 0 && playing == systems.Count,
                    $"item 184 ({row}): {systems.Count} Play On Awake systems on active objects, {playing} isPlaying right after Instantiate");
            }
            else Check("P1.V1", false, "prefab of item 184 not loadable");
        }
        else Check("P1.V1", false, "item row 184 not found");

        // P2 play / pause
        // the first vanilla heat-haze item that builds as itself (the test catalog may gate a row: the fallback sphere)
        string id = null;
        ItemRealization ir = null;
        foreach (int no in new[] { 184, 139, 509, 1434 })
        {
            id = rr.Ops.AddItem(ContentId.Vanilla(no))?.NewId;
            for (int k = 0; k < 60 && (ir = id != null ? rr.FindItem(id) : null)?.State != RealizationState.Built; k++) yield return null;
            Info($"item {no}: state {(ir != null ? ir.State.ToString() : "none")}, fallback {ir?.IsFallback}, instance {(ir?.Instance ? ir.Instance.name : "none")}, " +
                 $"systems in the instance {(ir?.Instance ? ir.Instance.GetComponentsInChildren<ParticleSystem>(true).Length : 0)}");
            if (ir != null && ir.State == RealizationState.Built && !ir.IsFallback) break;
            if (id != null) rr.Ops.Delete(new[] { id });
            id = null; ir = null;
            yield return null;
        }
        yield return PlayModeHarness.WaitFrames(10);
        var ps = ir?.Particles?.Where(p => p).ToList() ?? new List<ParticleSystem>();
        Check("P2.collected", ps.Count > 0 && ps.All(p => p.isPlaying), $"{ir?.Row}: {ps.Count} collected systems, {ps.Count(p => p.isPlaying)} playing");
        rs.Set(id, "visible", false);
        yield return PlayModeHarness.WaitFrames(3);
        int paused = ps.Count(p => p.isPaused && !p.isPlaying);
        rs.Set(id, "visible", true);
        yield return PlayModeHarness.WaitFrames(3);
        int replay = ps.Count(p => p.isPlaying);
        Check("P2.playPause", ps.Count > 0 && paused == ps.Count && replay == ps.Count, $"hidden: {paused} / {ps.Count} paused; shown again: {replay} / {ps.Count} playing");

        // P3 KriptoFX distortion of the item
        yield return PlayModeHarness.WaitFrames(30);
        var dist = ir?.Instance ? ir.Instance.GetComponentsInChildren<Renderer>(true).Where(r => r.sharedMaterials.Any(m => m && m.shader && m.shader.name == "HS2Next/Compatible/KriptoFX/ME/Distortion")).ToList() : new List<Renderer>();
        var cam = ViewLike();
        if (dist.Count > 0)
        {
            var b = dist[0].bounds;
            foreach (var r in dist) b.Encapsulate(r.bounds);
            int aliveRun = ir.Instance.GetComponentsInChildren<ParticleSystem>(true).Sum(p => p.particleCount);
            // the batch player loop may leave the systems empty: advance them explicitly (paused at 1.5 s, played again below)
            foreach (var p in ir.Instance.GetComponentsInChildren<ParticleSystem>(true)) p.Simulate(1.5f, true, true);
            int alive = ir.Instance.GetComponentsInChildren<ParticleSystem>(true).Sum(p => p.particleCount);
            b = dist[0].bounds;
            foreach (var r in dist) b.Encapsulate(r.bounds);
            var bm = Keep(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            bm.SetTexture("_BaseMap", Checker(16));
            const int W = 640, H = 360;
            int changed = 0; float d = 0, lum = 0, lumOff = 0; bool nan = false; string seen = "";
            // look at the distortion particles from four sides with a checker backdrop behind them; keep the best view
            foreach (var dir in new[] { new Vector3(0, 0.2f, -1), new Vector3(0, 0.2f, 1), new Vector3(1, 0.2f, 0), new Vector3(-1, 0.2f, 0) })
            {
                Frame(cam, b, 3f);
                cam.transform.position = b.center + dir.normalized * Mathf.Max(b.extents.magnitude, 0.2f) * 3f;
                cam.transform.LookAt(b.center);
                var back = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));
                Object.Destroy(back.GetComponent<Collider>());
                back.transform.position = b.center + cam.transform.forward * (b.extents.magnitude + 1f);
                back.transform.rotation = cam.transform.rotation;
                back.transform.localScale = Vector3.one * Mathf.Max(b.extents.magnitude, 0.2f) * 8f;
                back.GetComponent<MeshRenderer>().sharedMaterial = bm;
                string tag = dir.x != 0 ? (dir.x > 0 ? "px" : "nx") : (dir.z > 0 ? "pz" : "nz");
                var on = Capture(cam, W, H, "p3_kripto_on_" + tag);
                foreach (var r in dist) r.enabled = false;
                var off = Capture(cam, W, H, "p3_kripto_off_" + tag);
                foreach (var r in dist) r.enabled = true;
                Object.DestroyImmediate(back);
                int c = 0;
                for (int k = 0; k < on.Length; k++) if (Mathf.Abs(on[k].g - off[k].g) > 2f / 255f) c++;
                nan |= AnyNaN(on);
                if (c >= changed)
                {
                    changed = c; seen = tag;
                    d = Region(on, off, W, H, 0f, 1f, 0f, 1f).diff; lum = Region(on, off, W, H, 0f, 1f, 0f, 1f).lum; lumOff = Region(off, off, W, H, 0f, 1f, 0f, 1f).lum;
                }
            }
            foreach (var p in ir.Instance.GetComponentsInChildren<ParticleSystem>(true)) p.Play();
            Check("P3.kriptoFX", changed > 0 && lum > 0.5f * lumOff && !nan,
                $"{dist.Count} distortion renderers ({string.Join(", ", dist.Select(r => r.name).Distinct().Take(4))}), {alive} particles after a 1.5 s simulation ({aliveRun} from the play loop): best view {seen}: {changed} pixels changed (> 2 / 255), mean |d| {d * 255:F2} / 255, luminance {lum:F3} vs {lumOff:F3}");
        }
        else Check("P3.kriptoFX", false, "the item has no renderer with HS2Next/Compatible/KriptoFX/ME/Distortion");
        if (id != null) rr.Ops.Delete(new[] { id });
        yield return PlayModeHarness.WaitFrames(2);

        // P4 maps
        var mapIds = (Arg("-maps", null) ?? "0,14,32,34").Split(',').Select(x => int.Parse(x.Trim())).ToArray();
        var map = rr.GetPart<MapRealization>();
        foreach (int n in mapIds)
        {
            rs.Set(null, "map.ref", ContentId.Vanilla(n).ToString());
            bool[] ok = { false };
            yield return PlayModeHarness.WaitUntil(() => map.State == MapState.Loaded && !map.IsBusy, 300, ok);
            yield return PlayModeHarness.WaitFrames(5);
            if (!ok[0] || map.State != MapState.Loaded) { Check($"P4.map{n}", false, "map not loaded: " + map.State); continue; }
            var rends = map.Scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true))
                .Where(r => r.enabled && r.gameObject.activeInHierarchy && r.sharedMaterials.Any(m => m && m.shader && Refracting.Contains(m.shader.name))).ToList();
            var byShader = rends.SelectMany(r => r.sharedMaterials.Where(m => m && m.shader && Refracting.Contains(m.shader.name)).Select(m => m.shader.name)).GroupBy(x => x)
                .Select(g => $"{g.Key.Replace("HS2Next/", "")} x{g.Count()}");
            Info($"map {n}: refracting renderers {rends.Count} ({string.Join(", ", byShader)})");
            if (rends.Count == 0) { Check($"P4.map{n}", true, "no refracting renderer on this map (nothing to compare)"); continue; }
            var target = rends.Where(r => r.sharedMaterials.Any(m => m && m.shader && m.shader.name != "HS2Next/Effects/Ice/IceFront")).OrderByDescending(r => r.bounds.size.sqrMagnitude).FirstOrDefault()
                         ?? rends.OrderByDescending(r => r.bounds.size.sqrMagnitude).First();
            var tb = target.bounds;
            float ext = Mathf.Min(tb.extents.magnitude, 40f);
            cam.transform.position = tb.center + new Vector3(0.3f, 0.5f, -1f).normalized * (ext * 1.2f + 2f);
            cam.transform.LookAt(tb.center);
            const int W = 960, H = 540;
            var on = Capture(cam, W, H, $"map{n}_on");
            var urp = cam.GetComponent<UniversalAdditionalCameraData>();
            urp.requiresDepthOption = CameraOverrideOption.Off; urp.requiresColorOption = CameraOverrideOption.Off;
            var off = Capture(cam, W, H, $"map{n}_off");
            urp.requiresDepthOption = CameraOverrideOption.UsePipelineSettings; urp.requiresColorOption = CameraOverrideOption.UsePipelineSettings;
            var (d, _) = Region(on, off, W, H, 0f, 1f, 0f, 1f);
            // the comparison stage's camera (package H's recordings: position (0, 16.32, 39.13), Euler (12, 180, 0), FOV 30)
            var pose = (cam.transform.position, cam.transform.rotation, cam.fieldOfView);
            cam.transform.SetPositionAndRotation(new Vector3(0f, 16.3165f, 39.1259f), Quaternion.Euler(12f, 180f, 0f));
            cam.fieldOfView = 30f;
            Capture(cam, 1280, 720, $"map{n}_stage_on");
            urp.requiresDepthOption = CameraOverrideOption.Off; urp.requiresColorOption = CameraOverrideOption.Off;
            Capture(cam, 1280, 720, $"map{n}_stage_off");
            urp.requiresDepthOption = CameraOverrideOption.UsePipelineSettings; urp.requiresColorOption = CameraOverrideOption.UsePipelineSettings;
            cam.transform.SetPositionAndRotation(pose.position, pose.rotation);
            cam.fieldOfView = pose.fieldOfView;
            bool refracting = target.sharedMaterials.Any(m => m && m.shader && m.shader.name != "HS2Next/Effects/Ice/IceFront");
            Check($"P4.map{n}", !AnyNaN(on) && (!refracting || d > 0f),
                $"{target.name} ({string.Join(", ", target.sharedMaterials.Where(m => m && m.shader).Select(m => m.shader.name).Distinct())}) at {tb.center}: textures on vs off mean |d| {d * 255:F2} / 255 over the frame");
        }
    }
}
