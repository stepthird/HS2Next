// Batch test: assemble characters from cards and render them (front / 3-quarter) under
// Built-in, optionally also URP with shader replacement.
// Run: -executeMethod CharaRenderTest.Run [-card <png> | -cards <dir> | -male | -targets] [-limit N] [-urp [-diff] [-unfit "AIT/Skin True;..."] [-urpAsset spike|production|<path>]]
// Default: the first -limit (3) cards of <game>/UserData/chara/female (top level). -cards <dir>: the
// first -limit cards of <dir>, searched recursively and sorted by relative path; -male is
// -cards <game>/UserData/chara/male. Report: render_report.txt, or render_report_male.txt when a male card was built.
// -targets: the visual target cards of Tools/targets.json (TestTargets: BR-Chan, the first male card); report
// render_report_targets.txt.
// -diff: per card and view, RMS / mean |d| (linear, foreground only) between the Built-in and URP
// PNGs, a x4 difference PNG (<card>_diffx4_<view>.png) and the shaders still on the URP/Lit fallback.
// -unfit: remove originals from ShaderMap.Fitted for this run (to measure a baseline).
// -urpAsset: the URP asset of the URP renders (default the measurement asset, the measurement baseline; "production" = the app's
// asset, Forward+ with rendering layers and 4096 / 4-cascade soft shadows; AppPipeline.ResolveAssetPath).
// -noext: build without the card plugin data (CharaAssembler.ExtOptions = null; Runtime/Chara/Ext), for before/after comparisons.
// -notex: plugin data on, but without the texture stage (skin layers incl. KSOX and the uncensor overlay, KCOX, MaterialEditor).
// Male cards with a beard (face draw _Texture5 set): the face view is also rendered without the beard
// (<card>_<pipeline>_nobeard_face.png) and the beard's footprint is compared between the pipelines.
using System;
using System.Diagnostics;
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

public static class CharaRenderTest
{
    static string Out => CardResolveTest.OutDir;

    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    public static void Run()
    {
        // -urp renders switch the render pipeline; the project's own comes back in every case (an exception too), so the
        // Editor never saves a test's asset into GraphicsSettings / QualitySettings at exit
        var prevDefault = GraphicsSettings.defaultRenderPipeline;
        var prevQuality = QualitySettings.renderPipeline;
        try { RunCards(); }
        finally { GraphicsSettings.defaultRenderPipeline = prevDefault; QualitySettings.renderPipeline = prevQuality; }
    }

