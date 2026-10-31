// EffectsTest (Phase 3.3 package B): the screen-effect core.
//   & Tools\unity_batch.ps1 -Method EffectsTest.Run [-MethodArgs @('-only','EF2,EF3')]   -> Spike/out/effects/effects_report.txt
//   & Tools\unity_batch.ps1 -Method EffectsTest.RunRender -NoQuit                          -> Spike/out/effects/effects_render_report.txt
//   & Tools\unity_batch.ps1 -Method EffectsTest.RunPlay -NoQuit                            -> Spike/out/effects/effects_play_report.txt
//   & Tools\unity_batch.ps1 -Method EffectsTest.CreateTemplate                             (writes Resources/HS2NextEffects.asset once)
// Run (edit mode; EF2's strips need a graphics device — bundle textures are read back from the GPU):
//   EF0 the template profile Resources/HS2NextEffects has the five overrides (ColorLookup, Tonemapping, ColorAdjustments,
//       Bloom, Vignette) and the runtime volume copies it without touching the asset
//   EF1 routing: the first Apply applies blocks 1–10 in HS2's order; each `effects.<block>.*` path marks only its block;
//       several blocks in one batch apply in HS2's order; a scene change of another root marks nothing
//   EF2 LUT bake: the neutral grade at neutral inputs is the grid bit-exactly; the base grade with every case of package H's
//       Cache/effects/grade_expect_<hash>.json (14 full strips, 64 filter cases at 512 nodes): max |d| <= 0.5/255, mean
//       <= 0.1/255; the bake time; the strip texture's layout (linear RGBAHalf, bilinear, clamped)
//   EF3 BaseGrade: the reader's constants equal the reference file's (raw volume values, lift / invGamma / gain, colour
//       balance, the 8 curve tables); a truncated and a changed sharedassets2.assets fail closed (neutral, reason); the
//       runtime reports effects.baseGradeUnavailable once when the reader failed
//   EF4 bloom mapping golden values (fix round fit); vignette, probe, self shadow and fog state writes; the AO control
//       (HS2AoFeature added to / found in the production renderer, URP's SSAO off meanwhile, release restores both)
//   EF5 focus / caster binding: a folder (holder position), an item, BR-Chan (eye midpoint), none (the camera pivot),
//       an unknown id (pivot, "missing"); delete clears the references in the same command
//   EF6 effects.reset = ScreenEffectDefine's defaults (DefaultBlock = a new block); undo / redo of a set and of a reset
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.App.Commands;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static partial class EffectsTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "effects");
    static StringBuilder rep;
    static int pass, fail;
    static string[] only;

    static void Begin() { rep = new StringBuilder(); pass = fail = 0; }
    static void Section(string s) => rep.AppendLine("\n== " + s);
    static void Info(string s) => rep.AppendLine("   " + s);
    static void Check(string what, bool ok, string detail = "")
    {
        if (ok) pass++; else fail++;
        rep.AppendLine($"   {(ok ? "PASS" : "FAIL")} {what}{(string.IsNullOrEmpty(detail) ? "" : " - " + detail)}");
    }
    static void End(string file, string title, Stopwatch sw)
    {
        Directory.CreateDirectory(OutDir);
        rep.Insert(0, $"SUMMARY: {pass} PASS, {fail} FAIL\n{title} ({sw.Elapsed.TotalSeconds:F0} s)\n");
        File.WriteAllText(Path.Combine(OutDir, file), rep.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        Debug.Log($"{title}: {pass} PASS, {fail} FAIL -> {Path.Combine(OutDir, file)}");
    }
    static bool Want(string id) => only == null || only.Contains(id);
    static string F(double v) => v.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);

    static string[] ArgList(string name)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1].Split(',', '+', ';').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray() : null;
    }

    static RuntimeServices services;
    static RuntimeServices Services()
    {
        if (services != null) return services;
        var log = new StringBuilder();
        services = SceneRuntimeTest.NewServices(log);
        return services;
    }

    /// <summary>A session and a runtime with the standard parts (no drivers).</summary>
    sealed class Rig : IDisposable
    {
        public SceneSession Session;
        public SceneRuntime Runtime;
        public SceneEffects Fx => Runtime.GetPart<SceneEffects>();
        public Rig(RuntimeServices s)
        {
            Session = SceneRuntimeTest.NewSession(s);
            Runtime = new SceneRuntime(Session, s, null, false).AddStandardParts();
            Runtime.Apply();
            Runtime.LateApply();
        }
        public CommandResult Set(string path, object v)
        {
            var r = Session.Set(null, path, v);
            if (r == null || r.Status == CommandStatus.Error) Check("set " + path, false, r?.ToString() ?? "null");
            return r;
        }
        public void Dispose() { Runtime?.Dispose(); Runtime = null; }
    }

    // ------------------------------------------------------------------ template

    public const string TemplatePath = "Assets/HS2Next/Resources/HS2NextEffects.asset";

    /// <summary>Writes the template profile (every override present, all parameters at their defaults, not overridden).</summary>
    public static void CreateTemplate()
    {
        int code = 0;
        try
        {
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(TemplatePath);
            if (existing) AssetDatabase.DeleteAsset(TemplatePath);
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            p.name = "HS2NextEffects";
            AssetDatabase.CreateAsset(p, TemplatePath);
            void Add<T>() where T : VolumeComponent
            {
                var c = p.Add<T>(false);
                c.name = typeof(T).Name;
                c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(c, p);
            }
            Add<ColorLookup>(); Add<Tonemapping>(); Add<ColorAdjustments>(); Add<UnityEngine.Rendering.Universal.Bloom>(); Add<Vignette>();
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("EffectsTest.CreateTemplate: wrote " + TemplatePath + " with " + p.components.Count + " overrides");
        }
        catch (Exception e) { Debug.LogError("EffectsTest.CreateTemplate failed: " + e); code = 1; }
        EditorApplication.Exit(code);
    }

    // ------------------------------------------------------------------ Run

    public static void Run()
    {
        Begin();
        only = ArgList("-only");
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"EffectsTest (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, pipeline {(GraphicsSettings.currentRenderPipeline ? GraphicsSettings.currentRenderPipeline.name : "none")})");
        SceneEffects.ResetCaches();
        var steps = new (string id, Action run)[] { ("EF0", EF0), ("EF1", EF1), ("EF2", EF2), ("EF3", EF3), ("EF4", EF4), ("EF5", EF5), ("EF6", EF6) };
        foreach (var (id, run) in steps)
        {
            if (!Want(id)) continue;
            try { run(); }
            catch (Exception e) { Check(id + " ran without an exception", false, e.ToString()); }
        }
        End("effects_report.txt", "EFFECTS TEST", sw);
        EditorApplication.Exit(fail > 0 ? 1 : 0);
    }

    // ------------------------------------------------------------------ EF0

    static void EF0()
    {
        Section("EF0 template profile and the runtime volume");
        var t = Resources.Load<VolumeProfile>(EffectVolume.TemplateResource);
        Check("Resources/HS2NextEffects loads", t);
        if (!t) return;
        var types = t.components.Where(c => c).Select(c => c.GetType()).ToList();
        foreach (var ty in new[] { typeof(ColorLookup), typeof(Tonemapping), typeof(ColorAdjustments), typeof(UnityEngine.Rendering.Universal.Bloom), typeof(Vignette) })
            Check("template has " + ty.Name, types.Contains(ty));
        string before = string.Join("|", t.components.Select(c => EditorJsonUtility.ToJson(c)));
        using (var rig = new Rig(Services()))
        {
            var v = rig.Fx?.Volume;
            Check("the runtime has a SceneEffects part with a volume", v != null && v.Volume);
            if (v == null) return;
            Check("profile copied from the template", v.ProfileSource == "template" && v.Profile != t && v.Profile.components.All(c => !t.components.Contains(c)), v.ProfileSource);
            Check("volume global, priority 10, weight 1, layer 8", v.Volume.isGlobal && v.Volume.priority == 10f && v.Volume.weight == 1f && v.Root.layer == ViewSetup.EffectVolumeLayer);
            Check("volume under the runtime's View root", v.Root.transform.parent == rig.Runtime.ViewRoot);
            rig.Set("effects.vignette.intensity", 0.9f);
            rig.Runtime.Apply();
        }
        string after = string.Join("|", t.components.Select(c => EditorJsonUtility.ToJson(c)));
        Check("the template asset is unchanged after a runtime used and disposed it", before == after);
    }

    // ------------------------------------------------------------------ EF1

    static void EF1()
    {
        Section("EF1 routing and HS2's apply order");
        using (var rig = new Rig(Services()))
        {
            var fx = rig.Fx;
            var hs2Order = SceneEffects.BlockNames.Take(SceneEffects.RoutedBlocks).ToList();
            // the rig's constructor applied once; force all again to read the order
            fx.ApplyAll();
            Check("ApplyAll applies blocks 1–10 in HS2's order", fx.LastApplied.SequenceEqual(hs2Order), string.Join(",", fx.LastApplied));
            var paths = new Dictionary<string, (string path, object value)>
            {
                ["colorGrading"] = ("effects.colorGrading.saturation", 30),
                ["ambientOcclusion"] = ("effects.ambientOcclusion.intensity", 0.5f),
                ["bloom"] = ("effects.bloom.intensity", 3f),
                ["depthOfField"] = ("effects.depthOfField.focalSize", 1f),
                ["vignette"] = ("effects.vignette.intensity", 0.5f),
                ["ssr"] = ("effects.ssr.enabled", false),
                ["reflectionProbe"] = ("effects.reflectionProbe.intensity", 2f),
                ["fog"] = ("effects.fog.density", 0.0008f),
                ["sunShafts"] = ("effects.sunShafts.intensity", 2f),
                ["shadows"] = ("effects.shadows.enabled", false),
                ["ambient"] = ("effects.ambient.sky", new Color4(1f, 0f, 0f, 1f)),
                ["sky"] = ("effects.sky.enabled", true),
            };
            foreach (var kv in paths)
            {
                rig.Set(kv.Value.path, kv.Value.value);
                var d = fx.DirtyBlocks().ToList();
                Check(kv.Value.path + " marks only " + kv.Key, d.Count == 1 && d[0] == kv.Key, string.Join(",", d));
                rig.Runtime.Apply();
                bool routed = SceneEffects.BlockIndex(kv.Key) < SceneEffects.RoutedBlocks;
                Check("… Apply applies " + (routed ? kv.Key + " only" : "nothing (package S's block)"), routed ? fx.LastApplied.SequenceEqual(new[] { kv.Key }) : !fx.DirtyBlocks().Any(),
                    string.Join(",", fx.LastApplied));
            }
            // several blocks in one transaction, written in reverse order
            using (var tx = rig.Session.Commands.BeginOrJoin("several", default))
            {
                tx.Set(null, "effects.shadows.enabled", true);
                tx.Set(null, "effects.vignette.intensity", 0.2f);
                tx.Set(null, "effects.colorGrading.contrast", 10);
                tx.Commit();
            }
            rig.Runtime.Apply();
            Check("three blocks of one batch apply in HS2's order (grading, vignette, shadows)", fx.LastApplied.SequenceEqual(new[] { "colorGrading", "vignette", "shadows" }), string.Join(",", fx.LastApplied));
            rig.Session.Set(null, "map.pos", new Vec3(1f, 0f, 0f));
            Check("a change outside effects marks nothing", !fx.DirtyBlocks().Any(), string.Join(",", fx.DirtyBlocks()));
            rig.Runtime.Apply();
            rig.Session.Set(null, "effects.bloom", new BloomEffect { Intensity = 5f });
            Check("setting a whole block marks that block", fx.DirtyBlocks().SequenceEqual(new[] { "bloom" }), string.Join(",", fx.DirtyBlocks()));
            rig.Runtime.Apply();
            rig.Runtime.RealizeAll();
            Check("RealizeAll (load) marks every block", fx.DirtyBlocks().Count() == SceneEffects.BlockNames.Length);
            rig.Runtime.Apply();
            Check("… and applies 1–10", fx.LastApplied.SequenceEqual(hs2Order), string.Join(",", fx.LastApplied));
        }
    }

    // ------------------------------------------------------------------ EF2

    static JObject expect;
    static string expectPath;

    static JObject Expect()
    {
        if (expect != null) return expect;
        string root = CardResolveTest.GameRoot;
        expectPath = Path.Combine(ContentSettings.ProjectRoot, "Cache", "effects", "grade_expect_" + ZipmodSourceBase.PathHash(root) + ".json");
        if (!File.Exists(expectPath)) return null;
        expect = JObject.Parse(File.ReadAllText(expectPath));
        return expect;
    }

    static float[] Decode(string b64)
    {
        var bytes = Convert.FromBase64String(b64);
        var f = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, f, 0, bytes.Length);
        return f;
    }

    static void EF2()
    {
        Section("EF2 LUT bake");
        var grid = ColorGradingBake.Grid();
        var neutral = BaseGrade.Neutral.Resolve(0f);
        var id = ColorGradingBake.Bake(null, GradeInputs.Of(0, 0, 0), neutral);
        bool exact = true;
        for (int i = 0; i < grid.Length && exact; i++) exact = BitConverter.SingleToInt32Bits(grid[i]) == BitConverter.SingleToInt32Bits(id[i]);
        Check("neutral grade, neutral inputs, no LUT = the grid bit-exactly", exact);
        var userNeutral = ColorGradingBake.UserLut(null, null, 0.5f);
        var id2 = ColorGradingBake.Bake(userNeutral, GradeInputs.Of(0, 0, 0), neutral);
        Check("… also with an identity user LUT at blend 0.5", ColorGradingBake.Diff(id2, grid).max == 0);
        Info($"grid corners: black ({F(grid[0])}, {F(grid[1])}, {F(grid[2])}), white ({F(grid[grid.Length - 3])}, {F(grid[grid.Length - 2])}, {F(grid[grid.Length - 1])})");

        var tex = ColorGradingBake.ToTexture(grid);
        Check("strip texture 1024 × 32 RGBAHalf linear, bilinear, clamped", tex.width == 1024 && tex.height == 32 && tex.format == TextureFormat.RGBAHalf && !GraphicsFormatUtility.IsSRGBFormat(tex.graphicsFormat)
            && tex.filterMode == FilterMode.Bilinear && tex.wrapMode == TextureWrapMode.Clamp);
        Object.DestroyImmediate(tex);

        var ex = Expect();
        if (ex == null) { Check("the reference file exists (package H: Tools/survey/studio_grade.py bake)", false, expectPath); return; }
        Info("reference: " + Path.GetFileName(expectPath) + " written " + ex["meta"]?["written"]);
        var bg = BaseGrade.Read(CardResolveTest.GameRoot);
        Check("base grade read from the install", bg.Ok, bg.Error);
        if (!bg.Ok) return;
        Info($"base grade: route {bg.Route}, {bg.Milliseconds:F1} ms, main volume settings {bg.Main.PathId} (priority {bg.MainPriority}), colour {bg.Color.PathId} (priority {bg.ColorPriority})");
        if (!ColorGradingBake.CanReadStrips) { Info("no graphics device: the strip comparisons are skipped"); return; }
        var s = Services();
        string defRef = DocDefaults.NoneRef;
        var lutDefault = SceneEffects.FilterStrip(s, defRef, out bool defFound);
        Check("Lut_Default (Filter row 0) read back from the GPU", defFound && lutDefault != null);
        if (lutDefault == null) return;

        // the filter rows behind the keys: HS2Next's row vs the reference's effective row
        var effective = new Dictionary<int, string>();
        foreach (var f in ex["filters"]) if ((bool)f["effective"]) effective[(int)f["key"]] = (string)f["asset"];
        var mism = new List<string>();
        foreach (var kv in effective)
            if (s.Db.TryGetFilter(ContentId.Vanilla(kv.Key), out var row) && row.FileName != kv.Value) mism.Add(kv.Key + ": " + row.FileName + " vs HS2 " + kv.Value);
        Check("HS2Next's Filter rows name HS2's effective strips (HS2 keeps the last row of a key)", mism.Count == 0, string.Join("; ", mism));

        double worstMax = 0, worstMean = 0, totalMs = 0;
        int full = 0, sampled = 0, bakes = 0;
        var nodes = ex["meta"]["sampleNodes"].Select(n => ((int)n[0], (int)n[1], (int)n[2])).ToArray();
        var bad = new List<string>();
        foreach (JObject c in ex["cases"])
        {
            var inp = c["inputs"];
            int key = (int)inp["lut"];
            float blend = (float)inp["blend"];
            var gi = GradeInputs.Of((float)inp["saturation"], (float)inp["brightness"], (float)inp["contrast"]);
            float[] filter = key == 0 ? lutDefault : SceneEffects.FilterStrip(s, ContentId.Vanilla(key).ToString(), out _);
            var swb = Stopwatch.StartNew();
            var user = ColorGradingBake.UserLut(lutDefault, filter ?? lutDefault, blend);
            var mine = ColorGradingBake.Bake(user, gi, bg.Resolve(blend));
            totalMs += swb.Elapsed.TotalMilliseconds; bakes++;
            string name = (string)c["name"];
            double mx = 0, mean = 0;
            if (c["lut"] != null)
            {
                var want = Decode((string)c["lut"]);
                (mx, mean) = ColorGradingBake.Diff(mine, want);
                full++;
                Check($"{name}: max {F(mx * 255)} / 255, mean {F(mean * 255)} / 255", mx <= 0.5 / 255 && mean <= 0.1 / 255);
            }
            else if (c["samples"] is JArray smp)
            {
                double sum = 0;
                for (int k = 0; k < nodes.Length; k++)
                {
                    var (r, g, b) = nodes[k];
                    int idx = (g * 1024 + r + 32 * b) * 3;
                    for (int ch = 0; ch < 3; ch++)
                    {
                        double d = Math.Abs(mine[idx + ch] - (double)smp[k][ch]);
                        mx = Math.Max(mx, d); sum += d;
                    }
                }
                mean = sum / (nodes.Length * 3);
                sampled++;
                if (!(mx <= 0.5 / 255 && mean <= 0.1 / 255)) bad.Add($"{name} max {F(mx * 255)} mean {F(mean * 255)}");
            }
            worstMax = Math.Max(worstMax, mx);
            worstMean = Math.Max(worstMean, mean);
        }
        Check($"the {sampled} filter cases at the 512 sample nodes within max 0.5 / 255, mean 0.1 / 255", bad.Count == 0, string.Join("; ", bad.Take(8)));
        Info($"cases: {full} full strips, {sampled} sampled; worst max {F(worstMax * 255)} / 255, worst mean {F(worstMean * 255)} / 255");
        // HS2's own strips (package H read them back from HS2 Studio's ColorGrading renderer: 1024 × 32 RGBA float32)
        var live = new (string file, string name)[]
        {
            ("fx_defaults", "defaults"), ("cg_sliders0", "sliders0"), ("cg_sat-100", "saturation-100"), ("cg_sat+100", "saturation+100"),
            ("cg_bri-100", "brightness-100"), ("cg_bri+100", "brightness+100"), ("cg_con-100", "contrast-100"), ("cg_con+100", "contrast+100"),
            ("cg_f1_b0.5", "filter1_blend0.5"), ("cg_f1_b1", "filter1_blend1"), ("cg_f10_b0.5", "filter10_blend0.5"), ("cg_f10_b1", "filter10_blend1"),
            ("cg_f20_b0.5", "filter20_blend0.5"), ("cg_f20_b1", "filter20_blend1"),
        };
        string liveDir = Path.Combine(ContentSettings.ProjectRoot, "Cache", "compare", "effects");
        int liveN = 0;
        foreach (var (file, name) in live)
        {
            string lp = Path.Combine(liveDir, file + "_lut.bin");
            var lc = ex["cases"].FirstOrDefault(x => (string)x["name"] == name);
            if (!File.Exists(lp) || lc == null) continue;
            var raw = File.ReadAllBytes(lp);
            if (raw.Length != 1024 * 32 * 16) { Check(file + ": HS2 strip dump has the expected size", false, raw.Length.ToString()); continue; }
            var f4 = new float[raw.Length / 4];
            Buffer.BlockCopy(raw, 0, f4, 0, raw.Length);
            var hs2 = new float[ColorGradingBake.Floats];
            for (int t = 0; t < ColorGradingBake.Texels; t++) { hs2[t * 3] = f4[t * 4]; hs2[t * 3 + 1] = f4[t * 4 + 1]; hs2[t * 3 + 2] = f4[t * 4 + 2]; }
            var linp = lc["inputs"];
            int lkey = (int)linp["lut"];
            float lblend = (float)linp["blend"];
            float[] lfilter = lkey == 0 ? lutDefault : SceneEffects.FilterStrip(s, ContentId.Vanilla(lkey).ToString(), out _);
            var lmine = ColorGradingBake.Bake(ColorGradingBake.UserLut(lutDefault, lfilter ?? lutDefault, lblend),
                GradeInputs.Of((float)linp["saturation"], (float)linp["brightness"], (float)linp["contrast"]), bg.Resolve(lblend));
            var (lmx, lmean) = ColorGradingBake.Diff(lmine, hs2);
            bool mid = lblend > 0f && lblend < 1f;
            // blend 0 / 1: HS2's strip = the model within 0.14 / 255 (package H); 0 < blend < 1: HS2 lerps the two strips in an 8-bit
            // sRGB render texture (GPU rounding), the model is unquantized: max 2.5 / 255, mean 0.6 / 255 (H's tolerance)
            double tmax = mid ? 2.5 / 255 : 0.5 / 255, tmean = mid ? 0.6 / 255 : 0.1 / 255;
            Check($"vs HS2's live strip {file}: max {F(lmx * 255)} / 255, mean {F(lmean * 255)} / 255{(mid ? " (HS2 blends in 8-bit sRGB)" : "")}", lmx <= tmax && lmean <= tmean);
            liveN++;
        }
        if (liveN == 0) Info("HS2's live strips (Cache/compare/effects/*_lut.bin) not found: skipped");
        double ms = totalMs / Math.Max(1, bakes);
        Info($"CPU bake: {F(ms)} ms per strip (32768 texels, incl. the user-LUT lerp), {bakes} bakes");
        Check("bake time per strip reported (spec target < 1 ms: see the number)", ms > 0);
    }

    // ------------------------------------------------------------------ EF3

    static void EF3()
    {
        Section("EF3 BaseGrade reader");
        var bg = BaseGrade.Read(CardResolveTest.GameRoot);
        Check("read ok", bg.Ok, bg.Error);
        var ex = Expect();
        if (ex == null) { Check("the reference file exists", false, expectPath); return; }
        if (bg.Ok)
        {
            var vols = ex["volumes"] as JArray;
            var mine = new[] { bg.Main, bg.Color };
            for (int v = 0; v < 2; v++)
            {
                var cg = vols[v]["colorGrading"];
                var m = mine[v];
                bool Eq(string n, GradeParam p) => (bool)cg[n]["override"] == p.Override && (cg[n]["value"] is JArray a ? a.Select((x, i) => Math.Abs((double)x - p.Value[i]) <= 1e-7).All(x => x) : Math.Abs((double)cg[n]["value"] - p.F) <= 1e-7);
                var names = new[] { "ldrLutContribution", "temperature", "tint", "colorFilter", "hueShift", "saturation", "brightness", "postExposure", "contrast", "lift", "gamma", "gain" };
                var prms = new[] { m.LdrLutContribution, m.Temperature, m.Tint, m.ColorFilter, m.HueShift, m.Saturation, m.Brightness, m.PostExposure, m.Contrast, m.Lift, m.Gamma, m.Gain };
                var off = names.Where((n, i) => !Eq(n, prms[i])).ToList();
                Check($"volume {v} ({vols[v]["gameObject"]}): the 12 grading parameters (override and value) equal the reference", off.Count == 0, string.Join(",", off));
                var mixOff = Enumerable.Range(0, 9).Where(i => (bool)cg["mixer"][i]["override"] != m.Mixer[i].Override || Math.Abs((double)cg["mixer"][i]["value"] - m.Mixer[i].F) > 1e-7).ToList();
                Check($"volume {v}: mixer equal", mixOff.Count == 0, string.Join(",", mixOff));
                var curveOff = new List<string>();
                for (int k = 0; k < 8; k++)
                {
                    var rc = cg["curves"][ColorGradingSettings.CurveNames[k]];
                    var mc = m.Curves[k];
                    var keys = (JArray)rc["keys"];
                    bool ok = (bool)rc["override"] == mc.Override && keys.Count == mc.Keys.Count && (bool)rc["loop"] == mc.Loop && Math.Abs((double)rc["zeroValue"] - mc.ZeroValue) < 1e-7
                        && keys.Select((kk, i) => Enumerable.Range(0, 7).All(j => Math.Abs((double)kk[j] - mc.Keys[i][j]) <= 1e-7)).All(x => x)
                        && rc["cachedData"] is JArray cd && cd.Count == mc.CachedData.Length && cd.Select((x, i) => Math.Abs((double)x - mc.CachedData[i]) <= 1e-7).All(x => x);
                    if (!ok) curveOff.Add(ColorGradingSettings.CurveNames[k]);
                }
                Check($"volume {v}: the 8 splines (override, keys, loop, zero, cachedData) equal", curveOff.Count == 0, string.Join(",", curveOff));
            }
            var b = ex["baseGrade"];
            var p = bg.Resolve(0f);
            bool V3(JToken t, float[] a, double eps) => t is JArray j && j.Count >= 3 && Enumerable.Range(0, 3).All(i => Math.Abs((double)j[i] - a[i]) <= eps);
            Check("lift / invGamma / gain equal the reference (1e-6)", V3(b["lift"], p.Lift, 1e-6) && V3(b["invGamma"], p.InvGamma, 1e-6) && V3(b["gain"], p.Gain, 1e-6),
                $"lift ({F(p.Lift[0])}, {F(p.Lift[1])}, {F(p.Lift[2])}) invGamma ({F(p.InvGamma[0])}, {F(p.InvGamma[1])}, {F(p.InvGamma[2])}) gain ({F(p.Gain[0])}, {F(p.Gain[1])}, {F(p.Gain[2])})");
            Check("colour balance (1e-5; the identity), filter, contribution", V3(b["colorBalance"], p.ColorBalance, 1e-5) && V3(b["colorFilter"], p.ColorFilter, 1e-7) && Math.Abs((double)b["contribution"] - p.Contribution) < 1e-7);
            var tblOff = new List<string>();
            double worst = 0;
            for (int k = 0; k < 8; k++)
            {
                var want = (JArray)b["curveTable"][ColorGradingSettings.CurveNames[k]];
                for (int i = 0; i < 128; i++)
                {
                    double d = Math.Abs((double)want[i] - p.Curves[k][i]);
                    worst = Math.Max(worst, d);
                    if (d > Math.Max(1e-7, Math.Abs((double)want[i]) * 0.0005)) { tblOff.Add(ColorGradingSettings.CurveNames[k] + "[" + i + "]"); break; }
                }
            }
            Check("the 8 curve tables (half precision) equal the reference (<= 1 half ulp)", tblOff.Count == 0, string.Join(",", tblOff) + " worst " + F(worst));
            Check("sources: lift / gamma / gain / master / lumVsSat from volume 0, r / g / b default", p.Sources["lift"] == "volume0+volume1" || p.Sources["lift"] == "volume0", p.Sources["lift"]);
        }

        // fail closed: truncated and changed copies
        string tmp = Path.Combine(Path.GetTempPath(), "hs2next_ef3");
        string data = Path.Combine(tmp, "StudioNEOV2_Data");
        try
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(data);
            string src = Path.Combine(CardResolveTest.GameRoot, "StudioNEOV2_Data");
            File.Copy(Path.Combine(src, "level2"), Path.Combine(data, "level2"));
            var all = File.ReadAllBytes(Path.Combine(src, "sharedassets2.assets"));
            File.WriteAllBytes(Path.Combine(data, "sharedassets2.assets"), all.Take(1 << 20).ToArray());
            var t1 = BaseGrade.Read(tmp);
            Check("a truncated sharedassets2.assets fails closed (neutral, reason)", !t1.Ok && t1.Source == "neutral" && !string.IsNullOrEmpty(t1.Error), t1.Error);
            if (bg.Ok)
            {
                var sf = Hs2SerializedFile.Open(Path.Combine(src, "sharedassets2.assets"));
                sf.TryFind(bg.Main.PathId, out var o);
                long at = o.Start + o.Size - 516;   // the lumVsSat cachedData count (the last spline's 128 floats follow it)
                int count = BitConverter.ToInt32(all, (int)at);
                Info("changed copy: the last spline's cachedData count at offset " + at + " (" + count + ") set to 127");
                var changed = (byte[])all.Clone();
                BitConverter.GetBytes(127).CopyTo(changed, (int)at);
                File.WriteAllBytes(Path.Combine(data, "sharedassets2.assets"), changed);
                var t2 = BaseGrade.Read(tmp);
                Check("a changed ColorGrading object (parse does not end at its size) fails closed", count == 128 && !t2.Ok && t2.Error != null && t2.Error.Contains("parse ended"), t2.Error);
            }
            Directory.Delete(tmp, true);
            var t3 = BaseGrade.Read(tmp);
            Check("a missing install fails closed", !t3.Ok, t3.Error);
        }
        finally { try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch (IOException) { } }

        using (var rig = new Rig(Services()))
        {
            SceneEffects.ResetCaches();
            rig.Fx.BaseGradeOverride = BaseGrade.Unavailable("test: reader disabled");
            rig.Fx.ApplyAll();
            rig.Set("effects.colorGrading.saturation", 20);
            rig.Runtime.Apply();
            int n = rig.Session.Findings.Count(f => f.Code == "effects.baseGradeUnavailable");
            Check("the runtime reports effects.baseGradeUnavailable (warning) once", n == 1 && rig.Session.Findings.First(f => f.Code == "effects.baseGradeUnavailable").Severity == FindingSeverity.Warning, n + " findings");
            var ident = rig.Fx.StripData;
            var neutralBake = ColorGradingBake.Bake(null, GradeInputs.Of(20, -20, 40), BaseGrade.Neutral.Resolve(0f));
            Check("… and bakes the neutral grade (the user sliders only)", ident != null && ColorGradingBake.Diff(ident, neutralBake).max == 0);
            SceneEffects.ResetCaches();
        }
    }

    // ------------------------------------------------------------------ EF4

    static void EF4()
    {
        Section("EF4 bloom mapping, state writes, AO control");
        bool Near(float a, float b, float eps = 1e-4f) => Math.Abs(a - b) <= eps;
        // fix round mapping: t_u = t (1 − 1.95 d + 1.58 d²), d = k − 0.5; intensity (2^(I/10) − 1) · levels · 0.27 · max(rgb)
        float f076 = 1f - 1.95f * 0.26f + 1.58f * 0.26f * 0.26f, f0 = 1f + 1.95f * 0.5f + 1.58f * 0.25f;
        var d = BloomMapping.Map(true, 2f, 1f, 0.76f, true, 7f, new Color(191 / 255f, 191 / 255f, 191 / 255f), 1920, 1080);
        float dI = (Mathf.Pow(2f, 0.2f) - 1f) * 6f * 0.27f * (191f / 255f);
        Check($"defaults at 1920×1080: levels 6, iterations 6, intensity {F(dI)}, linear threshold {F(f076)} (k 0.76), clamp 1, scatter 0.78",
            d.Levels == 6 && d.MaxIterations == 6 && Near(d.Intensity, dI) && Near(d.ThresholdLinear, f076) && Near(d.Threshold, Mathf.LinearToGammaSpace(f076)) && d.Clamp == 1f && Near(d.Scatter, 0.78f) && d.Active,
            $"levels {d.Levels} it {d.MaxIterations} I {F(d.Intensity)} t {F(d.ThresholdLinear)} clamp {F(d.Clamp)} scatter {F(d.Scatter)}");
        var d2 = BloomMapping.Map(true, 10f, 0.5f, 0f, false, 2f, Color.white, 1280, 720);
        Check("intensity 10, threshold 0.5, knee 0, clamp off, diffusion 2, white, 1280×720: levels 1 (iterations 2), clamp 65472, I 0.27, t_u = GammaToLinear(0.5) · 2.37",
            d2.Levels == 1 && d2.MaxIterations == 2 && d2.Clamp == 65472f && Near(d2.Intensity, 0.27f) && Near(d2.ThresholdLinear, Mathf.GammaToLinearSpace(0.5f) * f0),
            $"levels {d2.Levels} I {F(d2.Intensity)} t {F(d2.ThresholdLinear)}");
        Check("knee 0.5: URP's threshold = PPv2's (identical curves)", Near(BloomMapping.Map(true, 2, 1.2f, 0.5f, true, 7, Color.white).ThresholdLinear, Mathf.GammaToLinearSpace(1.2f)));
        Check("disabled or intensity 0 → inactive", !BloomMapping.Map(false, 2, 1, 0.76f, true, 7, Color.white).Active && !BloomMapping.Map(true, 0, 1, 0.76f, true, 7, Color.white).Active);
        var a = AmbientOcclusionControl.Map(true, 0.2f, 2f);
        var a8 = AmbientOcclusionControl.Map(true, 1f, 8f);
        Check($"AO mapping: 0.2 / 2 → exponent {F(AmbientOcclusionControl.IntensityFit * Mathf.Pow(0.2f, AmbientOcclusionControl.IntensityPower))}, radius {F(AmbientOcclusionControl.RadiusFit)}, thickness {F(AmbientOcclusionControl.ThicknessFit)}; thickness 8: larger radius and intensity",
            a.Active && Near(a.Intensity, AmbientOcclusionControl.IntensityFit * Mathf.Pow(0.2f, AmbientOcclusionControl.IntensityPower)) && Near(a.Radius, AmbientOcclusionControl.RadiusFit)
            && Near(a.Thickness, AmbientOcclusionControl.ThicknessFit) && a8.Radius > a.Radius && a8.Intensity > a.Intensity, $"{F(a.Intensity)} {F(a.Radius)} {F(a.Thickness)}");

        using (var rig = new Rig(Services()))
        {
            var fx = rig.Fx;
            var v = fx.Volume;
            Check("defaults: vignette 0.35, smoothness 0.15, not rounded, black, centre", Near(v.Vignette.intensity.value, 0.35f) && Near(v.Vignette.smoothness.value, 0.15f) && !v.Vignette.rounded.value
                && v.Vignette.color.value == Color.black && v.Vignette.center.value == new Vector2(0.5f, 0.5f) && v.Vignette.intensity.overrideState);
            rig.Set("effects.vignette.enabled", false);
            rig.Runtime.Apply();
            Check("vignette off → intensity 0", v.Vignette.intensity.value == 0f);
            Check("bloom defaults on the volume: intensity / threshold / clamp / tint as the mapping", Near(v.Bloom.intensity.value, fx.Bloom.Intensity) && Near(v.Bloom.threshold.value, fx.Bloom.Threshold)
                && v.Bloom.clamp.value == 1f && v.Bloom.highQualityFiltering.value, $"I {F(v.Bloom.intensity.value)} t {F(v.Bloom.threshold.value)}");
            Check("grading: ColorLookup contribution 1 with the strip, Tonemapping None", v.Lookup.contribution.value == 1f && v.Lookup.texture.value == fx.Strip && fx.Strip && v.Tonemap.mode.value == TonemappingMode.None);

            // self shadow
            var cam = rig.Runtime.GetPart<SceneViewCamera>();
            rig.Set("effects.shadows.enabled", false);
            rig.Runtime.Apply();
            Check("self shadow off → ViewSetup.RenderShadows false", !cam.Setup.RenderShadows);
            rig.Runtime.LateApply();
            Check("… and the view camera's URP data renderShadows false", cam.Camera.TryGetComponent<UniversalAdditionalCameraData>(out var ud) && !ud.renderShadows);

            // fog
            RenderSettings.fog = true;
            rig.Set("effects.fog.enabled", true);
            rig.Set("effects.fog.color", new Color4(0.2f, 0.4f, 0.6f, 1f));
            rig.Set("effects.fog.density", 0.0007f);
            rig.Runtime.Apply();
            Check("fog: RenderSettings colour and density written, fog off", RenderSettings.fogColor == new Color(0.2f, 0.4f, 0.6f, 1f) && Near(RenderSettings.fogDensity, 0.0007f, 1e-8f) && !RenderSettings.fog);
            var fogIn = HS2Next.HS2FogFeature.Inputs;
            Check("fog enabled → the fog pass's inputs (enabled, density, linear colour, the scene's mode), no effects.fogNotDrawn (5.R)",
                fogIn.Enabled && Near(fogIn.Density, 0.0007f, 1e-8f) && fogIn.Mode == (int)RenderSettings.fogMode
                && Near(fogIn.ColorLinear.r, new Color(0.2f, 0.4f, 0.6f, 1f).linear.r, 1e-6f) && !rig.Session.Findings.Any(f => f.Code == "effects.fogNotDrawn"));
            rig.Set("effects.sunShafts.enabled", true);
            rig.Set("effects.ssr.enabled", true);
            rig.Set("effects.ambientOcclusion.color", new Color4(1f, 0f, 0f, 1f));
            rig.Runtime.Apply();
            rig.Runtime.LateApply();
            var shIn = HS2Next.HS2SunShaftsFeature.Inputs;
            Check("sun shafts enabled → the shaft pass's inputs (enabled, no caster, intensity 5, falloff 0.75, blur 5), no effects.sunShaftsNotDrawn (5.R)",
                shIn.Enabled && !shIn.HasCaster && Near(shIn.Intensity, 5f, 1e-6f) && Near(shIn.MaxRadius, 0.75f, 1e-6f) && Near(shIn.BlurSize, 5f, 1e-6f)
                && !rig.Session.Findings.Any(f => f.Code == "effects.sunShaftsNotDrawn"));
            Check("SSR enabled and AO colour not black → effects.ssrNotDrawn, aoColorIgnored",
                new[] { "effects.ssrNotDrawn", "effects.aoColorIgnored" }.All(code => rig.Session.Findings.Any(f => f.Code == code)));

            // probe
            rig.Set("effects.reflectionProbe.enabled", true);
            rig.Set("effects.reflectionProbe.cubemap", ContentId.Vanilla(1).ToString());
            rig.Set("effects.reflectionProbe.intensity", 2f);
            rig.Runtime.Apply();
            var p = fx.Probe;
            Check("probe: active, Custom, importance 10, intensity 2, box 999, blend 1, no box projection, cubemap of Probe row 1, under the view camera",
                p.Active && p.Probe.mode == ReflectionProbeMode.Custom && p.Probe.importance == 10 && Near(p.Probe.intensity, 2f) && p.Probe.size == CameraProbe.BoxSize && p.Probe.blendDistance == 1f
                && !p.Probe.boxProjection && p.Cubemap && p.Root.transform.parent == cam.Camera.transform, p.CubemapName);
            rig.Set("effects.reflectionProbe.cubemap", "illusion.hs2#9999");
            rig.Runtime.Apply();
            Check("unknown probe ref → off, no cubemap, effects.probeMissing (warning)", !p.Active && !p.Cubemap && rig.Session.Findings.Any(f => f.Code == "effects.probeMissing" && f.Severity == FindingSeverity.Warning));
            rig.Set("effects.reflectionProbe.enabled", false);
            rig.Runtime.Apply();
            Check("probe disabled → inactive", !p.Active);

            // render.grading = hdr (option, E22): ACES, the sliders through ColorAdjustments, no strip; back to hs2
            try
            {
                RenderOptions.Apply(new JObject { ["grading"] = "hdr" });
                rig.Runtime.Apply();
                Check("render.grading hdr: Tonemapping ACES, ColorLookup off, saturation / contrast on ColorAdjustments",
                    fx.GradingMode == "hdr" && v.Tonemap.mode.value == TonemappingMode.ACES && v.Lookup.contribution.value == 0f
                    && Math.Abs(v.Adjustments.saturation.value - 15f) < 1e-4f && Math.Abs(v.Adjustments.contrast.value - 40f) < 1e-4f && Math.Abs(v.Adjustments.postExposure.value - Mathf.Log(0.8f, 2f)) < 1e-4f,
                    $"{fx.GradingMode} {v.Tonemap.mode.value} {v.Lookup.contribution.value} sat {v.Adjustments.saturation.value} exp {v.Adjustments.postExposure.value}");
            }
            finally { RenderOptions.Apply(new JObject { ["grading"] = "hs2" }); }
            rig.Runtime.Apply();
            Check("… back to hs2: Tonemapping None, the strip at contribution 1, neutral adjustments", fx.GradingMode == "hs2" && v.Tonemap.mode.value == TonemappingMode.None && v.Lookup.contribution.value == 1f && v.Adjustments.saturation.value == 0f);

            if (ColorGradingBake.CanReadStrips)
            {
                rig.Set("effects.colorGrading.lut", "illusion.hs2#9999");
                rig.Runtime.Apply();
                Check("unknown LUT ref → effects.lutMissing (warning), the strip still baked (Lut_Default)", rig.Session.Findings.Any(f => f.Code == "effects.lutMissing") && fx.Strip && !fx.LutFound);
            }
        }

        // AO control: HS2AoFeature found in or added to the production renderer at run time, URP's SSAO switched off, all restored
        var pipe = new HS2Next.App.AppPipeline();
        pipe.Apply();
        try
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var rd = asset ? asset.rendererDataList[0] : null;
            int n0 = rd ? rd.rendererFeatures.Count : -1;
            var ssao = AmbientOcclusionControl.FindFeature();
            bool ssaoBefore = ssao && ssao.isActive;
            var ctl = new AmbientOcclusionControl();
            ctl.Apply(AmbientOcclusionControl.Map(true, 1f, 4f));
            var inp = HS2AoFeature.Inputs;
            Check($"AO control: the HS2AoFeature is in the renderer ({ctl.Source}), the inputs are written", ctl.Feature && rd.rendererFeatures.Contains(ctl.Feature) && inp.Active && Near(inp.Radius, AmbientOcclusionControl.Map(true, 1f, 4f).Radius), ctl.Source);
            Check("… URP's SSAO feature (if any) does not draw meanwhile", !ssao || !ssao.isActive, ssao ? "present" : "none");
            ctl.Apply(AmbientOcclusionControl.Map(false, 1f, 4f));
            Check("… disabled → inputs inactive", !HS2AoFeature.Inputs.Active);
            ctl.Release();
            Check("release: the run-time feature removed, URP's SSAO restored, inputs cleared", rd.rendererFeatures.Count == n0 && (!ssao || ssao.isActive == ssaoBefore) && !HS2AoFeature.Inputs.Active,
                $"{rd.rendererFeatures.Count} / {n0}");
        }
        finally { pipe.Restore(); }
    }

    // ------------------------------------------------------------------ EF5

    static void EF5()
    {
        Section("EF5 focus / caster binding");
        var s = Services();
        using (var rig = new Rig(s))
        {
            var fx = rig.Fx;
            var ses = rig.Session;
            var t = TestTargets.Load();
            string chara = rig.Runtime.Ops.AddCharaFromCard(TestTargets.Abs(t.visual.female))?.NewId;
            string folder = ses.AddFolder()?.NewId;
            ses.Set(folder, "transform.pos", new Vec3(3f, 4f, 5f));
            string item = rig.Runtime.Ops.AddItem(ContentId.Vanilla(399))?.NewId;
            if (item != null) ses.Set(item, "transform.pos", new Vec3(-2f, 1f, 0f));
            rig.Runtime.Apply();
            rig.Runtime.LateApply();
            Check("fixture: BR-Chan, a folder and item 399 added", chara != null && folder != null && item != null);

            rig.Runtime.LateApply();
            var pivot = SceneViewCamera.DefaultPivot;
            Check("no focus → the camera pivot", fx.Dof.FocusKind == "pivot" && fx.Dof.FocusPoint == pivot, fx.Dof.FocusKind + " " + fx.Dof.FocusPoint);
            rig.Set("effects.depthOfField.focus", folder);
            rig.Runtime.Apply(); rig.Runtime.LateApply();
            Check("focus on a folder → its holder (3, 4, 5)", fx.Dof.FocusKind == "node" && (fx.Dof.FocusPoint - new Vector3(3, 4, 5)).magnitude < 1e-4f, fx.Dof.FocusKind + " " + fx.Dof.FocusPoint);
            rig.Set("effects.depthOfField.focus", item);
            rig.Runtime.Apply(); rig.Runtime.LateApply();
            Check("focus on an item → its holder", fx.Dof.FocusKind == "node" && (fx.Dof.FocusPoint - new Vector3(-2, 1, 0)).magnitude < 1e-4f, fx.Dof.FocusPoint.ToString());
            if (chara != null)
            {
                rig.Set("effects.depthOfField.focus", chara);
                rig.Runtime.Apply(); rig.Runtime.LateApply();
                var c = rig.Runtime.FindChara(chara);
                var eyes = c?.Root ? c.Root.GetComponentsInChildren<Transform>(true).Where(x => x.name == "cf_J_Eye_t_L" || x.name == "cf_J_Eye_t_R").ToList() : new List<Transform>();
                Vector3 mid = eyes.Count == 2 ? (eyes[0].position + eyes[1].position) * 0.5f : Vector3.zero;
                Check("focus on BR-Chan → the eye midpoint", fx.Dof.FocusKind == "eyes" && eyes.Count == 2 && (fx.Dof.FocusPoint - mid).magnitude < 1e-4f, fx.Dof.FocusKind + " " + fx.Dof.FocusPoint + " mid " + mid);
                Info("BR-Chan eye midpoint " + mid + " (holder at " + c?.Holder.position + ")");
                rig.Set("effects.sunShafts.caster", chara);
                rig.Runtime.Apply(); rig.Runtime.LateApply();
                Check("sun-shaft caster on BR-Chan → the eye midpoint", fx.CasterKind == "eyes" && fx.CasterPoint.HasValue && (fx.CasterPoint.Value - mid).magnitude < 1e-4f);
            }
            rig.Set("effects.sunShafts.caster", item);
            rig.Runtime.Apply(); rig.Runtime.LateApply();
            Check("caster on the item → its holder", fx.CasterKind == "node" && fx.CasterPoint.HasValue);
            ses.Set(item, "transform.pos", new Vec3(-2f, 6f, 0f));
            rig.Runtime.Apply(); rig.Runtime.LateApply();
            Check("the point follows the node every LateApply", fx.CasterPoint.HasValue && Mathf.Abs(fx.CasterPoint.Value.y - 6f) < 1e-4f);
            rig.Set("effects.depthOfField.focus", item);
            rig.Runtime.Apply(); rig.Runtime.LateApply();
            var del = ses.Delete(new[] { item });
            rig.Runtime.Apply(); rig.Runtime.LateApply();
            var fxDoc = ses.Model.Settings.Effects;
            Check("deleting the node clears focus and caster in the same command", del != null && del.Status != CommandStatus.Error && fxDoc.DepthOfField.Focus == null && fxDoc.SunShafts.Caster == null);
            Check("… the focus falls back to the pivot, the caster to none", fx.Dof.FocusKind == "pivot" && fx.CasterKind == "none" && !fx.CasterPoint.HasValue);
            ses.Commands.Undo();
            rig.Runtime.Apply(); rig.Runtime.LateApply();
            Check("undo of the delete restores both references", fxDoc.DepthOfField.Focus == item && fxDoc.SunShafts.Caster == item && fx.Dof.FocusKind == "node");
            // an id that names no node (a scene saved with a stale id): acts as none, kept
            ses.Model.Settings.Effects.DepthOfField.Focus = "nonexistent";
            fx.ApplyAll();
            rig.Runtime.LateApply();
            Check("an unknown id acts as none (the pivot, \"missing\") and stays in the model", fx.Dof.FocusKind == "missing" && fx.Dof.FocusPoint == pivot && fxDoc.DepthOfField.Focus == "nonexistent");
            ses.Model.Settings.Effects.DepthOfField.Focus = null;
            var dof = SceneEffects.CurrentDof;
            Check("SceneEffects.CurrentDof carries the inputs for package C", dof.FocusKind != null);
        }
    }

    // ------------------------------------------------------------------ EF6

    static void EF6()
    {
        Section("EF6 reset defaults, undo / redo");
        var def = new ScreenEffects();
        foreach (var b in SceneEffects.BlockNames)
        {
            var blk = EffectCommands.DefaultBlock(b);
            Check("DefaultBlock(" + b + ") = ScreenEffectDefine's defaults", blk != null && FieldJson.ToText(blk) == FieldJson.ToText(typeof(ScreenEffects).GetFields().First(f => char.ToLowerInvariant(f.Name[0]) + f.Name.Substring(1) == b).GetValue(def)));
        }
        Check("defaults: grading 0 / 15 / −20 / 40, AO on 0.2 / 2, bloom on 2 / 1 / 0.76 / clamp / 7 / 191, DOF off 0.4 / 0.6, vignette on 0.35, probe off 1, fog off, shafts off, self shadow on",
            def.ColorGrading.Blend == 0 && def.ColorGrading.Saturation == 15 && def.ColorGrading.Brightness == -20 && def.ColorGrading.Contrast == 40 && def.AmbientOcclusion.Enabled && def.AmbientOcclusion.Intensity == 0.2f
            && def.Bloom.Enabled && def.Bloom.Intensity == 2f && def.Bloom.SoftKnee == 0.76f && def.Bloom.Clamp && def.Bloom.Diffusion == 7f && !def.DepthOfField.Enabled && def.Vignette.Intensity == 0.35f
            && !def.ReflectionProbe.Enabled && !def.Fog.Enabled && !def.SunShafts.Enabled && def.Shadows.Enabled);
        using (var rig = new Rig(Services()))
        {
            var ses = rig.Session;
            var fxDoc = ses.Model.Settings.Effects;
            rig.Set("effects.bloom.intensity", 7f);
            rig.Runtime.Apply();
            Check("set bloom 7", fxDoc.Bloom.Intensity == 7f && Math.Abs(rig.Fx.Volume.Bloom.intensity.value - rig.Fx.Bloom.Intensity) < 1e-6f);
            ses.Commands.Undo(); rig.Runtime.Apply();
            Check("undo → 2 and the volume re-applied", fxDoc.Bloom.Intensity == 2f && Math.Abs(rig.Fx.Volume.Bloom.intensity.value - BloomMapping.Map(true, 2, 1, 0.76f, true, 7, new Color(191 / 255f, 191 / 255f, 191 / 255f), rig.Runtime.GetPart<SceneViewCamera>().Camera.pixelWidth, rig.Runtime.GetPart<SceneViewCamera>().Camera.pixelHeight).Intensity) < 1e-5f);
            ses.Commands.Redo(); rig.Runtime.Apply();
            Check("redo → 7", fxDoc.Bloom.Intensity == 7f);
            rig.Set("effects.vignette.intensity", 0.9f);
            using (var tx = ses.Commands.BeginOrJoin("Reset effects", default))
            {
                foreach (var b in SceneEffects.BlockNames) tx.Set(null, "effects." + b, EffectCommands.DefaultBlock(b));
                tx.Commit();
            }
            rig.Runtime.Apply();
            Check("a reset (all blocks) restores the defaults", FieldJson.ToText(fxDoc) == FieldJson.ToText(new ScreenEffects()));
            ses.Commands.Undo(); rig.Runtime.Apply();
            Check("undo of the reset restores bloom 7 and vignette 0.9 in one step", fxDoc.Bloom.Intensity == 7f && Math.Abs(fxDoc.Vignette.Intensity - 0.9f) < 1e-6f && Math.Abs(rig.Fx.Volume.Vignette.intensity.value - 0.9f) < 1e-6f);
            ses.Commands.Redo(); rig.Runtime.Apply();
            Check("redo of the reset", FieldJson.ToText(fxDoc) == FieldJson.ToText(new ScreenEffects()));
        }
    }
}
