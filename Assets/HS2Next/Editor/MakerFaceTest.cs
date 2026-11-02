// Maker face tests of Phase 4.1 package A (.render, .timing).
//   Run (headless):
//     FT1 per family (27 face families + the stored-only paint layoutItem + 10 body families of the shared helper,
//         item 3): the creators one flush rebuilds (FlushReport.CreatorsRebuilt, CompositeInputs.Rebuilds) = the table;
//         after the families the character equals a fresh build of its documents
//     FT2 head re-interpretation on BR-Chan with non-default face shapes, through the scene: heads 0 → 2 → 3 → a W10 mod head
//         → the mod head with the most skins → 1 (ChangeItem): face.shape unchanged bit for bit, the skin snapped by the H3
//         rule (MakerLists.SkinAfterHeadChange), the snapshot = a fresh node of the edited documents
//     FT3 the preview / commit seam (CharaEditFlush.ForcePreview): 20 preview sets of a face shape in one transaction →
//         Deferred = {anim.refresh, dynbone}; commit → the deferred units run, Deferred empty, snapshot = a fresh node; the
//         same for a body shape (+ bust); a cancelled drag = the snapshot before it
//     FT4 face preset pushes (H4's heads; a head with a Preset column and no asset → the head change only, F5)
//     FT5 linked-eye edits, eye copies and resets through the realization: snapshots = fresh nodes
//     FT6 layout pushes (mole row 4, paint rows 0 / 5): snapshots = fresh nodes
//     FT7 heads with their own compositing materials (after 1.0.0): the element's rules on synthetic input; BR-Chan →
//         the first installed redirected female head → another redirected head → her own head: the passes' bundle, paint mask
//         and eyeshadow / cheek / lip layouts = the head's bundle, both passes rebuilt on each change, snapshots = fresh nodes
//   RunRender (graphics): the RT read-back hashes after FT1's selective rebuilds = after a rebuild of every creator (BR-Chan,
//     HERO107); the face gloss state; UF7 (W10: the drawn o_head slot 0 of a card using such a head); RT statistics.
//   RunTiming (graphics): targets (WARN above the target, FAIL above 2× without a recorded reason) and UF1, UF2, UF5, UF6.
// Run: -executeMethod MakerFaceTest.Run [-only FT1,FT3]; MakerFaceTest.RunRender; MakerFaceTest.RunTiming [-reps N].
// Reports: Spike/out/maker/maker_face_report.txt, maker_face_render_report.txt, maker_face_timing_report.txt; first line
// "SUMMARY: N PASS, M FAIL"; EditorApplication.Exit(1) on any FAIL. Numbers only (nothing is rendered or viewed); SFW
// visual targets; the W10 card of UF7 is read for shader names and numbers only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Chara;
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
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class MakerFaceTest
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
        Debug.Log($"MAKER FACE TEST DONE ({file}): {passes} PASS, {fails} FAIL, {warns} WARN");
        if (fails > 0 && !inSuite) TestExit.Exit(1);
        suiteFails += fails;
    }

    static bool inSuite;
    static int suiteFails;

    /// <summary>Runs several test methods in one Unity session (fewer runner sessions while other packages wait for the
    /// mutex): -suite "MakerFaceTest.Run,MakerUnitTest.RunU,..." (static, parameterless; methods of other classes end Unity on
    /// their own failure). Exit code 1 when any MakerFaceTest method failed.</summary>
    public static void RunSuite()
    {
        inSuite = true; suiteFails = 0;
        foreach (var name in (Arg("-suite") ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
        {
            int dot = name.LastIndexOf('.');
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name.Substring(0, dot))).FirstOrDefault(t => t != null);
            var m = type?.GetMethod(name.Substring(dot + 1), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, Type.EmptyTypes, null);
            Debug.Log("MakerFaceTest.RunSuite: " + name + (m == null ? " (not found)" : ""));
            if (m == null) { suiteFails++; continue; }
            try { m.Invoke(null, null); }
            catch (Exception e) { suiteFails++; Debug.LogError("MakerFaceTest.RunSuite: " + name + " threw " + (e.InnerException ?? e)); }
        }
        inSuite = false;
        TestExit.Exit(suiteFails > 0 ? 1 : 0);
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
    static string Rel(string f) => Path.GetFileName(f);
    static string Short(string s, int n = 400) => s == null ? "" : s.Length > n ? s.Substring(0, n) + "…" : s;
    static readonly ExecuteOptions User = new ExecuteOptions { Source = CommandSource.User };

    const string FaceC = "create_skin_face", FaceD = "create_skin detail_face", BodyC = "create_skin_body", BodyD = "create_skin detail_body";

    // ================================================================ families (FT1, render)

    sealed class Family
    {
        public string Name, Path;
        public Func<object> Get;
        public Action<object> Set;
        public Func<object, object> Alt;
        /// <summary>Creator names the flush must rebuild (exactly).</summary>
        public string[] Expect;
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

    static List<Family> Families(CharaDocument ch, ContentCatalog cat, bool male)
    {
        var l = new List<Family>();
        var sex = male ? CharaSex.Male : CharaSex.Female;
        void Add(string name, string path, Func<object> get, Action<object> set, Func<object, object> alt, params string[] expect) =>
            l.Add(new Family { Name = name, Path = path, Get = get, Set = set, Alt = alt, Expect = expect });
        Func<object, object> Ref(int c, Func<ListEntry, bool> ok = null) => v => NextRow(cat, c, (string)v, ok);
        var f = ch.Face; var b = ch.Body;
        // face
        Add("face.skin", "chara.face.skin", () => f.Skin, v => f.Skin = (string)v, Ref(sex.SkinF, r => HasTex(r, "MainTex")), FaceC, FaceD);
        Add("body.skin.color", "chara.body.skin.color", () => b.Skin.Color, v => b.Skin.Color = (Color4)v, AltC, FaceC, BodyC);
        foreach (var (name, layer, c) in new[] { ("eyeshadow", f.Makeup.Eyeshadow, 316), ("cheek", f.Makeup.Cheek, 320), ("lip", f.Makeup.Lip, 322) })
        {
            var m = layer; string p = "chara.face.makeup." + name;
            Add("face.makeup." + name + ".item", p + ".item", () => m.Item, v => m.Item = (string)v, Ref(c), FaceC, FaceD);
            Add("face.makeup." + name + ".color", p + ".color", () => m.Color, v => m.Color = (Color4)v, AltC, FaceC);
            Add("face.makeup." + name + ".gloss", p + ".gloss", () => m.Gloss, v => m.Gloss = Fl(v), AltF, FaceD);
        }
        for (int k = 0; k < 2; k++)
        {
            var pt = f.Makeup.Paint[k]; string p = "chara.face.makeup.paint[" + k + "]", n = "face.makeup.paint[" + k + "]";
            Add(n + ".item", p + ".item", () => pt.Item, v => pt.Item = (string)v, Ref(313), FaceC, FaceD);
            Add(n + ".color", p + ".color", () => pt.Color, v => pt.Color = (Color4)v, AltC, FaceC);
            Add(n + ".gloss", p + ".gloss", () => pt.Gloss, v => pt.Gloss = Fl(v), AltF, FaceD);
            Add(n + ".metallic", p + ".metallic", () => pt.Metallic, v => pt.Metallic = Fl(v), AltF, FaceD);
            Add(n + ".layout", p + ".layout", () => pt.Layout, v => pt.Layout = (Vec4)v, AltV4, FaceC, FaceD);
            Add(n + ".rotation", p + ".rotation", () => pt.Rotation, v => pt.Rotation = Fl(v), AltF, FaceC, FaceD);
        }
        Add("face.mole.item", "chara.face.mole.item", () => f.Mole.Item, v => f.Mole.Item = (string)v, Ref(323), FaceC);
        Add("face.mole.color", "chara.face.mole.color", () => f.Mole.Color, v => f.Mole.Color = (Color4)v, AltC, FaceC);
        Add("face.mole.layout", "chara.face.mole.layout", () => f.Mole.Layout, v => f.Mole.Layout = (Vec4)v, AltV4, FaceC);
        // a whole-object set covering both creators (the makeup block; the changed member is the lip colour)
        Add("face.makeup (whole)", "chara.face.makeup", () => f.Makeup.Lip.Color, v => f.Makeup.Lip.Color = (Color4)v, AltC, FaceC, FaceD);
        // stored only: no creator
        Add("face.makeup.paint[0].layoutItem (stored)", "chara.face.makeup.paint[0].layoutItem", () => f.Makeup.Paint[0].LayoutItem, v => f.Makeup.Paint[0].LayoutItem = (string)v, Ref(7));
        // body (the shared helper)
        Add("body.skin.item", "chara.body.skin.item", () => b.Skin.Item, v => b.Skin.Item = (string)v, Ref(sex.SkinB, r => HasTex(r, "MainTex")), BodyC);
        Add("body.sunburn.item", "chara.body.sunburn.item", () => b.Sunburn.Item, v => b.Sunburn.Item = (string)v, Ref(sex.Sunburn), BodyC);
        Add("body.sunburn.color", "chara.body.sunburn.color", () => b.Sunburn.Color, v => b.Sunburn.Color = (Color4)v, AltC, BodyC);
        {
            var pt = b.Paint[0]; const string p = "chara.body.paint[0]", n = "body.paint[0]";
            Add(n + ".item", p + ".item", () => pt.Item, v => pt.Item = (string)v, Ref(313), BodyC, BodyD);
            Add(n + ".color", p + ".color", () => pt.Color, v => pt.Color = (Color4)v, AltC, BodyC);
            Add(n + ".gloss", p + ".gloss", () => pt.Gloss, v => pt.Gloss = Fl(v), AltF, BodyD);
            Add(n + ".metallic", p + ".metallic", () => pt.Metallic, v => pt.Metallic = Fl(v), AltF, BodyD);
            Add(n + ".layout", p + ".layout", () => pt.Layout, v => pt.Layout = (Vec4)v, AltV4, BodyC, BodyD);
            Add(n + ".rotation", p + ".rotation", () => pt.Rotation, v => pt.Rotation = Fl(v), AltF, BodyC, BodyD);
            Add(n + ".layoutItem", p + ".layoutItem", () => pt.LayoutItem, v => pt.LayoutItem = (string)v, Ref(8), BodyC, BodyD);
        }
        return l;
    }

    static readonly string[] AllCreators = { FaceC, FaceD, BodyC, BodyD };

    static Dictionary<string, int> RebuildCounts(EditContext x) =>
        x.Composites().Where(c => AllCreators.Contains(c.Name)).ToDictionary(c => c.Name, c => c.Rebuilds);

    static void SetBack(Family fam, object old, CharaEditor ed)
    {
        fam.Set(old);
        ed.MarkDirty(fam.Path);
        ed.Flush();
    }

    // ================================================================ Run (headless)

    public static void Run()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"maker face (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        try
        {
            using (var h = MakerCoreTest.Harness.Open())
            {
                if (Want("FT1")) Guard("FT1", () => FT1(h));
                if (Want("FT2")) Guard("FT2", () => FT2(h));
                if (Want("FT3")) Guard("FT3", () => FT3(h));
                if (Want("FT4")) Guard("FT4", () => FT4(h));
                if (Want("FT5")) Guard("FT5", () => FT5(h));
                if (Want("FT6")) Guard("FT6", () => FT6(h));
                if (Want("FT7")) Guard("FT7", () => FT7(h));
            }
        }
        finally { FaceSetup.Seed = fseed; CharaEditFlush.ForcePreview = false; }
        Finish("maker_face_report.txt", "MakerFaceTest FT1-FT7", sw);
    }

    static void Guard(string id, Action a)
    {
        rep.AppendLine();
        rep.AppendLine("== " + id);
        try { a(); }
        catch (Exception e) { Check(id + ": no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace, 1200)); }
    }

    // ---------------------------------------------------------------- FT1

    static void FT1(MakerCoreTest.Harness h)
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
                foreach (var fam in Families(ctx.Chara, h.Catalog, ctx.IsMale))
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
                    var counted = AllCreators.Where(n => after.TryGetValue(n, out int a) && before.TryGetValue(n, out int b) && a > b).ToList();
                    var got = r.CreatorsRebuilt.Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
                    var exp = fam.Expect.OrderBy(n => n, StringComparer.Ordinal).ToList();
                    bool pass = got.SequenceEqual(exp) && counted.OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(exp) && r.CreatorsRebuilt.Count == exp.Count && r.Errors.Count == 0;
                    if (Check(label + ": creators rebuilt = the rebuild table", pass, $"rebuilt [{string.Join(", ", r.CreatorsRebuilt)}], expected [{string.Join(", ", exp)}]; units [{string.Join(",", r.Units)}]" + (r.Errors.Count > 0 ? "; errors " + string.Join("; ", r.Errors) : ""))) ok++;
                    SetBack(fam, old, ed);
                }
                // the character after the families (every member set back) equals a fresh build of its documents
                var sA = live.Snapshot();
                var container = new CharaContainer { Subtree = live.Result.Stored.Clone() };
                container.Res.CopyFrom(live.Container.Res, container.Subtree.ReferencedResources());
                using (var fresh = new Disposer(h.Build(container, "fresh")))
                {
                    var d = fresh.B.Ok ? CharaSnapshot.Compare(fresh.B.Snapshot(), sA, 12) : null;
                    Check(Rel(file) + ": after the families = a fresh build", d != null && d.Equal, d?.ToString() ?? fresh.B.Error);
                }
            }
            finally { live?.Dispose(); h.ReleaseBundles(); }
        }
        Check("FT1: every family rebuilds exactly the creators of the table", fams > 0 && ok == fams, $"{ok} of {fams}");
    }

    // ---------------------------------------------------------------- FT7 heads with their own compositing materials

    static void FT7(MakerCoreTest.Harness h)
    {
        FT7Rules(h);
        var lists = MakerCatalog.For(h.Catalog);
        var heads = h.Catalog.EntriesOf(HeadBundleRedirect.FemaleHeadCategory);
        var redirected = heads.Where(e => HeadBundleRedirect.RedirectOf(e) != null && lists.IsListed(210, 1, e.Key))
                              .OrderBy(e => e.Key.ToString(), StringComparer.Ordinal).ToList();
        Info($"FT7 female heads {heads.Count}, redirected and listed {redirected.Count}");
        // two heads of different bundles, so the second change reloads from one redirect to another
        var a = redirected.FirstOrDefault();
        var b = redirected.FirstOrDefault(e => a != null && HeadBundleRedirect.RedirectOf(e) != HeadBundleRedirect.RedirectOf(a));
        if (!Check("FT7 two installed heads with different redirects", a != null && b != null, $"{redirected.Count} redirected heads")) return;
        using (var n = NodeOf(h, FemaleCard))
        {
            var ids = new[] { n.Id };
            string own = n.Stored.Chara.Face.Head;
            PassesFrom(h, n, "FT7 BR-Chan (" + own + ")", HeadBundleRedirect.VanillaBundle, null, null);
            foreach (var (head, label) in new[] { (a.Key.ToString(), "a"), (b.Key.ToString(), "b"), (own, "own") })
            {
                var x = n.Real.Context.Edit;
                var before = RebuildCounts(x);
                var res = n.Ops.ChangeItem(ids, "appearance.chara.face.head", head, User);
                Check($"FT7 head → {head} ({label}): ok", res.Ok && n.Stored.Chara.Face.Head == head, res.ToString());
                ContentId.TryParse(head, out var hid);
                var row = h.Catalog.GetEntry(HeadBundleRedirect.FemaleHeadCategory, hid);
                PassesFrom(h, n, $"FT7 {head} ({label})", HeadBundleRedirect.BundleFor(row), row, before);
                var d = VsFresh(h, n);
                Check($"FT7 {head} ({label}): snapshot = a fresh node", d.Equal, d.ToString());
            }
        }
        h.ReleaseBundles();
    }

    /// <summary>The face passes of the node draw copies of <paramref name="bundle"/>'s materials (the paint mask object and the
    /// eyeshadow / cheek / lip layouts, which no input overwrites); with <paramref name="before"/>, both were rebuilt since.</summary>
    static void PassesFrom(MakerCoreTest.Harness h, MakerEditTest.Node n, string label, string bundle, ListEntry head, Dictionary<string, int> before)
    {
        var ctx = n.Real.Context;
        var detail = new List<string>();
        bool ok = true;
        foreach (var (c, mat) in new[] { (ctx.FaceSkin, FaceC), (ctx.FaceDetail, FaceD) })
        {
            var src = TexCreator.Source(h.Catalog, bundle, bundle == HeadBundleRedirect.VanillaBundle ? null : head, mat);
            if (c == null || !src || !c.Mat) { ok = false; detail.Add(mat + ": no pass or no source"); continue; }
            bool same = c.Bundle == bundle && c.Mat.GetTexture("_PaintMask") == src.GetTexture("_PaintMask")
                        && new[] { "_Texture9UV", "_Texture10UV", "_Texture11UV" }.All(k => c.Mat.GetVector(k) == src.GetVector(k));
            ok &= same;
            detail.Add($"{mat}: {c.Bundle}, mask {(c.Mat.GetTexture("_PaintMask") ? c.Mat.GetTexture("_PaintMask").width : 0)}, eyeshadow {c.Mat.GetVector("_Texture11UV")}");
        }
        if (before != null)
        {
            var after = RebuildCounts(ctx.Edit);
            bool rebuilt = new[] { FaceC, FaceD }.All(k => after.TryGetValue(k, out int v) && before.TryGetValue(k, out int w) && v > w);
            ok &= rebuilt;
            detail.Add("both rebuilt " + rebuilt);
        }
        Check(label + ": the face passes from " + bundle + (before != null ? ", both rebuilt" : ""), ok, string.Join("; ", detail));
    }

    /// <summary>rules on synthetic input: the element's attributes, and the head rows it applies to.</summary>
    static void FT7Rules(MakerCoreTest.Harness h)
    {
        var r = HeadBundleRedirect.Read("<Head_Bundle_Redirect Slot=\"727\" Path=\"chara\\mm_base-x.unity3d\" />");
        Check("FT7 rules: Slot and Path read (backslashes as slashes)", r.HasValue && r.Value.slot == 727 && r.Value.path == "chara/mm_base-x.unity3d", r?.ToString() ?? "null");
        Check("FT7 rules: no integer Slot, no Path or no XML redirect nothing",
              HeadBundleRedirect.Read("<Head_Bundle_Redirect Path=\"a.unity3d\" />") == null && HeadBundleRedirect.Read("<Head_Bundle_Redirect Slot=\"x\" Path=\"a.unity3d\" />") == null
              && HeadBundleRedirect.Read("<Head_Bundle_Redirect Slot=\"1\" />") == null && HeadBundleRedirect.Read("<Head_Bundle_Redirect Slot=\"1\" Path=\"\" />") == null
              && HeadBundleRedirect.Read("not xml") == null && HeadBundleRedirect.Read(null) == null);
        var real = h.Catalog.EntriesOf(HeadBundleRedirect.FemaleHeadCategory).FirstOrDefault(e => HeadBundleRedirect.RedirectOf(e) != null);
        if (real == null) return;
        var male = new ListEntry { Category = 110, Key = real.Key, Zipmod = real.Zipmod };
        var otherId = new ListEntry { Category = 210, Key = new ContentId(real.Key.SourceGuid, real.Key.Id + 1), Zipmod = real.Zipmod };
        var vanilla = h.Catalog.GetEntry(210, ContentId.Vanilla(0));
        Check("FT7 rules: a male head row, another id of the zipmod and a vanilla head are not redirected",
              HeadBundleRedirect.RedirectOf(male) == null && HeadBundleRedirect.RedirectOf(otherId) == null && HeadBundleRedirect.BundleFor(vanilla) == HeadBundleRedirect.VanillaBundle
              && HeadBundleRedirect.BundleFor(null) == HeadBundleRedirect.VanillaBundle);
    }

    sealed class Disposer : IDisposable
    {
        public readonly MakerCoreTest.Built B;
        public Disposer(MakerCoreTest.Built b) { B = b; }
        public void Dispose() => B?.Dispose();
    }

    // ---------------------------------------------------------------- scene helpers

    static MakerEditTest.Node NodeOf(MakerCoreTest.Harness h, string file)
    {
        var c = MakerEditTest.Import(h, file, out string err);
        if (c == null) throw new Exception("import " + Rel(file) + ": " + err);
        var n = MakerEditTest.NewNode(h, c, Rel(file));
        if (!n.Ok) { n.Dispose(); throw new Exception("node " + Rel(file) + ": " + n.Error); }
        return n;
    }

    /// <summary>The live node's snapshot vs a fresh node of its current documents.</summary>
    static SnapshotDiff VsFresh(MakerCoreTest.Harness h, MakerEditTest.Node n)
    {
        var s = n.Snapshot();
        using (var fresh = MakerEditTest.NewNode(h, n.Session.Model.File.ExtractCharacter(n.Id), n.Label + " (fresh)"))
        {
            if (!fresh.Ok) throw new Exception("fresh node: " + fresh.Error);
            return CharaSnapshot.Compare(fresh.Snapshot(), s, 12);
        }
    }

    static string FemaleCard => TestTargets.Abs(TestTargets.Load().visual.female);
    static string MaleCard => TestTargets.Abs(TestTargets.Load().visual.male);

    static bool BitsEqual(float[] a, float[] b) =>
        a != null && b != null && a.Length == b.Length && a.Select(BitConverter.SingleToInt32Bits).SequenceEqual(b.Select(BitConverter.SingleToInt32Bits));

    /// <summary>W10 heads (mod heads whose prefab carries AIT/Skin Translucency on o_head slot 0).</summary>
    static readonly string[] W10Heads = { "chloe#24", "cunihinx.akamegdn#482", "cunihinx.akanelj#518", "cunihinx.akira#177", "cunihinx.akisf6#538", "cunihinx.alisat8#520" };

    static string FirstListed(MakerLists lists, int cat, int sex, IEnumerable<string> refs)
    {
        foreach (var r in refs) if (ContentId.TryParse(r, out var id) && lists.IsListed(cat, sex, id)) return r;
        return null;
    }

    /// <summary>The listed head of the sex with the most face skins.</summary>
    static string MostSkinsHead(MakerLists lists, int sex)
    {
        int headCat = sex == 0 ? 110 : 210, skinCat = sex == 0 ? 111 : 211;
        string best = null; int most = -1;
        foreach (var row in lists.Rows(headCat, sex))
        {
            if (row.IsVanilla) continue;
            int n = lists.Rows(skinCat, sex, ListContext.ForHead(row.Key)).Count;
            if (n > most) { most = n; best = row.Key.ToString(); }
        }
        return best;
    }

    // ---------------------------------------------------------------- FT2

    static void FT2(MakerCoreTest.Harness h)
    {
        var lists = MakerCatalog.For(h.Catalog);
        using (var n = NodeOf(h, FemaleCard))
        {
            var ids = new[] { n.Id };
            int sex = n.Stored.Chara.Profile.Sex;
            // non-default face shapes, also beyond the slider range
            var shapeSets = new List<(string, object)> { ("appearance.chara.face.shape[0]", 0.83f), ("appearance.chara.face.shape[10]", 0.21f), ("appearance.chara.face.shape[20]", 1.35f), ("appearance.chara.face.shape[40]", -0.25f) };
            Check("FT2: non-default face shapes set", n.Ops.EditAppearance(ids, shapeSets, User).Ok);
            string w10 = FirstListed(lists, 210, sex, W10Heads);
            string most = MostSkinsHead(lists, sex);
            Info($"W10 head {w10 ?? "(none listed)"}, the mod head with the most skins {most} ({(most != null && ContentId.TryParse(most, out var mh) ? lists.Rows(211, sex, ListContext.ForHead(mh)).Count : 0)} skins)");
            var heads = new[] { "illusion.hs2#0", "illusion.hs2#2", "illusion.hs2#3", w10, most, "illusion.hs2#1" };
            int k = 0;
            foreach (var head in heads)
            {
                k++;
                if (head == null) { Info($"step {k}: no head (skipped)"); continue; }
                var f = n.Stored.Chara.Face;
                var shapes = (float[])f.Shape.Clone();
                string skin0 = f.Skin;
                ContentId.TryParse(head, out var hid);
                ContentId.TryParse(skin0, out var sid);
                string expectSkin = lists.SkinAfterHeadChange(hid, sid, sex).ToString();
                if (f.Head == head) Info($"step {k}: the head is already {head}");
                var res = n.Ops.ChangeItem(ids, "appearance.chara.face.head", head, User);
                f = n.Stored.Chara.Face;
                var r = n.Real.LastEditFlush;
                Check($"FT2 step {k} → {head}: ChangeItem ok, head set", res.Ok && f.Head == head, res.ToString());
                Check($"FT2 step {k} → {head}: face.shape unchanged bit for bit", BitsEqual(shapes, f.Shape));
                Check($"FT2 step {k} → {head}: skin by the H3 rule", f.Skin == expectSkin, $"{skin0} → {f.Skin}, expected {expectSkin}");
                Info($"step {k}: flush {r}");
                var d = VsFresh(h, n);
                Check($"FT2 step {k} → {head}: snapshot = a fresh node", d.Equal, d.ToString());
            }
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- FT3

    static void FT3(MakerCoreTest.Harness h)
    {
        CharaEditFlush.ForcePreview = true;
        try
        {
            foreach (var (file, what, path, extra) in new[]
            {
                (FemaleCard, "face shape", "appearance.chara.face.shape[5]", new string[0]),
                (FemaleCard, "body shape", "appearance.chara.body.shape[3]", new[] { UnitMap.Bust }),
                (MaleCard, "face shape (male)", "appearance.chara.face.shape[12]", new string[0]),
            })
                using (var n = NodeOf(h, file))
                {
                    var ids = new[] { n.Id };
                    var real = n.Real;
                    var ed = real.Editor;
                    var bus = n.Session.Commands;
                    int flushes0 = real.EditFlushes;
                    var previewMs = new List<double>();
                    bool allPreview = true, noDeferredRan = true;
                    var tx = bus.BeginOrJoin("Drag", new ExecuteOptions { Source = CommandSource.User, Label = "Drag" });
                    try
                    {
                        for (int i = 0; i < 20; i++)
                        {
                            var res = n.Ops.EditAppearance(ids, new List<(string, object)> { (path, 0.31f + i * 0.037f) }, User);
                            if (!res.Ok) { Check($"FT3 {what}: preview set {i}", false, res.ToString()); break; }
                            n.Apply();
                            var r = real.LastEditFlush;
                            allPreview &= r != null && r.Mode == FlushMode.Preview;
                            noDeferredRan &= r != null && !r.Units.Contains(UnitMap.AnimRefresh) && !r.Units.Contains(UnitMap.DynBone) && !r.Units.Contains(UnitMap.Bust);
                            if (r != null) previewMs.Add(r.Milliseconds);
                        }
                        var expect = new List<string> { UnitMap.AnimRefresh, UnitMap.DynBone }.Concat(extra).OrderBy(s => s, StringComparer.Ordinal).ToList();
                        var got = ed.Deferred.OrderBy(s => s, StringComparer.Ordinal).ToList();
                        Check($"FT3 {what}: 20 preview flushes, all in Preview mode, none ran the deferrable units", allPreview && noDeferredRan && real.EditFlushes - flushes0 >= 20,
                              $"flushes {real.EditFlushes - flushes0}, preview median {F(Median(previewMs))} ms");
                        Check($"FT3 {what}: Deferred = {{{string.Join(", ", expect)}}}", got.SequenceEqual(expect), "got " + string.Join(", ", got) + "; report " + real.LastEditFlush);
                        tx.Commit();
                    }
                    finally { tx.Dispose(); }
                    n.Apply();
                    var rc = real.LastEditFlush;
                    Check($"FT3 {what}: the commit flush runs the deferred units (Normal) and empties Deferred",
                          rc != null && rc.Mode == FlushMode.Normal && rc.Units.Contains(UnitMap.AnimRefresh) && rc.Units.Contains(UnitMap.DynBone) && !ed.HasDeferred && rc.Deferred.Count == 0,
                          rc?.ToString());
                    Info($"{what}: preview flush median {F(Median(previewMs))} ms, commit flush {F(rc?.Milliseconds ?? double.NaN)} ms (UF2, headless)");
                    var d = VsFresh(h, n);
                    Check($"FT3 {what}: after the commit = a fresh node", d.Equal, d.ToString());

                    // a cancelled drag returns to the snapshot before it
                    var s0 = n.Snapshot();
                    var tx2 = bus.BeginOrJoin("Drag", new ExecuteOptions { Source = CommandSource.User, Label = "Drag" });
                    try
                    {
                        for (int i = 0; i < 5; i++) { n.Ops.EditAppearance(ids, new List<(string, object)> { (path, 0.9f - i * 0.1f) }, User); n.Apply(); }
                        Check($"FT3 {what}: deferred work during the second drag", ed.HasDeferred, string.Join(", ", ed.Deferred));
                        tx2.Cancel();
                    }
                    finally { tx2.Dispose(); }
                    n.Apply();
                    var dc = CharaSnapshot.Compare(s0, n.Snapshot(), 12);
                    Check($"FT3 {what}: a cancelled drag = the snapshot before it, Deferred empty", dc.Equal && !ed.HasDeferred, dc.ToString());
                }
        }
        finally { CharaEditFlush.ForcePreview = false; h.ReleaseBundles(); }
    }

    // ---------------------------------------------------------------- FT4 preset pushes

    /// <summary>4.0's H4 heads (Tools/compare_maker.json h4); ada4#424 and ElenaFisher#166 name a preset their MainAB lacks.</summary>
    static readonly string[] H4Female = { "Roy12.Kula#86", "Roy12.Nagisa#87", "Roy12.Sayuri#81", "illusion.hs2#0", "mcm82.captainmmfr#315", "illusion.hs2#3", "SelfishCrow.raven#278" };
    static readonly string[] H4FemaleMissing = { "cunihinx.ada4#424", "Darkruler.ElenaFisher#166" };
    static readonly string[] H4Male = { "illusion.hs2#0", "Supreme.Maleface#38" };

    static int Entries(MakerEditTest.Node n) => n.Session.Commands.UndoList.Count;
    static string LastLabel(MakerEditTest.Node n) => n.Session.Commands.UndoList.Count > 0 ? n.Session.Commands.UndoList[0].Label : null;

    static void FT4(MakerCoreTest.Harness h)
    {
        var lists = MakerCatalog.For(h.Catalog);
        foreach (var (file, heads, missing) in new[] { (FemaleCard, H4Female, H4FemaleMissing), (MaleCard, H4Male, new string[0]) })
            using (var n = NodeOf(h, file))
            {
                var ids = new[] { n.Id };
                int sex = n.Stored.Chara.Profile.Sex;
                foreach (var head in heads.Concat(missing))
                {
                    bool miss = missing.Contains(head);
                    if (!ContentId.TryParse(head, out var hid) || !lists.IsListed(sex == 0 ? 110 : 210, sex, hid)) { Info($"{Rel(file)} {head}: not listed (skipped)"); continue; }
                    // start from another head so the push always changes the head
                    string other = sex == 0 ? lists.Rows(110, 0).First(r => r.Key != hid).Key.ToString() : head == "illusion.hs2#1" ? "illusion.hs2#2" : "illusion.hs2#1";
                    n.Ops.ChangeItem(ids, "appearance.chara.face.head", other, User);
                    var before = DocClone.Clone(n.Stored.Chara.Face);
                    int e0 = Entries(n);
                    var res = n.Ops.ApplyFacePreset(ids, head, User);
                    var f = n.Stored.Chara.Face;
                    bool warned = res.Warnings.Any(w => w.Contains("edit.presetMissing"));
                    // a preset carries its own head id, which wins (4.0 H4: some mod presets name a head that resolves to
                    // HS2's fallback); only the missing-asset push must end on the pushed head. A push that changes nothing is NoOp.
                    bool entryOk = res.Status == HS2Next.Scenes.CommandStatus.NoOp ? Entries(n) == e0 : Entries(n) == e0 + 1 && LastLabel(n) == "Face preset";
                    Check($"FT4 {Rel(file)} push {head}: ok, one entry \"Face preset\"" + (miss ? $", head = {head}, warning edit.presetMissing" : ""),
                          res.Ok && entryOk && (!miss || f.Head == head) && warned == miss,
                          $"{res}; entries +{Entries(n) - e0} '{LastLabel(n)}', head {f.Head}, warnings [{string.Join("; ", res.Warnings)}]");
                    if (miss)
                    {
                        // the head change only: every other face member kept (the skin by the H3 snap)
                        ContentId.TryParse(before.Skin, out var sid);
                        string expSkin = lists.SkinAfterHeadChange(hid, sid, sex).ToString();
                        bool same = BitsEqual(before.Shape, f.Shape) && before.Eyebrow.Item == f.Eyebrow.Item && before.Eyes.Left.Iris.Item == f.Eyes.Left.Iris.Item
                                    && before.Makeup.Lip.Item == f.Makeup.Lip.Item && before.Mole.Item == f.Mole.Item && before.Detail.Item == f.Detail.Item;
                        Check($"FT4 {Rel(file)} push {head} (no preset asset): only the head changed (skin by H3)", same && f.Skin == expSkin, $"skin {before.Skin} → {f.Skin} (expected {expSkin})");
                    }
                    var d = VsFresh(h, n);
                    Check($"FT4 {Rel(file)} push {head}: snapshot = a fresh node", d.Equal, d.ToString());
                }
                h.ReleaseBundles();
            }
    }

    // ---------------------------------------------------------------- FT5 linked eyes, copies, resets

    static void FT5(MakerCoreTest.Harness h)
    {
        using (var n = NodeOf(h, FemaleCard))
        {
            var ids = new[] { n.Id };
            var bus = n.Session.Commands;
            int base0 = Entries(n);
            var doc0 = DocClone.Clone(n.Stored.Chara);
            n.Ops.BeginEditSession(n.Id);   // the reference documents
            Eye L() => n.Stored.Chara.Face.Eyes.Left;
            Eye R() => n.Stored.Chara.Face.Eyes.Right;
            void Fresh(string what) { var d = VsFresh(h, n); Check("FT5 " + what + ": snapshot = a fresh node", d.Equal, d.ToString()); }
            OpResult Set(string p, object v) => n.Ops.EditAppearance(ids, new List<(string, object)> { (p, v) }, User);
            Set("appearance.chara.face.eyes.linked", true);
            var c = new Color4(0.21f, 0.62f, 0.33f, 0.9f);
            Set("appearance.chara.face.eyes.left.iris.color", c);
            Check("FT5 linked: an iris colour edit of the left eye sets both", L().Iris.Color.Equals(c) && R().Iris.Color.Equals(c));
            Fresh("linked iris colour");
            Set("appearance.chara.face.eyes.linked", false);
            var keepLeft = L().Pupil.Size;
            Set("appearance.chara.face.eyes.right.pupil.size", new Vec2(0.15f, 0.92f));
            Check("FT5 unlinked: a right pupil size edit leaves the left eye", L().Pupil.Size.Equals(keepLeft) && R().Pupil.Size.Equals(new Vec2(0.15f, 0.92f)));
            Set("appearance.chara.face.eyes.right.iris.emission", 1.4f);
            Fresh("unlinked right eye");
            int e0 = Entries(n);
            var cr = n.Ops.CopyEye(ids, false, User);
            Check("FT5 copy right → left: one entry, left = right", cr.Ok && Entries(n) == e0 + 1 && L().Pupil.Size.Equals(R().Pupil.Size) && L().Iris.Emission == R().Iris.Emission && L().Iris.Item == R().Iris.Item, cr.ToString());
            Fresh("copy right → left");
            Set("appearance.chara.face.eyes.left.iris.emission", 0.3f);
            var cl = n.Ops.CopyEye(ids, true, User);
            Check("FT5 copy left → right: right = left", cl.Ok && R().Iris.Emission == 0.3f, cl.ToString());
            Fresh("copy left → right");
            // resets to the reference (the documents when the session began)
            Set("appearance.chara.face.eyebrow.tilt", 0.93f);
            Set("appearance.chara.face.mole.layout", new Vec4(0.1f, 0.2f, 0.3f, 0.4f));
            e0 = Entries(n);
            var rr = n.Ops.ResetAppearance(ids, new[] { "appearance.chara.face.eyebrow.tilt", "appearance.chara.face.mole.layout#2", "appearance.chara.face.eyes.right.pupil.size" }, null, User);
            var f = n.Stored.Chara.Face;
            Check("FT5 reset: one entry \"Reset value\", values = the reference", rr.Ok && Entries(n) == e0 + 1 && LastLabel(n) == "Reset value" && f.Eyebrow.Tilt == doc0.Face.Eyebrow.Tilt
                  && f.Mole.Layout.Z == doc0.Face.Mole.Layout.Z && f.Mole.Layout.X == 0.1f && f.Eyes.Right.Pupil.Size.Equals(doc0.Face.Eyes.Right.Pupil.Size),
                  $"{rr}; tilt {f.Eyebrow.Tilt} (ref {doc0.Face.Eyebrow.Tilt}), mole {f.Mole.Layout.X},{f.Mole.Layout.Z} (ref z {doc0.Face.Mole.Layout.Z})");
            Fresh("resets");
            while (bus.CanUndo && bus.UndoList.Count > base0) bus.Undo();
            n.Apply();
            Fresh("undo all");
            while (bus.CanRedo) bus.Redo();
            n.Apply();
            Fresh("redo all");
            n.Ops.EndEditSession(n.Id);
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- FT6 layout pushes

    static void FT6(MakerCoreTest.Harness h)
    {
        using (var n = NodeOf(h, FemaleCard))
        {
            var ids = new[] { n.Id };
            foreach (var (path, row, exp) in new[]
            {
                ("appearance.chara.face.mole.layout", "illusion.hs2#4", new Vec4(0.33f, 0.33f, 0.75f, 0.52f)),
                ("appearance.chara.face.makeup.paint[0].layout", "illusion.hs2#0", new Vec4(0.5f, 0.5f, 0.76f, 0.35f)),
                ("appearance.chara.face.makeup.paint[1].layout", "illusion.hs2#5", new Vec4(0.5f, 0.5f, 0.48f, 0.07f)),
            })
            {
                n.Ops.EditAppearance(ids, new List<(string, object)> { (path, new Vec4(0.11f, 0.22f, 0.33f, 0.44f)) }, User);   // so the push changes it
                var f = n.Stored.Chara.Face;
                float rot0 = f.Makeup.Paint[0].Rotation; string li0 = f.Makeup.Paint[0].LayoutItem;
                int e0 = Entries(n);
                var r = n.Ops.ApplyLayoutPreset(ids, path, row, User);
                f = n.Stored.Chara.Face;
                Vec4 got = path.Contains("mole") ? f.Mole.Layout : path.Contains("paint[0]") ? f.Makeup.Paint[0].Layout : f.Makeup.Paint[1].Layout;
                bool near = Math.Abs(got.X - exp.X) < 1e-5f && Math.Abs(got.Y - exp.Y) < 1e-5f && Math.Abs(got.Z - exp.Z) < 1e-5f && Math.Abs(got.W - exp.W) < 1e-5f;
                Check($"FT6 push {row} → {path}: (S, S, X, Y), one entry, rotation and layoutItem kept", r.Ok && near && Entries(n) == e0 + 1 && f.Makeup.Paint[0].Rotation == rot0 && f.Makeup.Paint[0].LayoutItem == li0,
                      $"{r}; got ({got.X}, {got.Y}, {got.Z}, {got.W})");
                Info($"FT6 {row}: flush {n.Real.LastEditFlush}");
                var d = VsFresh(h, n);
                Check($"FT6 push {row} → {path}: snapshot = a fresh node", d.Equal, d.ToString());
            }
        }
        h.ReleaseBundles();
    }

    // ================================================================ RunRender (graphics)

    public static void RunRender()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"maker face render (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName})");
        bool device = FaceDigest.HasDevice;
        Check("graphics device present", device, SystemInfo.graphicsDeviceType.ToString());
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        try
        {
            if (device)
                using (var h = MakerCoreTest.Harness.Open())
                {
                    if (Want("R1")) Guard("R1 selective rebuilds = full rebuilds (RT read-back)", () => R1(h));
                    if (Want("R2")) Guard("R2 gloss state on the face", () => R2(h));
                    if (Want("R3")) Guard("R3 UF7 (W10)", () => R3(h));
                }
        }
        finally { FaceSetup.Seed = fseed; }
        Finish("maker_face_render_report.txt", "MakerFaceTest.render", sw);
    }

    /// <summary>Read-back hashes of the creators of the parts a family touches (face families: the face pair; body families:
    /// the body pair; the skin colour: both) — fewer 4096² read-backs (a long run of them reset the GPU device once).</summary>
    static Dictionary<string, string> RtHashes(EditContext x, Family fam)
    {
        bool face = fam.Expect.Any(e => e == FaceC || e == FaceD) || fam.Path.Contains(".face."), body = fam.Expect.Any(e => e == BodyC || e == BodyD);
        return x.Composites().Where(c => (face && (c.Name == FaceC || c.Name == FaceD)) || (body && (c.Name == BodyC || c.Name == BodyD)))
                .ToDictionary(c => c.Name, c => FaceDigest.RtHash(c.Creator.Target));
    }

    static void R1(MakerCoreTest.Harness h)
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
                foreach (var fam in Families(ctx.Chara, h.Catalog, ctx.IsMale))
                {
                    object old = fam.Get(), alt = fam.Alt(old);
                    if (alt == null || Equals(alt, old)) continue;
                    fams++;
                    fam.Set(alt);
                    ed.MarkDirty(fam.Path);
                    ed.Flush();
                    var selective = RtHashes(x, fam);
                    ed.MarkUnit(UnitMap.SkinFace); ed.MarkUnit(UnitMap.SkinBody);
                    var rf = ed.Flush();
                    var full = RtHashes(x, fam);
                    var diff = AllCreators.Where(c => selective.ContainsKey(c) && (!full.ContainsKey(c) || full[c] != selective[c])).ToList();
                    if (Check($"{Rel(file)} {fam.Name}: RT hashes after the selective flush = after rebuilding every creator", diff.Count == 0 && rf.CreatorsRebuilt.Count == 4,
                              diff.Count == 0 ? "" : "differ: " + string.Join(", ", diff))) ok++;
                    SetBack(fam, old, ed);
                }
            }
            finally { live?.Dispose(); h.ReleaseBundles(); }
        }
        Check("R1: every family", fams > 0 && ok == fams, $"{ok} of {fams}");
    }

    static float GlossOf(Material m) => m && m.HasProperty("_Gloss") ? m.GetFloat("_Gloss") : float.NaN;

    /// <summary>face _Gloss = draw.face's value + 0.2 · gloss state; re-applied after draw.face edits and a head swap.</summary>
    static void R2(MakerCoreTest.Harness h)
    {
        using (var n = NodeOf(h, FemaleCard))
        {
            var ids = new[] { n.Id };
            var real = n.Real;
            Material Face() => real.Assembler.LiveFaceDraw();
            float Expect() => Mathf.Lerp(0, 0.8f, n.Stored.Chara.Body.Skin.Gloss);
            float g0 = GlossOf(Face());
            Check("R2: state 0 → face _Gloss = lerp(0, 0.8, skin gloss)", Mathf.Abs(g0 - Expect()) < 1e-6f, $"{g0} vs {Expect()}");
            n.Ops.SetState(ids, "gloss", 0.5f, User); n.Apply();
            Check("R2: state 0.5 → face _Gloss = base + 0.1", Mathf.Abs(GlossOf(Face()) - (Expect() + 0.1f)) < 1e-6f, "" + GlossOf(Face()));
            n.Ops.EditAppearance(ids, new List<(string, object)> { ("appearance.chara.face.eyebrow.tilt", 0.71f) }, User); n.Apply();
            Check("R2: after a draw.face edit (eyebrow tilt) still base + 0.1", Mathf.Abs(GlossOf(Face()) - (Expect() + 0.1f)) < 1e-6f, "" + GlossOf(Face()));
            n.Ops.EditAppearance(ids, new List<(string, object)> { ("appearance.chara.body.skin.gloss", 0.35f) }, User); n.Apply();
            Check("R2: after a skin gloss edit: the new base + 0.1", Mathf.Abs(GlossOf(Face()) - (Expect() + 0.1f)) < 1e-6f, $"{GlossOf(Face())} vs {Expect() + 0.1f}");
            var oldFace = Face();
            n.Ops.ChangeItem(ids, "appearance.chara.face.head", n.Stored.Chara.Face.Head == "illusion.hs2#3" ? "illusion.hs2#1" : "illusion.hs2#3", User); n.Apply();
            Check("R2: after a head swap (new face draw) base + 0.1", Face() != oldFace && Mathf.Abs(GlossOf(Face()) - (Expect() + 0.1f)) < 1e-6f, "" + GlossOf(Face()));
            n.Ops.SetState(ids, "gloss", 0f, User); n.Apply();
            Check("R2: state 0 → base", Mathf.Abs(GlossOf(Face()) - Expect()) < 1e-6f, "" + GlossOf(Face()));
        }
    }

    /// <summary>UF7: a card whose head is a W10 head: o_head slot 0 is the face draw (MatData copy), no drawn
    /// AIT/Skin Translucency and no URP/Lit fallback on the face renderers. Shader names and numbers only.</summary>
    static void R3(MakerCoreTest.Harness h)
    {
        var cards = MakerCoreTest.AllCards();
        string card = cards.FirstOrDefault(c => Path.GetFileName(c) == "HS2ChaF_20250829211210545_Rowena Mod1.png")
                      ?? cards.FirstOrDefault(c => Path.GetFileName(c) == "AISChaF_20220306221701177.png");
        if (!Check("UF7: a W10 card of the census is installed", card != null)) return;
        using (var b = new Disposer(h.Build(card)))
        {
            if (!Check("UF7: built", b.B.Ok, b.B.Error)) return;
            var ctx = b.B.Context;
            Info("head " + ctx.Chara?.Face?.Head);
            var oHead = ctx.Head ? ctx.Head.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == "o_head") : null;
            var slot0 = oHead ? oHead.sharedMaterials.FirstOrDefault() : null;
            Info("o_head slot 0 shader: " + (slot0 && slot0.shader ? slot0.shader.name : "none") + "; face draw shader: " + (ctx.FaceDraw && ctx.FaceDraw.shader ? ctx.FaceDraw.shader.name : "none"));
            Check("UF7: o_head slot 0 = the face draw (MatData copy)", slot0 && (slot0 == ctx.FaceDraw || slot0.name.StartsWith(ctx.FaceDraw.name.Replace(" (Instance)", ""), StringComparison.Ordinal)), slot0 ? slot0.name : "none");
            int translucency = 0, lit = 0, total = 0, notDrawn = 0;
            foreach (var r in FaceDigest.RenderersOf(ctx, "face"))
            {
                var mats = r.sharedMaterials;
                bool drawn = r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff;
                int subMeshes = r is SkinnedMeshRenderer smr && smr.sharedMesh ? smr.sharedMesh.subMeshCount : r.GetComponent<MeshFilter>()?.sharedMesh ? r.GetComponent<MeshFilter>().sharedMesh.subMeshCount : 0;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!m || !m.shader) continue;
                    total++;
                    // W10 is the unmapped AIT/Skin Translucency (its fallback would draw); the vanilla tongue / teeth shader
                    // "AIT/Skin Translucency simple" is a different one, mapped by ShaderMap (HS2Next/AIT Skin Translucency simple)
                    bool tr = m.shader.name == "AIT/Skin Translucency", ul = m.shader.name == "Universal Render Pipeline/Lit";
                    if (m.shader.name.Contains("Translucency") && !tr) Info($"   {r.name} slot {i}: {m.shader.name} (mapped, not W10)");
                    // a slot beyond the mesh's sub-meshes is not drawn; nor is an inactive or disabled renderer
                    bool slotDrawn = drawn && i < subMeshes;
                    if ((tr || ul) && !slotDrawn) notDrawn++;
                    if (tr && slotDrawn) translucency++;
                    if (ul && slotDrawn) lit++;
                    if (tr || ul) Info($"   {r.name} slot {i}/{mats.Length} (sub-meshes {subMeshes}, renderer {(drawn ? "drawn" : "not drawn")}): {m.shader.name}");
                }
            }
            Check("UF7: no drawn AIT/Skin Translucency, no URP/Lit fallback on the face renderers", translucency == 0 && lit == 0 && total > 0, $"{total} face materials, drawn: {translucency} translucency, {lit} URP/Lit; not drawn: {notDrawn}");
            foreach (var s in FaceDigest.StatsOfPart(ctx, "face", 4))
                Info($"rtstats {s.Name} {s.Width}x{s.Height}: mean {string.Join(", ", s.Mean.Select(v => v.ToString("F4", CultureInfo.InvariantCulture)))}");
            var dg = FaceDigest.Compute(ctx, "face");
            Check("UF7: FaceDigest computes every section (with RT hashes)", dg.Materials != null && dg.Creators != null && dg.Bones != null && dg.Rts != null, dg.ToString());
            var dg2 = FaceDigest.Compute(ctx, "face");
            Check("UF7: FaceDigest is stable (two computations equal)", dg.Equals(dg2), dg2.ToString());
        }
        h.ReleaseBundles();
    }

    // ================================================================ RunTiming (graphics)

    static void Target(string what, double ms, double target, string reason = null)
    {
        string d = $"{F(ms)} ms (target {F(target)})";
        if (double.IsNaN(ms)) { Info(what + ": not measured"); return; }
        if (ms <= target) Check(what, true, d);
        else if (ms <= 2 * target || reason != null) Warn(what, d + (reason != null ? "; " + reason : ""));
        else Check(what, false, d + " (above 2× the target)");
    }

    static RenderTexture tinyFence;
    /// <summary>Waits for the GPU work submitted so far: one texel of <paramref name="rt"/> copied into a 1×1 RT, that RT read
    /// back (Phase 4.2 integrator T, as MakerBodyTest.Fence). The 4.1 fence, a region read-back of the creator's RT, allocates a
    /// full-size staging buffer per request on D3D12: every UF1 time included that allocation (4.2 package A's trap T1).</summary>
    static void Fence(RenderTexture rt)
    {
        if (!rt) return;
        if (!tinyFence || tinyFence.graphicsFormat != rt.graphicsFormat)
        {
            if (tinyFence) { tinyFence.Release(); Object.DestroyImmediate(tinyFence); }
            var d = rt.descriptor;
            d.width = d.height = 1; d.useMipMap = false; d.autoGenerateMips = false; d.depthBufferBits = 0; d.msaaSamples = 1;
            tinyFence = new RenderTexture(d) { name = "MakerFaceTest fence" };
            tinyFence.Create();
        }
        Graphics.CopyTexture(rt, 0, 0, 0, 0, 1, 1, tinyFence, 0, 0, 0, 0);
        AsyncGPUReadback.Request(tinyFence).WaitForCompletion();
    }

    static double GpuMs(Action draw, RenderTexture rt, int reps, List<double> cpu = null)
    {
        var l = new List<double>();
        for (int i = 0; i < reps; i++)
        {
            Fence(rt);
            var t = Stopwatch.StartNew();
            draw();
            double c = t.Elapsed.TotalMilliseconds;
            Fence(rt);
            l.Add(t.Elapsed.TotalMilliseconds);
            cpu?.Add(c);
        }
        return Median(l);
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

    public static void RunTiming()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        int reps = int.Parse(Arg("-reps", "20"), CultureInfo.InvariantCulture);
        rep.AppendLine($"maker face timing (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName}; Editor, warm, median of {reps}); WARN above the target, FAIL above 2×");
        bool device = FaceDigest.HasDevice;
        Check("graphics device present", device, SystemInfo.graphicsDeviceType.ToString());
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        try
        {
            if (device)
                using (var h = MakerCoreTest.Harness.Open())
                {
                    if (Want("UF1")) Guard("UF1 creator rebuilds (GPU incl. mips)", () => UF1(h, reps));
                    if (Want("UF2")) Guard("UF2 shape drags: preview vs commit; material units", () => UF2(h, reps));
                    if (Want("UF5")) Guard("UF5 head change by a pick", () => UF5(h, reps));
                    if (Want("UF6")) Guard("UF6 preset push", () => UF6(h, reps));
                }
        }
        finally { FaceSetup.Seed = fseed; CharaEditFlush.ForcePreview = false; }
        Finish("maker_face_timing_report.txt", "MakerFaceTest.timing", sw);
    }

    static void UF1(MakerCoreTest.Harness h, int reps)
    {
        using (var b = new Disposer(h.Build(FemaleCard)))
        {
            if (!Check("UF1: built", b.B.Ok, b.B.Error)) return;
            var x = b.B.Context.Edit;
            var ed = b.B.Context.Editor;
            var layer = Noise(2048, 2048, 7);
            try
            {
                foreach (var (name, pair) in new[] { ("face", new[] { x.FaceSkin, x.FaceDetail }), ("body", new[] { x.BodySkin, x.BodyDetail }) })
                {
                    var keep = pair.Select(c => (c.Creator.Under.ToList(), c.Creator.Over.ToList())).ToList();
                    for (int n = 0; n <= 2; n++)
                    {
                        foreach (var c in pair)
                        {
                            c.Creator.Under.Clear(); c.Creator.Over.Clear();
                            for (int i = 0; i < n; i++) c.Creator.Over.Add(new SkinLayer { TexType = 1, Texture = layer, Source = "uf1" });
                        }
                        var cpu = new List<double>();
                        double col = GpuMs(() => pair[0].Creator.Rebuild(), pair[0].Creator.Target, reps, cpu);
                        double det = GpuMs(() => pair[1].Creator.Rebuild(), pair[1].Creator.Target, reps);
                        double both = GpuMs(() => { pair[0].Creator.Rebuild(); pair[1].Creator.Rebuild(); }, pair[1].Creator.Target, reps);
                        Info($"UF1 {name} {pair[0].Creator.Target.width}², {n} KSOX layer(s): colour creator {F(col)} ms (CPU submit {F(Median(cpu))}), detail {F(det)} ms, both {F(both)} ms");
                        if (name == "face" && n == 0)
                        {
                            Target("face colour creator rebuild alone (GPU incl. mips)", col, 2);
                            Target("face colour creator rebuild alone (CPU submit)", Median(cpu), 0.5);
                            Target("face detail creator rebuild alone (GPU incl. mips)", det, 2);
                            Check("F8: a face colour-creator rebuild is below C5's 8 ms (no face throttle)", col < 8, F(col) + " ms");
                        }
                    }
                    for (int i = 0; i < pair.Length; i++)
                    {
                        pair[i].Creator.Under.Clear(); pair[i].Creator.Under.AddRange(keep[i].Item1);
                        pair[i].Creator.Over.Clear(); pair[i].Creator.Over.AddRange(keep[i].Item2);
                        pair[i].Creator.Rebuild();
                    }
                }
                // a face colour drag frame through the editor: skin.face with one creator (CPU flush + GPU fence)
                var lip = b.B.Context.Chara.Face.Makeup.Lip;
                var l1 = new List<double>(); var l2 = new List<double>();
                for (int i = 0; i < reps; i++)
                {
                    lip.Color = new Color4(0.2f + 0.01f * i, 0.3f, 0.4f, 1);
                    Fence(x.FaceSkin.Creator.Target);
                    ed.MarkDirty("chara.face.makeup.lip.color");
                    var t = Stopwatch.StartNew();
                    var r = ed.Flush();
                    l1.Add(t.Elapsed.TotalMilliseconds);
                    Fence(x.FaceSkin.Creator.Target);
                    l2.Add(t.Elapsed.TotalMilliseconds);
                }
                Info($"UF1 face colour drag frame (lip colour; skin.face rebuilds the colour creator only): flush CPU {F(Median(l1))} ms, incl. GPU {F(Median(l2))} ms");
                Target("face composite colour drag: flush CPU", Median(l1), 0.5);
                Info("(the GPU target applies to the creator alone, above; the frame adds the fences and the draw writes)");
            }
            finally { Object.DestroyImmediate(layer); }
        }
        h.ReleaseBundles();
    }

    static void UF2(MakerCoreTest.Harness h, int reps)
    {
        CharaEditFlush.ForcePreview = true;
        try
        {
            foreach (var (what, path, tPrev, tCommit) in new[] { ("face shape", "appearance.chara.face.shape[5]", 0.3, 3.0), ("body shape", "appearance.chara.body.shape[3]", 0.6, 6.0) })
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
                                n.Ops.EditAppearance(ids, new List<(string, object)> { (path, 0.3f + 0.05f * i + 0.001f * k) }, User);
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
                    Info($"UF2 {what}: preview flush median {F(Median(prev))} ms ({prev.Count}), commit flush median {F(Median(commit))} ms ({commit.Count})");
                    Target($"preview flush of a {what} drag", Median(prev), tPrev);
                    Target($"commit flush after a {what} drag", Median(commit), tCommit);
                }
            CharaEditFlush.ForcePreview = false;
            // face material units (draw.face, eyes)
            using (var n = NodeOf(h, FemaleCard))
            {
                var ids = new[] { n.Id };
                foreach (var (unit, path) in new[] { ("draw.face", "appearance.chara.face.eyebrow.tilt"), ("eyes", "appearance.chara.face.eyes.left.iris.emission") })
                {
                    var l = new List<double>();
                    for (int i = 0; i < reps; i++)
                    {
                        n.Ops.EditAppearance(ids, new List<(string, object)> { (path, 0.2f + 0.03f * i) }, User);
                        var r = n.Real.LastEditFlush;
                        if (r != null) l.Add(r.Milliseconds);
                    }
                    Target($"face material unit {unit} flush", Median(l), 0.1);
                }
            }
        }
        finally { CharaEditFlush.ForcePreview = false; h.ReleaseBundles(); }
    }

    static double TimeOp(MakerEditTest.Node n, Func<OpResult> op, out OpResult res)
    {
        var t = Stopwatch.StartNew();
        res = op();
        n.Apply();
        return t.Elapsed.TotalMilliseconds;
    }

    static void UF5(MakerCoreTest.Harness h, int reps)
    {
        var lists = MakerCatalog.For(h.Catalog);
        using (var n = NodeOf(h, FemaleCard))
        {
            var ids = new[] { n.Id };
            int sex = n.Stored.Chara.Profile.Sex;
            // warm: two vanilla heads back and forth (op + flush incl. both composites and the stage re-attach)
            var warm = new List<double>();
            string a = "illusion.hs2#2", b = "illusion.hs2#3";
            n.Ops.ChangeItem(ids, "appearance.chara.face.head", a, User);
            n.Ops.ChangeItem(ids, "appearance.chara.face.head", b, User);
            for (int i = 0; i < reps; i++)
                warm.Add(TimeOp(n, () => n.Ops.ChangeItem(ids, "appearance.chara.face.head", i % 2 == 0 ? a : b, User), out _));
            Target("head change by a pick, warm (op + flush)", Median(warm), 200);
            // cold vanilla: bundles released first
            var cold = new List<double>();
            for (int i = 0; i < 3; i++)
            {
                h.Catalog.UnloadAll();
                cold.Add(TimeOp(n, () => n.Ops.ChangeItem(ids, "appearance.chara.face.head", i % 2 == 0 ? "illusion.hs2#1" : "illusion.hs2#0", User), out _));
            }
            Info($"UF5 cold vanilla head change (bundles released; op + flush): median {F(Median(cold))} ms, values {string.Join(", ", cold.Select(F))}");
            // cold mod head: the asynchronous pre-open, then the edit
            string mod = FirstListed(lists, 210, sex, W10Heads) ?? MostSkinsHead(lists, sex);
            if (mod != null && ContentId.TryParse(mod, out var mid))
            {
                h.Catalog.UnloadAll();
                var row = h.Catalog.EntriesOf(210).FirstOrDefault(e => e.Key == mid);
                var t = Stopwatch.StartNew();
                var open = row != null ? h.Catalog.OpenBundleAsync(row, row["MainAB"]) : null;
                double issue = t.Elapsed.TotalMilliseconds;
                int polls = 0;
                while (open != null && !open.IsDone && t.Elapsed.TotalSeconds < 20) { h.Catalog.PumpOpens(); polls++; System.Threading.Thread.Sleep(1); }
                double opened = t.Elapsed.TotalMilliseconds;
                double edit = TimeOp(n, () => n.Ops.ChangeItem(ids, "appearance.chara.face.head", mod, User), out var res);
                Info($"UF5 cold mod head {mod}: async open issued in {F(issue)} ms, done after {F(opened)} ms ({polls} polls, edit mode: no frames; done {open?.IsDone}), then the edit (op + flush) {F(edit)} ms; ok {res?.Ok}");
                // the same head warm again
                n.Ops.ChangeItem(ids, "appearance.chara.face.head", "illusion.hs2#0", User);
                double warmMod = TimeOp(n, () => n.Ops.ChangeItem(ids, "appearance.chara.face.head", mod, User), out _);
                Info($"UF5 the mod head warm: {F(warmMod)} ms");
            }
        }
        h.ReleaseBundles();
    }

    static void UF6(MakerCoreTest.Harness h, int reps)
    {
        var lists = MakerCatalog.For(h.Catalog);
        using (var n = NodeOf(h, FemaleCard))
        {
            var ids = new[] { n.Id };
            int sex = n.Stored.Chara.Profile.Sex;
            var heads = lists.PresetHeads(sex).Select(r => r.Key.ToString()).ToList();
            string van = heads.FirstOrDefault(x => x.StartsWith("illusion.hs2#", StringComparison.Ordinal));
            // a mod head whose push changes the head (a preset asset that loads; 4.0's H4 heads)
            string mod = null;
            foreach (var x in heads.Where(x => !x.StartsWith("illusion.hs2#", StringComparison.Ordinal)).Take(40))
            {
                n.Ops.ChangeItem(ids, "appearance.chara.face.head", "illusion.hs2#1", User);
                var pr = n.Ops.ApplyFacePreset(ids, x, User);
                if (pr.Ok && n.Stored.Chara.Face.Head != "illusion.hs2#1") { mod = x; break; }
            }
            foreach (var (what, head) in new[] { ("vanilla", van), ("mod", mod) })
            {
                if (head == null) continue;
                var l = new List<double>();
                OpResult last = null;
                for (int i = 0; i < Math.Max(3, reps / 2); i++)
                {
                    n.Ops.ChangeItem(ids, "appearance.chara.face.head", "illusion.hs2#1", User);
                    l.Add(TimeOp(n, () => n.Ops.ApplyFacePreset(ids, head, User), out last));
                    if (last == null || !last.Ok || n.Stored.Chara.Face.Head == "illusion.hs2#1") break;   // no preset asset / no change
                }
                Info($"UF6 preset push {what} {head}: median {F(Median(l))} ms (op + flush incl. the head swap), last {last}");
                Target($"preset push ({what}, op + flush)", Median(l), 250);
            }
            // layout push (one composite; both creators for a paint, the colour creator for the mole)
            foreach (var path in new[] { "appearance.chara.face.mole.layout", "appearance.chara.face.makeup.paint[0].layout" })
            {
                var l = new List<double>();
                for (int i = 0; i < reps; i++)
                {
                    n.Ops.EditAppearance(ids, new List<(string, object)> { (path, new Vec4(0.1f, 0.2f, 0.3f, 0.4f)) }, User);
                    l.Add(TimeOp(n, () => n.Ops.ApplyLayoutPreset(ids, path, "illusion.hs2#" + (i % 4), User), out _));
                }
                Target($"layout push {path.Substring(path.LastIndexOf("face.", StringComparison.Ordinal))} (op + flush)", Median(l), 5);
            }
        }
        h.ReleaseBundles();
    }
}
