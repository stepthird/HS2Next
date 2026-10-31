// Maker hair tests of Phase 4.3 package A (.render, .timing,
// RunS1Explain, UH3, UH4, UH6).
//   Run (headless):
//     HT1 mesh pattern: BR-Chan's front hair as loaded (mesh_t_10, scale (0.5, 0.5), offset (0.5, 1.5)), each 306 row 0..11, the
//         layouts (0, 0, 0, 0), (1, 1, 0, 0), (0.5, …), (−0.5, 1.5, 1.5, −0.5) (clamped lerps), a colour with stored alpha 0 →
//         material alpha 1, row 0 → null; a mod prefab without useMesh keeps the prefab's mesh values; edit = fresh build
//     HT2 bundle rule: a valid list applies; a wrong count, a missing index, an extra entry, an empty list → the prefab
//         defaults; the marks (rates → light, noShake → full)
//     HT3 decoration colours: a vanilla 1-colour hair (every slot), a mod with a flag beyond acsDefColor (not written), a
//         multi-slot mod (every slot)
//     HT4 draw type 0 ↔ 1: shaders (the URP replacements), _MainTex = TexD / TexC, render queues kept, _Ringoff on a RingOff
//         row, the mesh values kept
//     HT5 bundles and chains: a rate drag on BR-Chan's back hair and on BR-Chan with the chain-transform back hair
//         Darkruler.Tifa#107 → preview flushes defer the light re-derivation (no full), the commit runs it once, correction
//         transforms and chain rest poses = a fresh build; noShake → the bundle's chains inactive, one full rebuild; UH3
//     HT6 a part whose rendHair is empty (mcm82.catherine#257): no shader, colours or mesh on its renderers; decoration kept
//     HT7 item change through the ops (StudioOps.ChangeItem): HairInfo facts and the hair gates follow the swap
//     HT8 male: HERO107 with 300#22 / #59 → p_cm_hair_b_19_00 (0 bundles) / p_cm_hair_b_39_00 (2)
//     HT9 digest: the hair materials / bones / chains sections change exactly when the rules say
//     HT10 HairReport facts of every vanilla row = the census (Tools/survey/maker_hair_census.txt aggregates, worked examples)
//          and = PartDefaults.TryHairBundles (the prefab read without instantiating it)
//   RunRender (graphics): the hair materials after HT1 / HT4 edits draw with the replacement shaders and the mesh texture bound;
//     the hair digest after selective edits = a fresh build's; a 64² RT of the front hair rendered twice hashes equal.
//   RunTiming (graphics, its own Unity session): targets of A and UH2, UH4, UH6.
//   RunS1Explain (headless, for T): S1 targets + 20 (or -all / -targets) vs the baselines; every differing snapshot line
//     classified (mesh, deco, rendHair-empty, bundles-J6, other); -vanillaMesh with HairUnit.MeshOnEveryHair off (prefab
//     useMesh only). bundles-J6: a part whose stored bundle list does not apply to the prefab realized (count ≠ boneInfo
//     count, typically a missing item realized as the category's fallback row) has its correction transforms at the prefab
//     defaults (J6, HS2's load rule; before 4.3 the stored entries were applied by index): their bone lines and the lines of
//     the chains rooted at or below them (the tip's end offset is taken in the owner's space at the chain capture).
// Run: -executeMethod MakerHairTest.Run [-only HT1,HT5]; MakerHairTest.RunRender; MakerHairTest.RunTiming [-reps N];
//      MakerHairTest.RunS1Explain [-all|-targets] [-only a.png,b.png] [-limit N] [-vanillaMesh] [-other]; -other prints
//      every unclassified line in full (default: the first 40, shortened) and those cards' hair parts (item, row, prefab,
//      boneInfo / stored bundle counts); the cards with such lines are always listed.
// Reports: Spike/out/maker/maker_hair_report.txt, maker_hair_render_report.txt, maker_hair_timing_report.txt,
// maker_hair_s1explain_report.txt; first line "SUMMARY: N PASS, M FAIL"; EditorApplication.Exit(1) on any FAIL. Content rule:
// numbers only (material values, transforms, chain states, hashes); nothing is rendered to a file or viewed; SFW targets.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AIChara;
using HS2Next.Chara;
using HS2Next.Chara.Edit;
using HS2Next.Chara.Edit.Units;
using HS2Next.Content;
using HS2Next.Motion.DynamicBones;
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

