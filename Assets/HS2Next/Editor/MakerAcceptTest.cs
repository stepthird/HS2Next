// Phase 4.0 rows of the integrator T.
//   S3 (RunS3; graphics): for each visual target B (Tools/targets.json) and -pairs N (default 6) other character cards A of
//      B's sex (spread over the card folders; cards whose uncensor, studioOverlays and Character materialOverrides equal
//      B's first, so that the morph needs no rebuild): A's documents are replaced by B's member groups (face, hair, body
//      except the uncensor, profile name, clothes, accessories, rule overrides; A's uncensor, studioOverlays and Character
//      materialOverrides are kept: the rebuild-only members), one flush through the edit units, then a fresh build of the
//      morphed documents. Two paths:
//      (1) Built-in: both built by CharaAssembler from the documents without URP mapping (as CharaDocBuildTest P3); the
//          three CharaRenderTest views (full, full34, face) byte-identical and the snapshots with the read-back hashes of
//          every render texture equal.
//      (2) URP: both on the scene builder path (MakerCoreTest.Harness: URP mapped in place; the units map new parts in
//          place); snapshots with the read-back hashes equal and the three views through URP (the measurement asset) with a max
//          channel difference ≤ 8/255 (0 expected).
//      Renders: Spike/out/maker/s3/ (the targets' faces and bodies as the morph builds them; nothing is viewed by the test).
//   T  (RunT; graphics): BR-Chan as a scene node (MakerEditTest.Node), warm, median of -reps 20, against the targets:
//      at or below the target PASS, above it WARN, above 2× FAIL unless the excess has a recorded reason (KnownExcess: then
//      WARN with the reason). Rows: flush with nothing dirty; shape.face; shape.body; material units (draw.body, draw.face,
//      eyes, hair colours, accessory colours); skin.face / skin.body rebuild (CPU = the flush, GPU = flush + a 1-pixel
//      read-back fence after an idle fence); clothes colour pass of the top; part swaps (accessory, hair front, clothes top,
//      head; ChangeItem + the flush, alternating two rows) and their undo; EditAppearance (one set, no flush); MakerLists.Rows
//      for ft_skin_f under BR-Chan's head (first call of a fresh MakerLists and warm); a full build (report only).
// Run: -executeMethod MakerAcceptTest.RunS3 [-pairs N]; MakerAcceptTest.RunT [-reps N]   (graphics device; no -nographics)
// Reports: Spike/out/maker/maker_s3_report.txt, maker_t_report.txt; first line "SUMMARY: N PASS, M FAIL";
// EditorApplication.Exit(1) on any FAIL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Chara;
using HS2Next.Chara.Documents;
using HS2Next.Chara.Edit;
using HS2Next.Content;
using HS2Next.Content.Maker;
using HS2Next.Motion.Face;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Tests.MakerCore;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class MakerAcceptTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "maker");

    // ================================================================ S3

    /// <summary>Step-10 hook of the tests outside a scene (as MakerUnitTest's): a head swap re-attaches the expression stage.</summary>
    sealed class Hooks : ICharaEditSceneHooks
    {
        public void AfterFlush(CharaEditor editor, FlushReport r)
        {
            var ctx = editor.Context?.Ctx;
            if (ctx == null || !r.HeadSwapped || ctx.Motion == null) return;
            foreach (var s in ctx.Motion.GetAll<HS2Next.Motion.IMotionStage>().ToList())
                if (s is ExpressionStage) ctx.Motion.Remove(s);
            FaceSetup.Attach(ctx);
        }
    }

    static string Sig(CharaSubtree s) =>
        HS2Next.Chara.Edit.Units.Signature.Of(s.Chara.Body.Uncensor) + "|" + HS2Next.Chara.Edit.Units.Signature.Of(s.Chara.StudioOverlays) + "|" +
        HS2Next.Chara.Edit.Units.Signature.Of(s.Chara.MaterialOverrides);

    /// <summary>A's documents take B's member groups (the rebuild-only members stay A's); the editor marks them.</summary>
    static void Morph(CharaContext ctx, CharaSubtree b, ResStore resOfB)
    {
        ctx.Edit.Input.Res.CopyFrom(resOfB, b.ReferencedResources());
        var ch = ctx.Chara; var co = ctx.Coord;
        ch.Face = b.Chara.Face; ch.Hair = b.Chara.Hair;
        var bb = b.Chara.Body; bb.Uncensor = ch.Body.Uncensor; ch.Body = bb;
        ch.Profile.Name = b.Chara.Profile.Name;
        co.Clothes = b.Coord.Clothes; co.Accessories.Clear(); co.Accessories.AddRange(b.Coord.Accessories);
        co.RuleOverrides = b.Coord.RuleOverrides;
        foreach (var p in new[] { "chara.face", "chara.hair", "chara.profile.name", "chara.body.shape", "chara.body.bust", "chara.body.skin", "chara.body.detail",
                                  "chara.body.sunburn", "chara.body.paint", "chara.body.nipple", "chara.body.underhair", "chara.body.nail", "chara.body.visible",
                                  "chara.body.skinOverlays", "chara.body.boneMods", "coord.clothes", "coord.accessories", "coord.ruleOverrides" })
            ctx.Editor.MarkDirty(p);
    }

    /// <summary>Built-in build (no URP mapping) of a character container: CharaAssembler over the document input.</summary>
    static (GameObject root, CharaContext ctx, string error) BuildBuiltin(ContentCatalog cat, CharaContainer c)
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var before = new HashSet<GameObject>(scene.GetRootGameObjects());
        try
        {
            var input = CharaDocumentInput.From(c, cat, new FindingList());
            var asm = new CharaAssembler(cat);
            var root = asm.Build(input);
            CharaDocumentInput.AfterBuild(asm.Context);
            foreach (var go in scene.GetRootGameObjects())
                if (go && go != root && !before.Contains(go) && go.transform.parent == null) go.transform.SetParent(root.transform, true);
            return (root, asm.Context, null);
        }
        catch (Exception e)
        {
            foreach (var go in scene.GetRootGameObjects()) if (go && !before.Contains(go)) Object.DestroyImmediate(go);
            return (null, null, e.GetType().Name + ": " + e.Message);
        }
    }

    static void Destroy(GameObject root)
    {
        if (!root) return;
        UnityObjects.ReleaseRenderTextures(root.transform);
        Object.DestroyImmediate(root);
    }

    /// <summary>The A cards of a pair set: same sex as the target, spread over the card folders, signature-equal first.</summary>
    static List<(string file, CharaContainer c)> PickSources(MakerCoreTest.Harness h, string target, CharaContainer tb, int n)
    {
        int sex = tb.Subtree.Chara.Profile.Sex;
        var all = MakerCoreTest.AllCards().Where(f => !string.Equals(Path.GetFullPath(f), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)).ToList();
        var visual = new HashSet<string>(TestTargets.Load().VisualCards.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        all = all.Where(f => !visual.Contains(Path.GetFullPath(f))).ToList();
        string sig = Sig(tb.Subtree);
        var same = new List<(string, CharaContainer)>(); var other = new List<(string, CharaContainer)>();
        int tries = Math.Min(all.Count, 120);
        for (int i = 0; i < tries && same.Count < n; i++)
        {
            string f = all[(int)((long)i * all.Count / tries)];
            var c = MakerEditTest.Import(h, f, out _);
            if (c == null || c.Subtree.Chara.Profile.Sex != sex) continue;
            if (Sig(c.Subtree) == sig) same.Add((f, c)); else if (other.Count < n) other.Add((f, c));
        }
        return same.Concat(other).Take(n).ToList();
    }

    static (int w, int h, int max) MaxDiff(string a, string b)
    {
        var ta = new Texture2D(2, 2); var tb = new Texture2D(2, 2);
        try
        {
            ta.LoadImage(File.ReadAllBytes(a)); tb.LoadImage(File.ReadAllBytes(b));
            if (ta.width != tb.width || ta.height != tb.height) return (ta.width, ta.height, 255);
            var pa = ta.GetPixels32(); var pb = tb.GetPixels32();
            int m = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                m = Math.Max(m, Math.Abs(pa[i].r - pb[i].r)); m = Math.Max(m, Math.Abs(pa[i].g - pb[i].g)); m = Math.Max(m, Math.Abs(pa[i].b - pb[i].b));
            }
            return (ta.width, ta.height, m);
        }
        finally { Object.DestroyImmediate(ta); Object.DestroyImmediate(tb); }
    }

    static readonly string[] Views = { "full", "full34", "face" };

    /// <summary>The CharaRenderTest views, rendered twice: the world bounds of a skinned renderer (the views' framing) are
    /// those of its last skinning, so a character whose shapes changed since its last render (the morph) is framed by its
    /// old bounds on the first render. The second render is the one compared.</summary>
    static void RenderTwice(GameObject chara, string prefix, RenderPipelineAsset rp)
    {
        CharaRenderTest.RenderViews(chara, prefix, rp);
        CharaRenderTest.RenderViews(chara, prefix, rp);
    }

    public static void RunS3()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        int n = int.Parse(Arg("-pairs", "6"), CultureInfo.InvariantCulture);
        rep.AppendLine($"maker S3 (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName}): morph(A → target) vs a fresh build, Built-in renders and RT read-backs; URP renders; {n} pairs per visual target");
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Check("graphics device present (run without -nographics)", false); Finish("maker_s3_report.txt", "S3 renders of morphs", sw); return; }
        ShaderUtil.allowAsyncCompilation = false;
        string dir = Path.Combine(OutDir, "s3");
        Directory.CreateDirectory(dir);
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HS2Next.App.AppPipeline.MeasureAssetPath);
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        int pairs = 0, builtinEq = 0, urpEq = 0, snapEq = 0, rtCount = 0;
        int maxUrp = 0;
        var head = new StringBuilder();
        try
        {
            using (var h = MakerCoreTest.Harness.Open(head))
            {
                rep.Append(head);
                foreach (var target in TestTargets.Load().VisualCards.Where(File.Exists))
                {
                    var tb = MakerEditTest.Import(h, target, out string et);
                    if (!Check(Rel(target) + ": imported", tb != null, et)) continue;
                    var sources = PickSources(h, target, tb, n);
                    Info($"{Rel(target)}: sources {string.Join(", ", sources.Select(s => Rel(s.file) + (Sig(s.c.Subtree) == Sig(tb.Subtree) ? "" : " (rebuild-only members kept from A)")))}");
                    int k = 0;
                    foreach (var (fa, ca) in sources)
                    {
                        k++;
                        pairs++;
                        string pair = Rel(fa) + " → " + Rel(target);
                        string tag = Safe(Path.GetFileNameWithoutExtension(target)) + "_" + k;
                        // (1) Built-in
                        GameObject a = null, b = null;
                        try
                        {
                            var ba = BuildBuiltin(h.Catalog, CloneOf(ca));
                            a = ba.root;
                            if (!Check(pair + ": A built (Built-in)", a != null, ba.error)) continue;
                            ba.ctx.Editor.SceneHooks = new Hooks();
                            CharaSnapshot.Take(a, ba.ctx);
                            var B = tb.Subtree.Clone();
                            Morph(ba.ctx, B, tb.Res);
                            var fr = ba.ctx.Editor.Flush();
                            var sa = CharaSnapshot.Take(a, ba.ctx, new SnapshotOptions { ReadBack = true });
                            RenderTwice(a, Path.Combine(dir, tag + "_morph_builtin"), null);
                            Destroy(a); a = null;   // the fresh root then takes the same place at the scene root
                            var fresh = new CharaContainer { Subtree = MorphedSubtree(ba.ctx, ca) };
                            fresh.Res.CopyFrom(ba.ctx.Edit.Input.Res, fresh.Subtree.ReferencedResources());
                            var bb = BuildBuiltin(h.Catalog, fresh);
                            b = bb.root;
                            if (!Check(pair + ": fresh build (Built-in)", b != null, bb.error)) continue;
                            var sb = CharaSnapshot.Take(b, bb.ctx, new SnapshotOptions { ReadBack = true });
                            RenderTwice(b, Path.Combine(dir, tag + "_fresh_builtin"), null);
                            var d = CharaSnapshot.Compare(sb, sa, 12);
                            rtCount += sa.ReadBackTextures;
                            if (d.Equal) snapEq++;
                            Check($"{pair}: Built-in snapshot incl. {sa.ReadBackTextures} RT read-backs = fresh build ({fr.Units.Count} units in the flush)", d.Equal, d.Equal ? sa.Sha256.Substring(0, 16) : d.ToString());
                            bool all = true;
                            foreach (var v in Views)
                            {
                                var pa = File.ReadAllBytes(Path.Combine(dir, $"{tag}_morph_builtin_{v}.png"));
                                var pb = File.ReadAllBytes(Path.Combine(dir, $"{tag}_fresh_builtin_{v}.png"));
                                bool eq = pa.SequenceEqual(pb);
                                all &= eq;
                                Check($"{pair} {v}: Built-in render byte-identical", eq, eq ? $"{pa.Length} bytes" : $"max channel difference {MaxDiff(Path.Combine(dir, $"{tag}_morph_builtin_{v}.png"), Path.Combine(dir, $"{tag}_fresh_builtin_{v}.png")).max}/255");
                            }
                            if (all) builtinEq++;
                        }
                        catch (Exception e) { Check(pair + ": Built-in, no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace)); }
                        finally
                        {
                            GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null;
                            Destroy(a); Destroy(b);
                        }
                        // (2) URP, the scene builder path
                        MakerCoreTest.Built ua = null, ub = null;
                        try
                        {
                            ua = h.Build(CloneOf(ca), "A " + Rel(fa));
                            if (!Check(pair + ": A built (URP)", ua.Ok, ua.Error)) continue;
                            var ctx = ua.Context;
                            ctx.Editor.SceneHooks = new Hooks();
                            ua.Snapshot();
                            Morph(ctx, tb.Subtree.Clone(), tb.Res);
                            ctx.Editor.Flush();
                            var sa = ua.Snapshot(new SnapshotOptions { ReadBack = true });
                            RenderTwice(ua.Root, Path.Combine(dir, tag + "_morph_urp"), urp);
                            ua.Root.SetActive(false);
                            var fresh = new CharaContainer { Subtree = ua.Result.Stored.Clone() };
                            fresh.Res.CopyFrom(ctx.Edit.Input.Res, fresh.Subtree.ReferencedResources());
                            ub = h.Build(fresh, "fresh " + Rel(target));
                            if (!Check(pair + ": fresh build (URP)", ub.Ok, ub.Error)) continue;
                            var sb = ub.Snapshot(new SnapshotOptions { ReadBack = true });
                            RenderTwice(ub.Root, Path.Combine(dir, tag + "_fresh_urp"), urp);
                            var d = CharaSnapshot.Compare(sb, sa, 12);
                            Check($"{pair}: URP snapshot incl. {sa.ReadBackTextures} RT read-backs = fresh build", d.Equal, d.Equal ? sa.Sha256.Substring(0, 16) : d.ToString());
                            int m = 0;
                            foreach (var v in Views)
                            {
                                var md = MaxDiff(Path.Combine(dir, $"{tag}_morph_urp_{v}.png"), Path.Combine(dir, $"{tag}_fresh_urp_{v}.png"));
                                m = Math.Max(m, md.max);
                                Check($"{pair} {v}: URP render max channel difference ≤ 8/255", md.max <= 8, md.max + "/255");
                            }
                            maxUrp = Math.Max(maxUrp, m);
                            if (m == 0) urpEq++;
                        }
                        catch (Exception e) { Check(pair + ": URP, no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace)); }
                        finally
                        {
                            GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null;
                            ua?.Dispose(); ub?.Dispose();
                        }
                    }
                    h.ReleaseBundles();
                }
            }
        }
        finally
        {
            FaceSetup.Seed = fseed;
            GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null;
        }
        Info($"S3: {pairs} pairs; Built-in renders byte-identical on {builtinEq}, Built-in snapshots with read-backs equal on {snapEq} ({rtCount} RTs hashed); URP renders identical on {urpEq} (max channel difference {maxUrp}/255)");
        Info("renders: Spike/out/maker/s3/");
        Finish("maker_s3_report.txt", "S3 renders of morphs", sw);
    }

    static CharaContainer CloneOf(CharaContainer c)
    {
        var x = new CharaContainer { Subtree = c.Subtree.Clone() };
        x.Res.CopyFrom(c.Res, x.Subtree.ReferencedResources());
        return x;
    }

    /// <summary>The morphed documents of a Built-in build as a subtree (the build's input documents, cloned).</summary>
    static CharaSubtree MorphedSubtree(CharaContext ctx, CharaContainer sourceOfA)
    {
        var s = sourceOfA.Subtree.Clone();
        s.Chara = ctx.Chara; s.Coord = ctx.Coord;
        return s.Clone();
    }

    // ================================================================ T

    /// <summary>Rows whose excess over the target has a recorded reason: WARN, not FAIL.</summary>
    static readonly Dictionary<string, string> KnownExcess = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["shape.face"] = "the flush re-derives the dynamic-bone chains after a shape edit (lengths and object scale follow the shape bones) so that the edited character equals a fresh build; the engine alone is ≈ 0.04 ms (A's U2); carry-over: re-derive on the drag's commit only",
        ["shape.body"] = "as shape.face, plus the bust chains re-derived; the engine alone ≈ 0.1 ms (A's U2)",
        ["skin.face GPU"] = "the target is [U]; two 2048² creators with mips (U1: 3.3–3.5 ms each); within one frame",
        ["skin.body GPU"] = "two 4096² creators with mips ≈ 16 ms (U1) > 8 ms: the C5 drag throttle applies (every other preview frame during a drag; always on commit / cancel)",
        ["clothes colour pass GPU"] = "the target is [U] for 1024² groups (U3: colour + detail creator 1.7 ms at 1024², 5.6 ms at 2048²); BR-Chan's top has 2048² groups",
        ["MakerLists.Rows first call"] = "the first call of a category builds its head index (L: 2.8 ms plain Mono, 4.8 ms Editor); later calls ≈ 0.3 µs",
    };

    static int warns;

    static void Judge(string name, double v, double target, string unit = "ms")
    {
        string val = $"{v.ToString("0.000", CultureInfo.InvariantCulture)} {unit} (target ≤ {target.ToString("0.###", CultureInfo.InvariantCulture)})";
        if (double.IsNaN(v)) { Check(name + ": measured", false, "no value"); return; }
        if (v <= target) { Check(name + ": " + val, true, "met"); return; }
        KnownExcess.TryGetValue(name, out string why);
        if (v <= 2 * target) { warns++; Check(name + ": " + val, true, "WARN above the target" + (why != null ? "; known: " + why : "")); return; }
        if (why != null) { warns++; Check(name + ": " + val, true, "WARN above 2× the target; known: " + why); return; }
        Check(name + ": " + val, false, "above 2× the target");
    }

    static double GpuFence(RenderTexture rt) { if (rt) AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion(); return 0; }

    public static void RunT()
    {
        Reset();
        warns = 0;
        var sw = Stopwatch.StartNew();
        int reps = int.Parse(Arg("-reps", "20"), CultureInfo.InvariantCulture);
        rep.AppendLine($"maker T (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName}): timing, BR-Chan as a scene node, warm, median of {reps}; WARN above the target, FAIL above 2× unless known");
        bool device = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
        Check("graphics device present", device, SystemInfo.graphicsDeviceType.ToString());
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        try
        {
            if (device)
                using (var h = MakerCoreTest.Harness.Open())
                {
                    var file = TestTargets.Load().VisualCards.FirstOrDefault(File.Exists);
                    var c = MakerEditTest.Import(h, file, out string err);
                    if (!Check("BR-Chan imported", c != null, err)) return;
                    var cold = MakerEditTest.NewNode(h, c, Rel(file));
                    double coldMs = cold.BuildMs;
                    cold.Dispose();
                    var n = MakerEditTest.NewNode(h, c, Rel(file));
                    try
                    {
                        if (!Check("BR-Chan built as a scene node", n.Ok, n.Error)) return;
                        Info($"full build (report only): cold {coldMs:F0} ms, warm {n.BuildMs:F0} ms (scene node: import excluded, realization included)");
                        var real = n.Real;
                        var ctx = real.Context;
                        var ed = ctx.Editor;
                        var x = ctx.Edit;
                        n.Apply();

                        double Med(Func<double> f) { var l = new List<double>(); f(); for (int i = 0; i < reps; i++) l.Add(f()); return Median(l); }
                        double FlushOf(string path) { ed.MarkDirty(path); return ed.Flush().Milliseconds; }

                        Judge("flush with nothing dirty", Med(() => { var t = Stopwatch.StartNew(); ed.Flush(); return t.Elapsed.TotalMilliseconds; }), 0.02);
                        Judge("shape.face", Med(() => FlushOf("chara.face.shape[3]")), 0.3);
                        Judge("shape.body", Med(() => FlushOf("chara.body.shape[3]")), 0.6);
                        Judge("material unit draw.body", Med(() => FlushOf("chara.body.nail")), 0.1);
                        Judge("material unit draw.face", Med(() => FlushOf("chara.face.eyebrow")), 0.1);
                        Judge("material unit eyes", Med(() => FlushOf("chara.face.eyes")), 0.1);
                        Judge("material unit hair colours", Med(() => FlushOf("chara.hair.front.baseColor")), 0.1);
                        var acc = n.Stored.Coord.Accessories.FirstOrDefault();
                        if (acc != null) Judge("material unit accessory colours", Med(() => FlushOf("coord.accessories[\"" + acc.Id + "\"].colors")), 0.1);
                        else Info("no accessory on the target: accessory colours not measured");

                        // composites: CPU = the flush, GPU = flush + fence after an idle fence
                        void Composite(string name, string path, RenderTexture rt, double cpuTarget, double gpuTarget)
                        {
                            var cpu = new List<double>(); var gpu = new List<double>();
                            for (int i = 0; i <= reps; i++)
                            {
                                GpuFence(rt);
                                var t = Stopwatch.StartNew();
                                ed.MarkDirty(path);
                                var r = ed.Flush();
                                GpuFence(rt);
                                if (i == 0) continue;
                                cpu.Add(r.Milliseconds); gpu.Add(t.Elapsed.TotalMilliseconds);
                            }
                            if (cpuTarget > 0) Judge(name + " CPU", Median(cpu), cpuTarget);
                            Judge(name + " GPU", Median(gpu), gpuTarget);
                        }
                        if (x.FaceSkin?.Creator?.Target) Composite("skin.face", "chara.face.makeup", x.FaceDetail?.Creator?.Target ?? x.FaceSkin.Creator.Target, 0.5, 2);
                        if (x.BodySkin?.Creator?.Target) Composite("skin.body", "chara.body.sunburn", x.BodyDetail?.Creator?.Target ?? x.BodySkin.Creator.Target, 0.5, 6);
                        int topKind = Enumerable.Range(0, 8).FirstOrDefault(k => x.ClothesColor[k, 0] != null);
                        if (x.ClothesColor[topKind, 0] != null)
                        {
                            var g = x.ClothesColor[topKind, 0].Creator.Target;
                            Info($"clothes colour pass: kind {ClothesSet.Names[topKind]}, group 0 {g.width}x{g.height}");
                            Composite("clothes colour pass", "coord.clothes." + ClothesSet.Names[topKind] + ".colors", g, 0, 1);
                        }

                        // part swaps (op incl. the flush), warm: two rows alternating
                        var lists = MakerCatalog.For(h.Catalog);
                        int sex = n.Stored.Chara.Profile.Sex;
                        var swaps = new List<(string name, string path, int cat, double target)>
                        {
                            ("part swap accessory", acc != null ? AppearancePaths.Accessory(acc.Id, "item") : null, acc?.Category ?? -1, 30),
                            ("part swap hair part", "appearance.chara.hair.front.item", 301, 60),
                            ("part swap clothes kind", "appearance.coord.clothes." + ClothesSet.Names[0] + ".item", (sex == 0 ? ClothesSet.MaleCategories : ClothesSet.FemaleCategories)[0], 100),
                            ("part swap head", "appearance.chara.face.head", sex == 0 ? 110 : 210, 200),
                        };
                        foreach (var (name, path, cat, target) in swaps)
                        {
                            if (path == null) { Info(name + ": no accessory"); continue; }
                            FieldAccessor.TryGet(n.Session.Model, n.Id, path, out var curObj, out _);
                            string cur = curObj as string;
                            var rows = lists.Rows(cat, sex).Select(r => r.Key.ToString()).Where(r => r != cur).ToList();
                            string alt = rows.FirstOrDefault(r => r.StartsWith("illusion.hs2#", StringComparison.Ordinal)) ?? rows.FirstOrDefault();
                            if (cur == null || alt == null) { Info(name + ": no second row"); continue; }
                            Op(n, () => n.Ops.ChangeItem(new[] { n.Id }, path, alt)); Op(n, () => n.Ops.ChangeItem(new[] { n.Id }, path, cur));
                            var ops = new List<double>(); var undo = new List<double>();
                            for (int i = 0; i < reps; i++) ops.Add(Op(n, () => n.Ops.ChangeItem(new[] { n.Id }, path, i % 2 == 0 ? alt : cur)));
                            if (reps % 2 == 1) Op(n, () => n.Ops.ChangeItem(new[] { n.Id }, path, cur));
                            for (int i = 0; i < Math.Min(reps, 10); i++)
                            {
                                Op(n, () => n.Ops.ChangeItem(new[] { n.Id }, path, alt));
                                undo.Add(Op(n, () => { n.Session.Commands.Undo(); return OpResult.Success(); }));
                            }
                            Judge(name, Median(ops), target);
                            Info($"{name}: {cur} ⇄ {alt}; undo of a swap median {Median(undo):F1} ms");
                        }
                        Check("part swaps left no rebuild", n.Real != null && n.Real.EditRebuilds == 0, "rebuilds " + n.Real?.EditRebuilds);

                        // EditAppearance: one set, validation, events (no flush)
                        string sp = "appearance.chara.face.shape[3]";
                        // the op ends with SceneRuntime.ApplyNow, i.e. the flush of the set: op time minus that flush
                        var ea = new List<double>(); var eaTotal = new List<double>();
                        for (int i = 0; i <= reps; i++)
                        {
                            int f0 = n.Real.EditFlushes;
                            var t = Stopwatch.StartNew();
                            n.Ops.EditAppearance(new[] { n.Id }, new List<(string, object)> { (sp, (float)(0.3 + 0.01 * (i % 2))) });
                            double total = t.Elapsed.TotalMilliseconds;
                            double flush = n.Real.EditFlushes > f0 && n.Real.LastEditFlush != null ? n.Real.LastEditFlush.Milliseconds : 0;
                            if (i > 0) { ea.Add(total - flush); eaTotal.Add(total); }
                        }
                        n.Apply();
                        Judge("EditAppearance op", Median(ea), 0.1);
                        Info($"EditAppearance op incl. its flush (ApplyNow; shape.face): median {Median(eaTotal):F3} ms; the op alone is the row above");

                        // MakerLists.Rows for ft_skin_f under the head
                        if (ContentId.TryParse(n.Stored.Chara.Face.Head, out var headId))
                        {
                            var fresh = new MakerLists(lists.Source);
                            var t0 = Stopwatch.StartNew();
                            int count = fresh.Rows(sex == 0 ? 111 : 211, sex, ListContext.ForHead(headId)).Count;
                            double first = t0.Elapsed.TotalMilliseconds;
                            var warm = new List<double>();
                            for (int i = 0; i < reps; i++) { var t = Stopwatch.StartNew(); fresh.Rows(sex == 0 ? 111 : 211, sex, ListContext.ForHead(headId)); warm.Add(t.Elapsed.TotalMilliseconds); }
                            Judge("MakerLists.Rows first call", first, 2);
                            Judge("MakerLists.Rows warm", Median(warm), 2);
                            Info($"MakerLists.Rows(ft_skin_f, head {headId}): {count} rows");
                        }
                    }
                    finally { n.Dispose(); }
                }
        }
        catch (Exception e) { Check("no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace)); }
        finally { FaceSetup.Seed = fseed; }
        Info($"WARN rows: {warns}");
        Finish("maker_t_report.txt", "T timing of the Maker core", sw);
    }

    static double Op(MakerEditTest.Node n, Func<OpResult> op)
    {
        var t = Stopwatch.StartNew();
        op();
        n.Apply();
        return t.Elapsed.TotalMilliseconds;
    }

    // ================================================================ report helpers

    static StringBuilder rep;
    static int passes, fails;

    static bool Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        return ok;
    }

    static void Info(string s) => rep.AppendLine("   " + s);
    static void Reset() { rep = new StringBuilder(); passes = fails = 0; }

    static void Finish(string file, string title, Stopwatch sw)
    {
        rep.Insert(0, $"SUMMARY: {passes} PASS, {fails} FAIL ({sw.Elapsed.TotalSeconds:F0} s)\n{title}\n");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, file), rep.ToString());
        Debug.Log($"MAKER ACCEPT TEST DONE ({file}): {passes} PASS, {fails} FAIL");
        if (fails > 0) EditorApplication.Exit(1);
    }

    static string Arg(string name, string def = null)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static string Short(string s, int n = 400) => s == null ? "" : s.Length > n ? s.Substring(0, n) + "…" : s;
    static string Safe(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_').ToArray());

    static double Median(List<double> l)
    {
        if (l == null || l.Count == 0) return double.NaN;
        var s = l.OrderBy(v => v).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }

    static string Rel(string file)
    {
        if (file == null) return null;
        string full = Path.GetFullPath(file), ud = Path.GetFullPath(TestTargets.UserData).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return (full.StartsWith(ud, StringComparison.OrdinalIgnoreCase) ? full.Substring(ud.Length) : Path.GetFileName(full)).Replace('\\', '/');
    }
}
