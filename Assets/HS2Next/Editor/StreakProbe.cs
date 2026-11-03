// Measurement tool and check: streaks along the edge loops of curved skin and clothes, the Built-in originals
// against the URP replacements on the same assembled character. The visual targets of Tools/targets.json are built from
// their cards (rest pose), framed on the left hand, forearm, upper arm, thigh and the bottom clothes at several yaw / pitch
// offsets, lit by one directional light that follows the camera like Studio's chara light (local rotation (lx, ly, 0)
// under the camera, white, soft shadows, strength 1) and a flat ambient. Every view is rendered per case:
//   Built-in: Studio's quality settings (shadow distance 400, 4 cascades, very high resolution, close fit, near plane
//             offset 2) and the chara light's biases 0.1 / 0.3: bi (shadows on), bi_off (shadows off)
//   URP (the app's production asset): urp_off, urp_b<depth>_n<normal> (per-light biases), urp_sd<distance>_..., and the
//             normal-map-free urp_flat_* (every _BumpScale / _BumpScale2 / _DetailNormalMapScale of the drawn materials 0)
//   urp_chara: the app's chara-light biases (SceneCharaLight.CharaLightShadowBias / CharaLightShadowNormalBias).
// Output: <out>/<card>/<view>_y<yaw>_p<pitch>_l<lx>_<ly>_<case>.png and streak_probe.txt (cases, cameras, light).
//   Unity -batchmode -executeMethod StreakProbe.Run [-out <dir>] [-cases a,b,...] [-views a,b] [-light 0:0,30:45]
//         [-angles 0:0,-30:0] [-size 900] [-msaa 8] (graphics)
// Check (StreakProbe.RunCheck, graphics; report streak_check.txt, first line "SUMMARY: N PASS, M FAIL"): the cases bi_off,
// bi, urp_off, urp_chara; per card, over the views at angles (0, 0) and (0, 25):
//   streaks, light along the view (0, 0): the darkening d = shadow off - shadow on (luminance 0..255) on the foreground of
//     both images eroded by 3 px: its mean, the share above 4 and its high-frequency part mean |d - box9(d)|; URP must stay
//     within mean + 0.5, share + 2 %, high frequency + 0.25 of Built-in's (1.1.0's 0.1 / 0.3 gave + 2.3, + 12.6 %, + 0.70);
//   contacts, light (30, 45), the c_* views: the shadow masks d > 12 of both pipelines: IoU >= 0.70 and the leak (Built-in
//     shadow that URP lights) <= 5 % of Built-in's shadow.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Chara;
using HS2Next.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class StreakProbe
{
    static string Arg(string name, string def)
    {
        var a = argsOverride ?? Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    // (view, bone a, bone b (midpoint; null = a), base offset from the target, female only, arms down). The arms-down
    // views turn both upper arms 75 degrees down (about the body's forward axis) so the arms and hands touch the body:
    // the contact views for shadow leaking at contacts.
    static readonly (string view, string a, string b, Vector3 off, bool femaleOnly, bool down)[] Views =
    {
        ("handL_top", "cf_J_Hand_Middle01_L", null, new Vector3(0f, 3f, 1.4f), false, false),
        ("handL_front", "cf_J_Hand_Middle01_L", null, new Vector3(0f, 0.3f, 3.4f), false, false),
        ("forearmL_front", "cf_J_ArmLow01_L", "cf_J_Hand_L", new Vector3(0f, 0.3f, 4.2f), false, false),
        ("upperarmL_front", "cf_J_ArmUp00_L", "cf_J_ArmLow01_L", new Vector3(0f, 0.3f, 4.5f), false, false),
        ("thighL_front", "cf_J_LegUp00_L", "cf_J_LegLow01_L", new Vector3(0f, 0f, 5.5f), true, false),
        ("thighL_side", "cf_J_LegUp00_L", "cf_J_LegLow01_L", new Vector3(-5.5f, 0f, -0.3f), true, false),
        ("skirt_front", "cf_J_Hips", "cf_J_LegLow01_L", new Vector3(0f, 0f, 9f), true, false),
        ("skirt_side", "cf_J_Hips", "cf_J_LegLow01_L", new Vector3(-9f, 0f, 0f), true, false),
        ("c_neck_back", "cf_J_Neck", null, new Vector3(0f, 0.8f, -5f), false, false),
        ("c_torso_front", "cf_J_Spine02", null, new Vector3(0f, 0.3f, 7f), false, false),
        ("c_skirt_hem", "cf_J_LegUp00_L", null, new Vector3(-1.5f, 0.5f, 4.5f), true, false),
        ("c_armdown_side", "cf_J_ArmLow01_L", "cf_J_Hand_L", new Vector3(-6f, 0.5f, 1.5f), false, true),
        ("c_armdown_front", "cf_J_ArmLow01_L", null, new Vector3(0f, 0.3f, 6f), false, true),
        ("c_hand_hip", "cf_J_Hand_L", null, new Vector3(-2.5f, 0.6f, 2.2f), false, true),
    };

    static readonly (int yaw, int pitch)[] Angles = { (0, 0), (-30, 0), (30, 0), (0, 25), (-30, 25), (30, 25) };

    const string DefaultCases = "bi,bi_off,urp_off,urp_b0.1_n0.3,urp_b1_n1,urp_b0.5_n1,urp_b1_n2,urp_b2_n2,urp_sd400_b0.1_n0.3,urp_flat_b0.1_n0.3,urp_flat_off";

    public static void Run()
    {
        var prevDefault = GraphicsSettings.defaultRenderPipeline;
        var prevQuality = QualitySettings.renderPipeline;
        var q0 = (QualitySettings.shadowDistance, QualitySettings.shadowCascades, QualitySettings.shadowResolution, QualitySettings.shadowProjection,
            QualitySettings.shadowNearPlaneOffset, QualitySettings.shadowCascade4Split, QualitySettings.shadows);
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HS2Next.App.AppPipeline.ResolveAssetPath("production"));
        float urpDistance = urp ? urp.shadowDistance : 100f;
        bool prevAsync = ShaderUtil.allowAsyncCompilation;
        var sb = new StringBuilder();
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            RunCards(sb, urp);
        }
        catch (Exception e) { sb.AppendLine("exception: " + e); }
        finally
        {
            if (urp) urp.shadowDistance = urpDistance;
            GraphicsSettings.defaultRenderPipeline = prevDefault; QualitySettings.renderPipeline = prevQuality;
            QualitySettings.shadowDistance = q0.Item1; QualitySettings.shadowCascades = q0.Item2; QualitySettings.shadowResolution = q0.Item3;
            QualitySettings.shadowProjection = q0.Item4; QualitySettings.shadowNearPlaneOffset = q0.Item5; QualitySettings.shadowCascade4Split = q0.Item6;
            QualitySettings.shadows = q0.Item7;
            ShaderUtil.allowAsyncCompilation = prevAsync;
        }
        string outDir = Arg("-out", Path.Combine(HS2Next.Content.ContentSettings.ProjectRoot, "Spike", "out", "streaks"));
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "streak_probe.txt"), sb.ToString(), new UTF8Encoding(false));
        Debug.Log("STREAK PROBE DONE\n" + sb);
    }

    static void RunCards(StringBuilder sb, UniversalRenderPipelineAsset urp)
    {
        string outDir = Arg("-out", Path.Combine(HS2Next.Content.ContentSettings.ProjectRoot, "Spike", "out", "streaks"));
        Directory.CreateDirectory(outDir);
        var cases = Arg("-cases", DefaultCases).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        var onlyViews = Arg("-views", null)?.Split(',').ToHashSet();
        var lights = Arg("-light", "0:0").Split(',').Select(s => s.Split(':')).Select(p => new Vector2(float.Parse(p[0]), float.Parse(p[1]))).ToList();
        int size = int.Parse(Arg("-size", "900"));
        int msaa = int.Parse(Arg("-msaa", "8"));
        var angles = Arg("-angles", null) is string an
            ? an.Split(',').Select(s => s.Split(':')).Select(p => (int.Parse(p[0]), int.Parse(p[1]))).ToArray() : Angles;
        sb.AppendLine($"StreakProbe (Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceType}); URP asset {(urp ? urp.name : "-")}: depth bias {urp?.shadowDepthBias}, normal bias {urp?.shadowNormalBias}, res {urp?.mainLightShadowmapResolution}, distance {urp?.shadowDistance}, cascades {urp?.shadowCascadeCount}, soft {urp?.supportsSoftShadows}");
        sb.AppendLine($"cases {string.Join(",", cases)}; lights {string.Join(",", lights)}; size {size}; msaa {msaa}");
        var gd = CardResolveTest.BuildDb(sb);
        foreach (var path in TestTargets.Load().VisualCards)
        {
            var card = Card.Load(path);
            var asm = new CharaAssembler(gd);
            GameObject chara;
            try { chara = asm.BuildFromDocuments(card); }
            catch (Exception e) { sb.AppendLine($"== {card.Name}: BUILD FAILED {e}"); continue; }
            foreach (var smr in chara.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.forceMatrixRecalculationPerRender = true;
            bool female = !asm.IsMale;
            string cdir = Path.Combine(outDir, Safe(card.Name));
            Directory.CreateDirectory(cdir);
            sb.AppendLine($"== {card.Name} ({(female ? "female" : "male")})");
            var bones = chara.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var armL = bones.TryGetValue("cf_J_ArmUp00_L", out var al) ? al : null;
            var armR = bones.TryGetValue("cf_J_ArmUp00_R", out var ar) ? ar : null;
            var rotL = armL ? armL.rotation : Quaternion.identity;
            var rotR = armR ? armR.rotation : Quaternion.identity;
            void Pose(bool down)
            {
                if (armL) armL.rotation = down ? Quaternion.AngleAxis(75f, chara.transform.forward) * rotL : rotL;
                if (armR) armR.rotation = down ? Quaternion.AngleAxis(-75f, chara.transform.forward) * rotR : rotR;
            }
            var shotsBy = new Dictionary<bool, List<(string tag, Vector3 pos, Vector3 target)>>();
            foreach (bool down in new[] { false, true })
            {
                Pose(down);
                var shots = shotsBy[down] = new List<(string tag, Vector3 pos, Vector3 target)>();
                foreach (var v in Views)
                {
                    if (v.down != down || (onlyViews != null && !onlyViews.Contains(v.view))) continue;
                    if (v.femaleOnly && !female) continue;
                    if (!bones.TryGetValue(v.a, out var ta)) { sb.AppendLine($"   {v.view}: no bone {v.a}"); continue; }
                    var t = ta.position;
                    if (v.b != null && bones.TryGetValue(v.b, out var tb)) t = (t + tb.position) * 0.5f;
                    foreach (var (yaw, pitch) in angles)
                    {
                        var o = Rotate(v.off, yaw, pitch);
                        shots.Add(($"{v.view}_y{yaw:+0;-0;+0}_p{pitch}", t + o, t));
                    }
                }
                foreach (var s in shots) sb.AppendLine($"   view {s.tag}{(down ? " (arms down)" : "")}: camera {s.pos:F3} -> {s.target:F3}");
            }
            Pose(false);
            void Cases(UniversalRenderPipelineAsset rp, IEnumerable<string> names)
            {
                foreach (var c in names)
                    foreach (bool down in new[] { false, true })
                    {
                        if (shotsBy[down].Count == 0) continue;
                        Pose(down);
                        RenderCase(chara, c, rp, shotsBy[down], lights, cdir, size, msaa, sb);
                        Pose(false);
                    }
            }
            // Built-in cases first (the bundle materials as loaded), then the URP swap on the character's copies.
            Cases(null, cases.Where(c => c.StartsWith("bi")));
            if (cases.Any(c => c.StartsWith("urp")))
            {
                int swapped = HS2Next.ShaderMap.ApplyUrp(chara);
                sb.AppendLine($"   URP: swapped {swapped} materials");
                Cases(urp, cases.Where(c => c.StartsWith("urp")));
            }
            Object.DestroyImmediate(chara);
        }
        gd.UnloadAll();
        GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null;
    }

    static Vector3 Rotate(Vector3 off, float yaw, float pitch)
    {
        float r = off.magnitude;
        float az = Mathf.Atan2(off.x, off.z) + yaw * Mathf.Deg2Rad;
        float el = Mathf.Clamp(Mathf.Atan2(off.y, new Vector2(off.x, off.z).magnitude) + pitch * Mathf.Deg2Rad, -85 * Mathf.Deg2Rad, 85 * Mathf.Deg2Rad);
        return new Vector3(r * Mathf.Cos(el) * Mathf.Sin(az), r * Mathf.Sin(el), r * Mathf.Cos(el) * Mathf.Cos(az));
    }

    // Case grammar: bi | bi_off | urp_off | urp[_flat][_sd<d>]_b<depth>_n<normal> | urp_flat_off | urp_pipeline
    static void RenderCase(GameObject chara, string c, UniversalRenderPipelineAsset urp, List<(string tag, Vector3 pos, Vector3 target)> shots,
                           List<Vector2> lights, string cdir, int size, int msaa, StringBuilder sb)
    {
        bool builtin = urp == null;
        var parts = c.Split('_');
        bool off = parts.Contains("off");
        bool flat = parts.Contains("flat");
        float bias = 0.1f, normal = 0.3f, distance = builtin ? 400f : 100f;
        bool pipeline = parts.Contains("pipeline");
        if (parts.Contains("chara"))
        {
            bias = HS2Next.Scenes.Runtime.SceneCharaLight.CharaLightShadowBias;
            normal = HS2Next.Scenes.Runtime.SceneCharaLight.CharaLightShadowNormalBias;
        }
        foreach (var p in parts)
        {
            if (p.Length > 1 && p[0] == 'b' && float.TryParse(p.Substring(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b)) bias = b;
            else if (p.Length > 1 && p[0] == 'n' && float.TryParse(p.Substring(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nb)) normal = nb;
            else if (p.StartsWith("sd") && float.TryParse(p.Substring(2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) distance = d;
        }
        var bumps = flat ? FlattenNormals(chara) : null;
        GraphicsSettings.defaultRenderPipeline = urp; QualitySettings.renderPipeline = urp;
        if (builtin)
        {
            QualitySettings.shadows = UnityEngine.ShadowQuality.All;
            QualitySettings.shadowDistance = distance; QualitySettings.shadowCascades = 4; QualitySettings.shadowResolution = UnityEngine.ShadowResolution.VeryHigh;
            QualitySettings.shadowProjection = ShadowProjection.CloseFit; QualitySettings.shadowNearPlaneOffset = 2f;
            QualitySettings.shadowCascade4Split = new Vector3(0.067f, 0.2f, 0.467f);
        }
        else urp.shadowDistance = distance;
        var amb = new Color(0.42f, 0.42f, 0.45f);
        var sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(amb.linear);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = amb; RenderSettings.ambientProbe = sh;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = ShaderProbe.BlackCube; RenderSettings.reflectionIntensity = 0;
        var cam = new GameObject("cam").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        cam.fieldOfView = 23; cam.nearClipPlane = 0.08f; cam.farClipPlane = 15000;
        var key = new GameObject("key").AddComponent<Light>();
        key.type = LightType.Directional; key.color = Color.white; key.intensity = 1f;
        key.shadows = off ? LightShadows.None : LightShadows.Soft; key.shadowStrength = 1f;
        key.transform.SetParent(cam.transform, false);
        HS2Next.RenderingLayers.ApplyToLight(key);
        if (!builtin)
        {
            var ld = key.GetComponent<UniversalAdditionalLightData>() ?? key.gameObject.AddComponent<UniversalAdditionalLightData>();
            ld.usePipelineSettings = pipeline;
            var cd = cam.GetUniversalAdditionalCameraData();
            cd.renderShadows = true;
        }
        if (!pipeline) { key.shadowBias = bias; key.shadowNormalBias = normal; }
        RenderSettings.sun = key;
        int n = 0;
        foreach (var lr in lights)
            foreach (var s in shots)
            {
                cam.transform.position = s.pos;
                cam.transform.LookAt(s.target);
                key.transform.localRotation = Quaternion.Euler(lr.x, lr.y, 0f);
                var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = msaa };
                cam.targetTexture = rt;
                cam.Render(); cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0); tex.Apply();
                RenderTexture.active = null; cam.targetTexture = null;
                File.WriteAllBytes(Path.Combine(cdir, $"{s.tag}_l{lr.x:0}_{lr.y:0}_{c}.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
                n++;
            }
        sb.AppendLine($"   case {c}: {(builtin ? "Built-in" : "URP")}, shadows {(off ? "off" : "soft")}, bias {(pipeline ? "pipeline" : bias + " / " + normal)}, distance {distance}, flat {flat}: {n} images");
        RenderSettings.sun = null;
        Object.DestroyImmediate(key.gameObject); Object.DestroyImmediate(cam.gameObject);
        if (bumps != null) foreach (var (m, p, v) in bumps) m.SetFloat(p, v);
    }

    static readonly string[] BumpProps = { "_BumpScale", "_BumpScale2", "_DetailNormalMapScale", "_NormalScale", "_BumpScale3" };

    /// <summary>Sets every normal-map scale of the drawn materials to 0; returns the old values.</summary>
    static List<(Material, string, float)> FlattenNormals(GameObject chara)
    {
        var old = new List<(Material, string, float)>();
        var seen = new HashSet<Material>();
        foreach (var r in chara.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
            {
                if (!m || !seen.Add(m)) continue;
                var kw = m.shaderKeywords;   // the Editor's toggle drawers may change keywords on the first property access
                foreach (var p in BumpProps)
                    if (m.HasProperty(p)) { old.Add((m, p, m.GetFloat(p))); m.SetFloat(p, 0f); }
                m.shaderKeywords = kw;
            }
        return old;
    }

    // ------------------------------------------------------------------ check

    public static void RunCheck()
    {
        string outDir = Arg("-out", Path.Combine(HS2Next.Content.ContentSettings.ProjectRoot, "Spike", "out", "streaks", "check"));
        var extra = new List<string>(Environment.GetCommandLineArgs());
        void Set(string k, string v) { int i = extra.IndexOf(k); if (i >= 0 && i + 1 < extra.Count) extra[i + 1] = v; else { extra.Add(k); extra.Add(v); } }
        Set("-out", outDir); Set("-cases", "bi_off,bi,urp_off,urp_chara"); Set("-angles", "0:0,0:25"); Set("-light", "0:0,30:45");
        argsOverride = extra.ToArray();
        var sb = new StringBuilder();
        int pass = 0, fail = 0;
        try
        {
            Run();
            foreach (var cdir in Directory.GetDirectories(outDir).OrderBy(d => d, StringComparer.Ordinal))
            {
                string card = Path.GetFileName(cdir);
                var streak = new List<(double dark, double cov, double hf)>[] { new List<(double, double, double)>(), new List<(double, double, double)>() };
                var contact = new List<(double iou, double leak)>();
                foreach (var fOff in Directory.GetFiles(cdir, "*_l0_0_bi_off.png").OrderBy(f => f, StringComparer.Ordinal))
                {
                    string stem = fOff.Substring(0, fOff.Length - "_bi_off.png".Length);
                    if (Path.GetFileName(stem).StartsWith("c_")) continue;
                    var (b0, m0) = Lum(fOff); var (b1, m1) = Lum(stem + "_bi.png");
                    var (u0, n0) = Lum(stem + "_urp_off.png"); var (u1, n1) = Lum(stem + "_urp_chara.png");
                    streak[0].Add(Darkening(b0, b1, And(m0, m1)));
                    streak[1].Add(Darkening(u0, u1, And(n0, n1)));
                }
                foreach (var fOff in Directory.GetFiles(cdir, "c_*_l30_45_bi_off.png").OrderBy(f => f, StringComparer.Ordinal))
                {
                    string stem = fOff.Substring(0, fOff.Length - "_bi_off.png".Length);
                    var (b0, m0) = Lum(fOff); var (b1, m1) = Lum(stem + "_bi.png");
                    var (u0, n0) = Lum(stem + "_urp_off.png"); var (u1, _) = Lum(stem + "_urp_chara.png");
                    var m = And(And(m0, m1), n0);
                    int bs = 0, both = 0, union = 0;
                    for (int i = 0; i < m.Length; i++)
                    {
                        if (!m[i]) continue;
                        bool sb1 = b0[i] - b1[i] > 12, su = u0[i] - u1[i] > 12;
                        if (sb1) bs++;
                        if (sb1 && su) both++;
                        if (sb1 || su) union++;
                    }
                    if (bs > 0) contact.Add((both / (double)Math.Max(1, union), (bs - both) / (double)bs));
                }
                double M(IEnumerable<double> x) => x.Any() ? x.Average() : 0;
                var bi = (M(streak[0].Select(x => x.dark)), M(streak[0].Select(x => x.cov)), M(streak[0].Select(x => x.hf)));
                var ur = (M(streak[1].Select(x => x.dark)), M(streak[1].Select(x => x.cov)), M(streak[1].Select(x => x.hf)));
                bool okS = streak[1].Count > 0 && ur.Item1 <= bi.Item1 + 0.5 && ur.Item2 <= bi.Item2 + 0.02 && ur.Item3 <= bi.Item3 + 0.25;
                sb.AppendLine($"{(okS ? "PASS" : "FAIL")}  {card} streaks ({streak[1].Count} views, light along the view): darkening Built-in {bi.Item1:F3} / {bi.Item2:P2} / hf {bi.Item3:F3}, URP {ur.Item1:F3} / {ur.Item2:P2} / hf {ur.Item3:F3} (limits + 0.5, + 2 %, + 0.25)");
                if (okS) pass++; else fail++;
                double iou = M(contact.Select(x => x.iou)), leak = M(contact.Select(x => x.leak));
                bool okC = contact.Count > 0 && iou >= 0.70 && leak <= 0.05;
                sb.AppendLine($"{(okC ? "PASS" : "FAIL")}  {card} contacts ({contact.Count} views, light (30, 45)): IoU {iou:F3} (>= 0.70), leak {leak:P2} (<= 5 %)");
                if (okC) pass++; else fail++;
            }
        }
        catch (Exception e) { sb.AppendLine("FAIL  exception: " + e); fail++; }
        finally { argsOverride = null; }
        string head = $"SUMMARY: {pass} PASS, {fail} FAIL (chara light biases {HS2Next.Scenes.Runtime.SceneCharaLight.CharaLightShadowBias} / {HS2Next.Scenes.Runtime.SceneCharaLight.CharaLightShadowNormalBias})";
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "streak_check.txt"), head + "\n" + sb, new UTF8Encoding(false));
        Debug.Log("STREAK CHECK DONE\n" + head + "\n" + sb);
        if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
    }

    static string[] argsOverride;

    static bool[] And(bool[] a, bool[] b) { var r = new bool[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = a[i] && b[i]; return r; }

    /// <summary>Luminance (0..255) of a PNG and its foreground (any channel more than 6 from pixel (2, 2)), eroded by 3 px.</summary>
    static (float[] lum, bool[] mask) Lum(string path)
    {
        var t = new Texture2D(2, 2);
        t.LoadImage(File.ReadAllBytes(path));
        int w = t.width, h = t.height;
        var px = t.GetPixels32();
        Object.DestroyImmediate(t);
        var bg = px[2 * w + 2];
        var l = new float[px.Length];
        var m = new bool[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            l[i] = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            m[i] = Math.Abs(c.r - bg.r) > 6 || Math.Abs(c.g - bg.g) > 6 || Math.Abs(c.b - bg.b) > 6;
        }
        for (int it = 0; it < 3; it++)
        {
            var e = new bool[m.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    e[i] = m[i] && (x == 0 || m[i - 1]) && (x == w - 1 || m[i + 1]) && (y == 0 || m[i - w]) && (y == h - 1 || m[i + w]);
                }
            m = e;
        }
        imageWidth = w;
        return (l, m);
    }

    static int imageWidth;   // every image of a run has the same size

    /// <summary>The darkening d = max(0, a - b) on the mask: mean, share above 4, mean |d - box9(d)| (edges clamped).</summary>
    static (double dark, double cov, double hf) Darkening(float[] a, float[] b, bool[] mask)
    {
        int w = imageWidth, h = a.Length / w;
        var d = new double[a.Length];
        for (int i = 0; i < a.Length; i++) d[i] = Math.Max(0f, a[i] - b[i]);
        // 9 x 9 box with clamped edges: pad by replication into an integral image
        int r = 4, W = w + 2 * r, H = h + 2 * r;
        var ii = new double[(W + 1) * (H + 1)];
        for (int y = 0; y < H; y++)
        {
            int sy = Math.Min(h - 1, Math.Max(0, y - r));
            double row = 0;
            for (int x = 0; x < W; x++)
            {
                int sx = Math.Min(w - 1, Math.Max(0, x - r));
                row += d[sy * w + sx];
                ii[(y + 1) * (W + 1) + x + 1] = ii[y * (W + 1) + x + 1] + row;
            }
        }
        double sum = 0, hf = 0; int n = 0, n4 = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!mask[i]) continue;
                int x0 = x, y0 = y, x1 = x + 2 * r + 1, y1 = y + 2 * r + 1;
                double box = (ii[y1 * (W + 1) + x1] - ii[y0 * (W + 1) + x1] - ii[y1 * (W + 1) + x0] + ii[y0 * (W + 1) + x0]) / 81.0;
                sum += d[i]; hf += Math.Abs(d[i] - box); n++; if (d[i] > 4) n4++;
            }
        return n == 0 ? (0, 0, 0) : (sum / n, n4 / (double)n, hf / n);
    }

    static string Safe(string s) => string.Concat(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
}