public static class MakerHairTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "maker");
    static StringBuilder rep;
    static int passes, fails, warns;

    static void Reset() { rep = new StringBuilder(); passes = fails = warns = 0; }
    static bool Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        return ok;
    }
    static void Warn(string name, string detail) { rep.AppendLine($"WARN  {name}  {detail}"); warns++; }
    static void Info(string s) => rep.AppendLine("   " + s);
    static void Finish(string file, string title, Stopwatch sw)
    {
        rep.Insert(0, $"SUMMARY: {passes} PASS, {fails} FAIL{(warns > 0 ? ", " + warns + " WARN" : "")} ({sw.Elapsed.TotalSeconds:F0} s)\n{title}\n");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, file), rep.ToString(), new UTF8Encoding(false));
        Debug.Log($"MAKER HAIR TEST DONE ({file}): {passes} PASS, {fails} FAIL, {warns} WARN");
        // inside MakerBodyTest.RunSuite (-suite) the suite ends Unity; the reports carry the results
        if (fails > 0 && !Flag("-suite")) TestExit.Exit(1);
    }

    static string Arg(string name, string def = null)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }
    static bool Flag(string name) => TestArgs.Get().Contains(name);
    static bool Want(string id)
    {
        var o = Arg("-only");
        if (o != null && !o.Split(',', '+').Select(x => x.Trim()).Contains(id)) return false;
        // -skip <ids>: the -Quick variant of the row leaves its sweep-like parts to the -Full row (which runs without it)
        var k = Arg("-skip");
        if (k != null && k.Split(',', '+').Select(x => x.Trim()).Contains(id)) { rep.AppendLine(); rep.AppendLine($"== {id} skipped (-skip)"); return false; }
        return true;
    }
    static double Median(List<double> l)
    {
        if (l.Count == 0) return double.NaN;
        var s = l.OrderBy(x => x).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }
    static string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
    static string G(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector2 v) => $"({G(v.x)}, {G(v.y)})";
    static string V(Vector3 v) => $"({G(v.x)}, {G(v.y)}, {G(v.z)})";
    static string Short(string s, int n = 400) => s == null ? "" : s.Length > n ? s.Substring(0, n) + "…" : s;
    static int Bits(float f) => BitConverter.SingleToInt32Bits(f);
    static bool Same(Vector2 a, Vector2 b) => Bits(a.x) == Bits(b.x) && Bits(a.y) == Bits(b.y);
    static bool Same(Vector3 a, Vector3 b) => Bits(a.x) == Bits(b.x) && Bits(a.y) == Bits(b.y) && Bits(a.z) == Bits(b.z);
    static bool Same(Quaternion a, Quaternion b) => Bits(a.x) == Bits(b.x) && Bits(a.y) == Bits(b.y) && Bits(a.z) == Bits(b.z) && Bits(a.w) == Bits(b.w);
    static bool Near(Color a, Color b, float eps = 1e-6f) => Mathf.Abs(a.r - b.r) <= eps && Mathf.Abs(a.g - b.g) <= eps && Mathf.Abs(a.b - b.b) <= eps && Mathf.Abs(a.a - b.a) <= eps;
    static bool Near(Vector3 a, Vector3 b, float eps) => Mathf.Abs(a.x - b.x) <= eps && Mathf.Abs(a.y - b.y) <= eps && Mathf.Abs(a.z - b.z) <= eps;

    static readonly string[] Parts = UnitMap.HairParts;
    static string FemaleCard => TestTargets.Abs(TestTargets.Load().visual.female);
    static string MaleCard => TestTargets.Abs(TestTargets.Load().visual.male);

    // rows of the census probe (the lists)
    const string ChainTransformBack = "Darkruler.Tifa#107";   // 300: three bundles, each correction transform = its chain root
    const string EmptyRendHairBack = "mcm82.catherine#257";   // 300: rendHair empty, rendAccessory 2 (acsDef 3, use01)
    const string AcsBeyondBack = "Darkruler.Elsa#199";        // 300: useAcsColor01 with acsDefColor empty
    const string AcsMultiSlotBack = "com.jason.tiandhair41#1"; // 300: rendAccessory with 2 slots, acsDef 3, use01
    const string ManyBundlesBack = "Roy12.Mila#69";            // 300: 37 bundles (UH2)
    const string RootChainBack = "[SH]hairs.collect.v4#1";     // 300: a chain on the prefab root holds bundle correction transforms

    sealed class Disposer : IDisposable
    {
        public readonly MakerCoreTest.Built B;
        public Disposer(MakerCoreTest.Built b) { B = b; }
        public void Dispose() => B?.Dispose();
    }

    // ================================================================ helpers

    static CharaContainer ContainerOf(MakerCoreTest.Built live)
    {
        var container = new CharaContainer { Subtree = live.Result.Stored.Clone() };
        container.Res.CopyFrom(live.Container.Res, container.Subtree.ReferencedResources());
        return container;
    }

    /// <summary>The live character vs a fresh build of its documents: the snapshot diff (null = the fresh build failed) and the
    /// hair digest texts (materials, bones, chains) compared; <paramref name="hairDiff"/> names the differing hair sections.</summary>
    static SnapshotDiff VsFresh(MakerCoreTest.Harness h, MakerCoreTest.Built live, out string error, out string hairDiff, Action<MakerCoreTest.Built> extra = null)
    {
        error = null; hairDiff = null;
        var sA = live.Snapshot();
        using (var fresh = new Disposer(h.Build(ContainerOf(live), "fresh")))
        {
            if (!fresh.B.Ok) { error = fresh.B.Error; return null; }
            extra?.Invoke(fresh.B);
            // both stepped once by 0 (the snapshot's Step(0): the chains take their switch state) before the digests
            var sB = fresh.B.Snapshot();
            var d = new List<string>();
            if (FaceDigest.MaterialsText(live.Context, "hair") != FaceDigest.MaterialsText(fresh.B.Context, "hair")) d.Add("materials");
            if (FaceDigest.BonesText(live.Context, "hair") != FaceDigest.BonesText(fresh.B.Context, "hair")) d.Add("bones");
            string ca = FaceDigest.ChainsText(live.Context, "hair"), cb = FaceDigest.ChainsText(fresh.B.Context, "hair");
            if (ca != cb) d.Add("chains (" + FirstDiff(ca, cb) + ")");
            hairDiff = d.Count == 0 ? null : string.Join(", ", d);
            return CharaSnapshot.Compare(sB, sA, 12);
        }
    }

    static string FirstDiff(string a, string b)
    {
        var la = a.Split('\n'); var lb = b.Split('\n');
        for (int i = 0; i < Math.Max(la.Length, lb.Length); i++)
        {
            string x = i < la.Length ? la[i] : "<end>", y = i < lb.Length ? lb[i] : "<end>";
            if (x != y) return $"line {i}: {Short(x, 160)} <> {Short(y, 160)}";
        }
        return "same";
    }

    /// <summary>Diagnostics for MakerEditTest's fuzz: the first difference of the hair materials texts of two characters, with
    /// the renderer it belongs to and the edited character's part facts (null when equal).</summary>
    public static string HairMaterialsDiff(CharaContext a, CharaContext b)
    {
        string ta = FaceDigest.MaterialsText(a, "hair"), tb = FaceDigest.MaterialsText(b, "hair");
        if (ta == tb) return null;
        var la = ta.Split('\n'); var lb = tb.Split('\n');
        string rend = "?";
        for (int i = 0; i < Math.Max(la.Length, lb.Length); i++)
        {
            string x = i < la.Length ? la[i] : "<end>", y = i < lb.Length ? lb[i] : "<end>";
            if (x.StartsWith("renderer ", StringComparison.Ordinal)) rend = x;
            if (x == y) continue;
            var parts = new List<string>();
            for (int k = 0; k < 4; k++)
            {
                var p = HairReport.PartOf(b, k);
                if (p != null && p.Realized) parts.Add($"{p.Name} {p.Item} {p.Prefab} cmp {p.HasCmpHair} useMesh {p.UseMesh} rendHair slots {p.Materials.Count} mesh {p.Mesh.Written}");
            }
            return $"{Short(rend, 300)} | {Short(x, 200)} <> {Short(y, 200)} | parts (edited): {string.Join("; ", parts)}";
        }
        return "same lines";
    }

    /// <summary>The live character = a fresh build of its documents: the snapshot and the hair digest texts (with
    /// <paramref name="hairOnly"/> the hair digest only: after simulated frames the other motion stages, e.g. the bust,
    /// keep their history); with <paramref name="digest"/> also every section of the "all" digest.</summary>
    static bool FreshOk(string label, MakerCoreTest.Harness h, MakerCoreTest.Built live, bool digest = false, bool hairOnly = false)
    {
        FaceDigest.Result da = null, db = null;
        var d = VsFresh(h, live, out string err, out string hd);
        if (digest)
        {
            // both stepped by 0 (VsFresh's snapshots) before the digests
            using (var fresh = new Disposer(h.Build(ContainerOf(live), "fresh")))
            {
                fresh.B.Snapshot();
                da = FaceDigest.Compute(live.Context, "all", false);
                db = FaceDigest.Compute(fresh.B.Context, "all", false);
            }
        }
        bool snapOk = d != null && (hairOnly || d.Equal);
        bool ok = Check(label + (hairOnly ? ": = a fresh build (hair digest)" : ": = a fresh build (snapshot and hair digest)"), snapOk && hd == null,
                        d == null ? err : (d.Equal || hairOnly ? "" : d.ToString()) + (hd != null ? "; hair digest differs: " + hd : ""));
        if (digest) ok = Check(label + ": the digest (all) = a fresh build's", da != null && da.Equals(db), da + " vs " + db) && ok;
        return ok;
    }

    static FlushReport Set(MakerCoreTest.Built live, string path, Action set, FlushMode mode = FlushMode.Normal)
    {
        set?.Invoke();
        live.Context.Editor.MarkDirty(path);
        return live.Context.Editor.Flush(mode);
    }

    /// <summary>Sets a hair part's item as the document (bundles and the part's MaterialEditor items cleared, as the op's
    /// consequences; decoration colours left as stored).</summary>
    static FlushReport SetItem(MakerCoreTest.Built live, int k, string reference)
    {
        var p = live.Context.Chara.Hair.Parts[k];
        return Set(live, "chara.hair." + Parts[k] + ".item", () => { p.Item = reference; p.Bundles.Clear(); p.MaterialOverrides.Clear(); });
    }

    static ListEntry RowOf(ContentCatalog cat, int category, string reference) =>
        ContentId.TryParse(reference, out var id) && !id.IsEmpty ? cat.GetEntry(category, id) : null;

    static bool Installed(MakerCoreTest.Harness h, int category, string reference, string what)
    {
        if (RowOf(h.Catalog, category, reference) != null) return true;
        Warn(what, $"row {category}:{reference} not installed: skipped");
        return false;
    }

    static CmpHair Cmp(CharaContext ctx, int k) => ctx.Hair[k] ? ctx.Hair[k].GetComponent<CmpHair>() : null;

    static IEnumerable<(Renderer r, int slot, Material m)> HairSlots(CharaContext ctx, int k)
    {
        var go = ctx.Hair[k];
        if (!go) yield break;
        foreach (var r in HairUnit.HairRenderers(go, Cmp(ctx, k)))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) yield return (r, i, mats[i]);
        }
    }

    static string TexName(Material m, string prop)
    {
        if (!m || !m.HasProperty(prop)) return "<absent>";
        var t = m.GetTexture(prop);
        return t ? t.name : null;
    }

    /// <summary>Every rendHair slot of part k holds the mesh values of the documents.</summary>
    static bool MeshAsWritten(CharaContext ctx, int k, string texture, Vec4 layout, Color4 colour, out string detail)
    {
        HairUnit.MeshScaleOffset(layout, out var sc, out var of);
        var want = new Color(colour.R, colour.G, colour.B, 1);
        int n = 0, bad = 0;
        string first = null;
        foreach (var (r, slot, m) in HairSlots(ctx, k))
        {
            n++;
            string tn = TexName(m, "_MeshColorMask_B");
            var s = m.GetTextureScale("_MeshColorMask_B"); var o = m.GetTextureOffset("_MeshColorMask_B");
            var c = m.GetColor("_MeshColor_01");
            bool ok = tn == texture && Same(s, sc) && Same(o, of) && Near(c, want);
            if (!ok) { bad++; first ??= $"{r.name}[{slot}] tex {tn ?? "null"} scale {V(s)} offset {V(o)} colour {c}"; }
        }
        detail = $"{n} slots" + (bad > 0 ? $", {bad} differ (first: {first}; expected {texture ?? "null"} {V(sc)} {V(of)} {want})" : "") + $"; expected scale {V(sc)} offset {V(of)}";
        return n > 0 && bad == 0;
    }

    // ================================================================ Run (headless)

    public static void Run()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"maker hair (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}); HairUnit.MeshOnEveryHair {HairUnit.MeshOnEveryHair}");
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        try
        {
            using (var h = MakerCoreTest.Harness.Open())
            {
                if (Want("HT1")) Guard("HT1 mesh pattern", () => HT1(h));
                if (Want("HT2")) Guard("HT2 bundle rule", () => HT2(h));
                if (Want("HT3")) Guard("HT3 decoration colours", () => HT3(h));
                if (Want("HT4")) Guard("HT4 draw type", () => HT4(h));
                if (Want("HT5")) Guard("HT5 bundles and chains (UH3)", () => HT5(h));
                if (Want("HT6")) Guard("HT6 empty rendHair", () => HT6(h));
                if (Want("HT7")) Guard("HT7 item change through the ops", () => HT7(h));
                if (Want("HT8")) Guard("HT8 male", () => HT8(h));
                if (Want("HT9")) Guard("HT9 digest", () => HT9(h));
                if (Want("HT10")) Guard("HT10 facts = census", () => HT10(h));
                if (Want("HT11")) Guard("HT11 HS2 compare findings (package E)", () => HT11(h));
            }
        }
        finally { FaceSetup.Seed = fseed; DynBoneUnit.ReapplyBundlesAfterRestore = true; }
        Finish("maker_hair_report.txt", "MakerHairTest HT1-HT10", sw);
    }

    static void Guard(string id, Action a)
    {
        rep.AppendLine();
        rep.AppendLine("== " + id);
        var t = Flag("-partTimes") ? Stopwatch.StartNew() : null;
        try { a(); }
        catch (Exception e) { Check(id + ": no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace, 1500)); }
        if (t != null) rep.AppendLine($"   part time: {t.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s (-partTimes)");
    }

    static MakerCoreTest.Built BuildOk(MakerCoreTest.Harness h, string file, string label)
    {
        var b = h.Build(file);
        Check(label + ": built", b.Ok, b.Error);
        return b;
    }

    // ---------------------------------------------------------------- HT1

    static readonly Vec4[] HT1Layouts = { new Vec4(0, 0, 0, 0), new Vec4(1, 1, 0, 0), new Vec4(0.5f, 0.5f, 0.5f, 0.5f), new Vec4(-0.5f, 1.5f, 1.5f, -0.5f) };

    static void HT1(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            var fr = ctx.Chara.Hair.Front;
            Info($"front {fr.Item} mesh {fr.Mesh.Item} layout ({G(fr.Mesh.Layout.X)}, {G(fr.Mesh.Layout.Y)}, {G(fr.Mesh.Layout.Z)}, {G(fr.Mesh.Layout.W)}) colour ({G(fr.Mesh.Color.R)}, {G(fr.Mesh.Color.G)}, {G(fr.Mesh.Color.B)}, {G(fr.Mesh.Color.A)}); useMesh {Cmp(ctx, 1)?.useMesh}");
            // the worked example (as loaded)
            Check("HT1 BR-Chan's front as built: mesh_t_10, scale (0.5, 0.5), offset (0.5, 1.5), colour white",
                  MeshAsWritten(ctx, 1, "mesh_t_10", new Vec4(0, 0, 0, 1), new Color4(1, 1, 1, 1), out string d0) && fr.Mesh.Item == "illusion.hs2#10", d0);
            var rep0 = HairReport.PartOf(ctx, 1);
            Check("HT1 HairReport: mesh written, uniform, mesh_t_10 1024x1024", rep0.Mesh.Written && rep0.Mesh.Uniform && rep0.Mesh.Texture == "mesh_t_10" && rep0.Mesh.TextureWidth == 1024,
                  $"{rep0.Mesh.Texture} {rep0.Mesh.TextureWidth}x{rep0.Mesh.TextureHeight} {rep0.Mesh.TextureFormat} uniform {rep0.Mesh.Uniform}");
            // each pattern row
            int rows = 0, ok = 0;
            var bad = new List<string>();
            for (int i = 0; i <= 11; i++)
            {
                var row = RowOf(h.Catalog, 306, "illusion.hs2#" + i);
                if (row == null) { bad.Add("row " + i + " not listed"); continue; }
                rows++;
                string want = i == 0 ? null : row["MainTex"];
                var r = Set(live, "chara.hair.front.mesh.item", () => fr.Mesh.Item = "illusion.hs2#" + i);
                bool swap = r.HairSwapped[1];
                if (MeshAsWritten(ctx, 1, want, fr.Mesh.Layout, fr.Mesh.Color, out string di) && !swap && r.Errors.Count == 0 && !r.Units.Contains(UnitMap.DynBone)) ok++;
                else bad.Add($"row {i}: {di}{(swap ? ", swapped" : "")}{(r.Errors.Count > 0 ? ", errors " + string.Join("; ", r.Errors) : "")}");
            }
            Check("HT1 every 306 row 0..11: _MeshColorMask_B = the row's MainTex (row 0 → null), no part swap, no chain work", rows == 12 && ok == 12, $"{ok} of {rows}; " + Short(string.Join(" | ", bad), 1500));
            // layouts (clamped lerps) on pattern 5
            Set(live, "chara.hair.front.mesh.item", () => fr.Mesh.Item = "illusion.hs2#5");
            string t5 = RowOf(h.Catalog, 306, "illusion.hs2#5")?["MainTex"];
            foreach (var lay in HT1Layouts)
            {
                Set(live, "chara.hair.front.mesh.layout", () => fr.Mesh.Layout = lay);
                Check($"HT1 layout ({G(lay.X)}, {G(lay.Y)}, {G(lay.Z)}, {G(lay.W)}): scale (lerp(0.5, 1, x), lerp(0.5, 1, y)), offset (lerp(0.5, 1.5, z), lerp(0.5, 1.5, w)), clamped",
                      MeshAsWritten(ctx, 1, t5, lay, fr.Mesh.Color, out string dl), dl);
            }
            HairUnit.MeshScaleOffset(new Vec4(-0.5f, 1.5f, 1.5f, -0.5f), out var cs, out var co);
            Check("HT1 the clamp: (−0.5, 1.5, 1.5, −0.5) → scale (0.5, 1), offset (1.5, 0.5)", Same(cs, new Vector2(0.5f, 1f)) && Same(co, new Vector2(1.5f, 0.5f)), V(cs) + " " + V(co));
            // colour with a stored alpha 0
            var col0 = new Color4(0.3f, 0.6f, 0.9f, 0f);
            Set(live, "chara.hair.front.mesh.color", () => fr.Mesh.Color = col0);
            Check("HT1 colour (0.3, 0.6, 0.9, alpha 0): _MeshColor_01 alpha 1, the document keeps alpha 0", MeshAsWritten(ctx, 1, t5, fr.Mesh.Layout, col0, out string dc) && fr.Mesh.Color.A == 0f, dc);
            FreshOk("HT1 after the mesh edits", h, live);
            // a prefab without useMesh: the prefab's mesh values stay
            if (Installed(h, 300, ChainTransformBack, "HT1 non-useMesh prefab"))
            {
                bool keepAll = HairUnit.MeshOnEveryHair;
                var bk = ctx.Chara.Hair.Back;
                try
                {
                    // vanilla's rule (MeshOnEveryHair off): a prefab without useMesh keeps its mesh values
                    HairUnit.MeshOnEveryHair = false;
                    SetItem(live, 0, ChainTransformBack);
                    var cmp = Cmp(ctx, 0);
                    Check("HT1 " + ChainTransformBack + ": a prefab without useMesh", cmp != null && cmp.useMesh == 0, "useMesh " + cmp?.useMesh);
                    var before = HairSlots(ctx, 0).Select(s => (TexName(s.m, "_MeshColorMask_B"), s.m.GetTextureScale("_MeshColorMask_B"), s.m.GetTextureOffset("_MeshColorMask_B"), s.m.GetColor("_MeshColor_01"))).ToList();
                    Set(live, "chara.hair.back.mesh.item", () => bk.Mesh.Item = "illusion.hs2#5");
                    Set(live, "chara.hair.back.mesh.layout", () => bk.Mesh.Layout = new Vec4(0.25f, 0.75f, 0.5f, 0.5f));
                    Set(live, "chara.hair.back.mesh.color", () => bk.Mesh.Color = new Color4(0.2f, 0.1f, 0.7f, 1f));
                    var after = HairSlots(ctx, 0).Select(s => (TexName(s.m, "_MeshColorMask_B"), s.m.GetTextureScale("_MeshColorMask_B"), s.m.GetTextureOffset("_MeshColorMask_B"), s.m.GetColor("_MeshColor_01"))).ToList();
                    bool same = before.Count > 0 && before.Count == after.Count && before.Zip(after, (a, b) => a.Item1 == b.Item1 && Same(a.Item2, b.Item2) && Same(a.Item3, b.Item3) && Near(a.Item4, b.Item4, 0)).All(x => x);
                    Check("HT1 MeshOnEveryHair off, " + ChainTransformBack + " (no useMesh): mesh item / layout / colour edits leave the prefab's mesh values", same, $"{before.Count} slots; before {string.Join(", ", before.Take(2))}; after {string.Join(", ", after.Take(2))}");
                    Check("HT1 MeshOnEveryHair off: HairReport says mesh not written, HairInfo facts UseMesh false", !HairReport.PartOf(ctx, 0).Mesh.Written && !HairInfo.FactsOf(ctx, 0).UseMesh);
                    FreshOk("HT1 MeshOnEveryHair off, the non-useMesh back hair", h, live);
                    // HairMeshforAll (MeshOnEveryHair on, the default): the same part draws the stored mesh values
                    HairUnit.MeshOnEveryHair = true;
                    live.Context.Editor.MarkUnit("hair.back"); live.Context.Editor.Flush();
                    Check("HT1 MeshOnEveryHair on: the non-useMesh part gets mesh_t_05, the layout's ST and the colour (alpha 1)",
                          MeshAsWritten(ctx, 0, RowOf(h.Catalog, 306, "illusion.hs2#5")?["MainTex"], bk.Mesh.Layout, bk.Mesh.Color, out string dm) && HairReport.PartOf(ctx, 0).Mesh.Written && HairInfo.FactsOf(ctx, 0).UseMesh, dm);
                    FreshOk("HT1 MeshOnEveryHair on, the non-useMesh back hair", h, live);
                }
                finally { HairUnit.MeshOnEveryHair = keepAll; }
            }
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- HT2

    static List<HairBundle> Bundles(int n, Func<int, Vec3> move, Func<int, Vec3> rot, Func<int, bool> ns = null) =>
        Enumerable.Range(0, n).Select(i => new HairBundle { Index = i, Move = move(i), Rotate = rot(i), NoShake = ns != null && ns(i) }).ToList();

    /// <summary>The correction transforms of part k = RangeLerp of the effective rates (or of the defaults when
    /// <paramref name="defaults"/>), bit for bit.</summary>
    static bool CorrectionsAre(CharaContext ctx, int k, bool defaults, out string detail)
    {
        var cmp = Cmp(ctx, k);
        var eff = HairUnit.EffectiveBundles(cmp, ctx.Chara.Hair.Parts[k]);
        int n = 0, bad = 0; string first = null;
        for (int b = 0; b < (cmp?.boneInfo?.Count ?? 0); b++)
        {
            var bi = cmp.boneInfo[b];
            if (!bi?.trfCorrect) continue;
            n++;
            Vector3 mv, rr;
            if (defaults) HairUnit.DefaultRates(bi, out mv, out rr); else { mv = eff[b].Move; rr = eff[b].Rotate; }
            Func<float, float, float, float> L = ShapeEngine.RangeLerp;
            var pos = new Vector3(L(bi.posMin.x, bi.posMax.x, mv.x), L(bi.posMin.y, bi.posMax.y, mv.y), L(bi.posMin.z, bi.posMax.z, mv.z));
            var probe = new GameObject("probe").transform;
            probe.localEulerAngles = new Vector3(L(bi.rotMin.x, bi.rotMax.x, rr.x), L(bi.rotMin.y, bi.rotMax.y, rr.y), L(bi.rotMin.z, bi.rotMax.z, rr.z));
            var q = probe.localRotation;
            Object.DestroyImmediate(probe.gameObject);
            if (!Same(bi.trfCorrect.localPosition, pos) || !Same(bi.trfCorrect.localRotation, q) || (defaults && eff[b].Stored))
            {
                bad++;
                first ??= $"bundle {b}: pos {V(bi.trfCorrect.localPosition)} vs {V(pos)}, rot {bi.trfCorrect.localRotation} vs {q}, stored {eff[b].Stored}";
            }
        }
        detail = $"{n} correction transforms" + (bad > 0 ? $", {bad} differ: {first}" : "");
        return n > 0 && bad == 0;
    }

    static void HT2(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            var back = ctx.Chara.Hair.Back;
            int n = Cmp(ctx, 0)?.boneInfo?.Count ?? 0;
            Info($"back {back.Item}: {n} bundles, stored {back.Bundles.Count}");
            Check("HT2 BR-Chan's back hair has bundles (3)", n == 3, n.ToString());
            bool c0 = CorrectionsAre(ctx, 0, false, out string d0);
            Check("HT2 as built: the stored list applies (= the defaults)", HairUnit.StoredValid(back, n) && c0, d0);
            // A: a valid edited list
            var r = Set(live, "chara.hair.back.bundles", () => back.Bundles = Bundles(n, i => new Vec3(0.1f + 0.1f * i, 0.2f, 0.9f), i => new Vec3(0.3f, 0.6f - 0.1f * i, 0.75f)));
            bool ca = CorrectionsAre(ctx, 0, false, out string da);
            Check("HT2 a valid list (3 entries, indices 0..2): the corrections follow the stored rates", HairUnit.StoredValid(back, n) && ca, da);
            Check("HT2 a rate change marks the light chain re-derivation (no full)", r.DynBone == "light" && r.Units.Contains(UnitMap.DynBone), $"dynbone {r.DynBone}; units [{string.Join(",", r.Units)}]");
            FreshOk("HT2 valid list", h, live);
            // B..E: invalid lists → every bundle at the defaults
            var cases = new (string what, Func<List<HairBundle>> list)[]
            {
                ("wrong count (2 of 3)", () => Bundles(2, i => new Vec3(0.9f, 0.9f, 0.9f), i => new Vec3(0.1f, 0.1f, 0.1f))),
                ("a missing index (0, 1, 1)", () => { var l = Bundles(3, i => new Vec3(0.9f, 0.9f, 0.9f), i => new Vec3(0.1f, 0.1f, 0.1f)); l[2].Index = 1; return l; }),
                ("an extra entry (4 of 3)", () => Bundles(4, i => new Vec3(0.9f, 0.9f, 0.9f), i => new Vec3(0.1f, 0.1f, 0.1f))),
                ("an index out of range (0, 1, 5)", () => { var l = Bundles(3, i => new Vec3(0.9f, 0.9f, 0.9f), i => new Vec3(0.1f, 0.1f, 0.1f)); l[2].Index = 5; return l; }),
                ("empty", () => new List<HairBundle>()),
            };
            foreach (var (what, list) in cases)
            {
                Set(live, "chara.hair.back.bundles", () => back.Bundles = list());
                var pr = HairReport.PartOf(ctx, 0);
                bool cd = CorrectionsAre(ctx, 0, true, out string dd);
                Check($"HT2 {what}: every bundle at the prefab defaults", !HairUnit.StoredValid(back, n) && !pr.StoredValid && cd && pr.Bundles.All(b => !b.EffectiveNoShake), dd);
            }
            // noShake in an invalid list does not apply; in a valid list it marks the full rebuild
            Set(live, "chara.hair.back.bundles", () => back.Bundles = Bundles(2, i => new Vec3(0.5f, 0.5f, 0.5f), i => new Vec3(0.5f, 0.5f, 0.5f), i => true));
            var chains0 = HairReport.PartOf(ctx, 0).Bundles.SelectMany(b => b.Chains).ToList();
            Check("HT2 noShake in a list of the wrong count: not applied (chains keep shaking)", chains0.Count > 0 && chains0.All(c => !c.NoShake), string.Join(", ", chains0.Select(c => c.Root + " noShake " + c.NoShake)));
            r = Set(live, "chara.hair.back.bundles", () => back.Bundles = Bundles(n, i => new Vec3(0.5f, 0.5f, 0.5f), i => new Vec3(0.5f, 0.5f, 0.5f), i => i == 1));
            var b1 = HairReport.PartOf(ctx, 0).Bundles[1];
            Check("HT2 a valid list with noShake on bundle 1: one full rebuild, its chains noShake", r.DynBone == "full" && b1.Chains.Count > 0 && b1.Chains.All(c => c.NoShake), $"dynbone {r.DynBone}; chains {string.Join(", ", b1.Chains.Select(c => c.Root + " noShake " + c.NoShake))}");
            FreshOk("HT2 after the cases", h, live);
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- HT3

    static void HT3(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            var back = ctx.Chara.Hair.Back;
            var x = new Color4(0.9f, 0.1f, 0.2f, 0.5f);
            void Case(string reference, string what, bool expectWritten, int expectSlots)
            {
                if (!Installed(h, 300, reference, "HT3 " + what)) return;
                SetItem(live, 0, reference);
                var cmp = Cmp(ctx, 0);
                Set(live, "chara.hair.back.accessoryColors", () => { back.AccessoryColors = new[] { x, new Color4(0.1f, 0.8f, 0.3f, 1), new Color4(0.4f, 0.4f, 0.9f, 1), Color4.White }; });
                var pr = HairReport.PartOf(ctx, 0);
                int slots = pr.Decoration.Count;
                var want = new Color(x.R, x.G, x.B, 1);
                int written = pr.Decoration.Count(d => d.Colors[0].HasValue && Near(d.Colors[0].Value, want));
                string detail = $"{reference}: acsDefColor {pr.AcsDefColorCount}, use [{(pr.UseAcsColor01 ? 1 : 0)},{(pr.UseAcsColor02 ? 1 : 0)},{(pr.UseAcsColor03 ? 1 : 0)}], decoration slots {slots}, _Color = colour 1 on {written}";
                if (expectWritten) Check($"HT3 {what}: _Color = colour 1 (alpha 1) on every slot of every rendAccessory renderer", slots == expectSlots && written == slots, detail);
                else
                {
                    // the prefab's own value stays: compare with the bundle prefab's material
                    var e = RowOf(h.Catalog, 300, reference);
                    var prefab = e != null ? h.Catalog.LoadAsset<GameObject>(e, "MainAB", "MainData") : null;
                    var pc = prefab ? prefab.GetComponent<CmpHair>() : null;
                    var pm = pc?.rendAccessory?.Where(r => r).SelectMany(r => r.sharedMaterials).ToList() ?? new List<Material>();
                    bool kept = pm.Count == slots && pm.Zip(pr.Decoration, (m, d) => m && m.HasProperty("_Color") ? d.Colors[0].HasValue && Near(d.Colors[0].Value, m.GetColor("_Color")) : !d.Colors[0].HasValue).All(b => b);
                    Check($"HT3 {what}: colour 1 not written (useAcsColor01 beyond acsDefColor); the prefab's _Color stays", slots == expectSlots && written == 0 && kept, detail + $"; prefab slots {pm.Count}, kept {kept}");
                }
                FreshOk("HT3 " + what, h, live);
            }
            Case("illusion.hs2#2", "vanilla 300#2 (one decoration colour)", true, 1);
            Case(AcsBeyondBack, "a flag beyond acsDefColor", false, 1);
            Case(AcsMultiSlotBack, "a two-slot decoration renderer", true, 2);
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- HT4

    static void HT4(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            var hair = ctx.Chara.Hair;
            // a back row with TexD != TexC and a RingOff row
            var rows = h.Catalog.EntriesOf(300).Where(e => e.IsVanilla && e["MainData"] != "p_dummy").ToList();
            var dc = rows.FirstOrDefault(e => !string.IsNullOrEmpty(e["TexD"]) && e["TexD"] != e["TexC"] && e["RingOff"] != "1");
            var ring = rows.FirstOrDefault(e => e["RingOff"] == "1");
            Info($"rows: TexD != TexC {dc?.Key} ({dc?["TexD"]} / {dc?["TexC"]}), RingOff {ring?.Key}");
            if (dc != null) SetItem(live, 0, dc.Key.ToString());
            var queues = Enumerable.Range(0, 4).SelectMany(k => HairSlots(ctx, k).Select(s => (k, s.r, s.slot, q: s.m.renderQueue))).ToList();
            foreach (int st in new[] { 1, 0 })
            {
                var r = Set(live, "chara.hair.shaderType", () => hair.ShaderType = st);
                string want = "HS2Next/Shader Forge/hair_0" + (st == 0 ? "7" : "8");
                int n = 0, badShader = 0, badQueue = 0; var bad = new List<string>();
                for (int k = 0; k < 4; k++)
                {
                    var unit = ctx.Editor.Unit(UnitMap.HairUnits[k]) as HairUnit;
                    var row = unit?.Row;
                    foreach (var (rr, slot, m) in HairSlots(ctx, k))
                    {
                        n++;
                        if (m.shader.name != want) { badShader++; bad.Add($"{Parts[k]} {rr.name}[{slot}] {m.shader.name}"); }
                        var q0 = queues.FirstOrDefault(t => t.k == k && t.r == rr && t.slot == slot);
                        if (q0.r && q0.q != m.renderQueue) badQueue++;
                        string tex = TexName(m, "_MainTex"), wantTex = row?[st == 0 ? "TexD" : "TexC"];
                        if (!string.IsNullOrEmpty(wantTex) && wantTex != "0" && tex != wantTex) bad.Add($"{Parts[k]} {rr.name}[{slot}] _MainTex {tex} (want {wantTex})");
                    }
                }
                Check($"HT4 shaderType {st}: every rendHair slot of the four parts has {want}, render queues kept, _MainTex = {(st == 0 ? "TexD" : "TexC")}",
                      n > 0 && badShader == 0 && badQueue == 0 && bad.Count == 0 && !r.AnySwap, $"{n} slots, shader {badShader}, queue {badQueue}; " + Short(string.Join(" | ", bad), 800));
                Check($"HT4 shaderType {st}: BR-Chan's front keeps its mesh values", MeshAsWritten(ctx, 1, "mesh_t_10", hair.Front.Mesh.Layout, hair.Front.Mesh.Color, out string dm), dm);
                Check($"HT4 shaderType {st}: the four hair units ran, the URP mapping in place (ownership step)", Enumerable.Range(0, 4).All(k => r.Units.Contains(UnitMap.HairUnits[k])) && r.Units.Contains(UnitMap.Ownership), string.Join(",", r.Units));
                FreshOk($"HT4 shaderType {st}", h, live);
            }
            if (ring != null)
            {
                SetItem(live, 0, ring.Key.ToString());
                var vals = HairSlots(ctx, 0).Where(s => s.m.HasProperty("_Ringoff")).Select(s => s.m.GetFloat("_Ringoff")).ToList();
                Check($"HT4 RingOff row {ring.Key}: _Ringoff 1 on its slots", vals.Count > 0 && vals.All(v => v == 1f), string.Join(",", vals.Select(G)));
                Set(live, "chara.hair.shaderType", () => hair.ShaderType = 1);
                vals = HairSlots(ctx, 0).Where(s => s.m.HasProperty("_Ringoff")).Select(s => s.m.GetFloat("_Ringoff")).ToList();
                Check("HT4 RingOff row with shaderType 1: _Ringoff 1", vals.Count > 0 && vals.All(v => v == 1f), string.Join(",", vals.Select(G)));
                Set(live, "chara.hair.shaderType", () => hair.ShaderType = 0);
                FreshOk("HT4 RingOff row", h, live);
            }
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- HT5 (+ UH3)

    /// <summary>A bundle drag: <paramref name="frames"/> preview flushes of bundle <paramref name="b"/>'s move.y, then the
    /// commit (a Normal flush). Returns the reports.</summary>
    static List<FlushReport> Drag(MakerCoreTest.Built live, int k, int b, float y0, int frames)
    {
        var part = live.Context.Chara.Hair.Parts[k];
        var l = new List<FlushReport>();
        for (int i = 0; i < frames; i++)
        {
            float y = y0 + 0.05f * i;
            l.Add(Set(live, $"chara.hair.{Parts[k]}.bundles[{b}].move", () => { var e = part.Bundles.First(x => x.Index == b); e.Move = new Vec3(e.Move.X, y, e.Move.Z); }, FlushMode.Preview));
        }
        l.Add(live.Context.Editor.Flush(FlushMode.Normal));
        return l;
    }

    /// <summary>The stored list = the defaults (the op's materialization).</summary>
    static void Materialize(CharaContext ctx, int k)
    {
        var cmp = Cmp(ctx, k);
        var part = ctx.Chara.Hair.Parts[k];
        part.Bundles = Enumerable.Range(0, cmp?.boneInfo?.Count ?? 0).Select(i =>
        {
            HairUnit.DefaultRates(cmp.boneInfo[i], out var mv, out var rr);
            return new HairBundle { Index = i, Move = new Vec3(mv.x, mv.y, mv.z), Rotate = new Vec3(rr.x, rr.y, rr.z) };
        }).ToList();
        ctx.Editor.MarkDirty("chara.hair." + Parts[k] + ".bundles");
        ctx.Editor.Flush();
    }

    static void HT5(MakerCoreTest.Harness h)
    {
        // (a) BR-Chan's own back hair (vanilla: every correction transform above its chains)
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (live.Ok)
            {
                var reps = Drag(live, 0, 0, 0.2f, 3);
                var prev = reps.Take(3).ToList(); var commit = reps[3];
                Check("HT5 BR-Chan back bundle 0 drag: 3 preview flushes run hair.back, defer the light re-derivation, no full rebuild",
                      prev.All(r => r.Units.Contains("hair.back") && r.DynBone == "none" && r.Deferred.Contains(UnitMap.DynBone) && r.Mode == FlushMode.Preview),
                      string.Join(" | ", prev.Select(r => $"dynbone {r.DynBone}, deferred [{string.Join(",", r.Deferred)}]")));
                Check("HT5 the commit runs the light re-derivation once (nothing deferred after)", commit.DynBone == "light" && commit.Deferred.Count == 0 && commit.Units.Count(u => u == UnitMap.DynBone) == 1,
                      $"dynbone {commit.DynBone}; units [{string.Join(",", commit.Units)}]");
                Check("HT5 BR-Chan's corrections = the stored rates", CorrectionsAre(live.Context, 0, false, out string d1), d1);
                FreshOk("HT5 BR-Chan after the drag", h, live);
                // noShake
                var back = live.Context.Chara.Hair.Back;
                var r2 = Set(live, "chara.hair.back.bundles[1].noShake", () => back.Bundles.First(x => x.Index == 1).NoShake = true);
                live.Context.Motion.Get<DynamicBoneStage>().ApplySwitches();   // the next step's switch state, without simulating
                var b1 = HairReport.PartOf(live.Context, 0).Bundles[1];
                Check("HT5 noShake on bundle 1: one full rebuild, its chains noShake and inactive", r2.DynBone == "full" && b1.Chains.Count > 0 && b1.Chains.All(c => c.NoShake && !c.Active),
                      $"dynbone {r2.DynBone}; {string.Join(", ", b1.Chains.Select(c => $"{c.Root} noShake {c.NoShake} active {c.Active}"))}");
                FreshOk("HT5 BR-Chan noShake", h, live);
            }
        }
        h.ReleaseBundles();
        // (b) the chain-transform back hair, three forms (UH3)
        if (!Installed(h, 300, ChainTransformBack, "HT5 chain-transform bundle")) return;
        var forms = new (string name, bool full, bool reapply)[]
        {
            ("full rebuild, no re-application (the commit of before 4.3)", true, false), ("light, no re-application", false, false),
            ("light with the re-application (4.3)", false, true), ("full with the re-application (4.3: noShake / swaps)", true, true),
        };
        foreach (var form in forms)
        {
            using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan (" + form.name + ")")))
            {
                var live = bb.B;
                if (!live.Ok) continue;
                var ctx = live.Context;
                SetItem(live, 0, ChainTransformBack);
                Materialize(ctx, 0);
                var pr0 = HairReport.PartOf(ctx, 0);
                var b0 = pr0.Bundles.FirstOrDefault();
                Info($"{ChainTransformBack}: {pr0.Bundles.Count} bundles; bundle 0 {b0?.Transform}: chains {string.Join(", ", b0?.Chains.Select(c => c.Root + " " + c.Relation) ?? new string[0])}");
                Check($"HT5 {ChainTransformBack} bundle 0: its correction transform is its chain's root", b0 != null && b0.Chains.Any(c => c.Relation == HairReport.RelationRoot));
                DynBoneUnit.ReapplyBundlesAfterRestore = form.reapply;
                bool keepLive = HairUnit.LiveChainRootDrags;
                HairUnit.LiveChainRootDrags = false;   // the commit forms alone (the live drag option is checked in (d))
                try
                {
                    var part = ctx.Chara.Hair.Back;
                    // play-mode-like: the chains simulate (5 frames), then a drag of 3 frames (a preview flush + a frame each)
                    for (int f = 0; f < 5; f++) ctx.Motion.Step(1f / 60);
                    for (int i = 0; i < 3; i++)
                    {
                        Set(live, "chara.hair.back.bundles[0].move", () => { var e = part.Bundles[0]; e.Move = new Vec3(e.Move.X, 0.1f + 0.3f * i, e.Move.Z); }, FlushMode.Preview);
                        ctx.Motion.Step(1f / 60);
                    }
                    if (form.full) ctx.Edit.DynBoneFull = true;
                    var commit = ctx.Editor.Flush(FlushMode.Normal);
                    Info($"{form.name}: commit dynbone {commit.DynBone}");
                    bool mine = form.reapply;
                    if (mine) FreshOk("HT5 chain-transform bundle drag, " + form.name + " (hair digest)", h, live, hairOnly: true);
                    // after the edit flush
                    var trf = Cmp(ctx, 0).boneInfo[0].trfCorrect;
                    var chain = ctx.Motion.Get<DynamicBoneStage>().Chains.FirstOrDefault(c => c.Root == trf);
                    using (var fresh = new Disposer(h.Build(ContainerOf(live), "fresh")))
                    {
                        var fctx = fresh.B.Context;
                        var ftrf = Cmp(fctx, 0).boneInfo[0].trfCorrect;
                        var fchain = fctx.Motion.Get<DynamicBoneStage>().Chains.FirstOrDefault(c => c.Root == ftrf);
                        string Report(string when)
                        {
                            float dp = (trf.localPosition - ftrf.localPosition).magnitude;
                            double da = AngleDeg(trf.localRotation, ftrf.localRotation);
                            float rp = chain != null && fchain != null ? (chain.RestLocalPosition(0) - fchain.RestLocalPosition(0)).magnitude : float.NaN;
                            double ra = chain != null && fchain != null ? AngleDeg(chain.RestLocalRotation(0), fchain.RestLocalRotation(0)) : double.NaN;
                            return $"{when}: correction transform |Δpos| {dp:G4}, angle {da:G4}°; chain rest pose |Δpos| {rp:G4}, angle {ra:G4}°";
                        }
                        string s0 = Report("after the commit flush");
                        bool eq0 = Same(trf.localPosition, ftrf.localPosition) && Same(trf.localRotation, ftrf.localRotation) && chain != null && fchain != null
                                   && Same(chain.RestLocalPosition(0), fchain.RestLocalPosition(0)) && Same(chain.RestLocalRotation(0), fchain.RestLocalRotation(0));
                        for (int f = 0; f < 10; f++) { ctx.Motion.Step(1f / 60); fctx.Motion.Step(1f / 60); }
                        string s10 = Report("after 10 frames");
                        bool eq10 = Near(trf.localPosition, ftrf.localPosition, 1e-5f) && AngleDeg(trf.localRotation, ftrf.localRotation) < 0.01;
                        Info($"UH3 {form.name}: {s0}; {s10}");
                        if (mine)
                            Check($"HT5 / UH3 {form.name}: correction transform and chain rest pose = a fresh build (bits), and after 10 frames (1e-5, 0.01°)", eq0 && eq10, s0 + "; " + s10);
                        else Info($"UH3 {form.name}: equal after the flush {eq0}, after 10 frames {eq10}");
                    }
                }
                finally { DynBoneUnit.ReapplyBundlesAfterRestore = true; HairUnit.LiveChainRootDrags = keepLive; }
            }
            h.ReleaseBundles();
        }
        // (d) the live option (UH2 ≤ 0.3 ms; HairUnit.LiveChainRootDrags, default on): during the drag frames a
        // chain-root correction shows its edit (off: the chain's reset returns it to the old rest pose every frame)
        foreach (bool liveOpt in new[] { true, false })
        {
            bool keepLive = HairUnit.LiveChainRootDrags;
            HairUnit.LiveChainRootDrags = liveOpt;
            try
            {
                using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan (live drag option " + (liveOpt ? "on" : "off") + ")")))
                {
                    var live = bb.B;
                    if (!live.Ok) continue;
                    var ctx = live.Context;
                    SetItem(live, 0, ChainTransformBack);
                    Materialize(ctx, 0);
                    var part = ctx.Chara.Hair.Back;
                    var bi = Cmp(ctx, 0).boneInfo[0];
                    for (int f = 0; f < 5; f++) ctx.Motion.Step(1f / 60);
                    int shown = 0;
                    var seen = new List<string>();
                    for (int i = 0; i < 3; i++)
                    {
                        float y = 0.1f + 0.3f * i;
                        Set(live, "chara.hair.back.bundles[0].move", () => { var e = part.Bundles[0]; e.Move = new Vec3(e.Move.X, y, e.Move.Z); }, FlushMode.Preview);
                        ctx.Motion.Step(1f / 60);
                        ctx.Motion.Step(1f / 60);
                        float want = ShapeEngine.RangeLerp(bi.posMin.y, bi.posMax.y, y);
                        float got = bi.trfCorrect.localPosition.y;
                        if (Mathf.Abs(got - want) < 1e-6f) shown++;
                        seen.Add($"{G(got)} (rate {G(y)} → {G(want)})");
                    }
                    ctx.Editor.Flush(FlushMode.Normal);
                    if (liveOpt)
                    {
                        Check("HT5 live drag option on: a chain-root correction shows each drag frame's rate (2 simulated frames after each preview flush)", shown == 3, string.Join("; ", seen));
                        FreshOk("HT5 live drag option on, after the commit (hair digest)", h, live, hairOnly: true);
                    }
                    else Info($"HT5 live drag option off: drag frames showing the rate {shown} of 3 ({string.Join("; ", seen)})");
                }
            }
            finally { HairUnit.LiveChainRootDrags = keepLive; }
            h.ReleaseBundles();
        }
        // (c) the parity switch HairUnit.ChainRootBundlesAsHS2 (a decision, default off): HS2's look
        bool keepParity = HairUnit.ChainRootBundlesAsHS2;
        try
        {
            using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan (ChainRootBundlesAsHS2)")))
            {
                var live = bb.B;
                if (live.Ok)
                {
                    var ctx = live.Context;
                    HairUnit.ChainRootBundlesAsHS2 = true;
                    SetItem(live, 0, ChainTransformBack);
                    Materialize(ctx, 0);
                    for (int f = 0; f < 3; f++) ctx.Motion.Step(1f / 60);
                    var part = ctx.Chara.Hair.Back;
                    Set(live, "chara.hair.back.bundles[0].move", () => part.Bundles[0].Move = new Vec3(0.9f, 0.1f, 0.8f), FlushMode.Preview);
                    var commit = ctx.Editor.Flush(FlushMode.Normal);
                    var bi = Cmp(ctx, 0).boneInfo[0];
                    var chain = ctx.Motion.Get<DynamicBoneStage>().Chains.FirstOrDefault(c => c.Root == bi.trfCorrect);
                    var probe = new GameObject("probe").transform; probe.localEulerAngles = bi.baseRot; var q = probe.localRotation; Object.DestroyImmediate(probe.gameObject);
                    Check("HT5 ChainRootBundlesAsHS2 on: an edited chain-root correction stays at the prefab's base pose (transform and chain rest pose)",
                          commit.DynBone == "light" && Same(bi.trfCorrect.localPosition, bi.basePos) && Same(bi.trfCorrect.localRotation, q) && chain != null && Same(chain.RestLocalPosition(0), bi.basePos),
                          $"pos {V(bi.trfCorrect.localPosition)} base {V(bi.basePos)}; rest {(chain != null ? V(chain.RestLocalPosition(0)) : "-")}");
                    FreshOk("HT5 ChainRootBundlesAsHS2 on (hair digest)", h, live, hairOnly: true);
                }
            }
        }
        finally { HairUnit.ChainRootBundlesAsHS2 = keepParity; }
        h.ReleaseBundles();
    }

    static double AngleDeg(Quaternion a, Quaternion b)
    {
        double dot = Math.Abs((double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z + (double)a.w * b.w);
        return dot >= 1 ? 0 : 2 * Math.Acos(Math.Min(1, dot)) * 180 / Math.PI;
    }

    // ---------------------------------------------------------------- HT6

    static void HT6(MakerCoreTest.Harness h)
    {
        if (!Installed(h, 300, EmptyRendHairBack, "HT6")) return;
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            var back = ctx.Chara.Hair.Back;
            SetItem(live, 0, EmptyRendHairBack);
            Set(live, "chara.hair.back", () =>
            {
                back.BaseColor = new Color4(0.8f, 0.05f, 0.05f, 1); back.Specular = new Color4(0.11f, 0.22f, 0.33f, 1); back.Metallic = 0.77f; back.Smoothness = 0.66f;
                back.Mesh.Item = "illusion.hs2#5";
            });
            Set(live, "chara.hair.back.accessoryColors", () => back.AccessoryColors = new[] { new Color4(0.05f, 0.7f, 0.1f, 1), Color4.White, Color4.White, Color4.White });
            var pr = HairReport.PartOf(ctx, 0);
            var go = ctx.Hair[0];
            var cmp = Cmp(ctx, 0);
            var acs = new HashSet<Renderer>(cmp?.rendAccessory?.Where(r => r) ?? new Renderer[0]);
            // the prefab's own materials (the renderers by name): what the URP mapping of the bundle material holds; on a
            // rendAccessory slot only _Color (the decoration colour 1) may differ
            var e = RowOf(h.Catalog, 300, EmptyRendHairBack);
            var prefab = e != null ? h.Catalog.LoadAsset<GameObject>(e, "MainAB", "MainData") : null;
            var pRends = prefab ? prefab.GetComponentsInChildren<Renderer>(true).ToList() : new List<Renderer>();
            int n = 0, kept = 0, shaderKept = 0;
            var bad = new List<string>();
            string[] colours = { "_Color", "_TopColor", "_UnderColor", "_Specular", "_MeshColor_01" };
            string[] floats = { "_Metallic", "_Smoothness" };
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var pr0 = pRends.FirstOrDefault(x => x.name == r.name);
                var mats = r.sharedMaterials; var pm = pr0 ? pr0.sharedMaterials : new Material[0];
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!m) continue;
                    n++;
                    var p = i < pm.Length ? pm[i] : null;
                    if (!p) { if (bad.Count < 4) bad.Add($"{r.name}[{i}] no prefab material"); continue; }
                    bool same = colours.Where(c => !(acs.Contains(r) && c == "_Color")).All(c => !m.HasProperty(c) || !p.HasProperty(c) || Near(m.GetColor(c), p.GetColor(c)))
                                && floats.All(f => !m.HasProperty(f) || !p.HasProperty(f) || m.GetFloat(f) == p.GetFloat(f))
                                && TexName(m, "_MeshColorMask_B") == TexName(p, "_MeshColorMask_B");
                    if (same) kept++; else if (bad.Count < 4) bad.Add($"{r.name}[{i}] values differ from the prefab's");
                    bool mapped = p.shader && HS2Next.ShaderMap.TryResolve(p.shader, out var rn) && m.shader.name == rn;
                    bool fallback = p.shader && !HS2Next.ShaderMap.TryResolve(p.shader, out _) && (m.shader.name == "Universal Render Pipeline/Lit" || m.shader.name == p.shader.name);
                    if (mapped || fallback) shaderKept++;
                    else if (bad.Count < 4) bad.Add($"{r.name}[{i}] {m.shader.name} (prefab {p.shader?.name})");
                }
            }
            Check("HT6 " + EmptyRendHairBack + ": rendHair empty (HairReport), no mesh written", pr.RendHairEmpty && !pr.Mesh.Written, $"rendHair empty {pr.RendHairEmpty}");
            Check("HT6 every renderer of the part keeps the prefab's materials (URP-mapped): no hair shader pass, no hair colours, no metallic / smoothness, no mesh (GH8, J15)",
                  n > 0 && kept == n && shaderKept == n, $"{n} materials: values kept {kept}, shader = the prefab's mapping {shaderKept}; " + string.Join(" | ", bad));
            int deco = pr.Decoration.Count(d => d.Colors[0].HasValue && Near(d.Colors[0].Value, new Color(0.05f, 0.7f, 0.1f, 1)));
            Check("HT6 the decoration colour is still written (rendAccessory)", pr.Decoration.Count > 0 && deco == pr.Decoration.Count, $"{deco} of {pr.Decoration.Count}");
            FreshOk("HT6", h, live);
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- HT7

    static void HT7(MakerCoreTest.Harness h)
    {
        var c = MakerEditTest.Import(h, FemaleCard, out string err);
        if (!Check("HT7 BR-Chan imported", c != null, err)) return;
        if (MakerServices.Hair == null) MakerServices.Hair = HairInfo.Instance;
        using (var n = MakerEditTest.NewNode(h, c, "BR-Chan"))
        {
            if (!Check("HT7 node built", n.Ok, n.Error)) return;
            HairInfo.Track(n.Runtime);
            bool f0 = HairInfo.TryPart(n.Runtime, n.Id, 0, out var a0);
            Check("HT7 facts of the back hair (BR-Chan #11): prefab p_c_hair_b_12_02, 3 bundles, no decoration", f0 && a0.HasPrefab && a0.PrefabName == "p_c_hair_b_12_02" && a0.Bundles.Length == 3 && !a0.UseAcsColor01,
                  $"{a0.PrefabName} bundles {a0.Bundles.Length} acs01 {a0.UseAcsColor01}");
            bool g0 = n.Ops.MakerGateHolds("maker.hair:back:acs1", n.Id), gb0 = n.Ops.MakerGateHolds("maker.hair:back:bundles", n.Id);
            var o = new ExecuteOptions { Source = CommandSource.User };
            var res = n.Ops.ChangeItem(new[] { n.Id }, "appearance.chara.hair.back.item", "illusion.hs2#2", o);
            n.Apply();
            bool f1 = HairInfo.Instance.TryPart(n.Id, 0, out var a1);
            var hp = n.Stored.Chara.Hair.Back;
            Check("HT7 ChangeItem 300#2: one entry, bundles cleared, decoration colour 1 = acsDefColor", res.Ok && hp.Bundles.Count == 0 && Mathf.Abs(hp.AccessoryColors[0].R - 0.3892f) < 2e-3f,
                  $"ok {res.Ok}; bundles {hp.Bundles.Count}; acs0 ({G(hp.AccessoryColors[0].R)}, {G(hp.AccessoryColors[0].G)}, {G(hp.AccessoryColors[0].B)})");
            Check("HT7 facts after the flush: p_c_hair_b_03_01, 1 bundle, decoration colour 1 (acsDefColor 1)", f1 && a1.PrefabName == "p_c_hair_b_03_01" && a1.Bundles.Length == 1 && a1.AcsShown(1) && !a1.AcsShown(2),
                  $"{a1.PrefabName} bundles {a1.Bundles.Length} acs {a1.AcsDefColorCount}");
            bool g1 = n.Ops.MakerGateHolds("maker.hair:back:acs1", n.Id), gb1 = n.Ops.MakerGateHolds("maker.hair:back:bundles", n.Id);
            Check("HT7 gates follow: maker.hair:back:acs1 false → true, :bundles true → true", !g0 && g1 && gb0 && gb1, $"acs1 {g0} → {g1}, bundles {gb0} → {gb1}");
            var pr = HairReport.PartOf(n.Real.Context, 0);
            Check("HT7 HairReport: default rates applied (nothing stored)", pr.Bundles.Count == 1 && !pr.Bundles[0].HasStored && Same(pr.Bundles[0].EffectiveMove, pr.Bundles[0].DefaultMove), V(pr.Bundles[0].EffectiveMove));
            Check("HT7 a node id that is not built: no facts", !HairInfo.Instance.TryPart("0123456789abcdef0123456789abcdef", 0, out var none) && !none.HasPrefab);
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- HT8

    static void HT8(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, MaleCard, "HERO107")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            foreach (var (id, prefab, bundles) in new[] { ("illusion.hs2#22", "p_cm_hair_b_19_00", 0), ("illusion.hs2#59", "p_cm_hair_b_39_00", 2), ("illusion.hs2#51", "p_cm_hair_b_hage01", 0) })
            {
                SetItem(live, 0, id);
                var pr = HairReport.PartOf(ctx, 0);
                int n = Cmp(ctx, 0)?.boneInfo?.Count ?? -1;
                Check($"HT8 HERO107 back {id}: the male prefab {prefab} with {bundles} bundles", pr.Prefab == prefab && pr.Bundles.Count == bundles && n == bundles && pr.HasCmpHair,
                      $"prefab {pr.Prefab}, bundles {pr.Bundles.Count}, boneInfo {n}");
                FreshOk("HT8 " + id, h, live);
            }
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- HT9

    static void HT9(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            var hair = ctx.Chara.Hair;
            FaceDigest.Result D() => FaceDigest.Compute(ctx, "hair", false);
            var d0 = D();
            Check("HT9 the hair digest has materials / bones / chains; stable", d0.Chains != null && d0.Materials != null && d0.Equals(D()), d0.ToString());
            Check("HT9 the face digest has no chains section", FaceDigest.Compute(ctx, "face", false).Chains == null && FaceDigest.Compute(ctx, "all", false).Chains == d0.Chains);
            Check("HT9 hair texts: renderers under the four parts, bundle bones, chains", FaceDigest.MaterialsText(ctx, "hair").Contains("renderer hair1 ") && FaceDigest.BonesText(ctx, "hair").Contains("hair0 0 euler")
                  && FaceDigest.ChainsText(ctx, "hair").Contains("chain hair0 bundle 0"));
            void Expect(string what, string path, Action set, bool materials, bool bones, bool chains)
            {
                var a = D();
                Set(live, path, set);
                var b = D();
                Check($"HT9 {what}: materials {(materials ? "change" : "stay")}, bones {(bones ? "change" : "stay")}, chains {(chains ? "change" : "stay")}",
                      (a.Materials != b.Materials) == materials && (a.Bones != b.Bones) == bones && (a.Chains != b.Chains) == chains,
                      $"materials {a.Materials.Substring(0, 8)}→{b.Materials.Substring(0, 8)}, bones {a.Bones.Substring(0, 8)}→{b.Bones.Substring(0, 8)}, chains {a.Chains.Substring(0, 8)}→{b.Chains.Substring(0, 8)}");
            }
            Expect("mesh layout", "chara.hair.front.mesh.layout", () => hair.Front.Mesh.Layout = new Vec4(0.3f, 0.6f, 0.2f, 0.8f), true, false, false);
            Expect("base colour", "chara.hair.back.baseColor", () => hair.Back.BaseColor = new Color4(0.4f, 0.3f, 0.2f, 1), true, false, false);
            Expect("draw type", "chara.hair.shaderType", () => hair.ShaderType = 1, true, false, false);
            Expect("bundle rate (vanilla: above its chains)", "chara.hair.back.bundles[0].rotate", () => hair.Back.Bundles.First(b => b.Index == 0).Rotate = new Vec3(0.1f, 0.9f, 0.4f), false, true, false);
            Expect("noShake", "chara.hair.back.bundles[0].noShake", () => hair.Back.Bundles.First(b => b.Index == 0).NoShake = true, false, false, true);
            if (Installed(h, 300, ChainTransformBack, "HT9 chain-transform bundle"))
            {
                SetItem(live, 0, ChainTransformBack);
                Materialize(ctx, 0);
                Expect("bundle rate (a chain-root correction transform)", "chara.hair.back.bundles[0].move", () => hair.Back.Bundles[0].Move = new Vec3(0.2f, 0.8f, 0.3f), false, true, true);
            }
            FreshOk("HT9 after the edits", h, live, true);
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- HT11 (package E's HS2 compare)

    static void HT11(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            var back = ctx.Chara.Hair.Back;
            // (1) _Mesh_onoff of 300#58's back materials: the serialized value of the prefab material HS2Next loads, kept by
            // the material copy and the URP mapping (HS2 serves another copy of the bundle)
            SetItem(live, 0, "illusion.hs2#58");
            var row = RowOf(h.Catalog, 300, "illusion.hs2#58");
            var prefab = row != null ? h.Catalog.LoadAsset<GameObject>(row, "MainAB", "MainData") : null;
            var pm = prefab ? prefab.GetComponent<CmpHair>()?.rendHair?.Where(r => r).SelectMany(r => r.sharedMaterials).ToList() : null;
            var im = HairSlots(ctx, 0).Select(s => s.m).ToList();
            string vals = string.Join(", ", im.Select(m => m.name + " " + G(m.GetFloat("_Mesh_onoff"))));
            string pvals = pm == null ? "-" : string.Join(", ", pm.Select(m => m.name + " " + (m.HasProperty("_Mesh_onoff") ? G(m.GetFloat("_Mesh_onoff")) : "absent")));
            Check("HT11 300#58 back: _Mesh_onoff on every slot = the loaded prefab material's serialized value (kept by the copy and the URP mapping)",
                  pm != null && pm.Count == im.Count && pm.Zip(im, (a, b) => a.HasProperty("_Mesh_onoff") && a.GetFloat("_Mesh_onoff") == b.GetFloat("_Mesh_onoff")).All(x => x),
                  $"instance {vals}; prefab {pvals}");
            // (2) 300#2's shadow renderer (rendHair, Default-Material / Standard): HS2's load order with ColoursBeforeShaderAsHS2
            bool keep = HairUnit.ColoursBeforeShaderAsHS2;
            try
            {
                Set(live, "chara.hair.back", () =>
                {
                    back.Item = "illusion.hs2#2"; back.Bundles.Clear();
                    back.BaseColor = new Color4(0.6f, 0.282f, 0.282f, 1); back.TopColor = new Color4(0.2f, 0.2f, 0.2f, 1); back.UnderColor = new Color4(0.88f, 0.88f, 0.88f, 1);
                    back.Specular = new Color4(0.91f, 0.88816f, 0.8372f, 1); back.Metallic = 0.31f; back.Smoothness = 0.38f;
                });
                Material Shadow() => HairSlots(ctx, 0).Select(s => s.m).FirstOrDefault(m => m && m.name.StartsWith("Default-Material", StringComparison.Ordinal));
                string Text(Material m) => m == null ? "-" : $"_Color {m.GetColor("_Color")} _TopColor {m.GetColor("_TopColor")} _UnderColor {m.GetColor("_UnderColor")} _Specular {m.GetColor("_Specular")} _Metallic {G(m.GetFloat("_Metallic"))} _Smoothness {G(m.GetFloat("_Smoothness"))}";
                var s0 = Shadow();
                Check("HT11 300#2 (switch off, default): the shadow renderer is in rendHair and takes the stored colours", s0 != null && Near(s0.GetColor("_UnderColor"), new Color(0.88f, 0.88f, 0.88f, 1)) && Mathf.Abs(s0.GetFloat("_Smoothness") - 0.38f) < 1e-6f, Text(s0));
                HairUnit.ColoursBeforeShaderAsHS2 = true;
                ctx.Editor.MarkUnit("hair.back"); ctx.Editor.Flush();
                var s1 = Shadow();
                var hs2Under = new Color(0f, 0.10449924319982529f, 1f, 1f); var hs2Spec = new Color(0.15499281883239746f, 0.6405537128448486f, 0.7641509771347046f, 1f);
                bool hs2Look = s1 != null && Near(s1.GetColor("_UnderColor"), hs2Under) && Near(s1.GetColor("_Specular"), hs2Spec) && s1.GetFloat("_Smoothness") == 0f
                               && Near(s1.GetColor("_Color"), new Color(0.6f, 0.282f, 0.282f, 1)) && Mathf.Abs(s1.GetFloat("_Metallic") - 0.31f) < 1e-6f;
                var other = HairSlots(ctx, 0).Select(s => s.m).First(m => m && m != s1);
                Check("HT11 300#2 with ColoursBeforeShaderAsHS2: the shadow material = HS2's record (_UnderColor (0, 0.1045, 1), _Specular (0.155, 0.641, 0.764), _Smoothness 0 = hair_07's defaults; _Color / _Metallic stored), the hair materials keep the stored colours",
                      hs2Look && Near(other.GetColor("_UnderColor"), new Color(0.88f, 0.88f, 0.88f, 1)), Text(s1));
                FreshOk("HT11 ColoursBeforeShaderAsHS2 on", h, live);
                HairUnit.ColoursBeforeShaderAsHS2 = false;
                ctx.Editor.MarkUnit("hair.back"); ctx.Editor.Flush();
                FreshOk("HT11 ColoursBeforeShaderAsHS2 back off", h, live);
            }
            finally { HairUnit.ColoursBeforeShaderAsHS2 = keep; }
            // (3) [SH]hairs.collect.v4#1: a chain on the prefab root (no bundle's list) holds bundles 0 / 1's correction transforms
            if (Installed(h, 300, RootChainBack, "HT11 root chain"))
            {
                SetItem(live, 0, RootChainBack);
                var pr = HairReport.PartOf(ctx, 0);
                var held = pr.OtherChains.Where(c => c.Relation == HairReport.RelationInside || c.Relation == HairReport.RelationRoot).ToList();
                Check($"HT11 {RootChainBack}: chains outside the bundle lists hold the correction transforms (relation inside / root)",
                      held.Count > 0 && pr.Bundles.Take(2).All(b => held.Any(c => c.Transform == b.Transform)),
                      string.Join("; ", pr.OtherChains.Select(c => $"{c.Root} {c.Relation} {c.Transform ?? "-"}")) + "; bundles " + string.Join(", ", pr.Bundles.Select(b => b.Transform)));
                FreshOk("HT11 " + RootChainBack, h, live);
                var cmp = Cmp(ctx, 0);
                string Pose() => string.Join("; ", cmp.boneInfo.Take(2).Select((bi, i) => $"bundle {i} pos {V(bi.trfCorrect.localPosition)} euler {V(bi.trfCorrect.localEulerAngles)} (base {V(bi.baseRot)})"));
                string p0 = Pose();
                for (int f = 0; f < 60; f++) ctx.Motion.Step(1f / 60);
                string p60 = Pose();
                var b0 = cmp.boneInfo[0];
                double off = AngleDeg(b0.trfCorrect.localRotation, Quaternion.Euler(b0.baseRot));
                Info($"HT11 {RootChainBack} before simulating: {p0}");
                Info($"HT11 {RootChainBack} after 60 simulated frames: {p60}; bundle 0 off its lerp (base) by {off:F2}° (HS2's record: Euler (358.57, 348.57, 339.44), 15° off)");
                Check($"HT11 {RootChainBack}: the simulation moves the correction transforms (as on HS2), position unchanged", off > 1 && Near(b0.trfCorrect.localPosition, b0.basePos, 1e-5f), $"{off:F2}°");
            }
        }
        h.ReleaseBundles();
        // (4) FK hair names (package H): Studio renames c_J_hairF… / c_J_hairB…[a-b]_N to c_J_hair_F… / _B… (AddObjectAssist
        // .ArrangeNames); HS2Next never renames and matches the normalised name. On BR-Chan (front #50 mixes both spellings)
        // every FK hair row HS2's rename would find has an FK bone here.
        var cont = MakerEditTest.Import(h, FemaleCard, out string importError);
        if (!Check("HT11 FK: BR-Chan imported", cont != null, importError)) return;
        var keepDb = h.Services.Db;
        try
        {
            // the FK rows are the Studio Bone list's: the harness has no Studio lists, so this check loads them for its node
            if (h.Services.Db == null) h.Services.Db = h.Catalog.LoadStudioLists();
            using (var n = MakerEditTest.NewNode(h, cont, "BR-Chan"))
            {
                if (!Check("HT11 FK: node built", n.Ok, n.Error)) return;
                var table = FkDriver.TableFor(n.Real);
                var ctx2 = n.Real.Context;
                var rx = new Regex[] { new Regex(@"c_J_hairF[CLRU]+[a-b]_(\d*)", RegexOptions.IgnoreCase), new Regex(@"c_J_hairB[CLRU]+[a-b]_(\d*)", RegexOptions.IgnoreCase) };
                string Hs2Name(string s) => rx[0].IsMatch(s) ? s.Replace("c_J_hairF", "c_J_hair_F") : rx[1].IsMatch(s) ? s.Replace("c_J_hairB", "c_J_hair_B") : s;
                // the names HS2's FK hair discovery sees: the back / front hair objects renamed, everything else under the head as is
                var renamed = new HashSet<Transform>(Enumerable.Range(0, 2).Where(k => ctx2.Hair[k]).SelectMany(k => ctx2.Hair[k].GetComponentsInChildren<Transform>(true)));
                var headParent = ctx2.HeadBone ? ctx2.HeadBone.transform.parent : null;
                var all = headParent ? headParent.GetComponentsInChildren<Transform>(true) : new Transform[0];
                var hs2Names = new HashSet<string>(all.Select(t => renamed.Contains(t) ? Hs2Name(t.name) : t.name), StringComparer.Ordinal);
                int legacy = renamed.Count(t => Hs2Name(t.name) != t.name);
                int rows = table == null ? -1 : table.Stats.Rows[(int)HS2Next.Motion.Fk.FkGroup.Hair];
                var found = table == null ? new List<HS2Next.Motion.Fk.FkBone>() : table.Of(HS2Next.Motion.Fk.FkGroup.Hair).ToList();
                var notFound = table == null ? new List<string>() : table.Stats.NotFoundNames.Where(s => s.StartsWith("c_J_hair", StringComparison.Ordinal)).ToList();
                var missed = notFound.Where(hs2Names.Contains).ToList();
                var extra = found.Where(b => !hs2Names.Contains(b.Row.Name)).Select(b => b.Row.Name).ToList();
                int viaLegacy = found.Count(b => b.Bone && b.Bone.name != b.Row.Name);
                Check("HT11 FK hair: the FK hair bones = the rows HS2's rename finds (BR-Chan back #11, front #50; none missed, none extra)",
                      table != null && rows > 0 && found.Count > 0 && missed.Count == 0 && extra.Count == 0,
                      $"hair rows {rows}, FK hair bones {found.Count} ({viaLegacy} on a transform spelled without the underscore), rows without a transform {notFound.Count}; " +
                      $"transforms HS2 renames {legacy}; missed {missed.Count} {string.Join(", ", missed.Take(8))}; extra {extra.Count} {string.Join(", ", extra.Take(8))}");
            }
        }
        finally { h.Services.Db = keepDb; }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- HT10

    sealed class Agg
    {
        public int Prefabs, Bundles, Empty, Beyond, Multi;
        public readonly SortedDictionary<string, int> Flags = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<int, int> PerPrefab = new SortedDictionary<int, int>();
        public void Flag(string f) => Flags[f] = Flags.TryGetValue(f, out var n) ? n + 1 : 1;
        public override string ToString() => $"prefabs {Prefabs}; flags {{{string.Join(", ", Flags.Select(kv => kv.Key + ": " + kv.Value))}}}; rendHair empty {Empty}; beyond {Beyond}; multi-slot {Multi}; bundles {Bundles}, per prefab {{{string.Join(", ", PerPrefab.Select(kv => kv.Key + ": " + kv.Value))}}}";
    }

    /// <summary>The vanilla aggregates of Tools/survey/maker_hair_census.txt for a category.</summary>
    static Agg CensusOf(int category, out string error)
    {
        error = null;
        var a = new Agg();
        string path = Path.Combine(ContentSettings.ProjectRoot, "Tools", "survey", "maker_hair_census.txt");
        if (!File.Exists(path)) { error = "no census file"; return null; }
        var lines = File.ReadAllLines(path);
        int i = Array.FindIndex(lines, l => l.StartsWith(category + " vanilla: prefabs ", StringComparison.Ordinal));
        if (i < 0 || i + 1 >= lines.Length) { error = "no census line for " + category; return null; }
        var m = Regex.Match(lines[i], @"prefabs (\d+); flags \{([^}]*)\}; rendHair empty (\d+); useAcsColor0N beyond acsDefColor (\d+); rendAccessory with > 1 material slot (\d+)");
        var mb = Regex.Match(lines[i + 1], @"bundles: (\d+) on \d+ prefabs, per prefab \{([^}]*)\}");
        if (!m.Success || !mb.Success) { error = "census lines do not parse"; return null; }
        a.Prefabs = int.Parse(m.Groups[1].Value);
        foreach (Match f in Regex.Matches(m.Groups[2].Value, @"'([^']+)': (\d+)")) a.Flags[f.Groups[1].Value] = int.Parse(f.Groups[2].Value);
        a.Empty = int.Parse(m.Groups[3].Value); a.Beyond = int.Parse(m.Groups[4].Value); a.Multi = int.Parse(m.Groups[5].Value);
        a.Bundles = int.Parse(mb.Groups[1].Value);
        foreach (Match f in Regex.Matches(mb.Groups[2].Value, @"(\d+): (\d+)")) a.PerPrefab[int.Parse(f.Groups[1].Value)] = int.Parse(f.Groups[2].Value);
        return a;
    }

    static void HT10(MakerCoreTest.Harness h)
    {
        var defaults = new PartDefaults(h.Catalog);
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            // worked examples
            var b11 = HairReport.PartOf(ctx, 0).Bundles.FirstOrDefault();
            Check("HT10 BR-Chan back #11 bundle 0 defaults (0.5, 0.2941, 0.3333) / (0.5, 0.5, 0.5) (1e-4)", b11 != null && Near(b11.DefaultMove, new Vector3(0.5f, 0.2941f, 0.3333f), 1e-4f) && Near(b11.DefaultRotate, new Vector3(0.5f, 0.5f, 0.5f), 1e-4f),
                  b11 == null ? "none" : V(b11.DefaultMove) + " / " + V(b11.DefaultRotate));
            for (int k = 0; k < 4; k++)
            {
                int cat = 300 + k;
                var exp = CensusOf(cat, out string cerr);
                if (!Check($"HT10 {cat}: census aggregates read", exp != null, cerr)) continue;
                var got = new Agg();
                int pdOk = 0, pdN = 0, defOk = 0, defN = 0; var pdBad = new List<string>(); var defBad = new List<string>();
                foreach (var e in h.Catalog.EntriesOf(cat).Where(e => e.IsVanilla && e["MainData"] != "p_dummy" && !string.IsNullOrEmpty(e["MainData"])).ToList())
                {
                    if (MakerHairListsHidden(e)) continue;
                    SetItem(live, k, e.Key.ToString());
                    var pr = HairReport.PartOf(ctx, k);
                    if (!pr.HasCmpHair) continue;
                    got.Prefabs++;
                    if (pr.UseTopColor) got.Flag("useTopColor"); else got.Flag("useTopColor false");
                    if (pr.UseUnderColor) got.Flag("useUnderColor"); else got.Flag("useUnderColor false");
                    if (pr.UseMesh) got.Flag("useMesh");
                    if (pr.UseSameSkinColorButton) got.Flag("useSameSkinColorButton");
                    if (pr.UseAcsColor01) got.Flag("useAcsColor01");
                    if (pr.UseAcsColor02) got.Flag("useAcsColor02");
                    if (pr.UseAcsColor03) got.Flag("useAcsColor03");
                    if (pr.RendHairEmpty) got.Empty++;
                    if ((pr.UseAcsColor01 && pr.AcsDefColorCount < 1) || (pr.UseAcsColor02 && pr.AcsDefColorCount < 2) || (pr.UseAcsColor03 && pr.AcsDefColorCount < 3)) got.Beyond++;
                    if (pr.Decoration.GroupBy(d => d.Renderer).Any(g => g.Count() > 1)) got.Multi++;
                    got.Bundles += pr.Bundles.Count;
                    got.PerPrefab[pr.Bundles.Count] = got.PerPrefab.TryGetValue(pr.Bundles.Count, out var c) ? c + 1 : 1;
                    // = PartDefaults.TryHairBundles (the prefab read without instantiating it; package B)
                    pdN++;
                    var facts = HairInfo.FactsOf(pr);
                    if (defaults.TryHairBundles(cat, e.Key.ToString(), 1, out var pdf) && pdf.Length == facts.Bundles.Length &&
                        pdf.Zip(facts.Bundles, (p, q) => p.Index == q.Index && p.Shown == q.Shown && p.Title == q.Title && p.DefaultMove.Equals(q.DefaultMove) && p.DefaultRotate.Equals(q.DefaultRotate)
                                                          && p.PosMin.Equals(q.PosMin) && p.RotMax.Equals(q.RotMax) && p.BasePos.Equals(q.BasePos)).All(x => x)) pdOk++;
                    else if (pdBad.Count < 4) pdBad.Add(e.Key + ": " + (pdf == null ? "no facts" : pdf.Length + " vs " + facts.Bundles.Length));
                    // the ops' defaults (MakerEdits.InverseLerp through HairBundleFacts) = the unit's (HairUnit.DefaultRates), bit for bit
                    defN += pr.Bundles.Count;
                    for (int i = 0; i < pr.Bundles.Count && i < facts.Bundles.Length; i++)
                    {
                        var u = pr.Bundles[i]; var f = facts.Bundles[i];
                        if (Same(u.DefaultMove, new Vector3(f.DefaultMove.X, f.DefaultMove.Y, f.DefaultMove.Z)) && Same(u.DefaultRotate, new Vector3(f.DefaultRotate.X, f.DefaultRotate.Y, f.DefaultRotate.Z))) defOk++;
                        else if (defBad.Count < 4) defBad.Add($"{e.Key} bundle {i}: unit {V(u.DefaultMove)} / {V(u.DefaultRotate)}, ops ({G(f.DefaultMove.X)}, {G(f.DefaultMove.Y)}, {G(f.DefaultMove.Z)}) / ({G(f.DefaultRotate.X)}, {G(f.DefaultRotate.Y)}, {G(f.DefaultRotate.Z)})");
                    }
                }
                Check($"HT10 {cat} vanilla: the ops' default rates (HairBundleFacts) = the unit's, bit for bit", defOk == defN, $"{defOk} of {defN}" + (defBad.Count > 0 ? "; " + string.Join(" | ", defBad) : ""));
                // useMesh apart: the census reads a vanilla row's bundle from the first zipmod that ships its path ([B17] HairMod v1
                // ships 14 vanilla back / side bundles with useMesh on), the catalog reads abdata (finding A43-10)
                int UseMesh(Agg x) => x.Flags.TryGetValue("useMesh", out var um) ? um : 0;
                if (UseMesh(exp) != UseMesh(got)) Warn($"HT10 {cat} vanilla: useMesh", $"census {UseMesh(exp)}, HS2Next (abdata bundles) {UseMesh(got)} — the census read zipmod copies of vanilla bundles");
                bool flagsEq = exp.Flags.Where(kv => kv.Key != "useMesh").All(kv => got.Flags.TryGetValue(kv.Key, out var v) && v == kv.Value)
                               && got.Flags.Where(kv => kv.Key != "useMesh").All(kv => exp.Flags.ContainsKey(kv.Key));
                bool perEq = exp.PerPrefab.Count == got.PerPrefab.Count && exp.PerPrefab.All(kv => got.PerPrefab.TryGetValue(kv.Key, out var v) && v == kv.Value);
                Check($"HT10 {cat} vanilla: HairReport facts = the census (prefabs, flags but useMesh, rendHair empty, beyond, multi-slot, bundles per prefab)",
                      exp.Prefabs == got.Prefabs && flagsEq && exp.Empty == got.Empty && exp.Beyond == got.Beyond && exp.Multi == got.Multi && exp.Bundles == got.Bundles && perEq,
                      $"census {exp}; HS2Next {got}");
                Check($"HT10 {cat} vanilla: the realized bundles = PartDefaults.TryHairBundles (limits, base, defaults, titles; exact)", pdN > 0 && pdOk == pdN, $"{pdOk} of {pdN}" + (pdBad.Count > 0 ? "; " + string.Join(" | ", pdBad) : ""));
            }
        }
        h.ReleaseBundles();
    }

    /// <summary>Rows the Maker hides (Possess 99, as the census and the Maker lists).</summary>
    static bool MakerHairListsHidden(ListEntry e) => e["Possess"] == "99";

    // ================================================================ RunRender (graphics)

    public static void RunRender()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"maker hair render (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName})");
        bool device = FaceDigest.HasDevice;
        Check("graphics device present", device, SystemInfo.graphicsDeviceType.ToString());
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        var keepRp = GraphicsSettings.defaultRenderPipeline; var keepQ = QualitySettings.renderPipeline;
        try
        {
            if (device)
                using (var h = MakerCoreTest.Harness.Open())
                using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
                {
                    var live = bb.B;
                    if (live.Ok)
                    {
                        var ctx = live.Context;
                        var hair = ctx.Chara.Hair;
                        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HS2Next.App.AppPipeline.MeasureAssetPath);
                        Check("URP asset", asset != null);
                        GraphicsSettings.defaultRenderPipeline = asset; QualitySettings.renderPipeline = asset;
                        // R1: the materials draw (replacement shaders supported, the mesh texture bound) after the HT1 / HT4 edits
                        Set(live, "chara.hair.front.mesh.item", () => hair.Front.Mesh.Item = "illusion.hs2#5");
                        Set(live, "chara.hair.shaderType", () => hair.ShaderType = 1);
                        var mats = Enumerable.Range(0, 4).SelectMany(k => HairSlots(ctx, k).Select(s => s.m)).ToList();
                        int unsupported = mats.Count(m => !m.shader || !m.shader.isSupported || !m.shader.name.StartsWith("HS2Next/"));
                        int bound = HairSlots(ctx, 1).Count(s => s.m.GetTexture("_MeshColorMask_B") is Texture2D t && t && t.name == "mesh_t_05");
                        Check("R1 hair materials after the edits: the supported replacement shaders, the front's mesh texture bound on every slot", mats.Count > 0 && unsupported == 0 && bound == HairSlots(ctx, 1).Count(),
                              $"{mats.Count} materials, unsupported / not replaced {unsupported}, mesh_t_05 bound {bound}");
                        // R2: 64² RT of the front hair, rendered twice → equal hashes; with the pattern none the hash differs (information)
                        string a = RenderFront(ctx), b = RenderFront(ctx);
                        Check("R2 a 64² RT of the front hair rendered twice: equal hashes", a != null && a == b, $"{Short(a, 16)} / {Short(b, 16)}");
                        Set(live, "chara.hair.front.mesh.item", () => hair.Front.Mesh.Item = "illusion.hs2#0");
                        string c = RenderFront(ctx);
                        Info($"R2 pattern none vs pattern 5: {(c == a ? "equal" : "different")} hashes ({Short(c, 16)})");
                        Set(live, "chara.hair.shaderType", () => hair.ShaderType = 0);
                        // R3: the hair digest after selective edits = a fresh build's (hair digest and the "all" digest only: the
                        // camera draws of R2 updated skinned-mesh bounds, which the snapshot's rend lines hold)
                        Set(live, "chara.hair.front.mesh.layout", () => hair.Front.Mesh.Layout = new Vec4(0.2f, 0.4f, 0.6f, 0.8f));
                        FreshOk("R3 after the selective edits", h, live, digest: true, hairOnly: true);
                    }
                }
        }
        finally
        {
            FaceSetup.Seed = fseed;
            GraphicsSettings.defaultRenderPipeline = keepRp; QualitySettings.renderPipeline = keepQ;
        }
        Finish("maker_hair_render_report.txt", "MakerHairTest.render", sw);
    }

    /// <summary>Renders the front hair's bounds into a 64² RT (a temporary camera and light) and returns the SHA-256 of the
    /// read-back (numbers only; nothing is saved).</summary>
    static string RenderFront(CharaContext ctx)
    {
        var go = ctx.Hair[1];
        if (!go) return null;
        var rends = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
        if (rends.Count == 0) return null;
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.forceMatrixRecalculationPerRender = true;
        var b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        var cam = new GameObject("MakerHairTest cam").AddComponent<Camera>();
        var key = new GameObject("MakerHairTest key").AddComponent<Light>();
        var rt = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32) { name = "MakerHairTest front" };
        try
        {
            key.type = LightType.Directional; key.intensity = 1f; key.transform.rotation = Quaternion.Euler(30, 200, 0);
            HS2Next.RenderingLayers.ApplyToLight(key);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            cam.fieldOfView = 30; cam.nearClipPlane = 0.05f; cam.farClipPlane = 100;
            float d = Mathf.Max(b.extents.magnitude, 0.1f) / Mathf.Tan(15 * Mathf.Deg2Rad);
            cam.transform.position = b.center + Vector3.forward * d;   // HS2 characters face +Z
            cam.transform.LookAt(b.center);
            cam.targetTexture = rt;
            cam.Render(); cam.Render();
            cam.targetTexture = null;
            var req = AsyncGPUReadback.Request(rt, 0);
            req.WaitForCompletion();
            if (req.hasError) return "readback-error";
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(req.GetData<byte>().ToArray())).Replace("-", "").ToLowerInvariant();
        }
        finally
        {
            cam.targetTexture = null;
            rt.Release(); Object.DestroyImmediate(rt);
            Object.DestroyImmediate(cam.gameObject); Object.DestroyImmediate(key.gameObject);
        }
    }

    // ================================================================ RunTiming (graphics; its own Unity session)

    static void Target(string what, double ms, double target, string reason = null)
    {
        string line = $"{what}: median {F(ms)} ms (target ≤ {F(target)} ms)";
        if (double.IsNaN(ms)) { Warn(what, "not measured" + (reason != null ? " (" + reason + ")" : "")); return; }
        if (ms <= target) Check(line, true);
        else if (ms <= 2 * target || reason != null) Warn(line, reason ?? "above the target (≤ 2×)");
        else Check(line, false, "above 2× the target");
    }

    static double TimeFlush(MakerCoreTest.Built live, int reps, Func<int, string> step, FlushMode mode = FlushMode.Normal, Action after = null, List<FlushReport> reports = null)
    {
        var ed = live.Context.Editor;
        var l = new List<double>();
        for (int i = 0; i < reps; i++)
        {
            string path = step(i);
            if (path != null) ed.MarkDirty(path);
            var sw = Stopwatch.StartNew();
            var r = ed.Flush(mode);
            l.Add(sw.Elapsed.TotalMilliseconds);
            reports?.Add(r);
            after?.Invoke();
        }
        return Median(l);
    }

    public static void RunTiming()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        int reps = int.Parse(Arg("-reps", "20"), CultureInfo.InvariantCulture);
        rep.AppendLine($"maker hair timing (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName}), {reps} repetitions, warm, edit mode (CPU)");
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        try
        {
            using (var h = MakerCoreTest.Harness.Open())
            {
                using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
                {
                    var live = bb.B;
                    if (live.Ok) TimingBrChan(h, live, reps);
                }
                h.ReleaseBundles();
                // UH2 with a 37-bundle mod hair
                if (Installed(h, 300, ManyBundlesBack, "UH2 37 bundles"))
                    using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan + " + ManyBundlesBack)))
                    {
                        var live = bb.B;
                        if (live.Ok)
                        {
                            SetItem(live, 0, ManyBundlesBack);
                            Materialize(live.Context, 0);
                            UH2(live, reps, ManyBundlesBack);
                        }
                    }
                h.ReleaseBundles();
                // UH2 with a chain-transform back hair (the live drag option re-derives its chains in the preview flushes)
                if (Installed(h, 300, ChainTransformBack, "UH2 chain-transform bundles"))
                    using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan + " + ChainTransformBack)))
                    {
                        var live = bb.B;
                        if (live.Ok)
                        {
                            SetItem(live, 0, ChainTransformBack);
                            Materialize(live.Context, 0);
                            UH2(live, reps, ChainTransformBack);
                        }
                    }
                h.ReleaseBundles();
            }
        }
        finally { FaceSetup.Seed = fseed; }
        Finish("maker_hair_timing_report.txt", "MakerHairTest.timing (UH2, UH4, UH6)", sw);
    }

    static void TimingBrChan(MakerCoreTest.Harness h, MakerCoreTest.Built live, int reps)
    {
        var ctx = live.Context;
        var hair = ctx.Chara.Hair;
        var ed = ctx.Editor;
        // warm-up
        for (int i = 0; i < 3; i++) { ed.MarkDirty("chara.hair.back.baseColor"); ed.Flush(); }
        // colour drag frame: the four parts (same setting) → materials only
        double colour = TimeFlush(live, reps, i =>
        {
            var c = new Color4(0.2f + 0.01f * i, 0.3f, 0.4f, 1);
            for (int k = 0; k < 4; k++) { hair.Parts[k].BaseColor = c; ed.MarkDirty("chara.hair." + Parts[k] + ".baseColor"); }
            return null;
        }, FlushMode.Preview);
        Target("hair colour drag frame (four parts, materials only; preview flush)", colour, 0.5);
        // bundle drags (UH2) on BR-Chan's back hair
        UH2(live, reps, "BR-Chan back #11");
        // mesh layout / colour drag
        double layout = TimeFlush(live, reps, i => { hair.Front.Mesh.Layout = new Vec4(0.5f, 0.5f + 0.01f * i, 0.2f, 0.3f); return "chara.hair.front.mesh.layout"; }, FlushMode.Preview);
        Target("mesh layout drag frame (preview flush)", layout, 0.1, "the part unit re-applies every material value of the part (colours, decoration, bundles) with the mesh values");
        double mcol = TimeFlush(live, reps, i => { hair.Front.Mesh.Color = new Color4(0.5f, 0.01f * i, 0.2f, 1); return "chara.hair.front.mesh.color"; }, FlushMode.Preview);
        Target("mesh colour drag frame (preview flush)", mcol, 0.1, "as the layout drag");
        // UH6: mesh pattern picks, warm (cached per row: alternate two rows) and a first load per row
        var first = new List<double>();
        for (int i = 1; i <= 11; i++)
        {
            int row = i;
            hair.Front.Mesh.Item = "illusion.hs2#" + row;
            ed.MarkDirty("chara.hair.front.mesh.item");
            var t = Stopwatch.StartNew(); ed.Flush(); first.Add(t.Elapsed.TotalMilliseconds);
        }
        double warm = TimeFlush(live, reps, i => { hair.Front.Mesh.Item = "illusion.hs2#" + (i % 2 == 0 ? 3 : 7); return "chara.hair.front.mesh.item"; });
        Target("UH6 mesh pattern pick, warm (flush)", warm, 5);
        Target("UH6 mesh pattern pick, first use of each row (open bundle, texture load + flush)", Median(first), 60);
        long bytes = 0; int texs = 0;
        foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>().Where(t => t && t.name.StartsWith("mesh_t_"))) { texs++; bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t); }
        Info($"UH6 first loads per row (ms): {string.Join(", ", first.Select(F))}; mesh textures in memory {texs}, {bytes / (1024.0 * 1024.0):F1} MiB (catalog-owned, not per character)");
        // UH4 draw type switch (four parts + URP mapping)
        double draw = TimeFlush(live, reps, i => { hair.ShaderType = i % 2 == 0 ? 1 : 0; return "chara.hair.shaderType"; });
        Target("UH4 draw type switch (four parts, shader pass + URP mapping)", draw, 20);
        // hair item swap (warm): alternate two vanilla back rows
        SetItem(live, 0, "illusion.hs2#1"); SetItem(live, 0, "illusion.hs2#11");
        double swap = TimeFlush(live, Math.Min(reps, 10), i => { var p = hair.Back; p.Item = i % 2 == 0 ? "illusion.hs2#1" : "illusion.hs2#11"; p.Bundles.Clear(); return "chara.hair.back.item"; });
        Target("hair item swap, warm (flush incl. the full chain rebuild)", swap, 60);
    }

    /// <summary>UH2: a bundle drag's flush times — today's form (a full rebuild every preview frame), the light re-derivation
    /// deferred (4.3), the commit, the light re-derivation of the part's chains alone, a noShake toggle.</summary>
    static void UH2(MakerCoreTest.Built live, int reps, string what)
    {
        var ctx = live.Context;
        var ed = ctx.Editor;
        var part = ctx.Chara.Hair.Back;
        if (part.Bundles.Count == 0) Materialize(ctx, 0);
        int n = part.Bundles.Count;
        // every step writes a value no earlier step wrote (a counter), so each flush sees a rate change
        int counter = 0;
        string Step(int i) { var e = part.Bundles[i % n]; e.Move = new Vec3(e.Move.X, 0.05f + 0.0037f * (++counter % 240), e.Move.Z); return "chara.hair.back.bundles[" + (i % n) + "].move"; }
        bool chainRoot = HairReport.PartOf(ctx, 0).Bundles.Any(b => b.Chains.Any(c => c.Relation == HairReport.RelationRoot || c.Relation == HairReport.RelationInside));
        var reports = new List<FlushReport>();
        double preview = TimeFlush(live, reps, Step, FlushMode.Preview, null, reports);
        bool deferred = reports.All(r => r.DynBone == "none" && r.Deferred.Contains(UnitMap.DynBone));
        double commit = TimeFlush(live, reps, Step, FlushMode.Normal);
        double today = TimeFlush(live, reps, i => { string p = Step(i); ctx.Edit.DynBoneFull = true; return p; }, FlushMode.Preview);
        // the light re-derivation of the part's chains alone (the live option)
        var stage = ctx.Motion.Get<DynamicBoneStage>();
        var cmp = Cmp(ctx, 0);
        var mine = stage.Chains.Where(c => c.Category == DynamicBoneCategory.Hair && c.Kind == 0).ToList();
        var only = new HashSet<Transform>(mine.SelectMany(c => Enumerable.Range(0, c.ParticleCount).Select(c.ParticleTransform)).Where(t => t));
        var partMs = new List<double>();
        for (int i = 0; i < reps; i++)
        {
            Step(i);
            var t = Stopwatch.StartNew();
            var was = mine.Select(c => c.RestoreForRebuild()).ToList();
            HairUnit.ApplyBundles(cmp, part, only);
            for (int j = 0; j < mine.Count; j++) mine[j].BuildAfterRestore(was[j]);
            foreach (var c in mine) c.ResetAll();
            partMs.Add(t.Elapsed.TotalMilliseconds);
        }
        ed.MarkDirty("chara.hair.back.bundles"); ed.Flush();
        double noShake = TimeFlush(live, Math.Min(reps, 10), i => { part.Bundles[0].NoShake = i % 2 == 0; return "chara.hair.back.bundles[0].noShake"; });
        part.Bundles[0].NoShake = false; ed.MarkDirty("chara.hair.back.bundles[0].noShake"); ed.Flush();
        Info($"UH2 {what}: {n} bundles, {mine.Count} chains of the part ({only.Count} transforms), {stage.Chains.Count} chains of the character");
        Check($"UH2 {what}: the preview flushes deferred the light re-derivation", deferred);
        Target($"UH2 {what}: bundle preview flush (light deferred, 4.3{(chainRoot && HairUnit.LiveChainRootDrags ? "; chain-root chains re-derived live" : "")})", preview, 0.3);
        Info($"UH2 {what}: today's form (a full rebuild in every preview frame): median {F(today)} ms");
        Target($"UH2 {what}: commit after a bundle drag (light re-derivation)", commit, 6);
        Target($"UH2 {what}: light re-derivation of the part's chains alone (the live option)", Median(partMs), 0.3, "decides the live option (J7): above 0.3 ms it stays at the commit");
        Target($"UH2 {what}: noShake toggle (full rebuild)", noShake, 20);
    }

    // ================================================================ RunFuzzVanillaMesh (headless)

    /// <summary>MakerEditTest.RunFuzz with HairUnit.MeshOnEveryHair off (vanilla's prefab useMesh; the named switch must keep
    /// edit = fresh build too). The report goes to maker_fuzz_report_vanillamesh.txt; a maker_fuzz_report.txt written
    /// before is kept (copied aside first and put back). RunFuzz ends Unity on a failure: run it last in a suite.</summary>
    public static void RunFuzzVanillaMesh()
    {
        string main = Path.Combine(OutDir, "maker_fuzz_report.txt"), aside = Path.Combine(OutDir, "maker_fuzz_report_kept.txt");
        bool had = File.Exists(main);
        if (had) File.Copy(main, aside, true);
        bool keep = HairUnit.MeshOnEveryHair;
        HairUnit.MeshOnEveryHair = false;
        try { MakerEditTest.RunFuzz(); }
        finally { HairUnit.MeshOnEveryHair = keep; }
        File.Copy(main, Path.Combine(OutDir, "maker_fuzz_report_vanillamesh.txt"), true);
        if (had) { File.Copy(aside, main, true); File.Delete(aside); }
    }

    // ================================================================ RunS1Explain (headless, for T)

    sealed class Classes
    {
        public readonly SortedDictionary<string, int> Lines = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, HashSet<string>> Cards = new SortedDictionary<string, HashSet<string>>(StringComparer.Ordinal);
        /// <summary>Lines per class and card.</summary>
        public readonly SortedDictionary<string, SortedDictionary<string, int>> PerCard = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
        public void Add(string cls, string card)
        {
            Lines[cls] = Lines.TryGetValue(cls, out var n) ? n + 1 : 1;
            if (!Cards.TryGetValue(cls, out var s)) Cards[cls] = s = new HashSet<string>(StringComparer.Ordinal);
            s.Add(card);
            if (!PerCard.TryGetValue(cls, out var pc)) PerCard[cls] = pc = new SortedDictionary<string, int>(StringComparer.Ordinal);
            pc[card] = pc.TryGetValue(card, out var m) ? m + 1 : 1;
        }
        public int Count(string cls) => Lines.TryGetValue(cls, out var n) ? n : 0;
    }

    public static void RunS1Explain()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        List<string> cards;
        string what;
        string only = Arg("-only");
        if (!string.IsNullOrEmpty(only))
        {
            var names = new HashSet<string>(only.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
            cards = MakerCoreTest.AllCards().Where(f => names.Contains(Path.GetFileName(f))).ToList();
            what = "the cards of -only (" + names.Count + " named)";
        }
        else if (Flag("-all")) { cards = MakerCoreTest.AllCards(); what = "every card"; }
        else if (Flag("-targets")) { cards = TestTargets.Load().VisualCards.Where(File.Exists).ToList(); what = "the visual targets"; }
        else { cards = MakerCoreTest.SampleCards(); what = "the visual targets + 20 sampled cards"; }
        int limit = int.Parse(Arg("-limit", int.MaxValue.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        if (cards.Count > limit) cards = cards.Take(limit).ToList();
        bool meshAll = !Flag("-vanillaMesh");
        bool fullOther = Flag("-other");
        bool keep = HairUnit.MeshOnEveryHair;
        HairUnit.MeshOnEveryHair = meshAll;
        rep.AppendLine($"maker hair S1 explain (Unity {Application.unityVersion}): {cards.Count} files, {what}; HairUnit.MeshOnEveryHair {meshAll}; baselines {MakerCoreTest.BaselineDir}");
        var cls = new Classes();
        int equal = 0, differ = 0, missing = 0, failed = 0, notChara = 0, n = 0;
        var others = new List<string>();
        try
        {
            using (var h = MakerCoreTest.Harness.Open())
            {
                foreach (var file in cards)
                {
                    n++;
                    if (n % 25 == 0) Debug.Log($"MakerHairTest S1 explain: {n}/{cards.Count}, {sw.Elapsed.TotalSeconds:F0} s");
                    MakerCoreTest.Built b = null;
                    try
                    {
                        b = h.Build(file);
                        if (b.NotCharacter) { notChara++; continue; }
                        if (!b.Ok) { failed++; Check(Path.GetFileName(file) + ": built", false, b.Error); continue; }
                        var basis = MakerCoreTest.LoadBaseline(b.CardSha);
                        if (basis == null) { missing++; Info(Path.GetFileName(file) + ": no baseline"); continue; }
                        var s = b.Snapshot();
                        if (s.Sha256 == basis.Sha256) { equal++; continue; }
                        differ++;
                        int before = others.Count, beforeJ6 = cls.Count("bundles-J6");
                        Classify(b, basis, s, cls, others, fullOther);
                        if (fullOther && (others.Count > before || cls.Count("bundles-J6") > beforeJ6))
                            for (int k = 0; k < 4; k++)
                            {
                                var pr = HairReport.PartOf(b.Context, k);
                                if (pr == null || (pr.Item == null && !pr.Realized)) continue;
                                Info($"card facts: {Path.GetFileName(file)} part {k} item {pr.Item ?? "-"} row {pr.Row ?? "-"} prefab {pr.Prefab ?? "-"} cmpHair {pr.HasCmpHair} " +
                                     $"boneInfo {pr.Bundles.Count} stored {pr.StoredCount} storedValid {pr.StoredValid}");
                            }
                    }
                    catch (Exception e) { failed++; Check(Path.GetFileName(file) + ": no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace)); }
                    finally { b?.Dispose(); }
                    if (n % 10 == 0) h.ReleaseBundles();
                }
            }
        }
        finally { HairUnit.MeshOnEveryHair = keep; }
        Info($"{equal} equal, {differ} differ, {missing} without a baseline, {failed} failed, {notChara} not character cards");
        foreach (var kv in cls.Lines) Info($"class {kv.Key}: {kv.Value} lines on {cls.Cards[kv.Key].Count} cards");
        foreach (var kc in new[] { "bundles-J6", "other" })
            if (cls.PerCard.TryGetValue(kc, out var pc))
                foreach (var oc in pc) Info($"{kc} card: {oc.Key} ({oc.Value} lines)");
        foreach (var o in fullOther ? others : others.Take(40)) Info("other: " + o);
        Check("S1 explain: every differing line is classified (mesh, deco, rendHair-empty, bundles-J6); 0 other", failed == 0 && !cls.Lines.ContainsKey("other"),
              $"other {(cls.Lines.TryGetValue("other", out var on) ? on : 0)} lines");
        Finish("maker_hair_s1explain_report.txt" .Replace(".txt", meshAll ? ".txt" : "_vanillamesh.txt"), "MakerHairTest.RunS1Explain", sw);
    }

    /// <summary>The snapshot path of a transform (CharaSnapshot's: root name, "/" + child name, "[k]" for the k-th same-named
    /// sibling).</summary>
    static Dictionary<Transform, string> SnapPaths(Transform root)
    {
        var d = new Dictionary<Transform, string>();
        void Walk(Transform t, string p)
        {
            d[t] = p;
            var count = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                int k = count.TryGetValue(c.name, out var v) ? v + 1 : 0;
                count[c.name] = k;
                Walk(c, p + "/" + c.name + (k == 0 ? "" : "[" + k + "]"));
            }
        }
        Walk(root, root.name);
        return d;
    }

    static readonly string[] MeshTokens = { "_MeshColorMask_B", "_MeshColor_01" };
    static readonly string[] DecoTokens = { "_Color", "_Color2", "_Color3" };

    static void Classify(MakerCoreTest.Built b, CharaSnapshot basis, CharaSnapshot now, Classes cls, List<string> others, bool full = false)
    {
        int cut = full ? int.MaxValue : 160;
        var ctx = b.Context;
        string card = Path.GetFileName(b.File);
        var paths = SnapPaths(b.Root.transform);
        // renderer paths per class: rendHair of parts that draw the mesh, rendAccessory, parts with an empty rendHair
        var meshRend = new HashSet<string>(StringComparer.Ordinal); var decoRend = new HashSet<string>(StringComparer.Ordinal); var emptyPart = new List<string>();
        for (int k = 0; k < 4; k++)
        {
            var go = ctx.Hair[k];
            var cmp = go ? go.GetComponent<CmpHair>() : null;
            if (cmp == null) continue;
            var rends = HairUnit.HairRenderers(go, cmp);
            if (HairUnit.MeshDrawn(cmp, rends.Count)) foreach (var r in rends) if (paths.TryGetValue(r.transform, out var p)) meshRend.Add(p);
            if (cmp.rendAccessory != null) foreach (var r in cmp.rendAccessory.Where(r => r)) if (paths.TryGetValue(r.transform, out var p)) decoRend.Add(p);
            if (rends.Count == 0 && paths.TryGetValue(go.transform, out var gp)) emptyPart.Add(gp + "/");
        }
        // J6: correction transforms of parts with a stored list that does not apply (count ≠ boneInfo count)
        var j6 = new HashSet<Transform>();
        for (int k = 0; k < 4; k++)
        {
            var go = ctx.Hair[k];
            var cmp = go ? go.GetComponent<CmpHair>() : null;
            var doc = ctx.Chara?.Hair?.Parts[k];
            if (cmp?.boneInfo == null || doc?.Bundles == null || doc.Bundles.Count == 0 || HairUnit.StoredValid(doc, cmp.boneInfo.Count)) continue;
            foreach (var bi in cmp.boneInfo) if (bi?.trfCorrect) j6.Add(bi.trfCorrect);
        }
        var byPath = new Dictionary<string, Transform>(StringComparer.Ordinal);
        foreach (var kv2 in paths) byPath[kv2.Value] = kv2.Key;
        var stage = ctx.Motion != null ? ctx.Motion.Get<DynamicBoneStage>() : null;
        bool J6Chain(string key)
        {
            if (j6.Count == 0 || stage == null || !key.StartsWith("motion|chain[", StringComparison.Ordinal)) return false;
            int e = key.IndexOf(']', 13);
            if (e < 0 || !int.TryParse(key.Substring(13, e - 13), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ci) || ci < 0 || ci >= stage.Chains.Count) return false;
            var root = stage.Chains[ci].Root;
            return root && j6.Any(t => root.IsChildOf(t));
        }
        var a = Keyed(basis.Lines); var c = Keyed(now.Lines);
        foreach (var kv in a.Concat(c.Where(x => !a.ContainsKey(x.Key))))
        {
            a.TryGetValue(kv.Key, out var la); c.TryGetValue(kv.Key, out var lc);
            if (la == lc) continue;
            string key = kv.Key, klass = "other";
            if (key.StartsWith("mat|", StringComparison.Ordinal) && la != null && lc != null)
            {
                string rend = key.Substring(4); int br = rend.LastIndexOf('['); if (br > 0) rend = rend.Substring(0, br);
                var diffTokens = TokenNames(la, lc);
                if (emptyPart.Any(p => (rend + "/").StartsWith(p, StringComparison.Ordinal))) klass = "rendHair-empty";
                else if (meshRend.Contains(rend) && diffTokens.All(t => MeshTokens.Contains(t))) klass = "mesh";
                else if (decoRend.Contains(rend) && diffTokens.All(t => DecoTokens.Contains(t))) klass = "deco";
                if (klass == "other") others.Add($"{card} {Short(key, cut)}: tokens [{string.Join(",", diffTokens)}]");
            }
            else if (key.StartsWith("ctx|missing", StringComparison.Ordinal) && ((la ?? "") + (lc ?? "")).Contains(".mesh.item")) klass = "mesh";
            else if (la != null && lc != null && key.StartsWith("bone|", StringComparison.Ordinal) && byPath.TryGetValue(key.Substring(5), out var bt) && j6.Contains(bt)) klass = "bundles-J6";
            else if (la != null && lc != null && J6Chain(key)) klass = "bundles-J6";
            else others.Add($"{card} {Short(key, cut)}: {Short(la, cut)} <> {Short(lc, cut)}");
            cls.Add(klass, card);
        }
    }

    static Dictionary<string, string> Keyed(List<string> lines)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        var n = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var x in lines)
        {
            int p1 = x.IndexOf('|'); int p2 = p1 < 0 ? -1 : x.IndexOf('|', p1 + 1);
            string k = p2 < 0 ? x : x.Substring(0, p2);
            int c = n.TryGetValue(k, out var v) ? v + 1 : 0;
            n[k] = c;
            d[c == 0 ? k : k + "#" + c] = x;
        }
        return d;
    }

    /// <summary>The property names whose "name=value" tokens differ between two material lines (other differing tokens are
    /// returned as themselves).</summary>
    static List<string> TokenNames(string a, string b)
    {
        Dictionary<string, string> T(string s)
        {
            // tokens are separated by spaces outside parentheses (a texture value is "name(WxH FORMAT mips=N)@scale+offset")
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            var parts = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i <= s.Length; i++)
            {
                char ch = i < s.Length ? s[i] : ' ';
                if (ch == '(') depth++;
                else if (ch == ')') depth = Math.Max(0, depth - 1);
                else if (ch == ' ' && depth == 0) { if (i > start) parts.Add(s.Substring(start, i - start)); start = i + 1; }
            }
            foreach (var t in parts)
            {
                int e = t.IndexOf('=');
                string k = e > 0 ? t.Substring(0, e) : t;
                d[k] = t;
            }
            return d;
        }
        var ta = T(a); var tb = T(b);
        var l = new List<string>();
        foreach (var k in ta.Keys.Union(tb.Keys))
        {
            ta.TryGetValue(k, out var x); tb.TryGetValue(k, out var y);
            if (x != y) l.Add(k);
        }
        return l;
    }
}
