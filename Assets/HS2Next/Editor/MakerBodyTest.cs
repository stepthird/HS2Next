// Maker body tests of Phase 4.2 package A (.render, .timing).
//   Run (headless):
//     BT1 per body family (skin item / colour, sunburn item / colour, paint k item / colour / gloss / metallic / layout /
//         rotation / region, a whole body.paint set, a KSOX re-registration): the creators one flush rebuilds = the
//         HS2Next column (+ the face colour creator for the skin colour); afterwards the character = a fresh build
//     BT2 body shapes: each of the 33 indices and softness / weight / areola set to 0.25, 0.75, −0.5, 1.5 (a fresh-build
//         comparison after 1.5; every value compared for the indices 0, 1, 9, 23, 32) and back (= the initial snapshot), on
//         BR-Chan and HERO107 (U rule; the bust stage values are in the snapshot)
//     BT3 breast normals: size ∈ {−0.5, 0, 0.25, 0.4999, 0.5, 0.58, 1, 1.5} → the drawn normals = Vector3.Lerp(Min, Max, r)
//         bit for bit, = a fresh build's; size ≥ 0.5 = the shipped mesh normals; the copy keeps name and vertex count and is
//         owned; male: no copy, no change; a card's uncensor body data is kept as data only (no BodyMesh build hook in the
//         official build: the prefab body and its normals); ReleaseContent destroys the copy (scene node)
//     BT4 bust physics: BR-Chan's values give the worked example (1e-6); softness / weight at −0.5 and 1.5 follow Y10
//         (the gravity lerp clamped; the softness product clamped before the three-point lerp)
//     BT5 swatches: MakerEdits.HsvToRgb = Color.HSVToRGB bit for bit (the 7 rows of 336 and 1,000 random h, s, v); a skin
//         colour set rebuilds only the colour creators (body + face); with B's op (StudioOps.ApplyColourPreset) a push through
//         the realization: one entry, colour creators only
//     BT6 paint regions: regions 0, 2 and the mod region 20 → _Texture5UV / _Texture5Rotator = the layout's values (worked example); a
//         missing region → the creators' initial values; valid → missing → valid = a fresh build at each step
//     BT7 male: the hidden rows' stored values drive the bones (= a fresh build); the height with MaleHeightPin075 on / off
//     BT8 throttle weight (CPU, no frames): W per family with 0, 1, 2 layers on the creators; the rule per mode
//     BT9 digest: the body digest's normals / bust sections change exactly when the rules say; no cf_J_look_* anywhere
//   RunRender (graphics): RT read-back hashes after BT1's selective rebuilds = after rebuilding both body creators (BR-Chan,
//     HERO107); the gloss state on the body after a draw.body edit; a female at size 0.3 skins with the blended normals.
//   RunTiming (graphics, its own Unity session): targets and UB2–UB4, UB7.
// Run: -executeMethod MakerBodyTest.Run [-only BT1,BT3]; MakerBodyTest.RunRender; MakerBodyTest.RunTiming [-reps N];
//      MakerBodyTest.RunSuite -suite "MakerBodyTest.Run,MakerUnitTest.RunU,..." (with -NoQuit; exits itself).
// Reports: Spike/out/maker/maker_body_report.txt, maker_body_render_report.txt, maker_body_timing_report.txt; first line
// "SUMMARY: N PASS, M FAIL"; EditorApplication.Exit(1) on any FAIL. Content rule: numbers only (hashes, normals statistics,
// bone and material values); nothing is rendered to a file or viewed; SFW visual targets.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Chara;
using HS2Next.Chara.Edit;
using HS2Next.Chara.Edit.Units;
using HS2Next.Content;
using HS2Next.Content.Maker;
using HS2Next.Motion.DynamicBones;
using HS2Next.Motion.Face;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Tests.MakerCore;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class MakerBodyTest
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
        Debug.Log($"MAKER BODY TEST DONE ({file}): {passes} PASS, {fails} FAIL, {warns} WARN");
        if (fails > 0 && !inSuite) TestExit.Exit(1);
        suiteFails += fails;
    }

    static bool inSuite;
    static int suiteFails;

    /// <summary>Runs several static parameterless test methods in one Unity session (-suite "A.B,C.D"); exits Unity with 1
    /// when any MakerBodyTest method failed or any method threw (methods of other classes end Unity on their own failure).</summary>
    public static void RunSuite()
    {
        inSuite = true; suiteFails = 0;
        foreach (var name in (Arg("-suite") ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
        {
            int dot = name.LastIndexOf('.');
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name.Substring(0, dot))).FirstOrDefault(t => t != null);
            var m = type?.GetMethod(name.Substring(dot + 1), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, Type.EmptyTypes, null);
            Debug.Log("MakerBodyTest.RunSuite: " + name + (m == null ? " (not found)" : ""));
            if (m == null) { suiteFails++; continue; }
            try { m.Invoke(null, null); }
            catch (Exception e) { suiteFails++; Debug.LogError("MakerBodyTest.RunSuite: " + name + " threw " + (e.InnerException ?? e)); }
        }
        inSuite = false;
        Debug.Log("MakerBodyTest.RunSuite: done, failures " + suiteFails);
        TestExit.Exit(suiteFails > 0 ? 1 : 0);
    }

    /// <summary>Diagnostic (Y10): S1 with the gravity lerp of before 4.2 (BustStage.ExtrapolateGravity) — the baseline
    /// differences of the cards whose bust weight lies outside 0..1 vanish, so nothing else of 4.2 changed the snapshot.</summary>
    public static void RunS1OldGravity()
    {
        BustStage.ExtrapolateGravity = true;
        try { MakerCoreTest.RunS1(); }
        finally { BustStage.ExtrapolateGravity = false; }
    }

    static string Arg(string name, string def = null)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }
    static bool Want(string id)
    {
        var o = Arg("-only");
        return o == null || o.Split(',', '+').Select(x => x.Trim()).Contains(id);
    }
    static double Median(List<double> l)
    {
        if (l.Count == 0) return double.NaN;
        var s = l.OrderBy(x => x).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }
    static string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
    static string G(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string Rel(string f) => Path.GetFileName(f);
    static string Short(string s, int n = 400) => s == null ? "" : s.Length > n ? s.Substring(0, n) + "…" : s;
    static int Bits(float f) => BitConverter.SingleToInt32Bits(f);
    static readonly ExecuteOptions User = new ExecuteOptions { Source = CommandSource.User };

    const string FaceC = "create_skin_face", FaceD = "create_skin detail_face", BodyC = "create_skin_body", BodyD = "create_skin detail_body";
    static readonly string[] AllCreators = { FaceC, FaceD, BodyC, BodyD };

    static string FemaleCard => TestTargets.Abs(TestTargets.Load().visual.female);
    static string MaleCard => TestTargets.Abs(TestTargets.Load().visual.male);

    /// <summary>Female uncensor body GUIDs of this install's cards (the mosaic census): kept as data only in the official build.</summary>
    const string UncBlend = "bp.sac_innie_v2", UncSkip = "Anacs01", UncNone = "navy";

    sealed class Disposer : IDisposable
    {
        public readonly MakerCoreTest.Built B;
        public Disposer(MakerCoreTest.Built b) { B = b; }
        public void Dispose() => B?.Dispose();
    }

    // ================================================================ helpers

    /// <summary>A container of the live character's current documents (and its resources).</summary>
    static CharaContainer ContainerOf(MakerCoreTest.Built live)
    {
        var container = new CharaContainer { Subtree = live.Result.Stored.Clone() };
        container.Res.CopyFrom(live.Container.Res, container.Subtree.ReferencedResources());
        return container;
    }

    /// <summary>The live character's snapshot vs a fresh build of its documents (null diff = the fresh build failed; error set).</summary>
    static SnapshotDiff VsFresh(MakerCoreTest.Harness h, MakerCoreTest.Built live, out string error, Action<MakerCoreTest.Built, MakerCoreTest.Built> extra = null)
    {
        error = null;
        var sA = live.Snapshot();
        using (var fresh = new Disposer(h.Build(ContainerOf(live), "fresh")))
        {
            if (!fresh.B.Ok) { error = fresh.B.Error; return null; }
            // both characters stepped once by 0 (the snapshot's Step(0)) before the extra checks: a fresh build's hair chains
            // are inactive until its first step, the live character's took their switch state in its snapshot
            var sB = fresh.B.Snapshot();
            extra?.Invoke(live, fresh.B);
            return CharaSnapshot.Compare(sB, sA, 12);
        }
    }

    static FlushReport Set(MakerCoreTest.Built live, string path, Action set)
    {
        set();
        live.Context.Editor.MarkDirty(path);
        return live.Context.Editor.Flush();
    }

    static string NextRow(ContentCatalog cat, int category, string current, Func<ListEntry, bool> ok = null)
    {
        var rows = cat.EntriesOf(category);
        ContentId.TryParse(current, out var cur);
        foreach (var r in rows) if (r.IsVanilla && r.Key != cur && (ok == null || ok(r))) return r.Key.ToString();
        foreach (var r in rows) if (r.Key != cur && (ok == null || ok(r))) return r.Key.ToString();
        return null;
    }
    static bool HasTex(ListEntry r, string col) => !string.IsNullOrEmpty(r[col]) && r[col] != "0";
    static float Fl(object v) => (float)v;
    static object AltF(object v) { float f = (float)v; return f < 0.6f ? f + 0.37f : f - 0.41f; }
    static object AltC(object v) { var c = (Color4)v; return new Color4(c.R * 0.5f + 0.13f, c.G * 0.25f + 0.31f, 1 - c.B * 0.5f, c.A); }
    static object AltV4(object v) { var c = (Vec4)v; return new Vec4(c.X * 0.5f + 0.1f, c.Y * 0.75f + 0.2f, 1 - c.Z, c.W * 0.5f + 0.3f); }

    sealed class Family
    {
        public string Name, Path;
        public Func<object> Get;
        public Action<object> Set;
        public Func<object, object> Alt;
        public string[] Expect;
    }

    /// <summary>The body families: the HS2Next column with the throttle families.</summary>
    static List<Family> BodyFamilies(CharaDocument ch, ContentCatalog cat, bool male)
    {
        var l = new List<Family>();
        var sex = male ? CharaSex.Male : CharaSex.Female;
        void Add(string name, string path, Func<object> get, Action<object> set, Func<object, object> alt, params string[] expect) =>
            l.Add(new Family { Name = name, Path = path, Get = get, Set = set, Alt = alt, Expect = expect });
        Func<object, object> Ref(int c, Func<ListEntry, bool> ok = null) => v => NextRow(cat, c, (string)v, ok);
        var b = ch.Body;
        Add("body.skin.item", "chara.body.skin.item", () => b.Skin.Item, v => b.Skin.Item = (string)v, Ref(sex.SkinB, r => HasTex(r, "MainTex")), BodyC);
        Add("body.skin.color", "chara.body.skin.color", () => b.Skin.Color, v => b.Skin.Color = (Color4)v, AltC, BodyC, FaceC);
        Add("body.sunburn.item", "chara.body.sunburn.item", () => b.Sunburn.Item, v => b.Sunburn.Item = (string)v, Ref(sex.Sunburn), BodyC);
        Add("body.sunburn.color", "chara.body.sunburn.color", () => b.Sunburn.Color, v => b.Sunburn.Color = (Color4)v, AltC, BodyC);
        for (int k = 0; k < 2; k++)
        {
            var pt = b.Paint[k]; string p = "chara.body.paint[" + k + "]", n = "body.paint[" + k + "]";
            Add(n + ".item", p + ".item", () => pt.Item, v => pt.Item = (string)v, Ref(313), BodyC, BodyD);
            Add(n + ".color", p + ".color", () => pt.Color, v => pt.Color = (Color4)v, AltC, BodyC);
            Add(n + ".gloss", p + ".gloss", () => pt.Gloss, v => pt.Gloss = Fl(v), AltF, BodyD);
            Add(n + ".metallic", p + ".metallic", () => pt.Metallic, v => pt.Metallic = Fl(v), AltF, BodyD);
            Add(n + ".layout", p + ".layout", () => pt.Layout, v => pt.Layout = (Vec4)v, AltV4, BodyC, BodyD);
            Add(n + ".rotation", p + ".rotation", () => pt.Rotation, v => pt.Rotation = Fl(v), AltF, BodyC, BodyD);
            Add(n + ".layoutItem", p + ".layoutItem", () => pt.LayoutItem, v => pt.LayoutItem = (string)v, Ref(8), BodyC, BodyD);
        }
        // a whole body.paint set (the changed member is paint 1's colour): both creators
        Add("body.paint (whole)", "chara.body.paint", () => b.Paint[1].Color, v => b.Paint[1].Color = (Color4)v, AltC, BodyC, BodyD);
        // material units: no creator
        Add("body.detail.power (material)", "chara.body.detail.power", () => b.Detail.Power, v => b.Detail.Power = Fl(v), AltF);
        Add("body.nipple.color (material)", "chara.body.nipple.color", () => b.Nipple.Color, v => b.Nipple.Color = (Color4)v, AltC);
        Add("body.shape[3] (bones)", "chara.body.shape[3]", () => b.Shape[3], v => b.Shape[3] = Fl(v), AltF);
        return l;
    }

    static Dictionary<string, int> RebuildCounts(EditContext x) =>
        x.Composites().Where(c => AllCreators.Contains(c.Name)).ToDictionary(c => c.Name, c => c.Rebuilds);

    static void SetBack(Family fam, object old, CharaEditor ed)
    {
        fam.Set(old);
        ed.MarkDirty(fam.Path);
        ed.Flush();
    }

    static Mesh DrawnMesh(CharaContext ctx) => BustNormalsUnit.MeshOf(ctx?.BodyRenderer);

    static Vector3[] NormalsOf(Mesh m)
    {
        if (!m || !m.isReadable) return null;
        var l = new List<Vector3>(m.vertexCount);
        m.GetNormals(l);
        return l.ToArray();
    }

    static bool SameBits(Vector3[] a, Vector3[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (Bits(a[i].x) != Bits(b[i].x) || Bits(a[i].y) != Bits(b[i].y) || Bits(a[i].z) != Bits(b[i].z)) return false;
        return true;
    }

    static int DiffCount(Vector3[] a, Vector3[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return -1;
        int n = 0;
        for (int i = 0; i < a.Length; i++) if (Bits(a[i].x) != Bits(b[i].x) || Bits(a[i].y) != Bits(b[i].y) || Bits(a[i].z) != Bits(b[i].z)) n++;
        return n;
    }

    static Vector3[] Blend(Vector3[] min, Vector3[] max, float r)
    {
        var o = new Vector3[max.Length];
        for (int i = 0; i < o.Length; i++) o[i] = Vector3.Lerp(min[i], max[i], r);
        return o;
    }

    /// <summary>The vanilla NormalData asset and the shipped o_body_cf normals (read independently of the unit).</summary>
    static bool VanillaNormals(ContentCatalog cat, out Vector3[] min, out Vector3[] max, out Vector3[] shipped)
    {
        shipped = null;
        if (!BustNormalsUnit.ReadEntry(cat, null, "chara/oo_base.unity3d", "p_cf_body_00_Nml", "o_body_cf", out min, out max)) return false;
        var prefab = cat.LoadAsset<GameObject>("chara/oo_base.unity3d", "p_cf_body_00");
        var smr = prefab ? EditContext.Find(prefab.transform, "o_body_cf")?.GetComponent<SkinnedMeshRenderer>() : null;
        shipped = NormalsOf(smr ? smr.sharedMesh : null);
        return shipped != null;
    }

    static CharaContainer WithUncensor(CharaContainer src, string guid)
    {
        var c = new CharaContainer { Subtree = src.Subtree.Clone() };
        c.Res.CopyFrom(src.Res, c.Subtree.ReferencedResources());
        c.Subtree.Chara.Body.Uncensor = guid == null ? null : new UncensorData { Body = guid };
        return c;
    }

    // ================================================================ Run (headless)

    public static void Run()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"maker body (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        bool pin = CharaAssembler.MaleHeightPin075, extra = BustStage.ExtrapolateGravity;
        var mode = CharaEditFlush.BodyThrottle;
        try
        {
            using (var h = MakerCoreTest.Harness.Open())
            {
                if (Want("BT1")) Guard("BT1", () => BT1(h));
                if (Want("BT2")) Guard("BT2", () => BT2(h));
                if (Want("BT3")) Guard("BT3", () => BT3(h));
                if (Want("BT4")) Guard("BT4", () => BT4(h));
                if (Want("BT5")) Guard("BT5", () => BT5(h));
                if (Want("BT6")) Guard("BT6", () => BT6(h));
                if (Want("BT7")) Guard("BT7", () => BT7(h));
                if (Want("BT8")) Guard("BT8", () => BT8(h));
                if (Want("BT9")) Guard("BT9", () => BT9(h));
            }
        }
        finally
        {
            FaceSetup.Seed = fseed; CharaEditFlush.ForcePreview = false;
            CharaAssembler.MaleHeightPin075 = pin; BustStage.ExtrapolateGravity = extra; CharaEditFlush.BodyThrottle = mode;
        }
        Finish("maker_body_report.txt", "MakerBodyTest BT1-BT9", sw);
    }

    static void Guard(string id, Action a)
    {
        rep.AppendLine();
        rep.AppendLine("== " + id);
        try { a(); }
        catch (Exception e) { Check(id + ": no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace, 1200)); }
    }

    // ---------------------------------------------------------------- BT1

    static void BT1(MakerCoreTest.Harness h)
    {
        int fams = 0, ok = 0;
        foreach (var file in TestTargets.Load().VisualCards.Where(File.Exists))
        {
            MakerCoreTest.Built live = null;
            try
            {
                live = h.Build(file);
                if (!Check(Rel(file) + ": built", live.Ok, live.Error)) continue;
                var ctx = live.Context; var ed = ctx.Editor; var x = ctx.Edit;
                foreach (var fam in BodyFamilies(ctx.Chara, h.Catalog, ctx.IsMale))
                {
                    string label = Rel(file) + " " + fam.Name;
                    object old = fam.Get(), alt = fam.Alt(old);
                    if (alt == null || Equals(alt, old)) { Info(label + ": skipped (no other value)"); continue; }
                    fams++;
                    fam.Set(alt);
                    var before = RebuildCounts(x);
                    ed.MarkDirty(fam.Path);
                    var r = ed.Flush();
                    var after = RebuildCounts(x);
                    var counted = AllCreators.Where(n => after.TryGetValue(n, out int a) && before.TryGetValue(n, out int b) && a > b).OrderBy(n => n, StringComparer.Ordinal).ToList();
                    var got = r.CreatorsRebuilt.Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
                    var exp = fam.Expect.OrderBy(n => n, StringComparer.Ordinal).ToList();
                    bool pass = got.SequenceEqual(exp) && counted.SequenceEqual(exp) && r.CreatorsRebuilt.Count == exp.Count && r.Errors.Count == 0;
                    if (Check(label + ": creators rebuilt = the HS2Next column", pass, $"rebuilt [{string.Join(", ", r.CreatorsRebuilt)}], expected [{string.Join(", ", exp)}]; units [{string.Join(",", r.Units)}]" + (r.Errors.Count > 0 ? "; errors " + string.Join("; ", r.Errors) : ""))) ok++;
                    SetBack(fam, old, ed);
                }
                // KSOX re-registration (body.skinOverlays marked): the skin units rebuild both body and both face creators
                {
                    fams++;
                    var r = Set(live, "chara.body.skinOverlays", () => { });
                    var got = r.CreatorsRebuilt.Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
                    var exp = AllCreators.OrderBy(n => n, StringComparer.Ordinal).ToList();
                    if (Check(Rel(file) + " body.skinOverlays (KSOX layers): both body and both face creators", got.SequenceEqual(exp) && r.Units.Contains(UnitMap.ExtTextures) && r.Errors.Count == 0,
                              $"rebuilt [{string.Join(", ", r.CreatorsRebuilt)}]; units [{string.Join(",", r.Units)}]")) ok++;
                }
                var d = VsFresh(h, live, out string err);
                Check(Rel(file) + ": after the families = a fresh build", d != null && d.Equal, d?.ToString() ?? err);
            }
            finally { live?.Dispose(); h.ReleaseBundles(); }
        }
        Check("BT1: every body family rebuilds exactly the creators", fams > 0 && ok == fams, $"{ok} of {fams}");
    }

    // ---------------------------------------------------------------- BT2

    static readonly float[] BT2Values = { 0.25f, 0.75f, -0.5f, 1.5f };
    static readonly int[] BT2Every = { 0, 1, 9, 23, 32 };

    static void BT2(MakerCoreTest.Harness h)
    {
        foreach (var file in TestTargets.Load().VisualCards.Where(File.Exists))
        {
            MakerCoreTest.Built live = null;
            try
            {
                live = h.Build(file);
                if (!Check(Rel(file) + ": built", live.Ok, live.Error)) continue;
                var b = live.Context.Chara.Body;
                var s0 = live.Snapshot();
                var members = new List<(string name, string path, Func<float> get, Action<float> set, bool every)>();
                for (int i = 0; i < b.Shape.Length; i++)
                {
                    int k = i;
                    members.Add(("shape[" + k + "]", "chara.body.shape[" + k + "]", () => b.Shape[k], v => b.Shape[k] = v, BT2Every.Contains(k)));
                }
                members.Add(("bust.softness", "chara.body.bust.softness", () => b.Bust.Softness, v => b.Bust.Softness = v, true));
                members.Add(("bust.weight", "chara.body.bust.weight", () => b.Bust.Weight, v => b.Bust.Weight = v, true));
                members.Add(("nipple.areola", "chara.body.nipple.areola", () => b.Nipple.Areola, v => b.Nipple.Areola = v, false));
                int fresh = 0, equal = 0, back = 0;
                var bad = new List<string>();
                foreach (var m in members)
                {
                    float old = m.get();
                    foreach (var v in BT2Values)
                    {
                        var r = Set(live, m.path, () => m.set(v));
                        if (r.Errors.Count > 0) bad.Add($"{m.name}={G(v)}: {string.Join("; ", r.Errors)}");
                        if (m.every || v == BT2Values[BT2Values.Length - 1])
                        {
                            fresh++;
                            var d = VsFresh(h, live, out string err);
                            if (d != null && d.Equal) equal++;
                            else bad.Add($"{m.name}={G(v)}: {d?.ToString() ?? err}");
                        }
                    }
                    Set(live, m.path, () => m.set(old));
                    var db = CharaSnapshot.Compare(s0, live.Snapshot(), 12);
                    if (db.Equal) back++; else bad.Add($"{m.name} back to {G(old)}: {db}");
                }
                Check($"BT2 {Rel(file)}: {members.Count} members × (0.25, 0.75, −0.5, 1.5): each compared state = a fresh build of the documents", fresh > 0 && equal == fresh, $"{equal} of {fresh}" + (bad.Count > 0 ? "; " + Short(string.Join(" | ", bad.Take(4)), 2000) : ""));
                Check($"BT2 {Rel(file)}: every member set back = the initial snapshot", back == members.Count, $"{back} of {members.Count}");
            }
            finally { live?.Dispose(); h.ReleaseBundles(); }
        }
    }

    // ---------------------------------------------------------------- BT3

    static readonly float[] BT3Sizes = { -0.5f, 0f, 0.25f, 0.4999f, 0.5f, 0.58f, 1f, 1.5f };

    static void BT3(MakerCoreTest.Harness h)
    {
        if (!Check("BT3: the vanilla NormalData (p_cf_body_00_Nml / o_body_cf) and the shipped normals read", VanillaNormals(h.Catalog, out var min, out var max, out var shipped))) return;
        Check("BT3 [V]: NormalMax = the shipped mesh normals bit for bit", SameBits(max, shipped), $"{DiffCount(max, shipped)} of {shipped?.Length} differ");
        Info($"o_body_cf: {shipped.Length} vertices; Min != Max on {DiffCount(min, max)}");
        using (var b = new Disposer(h.Build(FemaleCard)))
        {
            var live = b.B;
            if (!Check("BT3 female: built", live.Ok, live.Error)) return;
            var ctx = live.Context;
            var st = ctx.BustNormals;
            var drawn = DrawnMesh(ctx);
            var prefab = h.Catalog.LoadAsset<GameObject>("chara/oo_base.unity3d", "p_cf_body_00");
            var orig = prefab ? EditContext.Find(prefab.transform, "o_body_cf")?.GetComponent<SkinnedMeshRenderer>()?.sharedMesh : null;
            Check("BT3 female: the drawn body mesh is an owned copy (name and vertex count kept)",
                  st != null && !st.Missing && drawn && orig && drawn != orig && ctx.OwnedMeshes.Count == 1 && ctx.OwnedMeshes[0] == drawn && drawn.name == orig.name && drawn.vertexCount == orig.vertexCount,
                  $"state {(st == null ? "none" : st.Source + " " + st.Asset + (st.Missing ? " missing" : ""))}, owned {ctx.OwnedMeshes.Count}, drawn {(drawn ? drawn.name + "/" + drawn.vertexCount : "none")}, original {(orig ? orig.name + "/" + orig.vertexCount : "none")}");
            Check("BT3 female: the local bounds of the body renderer are kept (the rebind's)", ctx.BodyRenderer && ctx.BodyRenderer.localBounds.center == new Vector3(0, -2, 0) && ctx.BodyRenderer.localBounds.size == new Vector3(20, 20, 20), ctx.BodyRenderer ? ctx.BodyRenderer.localBounds.ToString() : "-");
            Check("BT3 female: the mesh copy is readable", drawn && drawn.isReadable);
            float size0 = ctx.Chara.Body.Shape[1];
            Info($"BR-Chan bust size {G(size0)}, rate {G(BustNormalsUnit.RateOf(size0))}, blend {F(st?.LastBlendMs ?? double.NaN)} ms, copy {F(st?.LastCopyMs ?? double.NaN)} ms, {st?.CopyBytes ?? 0} B");
            foreach (var s in BT3Sizes)
            {
                Set(live, "chara.body.shape[1]", () => ctx.Chara.Body.Shape[1] = s);
                float r = BustNormalsUnit.RateOf(s);
                float expR = s >= 0.5f ? 1f : Mathf.Clamp01(s / 0.5f);
                var got = NormalsOf(DrawnMesh(ctx));
                var want = Blend(min, max, r);
                Check($"BT3 size {G(s)}: rate {G(r)} = HS2's ({G(expR)}); normals = Lerp(Min, Max, r) bit for bit", Bits(r) == Bits(expR) && SameBits(got, want),
                      $"{DiffCount(got, want)} of {want.Length} differ; state rate {G(ctx.BustNormals.Rate)}");
                if (s >= 0.5f) Check($"BT3 size {G(s)}: = the shipped normals", SameBits(got, shipped), $"{DiffCount(got, shipped)} differ");
                if (s == 0.25f || s == 0.5f || s == 1.5f)
                {
                    string hl = FaceDigest.NormalsHash(DrawnMesh(ctx)), hf = null;
                    var d = VsFresh(h, live, out string err, (l, f) => hf = FaceDigest.NormalsHash(DrawnMesh(f.Context)));
                    Check($"BT3 size {G(s)}: the edit = a fresh build (snapshot and normals hash)", d != null && d.Equal && hl == hf, (d?.ToString() ?? err) + $"; normals {hl?.Substring(0, 12)} vs {hf?.Substring(0, 12)}");
                }
            }
            int blends = ctx.BustNormals.Blends;
            Set(live, "chara.body.shape[1]", () => ctx.Chara.Body.Shape[1] = 1.7f);   // r = 1 again (unchanged): no write
            Check("BT3: an unchanged rate writes nothing (1.5 → 1.7)", ctx.BustNormals.Blends == blends, $"blends {blends} → {ctx.BustNormals.Blends}");
            Set(live, "chara.body.shape[3]", () => ctx.Chara.Body.Shape[3] = 0.77f);
            Check("BT3: another shape index marks the unit (the rate re-read, nothing written)", live.Context.Editor.LastFlush.Units.Contains(UnitMap.BustNormals) && ctx.BustNormals.Blends == blends);
            Set(live, "chara.body.shape[1]", () => ctx.Chara.Body.Shape[1] = size0);
        }
        h.ReleaseBundles();

        // male: no copy, no change
        using (var b = new Disposer(h.Build(MaleCard)))
        {
            var live = b.B;
            if (Check("BT3 male: built", live.Ok, live.Error))
            {
                var ctx = live.Context;
                var prefab = h.Catalog.LoadAsset<GameObject>("chara/oo_base.unity3d", "p_cm_body_00");
                var orig = prefab ? EditContext.Find(prefab.transform, "o_body_cm")?.GetComponent<SkinnedMeshRenderer>()?.sharedMesh : null;
                Set(live, "chara.body.shape[1]", () => ctx.Chara.Body.Shape[1] = 0.2f);
                Check("BT3 male: no state, no owned mesh, the drawn mesh is the prefab's", ctx.BustNormals == null && ctx.OwnedMeshes.Count == 0 && DrawnMesh(ctx) == orig && orig,
                      $"state {ctx.BustNormals != null}, owned {ctx.OwnedMeshes.Count}, drawn {DrawnMesh(ctx)?.name}");
            }
        }
        h.ReleaseBundles();

        // the card's uncensor body data (BR-Chan's documents with an uncensor body GUID): kept as data only, the prefab body is
        // built (no BodyMesh build hook in the official build; numbers only)
        var imp = MakerEditTest.Import(h, FemaleCard, out string ierr);
        if (!Check("BT3: BR-Chan imported", imp != null, ierr)) return;
        if (HS2Next.Chara.Ext.CharaBuildHooks.Any(HS2Next.Chara.Ext.HookStage.BodyMesh))
            Warn("BT3 body data", "a BodyMesh build hook is registered (not the official build): the check is skipped");
        else
            foreach (var guid in new[] { UncBlend, UncSkip, UncNone })
            {
                var c = WithUncensor(imp, guid);
                c.Subtree.Chara.Body.Shape[1] = 0.3f;
                using (var b = new Disposer(h.Build(c, "body data " + guid)))
                {
                    var live = b.B;
                    if (!Check($"BT3 body data {guid}: built", live.Ok, live.Error)) continue;
                    var ctx = live.Context;
                    var st = ctx.BustNormals;
                    Check($"BT3 body data {guid}: the prefab body, the vanilla normals source, one owned copy, the data kept",
                          ctx.ReplacedBodyMesh == null && ctx.BodyNormals == null && st != null && st.Source == "vanilla" && ctx.OwnedMeshes.Count == 1
                          && ctx.Chara?.Body?.Uncensor?.Body == guid,
                          $"replaced {ctx.ReplacedBodyMesh ?? "-"}, source {st?.Source}, owned {ctx.OwnedMeshes.Count}");
                }
                h.ReleaseBundles();
            }

        // through the scene: the copy is owned by the node and destroyed with its content; the missing finding
        using (var n = NodeOf(h, FemaleCard))
        {
            var real = n.Real;
            var ctx = real.Context;
            var copy = ctx.OwnedMeshes.FirstOrDefault();
            bool had = (object)copy != null && copy;
            n.Dispose();
            Check("BT3: ReleaseContent destroys the owned mesh copy", had && !copy && ctx.OwnedMeshes.Count == 0, had ? (copy ? "alive" : "destroyed") : "no copy before");
        }
        h.ReleaseBundles();
    }

    static MakerEditTest.Node NodeOf(MakerCoreTest.Harness h, string file)
    {
        var c = MakerEditTest.Import(h, file, out string err);
        if (c == null) throw new Exception("import " + Rel(file) + ": " + err);
        var n = MakerEditTest.NewNode(h, c, Rel(file));
        if (!n.Ok) { n.Dispose(); throw new Exception("node " + Rel(file) + ": " + n.Error); }
        return n;
    }

    // ---------------------------------------------------------------- BT4

    static void BustExpect(float s, float w, float b, bool extrapolate, out float d, out float e, out float st, out float gy)
    {
        // in double (the stage computes in float; compared at 1e-6)
        double t = Math.Min(1, Math.Max(0, (double)s * b + 0.01));
        double Tri(double v0, double v1, double v2) => t < 0.5 ? v0 + (v1 - v0) * (2 * t) : v1 + (v2 - v1) * (2 * t - 1);
        st = (float)Tri(1, 0.1, 0.01); e = (float)Tri(0.2, 0.15, 0.05); d = (float)Tri(0.2, 0.1, 0.1);
        double wl = extrapolate ? w : Math.Min(1, Math.Max(0, w));
        gy = (float)(-0.05 * wl * b * s * 0.5);
    }

    static void BT4(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(h.Build(FemaleCard)))
        {
            var live = bb.B;
            if (!Check("BT4: built", live.Ok, live.Error)) return;
            var ctx = live.Context;
            var bust = ctx.Motion.Get<BustStage>();
            var body = ctx.Chara.Body;
            Info($"BR-Chan: softness {G(body.Bust.Softness)}, weight {G(body.Bust.Weight)}, size {G(body.Shape[1])}");
            Check("BT4: BR-Chan's card values are the worked example's", Math.Abs(body.Bust.Softness - 0.57f) < 1e-4 && Math.Abs(body.Bust.Weight - 0.388f) < 1e-3 && Math.Abs(body.Shape[1] - 0.58f) < 1e-4);
            void Expect(string what, float s, float w, float b)
            {
                BustExpect(s, w, b, BustStage.ExtrapolateGravity, out float d, out float e, out float st, out float gy);
                var rep = BustReport.Of(ctx);
                var br = rep.Physics.Chains.Where(c => c.Kind == 0 || c.Kind == 1).ToList();
                bool ok = br.Count == 2 && br.All(c => Math.Abs(c.Damping - d) < 1e-6 && Math.Abs(c.Elasticity - e) < 1e-6 && Math.Abs(c.Stiffness - st) < 1e-6 && Math.Abs(c.Gravity.y - gy) < 1e-6 && c.Gravity.x == 0 && c.Gravity.z == 0);
                var c0 = br.FirstOrDefault();
                Check($"BT4 {what}: damping / elasticity / stiffness / gravity = the bust formulas (1e-6)", ok,
                      $"expected {d:R} / {e:R} / {st:R} / {gy:R}; got {c0?.Damping:R} / {c0?.Elasticity:R} / {c0?.Stiffness:R} / {c0?.Gravity.y:R} ({br.Count} breast chains)");
            }
            // the worked example: stiffness 0.38692, elasticity 0.16594, damping 0.13188, gravity −0.003207
            BustExpect(0.57f, 0.388f, 0.58f, false, out float wd, out float we, out float ws, out float wg);
            Check("BT4: worked example in double", Math.Abs(ws - 0.38692) < 1e-6 && Math.Abs(we - 0.16594) < 1e-6 && Math.Abs(wd - 0.13188) < 1e-6 && Math.Abs(wg - -0.003207) < 2e-6, $"{ws:R} {we:R} {wd:R} {wg:R}");
            Expect("BR-Chan as built", body.Bust.Softness, body.Bust.Weight, body.Shape[1]);
            float s0 = body.Bust.Softness, w0 = body.Bust.Weight;
            foreach (var extrapolate in new[] { false, true })
            {
                BustStage.ExtrapolateGravity = extrapolate;
                string rule = extrapolate ? "extrapolated (HB2 alternative)" : "clamped (Y10)";
                foreach (var w in new[] { -0.5f, 1.5f })
                {
                    Set(live, "chara.body.bust.weight", () => body.Bust.Weight = w);
                    Expect($"weight {G(w)}, gravity {rule}", body.Bust.Softness, w, body.Shape[1]);
                }
                Set(live, "chara.body.bust.weight", () => body.Bust.Weight = w0);
                foreach (var s in new[] { -0.5f, 1.5f })
                {
                    Set(live, "chara.body.bust.softness", () => body.Bust.Softness = s);
                    Expect($"softness {G(s)}, gravity {rule}", s, body.Bust.Weight, body.Shape[1]);
                }
                Set(live, "chara.body.bust.softness", () => body.Bust.Softness = s0);
            }
            BustStage.ExtrapolateGravity = false;
            Check("BT4: Y10 default = clamped (BustStage.ExtrapolateGravity false)", !BustStage.ExtrapolateGravity && BustStage.BustGravityY(0.5f, 1.5f, 0.6f) == BustStage.BustGravityY(0.5f, 1f, 0.6f) && BustStage.BustGravityY(0.5f, -0.5f, 0.6f) == 0f);
            // a body-shape change re-derives the chains; the values follow the new size (and = a fresh build)
            Set(live, "chara.body.shape[1]", () => body.Shape[1] = 0.9f);
            Expect("size 0.9 (after the re-derivation)", body.Bust.Softness, body.Bust.Weight, 0.9f);
            var dd = VsFresh(h, live, out string err);
            Check("BT4: after the bust edits = a fresh build", dd != null && dd.Equal, dd?.ToString() ?? err);
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- BT5

    static void BT5(MakerCoreTest.Harness h)
    {
        var rows = h.Catalog.EntriesOf(336).Where(r => r.IsVanilla).OrderBy(r => r.Key.Id).ToList();
        int same = 0, total = 0;
        var bad = new List<string>();
        void One(float hh, float s, float v, string what)
        {
            total++;
            var u = Color.HSVToRGB(hh, s, v);
            var m = MakerEdits.HsvToRgb(hh, s, v);
            if (Bits(u.r) == Bits(m.R) && Bits(u.g) == Bits(m.G) && Bits(u.b) == Bits(m.B)) same++;
            else if (bad.Count < 4) bad.Add($"{what} ({G(hh)}, {G(s)}, {G(v)}): Unity ({G(u.r)}, {G(u.g)}, {G(u.b)}) vs ({G(m.R)}, {G(m.G)}, {G(m.B)})");
        }
        foreach (var r in rows)
        {
            One(EditContext.F(r["BaseH"]), EditContext.F(r["BaseS"]), EditContext.F(r["BaseV"]), "row " + r.Key + " base");
            One(EditContext.F(r["SampleH"]), EditContext.F(r["SampleS"]), EditContext.F(r["SampleV"]), "row " + r.Key + " sample");
        }
        var rnd = new System.Random(4242);
        for (int i = 0; i < 1000; i++) One((float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble(), "random");
        Check($"BT5: MakerEdits.HsvToRgb = Color.HSVToRGB bit for bit ({rows.Count} rows of 336 × base / sample, 1,000 random)", rows.Count == 7 && same == total, $"{same} of {total}" + (bad.Count > 0 ? "; " + string.Join(" | ", bad) : ""));
        if (rows.Count > 3)
        {
            var r3 = rows.First(r => r.Key.Id == 3);
            var c = Color.HSVToRGB(EditContext.F(r3["BaseH"]), EditContext.F(r3["BaseS"]), EditContext.F(r3["BaseV"]));
            Check("BT5: row 3 «タン» = (0.7, 0.6042, 0.553) (1e-4)", Math.Abs(c.r - 0.7) < 1e-4 && Math.Abs(c.g - 0.6042) < 1e-4 && Math.Abs(c.b - 0.553) < 1e-4, $"{G(c.r)} {G(c.g)} {G(c.b)}");
        }
        // the push through the realization (package B's op)
        using (var n = NodeOf(h, FemaleCard))
        {
            var ids = new[] { n.Id };
            int e0 = n.Session.Commands.UndoList.Count;
            var res = n.Ops.ApplyColourPreset(ids, "appearance.chara.body.skin.color", "illusion.hs2#3", User);
            n.Apply();
            var r = n.Real.LastEditFlush;
            var got = r?.CreatorsRebuilt.Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList() ?? new List<string>();
            var col = n.Stored.Chara.Body.Skin.Color;
            var want = Color.HSVToRGB(0.058f, 0.21f, 0.7f);
            Check("BT5: the swatch push: one entry, body.skin.color = HSVToRGB(row 3) with α 1, only the colour creators rebuilt (body + face)",
                  res != null && res.Ok && n.Session.Commands.UndoList.Count == e0 + 1 && got.SequenceEqual(new[] { BodyC, FaceC }.OrderBy(s => s, StringComparer.Ordinal)) &&
                  Math.Abs(col.R - want.r) < 1e-6 && Math.Abs(col.G - want.g) < 1e-6 && Math.Abs(col.B - want.b) < 1e-6 && col.A == 1f,
                  $"op {res}; entries +{n.Session.Commands.UndoList.Count - e0} ({n.Session.Commands.UndoList.FirstOrDefault()?.Label}); rebuilt [{string.Join(", ", got)}]; colour ({G(col.R)}, {G(col.G)}, {G(col.B)}, {G(col.A)})");
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- BT6

    static Vector4 RegionUv(ListEntry L, Vec4 lay)
    {
        float cx = EditContext.F(L["CenterX"]), mx = EditContext.F(L["MoveX"]), cy = EditContext.F(L["CenterY"]), my = EditContext.F(L["MoveY"]), cs = EditContext.F(L["CenterScale"]), a = EditContext.F(L["AddScale"]);
        return new Vector4(Mathf.Lerp(cs + a, cs - a, lay.X), Mathf.Lerp(cs + a, cs - a, lay.Y), Mathf.Lerp(cx + mx, cx - mx, lay.Z), Mathf.Lerp(cy + my, cy - my, lay.W));
    }

    static bool SameV(Vector4 a, Vector4 b) => Bits(a.x) == Bits(b.x) && Bits(a.y) == Bits(b.y) && Bits(a.z) == Bits(b.z) && Bits(a.w) == Bits(b.w);

    static void BT6(MakerCoreTest.Harness h)
    {
        var regions = h.Catalog.EntriesOf(8);
        ListEntry Row(string r) { ContentId.TryParse(r, out var id); return regions.FirstOrDefault(e => e.Key == id); }
        const string Mod20 = "com.toshiaki.bodypaint_layout_abdomen#20";
        using (var bb = new Disposer(h.Build(FemaleCard)))
        {
            var live = bb.B;
            if (!Check("BT6: built", live.Ok, live.Error)) return;
            var ctx = live.Context; var x = ctx.Edit;
            var p = ctx.Chara.Body.Paint[0];
            var c0 = x.BodySkin; var c1 = x.BodyDetail;
            Check("BT6: the creators' initial UV / rotation captured", c0.Initial.ContainsKey("_Texture5UV") && c0.Initial.ContainsKey("_Texture5Rotator") && c1.Initial.ContainsKey("_Texture6UV"),
                  string.Join(", ", c0.Initial.Select(kv => kv.Key + " " + kv.Value)));
            Vec4 lay0 = p.Layout; float rot0 = p.Rotation; string item0 = p.LayoutItem;
            Set(live, "chara.body.paint[0].layout", () => p.Layout = new Vec4(0.5f, 0.5f, 0.5f, 0.5f));
            Set(live, "chara.body.paint[0].rotation", () => p.Rotation = 0.5f);
            foreach (var reg in new[] { "illusion.hs2#0", "illusion.hs2#2", Mod20 })
            {
                var L = Row(reg);
                if (L == null) { if (reg == Mod20) Warn("BT6 mod region 20", "not installed: skipped"); else Check("BT6 region " + reg + " listed", false); continue; }
                Set(live, "chara.body.paint[0].layoutItem", () => p.LayoutItem = reg);
                var want = RegionUv(L, p.Layout);
                bool ok = true;
                foreach (var c in new[] { c0, c1 })
                    ok &= SameV(c.Creator.Mat.GetVector("_Texture5UV"), want) && Bits(c.Creator.Mat.GetFloat("_Texture5Rotator")) == Bits(Mathf.Lerp(1, -1, 0.5f));
                Check($"BT6 region {reg}: both creators' _Texture5UV = the layout's values {want}, rotator 0", ok, $"colour {c0.Creator.Mat.GetVector("_Texture5UV")}, detail {c1.Creator.Mat.GetVector("_Texture5UV")}");
                Check($"BT6 region {reg}: the region row is an input of both creators", c0.Textures.TryGetValue("_Texture5UV", out var t0) && t0.row == L && c1.Textures.TryGetValue("_Texture5UV", out var t1) && t1.row == L);
                if (reg == "illusion.hs2#0")
                {
                    Check("BT6 worked example: region 0, layout (0.5, …), rotation 0.5 → (13, 13, 0.375, −0.375)", Math.Abs(want.x - 13) < 1e-5 && Math.Abs(want.y - 13) < 1e-5 && Math.Abs(want.z - 0.375) < 1e-6 && Math.Abs(want.w + 0.375) < 1e-6, want.ToString("R"));
                    Set(live, "chara.body.paint[0].layout", () => p.Layout = new Vec4(0f, 0.5f, 0.5f, 0.5f));
                    float u0 = c0.Creator.Mat.GetVector("_Texture5UV").x;
                    Set(live, "chara.body.paint[0].layout", () => p.Layout = new Vec4(1.5f, 0.5f, 0.5f, 0.5f));
                    float u15 = c0.Creator.Mat.GetVector("_Texture5UV").x;
                    Check("BT6 worked example: x = 0 → 23, x = 1.5 → 3 (clamped lerp)", Math.Abs(u0 - 23) < 1e-5 && Math.Abs(u15 - 3) < 1e-5, $"{G(u0)}, {G(u15)}");
                    Set(live, "chara.body.paint[0].layout", () => p.Layout = new Vec4(0.5f, 0.5f, 0.5f, 0.5f));
                }
                var d = VsFresh(h, live, out string err);
                Check($"BT6 region {reg}: = a fresh build", d != null && d.Equal, d?.ToString() ?? err);
            }
            // a region that does not resolve → the initial values (valid → missing → valid)
            const string Missing = "illusion.hs2#9999";
            Set(live, "chara.body.paint[0].layoutItem", () => p.LayoutItem = Missing);
            bool init = true;
            foreach (var c in new[] { c0, c1 })
                init &= SameV(c.Creator.Mat.GetVector("_Texture5UV"), c.Initial["_Texture5UV"]) && Bits(c.Creator.Mat.GetFloat("_Texture5Rotator")) == Bits(c.Initial["_Texture5Rotator"].x);
            Check("BT6 missing region: both creators back to their initial _Texture5UV / rotator", init, $"colour {c0.Creator.Mat.GetVector("_Texture5UV")} vs initial {c0.Initial["_Texture5UV"]}");
            Check("BT6 missing region: the creators' input shows it ('initial')", c0.Textures.TryGetValue("_Texture5UV", out var tm) && tm.row == null && tm.column == "initial");
            var dm = VsFresh(h, live, out string errm);
            Check("BT6 missing region: = a fresh build (valid → missing)", dm != null && dm.Equal, dm?.ToString() ?? errm);
            Set(live, "chara.body.paint[0].layoutItem", () => p.LayoutItem = "illusion.hs2#2");
            var dv = VsFresh(h, live, out string errv);
            Check("BT6 missing → valid: = a fresh build", dv != null && dv.Equal, dv?.ToString() ?? errv);
            Set(live, "chara.body.paint[0].layoutItem", () => p.LayoutItem = item0);
            Set(live, "chara.body.paint[0].layout", () => p.Layout = lay0);
            Set(live, "chara.body.paint[0].rotation", () => p.Rotation = rot0);
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- BT7

    static void BT7(MakerCoreTest.Harness h)
    {
        bool pin = CharaAssembler.MaleHeightPin075;
        try
        {
            using (var bb = new Disposer(h.Build(MaleCard)))
            {
                var live = bb.B;
                if (!Check("BT7: HERO107 built", live.Ok, live.Error)) return;
                var ctx = live.Context; var body = ctx.Chara.Body;
                string bones0 = FaceDigest.BonesText(ctx, "body");
                // the rows hidden for males: their stored values still drive the bones
                foreach (var i in new[] { 5, 6, 7, 8, 32 })
                {
                    float old = body.Shape[i];
                    Set(live, "chara.body.shape[" + i + "]", () => body.Shape[i] = old < 0.5f ? 0.95f : 0.05f);
                    string b1 = FaceDigest.BonesText(ctx, "body");
                    var d = VsFresh(h, live, out string err);
                    Check($"BT7 male hidden row shape[{i}]: the bones follow the stored value, = a fresh build", b1 != bones0 && d != null && d.Equal, d?.ToString() ?? err);
                    Set(live, "chara.body.shape[" + i + "]", () => body.Shape[i] = old);
                }
                Check("BT7: the hidden rows set back = the start", FaceDigest.BonesText(ctx, "body") == bones0);
                // the height: the pin on → 0.85 = 0.75 in effect; off (default) → the card value
                float h0 = body.Shape[0];
                CharaAssembler.MaleHeightPin075 = true;
                Set(live, "chara.body.shape[0]", () => body.Shape[0] = 0.75f);
                string pinned = FaceDigest.BonesText(ctx, "body");
                Set(live, "chara.body.shape[0]", () => body.Shape[0] = 0.85f);
                Check("BT7 MaleHeightPin075 on: height 0.85 drives the bones as 0.75", FaceDigest.BonesText(ctx, "body") == pinned);
                CharaAssembler.MaleHeightPin075 = false;
                Set(live, "chara.body.shape[0]", () => body.Shape[0] = 0.85f);
                string free85 = FaceDigest.BonesText(ctx, "body");
                Set(live, "chara.body.shape[0]", () => body.Shape[0] = 0.75f);
                Check("BT7 MaleHeightPin075 off: 0.85 and 0.75 differ; 0.75 = the pinned bones", free85 != pinned && FaceDigest.BonesText(ctx, "body") == pinned);
                Set(live, "chara.body.shape[0]", () => body.Shape[0] = 1.05f);
                var dh = VsFresh(h, live, out string errh);
                Check("BT7 height 1.05 (outside 0..1, the user's card) = a fresh build", dh != null && dh.Equal, dh?.ToString() ?? errh);
                Set(live, "chara.body.shape[0]", () => body.Shape[0] = h0);
                // the male chains keep running with the male card's values (the game's flags are never read,
                // ; BustOptions.MaleBustPhysics, default on) — 4.2 does not change that (A42-9)
                var mb = BustReport.Of(ctx);
                var stage = ctx.Motion.Get<BustStage>();
                Check("BT7 male: no breast normals; bust physics per BustOptions.MaleBustPhysics (the stage's male option)", ctx.BustNormals == null && stage != null && stage.IsMale && mb.Physics.Enabled == stage.Options.MaleBustPhysics,
                      $"physics {(mb.Physics.Enabled ? "on" : "off")}, MaleBustPhysics {stage?.Options.MaleBustPhysics}");
            }
        }
        finally { CharaAssembler.MaleHeightPin075 = pin; h.ReleaseBundles(); }
    }

    // ---------------------------------------------------------------- BT8

    static void BT8(MakerCoreTest.Harness h)
    {
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        try
        {
            using (var bb = new Disposer(h.Build(FemaleCard)))
            {
                var live = bb.B;
                if (!Check("BT8: built", live.Ok, live.Error)) return;
                var ctx = live.Context; var ed = ctx.Editor; var x = ctx.Edit;
                var keep = new[] { x.BodySkin, x.BodyDetail }.Select(c => (c.Creator.Under.ToList(), c.Creator.Over.ToList())).ToList();
                // (family, path, colour creator rebuilt, detail creator rebuilt)
                var fams = new[]
                {
                    ("skin colour", "chara.body.skin.color", true, false),
                    ("sunburn colour", "chara.body.sunburn.color", true, false),
                    ("paint colour", "chara.body.paint[0].color", true, false),
                    ("paint gloss", "chara.body.paint[0].gloss", false, true),
                    ("paint metallic", "chara.body.paint[1].metallic", false, true),
                    ("paint layout", "chara.body.paint[0].layout", true, true),
                    ("paint rotation", "chara.body.paint[1].rotation", true, true),
                    ("region", "chara.body.paint[0].layoutItem", true, true),
                    ("body shape", "chara.body.shape[3]", false, false),
                    ("nipple colour (material)", "chara.body.nipple.color", false, false),
                    ("gloss (material)", "chara.body.skin.gloss", false, false),
                };
                int ok = 0, n = 0;
                var bad = new List<string>();
                foreach (var (name, path, col, det) in fams)
                    for (int layers = 0; layers <= 2; layers++)
                    {
                        for (int i = 0; i < 2; i++)
                        {
                            var c = i == 0 ? x.BodySkin : x.BodyDetail;
                            c.Creator.Under.Clear(); c.Creator.Over.Clear();
                            for (int k = 0; k < layers; k++) c.Creator.Over.Add(new SkinLayer { TexType = 1, Texture = tex, Source = "bt8" });
                        }
                        ed.MarkDirty(path);
                        int w = ed.PendingBodyWeight();
                        int want = (col ? 1 + layers : 0) + (det ? 1 + layers : 0);
                        bool rules = CharaEditFlush.ThrottlesWeight(BodyThrottleMode.Auto, w) == (want >= 2) && CharaEditFlush.ThrottlesWeight(BodyThrottleMode.Always, w) == (want >= 1)
                                     && !CharaEditFlush.ThrottlesWeight(BodyThrottleMode.Off, w);
                        n++;
                        if (w == want && rules) ok++; else bad.Add($"{name} {layers} layer(s): W {w}, expected {want}");
                        // the layers go before the flush (a fake layer is never composited); the flush reports the weight
                        for (int i = 0; i < 2; i++) { var c = i == 0 ? x.BodySkin : x.BodyDetail; c.Creator.Under.Clear(); c.Creator.Over.Clear(); c.Creator.Under.AddRange(keep[i].Item1); c.Creator.Over.AddRange(keep[i].Item2); }
                        int w2 = ed.PendingBodyWeight();
                        var r = ed.Flush();
                        if (r.ThrottleWeight != w2) bad.Add($"{name}: FlushReport.ThrottleWeight {r.ThrottleWeight} vs pending {w2}");
                    }
                Check("BT8: W = Σ over the rebuilt body creators of (1 + layers) for every family with 0, 1, 2 layers; the rule per mode (auto W ≥ 2, always W ≥ 1, off never)", ok == n && bad.Count == 0, $"{ok} of {n}" + (bad.Count > 0 ? "; " + string.Join(" | ", bad.Take(6)) : ""));
                Check("BT8: a skin-colour drag (W 1) is not throttled under auto; a layout drag (W 2) is", !CharaEditFlush.ThrottlesWeight(BodyThrottleMode.Auto, 1) && CharaEditFlush.ThrottlesWeight(BodyThrottleMode.Auto, 2));
                bool parse = CharaEditFlush.TryParseBodyThrottle("always", out var m1) && m1 == BodyThrottleMode.Always && CharaEditFlush.TryParseBodyThrottle("OFF", out var m2) && m2 == BodyThrottleMode.Off
                             && CharaEditFlush.TryParseBodyThrottle("auto", out var m3) && m3 == BodyThrottleMode.Auto && !CharaEditFlush.TryParseBodyThrottle("x", out _)
                             && CharaEditFlush.BodyThrottleName(BodyThrottleMode.Always) == "always";
                Check("BT8: maker.bodyThrottle values parse (auto / always / off); default auto", parse && CharaEditFlush.BodyThrottle == BodyThrottleMode.Auto);
                var dd = VsFresh(h, live, out string err);
                Check("BT8: after the families = a fresh build", dd != null && dd.Equal, dd?.ToString() ?? err);
            }
        }
        finally { Object.DestroyImmediate(tex); h.ReleaseBundles(); }
    }

    // ---------------------------------------------------------------- BT9

    static void BT9(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(h.Build(FemaleCard)))
        {
            var live = bb.B;
            if (!Check("BT9: built", live.Ok, live.Error)) return;
            var ctx = live.Context; var body = ctx.Chara.Body;
            FaceDigest.Result D() => FaceDigest.Compute(ctx, "body", false);
            var d0 = D();
            Check("BT9: the body digest has normals and bust sections; stable", d0.Normals != null && d0.Bust != null && d0.Equals(D()), d0.ToString());
            Check("BT9: the face digest has no normals / bust sections", FaceDigest.Compute(ctx, "face", false).Normals == null);
            string all = FaceDigest.BonesText(ctx, "all");
            Check("BT9: no cf_J_look_L / cf_J_look_R in any bone section", !all.Contains("cf_J_look_L") && !all.Contains("cf_J_look_R") && ctx.HeadBones.ContainsKey("cf_J_look_L"));
            int dst = FaceDigest.BodyShapeDestinations().Count;
            Check("BT9: the body bones = the shape map's destinations (incl. the skirt *_dam rows)", dst > 80 && all.Contains("body cf_J_sk_00_00_dam") && all.Contains("body cf_J_ArmElbo_low_s_L"), dst + " destinations");
            void Expect(string what, string path, Action set, bool normals, bool bust)
            {
                var a = D();
                Set(live, path, set);
                var b = D();
                Check($"BT9 {what}: normals {(normals ? "changes" : "stays")}, bust {(bust ? "changes" : "stays")}", (a.Normals != b.Normals) == normals && (a.Bust != b.Bust) == bust,
                      $"normals {a.Normals?.Substring(0, 8)} → {b.Normals?.Substring(0, 8)}, bust {a.Bust?.Substring(0, 8)} → {b.Bust?.Substring(0, 8)}");
            }
            float s1 = body.Shape[1];
            Expect("bust size 0.58 → 0.3 (rate 1 → 0.6)", "chara.body.shape[1]", () => body.Shape[1] = 0.3f, true, true);
            Expect("bust size 0.3 → 0.7 (rate 0.6 → 1)", "chara.body.shape[1]", () => body.Shape[1] = 0.7f, true, true);
            Expect("bust size 0.7 → 0.9 (rate 1 both)", "chara.body.shape[1]", () => body.Shape[1] = 0.9f, false, true);
            Expect("softness", "chara.body.bust.softness", () => body.Bust.Softness = 0.2f, false, true);
            Expect("weight", "chara.body.bust.weight", () => body.Bust.Weight = 0.8f, false, true);
            Expect("skin colour", "chara.body.skin.color", () => body.Skin.Color = new Color4(0.5f, 0.4f, 0.3f, 1), false, false);
            Expect("nipple gloss (material)", "chara.body.nipple.gloss", () => body.Nipple.Gloss = 0.1f, false, false);
            Set(live, "chara.body.shape[1]", () => body.Shape[1] = s1);
            var dd = VsFresh(h, live, out string err, (l, f) =>
            {
                var a = FaceDigest.Compute(l.Context, "all", false); var b = FaceDigest.Compute(f.Context, "all", false);
                Check("BT9: the body + face digest (all sections) = a fresh build's", a.Equals(b), a + " vs " + b);
            });
            Check("BT9: after the edits = a fresh build", dd != null && dd.Equal, dd?.ToString() ?? err);
        }
        h.ReleaseBundles();
    }

    // ================================================================ RunRender (graphics)

    public static void RunRender()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"maker body render (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName})");
        bool device = FaceDigest.HasDevice;
        Check("graphics device present", device, SystemInfo.graphicsDeviceType.ToString());
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        try
        {
            if (device)
                using (var h = MakerCoreTest.Harness.Open())
                {
                    if (Want("R1")) Guard("R1 selective body rebuilds = full rebuilds (RT read-back)", () => R1(h));
                    if (Want("R2")) Guard("R2 gloss state on the body", () => R2(h));
                    if (Want("R3")) Guard("R3 the blended normals are skinned", () => R3(h));
                }
        }
        finally { FaceSetup.Seed = fseed; }
        Finish("maker_body_render_report.txt", "MakerBodyTest.render", sw);
    }

    /// <summary>
    /// The RT of every creator a body family's selective flush did NOT rebuild (it kept its earlier RT) read back, then every
    /// creator rebuilt (skin.body, and skin.face for a family that feeds the face) and read back again: the kept RTs equal
    /// their full rebuild (each creator is a function of its own inputs). Families rebuilding both body creators compare the
    /// colour creator once (determinism). Few 4096² read-backs (a long run of them reset the D3D12 device in 4.1).
    /// </summary>
    static void R1(MakerCoreTest.Harness h)
    {
        int fams = 0, ok = 0, reads = 0;
        foreach (var file in TestTargets.Load().VisualCards.Where(File.Exists))
        {
            MakerCoreTest.Built live = null;
            try
            {
                live = h.Build(file);
                if (!Check(Rel(file) + ": built", live.Ok, live.Error)) continue;
                var ctx = live.Context; var ed = ctx.Editor; var x = ctx.Edit;
                foreach (var fam in BodyFamilies(ctx.Chara, h.Catalog, ctx.IsMale).Where(f => f.Expect.Length > 0))
                {
                    object old = fam.Get(), alt = fam.Alt(old);
                    if (alt == null || Equals(alt, old)) continue;
                    fams++;
                    fam.Set(alt);
                    ed.MarkDirty(fam.Path);
                    var r = ed.Flush();
                    bool face = fam.Expect.Contains(FaceC);
                    var pool = x.Composites().Where(c => c.Name == BodyC || c.Name == BodyD || (face && (c.Name == FaceC || c.Name == FaceD))).ToList();
                    var kept = pool.Where(c => !r.CreatorsRebuilt.Contains(c.Name)).ToList();
                    if (kept.Count == 0) kept = pool.Where(c => c.Name == BodyC).ToList();
                    var before = kept.ToDictionary(c => c.Name, c => FaceDigest.RtHash(c.Creator.Target));
                    ed.MarkUnit(UnitMap.SkinBody);
                    if (face) ed.MarkUnit(UnitMap.SkinFace);
                    var rf = ed.Flush();
                    var after = kept.ToDictionary(c => c.Name, c => FaceDigest.RtHash(c.Creator.Target));
                    reads += 2 * kept.Count;
                    var diff = kept.Where(c => before[c.Name] == null || before[c.Name] != after[c.Name]).Select(c => c.Name).ToList();
                    if (Check($"{Rel(file)} {fam.Name}: the RTs the selective flush kept ({string.Join(", ", kept.Select(c => c.Name))}) = their full rebuild",
                              diff.Count == 0 && rf.CreatorsRebuilt.Contains(BodyC) && rf.CreatorsRebuilt.Contains(BodyD), diff.Count == 0 ? "" : "differ: " + string.Join(", ", diff))) ok++;
                    SetBack(fam, old, ed);
                }
            }
            finally { live?.Dispose(); h.ReleaseBundles(); }
        }
        Check("R1: every body family", fams > 0 && ok == fams, $"{ok} of {fams} ({reads} RT read-backs)");
    }

    static float GlossOf(Material m) => m && m.HasProperty("_Gloss") ? m.GetFloat("_Gloss") : float.NaN;

    /// <summary>The gloss state on the body draw (4.0 rule): _Gloss = lerp(0, 0.8, skin gloss) + 0.2 · state, kept after a
    /// draw.body edit and after a skin gloss edit.</summary>
    static void R2(MakerCoreTest.Harness h)
    {
        using (var n = NodeOf(h, FemaleCard))
        {
            var ids = new[] { n.Id };
            var real = n.Real;
            Material Body() => real.Context.BodyDraw;
            float Expect() => Mathf.Lerp(0, 0.8f, n.Stored.Chara.Body.Skin.Gloss);
            float g0 = GlossOf(Body());
            Check("R2: state 0 → body _Gloss = lerp(0, 0.8, skin gloss)", Mathf.Abs(g0 - Expect()) < 1e-6f, $"{g0} vs {Expect()}");
            n.Ops.SetState(ids, "gloss", 0.5f, User); n.Apply();
            Check("R2: state 0.5 → body _Gloss = base + 0.1", Mathf.Abs(GlossOf(Body()) - (Expect() + 0.1f)) < 1e-6f, "" + GlossOf(Body()));
            n.Ops.EditAppearance(ids, new List<(string, object)> { ("appearance.chara.body.nipple.gloss", 0.21f) }, User); n.Apply();
            Check("R2: after a draw.body edit (nipple gloss) still base + 0.1", Mathf.Abs(GlossOf(Body()) - (Expect() + 0.1f)) < 1e-6f, "" + GlossOf(Body()));
            n.Ops.EditAppearance(ids, new List<(string, object)> { ("appearance.chara.body.skin.gloss", 0.35f) }, User); n.Apply();
            Check("R2: after a skin gloss edit: the new base + 0.1", Mathf.Abs(GlossOf(Body()) - (Expect() + 0.1f)) < 1e-6f, $"{GlossOf(Body())} vs {Expect() + 0.1f}");
            n.Ops.ApplyColourPreset(ids, "appearance.chara.body.skin.color", "illusion.hs2#1", User); n.Apply();
            Check("R2: after a swatch push (composite only) still base + 0.1", Mathf.Abs(GlossOf(Body()) - (Expect() + 0.1f)) < 1e-6f, "" + GlossOf(Body()));
            n.Ops.SetState(ids, "gloss", 0f, User); n.Apply();
            Check("R2: state 0 → base", Mathf.Abs(GlossOf(Body()) - Expect()) < 1e-6f, "" + GlossOf(Body()));
        }
        h.ReleaseBundles();
    }

    /// <summary>A female at bust size 0.3 skins with the blended normals: the drawn mesh is the owned copy holding
    /// Lerp(Min, Max, 0.6) (digest), and baking the skinned mesh with those normals differs from baking it with the shipped
    /// (Max) normals at the same pose — the renderer reads the copy's normals.</summary>
    static void R3(MakerCoreTest.Harness h)
    {
        if (!Check("R3: the vanilla NormalData read", VanillaNormals(h.Catalog, out var min, out var max, out var shipped))) return;
        using (var n = NodeOf(h, FemaleCard))
        {
            var ids = new[] { n.Id };
            n.Ops.EditAppearance(ids, new List<(string, object)> { ("appearance.chara.body.shape[1]", 0.3f) }, User); n.Apply();
            var ctx = n.Real.Context;
            var smr = ctx.BodyRenderer;
            var copy = DrawnMesh(ctx);
            var want = Blend(min, max, 0.6f);
            Check("R3: the drawn mesh = the owned copy, normals = Lerp(Min, Max, 0.6) (digest normals hash)", copy && ctx.OwnedMeshes.Contains(copy) && SameBits(NormalsOf(copy), want),
                  $"normals {FaceDigest.NormalsHash(copy)?.Substring(0, 12)}");
            var dg = FaceDigest.Compute(ctx, "body");
            Check("R3: the body digest with RT hashes (device) has every section", dg.Materials != null && dg.Normals != null && dg.Bust != null && dg.Rts != null, dg.ToString());
            Vector3[] Bake()
            {
                var m = new Mesh();
                try { smr.BakeMesh(m); return NormalsOf(m); }
                finally { Object.DestroyImmediate(m); }
            }
            var blended = Bake();
            copy.SetNormals(max);
            var maxBaked = Bake();
            copy.SetNormals(want);   // back (the unit's rate cache stays valid)
            int diff = DiffCount(blended, maxBaked);
            Check("R3: the skinned (baked) normals with the blend differ from those with the shipped normals at the same pose", diff > 1000 && SameBits(Bake(), blended), $"{diff} of {blended?.Length} vertices differ");
        }
        h.ReleaseBundles();
    }

    // ================================================================ RunTiming (graphics; its own Unity session)

    static void Target(string what, double ms, double target, string reason = null)
    {
        string d = $"{F(ms)} ms (target {F(target)})";
        if (double.IsNaN(ms)) { Info(what + ": not measured"); return; }
        if (ms <= target) Check(what, true, d);
        else if (ms <= 2 * target || reason != null) Warn(what, d + (reason != null ? "; " + reason : ""));
        else Check(what, false, d + " (above 2× the target)");
    }

    static RenderTexture tinyFence;
    /// <summary>Waits for the GPU work submitted so far: one texel of <paramref name="rt"/> copied into a 1×1 RT, that RT
    /// read back. (A region read-back of a 4096² RT allocates a full-size staging buffer per request on D3D12; hundreds of them
    /// in a batch run without frames removed the device, 887a0005 / 887a0007, in this test's first version.)</summary>
    static void Fence(RenderTexture rt)
    {
        if (!rt) return;
        if (!tinyFence || tinyFence.graphicsFormat != rt.graphicsFormat)
        {
            if (tinyFence) { tinyFence.Release(); Object.DestroyImmediate(tinyFence); }
            var d = rt.descriptor;
            d.width = d.height = 1; d.useMipMap = false; d.autoGenerateMips = false; d.depthBufferBits = 0; d.msaaSamples = 1;
            tinyFence = new RenderTexture(d) { name = "MakerBodyTest fence" };
            tinyFence.Create();
        }
        Graphics.CopyTexture(rt, 0, 0, 0, 0, 1, 1, tinyFence, 0, 0, 0, 0);
        AsyncGPUReadback.Request(tinyFence).WaitForCompletion();
    }

    static Texture2D Noise(int w, int hh, int seed)
    {
        var rnd = new System.Random(seed);
        var t = new Texture2D(w, hh, TextureFormat.RGBA32, true);
        var px = new Color32[w * hh];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32((byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256));
        t.SetPixels32(px);
        t.Apply(true);
        return t;
    }

    static double TimeOp(MakerEditTest.Node n, Func<OpResult> op, out OpResult res)
    {
        var t = Stopwatch.StartNew();
        res = op();
        n.Apply();
        return t.Elapsed.TotalMilliseconds;
    }

    /// <summary>and UB2–UB4, UB7 (edit mode, warm, medians). With -play the editor then enters play mode for the frame
    /// measurements of UB2 (throttled frames per second, PlayTiming; the runner needs -NoQuit).</summary>
    public static void RunTiming()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        int reps = int.Parse(Arg("-reps", "20"), CultureInfo.InvariantCulture);
        rep.AppendLine($"maker body timing (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName}; Editor, warm, median of {reps}); WARN above the target, FAIL above 2×");
        bool device = FaceDigest.HasDevice;
        Check("graphics device present", device, SystemInfo.graphicsDeviceType.ToString());
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        try
        {
            if (device)
                using (var h = MakerCoreTest.Harness.Open())
                {
                    if (Want("UB4")) Guard("UB4 breast normals: mesh copy at the build, one blend", () => UB4(h, reps));
                    if (Want("UB3")) Guard("UB3 body shape / bust drags: preview vs commit; material unit", () => UB3(h, reps));
                    if (Want("UB2")) Guard("UB2 body composite drag frames (GPU fences), swatch push, region pick", () => UB2(h, reps));
                    if (Want("UB7")) Guard("UB7 memory per female character", () => UB7(h));
                }
        }
        finally { FaceSetup.Seed = fseed; CharaEditFlush.ForcePreview = false; }
        bool play = TestArgs.Get().Contains("-play");
        // with -play the editor stays for the play-mode part (which ends it with the combined exit code)
        bool suite = inSuite;
        if (play) inSuite = true;
        Finish("maker_body_timing_report.txt", "MakerBodyTest.timing", sw);
        inSuite = suite;
        if (play)
        {
            playEditFails = fails;
            File.WriteAllText(PlayReport, "SUMMARY: 0 PASS, 1 FAIL\nMakerBodyTest.timing play: started, no result yet\n", new UTF8Encoding(false));
            PlayModeHarness.Start(nameof(MakerBodyTest) + "." + nameof(PlayTiming), PlayReport, 20);
        }
    }

    /// <summary>Failures of the edit-mode timing part before the play part (SessionState: the domain reloads on entering play mode).</summary>
    static int playEditFails
    {
        get => SessionState.GetInt("HS2Next.MakerBodyTest.TimingFails", 0);
        set => SessionState.SetInt("HS2Next.MakerBodyTest.TimingFails", value);
    }

    static void UB4(MakerCoreTest.Harness h, int reps)
    {
        var imp = MakerEditTest.Import(h, FemaleCard, out string err);
        if (!Check("UB4: BR-Chan imported", imp != null, err)) return;
        foreach (var (what, guid) in new[] { ("BR-Chan (vanilla body)", (string)null), ("uncensor " + UncBlend, UncBlend), ("uncensor dainty.highpoly (87,867 vertices)", "dainty.highpoly") })
        {
            if (guid != null && !HS2Next.Chara.Ext.CharaBuildHooks.Any(HS2Next.Chara.Ext.HookStage.BodyMesh)) { Info(what + ": no BodyMesh build hook (the official build): skipped"); continue; }
            var copies = new List<double>(); var blends = new List<double>(); var flushes = new List<double>();
            long bytes = 0; int vertices = 0;
            for (int k = 0; k < Math.Max(3, reps / 4); k++)
            {
                var c = WithUncensor(imp, guid);
                c.Subtree.Chara.Body.Shape[1] = 0.3f;
                using (var b = new Disposer(h.Build(c, what)))
                {
                    if (!b.B.Ok) { Check($"UB4 {what}: built", false, b.B.Error); break; }
                    var ctx = b.B.Context; var st = ctx.BustNormals;
                    if (k == 0) continue;   // the first build warms the bundles
                    if (st == null || st.Missing || !st.MeshCopied) { Info($"{what}: no copy ({st?.MissingReason})"); break; }
                    copies.Add(st.LastCopyMs); bytes = st.CopyBytes; vertices = DrawnMesh(ctx).vertexCount;
                    for (int i = 0; i < reps; i++)
                    {
                        float s = i % 2 == 0 ? 0.31f : 0.3f;
                        ctx.Chara.Body.Shape[1] = s;
                        ctx.Editor.MarkUnit(UnitMap.BustNormals);
                        var t = Stopwatch.StartNew();
                        ctx.Editor.Flush();
                        flushes.Add(t.Elapsed.TotalMilliseconds);
                        blends.Add(st.LastBlendMs);
                    }
                }
            }
            Info($"UB4 {what}: {vertices} vertices; mesh copy at the build {F(Median(copies))} ms ({copies.Count} builds), {bytes} B ({bytes / 1048576.0:F2} MiB); one blend (lerp + SetNormals) {F(Median(blends))} ms; the unit's flush {F(Median(flushes))} ms");
            if (guid == null || guid == UncBlend)
            {
                Target($"UB4 {what}: mesh copy at a female build", Median(copies), 3);
                Target($"UB4 {what}: breast normals blend (≤ 0.5 ms → live in drags)", Median(blends), 0.5);
            }
            if (guid == null) Check("UB4 decision: the blend is live in drags (BustNormalsUnit.LiveInDrags) exactly when it measured ≤ 0.5 ms", BustNormalsUnit.LiveInDrags == (Median(blends) <= 0.5), $"LiveInDrags {BustNormalsUnit.LiveInDrags}, blend {F(Median(blends))} ms");
        }
        h.ReleaseBundles();
    }

    static void UB3(MakerCoreTest.Harness h, int reps)
    {
        CharaEditFlush.ForcePreview = true;
        try
        {
            foreach (var (what, path, v0, dv, tPrev, tCommit) in new[]
            {
                ("bust size drag (normals live, rate changes)", "appearance.chara.body.shape[1]", 0.3f, 0.01f, 0.6, 6.0),
                ("body shape drag (index 3)", "appearance.chara.body.shape[3]", 0.3f, 0.05f, 0.6, 6.0),
                ("bust softness drag (bust unit)", "appearance.chara.body.bust.softness", 0.3f, 0.05f, 0.3, 6.0),
            })
                using (var n = NodeOf(h, FemaleCard))
                {
                    var ids = new[] { n.Id };
                    var prev = new List<double>(); var commit = new List<double>();
                    for (int k = 0; k < Math.Max(3, reps / 4); k++)
                    {
                        var tx = n.Session.Commands.BeginOrJoin("Drag", new ExecuteOptions { Source = CommandSource.User, Label = "Drag" });
                        try
                        {
                            for (int i = 0; i < 8; i++)
                            {
                                n.Ops.EditAppearance(ids, new List<(string, object)> { (path, v0 + dv * i + 0.001f * k) }, User);
                                n.Apply();
                                var r = n.Real.LastEditFlush;
                                if (r != null && r.Mode == FlushMode.Preview) prev.Add(r.Milliseconds);
                            }
                            tx.Commit();
                        }
                        finally { tx.Dispose(); }
                        n.Apply();
                        var rc = n.Real.LastEditFlush;
                        if (rc != null && rc.Mode == FlushMode.Normal) commit.Add(rc.Milliseconds);
                    }
                    var lf = n.Real.LastEditFlush;
                    Info($"UB3 {what}: preview flush median {F(Median(prev))} ms ({prev.Count}), commit flush median {F(Median(commit))} ms ({commit.Count})");
                    Target($"UB3 preview flush of a {what}", Median(prev), tPrev);
                    Target($"UB3 commit flush after a {what}", Median(commit), tCommit);
                }
            CharaEditFlush.ForcePreview = false;
            using (var n = NodeOf(h, FemaleCard))
            {
                var ids = new[] { n.Id };
                var l = new List<double>();
                for (int i = 0; i < reps; i++)
                {
                    n.Ops.EditAppearance(ids, new List<(string, object)> { ("appearance.chara.body.nipple.gloss", 0.2f + 0.03f * i) }, User);
                    var r = n.Real.LastEditFlush;
                    if (r != null) l.Add(r.Milliseconds);
                }
                Target("body material unit draw.body flush", Median(l), 0.1);
            }
        }
        finally { CharaEditFlush.ForcePreview = false; h.ReleaseBundles(); }
    }

    static void UB2(MakerCoreTest.Harness h, int reps)
    {
        var layer = Noise(2048, 2048, 7);
        try
        {
            using (var n = NodeOf(h, FemaleCard))
            {
                var ids = new[] { n.Id };
                var ctx = n.Real.Context; var x = ctx.Edit; var ed = ctx.Editor;
                var body = new[] { x.BodySkin, x.BodyDetail };
                var keep = body.Select(c => (c.Creator.Under.ToList(), c.Creator.Over.ToList())).ToList();
                // warm-up (GPU clocks), then the creators alone with 4.1's UF1 method (fence, rebuild, fence)
                for (int i = 0; i < 30; i++) { x.BodySkin.Creator.Rebuild(); x.BodyDetail.Creator.Rebuild(); Fence(x.BodyDetail.Creator.Target); }
                double Gpu(Action a)
                {
                    var l = new List<double>();
                    for (int i = 0; i < reps; i++)
                    {
                        Fence(x.BodyDetail.Creator.Target);
                        var t = Stopwatch.StartNew();
                        a();
                        Fence(x.BodySkin.Creator.Target);
                        l.Add(t.Elapsed.TotalMilliseconds);
                    }
                    return Median(l);
                }
                double gc = Gpu(() => x.BodySkin.Creator.Rebuild()), gd = Gpu(() => x.BodyDetail.Creator.Rebuild()),
                       gb = Gpu(() => { x.BodySkin.Creator.Rebuild(); x.BodyDetail.Creator.Rebuild(); }), gf = Gpu(() => x.FaceSkin.Creator.Rebuild());
                Info($"UB2 creators alone (GPU incl. mips, 4.1 UF1 method): body colour {F(gc)} ms, body detail {F(gd)} ms, both {F(gb)} ms, face colour {F(gf)} ms (4.1 UF1: 4.95 / 4.68 / 12.8 / 1.50)");
                // (family, path, value of frame i)
                var fams = new (string name, string path, Func<int, object> v)[]
                {
                    ("skin colour (body + face colour creators)", "chara.body.skin.color", i => new Color4(0.7f + 0.002f * i, 0.6f, 0.55f, 1)),
                    ("paint colour (body colour creator)", "chara.body.paint[0].color", i => new Color4(0.5f, 0.2f + 0.003f * i, 0.3f, 1)),
                    ("paint gloss (body detail creator)", "chara.body.paint[0].gloss", i => 0.4f + 0.003f * i),
                    ("paint layout (both body creators)", "chara.body.paint[0].layout", i => new Vec4(0.5f + 0.002f * i, 0.5f, 0.5f, 0.5f)),
                };
                foreach (var (name, path, v) in fams)
                    for (int layers = 0; layers <= 1; layers++)
                    {
                        for (int i = 0; i < 2; i++)
                        {
                            body[i].Creator.Under.Clear(); body[i].Creator.Over.Clear();
                            body[i].Creator.Under.AddRange(keep[i].Item1); body[i].Creator.Over.AddRange(keep[i].Item2);
                            for (int k = 0; k < layers; k++) body[i].Creator.Over.Add(new SkinLayer { TexType = 1, Texture = layer, Source = "ub2" });
                        }
                        var member = path.Substring("chara.".Length);
                        var frame = new List<double>(); var cpu = new List<double>();
                        int w = 0;
                        for (int i = -5; i < reps; i++)   // 5 warm-up frames
                        {
                            var doc = ctx.Chara;
                            SetMember(doc, member, v(i + 5));
                            Fence(x.BodySkin.Creator.Target);
                            ed.MarkDirty(path);
                            w = ed.PendingBodyWeight();
                            var t = Stopwatch.StartNew();
                            ed.Flush();
                            double c = t.Elapsed.TotalMilliseconds;
                            Fence(x.BodySkin.Creator.Target);
                            if (i < 0) continue;
                            cpu.Add(c);
                            frame.Add(t.Elapsed.TotalMilliseconds);
                        }
                        bool throttledAuto = CharaEditFlush.ThrottlesWeight(BodyThrottleMode.Auto, w), throttledAlways = CharaEditFlush.ThrottlesWeight(BodyThrottleMode.Always, w);
                        double perFrameAuto = throttledAuto ? Median(frame) / 2 : Median(frame), perFrameAlways = throttledAlways ? Median(frame) / 2 : Median(frame);
                        Info($"UB2 {name}, {layers} KSOX layer(s): W {w}; frame flush CPU {F(Median(cpu))} ms, incl. GPU fences {F(Median(frame))} ms; auto {(throttledAuto ? "throttled (every other frame)" : "every frame")} → {F(perFrameAuto)} ms per frame on average; always → {F(perFrameAlways)} ms");
                        if (layers == 0 && name.StartsWith("skin colour", StringComparison.Ordinal)) Target("UB2 skin colour drag frame (body + face colour creators, not throttled), GPU incl.", Median(frame), 8);
                        if (layers == 0 && name.StartsWith("paint layout", StringComparison.Ordinal)) Target("UB2 paint layout drag frame (both creators, throttled under auto): effective per frame", perFrameAuto, 8);
                    }
                for (int i = 0; i < 2; i++) { body[i].Creator.Under.Clear(); body[i].Creator.Over.Clear(); body[i].Creator.Under.AddRange(keep[i].Item1); body[i].Creator.Over.AddRange(keep[i].Item2); }
                ed.MarkUnit(UnitMap.SkinBody); ed.Flush();
                // swatch push and region pick (op + flush, GPU fenced)
                var sp = new List<double>(); var rp = new List<double>();
                for (int i = 0; i < reps; i++)
                {
                    Fence(x.BodySkin.Creator.Target);
                    var t = Stopwatch.StartNew();
                    n.Ops.ApplyColourPreset(ids, "appearance.chara.body.skin.color", "illusion.hs2#" + (i % 7), User); n.Apply();
                    Fence(x.BodySkin.Creator.Target);
                    sp.Add(t.Elapsed.TotalMilliseconds);
                    Fence(x.BodySkin.Creator.Target);
                    t = Stopwatch.StartNew();
                    n.Ops.ChangeItem(ids, "appearance.chara.body.paint[1].layoutItem", "illusion.hs2#" + (i % 4), User); n.Apply();
                    Fence(x.BodySkin.Creator.Target);
                    rp.Add(t.Elapsed.TotalMilliseconds);
                }
                Target("UB2 swatch push (op + flush, body + face colour creators, GPU incl.)", Median(sp), 12);
                Target("UB2 region pick (op + flush, both body creators, GPU incl.)", Median(rp), 20);
            }
        }
        finally { Object.DestroyImmediate(layer); h.ReleaseBundles(); }
    }

    /// <summary>A document member by its dotted path (the families of UB2).</summary>
    static void SetMember(CharaDocument d, string member, object v)
    {
        var b = d.Body;
        switch (member)
        {
            case "body.skin.color": b.Skin.Color = (Color4)v; break;
            case "body.paint[0].color": b.Paint[0].Color = (Color4)v; break;
            case "body.paint[0].gloss": b.Paint[0].Gloss = (float)v; break;
            case "body.paint[0].layout": b.Paint[0].Layout = (Vec4)v; break;
            default: throw new ArgumentException(member);
        }
    }

    static void UB7(MakerCoreTest.Harness h)
    {
        using (var n = NodeOf(h, FemaleCard))
        {
            var ctx = n.Real.Context;
            long meshBytes = ctx.OwnedMeshes.Sum(m => m ? UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m) : 0);
            long rtBytes = new[] { ctx.BodySkin, ctx.BodyDetail, ctx.FaceSkin, ctx.FaceDetail }.Where(c => c?.Target).Sum(c => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(c.Target));
            Info($"UB7 BR-Chan: owned meshes {ctx.OwnedMeshes.Count} ({meshBytes} B = {meshBytes / 1048576.0:F2} MiB; the breast-normal copy of o_body_cf); skin RTs {rtBytes / 1048576.0:F1} MiB; owned materials {ctx.OwnedMaterials.Count}");
            Check("UB7: one owned mesh per applicable entry (vanilla body: 1)", ctx.OwnedMeshes.Count == ctx.BustNormals.Entries.Count(e => e.Applicable) && ctx.OwnedMeshes.Count == 1);
        }
        using (var n = NodeOf(h, MaleCard))
            Check("UB7: male: no owned mesh", n.Real.Context.OwnedMeshes.Count == 0);
        h.ReleaseBundles();
    }

    // ================================================================ PlayTiming (play mode; RunTiming -play, runner -NoQuit)

    static string PlayReport => Path.Combine(OutDir, "maker_body_timing_play_report.txt");

    /// <summary>
    /// UB2 in play mode: drags through the scene's flush point with real frames (CharaEditFlush.Run judges the C5 throttle
    /// by Time.frameCount): 60 frames per family (skin colour, paint colour, paint layout) × 0 / 1 KSOX layer × mode (auto,
    /// always); per run the frames the throttle made wait, the body composites, the throttled frames per second and the
    /// average frame time (edit + flush + GPU fence; no camera draws in the empty play scene).
    /// </summary>
    static System.Collections.IEnumerator PlayTiming()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"maker body timing, play mode (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName})");
        var mode0 = CharaEditFlush.BodyThrottle;
        MakerCoreTest.Harness h = null; MakerEditTest.Node n = null; Texture2D layer = null;
        try
        {
            h = MakerCoreTest.Harness.Open();
            n = NodeOf(h, FemaleCard);
            layer = Noise(2048, 2048, 7);
            Check("play: in play mode, BR-Chan built", Application.isPlaying && n.Ok, n.Error);
        }
        catch (Exception e) { Check("play: setup", false, e.ToString()); }
        if (n != null && n.Ok)
        {
            var ids = new[] { n.Id };
            var x = n.Real.Context.Edit;
            var body = new[] { x.BodySkin, x.BodyDetail };
            var keep = body.Select(c => (c.Creator.Under.ToList(), c.Creator.Over.ToList())).ToList();
            var fams = new (string name, string path, Func<int, object> v, int w0)[]
            {
                ("skin colour", "appearance.chara.body.skin.color", i => new Color4(0.7f + 0.002f * i, 0.6f, 0.55f, 1), 1),
                ("paint colour", "appearance.chara.body.paint[0].color", i => new Color4(0.5f, 0.2f + 0.003f * i, 0.3f, 1), 1),
                ("paint layout", "appearance.chara.body.paint[0].layout", i => new Vec4(0.5f + 0.002f * i, 0.5f, 0.5f, 0.5f), 2),
            };
            const int Frames = 60;
            foreach (var mode in new[] { BodyThrottleMode.Auto, BodyThrottleMode.Always })
                foreach (var (name, path, v, w0) in fams)
                    for (int layers = 0; layers <= 1; layers++)
                    {
                        CharaEditFlush.BodyThrottle = mode;
                        for (int i = 0; i < 2; i++)
                        {
                            body[i].Creator.Under.Clear(); body[i].Creator.Over.Clear();
                            body[i].Creator.Under.AddRange(keep[i].Item1); body[i].Creator.Over.AddRange(keep[i].Item2);
                            for (int k = 0; k < layers; k++) body[i].Creator.Over.Add(new SkinLayer { TexType = 1, Texture = layer, Source = "ub2" });
                        }
                        var real = n.Real;
                        int t0 = real.EditThrottled, f0 = real.EditFlushes, composites = 0;
                        var tx = n.Session.Commands.BeginOrJoin("Drag", new ExecuteOptions { Source = CommandSource.User, Label = "Drag" });
                        var frame = new List<double>();
                        var all = Stopwatch.StartNew();
                        for (int i = 0; i < 5; i++) { n.Ops.EditAppearance(ids, new List<(string, object)> { (path, v(Frames + i)) }, User); n.Apply(); yield return null; }   // warm-up
                        t0 = real.EditThrottled; f0 = real.EditFlushes;
                        all.Restart();
                        for (int i = 0; i < Frames; i++)
                        {
                            var t = Stopwatch.StartNew();
                            int fb = real.EditFlushes;
                            n.Ops.EditAppearance(ids, new List<(string, object)> { (path, v(i)) }, User);
                            n.Apply();
                            var lf = real.LastEditFlush;
                            if (lf != null && real.EditFlushes > fb && lf.Units.Contains(UnitMap.SkinBody)) composites++;
                            Fence(x.BodySkin.Creator.Target);
                            frame.Add(t.Elapsed.TotalMilliseconds);
                            yield return null;
                        }
                        double secs = all.Elapsed.TotalSeconds;
                        tx.Commit(); tx.Dispose();
                        n.Apply();
                        // the op applies the scene itself and the test applies again: CharaEditFlush.Run runs twice per frame, so
                        // EditThrottled counts both deferrals of a waiting frame; the frames that waited = frames without a composite
                        int deferrals = real.EditThrottled - t0, waited = Frames - composites;
                        double avg = frame.Average();
                        int w = w0 + (w0 == 1 ? layers : 2 * layers);
                        Info($"play {CharaEditFlush.BodyThrottleName(mode)} {name}, {layers} layer(s): W {w}; {Frames} frames in {secs:F2} s, body composites {composites}, frames that waited {waited} ({waited / secs:F1} per s; flush-point deferrals {deferrals}); frame (edit + flush + fence) avg {F(avg)} ms, median {F(Median(frame))} ms");
                        bool expect = CharaEditFlush.ThrottlesWeight(mode, w);
                        Check($"play {CharaEditFlush.BodyThrottleName(mode)} {name}, {layers} layer(s): {(expect ? "every other frame composites (the rest wait)" : "every frame composites")}",
                              expect ? composites >= Frames / 2 - 1 && composites <= Frames / 2 + 1 && deferrals > 0 : composites == Frames && deferrals == 0, $"composites {composites} of {Frames}, deferrals {deferrals}");
                        if (mode == BodyThrottleMode.Auto && layers == 0 && name == "paint layout") Target("UB2 play: paint layout drag (throttled), average frame", avg, 8);
                        if (mode == BodyThrottleMode.Auto && layers == 0 && name == "skin colour") Target("UB2 play: skin colour drag (every frame), median frame", Median(frame), 8);
                    }
            for (int i = 0; i < 2; i++) { body[i].Creator.Under.Clear(); body[i].Creator.Over.Clear(); body[i].Creator.Under.AddRange(keep[i].Item1); body[i].Creator.Over.AddRange(keep[i].Item2); }
        }
        CharaEditFlush.BodyThrottle = mode0;
        try { n?.Dispose(); h?.Dispose(); if (layer) Object.DestroyImmediate(layer); } catch (Exception e) { Info("dispose: " + e.Message); }
        rep.Insert(0, $"SUMMARY: {passes} PASS, {fails} FAIL{(warns > 0 ? ", " + warns + " WARN" : "")} ({sw.Elapsed.TotalSeconds:F0} s)\nMakerBodyTest.timing play\n");
        File.WriteAllText(PlayReport, rep.ToString(), new UTF8Encoding(false));
        Debug.Log($"MAKER BODY PLAY TIMING: {passes} PASS, {fails} FAIL, {warns} WARN");
        PlayModeHarness.ExitCode = fails > 0 || playEditFails > 0 ? 1 : 0;
    }

    // ================================================================ RunMultiPlay (W-1: several characters' body composites)

    static string MultiReport => Path.Combine(OutDir, "maker_body_multi_report.txt");

    /// <summary>
    /// W-1 harness (play mode, graphics; runner -NoQuit): a scene of BR-Chan, HERO107 and a second BR-Chan (-chars 1..3 takes the
    /// first n), the app's URP pipeline and a camera drawing them into an off-screen RT every frame (-nocamera: none). Phase 1:
    /// one frame each with 1, 2, 3 characters rebuilding both body creators (a body.paint[0].layout set on n characters in
    /// one op): GPU time of the frame (1×1 fence) and memory. Phase 2: -iters writes of body.paint[0].layout on every
    /// female in one op (as W's component-row write), an undo ×3 every 5 writes; per frame the RT count / bytes, temporary
    /// RTs, Profiler.GetAllocatedMemoryForGraphicsDriver and the per-character GPU bytes. Lines are appended to the report and
    /// logged as they are measured (a device loss keeps the numbers before it). -off42: the 4.2 breast-normal unit off (no
    /// mesh copies) for the 4.1 comparison. Numbers only; the camera RT is never read or saved.
    /// </summary>
    public static void RunMultiPlay()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(MultiReport, "SUMMARY: 0 PASS, 1 FAIL\nMakerBodyTest.multi: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(MakerBodyTest) + "." + nameof(MultiPlay), MultiReport, 25);
    }

    static void MLine(string s)
    {
        try { File.AppendAllText(MultiReport, s + "\n", new UTF8Encoding(false)); } catch (Exception) { }
        Debug.Log("MULTI " + s);
    }

    /// <summary>GPU bytes a character holds: the render textures its renderers' materials and its creators reference (unique)
    /// and its owned meshes.</summary>
    static (int rts, long rtBytes, long meshBytes) CharaGpu(CharaContext ctx)
    {
        var set = new HashSet<RenderTexture>();
        if (ctx?.Root)
            foreach (var r in ctx.Root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (!m) continue;
                    foreach (var id in m.GetTexturePropertyNameIDs()) if (m.GetTexture(id) is RenderTexture rt && rt) set.Add(rt);
                }
        if (ctx?.Edit != null) foreach (var c in ctx.Edit.Composites()) if (c.Creator?.Target) set.Add(c.Creator.Target);
        long b = set.Sum(t => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t));
        long mb = ctx == null ? 0 : ctx.OwnedMeshes.Where(m => m).Sum(m => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m));
        return (set.Count, b, mb);
    }

    static string GlobalGpu()
    {
        int n = 0, temp = 0; long bytes = 0;
        foreach (var rt in Resources.FindObjectsOfTypeAll<RenderTexture>())
        {
            if (!rt || !rt.IsCreated()) continue;
            n++; bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(rt);
            if (rt.name != null && rt.name.StartsWith("TempBuffer", StringComparison.Ordinal)) temp++;
        }
        long drv = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();
        return $"RTs {n} ({bytes / 1048576.0:F1} MiB, temporary {temp}), graphics driver {drv / 1048576.0:F1} MiB, meshes {Resources.FindObjectsOfTypeAll<Mesh>().Length}";
    }

    static System.Collections.IEnumerator MultiPlay()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        var args = TestArgs.Get();
        int chars = int.Parse(Arg("-chars", "3"), CultureInfo.InvariantCulture);
        int iters = int.Parse(Arg("-iters", "60"), CultureInfo.InvariantCulture);
        bool camera = !args.Contains("-nocamera"), off42 = args.Contains("-off42");
        bool keepUnit = BustNormalsUnit.Enabled;
        if (off42) BustNormalsUnit.Enabled = false;
        File.WriteAllText(MultiReport, "", new UTF8Encoding(false));
        MLine($"maker body multi (Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsMemorySize} MB): chars {chars}, iters {iters}, camera {camera}, 4.2 breast normals {(off42 ? "OFF" : "on")}");
        HS2Next.App.AppPipeline pipe = null;
        MakerCoreTest.Harness h = null; SceneSession session = null; SceneRuntime runtime = null; StudioOps ops = null;
        var ids = new List<string>(); var females = new List<string>();
        GameObject camGo = null; RenderTexture camRt = null;
        try
        {
            pipe = new HS2Next.App.AppPipeline(); pipe.Apply();
            MLine("pipeline: " + pipe.Summary);
            h = MakerCoreTest.Harness.Open();
            session = SceneRuntimeTest.NewSession(h.Services);
            runtime = new SceneRuntime(session, h.Services, null, false);
            ops = new StudioOps(runtime);
            MLine("before the characters: " + GlobalGpu());
            var files = new[] { FemaleCard, MaleCard }.Concat(Enumerable.Repeat(FemaleCard, 6)).Take(Math.Max(1, Math.Min(8, chars))).ToList();
            foreach (var file in files)
            {
                var c = MakerEditTest.Import(h, file, out string err);
                if (c == null) { Check("import " + Rel(file), false, err); continue; }
                var before = session.Model.PreOrder().Where(x => x.Kind == NodeKind.Chara).Select(x => x.Id).ToList();
                session.AddCharaFromContainer(c);
                runtime.Apply();
                string id = session.Model.PreOrder().Where(x => x.Kind == NodeKind.Chara).Select(x => x.Id).FirstOrDefault(x => !before.Contains(x));
                var real = id != null ? runtime.FindChara(id) : null;
                Check($"built {Rel(file)}", real != null && real.State == RealizationState.Built, real?.State.ToString());
                if (real == null) continue;
                ids.Add(id);
                if (!real.Context.IsMale) females.Add(id);
                var g = CharaGpu(real.Context);
                MLine($"built {Rel(file)}: RTs {g.rts} ({g.rtBytes / 1048576.0:F1} MiB), owned meshes {real.Context.OwnedMeshes.Count} ({g.meshBytes / 1048576.0:F2} MiB); global {GlobalGpu()}");
            }
            if (camera)
            {
                camRt = new RenderTexture(1280, 720, 24) { name = "MakerBodyTest multi camera" };
                camGo = new GameObject("MakerBodyTest multi camera");
                var cam = camGo.AddComponent<Camera>();
                cam.targetTexture = camRt;
                cam.transform.position = new Vector3(0, 1.0f, 3.2f);
                cam.transform.LookAt(new Vector3(0, 0.9f, 0));
                cam.nearClipPlane = 0.05f; cam.farClipPlane = 50;
                foreach (var id in ids) foreach (var smr in runtime.FindChara(id).Root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
            }
        }
        catch (Exception e) { Check("setup", false, e.ToString()); }
        if (ops != null && ids.Count > 0)
        {
            var bus = session.Commands;
            for (int i = 0; i < 10; i++) { runtime.Apply(); yield return null; }
            var anyReal = runtime.FindChara(ids[0]);
            var fenceRt = anyReal.Context.Edit.BodySkin.Creator.Target;
            // ---- phase 1: 1, 2, 3 characters rebuilding both body creators in one frame
            for (int n = 1; n <= ids.Count; n++)
            {
                var l = new List<double>();
                for (int k = 0; k < 8; k++)
                {
                    Fence(fenceRt);
                    var t = Stopwatch.StartNew();
                    ops.EditAppearance(ids.Take(n), new List<(string, object)> { ("appearance.chara.body.paint[0].layout", new Vec4(0.3f + 0.02f * k, 0.5f, 0.5f, 0.5f)) }, User);
                    runtime.Apply();
                    Fence(fenceRt);
                    l.Add(t.Elapsed.TotalMilliseconds);
                    yield return null;
                }
                MLine($"phase 1: {n} character(s) rebuilding both body creators in one frame (op + flush + GPU fence): median {F(Median(l))} ms, max {F(l.Max())} ms; {GlobalGpu()}");
            }
            // ---- phase 2: W's component-row writes on every female in one op, undo ×3 every 5 writes
            var rnd = new System.Random(42);
            int writes = 0, undos = 0;
            var frameMs = new List<double>();
            string first = null;
            for (int it = 0; it < iters; it++)
            {
                var t = Stopwatch.StartNew();
                var targets = females.Count > 0 ? females : ids;
                var res = ops.EditAppearance(targets, new List<(string, object)> { ("appearance.chara.body.paint[0].layout", new Vec4((float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble())) }, User);
                runtime.Apply();
                writes++;
                yield return null;
                if (it % 5 == 4)
                    for (int u = 0; u < 3 && bus.CanUndo; u++) { bus.Undo(); runtime.Apply(); undos++; yield return null; }
                Fence(fenceRt);
                frameMs.Add(t.Elapsed.TotalMilliseconds);
                if (it % 10 == 0 || it == iters - 1)
                {
                    string per = string.Join("; ", ids.Select(id => { var r = runtime.FindChara(id); var g = CharaGpu(r?.Context); return $"{(r != null && r.Context.IsMale ? "M" : "F")} RTs {g.rts} {g.rtBytes / 1048576.0:F1} MiB mesh {g.meshBytes / 1048576.0:F2}"; }));
                    string glob = GlobalGpu();
                    if (first == null) first = glob;
                    MLine($"phase 2 it {it}: op {res?.Status}, writes {writes}, undos {undos}, last frame block {F(frameMs.Last())} ms; {glob}; per character: {per}");
                }
            }
            MLine($"phase 2 done: {writes} writes, {undos} undos, frame block median {F(Median(frameMs))} ms, max {F(frameMs.Max())} ms; first {first}; last {GlobalGpu()}");
            // -cycles N (as RunMultiApp's): a further BR-Chan added and built, a paint-layout write on it and the first female
            // in one op, undo ×2 — a fresh 4096² body right before the multi-character composites, without the UI
            int cycles = int.Parse(Arg("-cycles", "0"), CultureInfo.InvariantCulture);
            var fc = cycles > 0 ? MakerEditTest.Import(h, FemaleCard, out _) : null;
            for (int cy = 0; cy < cycles && fc != null; cy++)
            {
                var t = Stopwatch.StartNew();
                var before = session.Model.PreOrder().Where(x => x.Kind == NodeKind.Chara).Select(x => x.Id).ToList();
                session.AddCharaFromContainer(fc);
                runtime.Apply();
                string nid = session.Model.PreOrder().Where(x => x.Kind == NodeKind.Chara).Select(x => x.Id).FirstOrDefault(x => !before.Contains(x));
                double build = t.Elapsed.TotalMilliseconds;
                yield return null;
                var res = ops.EditAppearance(new[] { (females.Count > 0 ? females[0] : ids[0]), nid }, new List<(string, object)> { ("appearance.chara.body.paint[0].layout", new Vec4(0.25f, (float)rnd.NextDouble(), 0.5f, 0.5f)) }, User);
                runtime.Apply();
                yield return null;
                bus.Undo(); runtime.Apply(); yield return null;
                bus.Undo(); runtime.Apply(); yield return null;
                Fence(fenceRt);
                MLine($"cycle {cy}: build {F(build)} ms, write {res?.Status}, cycle {F(t.Elapsed.TotalMilliseconds)} ms; {GlobalGpu()}");
            }
            if (cycles > 0) Check($"W-1 harness: {cycles} cycles (a fresh body built, the two-character write, undo ×2) without a device loss", true);
            Check("W-1 harness: the multi-character body writes ran without a device loss", true, $"{writes} writes, {undos} undos");
        }
        try
        {
            if (camGo) Object.DestroyImmediate(camGo);
            if (camRt) { camRt.Release(); Object.DestroyImmediate(camRt); }
            runtime?.Dispose(); h?.Dispose(); pipe?.Restore();
        }
        catch (Exception e) { MLine("dispose: " + e.Message); }
        BustNormalsUnit.Enabled = keepUnit;
        string summary = $"SUMMARY: {passes} PASS, {fails} FAIL ({sw.Elapsed.TotalSeconds:F0} s)";
        var text = File.Exists(MultiReport) ? File.ReadAllText(MultiReport) : "";
        File.WriteAllText(MultiReport, summary + "\nMakerBodyTest.multi (W-1)\n" + text + rep, new UTF8Encoding(false));
        Debug.Log("MAKER BODY MULTI: " + summary);
        PlayModeHarness.ExitCode = fails > 0 ? 1 : 0;
    }

    /// <summary>
    /// W-1 harness inside the app (play mode with the UI, as UiPanelsTest; runner -NoQuit; bridge port 17421, A's): BR-Chan,
    /// HERO107 and a second BR-Chan added through the app's ops, both females selected with the Edit tab on Body › Paint (the
    /// part lists draw their thumbnails), then -iters writes of body.paint[0].layout on both females in one op (W's
    /// component-row write), undo ×3 every 5 writes; memory and frame times logged as in RunMultiPlay. -off42 as there.
    /// </summary>
    public static void RunMultiApp()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(MultiReport, "SUMMARY: 0 PASS, 1 FAIL\nMakerBodyTest.multi app: started, no result yet\n", new UTF8Encoding(false));
        const string launch = "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},\"userData\":\"Spike/out/maker/multi_userdata\",\"bridge\":{\"port\":17421,\"outputDir\":\"Spike/out/maker/multi_out\",\"allowQuit\":false}}";
        PlayModeHarness.Start(nameof(MakerBodyTest) + "." + nameof(MultiApp), MultiReport, 25, true, launch);
    }

    static System.Collections.IEnumerator MultiApp()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        var args = TestArgs.Get();
        int iters = int.Parse(Arg("-iters", "60"), CultureInfo.InvariantCulture);
        bool off42 = args.Contains("-off42");
        bool keepUnit = BustNormalsUnit.Enabled;
        if (off42) BustNormalsUnit.Enabled = false;
        File.WriteAllText(MultiReport, "", new UTF8Encoding(false));
        MLine($"maker body multi in the app (Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsMemorySize} MB): iters {iters}, 4.2 breast normals {(off42 ? "OFF" : "on")}, args {string.Join(" ", args.SkipWhile(a => a != "-iters"))}");
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => HS2Next.App.AppCore.Current != null && HS2Next.App.AppCore.Current.Stage != HS2Next.App.AppStage.Starting, 240, ok);
        var app = HS2Next.App.AppCore.Current;
        yield return PlayModeHarness.WaitUntil(() => HS2Next.UI.UiHost.Current != null && HS2Next.UI.UiHost.Current.IsReady, 30, ok);
        var ui = HS2Next.UI.UiHost.Current?.Context;
        if (!Check("app and UI ready", app != null && app.Stage == HS2Next.App.AppStage.Ready && ui != null, app?.Stage.ToString()))
        {
            BustNormalsUnit.Enabled = keepUnit;
            PlayModeHarness.ExitCode = 1;
            yield break;
        }
        MLine("before the characters: " + GlobalGpu());
        var ids = new List<string>(); var females = new List<string>();
        foreach (var card in new[] { FemaleCard, MaleCard, FemaleCard })
        {
            var r = ui.Ops.AddChara(card, null, null, -1, null, User);
            string id = r.NewIds.FirstOrDefault();
            yield return PlayModeHarness.WaitUntil(() => id != null && app.Runtime.FindChara(id)?.State == RealizationState.Built, 120, ok);
            var real = id != null ? app.Runtime.FindChara(id) : null;
            if (!Check("added and built " + Rel(card), ok[0] && real != null, r.Message)) continue;
            ids.Add(id);
            if (!real.Context.IsMale) females.Add(id);
            var g = CharaGpu(real.Context);
            MLine($"built {Rel(card)}: RTs {g.rts} ({g.rtBytes / 1048576.0:F1} MiB), owned meshes {real.Context.OwnedMeshes.Count}; global {GlobalGpu()}");
            yield return PlayModeHarness.WaitFrames(5);
        }
        bool noEdit = args.Contains("-noedit"), keepSel = args.Contains("-keepsel");
        if (args.Contains("-nodiskwrite"))
        {
            // the thumbnail loader's disk-cache writes (a GPU read-back of each loaded thumbnail) off
            var pt = HS2Next.UI.Panels.Maker.PartThumbnails.Shared(app.Runtime.Services.Catalog);
            pt.WriteDisk = false;
            MLine("thumbnail disk writes off (PartThumbnails.WriteDisk = false)");
        }
        if (females.Count == 2)
        {
            ui.Ops.Select(females);
            if (!noEdit) ui.Shell?.FocusPanel("inspector");
            yield return PlayModeHarness.WaitFrames(3);
            var iroot = noEdit ? null : ui.Shell?.PanelRoot("inspector");
            var insp = iroot as HS2Next.UI.Panels.InspectorPanel ?? (iroot != null ? UnityEngine.UIElements.UQueryExtensions.Q<HS2Next.UI.Panels.InspectorPanel>(iroot) : null);
            insp?.Refresh(true);
            insp?.Chara?.ShowTab(HS2Next.UI.Panels.CharaEditTab.TabId);
            var tab = insp?.Chara?.TabBodies[HS2Next.UI.Panels.CharaEditTab.TabId] as HS2Next.UI.Panels.CharaEditTab;
            string node = Arg("-node", "body"), page = Arg("-page", "paint");
            tab?.Reveal(node, page);
            yield return PlayModeHarness.WaitFrames(10);
            MLine($"Edit tab on {node} › {page}: {(tab != null ? "shown" : "not found")}; {GlobalGpu()}");
            var bus = app.Session.Commands;
            var rnd = new System.Random(42);
            int writes = 0, undos = 0;
            var frame = new List<double>();
            var fenceRt = app.Runtime.FindChara(females[0]).Context.Edit.BodySkin.Creator.Target;
            for (int it = 0; it < iters; it++)
            {
                var t = Stopwatch.StartNew();
                var res = ui.Ops.EditAppearance(females, new List<(string, object)> { ("appearance.chara.body.paint[0].layout", new Vec4((float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble())) }, User);
                writes++;
                yield return PlayModeHarness.WaitFrames(2);
                if (it % 5 == 4)
                    for (int u = 0; u < 3 && bus.CanUndo; u++) { bus.Undo(); undos++; yield return PlayModeHarness.WaitFrames(1); }
                Fence(fenceRt);
                frame.Add(t.Elapsed.TotalMilliseconds);
                if (it % 10 == 0 || it == iters - 1)
                {
                    string per = string.Join("; ", ids.Select(id => { var rr = app.Runtime.FindChara(id); var g = CharaGpu(rr?.Context); return $"{(rr != null && rr.Context.IsMale ? "M" : "F")} RTs {g.rts} {g.rtBytes / 1048576.0:F1} MiB"; }));
                    MLine($"it {it}: op {res?.Status}, writes {writes}, undos {undos}, last block {F(frame.Last())} ms; {GlobalGpu()}; per character: {per}");
                }
            }
            MLine($"done: {writes} writes, {undos} undos, block median {F(Median(frame))} ms, max {F(frame.Max())} ms; {GlobalGpu()}");
            Check("W-1 app harness: the two-female writes with three bodies ran without a device loss", true, $"{writes} writes, {undos} undos");

            // -cycles N: W's sequence around a fresh body N times — a further BR-Chan added and built, a paint-layout write on
            // it and the first female in one op, undo ×2 (the write and the add), so every cycle builds a 4096² body (and,
            // with the 4.2 unit, copies its mesh) right before the multi-character composites
            int cycles = int.Parse(Arg("-cycles", "0"), CultureInfo.InvariantCulture);
            for (int cy = 0; cy < cycles; cy++)
            {
                var t = Stopwatch.StartNew();
                var r = ui.Ops.AddChara(FemaleCard, null, null, -1, null, User);
                string id = r.NewIds.FirstOrDefault();
                if (keepSel) ui.Ops.Select(females);   // the inspector keeps the two females (no Edit tab for the new body)
                yield return PlayModeHarness.WaitUntil(() => id != null && app.Runtime.FindChara(id)?.State == RealizationState.Built, 120, ok);
                if (!ok[0]) { Check($"cycle {cy}: built", false, r.Message); break; }
                double build = t.Elapsed.TotalMilliseconds;
                var res = ui.Ops.EditAppearance(new[] { females[0], id }, new List<(string, object)> { ("appearance.chara.body.paint[0].layout", new Vec4(0.25f, (float)rnd.NextDouble(), 0.5f, 0.5f)) }, User);
                yield return PlayModeHarness.WaitFrames(2);
                bus.Undo(); yield return PlayModeHarness.WaitFrames(1);
                bus.Undo(); yield return PlayModeHarness.WaitFrames(3);
                Fence(fenceRt);
                MLine($"cycle {cy}: build {F(build)} ms, write {res?.Status}, cycle {F(t.Elapsed.TotalMilliseconds)} ms; {GlobalGpu()}");
            }
            if (cycles > 0) Check($"W-1 app harness: {cycles} cycles (a fresh body built, then the two-character write and undo ×2) without a device loss", true);
        }
        BustNormalsUnit.Enabled = keepUnit;
        string summary = $"SUMMARY: {passes} PASS, {fails} FAIL ({sw.Elapsed.TotalSeconds:F0} s)";
        var text = File.Exists(MultiReport) ? File.ReadAllText(MultiReport) : "";
        File.WriteAllText(MultiReport, summary + "\nMakerBodyTest.multi app (W-1)\n" + text + rep, new UTF8Encoding(false));
        Debug.Log("MAKER BODY MULTI APP: " + summary);
        PlayModeHarness.ExitCode = fails > 0 ? 1 : 0;
    }
}