    static void RunCards()
    {
        ShaderUtil.allowAsyncCompilation = false;   // no cyan placeholder shaders in the first URP frames
        Directory.CreateDirectory(Out);
        var sb = new StringBuilder();
        var gd = CardResolveTest.BuildDb(sb);
        bool maleDir = Environment.GetCommandLineArgs().Contains("-male");
        string cardsDir = maleDir ? Path.Combine(CardResolveTest.GameRoot, @"UserData\chara\male") : Arg("-cards", null);
        int limit = int.Parse(Arg("-limit", "3"));
        bool targets = Environment.GetCommandLineArgs().Contains("-targets");
        var cards = targets ? TestTargets.Load().VisualCards
            : Arg("-card", null) is string one ? new[] { one }
            : cardsDir != null ? Directory.GetFiles(cardsDir, "*.png", SearchOption.AllDirectories)
                .OrderBy(f => f.Substring(cardsDir.Length).Replace('\\', '/'), StringComparer.Ordinal).Take(limit).ToArray()
            : Directory.GetFiles(Path.Combine(CardResolveTest.GameRoot, @"UserData\chara\female"), "*.png").Take(limit).ToArray();
        bool urp = Environment.GetCommandLineArgs().Contains("-urp");
        bool diff = Environment.GetCommandLineArgs().Contains("-diff");
        bool noExt = Environment.GetCommandLineArgs().Contains("-noext");
        if (noExt) sb.AppendLine("(extensions off: -noext)");
        bool noTex = Environment.GetCommandLineArgs().Contains("-notex");
        if (noTex) sb.AppendLine("(texture stage off: -notex)");
        // -unfit "A;B": drop these originals from the fitted map for this run (baseline comparisons).
        if (Arg("-unfit", null) is string unfit)
            foreach (var s in unfit.Split(';')) if (HS2Next.ShaderMap.Fitted.Remove(s.Trim())) sb.AppendLine($"(unfit {s.Trim()})");
        if (Environment.GetCommandLineArgs().Contains("-dumpRT")) { TexCreator.DebugDir = Path.Combine(Out, "rt"); Directory.CreateDirectory(TexCreator.DebugDir); }
        bool anyMale = false;
        foreach (var path in cards)
        {
            var card = Card.Load(path);
            var sw = Stopwatch.StartNew();
            var asm = new CharaAssembler(gd);
            if (noExt) asm.ExtOptions = null;
            else if (noTex) { asm.ExtOptions.SkinOverlays = false; asm.ExtOptions.ClothesOverlays = false; asm.ExtOptions.MaterialEditor = false; }
            GameObject chara;
            try { chara = asm.BuildFromDocuments(card); }
            catch (Exception e) { sb.AppendLine($"== {card.Name}: BUILD FAILED {e}"); continue; }
            sb.AppendLine($"== {card.Name}: built in {sw.ElapsedMilliseconds} ms");
            foreach (var l in asm.Log) sb.AppendLine("   " + l);
            if (!noExt && asm.Context != null)
            {
                sb.AppendLine("   ext: " + asm.Context.Stats.Summary());
                if (asm.Context.Bones != null) foreach (var m in asm.Context.Bones.Modifiers) sb.AppendLine("   bone modifier " + m);
            }
            string name = Safe(Path.GetFileNameWithoutExtension(path));
            RenderViews(chara, Path.Combine(Out, name + "_builtin"), null);
            anyMale |= asm.IsMale;
            var beardMat = asm.IsMale ? BeardMaterial(chara) : null;
            if (beardMat) RenderWithoutBeard(chara, beardMat, Path.Combine(Out, name + "_builtin_nobeard"), null);
            if (urp)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HS2Next.App.AppPipeline.ResolveAssetPath(Arg("-urpAsset", null)));
                if (Arg("-urpAsset", null) != null) sb.AppendLine("   URP asset: " + (asset ? AssetDatabase.GetAssetPath(asset) : "NOT FOUND"));
                var fallback = FallbackShaders(chara);
                int swapped = HS2Next.ShaderMap.ApplyUrp(chara);
                sb.AppendLine($"   URP: swapped {swapped} materials");
                RenderViews(chara, Path.Combine(Out, name + "_urp"), asset);
                // ApplyUrp swaps in material copies: take the beard material from the renderer again.
                if (beardMat) beardMat = BeardMaterial(chara);
                if (beardMat) RenderWithoutBeard(chara, beardMat, Path.Combine(Out, name + "_urp_nobeard"), asset);
                if (diff)
                {
                    sb.AppendLine("   fallback (URP/Lit) shaders: " + (fallback.Count == 0 ? "none" : string.Join(", ", fallback.Select(kv => $"{kv.Key} x{kv.Value}"))));
                    foreach (var v in new[] { "face", "full", "full34" })
                        sb.AppendLine("   diff " + DiffViews(Path.Combine(Out, name), v));
                    if (beardMat) sb.AppendLine("   beard " + BeardFootprint(Path.Combine(Out, name)));
                }
            }
            Object.DestroyImmediate(chara);
        }
        foreach (var l in gd.Log.Where(l => !l.StartsWith("vanilla") && !l.StartsWith("mod list"))) sb.AppendLine(l);
        File.WriteAllText(Path.Combine(Out, targets ? "render_report_targets.txt" : anyMale ? "render_report_male.txt" : "render_report.txt"), sb.ToString());
        gd.UnloadAll();
        GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null;
        Debug.Log("CHARA TEST DONE");
    }

    /// <summary>Renders the views full / full34 / face of a character (Built-in when rp is null) into "<prefix>_<view>.png";
    /// with <paramref name="inMemory"/> each view's read-back goes to it instead and no file is written.</summary>
    public static void RenderViews(GameObject chara, string prefix, RenderPipelineAsset rp, string onlyView = null,
                                   System.Action<string, Texture2D> inMemory = null)
    {
        GraphicsSettings.defaultRenderPipeline = rp; QualitySettings.renderPipeline = rp;
        var amb = new Color(0.42f, 0.42f, 0.45f);
        var sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(amb.linear);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = amb;
        RenderSettings.ambientProbe = sh;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = ShaderProbe.BlackCube; RenderSettings.reflectionIntensity = 0;
        var key = new GameObject("key").AddComponent<Light>();
        key.type = LightType.Directional; key.intensity = 1.0f; key.shadows = LightShadows.Soft;
        key.transform.rotation = Quaternion.Euler(30, 200, 0);
        HS2Next.RenderingLayers.ApplyToLight(key);   // all layers, also under an asset with rendering layers
        var b = new Bounds(chara.transform.position, Vector3.zero);
        foreach (var r in chara.GetComponentsInChildren<Renderer>()) if (r.enabled && r.gameObject.activeInHierarchy) b.Encapsulate(r.bounds);
        var cam = new GameObject("cam").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.30f, 0.32f, 0.36f);
        cam.fieldOfView = 24; cam.nearClipPlane = 0.5f; cam.farClipPlane = 1000;
        float h = Mathf.Max(b.size.y, 1);
        float d = h / (2 * Mathf.Tan(12 * Mathf.Deg2Rad)) * 1.08f;
        var views = new (string, float, Vector3, float)[]
        {
            ("full", 0, b.center, d), ("full34", 35, b.center, d),
            ("face", 0, FaceTarget(chara), d * 0.16f),
        };
        foreach (var (vn, yaw, target, dist) in views)
        {
            if (onlyView != null && vn != onlyView) continue;
            var dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;   // HS2 faces +Z
            cam.transform.position = target + dir * dist;
            cam.transform.LookAt(target);
            var rt = new RenderTexture(900, 1200, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render(); cam.Render(); // first frame after a pipeline switch can be empty
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null;
            if (inMemory != null) inMemory(vn, tex);
            else File.WriteAllBytes($"{prefix}_{vn}.png", tex.EncodeToPNG());
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }
        Object.DestroyImmediate(cam.gameObject); Object.DestroyImmediate(key.gameObject);
    }

    // Shaders that ShaderMap.ApplyUrp will send to the URP/Lit fallback, with material counts.
    static System.Collections.Generic.SortedDictionary<string, int> FallbackShaders(GameObject chara)
    {
        var d = new System.Collections.Generic.SortedDictionary<string, int>();
        var seen = new System.Collections.Generic.HashSet<Material>();
        foreach (var r in chara.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
            {
                if (!m || !m.shader || !seen.Add(m)) continue;
                string n = m.shader.name;
                if (HS2Next.ShaderMap.TryResolve(m.shader, out _) || n.StartsWith("Universal Render Pipeline/") || n.StartsWith("HS2Next/")) continue;
                d[n] = d.TryGetValue(n, out var k) ? k + 1 : 1;
            }
        return d;
    }

    // Built-in vs URP PNG of one view: RMS and mean |d| in linear 0..1 over foreground pixels
    // (pixels differing from the background colour in either image); writes a x4 difference PNG.
    static string DiffViews(string prefix, string view)
    {
        string fa = $"{prefix}_builtin_{view}.png", fb = $"{prefix}_urp_{view}.png";
        var a = new Texture2D(2, 2); var b = new Texture2D(2, 2);
        a.LoadImage(File.ReadAllBytes(fa)); b.LoadImage(File.ReadAllBytes(fb));
        var pa = a.GetPixels32(); var pb = b.GetPixels32();
        var bg = pa[0];
        float Lin(byte v) => Mathf.GammaToLinearSpace(v / 255f);
        bool Bg(Color32 c) => Math.Abs(c.r - bg.r) <= 2 && Math.Abs(c.g - bg.g) <= 2 && Math.Abs(c.b - bg.b) <= 2;
        double sq = 0, ab = 0; int n = 0;
        var pd = new Color32[pa.Length];
        for (int i = 0; i < pa.Length; i++)
        {
            float dr = Lin(pa[i].r) - Lin(pb[i].r), dg = Lin(pa[i].g) - Lin(pb[i].g), db = Lin(pa[i].b) - Lin(pb[i].b);
            pd[i] = new Color32((byte)Mathf.Min(255, Math.Abs(dr) * 4 * 255), (byte)Mathf.Min(255, Math.Abs(dg) * 4 * 255), (byte)Mathf.Min(255, Math.Abs(db) * 4 * 255), 255);
            if (Bg(pa[i]) && Bg(pb[i])) continue;
            n++; sq += (dr * dr + dg * dg + db * db) / 3; ab += (Math.Abs(dr) + Math.Abs(dg) + Math.Abs(db)) / 3;
        }
        var d = new Texture2D(a.width, a.height, TextureFormat.RGBA32, false);
        d.SetPixels32(pd); d.Apply();
        File.WriteAllBytes($"{prefix}_diffx4_{view}.png", d.EncodeToPNG());
        Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(d);
        return $"{view,-6}: foreground px {n}, RMS {Math.Sqrt(sq / Math.Max(1, n)):F4}, mean |d| {ab / Math.Max(1, n):F4} (linear)";
    }

    // Face draw material (o_head slot 0) when it carries a beard texture (male spec section 3.5), else null.
    static Material BeardMaterial(GameObject chara)
    {
        var r = CharaAssembler.Find(chara.transform, "o_head")?.GetComponent<Renderer>();
        var m = r ? r.sharedMaterial : null;
        return m && m.HasProperty("_Texture5") && m.GetTexture("_Texture5") ? m : null;
    }

    static void RenderWithoutBeard(GameObject chara, Material m, string prefix, RenderPipelineAsset rp)
    {
        var t = m.GetTexture("_Texture5");
        m.SetTexture("_Texture5", null);
        RenderViews(chara, prefix, rp, "face");
        m.SetTexture("_Texture5", t);
    }

    // Beard footprint per pipeline: face pixels that change by more than 2/255 (any channel) when the
    // beard texture is removed, the mean linear |d| over them, and the overlap (IoU) of the two
    // footprints; plus the Built-in vs URP difference inside the union of the footprints.
    static string BeardFootprint(string prefix)
    {
        Color32[] Load(string f) { var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(f)); var p = t.GetPixels32(); Object.DestroyImmediate(t); return p; }
        var bi = Load($"{prefix}_builtin_face.png"); var bi0 = Load($"{prefix}_builtin_nobeard_face.png");
        var ur = Load($"{prefix}_urp_face.png"); var ur0 = Load($"{prefix}_urp_nobeard_face.png");
        float Lin(byte v) => Mathf.GammaToLinearSpace(v / 255f);
        float D(Color32 a, Color32 b) => (Math.Abs(Lin(a.r) - Lin(b.r)) + Math.Abs(Lin(a.g) - Lin(b.g)) + Math.Abs(Lin(a.b) - Lin(b.b))) / 3;
        bool Ch(Color32 a, Color32 b) => Math.Abs(a.r - b.r) > 2 || Math.Abs(a.g - b.g) > 2 || Math.Abs(a.b - b.b) > 2;
        int nb = 0, nu = 0, both = 0, union = 0; double sb = 0, su = 0, sx = 0;
        for (int i = 0; i < bi.Length; i++)
        {
            bool b = Ch(bi[i], bi0[i]), u = Ch(ur[i], ur0[i]);
            if (b) { nb++; sb += D(bi[i], bi0[i]); }
            if (u) { nu++; su += D(ur[i], ur0[i]); }
            if (b && u) both++;
            if (b || u) { union++; sx += D(bi[i], ur[i]); }
        }
        return $"face: built-in {nb} px (mean |d| {sb / Math.Max(1, nb):F4}), URP {nu} px (mean |d| {su / Math.Max(1, nu):F4}), " +
               $"IoU {both / (double)Math.Max(1, union):F3}, built-in vs URP inside the union mean |d| {sx / Math.Max(1, union):F4} (linear)";
    }

    static Vector3 FaceTarget(GameObject chara)
    {
        var t = CharaAssembler.Find(chara.transform, "cf_J_Head");
        return t ? t.position + Vector3.up * 0.6f : chara.transform.position + Vector3.up * 15f;
    }

    static string Safe(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
}
