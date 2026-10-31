// Maker clothes tests of Phase 4.4 package A (the clothes units and runtime): rows MakerClothesTest, .render, .timing and
// RunS1Explain; measurements WU1–WU3.
//   Run (headless):
//     UM   the Unity-free unit map of the coordinate's member paths: the clothes / accessory members mark units and no
//          rebuild (a whole-coordinate path is a sex change, the one rebuild); the per-creator selection of the colour pass
//     WT1  colour sets: per member the creator values = the documents (base with alpha 1 unless useColorA0n, pattern colour,
//          layout lerp(20, 1, ·) / lerp(−1, 1, ·), rotation lerp(−1, 1, ·), gloss, metallic, the prefab's fourth colour and
//          uvScalePattern) on BR-Chan's clothes, a vanilla three-set top, a useColorA bottom with alpha 0.5 and an item without
//          useColorA with a stored alpha 0.5 (alpha 1 on the creator, the document kept); edit = fresh build
//     WT2  patterns: every vanilla 348 row on set 1 (row 0 → no texture), a loose-PNG pattern, the layouts and rotations
//          clamped at −0.5 / 1.5
//     WT3  an option-part toggle on a top in state "half" keeps it half (the stored state is re-applied after the part run);
//          with the diagnostic switch off the top shows dressed (the behaviour before the fix); = a fresh node in that state
//     WT4  per-creator rebuilds: gloss / metallic rebuild only the detail creators, base / pattern colour / pattern / layout /
//          rotation only the colour creators, a whole set both; the result equals a full pass and a fresh build
//     WT5  the break: _AlphaEx = 1 − rate on slot 0 of every break renderer only (other slots keep the prefab value, on a
//          multi-slot break item), the masks' _alpha_c after a top break, _alpha_d with a BreakDisableMask row, the kimono
//          240#223 (useBreak only in the redirected copy)
//     WT6  one-piece: a one-piece top skips the stored bottom (kept in the document), a two-piece top loads it again; the
//          same for the inner top / inner bottom
//     WT7  coordinate applies (an HS2 coordinate card, the three modes): one entry, the documents wear the source's half,
//          realized by per-part units (EditRebuilds unchanged, no rebuild requested), = a fresh node, undo; a source with a
//          KCOX overlay re-applies it
//     WT8  male: HERO107's four kinds from the male lists; a stored female-list inner top loads; a male top with options
//     WT9  ClothesReport facts of every vanilla row of 240–247 and 140 / 141 / 144 / 147 = the census (vanilla examples,
//          MainTex sizes) and the prefab defaults = PartDefaults.TryClothesColors; the vanilla rows with the largest
//          compositing work (the worst item of WU1)
//     WT10 the clothes digest sections (materials, creators, objects) change exactly when the edits say
//     WT11 an item swap of a skirt: the chains of the new part (roots, colliders, captured rest pose) = a fresh build's;
//          the animated case (a playing clip, simulated frames, the skirt bones turned as FK would) with and without the
//          restore before the release
//     WT12 ClothesInfo's facts cache: after an item change, an option toggle, a one-piece swap, a coordinate apply, an undo
//          and a redo the facts of every kind (asked before the change, so a stale entry would show) = the facts of a fresh
//          report; a repeated request comes from the cache; the gate checks of a first show (26) cold and warm, timed
//   RunRender (graphics): the creators' RT read-backs after WT1 / WT2 / WT4 edits = a fresh build's (and a full pass's);
//     a KCOX-grown top (a synthetic 2× overlay): the creator keeps its RT live at the grown size, edits = a fresh build,
//     a detail edit leaves the colour RT (with the overlay) alone, every creator material / RT destroyed after the release;
//     a 64² RT of the top rendered twice hashes equal.
//   RunTiming (graphics, its own Unity session): the rows of A (WU1–WU3) with the 1 × 1 copy fence.
//   RunS1Explain (headless, for T): S1 targets + 20 (or -all / -targets) vs the baselines with the redirect off: every
//     differing line classified (clothes.break: _AlphaEx on slots ≥ 1 of break renderers; clothes.state: clothes object
//     activity; me.shaderRegistry: the lines of MaterialEditor shader swaps, MaterialEditorShaderTest.RegistryClass) or printed.
// Run: -executeMethod MakerClothesTest.Run [-only UM,WT1]; .RunRender; .RunTiming [-reps N]; .RunS1Explain [-all|-targets]
//      [-only a.png,b.png] [-limit N] [-redirect] (keep the redirect on).
// Reports: Spike/out/maker/maker_clothes_report.txt, maker_clothes_render_report.txt, maker_clothes_timing_report.txt,
// maker_clothes_s1explain_report.txt. Content rule: numbers only (material values, transforms, chain states, hashes); nothing
// is rendered to a file or viewed; SFW targets; clothes appear as data names only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AIChara;
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
using UnityEngine.Rendering.Universal;
using static MakerTestKit;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class MakerClothesTest
{
    // rows (census "vanilla examples")
    const string V = "illusion.hs2#";
    const string Top3 = V + "17";            // 240 three colour sets
    const string TopBreak = V + "2";         // 240 useBreak, a two-piece top
    const string TopOnePiece = V + "1";      // 240 Coordinate 1
    const string TopOptions = V + "51";      // 240 option parts
    const string TopKimono = V + "223";      // 240 useBreak only in the redirected copy
    const string BottomAlpha = V + "9";      // 241 useColorA01
    const string BottomSkirt = V + "4";      // 241 useBreak, DynamicBones (skirt)
    const string BottomSkirt2 = V + "14";    // 241 DynamicBones (skirt)
    const string InnerOnePiece = V + "15";   // 242 Coordinate 1
    const string InnerTop = V + "1";         // 242
    const string MaleOptionTop = V + "201";  // 140 option parts, DynamicBones
    static readonly int[] KimonoRows = { 223, 235, 244 };
    static AnimeRef Dance => new AnimeRef(ContentIds.Vanilla, 11, 5, 0);   // Tools/targets.json animation "dance_00" (loop)

    static readonly string[] Kinds = UnitMap.ClothesKinds;
    static readonly string[] CP = { "_Color", "_Color2", "_Color3" }, PP = { "_Color1_2", "_Color2_2", "_Color3_2" };

    // ================================================================ helpers

    static MakerCoreTest.Built BuildOk(MakerCoreTest.Harness h, string file, string label)
    {
        var b = h.Build(file);
        Check(label + ": built", b.Ok, b.Error);
        return b;
    }

    static string Doc(int k) => "coord.clothes." + ClothesSet.Names[k];
    static bool HasCol(ListRow r, string col) => !string.IsNullOrEmpty(r[col]) && r[col] != "0";
    static ClothesPart PartOf(CharaContext ctx, int k) => ctx.Coord.Clothes.Parts[k];
    static int CatOf(CharaContext ctx, int k) => (ctx.IsMale ? ClothesSet.MaleCategories : ClothesSet.FemaleCategories)[k];

    static ListEntry RowOf(ContentCatalog cat, int category, string reference) =>
        ContentId.TryParse(reference, out var id) && !id.IsEmpty ? cat.GetEntry(category, id) : null;

    static bool Installed(MakerCoreTest.Harness h, int category, string reference, string what)
    {
        if (RowOf(h.Catalog, category, reference) != null) return true;
        Warn(what, $"row {category}:{reference} not installed: skipped");
        return false;
    }

    /// <summary>Sets kind k's item as the document (the kind's MaterialEditor items cleared as the op does; colours kept).</summary>
    static FlushReport SetItem(MakerCoreTest.Built live, int k, string reference, FlushMode mode = FlushMode.Normal)
    {
        var p = PartOf(live.Context, k);
        p.Item = reference; p.MaterialOverrides.Clear();
        live.Context.Editor.MarkDirty(Doc(k) + ".item");
        return live.Context.Editor.Flush(mode);
    }

    static FlushReport SetM(MakerCoreTest.Built live, string path, Action set, FlushMode mode = FlushMode.Normal)
    {
        set();
        live.Context.Editor.MarkDirty(path);
        return live.Context.Editor.Flush(mode);
    }

    static bool Near(Color a, Color b, float eps = 1e-6f) => Mathf.Abs(a.r - b.r) <= eps && Mathf.Abs(a.g - b.g) <= eps && Mathf.Abs(a.b - b.b) <= eps && Mathf.Abs(a.a - b.a) <= eps;
    static bool Same(Vector4 a, Vector4 b) => Bits(a.x) == Bits(b.x) && Bits(a.y) == Bits(b.y) && Bits(a.z) == Bits(b.z) && Bits(a.w) == Bits(b.w);
    static string Vs(Vector4 v) => $"({G(v.x)}, {G(v.y)}, {G(v.z)}, {G(v.w)})";
    static string Cs(Color c) => $"({G(c.r)}, {G(c.g)}, {G(c.b)}, {G(c.a)})";
    static Color U(Color4 c) => new Color(c.R, c.G, c.B, c.A);

    static string FirstDiff(string a, string b)
    {
        var la = (a ?? "").Split('\n'); var lb = (b ?? "").Split('\n');
        for (int i = 0; i < Math.Max(la.Length, lb.Length); i++)
        {
            string x = i < la.Length ? la[i] : "<end>", y = i < lb.Length ? lb[i] : "<end>";
            if (x != y) return $"line {i}: {Short(x, 160)} <> {Short(y, 160)}";
        }
        return "same";
    }

    /// <summary>The clothes digest texts of two characters compared (materials, creators, objects; the RT read-backs with
    /// <paramref name="rts"/> and a device); null when equal, else the differing sections.</summary>
    public static string ClothesDiff(CharaContext a, CharaContext b, bool rts = false)
    {
        var l = new List<string>();
        void T(string name, string x, string y) { if (x != y) l.Add(name + " (" + FirstDiff(x, y) + ")"); }
        T("materials", FaceDigest.MaterialsText(a, "clothes"), FaceDigest.MaterialsText(b, "clothes"));
        T("creators", FaceDigest.CreatorsText(a, "clothes"), FaceDigest.CreatorsText(b, "clothes"));
        T("objects", FaceDigest.ObjectsText(a, "clothes"), FaceDigest.ObjectsText(b, "clothes"));
        T("chains", ClothesChainsText(a), ClothesChainsText(b));
        if (rts && FaceDigest.HasDevice) T("rts", FaceDigest.RtsText(a, "clothes"), FaceDigest.RtsText(b, "clothes"));
        return l.Count == 0 ? null : string.Join(", ", l);
    }

    /// <summary>The live character = a fresh build of its documents: the snapshot and the clothes digest texts (with
    /// <paramref name="rts"/> the creators' RT read-backs too).</summary>
    static bool FreshOk(string label, MakerCoreTest.Harness h, MakerCoreTest.Built live, bool rts = false)
    {
        string cd = null;
        var d = VsFresh(h, live, out string err, (a, b) => cd = ClothesDiff(a.Context, b.Context, rts));
        return Check(label + ": = a fresh build (snapshot, clothes digest" + (rts ? " with the RT read-backs" : "") + ")", d != null && d.Equal && cd == null,
                     d == null ? err : (d.Equal ? "" : d.ToString()) + (cd != null ? "; clothes digest differs: " + cd : ""));
    }

    /// <summary>The clothes chains of the dynamic-bone stage: per chain (stage order) kind, root and owner names, active,
    /// particle count, the colliders' paths and per particle the transform name and the captured rest local pose as float
    /// bits. Empty without a stage.</summary>
    public static string ClothesChainsText(CharaContext ctx)
    {
        var sb = new StringBuilder();
        var stage = ctx?.Motion != null ? ctx.Motion.Get<DynamicBoneStage>() : null;
        if (stage == null) return "dynbone none\n";
        foreach (var c in stage.Chains.Where(c => c != null && c.Category == DynamicBoneCategory.Clothes))
        {
            sb.Append("chain clothes").Append(c.Kind).Append(" root ").Append(c.RootName).Append(" owner ").Append(c.Owner ? c.Owner.name : "-")
              .Append(" skirt ").Append(c.IsSkirt ? 1 : 0).Append(" particles ").Append(c.ParticleCount).Append(" colliders ");
            for (int k = 0; k < c.ColliderCount; k++) sb.Append(k == 0 ? "" : ",").Append(stage.Colliders.Path(c.ColliderIndex(k)));
            sb.Append('\n');
            for (int i = 0; i < c.ParticleCount; i++)
            {
                var t = c.ParticleTransform(i);
                var p = c.RestLocalPosition(i); var q = c.RestLocalRotation(i);
                sb.Append(" p").Append(i).Append(' ').Append(t ? t.name : "tip").Append(' ').Append(Bits(p.x).ToString("x8")).Append(' ').Append(Bits(p.y).ToString("x8")).Append(' ').Append(Bits(p.z).ToString("x8"))
                  .Append(' ').Append(Bits(q.x).ToString("x8")).Append(' ').Append(Bits(q.y).ToString("x8")).Append(' ').Append(Bits(q.z).ToString("x8")).Append(' ').Append(Bits(q.w).ToString("x8")).Append('\n');
            }
        }
        return sb.ToString();
    }

    /// <summary>Every creator of kind k holds the values for the documents: per colour set the main colour (alpha 1
    /// unless useColorA0n), the pattern colour, the layout and rotation lerps, the pattern texture (the 348 row's MainTex as
    /// the catalog serves it; none → null), gloss and metallic on the detail creator; the prefab's fourth colour and
    /// uvScalePattern.</summary>
    static bool CreatorsAsDocument(ContentCatalog cat, CharaContext ctx, int k, out string detail)
    {
        var x = ctx.Edit; var go = ctx.Clothes[k]; var cmp = go ? go.GetComponent<CmpClothes>() : null;
        var part = PartOf(ctx, k);
        if (cmp == null) { detail = "no CmpClothes"; return false; }
        var useA = new[] { cmp.useColorA01, cmp.useColorA02, cmp.useColorA03 };
        var bad = new List<string>();
        int groups = 0;
        for (int j = 0; j < 3; j++)
        {
            var ci = x.ClothesColor[k, j]; var di = x.ClothesGloss[k, j];
            if (ci == null && di == null) continue;
            groups++;
            if (ci == null || di == null) { bad.Add($"group {j}: a creator missing"); continue; }
            var cm = ci.Creator.Mat; var dm = di.Creator.Mat;
            for (int c = 0; c < 3; c++)
            {
                var cc = part.Colors[c];
                var bc = new Color(cc.Base.R, cc.Base.G, cc.Base.B, useA[c] != 0 ? cc.Base.A : 1f);
                if (!Near(cm.GetColor(CP[c]), bc)) bad.Add($"g{j} {CP[c]} {Cs(cm.GetColor(CP[c]))} want {Cs(bc)}");
                if (!Near(cm.GetColor(PP[c]), U(cc.PatternColor))) bad.Add($"g{j} {PP[c]} {Cs(cm.GetColor(PP[c]))} want {Cs(U(cc.PatternColor))}");
                var uv = new Vector4(Mathf.Lerp(20, 1, cc.Layout.X), Mathf.Lerp(20, 1, cc.Layout.Y), Mathf.Lerp(-1, 1, cc.Layout.Z), Mathf.Lerp(-1, 1, cc.Layout.W));
                string uvp = "_patternuv" + (c + 1);
                if (!Same(cm.GetVector(uvp), uv)) bad.Add($"g{j} {uvp} {Vs(cm.GetVector(uvp))} want {Vs(uv)}");
                float rot = Mathf.Lerp(-1, 1, cc.Rotation);
                if (Bits(cm.GetFloat(uvp + "Rotator")) != Bits(rot)) bad.Add($"g{j} {uvp}Rotator {G(cm.GetFloat(uvp + "Rotator"))} want {G(rot)}");
                string sfx = c == 0 ? "" : (c + 1).ToString(CultureInfo.InvariantCulture);
                if (Bits(dm.GetFloat("_Glossiness" + sfx)) != Bits(cc.Gloss)) bad.Add($"g{j} _Glossiness{sfx} {G(dm.GetFloat("_Glossiness" + sfx))} want {G(cc.Gloss)}");
                if (Bits(dm.GetFloat("_Metallic" + sfx)) != Bits(cc.Metallic)) bad.Add($"g{j} _Metallic{sfx} {G(dm.GetFloat("_Metallic" + sfx))} want {G(cc.Metallic)}");
                var prow = RowOf(cat, 348, cc.Pattern);
                var want = prow != null ? cat.LoadAsset<Texture2D>(prow, "MainTexAB", "MainTex") : null;
                var got = cm.GetTexture("_PatternMask" + (c + 1));
                if (got != want) bad.Add($"g{j} _PatternMask{c + 1} {(got ? got.name : "null")} want {(want ? want.name : "null")}");
            }
            var uvs = cmp.uvScalePattern == Vector4.zero ? new Vector4(1, 1, 0, 0) : cmp.uvScalePattern;
            if (!Same(cm.GetVector("_UVScalePattern"), uvs)) bad.Add($"g{j} _UVScalePattern {Vs(cm.GetVector("_UVScalePattern"))} want {Vs(uvs)}");
            if (!Near(cm.GetColor("_Color4"), cmp.defMainColor04)) bad.Add($"g{j} _Color4");
            if (Bits(dm.GetFloat("_Glossiness4")) != Bits(cmp.defGloss04) || Bits(dm.GetFloat("_Metallic4")) != Bits(cmp.defMetallic04)) bad.Add($"g{j} _Glossiness4 / _Metallic4");
        }
        detail = $"{groups} groups" + (bad.Count > 0 ? "; " + bad.Count + " differ: " + string.Join("; ", bad.Take(6)) : "");
        return groups > 0 && bad.Count == 0;
    }

    /// <summary>The names of kind k's creators (colour, detail) by group.</summary>
    static List<string> CreatorNames(CharaContext ctx, int k, bool colour, bool detail)
    {
        var l = new List<string>();
        for (int j = 0; j < 3; j++)
        {
            if (colour && ctx.Edit.ClothesColor[k, j] != null) l.Add(ctx.Edit.ClothesColor[k, j].Name);
            if (detail && ctx.Edit.ClothesGloss[k, j] != null) l.Add(ctx.Edit.ClothesGloss[k, j].Name);
        }
        return l;
    }

    static int Groups(CharaContext ctx, int k) => Enumerable.Range(0, 3).Count(j => ctx.Edit.ClothesColor[k, j] != null);

    /// <summary>The first vanilla row of the Maker list of kind k (the character's sex) whose realized report passes
    /// <paramref name="ok"/> (each candidate is set on the live character; the item is left on the found row, else back on
    /// the first). Null when none.</summary>
    static string FindRow(MakerCoreTest.Harness h, MakerCoreTest.Built live, int k, Func<ListRow, bool> rowOk, Func<ClothesReport.KindReport, bool> ok, int cap = 400)
    {
        var ctx = live.Context;
        string keep = PartOf(ctx, k).Item;
        int sex = ctx.IsMale ? 0 : 1, n = 0;
        foreach (var r in MakerCatalog.For(h.Catalog).Rows(CatOf(ctx, k), sex))
        {
            if (!r.IsVanilla || r["MainData"] == "p_dummy" || (rowOk != null && !rowOk(r))) continue;
            if (++n > cap) break;
            SetItem(live, k, r.Key.ToString());
            var rep = ClothesReport.KindOf(ctx, k);
            if (rep != null && rep.HasCmpClothes && ok(rep)) return r.Key.ToString();
        }
        SetItem(live, k, keep);
        return null;
    }

    // ================================================================ Run (headless)

    public static void Run()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        Rep.AppendLine($"maker clothes (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}); redirect {RedirectState()}");
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        try
        {
            if (Want("UM")) Guard("UM unit map of the coordinate's members (Unity-free)", UM);
            using (var h = MakerCoreTest.Harness.Open())
            {
                Info("redirect after the catalog's settings: " + RedirectState(h.Catalog));
                if (Want("WT1")) Guard("WT1 colour sets", () => WT1(h));
                if (Want("WT2")) Guard("WT2 patterns", () => WT2(h));
                if (Want("WT3")) Guard("WT3 option toggle in state half (GW3)", () => WT3(h));
                if (Want("WT4")) Guard("WT4 per-creator rebuilds (GW4)", () => WT4(h));
                if (Want("WT5")) Guard("WT5 break (GW6) and masks", () => WT5(h));
                if (Want("WT6")) Guard("WT6 one-piece", () => WT6(h));
                if (Want("WT7")) Guard("WT7 coordinate apply (three modes)", () => WT7(h));
                if (Want("WT8")) Guard("WT8 male", () => WT8(h));
                if (Want("WT9")) Guard("WT9 facts = census", () => WT9(h));
                if (Want("WT10")) Guard("WT10 digest sections", () => WT10(h));
                if (Want("WT11")) Guard("WT11 item swap with colliders, animated", () => WT11(h));
                if (Want("WT12")) Guard("WT12 the facts cache of ClothesInfo", () => WT12(h));
            }
        }
        finally
        {
            FaceSetup.Seed = fseed;
            ClothesPartUnit.RestoreChainsBeforeRelease = true;
            CharaStateRealizer.ReapplyAfterObjectsReset = true;
        }
        Finish("maker_clothes_report.txt", "MakerClothesTest UM, WT1-WT11", sw);
    }

    /// <summary>"on" / "off" / "absent": the catalog's per-asset redirect switch (ContentCatalog.SideloaderAssetRedirect)
    /// as read by reflection (the switch arrives with package R).</summary>
    static string RedirectState(ContentCatalog cat = null)
    {
        var v = RedirectSwitch(cat);
        return v == null ? "absent" : v.Value ? "on" : "off";
    }

    static bool? RedirectSwitch(ContentCatalog cat, bool? set = null)
    {
        var t = typeof(ContentCatalog);
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var fi = t.GetField("SideloaderAssetRedirect", F);
        var pi = fi == null ? t.GetProperty("SideloaderAssetRedirect", F) : null;
        bool isStatic = fi != null ? fi.IsStatic : pi?.GetGetMethod(true)?.IsStatic ?? false;
        object target = isStatic ? null : cat;
        if (fi == null && pi == null || (!isStatic && cat == null)) return null;
        if (set != null)
        {
            if (fi != null) fi.SetValue(target, set.Value);
            else if (pi.CanWrite) pi.SetValue(target, set.Value);
        }
        object v = fi != null ? fi.GetValue(target) : pi.GetValue(target);
        return v is bool b ? b : (bool?)null;
    }

    // ---------------------------------------------------------------- UM

    static void UM()
    {
        HashSet<string> Map(string path, out bool rebuild, out string why, IEnumerable<string> ids = null)
        {
            var u = new HashSet<string>(StringComparer.Ordinal);
            rebuild = UnitMap.Map(path, u, out why, ids);
            return u;
        }
        string J(IEnumerable<string> s) => string.Join(", ", s.OrderBy(x => x, StringComparer.Ordinal));
        var all = new HashSet<string>(StringComparer.Ordinal) { UnitMap.Masks };
        for (int k = 0; k < 8; k++) { all.Add(UnitMap.ClothesUnits[k]); all.Add(ClothesNames.ColorsUnit(k)); all.Add(ClothesNames.BreakUnit(k)); }
        var u0 = Map("appearance.coord.clothes", out bool r0, out string w0);
        Check("UM appearance.coord.clothes: the eight part units, their colour and break passes and the masks; no rebuild", !r0 && all.SetEquals(u0), J(u0) + (r0 ? " rebuild " + w0 : ""));
        var u1 = Map("appearance.coord.clothes.top", out bool r1, out _);
        Check("UM clothes.top: part, colours, break, masks", !r1 && u1.SetEquals(new[] { "clothes.top", "clothes.top#colors", "clothes.top#break", UnitMap.Masks }), J(u1));
        var u2 = Map("appearance.coord.clothes.top.colors[0].gloss", out bool r2, out _);
        Check("UM clothes.top.colors[0].gloss: the colour pass only", !r2 && u2.SetEquals(new[] { "clothes.top#colors" }), J(u2));
        var u3 = Map("appearance.coord.clothes.bottom.breakRate", out bool r3, out _);
        Check("UM clothes.bottom.breakRate: the break pass and the masks", !r3 && u3.SetEquals(new[] { "clothes.bottom#break", UnitMap.Masks }), J(u3));
        var u4 = Map("appearance.coord.clothes.top.hideOptions[1]", out bool r4, out _);
        Check("UM clothes.top.hideOptions[1]: the part unit only", !r4 && u4.SetEquals(new[] { "clothes.top" }), J(u4));
        var ids = new[] { "a1", "b2" };
        var u5 = Map("appearance.coord.accessories", out bool r5, out _, ids);
        Check("UM appearance.coord.accessories: the slot table and every accessory; no rebuild", !r5 && u5.SetEquals(new[] { UnitMap.AccList, UnitMap.Accessory("a1"), UnitMap.Accessory("b2") }), J(u5));
        var u6 = Map("appearance.coord.accessories[\"a1\"].item", out bool r6, out _, ids);
        Check("UM accessories[\"a1\"].item: that accessory", !r6 && u6.SetEquals(new[] { UnitMap.Accessory("a1") }), J(u6));
        var u7 = Map("appearance.coord.name", out bool r7, out _);
        Check("UM appearance.coord.name: stored only (no unit, no rebuild)", !r7 && u7.Count == 0, J(u7));
        var u8 = Map("appearance.coord.ruleOverrides", out bool r8, out _);
        Check("UM appearance.coord.ruleOverrides: the plugin final stage", !r8 && u8.SetEquals(new[] { UnitMap.ExtFinal }), J(u8));
        Map("appearance.coord", out bool r9, out string w9);
        Check("UM appearance.coord (whole): a rebuild (coord:sex) — a coordinate apply must notify the members, not the whole document", r9 && w9 == "coord:sex", w9);
        // the per-creator selection of the colour pass
        int okC = 0, okD = 0, n = 0;
        foreach (var m in UnitMap.ClothesColourMembers)
            for (int c = 0; c < 3; c++) { n++; UnitMap.ClothesCreators(0, new[] { "clothes.top.colors[" + c + "]." + m }, false, out bool col, out bool det); if (col && !det) okC++; }
        Check("UM ClothesCreators: base, patternColor, pattern, layout, rotation of each set → the colour creators only", okC == n, $"{okC} of {n}");
        n = 0;
        foreach (var m in UnitMap.ClothesDetailMembers)
            for (int c = 0; c < 3; c++) { n++; UnitMap.ClothesCreators(0, new[] { "clothes.top.colors[" + c + "]." + m }, false, out bool col, out bool det); if (!col && det) okD++; }
        Check("UM ClothesCreators: gloss, metallic → the detail creators only", okD == n, $"{okD} of {n}");
        bool Both(int k, string[] p, bool a) { UnitMap.ClothesCreators(k, p, a, out bool c, out bool d); return c && d; }
        bool Neither(int k, string[] p) { UnitMap.ClothesCreators(k, p, false, out bool c, out bool d); return !c && !d; }
        Check("UM ClothesCreators: a whole set, the colours, the kind, all, no paths → both; another kind's member → neither",
              Both(0, new[] { "clothes.top.colors[2]" }, false) && Both(0, new[] { "clothes.top.colors" }, false) && Both(0, new[] { "clothes.top" }, false)
              && Both(0, new[] { "clothes.top.colors[0].gloss" }, true) && Both(0, new string[0], false) && Both(0, null, false)
              && Neither(0, new[] { "clothes.bottom.colors[0].base" }) && Both(1, new[] { "clothes.bottom.colors[0].base", "clothes.bottom.colors[1].metallic" }, false));
    }

    // ---------------------------------------------------------------- WT1

    static void WT1(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            Info("BR-Chan clothes: " + ClothesReport.Of(ctx));
            int withCreators = 0;
            for (int k = 0; k < 8; k++)
            {
                if (Groups(ctx, k) == 0) continue;
                withCreators++;
                Check($"WT1 BR-Chan {Kinds[k]} as built: creator values = the documents", CreatorsAsDocument(h.Catalog, ctx, k, out string d), d);
            }
            Check("WT1 BR-Chan has clothes with creators", withCreators > 0, withCreators + " kinds");
            // every member of the three sets of the first kind with creators
            int kk = Enumerable.Range(0, 8).FirstOrDefault(k => Groups(ctx, k) > 0);
            EditAllMembers(h, live, kk, "BR-Chan " + Kinds[kk]);
            FreshOk("WT1 BR-Chan after the colour edits", h, live);
            // a vanilla top with three colour sets
            if (Installed(h, 240, Top3, "WT1 three-set top"))
            {
                SetItem(live, 0, Top3);
                var rep = ClothesReport.KindOf(ctx, 0);
                Check("WT1 240#17: three colour sets shown", rep.SetShown(1) && rep.SetShown(2) && rep.SetShown(3), rep.ToString());
                EditAllMembers(h, live, 0, "240#17");
                FreshOk("WT1 240#17 after the edits", h, live);
            }
            // the main colour's alpha: a useColorA01 bottom keeps alpha 0.5 on the creator; an item without useColorA writes 1
            if (Installed(h, 241, BottomAlpha, "WT1 useColorA bottom"))
            {
                Doc1Top(live);
                SetItem(live, 1, BottomAlpha);
                var rep = ClothesReport.KindOf(ctx, 1);
                var p = PartOf(ctx, 1);
                SetM(live, Doc(1) + ".colors[0].base", () => p.Colors[0].Base = new Color4(0.8f, 0.3f, 0.2f, 0.5f));
                var cm = ctx.Edit.ClothesColor[1, 0]?.Creator.Mat;
                Check("WT1 241#9 (useColorA01): the creator's _Color alpha = the stored 0.5", rep.AlphaShown(1) && cm && Mathf.Abs(cm.GetColor("_Color").a - 0.5f) < 1e-6f,
                      $"alpha shown {rep.AlphaShown(1)}, _Color {(cm ? Cs(cm.GetColor("_Color")) : "no creator")}");
                FreshOk("WT1 241#9 with alpha 0.5", h, live);
            }
            if (Installed(h, 240, TopBreak, "WT1 top without useColorA"))
            {
                SetItem(live, 0, TopBreak);
                var rep = ClothesReport.KindOf(ctx, 0);
                var p = PartOf(ctx, 0);
                SetM(live, Doc(0) + ".colors[0].base", () => p.Colors[0].Base = new Color4(0.1f, 0.5f, 0.9f, 0.5f));
                var cm = ctx.Edit.ClothesColor[0, 0]?.Creator.Mat;
                Check("WT1 240#2 (no useColorA01): the creator's _Color alpha 1, the document keeps 0.5", !rep.AlphaShown(1) && cm && cm.GetColor("_Color").a == 1f && p.Colors[0].Base.A == 0.5f,
                      $"alpha shown {rep.AlphaShown(1)}, _Color {(cm ? Cs(cm.GetColor("_Color")) : "no creator")}, stored {G(p.Colors[0].Base.A)}");
                FreshOk("WT1 240#2 with a stored alpha 0.5", h, live);
            }
        }
        h.ReleaseBundles();
    }

    /// <summary>Makes sure the top is a two-piece (a one-piece top would skip the bottom): sets 240#2 when it is not.</summary>
    static bool Doc1Top(MakerCoreTest.Built live)
    {
        var r = ClothesReport.KindOf(live.Context, 0);
        if (r != null && r.OnePiece) { SetItem(live, 0, TopBreak); return true; }
        return false;
    }

    /// <summary>Every member of the three colour sets of kind k set to new values (one flush each), the creator values
    /// checked after each set; values outside 0..1 for gloss / metallic (stored unclamped).</summary>
    static void EditAllMembers(MakerCoreTest.Harness h, MakerCoreTest.Built live, int k, string what)
    {
        var ctx = live.Context;
        var p = PartOf(ctx, k);
        int sets = 0, ok = 0;
        var bad = new List<string>();
        for (int c = 0; c < 3; c++)
        {
            string pre = Doc(k) + ".colors[" + c + "].";
            var cc = p.Colors[c];
            var steps = new (string member, Action set)[]
            {
                ("base", () => cc.Base = new Color4(0.21f + 0.1f * c, 0.62f, 0.33f, 0.7f)),
                ("patternColor", () => cc.PatternColor = new Color4(0.9f, 0.15f * c, 0.4f, 0.6f)),
                ("layout", () => cc.Layout = new Vec4(0.25f, 0.75f, 0.4f, 0.6f)),
                ("rotation", () => cc.Rotation = 0.3f + 0.1f * c),
                ("gloss", () => cc.Gloss = 1.5f),
                ("metallic", () => cc.Metallic = -0.5f),
            };
            foreach (var (member, set) in steps)
            {
                sets++;
                var r = SetM(live, pre + member, set);
                if (CreatorsAsDocument(h.Catalog, ctx, k, out string d) && r.Errors.Count == 0 && !r.ClothesSwapped[k]) ok++;
                else bad.Add($"set {c} {member}: {d}{(r.Errors.Count > 0 ? " errors " + string.Join("; ", r.Errors) : "")}");
            }
        }
        Check($"WT1 {what}: every member of the three sets (base, pattern colour, layout, rotation, gloss 1.5, metallic −0.5) → creator values = the documents, no swap",
              ok == sets, $"{ok} of {sets}" + (bad.Count > 0 ? "; " + Short(string.Join(" | ", bad), 1200) : ""));
    }

    // ---------------------------------------------------------------- WT2

    static void WT2(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            if (Installed(h, 240, Top3, "WT2 three-set top")) SetItem(live, 0, Top3);
            if (Groups(ctx, 0) == 0) { Check("WT2 the top has creators", false, ClothesReport.KindOf(ctx, 0).ToString()); return; }
            var p = PartOf(ctx, 0);
            var rows = h.Catalog.EntriesOf(348).Where(e => e.IsVanilla).ToList();
            int ok = 0, n = 0, colourOnly = 0;
            var bad = new List<string>();
            foreach (var r in rows)
            {
                n++;
                string reference = r.Key.ToString();
                var fr = SetM(live, Doc(0) + ".colors[0].pattern", () => p.Colors[0].Pattern = reference);
                var tex = ctx.Edit.ClothesColor[0, 0].Creator.Mat.GetTexture("_PatternMask1");
                bool none = r.Key.Id == 0 || !HasTex(r, "MainTex");
                bool texOk = none ? tex == null : tex && tex.name == r["MainTex"];
                if (texOk && CreatorsAsDocument(h.Catalog, ctx, 0, out string d) && !fr.ClothesSwapped[0]) ok++;
                else bad.Add($"{reference}: _PatternMask1 {(tex ? tex.name : "null")} (row MainTex {r["MainTex"]})");
                if (fr.ClothesCreatorsRebuilt.All(c => c.Kind == 0 && c.Colour && !c.Detail) && fr.ClothesCreatorsRebuilt.Count > 0) colourOnly++;
            }
            Check("WT2 every vanilla 348 row on set 1: _PatternMask1 = the row's MainTex (row 0 → null), creator values = the documents, no swap",
                  n > 0 && ok == n, $"{ok} of {n}" + (bad.Count > 0 ? "; " + Short(string.Join(" | ", bad), 1200) : ""));
            Check("WT2 a pattern pick rebuilds the colour creators only", colourOnly == n, $"{colourOnly} of {n}");
            // a Sideloader loose-PNG pattern (a mod 348 row whose texture is a loose PNG of its zipmod)
            var loose = h.Catalog.EntriesOf(348).FirstOrDefault(e => !e.IsVanilla && HasTex(e, "MainTex") && h.Catalog.Loose.TryFind(e["MainTexAB"], e["MainTex"], out _));
            if (loose == null) Warn("WT2 loose-PNG pattern", "no 348 row served as a loose PNG found");
            else
            {
                SetM(live, Doc(0) + ".colors[1].pattern", () => p.Colors[1].Pattern = loose.Key.ToString());
                var want = h.Catalog.LoadAsset<Texture2D>(loose, "MainTexAB", "MainTex");
                var tex = ctx.Edit.ClothesColor[0, 0].Creator.Mat.GetTexture("_PatternMask2");
                Check($"WT2 loose-PNG pattern {loose.Key} on set 2: _PatternMask2 = the loose texture the catalog serves", want && tex == want, $"{(tex ? tex.name + " " + tex.width + "x" + tex.height : "null")}");
            }
            // clamped lerps of the layout and the rotation
            SetM(live, Doc(0) + ".colors[0].pattern", () => p.Colors[0].Pattern = V + "5");
            SetM(live, Doc(0) + ".colors[0].layout", () => p.Colors[0].Layout = new Vec4(-0.5f, 1.5f, 1.5f, -0.5f));
            SetM(live, Doc(0) + ".colors[0].rotation", () => p.Colors[0].Rotation = 1.5f);
            var cm = ctx.Edit.ClothesColor[0, 0].Creator.Mat;
            Check("WT2 layout (−0.5, 1.5, 1.5, −0.5) → _patternuv1 (20, 1, 1, −1); rotation 1.5 → _patternuv1Rotator 1 (clamped lerps)",
                  Same(cm.GetVector("_patternuv1"), new Vector4(20, 1, 1, -1)) && cm.GetFloat("_patternuv1Rotator") == 1f, Vs(cm.GetVector("_patternuv1")) + " rot " + G(cm.GetFloat("_patternuv1Rotator")));
            SetM(live, Doc(0) + ".colors[0].rotation", () => p.Colors[0].Rotation = -0.5f);
            Check("WT2 rotation −0.5 → _patternuv1Rotator −1", cm.GetFloat("_patternuv1Rotator") == -1f, G(cm.GetFloat("_patternuv1Rotator")));
            FreshOk("WT2 after the pattern edits", h, live);
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- WT3

    static string ObjectsOf(CharaContext ctx, int k)
    {
        var o = ClothesReport.KindOf(ctx, k)?.Objects;
        if (o == null) return "-";
        string B(bool? b) => b == null ? "-" : b.Value ? "1" : "0";
        return $"active {(o.Active ? 1 : 0)} topDef {B(o.TopDef)} topHalf {B(o.TopHalf)} botDef {B(o.BotDef)} botHalf {B(o.BotHalf)} opt1 {string.Join(",", o.Opt01.Select(a => a ? 1 : 0))} opt2 {string.Join(",", o.Opt02.Select(a => a ? 1 : 0))}";
    }

    static void WT3(MakerCoreTest.Harness h)
    {
        // a vanilla top with option parts and three states (StateType 0) whose half state changes an object
        string row = null;
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan (row search)")))
        {
            if (!bb.B.Ok) return;
            bool Fits(ClothesReport.KindReport r) => r.Opt01 > 0 && r.StateType == "0" && (r.HasTopHalf || r.HasTopDef);
            if (RowOf(h.Catalog, 240, TopOptions) != null)
            {
                SetItem(bb.B, 0, TopOptions);
                if (Fits(ClothesReport.KindOf(bb.B.Context, 0))) row = TopOptions;
            }
            row ??= FindRow(h, bb.B, 0, e => e["StateType"] == "0", Fits);
        }
        if (!Check("WT3 a vanilla top with option parts, three states and a def / half object", row != null, row ?? "none")) return;
        Info("WT3 top " + row);
        var c = MakerEditTest.Import(h, FemaleCard, out string err);
        if (!Check("WT3 BR-Chan imported", c != null, err)) return;
        var n = MakerEditTest.NewNode(h, c, "BR-Chan");
        try
        {
            if (!Check("WT3 node built", n.Ok, n.Error)) return;
            var ids = new[] { n.Id };
            var r0 = n.Ops.ChangeItem(ids, "appearance.coord.clothes.top.item", row, User); n.Apply();
            Check("WT3 the top changed through the op", r0.Ok, r0.ToString());
            var rs = n.Ops.SetClothes(ids, 0, 1, false, User); n.Apply();
            var real = n.Real;
            Check("WT3 the top's state realized as half", rs.Ok && real.StateRealizer.Realized[0] == 1, "realized " + string.Join(",", real.StateRealizer.Realized));
            bool HalfShown()
            {
                var o = ClothesReport.KindOf(real.Context, 0).Objects;
                return (o.TopDef == null || o.TopDef == false) && (o.TopHalf == null || o.TopHalf == true) && (o.TopDef != null || o.TopHalf != null);
            }
            Check("WT3 in state half: objTopDef inactive, objTopHalf active", HalfShown(), ObjectsOf(real.Context, 0));
            foreach (bool fix in new[] { false, true })
            {
                CharaStateRealizer.ReapplyAfterObjectsReset = fix;
                string tag = fix ? "with the fix" : "diagnostic switch off";
                var e1 = n.Ops.EditAppearance(ids, new List<(string, object)> { ("appearance.coord.clothes.top.hideOptions[0]", true) }, User); n.Apply();
                var fr = real.LastEditFlush;
                var rep = ClothesReport.KindOf(real.Context, 0);
                bool optsOff = rep.Objects.Opt01.Length > 0 && rep.Objects.Opt01.All(a => !a);
                Check($"WT3 ({tag}) option 1 hidden: one part run without a swap, reported as objects reset", e1.Ok && fr != null && fr.ClothesObjectsReset[0] && !fr.ClothesSwapped[0] && optsOff,
                      $"op {e1}; objectsReset {fr?.ClothesObjectsReset[0]}, swapped {fr?.ClothesSwapped[0]}, {ObjectsOf(real.Context, 0)}");
                if (fix) Check("WT3 the top stays half after the toggle (the stored state re-applied)", HalfShown(), ObjectsOf(real.Context, 0));
                else Check("WT3 without the re-application the top shows dressed after the toggle (the gap the fix closes)", !HalfShown(), ObjectsOf(real.Context, 0));
                var e2 = n.Ops.EditAppearance(ids, new List<(string, object)> { ("appearance.coord.clothes.top.hideOptions[0]", false) }, User); n.Apply();
                if (fix) Check("WT3 option 1 shown again: still half, the options active", e2.Ok && HalfShown() && ClothesReport.KindOf(real.Context, 0).Objects.Opt01.All(a => a), ObjectsOf(real.Context, 0));
                else { n.Ops.SetClothes(ids, 0, 0, false, User); n.Apply(); n.Ops.SetClothes(ids, 0, 1, false, User); n.Apply(); }
            }
            n.Ops.EditAppearance(ids, new List<(string, object)> { ("appearance.coord.clothes.top.hideOptions[0]", true) }, User); n.Apply();
            using (var fresh = MakerEditTest.NewNode(h, n.Session.Model.File.ExtractCharacter(n.Id), "BR-Chan (fresh)"))
            {
                if (!Check("WT3 fresh node built", fresh.Ok, fresh.Error)) return;
                fresh.Ops.SetClothes(new[] { fresh.Id }, 0, 1, false, User); fresh.Apply();
                var d = CharaSnapshot.Compare(fresh.Snapshot(), n.Snapshot(), 12);
                string od = ClothesDiff(fresh.Real.Context, real.Context);
                Check("WT3 = a fresh node in state half with option 1 hidden (snapshot, clothes digest)", d.Equal && od == null, (d.Equal ? "" : d.ToString()) + (od != null ? "; " + od : ""));
            }
        }
        finally { CharaStateRealizer.ReapplyAfterObjectsReset = true; n.Dispose(); }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- WT4

    static void WT4(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            string row = FindRow(h, live, 0, r => HasCol(r, "MainTex02") && HasCol(r, "ColorMask02Tex"), r => r.Groups[1].HasCreators);
            if (row == null) Warn("WT4 two-group top", "none found; the first top with creators is used");
            if (Groups(ctx, 0) == 0) FindRow(h, live, 0, null, r => r.Groups[0].HasCreators);
            var p = PartOf(ctx, 0);
            Info($"WT4 top {p.Item}: {Groups(ctx, 0)} colour groups with creators");
            var colourNames = CreatorNames(ctx, 0, true, false); var detailNames = CreatorNames(ctx, 0, false, true);
            if (!Check("WT4 the top has creators", colourNames.Count > 0 && detailNames.Count == colourNames.Count)) return;
            var cases = new (string member, Action set, bool colour)[]
            {
                ("colors[0].gloss", () => p.Colors[0].Gloss = 0.31f, false),
                ("colors[1].metallic", () => p.Colors[1].Metallic = 0.77f, false),
                ("colors[2].gloss", () => p.Colors[2].Gloss = 1.25f, false),
                ("colors[0].base", () => p.Colors[0].Base = new Color4(0.4f, 0.5f, 0.6f, 1), true),
                ("colors[2].patternColor", () => p.Colors[2].PatternColor = new Color4(0.1f, 0.9f, 0.2f, 0.8f), true),
                ("colors[0].pattern", () => p.Colors[0].Pattern = V + "3", true),
                ("colors[1].layout", () => p.Colors[1].Layout = new Vec4(0.6f, 0.4f, 0.3f, 0.2f), true),
                ("colors[2].rotation", () => p.Colors[2].Rotation = 0.8f, true),
            };
            foreach (var (m, set, colour) in cases)
            {
                var fr = SetM(live, Doc(0) + "." + m, set);
                var want = colour ? colourNames : detailNames;
                bool namesOk = fr.CreatorsRebuilt.SequenceEqual(want);
                bool runsOk = fr.ClothesCreatorsRebuilt.Count == want.Count && fr.ClothesCreatorsRebuilt.All(r => r.Kind == 0 && r.Colour == colour && r.Detail == !colour);
                Check($"WT4 {m}: only the {(colour ? "colour" : "detail")} creators rebuilt, values = the documents", namesOk && runsOk && CreatorsAsDocument(h.Catalog, ctx, 0, out string d),
                      $"rebuilt [{string.Join(", ", fr.CreatorsRebuilt)}]; runs [{string.Join(", ", fr.ClothesCreatorsRebuilt)}]");
            }
            var fr2 = SetM(live, Doc(0) + ".colors[1]", () => { p.Colors[1].Gloss = 0.2f; p.Colors[1].Base = new Color4(0.3f, 0.3f, 0.9f, 1); });
            Check("WT4 a whole set: both creators of every group", fr2.CreatorsRebuilt.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(colourNames.Concat(detailNames).OrderBy(x => x, StringComparer.Ordinal))
                  && fr2.ClothesCreatorsRebuilt.All(r => r.Colour && r.Detail), "rebuilt " + string.Join(", ", fr2.CreatorsRebuilt));
            string cre = FaceDigest.CreatorsText(ctx, "clothes"), mats = FaceDigest.MaterialsText(ctx, "clothes");
            ctx.Editor.MarkUnit(ClothesNames.ColorsUnit(0));
            var full = ctx.Editor.Flush();
            Check("WT4 a full pass afterwards rebuilds both creators of every group and changes no creator or material value", full.CreatorsRebuilt.Count == colourNames.Count + detailNames.Count
                  && FaceDigest.CreatorsText(ctx, "clothes") == cre && FaceDigest.MaterialsText(ctx, "clothes") == mats, "rebuilt " + string.Join(", ", full.CreatorsRebuilt));
            FreshOk("WT4 after the selective edits", h, live);
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- WT5

    /// <summary>_AlphaEx of every break renderer of kind k: slot 0 = 1 − rate, the other slots = the prefab's values.</summary>
    static bool BreakAsWritten(MakerCoreTest.Harness h, CharaContext ctx, int k, float rate, out string detail, out int multiSlots)
    {
        multiSlots = 0;
        var go = ctx.Clothes[k];
        var cmp = go ? go.GetComponent<CmpClothes>() : null;
        if (cmp == null) { detail = "no CmpClothes"; return false; }
        var rep = ClothesReport.KindOf(ctx, k);
        var row = RowOf(h.Catalog, CatOf(ctx, k), PartOf(ctx, k).Item);
        string col = rep.Prefab != null && row != null && rep.Prefab != row["MainData"] && rep.Prefab == row["MainData02"] ? "MainData02" : "MainData";
        var prefab = row != null && rep.Prefab != null ? h.Catalog.LoadAsset<GameObject>(row, "MainAB", col) : null;
        var pcmp = prefab ? prefab.GetComponentInChildren<CmpClothes>(true) : null;
        float want = 1 - rate;
        int slot0 = 0, others = 0, bad = 0;
        var notes = new List<string>();
        var prefRends = pcmp != null ? ClothesBreakUnit.BreakRenderers(pcmp) : new List<Renderer>();
        var rends = ClothesBreakUnit.BreakRenderers(cmp);
        for (int i = 0; i < rends.Count; i++)
        {
            var mats = rends[i].sharedMaterials;
            var pm = i < prefRends.Count && prefRends[i] ? prefRends[i].sharedMaterials : null;
            for (int s = 0; s < mats.Length; s++)
            {
                var m = mats[s];
                if (!m || !m.HasProperty("_AlphaEx")) continue;
                float v = m.GetFloat("_AlphaEx");
                if (s == 0) { slot0++; if (Bits(v) != Bits(want)) { bad++; notes.Add($"{rends[i].name}[0] {G(v)}"); } }
                else
                {
                    others++;
                    float pv = pm != null && s < pm.Length && pm[s] && pm[s].HasProperty("_AlphaEx") ? pm[s].GetFloat("_AlphaEx") : float.NaN;
                    if (Bits(v) != Bits(pv)) { bad++; notes.Add($"{rends[i].name}[{s}] {G(v)} prefab {G(pv)}"); }
                }
            }
            if (mats.Length > 1) multiSlots++;
        }
        detail = $"{rends.Count} renderers, slot 0 with _AlphaEx {slot0}, other slots {others}" + (bad > 0 ? $"; {bad} differ: {string.Join("; ", notes.Take(6))}" : "") + (pcmp == null ? "; prefab not read" : "");
        return slot0 > 0 && bad == 0 && (others == 0 || pcmp != null);
    }

    static float BodyF(CharaContext ctx, string prop) => ctx.BodyDraw && ctx.BodyDraw.HasProperty(prop) ? ctx.BodyDraw.GetFloat(prop) : float.NaN;

    /// <summary>A mod top / bottom whose prefab has useBreak and a break renderer with more than one material slot that
    /// declares _AlphaEx beyond slot 0 (no vanilla item has one); prefabs read without instantiating them.</summary>
    static (int kind, string reference) FindMultiSlotBreak(MakerCoreTest.Harness h, int cap)
    {
        foreach (int k in new[] { 0, 1 })
        {
            int n = 0;
            foreach (var e in h.Catalog.EntriesOf(ClothesSet.FemaleCategories[k]))
            {
                if (e.IsVanilla || string.IsNullOrEmpty(e["MainData"]) || e["MainData"] == "p_dummy") continue;
                if (++n > cap) break;
                GameObject prefab;
                try { prefab = h.Catalog.LoadAsset<GameObject>(e, "MainAB", "MainData"); } catch (Exception) { continue; }
                var cmp = prefab ? prefab.GetComponentInChildren<CmpClothes>(true) : null;
                if (cmp == null || cmp.useBreak == 0) continue;
                foreach (var r in ClothesBreakUnit.BreakRenderers(cmp))
                {
                    var mats = r.sharedMaterials;
                    if (mats.Length > 1 && mats.Skip(1).Any(m => m && m.HasProperty("_AlphaEx"))) return (k, e.Key.ToString());
                }
            }
        }
        return (-1, null);
    }

    static void WT5(MakerCoreTest.Harness h)
    {
        var (mk, multi) = FindMultiSlotBreak(h, 250);
        Info($"WT5 multi-slot break item: {(multi == null ? "none in the first 250 mod rows of 240 / 241" : ClothesSet.FemaleCategories[mk] + ":" + multi)}");
        h.ReleaseBundles();
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            if (Installed(h, 240, TopBreak, "WT5 break top"))
            {
                SetItem(live, 0, TopBreak);
                var p = PartOf(ctx, 0);
                Check("WT5 240#2 has useBreak", ClothesReport.KindOf(ctx, 0).UseBreak);
                SetM(live, Doc(0) + ".breakRate", () => p.BreakRate = 0.4f);
                Check("WT5 240#2 break 0.4: _AlphaEx 0.6 on slot 0 of every break renderer", BreakAsWritten(h, ctx, 0, 0.4f, out string d, out _), d);
                Check("WT5 the top's break → body _alpha_c 1", BodyF(ctx, "_alpha_c") == 1f, G(BodyF(ctx, "_alpha_c")));
                FreshOk("WT5 240#2 break 0.4", h, live);
                SetM(live, Doc(0) + ".breakRate", () => p.BreakRate = 0f);
                Check("WT5 break 0: _AlphaEx 1 on slot 0, body _alpha_c 0", BreakAsWritten(h, ctx, 0, 0f, out string d0, out _) && BodyF(ctx, "_alpha_c") == 0f, d0 + " _alpha_c " + G(BodyF(ctx, "_alpha_c")));
            }
            if (multi != null)
            {
                Doc1Top(live);
                SetItem(live, mk, multi);
                var p = PartOf(ctx, mk);
                SetM(live, Doc(mk) + ".breakRate", () => p.BreakRate = 0.3f);
                bool ok = BreakAsWritten(h, ctx, mk, 0.3f, out string d, out int ms);
                Check($"WT5 multi-slot break item {multi}: _AlphaEx 0.7 on slot 0 only, the other slots keep the prefab's value", ok && ms > 0, d + $"; multi-slot renderers {ms}");
                FreshOk("WT5 multi-slot break item", h, live);
                SetItem(live, mk, mk == 0 ? TopBreak : V + "0");
            }
            else Warn("WT5 multi-slot break", "no item found: the slot rule is checked on single-slot renderers only");
            // _alpha_d with a BreakDisableMask bottom that brings the lower masks
            Doc1Top(live);
            string bdm = FindRow(h, live, 1, r => r["BreakDisableMask"] == "1" && HasCol(r, "OverBodyBMask"), r => r.UseBreak);
            if (bdm == null) Warn("WT5 BreakDisableMask", "no vanilla bottom with BreakDisableMask, lower masks and useBreak");
            else
            {
                var p = PartOf(ctx, 1);
                SetM(live, Doc(1) + ".breakRate", () => p.BreakRate = 0f);
                float d0 = BodyF(ctx, "_alpha_d");
                SetM(live, Doc(1) + ".breakRate", () => p.BreakRate = 0.5f);
                float d1 = BodyF(ctx, "_alpha_d");
                Check($"WT5 BreakDisableMask bottom {bdm}: body _alpha_d 1 without a break, 0 with break 0.5 (the lower masks off)", d0 == 1f && d1 == 0f && ctx.Edit.MaskLowerType == 1,
                      $"_alpha_d {G(d0)} → {G(d1)}, lower masks from {ctx.Edit.MaskLowerType}");
                FreshOk("WT5 BreakDisableMask bottom with break 0.5", h, live);
            }
            // the kimono: useBreak only in Terable Kimonos' copy (served with the per-asset redirect)
            if (Installed(h, 240, TopKimono, "WT5 kimono"))
            {
                SetItem(live, 0, TopKimono);
                var rep = ClothesReport.KindOf(ctx, 0);
                if (!rep.UseBreak) Warn("WT5 kimono 240#223", $"useBreak false (redirect {RedirectState(h.Catalog)}: the abdata copy); with the redirect on it is Terable Kimonos' copy with useBreak");
                else
                {
                    var p = PartOf(ctx, 0);
                    SetM(live, Doc(0) + ".breakRate", () => p.BreakRate = 0.4f);
                    Check("WT5 kimono 240#223 (redirected copy): break 0.4 → _AlphaEx 0.6 on slot 0, body _alpha_c 1", BreakAsWritten(h, ctx, 0, 0.4f, out string d, out _) && BodyF(ctx, "_alpha_c") == 1f, d);
                    FreshOk("WT5 kimono with break 0.4", h, live);
                }
            }
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- WT6

    static void WT6(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            Doc1Top(live);
            if (!ctx.Clothes[1]) SetItem(live, 1, BottomSkirt);
            string bottom = PartOf(ctx, 1).Item, top = PartOf(ctx, 0).Item;
            Check("WT6 a two-piece top and a realized bottom to start from", ctx.Clothes[1] && !ClothesReport.KindOf(ctx, 1).Skipped, ClothesReport.KindOf(ctx, 1).ToString());
            if (Installed(h, 240, TopOnePiece, "WT6 one-piece top"))
            {
                var fr = SetItem(live, 0, TopOnePiece);
                var b = ClothesReport.KindOf(ctx, 1);
                Check("WT6 a one-piece top (240#1): the bottom is skipped (no object), its stored item kept", ClothesReport.KindOf(ctx, 0).OnePiece && b.Skipped && !ctx.Clothes[1] && PartOf(ctx, 1).Item == bottom && fr.ClothesSwapped[1],
                      $"bottom skipped {b.Skipped}, object {(bool)ctx.Clothes[1]}, item {PartOf(ctx, 1).Item}");
                FreshOk("WT6 with the one-piece top", h, live);
                SetItem(live, 0, top);
                b = ClothesReport.KindOf(ctx, 1);
                Check("WT6 back to the two-piece top: the stored bottom realized again", !b.Skipped && ctx.Clothes[1] && b.Item == bottom, b.ToString());
                FreshOk("WT6 back to the two-piece top", h, live);
            }
            if (Installed(h, 242, InnerOnePiece, "WT6 one-piece inner top"))
            {
                if (!ctx.Clothes[3])
                {
                    string ib = FindRow(h, live, 3, null, r => r.HasCmpClothes);
                    Info("WT6 inner bottom " + (ib ?? "none found"));
                }
                string inner = PartOf(ctx, 2).Item, innerB = PartOf(ctx, 3).Item;
                if (ClothesReport.KindOf(ctx, 2).OnePiece && Installed(h, 242, InnerTop, "WT6 inner top")) { SetItem(live, 2, InnerTop); inner = InnerTop; }
                SetItem(live, 2, InnerOnePiece);
                var ib2 = ClothesReport.KindOf(ctx, 3);
                Check("WT6 a one-piece inner top (242#15): the inner bottom is skipped, its stored item kept", ib2.Skipped && !ctx.Clothes[3] && PartOf(ctx, 3).Item == innerB, ib2.ToString());
                FreshOk("WT6 with the one-piece inner top", h, live);
                SetItem(live, 2, inner);
                ib2 = ClothesReport.KindOf(ctx, 3);
                Check("WT6 back to a two-piece inner top: the inner bottom realized again", !ib2.Skipped && (ctx.Clothes[3] || ib2.Prefab == "p_dummy"), ib2.ToString());
                FreshOk("WT6 back to the two-piece inner top", h, live);
            }
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- WT7

    static string CoordFile(string sex, string name) => Path.Combine(TestTargets.UserData, "coordinate", sex, name);

    /// <summary>The documents' clothes items and accessory items (order) as text.</summary>
    static string Wear(CoordDocument d) =>
        string.Join(",", d.Clothes.Parts.Select(p => p.Item)) + " | " + string.Join(",", d.Accessories.Select(a => a.Category + ":" + a.Item));

    static void WT7(MakerCoreTest.Harness h)
    {
        string file = CoordFile("female", "ガーリーブラウス.png");
        if (!File.Exists(file)) { Warn("WT7 coordinate", "the SFW coordinate card is not installed: " + Rel(file)); return; }
        var c = MakerEditTest.Import(h, FemaleCard, out string err);
        if (!Check("WT7 BR-Chan imported", c != null, err)) return;
        var n = MakerEditTest.NewNode(h, c, "BR-Chan");
        try
        {
            if (!Check("WT7 node built", n.Ok, n.Error)) return;
            var ids = new[] { n.Id };
            var src = n.Ops.LoadCoordinate(file, null, out bool imported, out var fail);
            if (!Check("WT7 the coordinate card read (imported)", src != null && imported, fail?.ToString())) return;
            Info("WT7 source: " + Wear(src.Coord) + "; BR-Chan: " + Wear(n.Stored.Coord));
            var real = n.Real;
            var before = n.Stored.Coord;
            string clothes0 = string.Join(",", before.Clothes.Parts.Select(p => p.Item)), accs0 = string.Join(",", before.Accessories.Select(a => a.Item));
            foreach (var mode in new[] { CoordinateApplyMode.Clothes, CoordinateApplyMode.Accessories, CoordinateApplyMode.All })
            {
                var bus = n.Session.Commands;
                int u0 = bus.UndoList.Count, rb0 = real.EditRebuilds, f0 = real.EditFlushes;
                var r = n.Ops.ApplyCoordinate(ids, src, mode, User);
                n.Apply();
                real = n.Real;
                var fr = real.LastEditFlush;
                var doc = n.Stored.Coord;
                string cl = string.Join(",", doc.Clothes.Parts.Select(p => p.Item)), ac = string.Join(",", doc.Accessories.Select(a => a.Item));
                string scl = string.Join(",", src.Coord.Clothes.Parts.Select(p => p.Item)), sac = string.Join(",", src.Coord.Accessories.Select(a => a.Item));
                bool docsOk = mode == CoordinateApplyMode.Clothes ? cl == scl && ac == accs0
                            : mode == CoordinateApplyMode.Accessories ? cl == clothes0 && ac == sac
                            : cl == scl && ac == sac;
                Check($"WT7 {mode}: one entry \"Load coordinate\", the documents wear the source's {(mode == CoordinateApplyMode.Clothes ? "clothes (accessories kept)" : mode == CoordinateApplyMode.Accessories ? "accessories (clothes kept)" : "clothes and accessories")}",
                      r.Ok && bus.UndoList.Count == u0 + 1 && docsOk, $"op {r}; clothes {cl}; accessories {ac}");
                Check($"WT7 {mode}: realized by per-part units (EditRebuilds unchanged, no rebuild requested, a flush ran)",
                      real.EditRebuilds == rb0 && fr != null && !fr.RebuildRequested && real.EditFlushes > f0,
                      $"rebuilds {rb0} → {real.EditRebuilds}, flushes {f0} → {real.EditFlushes}, last flush {fr}");
                var d = VsFresh(h, n);
                using (var fresh = MakerEditTest.NewNode(h, n.Session.Model.File.ExtractCharacter(n.Id), "BR-Chan (fresh)"))
                {
                    string cd = fresh.Ok ? ClothesDiff(fresh.Real.Context, real.Context) : fresh.Error;
                    Check($"WT7 {mode}: = a fresh node of the documents (snapshot, clothes digest)", d.Equal && cd == null, (d.Equal ? "" : d.ToString()) + (cd != null ? "; " + cd : ""));
                }
                bus.Undo(); n.Apply();
                real = n.Real;
                Check($"WT7 {mode}: undo restores the clothes and accessories without a rebuild", string.Join(",", n.Stored.Coord.Clothes.Parts.Select(p => p.Item)) == clothes0
                      && string.Join(",", n.Stored.Coord.Accessories.Select(a => a.Item)) == accs0 && real.EditRebuilds == rb0, $"rebuilds {real.EditRebuilds}");
            }
            // KCOX re-application and MaterialEditor items travel with the clothes (All on the KCOX fixture's documents as the source)
            var kc = KcoxFixture(h, c, out int kw, out int kh);
            if (kc != null)
            {
                var ksrc = new CoordContainer { Coord = kc.Subtree.Coord };
                ksrc.Res.CopyFrom(kc.Res, kc.Subtree.ReferencedResources());
                int rb0 = real.EditRebuilds;
                var r = n.Ops.ApplyCoordinate(ids, ksrc, CoordinateApplyMode.Clothes, User);
                n.Apply();
                real = n.Real;
                var col = real.Context.Edit.ClothesColor[0, 0];
                using (var fresh = MakerEditTest.NewNode(h, n.Session.Model.File.ExtractCharacter(n.Id), "BR-Chan (fresh, KCOX)"))
                {
                    string cd = fresh.Ok ? ClothesDiff(fresh.Real.Context, real.Context) : fresh.Error;
                    Check("WT7 Clothes from a source with a KCOX overlay: the overlay re-applied (the grown creator RT where a device exists) = a fresh node, no rebuild",
                          r.Ok && real.EditRebuilds == rb0 && cd == null && real.Context.Stats.KcoxEntries == fresh.Real.Context.Stats.KcoxEntries && real.Context.Stats.KcoxResized == fresh.Real.Context.Stats.KcoxResized,
                          $"op {r}; KCOX entries {real.Context.Stats.KcoxEntries} / {fresh.Real.Context.Stats.KcoxEntries}, resized {real.Context.Stats.KcoxResized} / {fresh.Real.Context.Stats.KcoxResized}; {cd}");
                }
            }
        }
        finally { n.Dispose(); }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- WT8

    static int DynamicBonesOf(CharaContext ctx, int k) => ctx.Clothes[k] ? ctx.Clothes[k].GetComponentsInChildren<DynamicBone>(true).Length : 0;

    static void WT8(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, MaleCard, "HERO107")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            var rep = ClothesReport.Of(ctx);
            Info("HERO107 clothes: " + rep);
            Check("WT8 HERO107: male; kinds 0, 1, 4, 7 on the male lists 140, 141, 144, 147; kinds 2, 3, 5, 6 on the female lists",
                  rep.Male && rep.Kinds[0].Category == 140 && rep.Kinds[1].Category == 141 && rep.Kinds[4].Category == 144 && rep.Kinds[7].Category == 147
                  && rep.Kinds[2].Category == 242 && rep.Kinds[3].Category == 243 && rep.Kinds[5].Category == 245 && rep.Kinds[6].Category == 246);
            foreach (int k in new[] { 0, 1, 4, 7 })
            {
                var kr = rep.Kinds[k];
                if (kr.Realized) Check($"WT8 HERO107 {Kinds[k]}: realized from its male row", kr.Row != null && kr.Row.StartsWith(kr.Category + ":", StringComparison.Ordinal) && kr.HasCmpClothes, $"{kr.Item} → {kr.Row} {kr.Prefab}");
                else Info($"WT8 HERO107 {Kinds[k]}: {kr.Item} (no object)");
            }
            Info("WT8 HERO107 stored kinds 2, 3, 5, 6: " + string.Join(", ", new[] { 2, 3, 5, 6 }.Select(k => Kinds[k] + " " + rep.Kinds[k].Item)));
            if (Installed(h, 242, InnerTop, "WT8 female-list inner top"))
            {
                SetItem(live, 2, InnerTop);
                var k2 = ClothesReport.KindOf(ctx, 2);
                Check("WT8 a stored inner top 242#1 loads on the male (the female list)", k2.Category == 242 && k2.Realized && k2.HasCmpClothes && k2.Row == "242:" + InnerTop, k2.ToString() + " row " + k2.Row);
                FreshOk("WT8 HERO107 with a female-list inner top", h, live);
                SetItem(live, 2, V + "0");
            }
            if (Installed(h, 140, MaleOptionTop, "WT8 male option top"))
            {
                SetItem(live, 0, MaleOptionTop);
                var t = ClothesReport.KindOf(ctx, 0);
                Check("WT8 140#201: option parts and DynamicBones (the male page's option rows)", t.HasOptions && DynamicBonesOf(ctx, 0) > 0, $"{t}; DynamicBones {DynamicBonesOf(ctx, 0)}");
                FreshOk("WT8 HERO107 with 140#201", h, live);
            }
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- WT9

    sealed class CensusCat
    {
        public readonly Dictionary<string, (int n, List<int> ex)> Facts = new Dictionary<string, (int, List<int>)>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> Sizes = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    /// <summary>The census's vanilla examples and MainTex sizes per category (Tools/survey/maker_clothes_census.txt).</summary>
    static Dictionary<int, CensusCat> ReadCensus(out string error)
    {
        error = null;
        string file = Path.Combine(ContentSettings.ProjectRoot, "Tools", "survey", "maker_clothes_census.txt");
        if (!File.Exists(file)) { error = "no " + file; return null; }
        var d = new Dictionary<int, CensusCat>();
        CensusCat Of(int c) => d.TryGetValue(c, out var x) ? x : d[c] = new CensusCat();
        int mode = 0;
        var fact = new System.Text.RegularExpressions.Regex(@"^(?<label>.+?) (?<n>\d+): (?<ex>.*)$");
        var line = new System.Text.RegularExpressions.Regex(@"^  (?<cat>\d+): (?<rest>.*)$");
        foreach (var raw in File.ReadAllLines(file, Encoding.UTF8))
        {
            if (raw.StartsWith("vanilla examples", StringComparison.Ordinal)) { mode = 1; continue; }
            if (raw.StartsWith("MainTex sizes of vanilla rows", StringComparison.Ordinal)) { mode = 2; continue; }
            if (mode == 0) continue;
            var m = line.Match(raw);
            if (!m.Success) { if (mode == 2) break; continue; }
            var cc = Of(int.Parse(m.Groups["cat"].Value, CultureInfo.InvariantCulture));
            string rest = m.Groups["rest"].Value;
            if (mode == 1)
                foreach (var part in rest.Split(new[] { " | " }, StringSplitOptions.None))
                {
                    var f = fact.Match(part);
                    if (!f.Success) continue;
                    var ex = System.Text.RegularExpressions.Regex.Matches(f.Groups["ex"].Value, @"#(\d+)").Cast<System.Text.RegularExpressions.Match>().Select(x => int.Parse(x.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
                    cc.Facts[f.Groups["label"].Value] = (int.Parse(f.Groups["n"].Value, CultureInfo.InvariantCulture), ex);
                }
            else
                foreach (var part in rest.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var sp = part.Split(' ');
                    if (sp.Length == 2 && int.TryParse(sp[1], out int cnt)) cc.Sizes[sp[0]] = cnt;
                }
        }
        if (d.Count == 0) error = "no vanilla examples read from " + file;
        return d;
    }

    static readonly string[] CensusFacts = { "useColorA (any set)", "three colour sets shown", "useBreak", "useBreak with a multi-slot renderer", "option parts", "DynamicBones", "defPtnIndex != 0", "Coordinate != 0" };

    static void WT9(MakerCoreTest.Harness h)
    {
        var census = ReadCensus(out string cerr);
        if (!Check("WT9 census read", census != null, cerr)) return;
        var defaults = new PartDefaults(h.Catalog);
        var worst = new List<(long px, string what)>();
        foreach (bool male in new[] { false, true })
            using (var bb = new Disposer(BuildOk(h, male ? MaleCard : FemaleCard, male ? "HERO107" : "BR-Chan")))
            {
                var live = bb.B;
                if (!live.Ok) continue;
                var ctx = live.Context;
                Doc1Top(live);
                if (!male && Installed(h, 242, InnerTop, "WT9 two-piece inner top")) SetItem(live, 2, InnerTop);
                foreach (int k in male ? new[] { 0, 1, 4, 7 } : Enumerable.Range(0, 8).ToArray())
                {
                    int cat = CatOf(ctx, k);
                    if (!census.TryGetValue(cat, out var exp)) exp = new CensusCat();
                    var got = new Dictionary<string, List<int>>(StringComparer.Ordinal);
                    void Add(string f, int id) { if (!got.TryGetValue(f, out var l)) got[f] = l = new List<int>(); l.Add(id); }
                    var sizes = new Dictionary<string, int>(StringComparer.Ordinal);
                    int prefabs = 0, pdOk = 0, pdN = 0, kimonoAbdata = 0;
                    var pdBad = new List<string>();
                    if (k == 1) Doc1Top(live);
                    if (k == 3 && ClothesReport.KindOf(ctx, 2).OnePiece) SetItem(live, 2, InnerTop);   // a two-piece inner top
                    var rows = MakerCatalog.For(h.Catalog).Rows(cat, male ? 0 : 1).Where(r => r.IsVanilla).ToList();
                    foreach (var r in rows)
                    {
                        int id = r.Key.Id;
                        if (r["Coordinate"] != null && r["Coordinate"] != "" && r["Coordinate"] != "0") Add("Coordinate != 0", id);
                        if (string.IsNullOrEmpty(r["MainData"]) || r["MainData"] == "p_dummy") continue;
                        var entry = h.Catalog.GetEntry(cat, r.Key);
                        if (entry != null && HasTex(entry, "MainTex"))
                        {
                            var mt = h.Catalog.LoadAsset<Texture2D>(entry, "MainAB", "MainTex");
                            if (mt) { string s = mt.width + "x" + mt.height; sizes[s] = (sizes.TryGetValue(s, out var c0) ? c0 : 0) + 1; }
                        }
                        SetItem(live, k, r.Key.ToString());
                        var kr = ClothesReport.KindOf(ctx, k);
                        if (kr == null || !kr.HasCmpClothes) continue;
                        prefabs++;
                        if (kr.UseColorA.Any(a => a)) Add("useColorA (any set)", id);
                        if (kr.SetShown(1) && kr.SetShown(2) && kr.SetShown(3)) Add("three colour sets shown", id);
                        if (kr.UseBreak) Add("useBreak", id);
                        else if (cat == 240 && KimonoRows.Contains(id)) kimonoAbdata++;
                        if (kr.UseBreak && kr.Groups.Any(g => g.Slots > g.Renderers)) Add("useBreak with a multi-slot renderer", id);
                        if (kr.HasOptions) Add("option parts", id);
                        if (DynamicBonesOf(ctx, k) > 0) Add("DynamicBones", id);
                        if (kr.Defaults.Any(c => c.Pattern != DocDefaults.NoneRef)) Add("defPtnIndex != 0", id);
                        long px = kr.Groups.Where(g => g.HasCreators).Sum(g => (long)g.MainWidth * g.MainHeight);
                        worst.Add((px, $"{cat}:{r.Key} ({string.Join(" + ", kr.Groups.Where(g => g.HasCreators).Select(g => g.MainWidth + "x" + g.MainHeight))})"));
                        // the realized prefab's defaults = the ops' (PartDefaults reads the row's prefab without instantiating it)
                        pdN++;
                        if (defaults.TryClothesColors(cat, r.Key.ToString(), out var pd) && pd.Length == 3 && Enumerable.Range(0, 3).All(c => SameSet(pd[c], kr.Defaults[c]))) pdOk++;
                        else if (pdBad.Count < 4) pdBad.Add(r.Key.ToString());
                    }
                    // the census against the realized facts (the kimono rows: useBreak only in the redirected copy)
                    var lines = new List<string>(); bool same = true;
                    foreach (var f in CensusFacts)
                    {
                        exp.Facts.TryGetValue(f, out var e);
                        got.TryGetValue(f, out var g);
                        int gn = g?.Count ?? 0, en = e.ex == null ? 0 : e.n;
                        if (f == "useBreak" && cat == 240) gn += kimonoAbdata;
                        var gex = (g ?? new List<int>()).OrderBy(x => x).Take(4).ToList();
                        bool exOk = e.ex == null ? gn == 0 : gex.SequenceEqual(e.ex) || (f == "useBreak" && kimonoAbdata > 0);
                        bool ok = gn == en && exOk;
                        same &= ok;
                        if (!ok || gn > 0) lines.Add($"{f}: {gn}{(ok ? "" : " (census " + en + ": " + string.Join(",", e.ex ?? new List<int>()) + "; HS2Next " + string.Join(",", gex) + ")")}");
                    }
                    bool sizesOk = exp.Sizes.All(kv => sizes.TryGetValue(kv.Key, out var n2) && n2 == kv.Value);
                    Check($"WT9 {cat} vanilla ({prefabs} prefabs): ClothesReport facts = the census's vanilla examples", same, string.Join("; ", lines));
                    Check($"WT9 {cat} vanilla: MainTex sizes = the census's", sizesOk && exp.Sizes.Count > 0, "census " + string.Join(", ", exp.Sizes.Select(kv => kv.Key + " " + kv.Value)) + "; HS2Next " + string.Join(", ", sizes.OrderByDescending(kv => kv.Value).Take(8).Select(kv => kv.Key + " " + kv.Value)));
                    Check($"WT9 {cat} vanilla: the realized prefab's defaults = PartDefaults.TryClothesColors (bit for bit)", pdN > 0 && pdOk == pdN, $"{pdOk} of {pdN}" + (pdBad.Count > 0 ? "; " + string.Join(", ", pdBad) : ""));
                    if (kimonoAbdata > 0) Warn($"WT9 {cat} kimono rows", $"{kimonoAbdata} of 240#223 / #235 / #244 without useBreak (redirect {RedirectState(h.Catalog)}: the abdata copy); the census reads Terable Kimonos' copy (useBreak)");
                }
                h.ReleaseBundles();
            }
        foreach (var w in worst.OrderByDescending(x => x.px).Take(5)) Info($"WT9 compositing work: {w.what} = {w.px / 1048576.0:F1} Mpx per pass");
    }

    static bool SameSet(ClothesColor a, ClothesColor b) =>
        a != null && b != null && a.Pattern == b.Pattern && SameC(a.Base, b.Base) && SameC(a.PatternColor, b.PatternColor)
        && Bits(a.Layout.X) == Bits(b.Layout.X) && Bits(a.Layout.Y) == Bits(b.Layout.Y) && Bits(a.Layout.Z) == Bits(b.Layout.Z) && Bits(a.Layout.W) == Bits(b.Layout.W)
        && Bits(a.Rotation) == Bits(b.Rotation) && Bits(a.Gloss) == Bits(b.Gloss) && Bits(a.Metallic) == Bits(b.Metallic);

    static bool SameC(Color4 a, Color4 b) => Bits(a.R) == Bits(b.R) && Bits(a.G) == Bits(b.G) && Bits(a.B) == Bits(b.B) && Bits(a.A) == Bits(b.A);

    // ---------------------------------------------------------------- WT10

    static void WT10(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            if (!Installed(h, 240, TopBreak, "WT10 break top")) return;
            SetItem(live, 0, TopBreak);
            FaceDigest.Result D() => FaceDigest.Compute(ctx, "clothes", false);
            var d0 = D();
            // since Phase 4.5 the part "all" also holds the accessory slot objects after the clothes' (FaceDigest.ObjectsText)
            Check("WT10 sections: clothes has objects (no normals, bust, chains); face has no objects; all has the clothes' objects first",
                  d0.Objects != null && d0.Normals == null && d0.Chains == null && FaceDigest.Compute(ctx, "face", false).Objects == null && FaceDigest.Compute(ctx, "all", false).Objects != null
                  && FaceDigest.ObjectsText(ctx, "all").StartsWith(FaceDigest.ObjectsText(ctx, "clothes"), StringComparison.Ordinal)
                  && FaceDigest.Sections.Contains("objects") && FaceDigest.IsPart("clothes"));
            Check("WT10 texts: clothes renderers, clothes creators, objects per kind",
                  FaceDigest.MaterialsText(ctx, "clothes").Contains("renderer clothes0 ") && FaceDigest.CreatorsText(ctx, "clothes").Contains("creator create_clothes 0.0 ")
                  && FaceDigest.ObjectsText(ctx, "clothes").Contains("clothes0 ") && FaceDigest.CreatorsOf(ctx, "clothes").Any());
            var p = PartOf(ctx, 0);
            void Step(string what, string path, Action set, bool mats, bool creators, bool objects)
            {
                var a = D();
                SetM(live, path, set);
                var b = D();
                bool ok = (a.Materials != b.Materials) == mats && (a.Creators != b.Creators) == creators && (a.Objects != b.Objects) == objects;
                string Say(bool x) => x ? "change" : "same";
                Check($"WT10 {what}: materials {Say(mats)}, creators {Say(creators)}, objects {Say(objects)}", ok,
                      $"materials {Say(a.Materials != b.Materials)}, creators {Say(a.Creators != b.Creators)}, objects {Say(a.Objects != b.Objects)}");
            }
            float g0 = p.Colors[0].Gloss, br0 = p.BreakRate; var b0 = p.Colors[0].Base;
            Step("gloss", Doc(0) + ".colors[0].gloss", () => p.Colors[0].Gloss = 0.123f, false, true, false);
            Step("base colour", Doc(0) + ".colors[0].base", () => p.Colors[0].Base = new Color4(0.2f, 0.7f, 0.1f, 1), false, true, false);
            Step("break", Doc(0) + ".breakRate", () => p.BreakRate = 0.45f, true, false, false);
            SetM(live, Doc(0) + ".colors[0].gloss", () => p.Colors[0].Gloss = g0);
            SetM(live, Doc(0) + ".colors[0].base", () => p.Colors[0].Base = b0);
            SetM(live, Doc(0) + ".breakRate", () => p.BreakRate = br0);
            var dz = D();
            Check("WT10 every value back: every section = the start's", dz.Materials == d0.Materials && dz.Creators == d0.Creators && dz.Objects == d0.Objects, dz + " vs " + d0);
            if (Installed(h, 240, TopOptions, "WT10 option top"))
            {
                SetItem(live, 0, TopOptions);
                var rep = ClothesReport.KindOf(ctx, 0);
                if (rep.Opt01 > 0)
                {
                    var a = D();
                    SetM(live, Doc(0) + ".hideOptions", () => p.HideOptions = new[] { true, false });
                    var b = D();
                    Check("WT10 an option toggle: objects change, creators same", a.Objects != b.Objects && a.Creators == b.Creators, $"{a} → {b}");
                    SetM(live, Doc(0) + ".hideOptions", () => p.HideOptions = new[] { false, false });
                    Check("WT10 the option back: objects = before", D().Objects == a.Objects);
                }
                else Warn("WT10 option toggle", TopOptions + " has no option part 1");
                var s0 = D();
                SetItem(live, 0, TopBreak);
                var s1 = D();
                Check("WT10 an item swap: materials, creators and objects change", s0.Materials != s1.Materials && s0.Creators != s1.Creators && s0.Objects != s1.Objects, $"{s0} → {s1}");
            }
            FreshOk("WT10 at the end", h, live);
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- WT11

    static void WT11(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (live.Ok && Installed(h, 241, BottomSkirt, "WT11 skirt") && Installed(h, 241, BottomSkirt2, "WT11 second skirt"))
            {
                var ctx = live.Context;
                Doc1Top(live);
                SetItem(live, 1, BottomSkirt);
                int c1 = ClothesChains(ctx, 1).Count;
                Check("WT11 241#4: clothes chains on the bottom", c1 > 0, c1 + " chains");
                FreshOk("WT11 after the swap to 241#4 (chains, colliders, rest pose in the snapshot and the chains text)", h, live);
                SetItem(live, 1, BottomSkirt2);
                FreshOk("WT11 after the swap to 241#14", h, live);
                var stage = ctx.Motion.Get<DynamicBoneStage>();
                Info($"WT11 chains of the bottom {ClothesChains(ctx, 1).Count}, collider table {stage.Colliders.Count}, colliders of the first chain {ClothesChains(ctx, 1).FirstOrDefault()?.ColliderCount}");
            }
        }
        h.ReleaseBundles();
        if (!Installed(h, 241, BottomSkirt, "WT11 animated") || !Installed(h, 241, BottomSkirt2, "WT11 animated")) return;
        var keepDb = h.Services.Db;
        try
        {
            if (h.Services.Db == null) h.Services.Db = h.Catalog.LoadStudioLists();
            foreach (bool fix in new[] { true, false })
            {
                ClothesPartUnit.RestoreChainsBeforeRelease = fix;
                string diff = AnimatedSwap(h, out string info);
                if (fix) Check("WT11 animated: a skirt swap under a playing clip, after simulated frames, with the skirt bones turned: the new chains (rest pose, colliders) = a fresh build's", diff == null, info + (diff != null ? "; differs: " + diff : ""));
                else if (diff != null) Info("WT11 without the restore before the release the new chains differ (the case the restore closes): " + Short(diff, 300));
                else Warn("WT11 diagnostic", "without the restore the chains are equal too: the case did not move the re-pointed bones (" + info + ")");
            }
        }
        finally
        {
            ClothesPartUnit.RestoreChainsBeforeRelease = true;
            h.Services.Db = keepDb;
        }
        h.ReleaseBundles();
    }

    static List<DynamicBoneChain> ClothesChains(CharaContext ctx, int kind)
    {
        var stage = ctx?.Motion != null ? ctx.Motion.Get<DynamicBoneStage>() : null;
        return stage == null ? new List<DynamicBoneChain>() : stage.Chains.Where(c => c != null && c.Category == DynamicBoneCategory.Clothes && c.Kind == kind).ToList();
    }

    /// <summary>A scene node of BR-Chan wearing skirt 241#4 plays a clip (dance_00) for 20 frames, the bones of the skirt's
    /// chains are turned by 15° (as FK or the clip would leave them), then the bottom is swapped to 241#14; returns null when
    /// the new chains' text equals a fresh node's of the same documents and clip, else the first difference.</summary>
    static string AnimatedSwap(MakerCoreTest.Harness h, out string info)
    {
        info = "";
        var c = MakerEditTest.Import(h, FemaleCard, out string err);
        if (c == null) return "import: " + err;
        using (var n = MakerEditTest.NewNode(h, c, "BR-Chan (animated)"))
        {
            if (!n.Ok) return "node: " + n.Error;
            var ids = new[] { n.Id };
            if (ClothesReport.KindOf(n.Real.Context, 0).OnePiece) { n.Ops.ChangeItem(ids, "appearance.coord.clothes.top.item", TopBreak, User); n.Apply(); }
            n.Ops.ChangeItem(ids, "appearance.coord.clothes.bottom.item", BottomSkirt, User); n.Apply();
            var sr = n.Session.Commands.Set(n.Id, "chara.anime.ref", Dance, User);
            n.Apply();
            var real = n.Real;
            for (int i = 0; i < 20; i++) n.Runtime.Tick(1f / 30f);
            var old = ClothesChains(real.Context, 1);
            int turned = 0;
            foreach (var ch in old)
                for (int i = 0; i < ch.ParticleCount; i++)
                {
                    var t = ch.ParticleTransform(i);
                    if (!t) continue;
                    t.localRotation = t.localRotation * Quaternion.Euler(15f, 0f, 5f);
                    turned++;
                }
            bool playing = real.Animation && real.Animation.isActiveAndEnabled;
            var op = n.Ops.ChangeItem(ids, "appearance.coord.clothes.bottom.item", BottomSkirt2, User);
            n.Apply();
            string liveText = ClothesChainsText(real.Context);
            info = $"clip set {sr.Status}, animation component {playing}, old chains {old.Count} ({turned} bones turned), swap {op.Ok}, new chains {ClothesChains(real.Context, 1).Count}";
            using (var fresh = MakerEditTest.NewNode(h, n.Session.Model.File.ExtractCharacter(n.Id), "BR-Chan (animated, fresh)"))
            {
                if (!fresh.Ok) return "fresh node: " + fresh.Error;
                fresh.Session.Commands.Set(fresh.Id, "chara.anime.ref", Dance, User);
                fresh.Apply();
                string freshText = ClothesChainsText(fresh.Real.Context);
                return freshText == liveText ? null : FirstDiff(freshText, liveText);
            }
        }
    }

    // ---------------------------------------------------------------- WT12

    /// <summary>Every member of a facts value as text (groups and defaults included).</summary>
    static string FactsSig(ClothesKindFacts f)
    {
        if (f == null) return "null";
        var sb = new StringBuilder();
        sb.Append(f.HasPrefab).Append('|').Append(f.Item).Append('|').Append(f.PrefabName).Append('|')
          .Append(f.UseColorN01).Append(f.UseColorN02).Append(f.UseColorN03).Append(f.UseColorA01).Append(f.UseColorA02).Append(f.UseColorA03)
          .Append('|').Append(f.UseBreak).Append('|').Append(f.Option01Count).Append(',').Append(f.Option02Count).Append('|').Append(f.Coordinate)
          .Append('|').Append(f.NotBra).Append('|').Append(f.StateType).Append('|').Append(f.Skipped).Append('|')
          .Append(f.HasTopDef).Append(f.HasTopHalf).Append(f.HasBotDef).Append(f.HasBotHalf);
        foreach (var g in f.Groups ?? new ClothesGroupFacts[0])
            sb.Append("|g").Append(g.Group).Append(':').Append(g.Renderers).Append(',').Append(g.Slots).Append(',').Append(g.MainTexWidth).Append('x').Append(g.MainTexHeight).Append(',').Append(g.HasCreators);
        if (f.Defaults != null)
            foreach (var c in f.Defaults)
                sb.Append("|d").Append(c == null ? "null" : $"{c.Pattern} {Bits(c.Base.R)} {Bits(c.Base.G)} {Bits(c.Base.B)} {Bits(c.Base.A)} {Bits(c.Gloss)} {Bits(c.Metallic)} {Bits(c.Rotation)} {Bits(c.Layout.X)} {Bits(c.Layout.W)}");
        return sb.ToString();
    }

    static void WT12(MakerCoreTest.Harness h)
    {
        var c = MakerEditTest.Import(h, FemaleCard, out string err);
        if (!Check("WT12 BR-Chan imported", c != null, err)) return;
        var n = MakerEditTest.NewNode(h, c, "BR-Chan");
        bool keep = ClothesInfo.UseCache;
        try
        {
            if (!Check("WT12 node built", n.Ok, n.Error)) return;
            ClothesInfo.UseCache = true;
            ClothesInfo.Track(n.Runtime);
            var ids = new[] { n.Id };
            var info = ClothesInfo.Instance;
            // every kind asked (fills the cache), then compared with a fresh report of the realization
            int asked = 0, stale = 0;
            var bad = new List<string>();
            void Ask() { for (int k = 0; k < 8; k++) if (info.TryKind(n.Id, k, out _)) asked++; }
            void Same(string when)
            {
                var ctx = n.Real.Context;
                for (int k = 0; k < 8; k++)
                {
                    bool ok = info.TryKind(n.Id, k, out var f);
                    string a = FactsSig(f), b = FactsSig(ClothesInfo.FactsOf(ClothesReport.KindOf(ctx, k)));
                    if (!ok || a != b) { stale++; if (bad.Count < 6) bad.Add($"{when} {Kinds[k]}: cached {Short(a, 120)} | fresh {Short(b, 120)}"); }
                }
            }
            Ask(); Same("built");
            var steps = new (string what, Func<OpResult> op)[]
            {
                ("item change (top → 240#51)", () => n.Ops.ChangeItem(ids, "appearance.coord.clothes.top.item", TopOptions, User)),
                ("option toggle (top option 1 hidden)", () => n.Ops.EditAppearance(ids, new List<(string, object)> { ("appearance.coord.clothes.top.hideOptions[0]", true) }, User)),
                ("one-piece swap (top → 240#1, bottom skipped)", () => n.Ops.ChangeItem(ids, "appearance.coord.clothes.top.item", TopOnePiece, User)),
                ("two-piece back (top → 240#2)", () => n.Ops.ChangeItem(ids, "appearance.coord.clothes.top.item", TopBreak, User)),
            };
            foreach (var (what, op) in steps) { Ask(); var r = op(); n.Apply(); Same(what); if (!r.Ok) bad.Add(what + ": " + r); }
            string file = CoordFile("female", "ガーリーブラウス.png");
            var src = File.Exists(file) ? n.Ops.LoadCoordinate(file, null, out _, out _) : null;
            if (src != null)
                foreach (var mode in new[] { CoordinateApplyMode.Clothes, CoordinateApplyMode.All })
                {
                    Ask(); n.Ops.ApplyCoordinate(ids, src, mode, User); n.Apply(); Same("coordinate apply " + mode);
                }
            else Warn("WT12 coordinate", "the coordinate card did not load: the apply steps skipped");
            var bus = n.Session.Commands;
            for (int i = 0; i < 3 && bus.CanUndo; i++) { Ask(); bus.Undo(); n.Apply(); Same("undo " + (i + 1)); }
            for (int i = 0; i < 2 && bus.CanRedo; i++) { Ask(); bus.Redo(); n.Apply(); Same("redo " + (i + 1)); }
            Check("WT12 the cached facts of every kind = a fresh report's after item changes, an option toggle, a one-piece swap and back, coordinate applies, undo and redo",
                  stale == 0 && asked > 0, $"{stale} stale; " + string.Join(" | ", bad));
            // a repeated request comes from the cache; a flush invalidates it
            int b0 = ClothesInfo.FactsBuilt, c0 = ClothesInfo.FactsFromCache;
            Ask();
            int b1 = ClothesInfo.FactsBuilt, c1 = ClothesInfo.FactsFromCache;
            n.Ops.EditAppearance(ids, new List<(string, object)> { ("appearance.coord.clothes.top.colors[0].gloss", 0.37f) }, User); n.Apply();
            Ask();
            int b2 = ClothesInfo.FactsBuilt;
            Check("WT12 a repeated request is served from the cache; the next flush makes the facts be read again", b1 == b0 && c1 - c0 == 8 && b2 - b1 == 8,
                  $"built +{b1 - b0} / cached +{c1 - c0} on a repeat, built +{b2 - b1} after a flush");
            // the gate checks of a first show: 26 requests over the eight kinds, without and with the cache
            double Time26()
            {
                n.Ops.EditAppearance(ids, new List<(string, object)> { ("appearance.coord.clothes.top.colors[0].gloss", (float)(0.1 + 0.8 * new System.Random().NextDouble())) }, User); n.Apply();
                var t = Stopwatch.StartNew();
                for (int i = 0; i < 26; i++) info.TryKind(n.Id, i % 8, out _);
                return t.Elapsed.TotalMilliseconds;
            }
            var cold = new List<double>(); var uncached = new List<double>(); var report = new List<double>();
            for (int i = 0; i < 15; i++)
            {
                ClothesInfo.UseCache = true; cold.Add(Time26());
                ClothesInfo.UseCache = false; uncached.Add(Time26());
                var t = Stopwatch.StartNew();
                for (int j = 0; j < 26; j++) ClothesReport.KindOf(n.Real.Context, j % 8);
                report.Add(t.Elapsed.TotalMilliseconds);
            }
            ClothesInfo.UseCache = true;
            Info($"WT12 26 facts requests after a flush (a first show's gate checks): {F(Median(cold))} ms with the cache, {F(Median(uncached))} ms without; 26 full reports (with the material slots, the facts' former source) {F(Median(report))} ms (medians of 15, interleaved)");
        }
        finally { ClothesInfo.UseCache = keep; n.Dispose(); }
        h.ReleaseBundles();
    }

    // ================================================================ RunRender (graphics)

    public static void RunRender()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        Rep.AppendLine($"maker clothes render (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName}); redirect {RedirectState()}");
        bool device = FaceDigest.HasDevice;
        Check("graphics device present", device, SystemInfo.graphicsDeviceType.ToString());
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        var keepRp = GraphicsSettings.defaultRenderPipeline; var keepQ = QualitySettings.renderPipeline;
        try
        {
            if (device)
                using (var h = MakerCoreTest.Harness.Open())
                {
                    Info("redirect after the catalog's settings: " + RedirectState(h.Catalog));
                    using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
                    {
                        var live = bb.B;
                        if (live.Ok)
                        {
                            var ctx = live.Context;
                            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HS2Next.App.AppPipeline.MeasureAssetPath);
                            Check("URP asset", asset != null);
                            GraphicsSettings.defaultRenderPipeline = asset; QualitySettings.renderPipeline = asset;
                            Doc1Top(live);
                            if (Groups(ctx, 0) == 0 && Installed(h, 240, Top3, "R1 three-set top")) SetItem(live, 0, Top3);
                            // R1: the WT1 / WT2 edits — the creators' RT read-backs = a fresh build's
                            EditAllMembers(h, live, 0, "R1 top");
                            var p = PartOf(ctx, 0);
                            SetM(live, Doc(0) + ".colors[0].pattern", () => p.Colors[0].Pattern = V + "7");
                            SetM(live, Doc(0) + ".colors[0].layout", () => p.Colors[0].Layout = new Vec4(-0.5f, 1.5f, 0.3f, 0.7f));
                            FreshOk("R1 after the colour and pattern edits", h, live, rts: true);
                            // R2: selective rebuilds = a full pass, bit for bit
                            SetM(live, Doc(0) + ".colors[1].gloss", () => p.Colors[1].Gloss = 0.66f);
                            SetM(live, Doc(0) + ".colors[2].base", () => p.Colors[2].Base = new Color4(0.7f, 0.2f, 0.5f, 1));
                            string a = FaceDigest.RtsText(ctx, "clothes");
                            ctx.Editor.MarkUnit(ClothesNames.ColorsUnit(0)); ctx.Editor.Flush();
                            string b = FaceDigest.RtsText(ctx, "clothes");
                            Check("R2 the RTs after per-creator rebuilds = after a full pass (read-back hashes)", a != null && a == b, FirstDiff(a, b));
                            // R4: a 64² RT of the top rendered twice
                            string r1 = RenderPart(ctx.Clothes[0]), r2 = RenderPart(ctx.Clothes[0]);
                            Check("R4 a 64² RT of the top rendered twice: equal hashes", r1 != null && r1 == r2, $"{Short(r1, 16)} / {Short(r2, 16)}");
                        }
                    }
                    h.ReleaseBundles();
                    Guard("R3 KCOX-grown top", () => R3(h));
                    h.ReleaseBundles();
                }
        }
        finally
        {
            FaceSetup.Seed = fseed;
            GraphicsSettings.defaultRenderPipeline = keepRp; QualitySettings.renderPipeline = keepQ;
        }
        Finish("maker_clothes_render_report.txt", "MakerClothesTest.render", sw);
    }

    /// <summary>A synthetic overlay image (noise, RGBA, PNG bytes) of the given size; never shown or saved.</summary>
    static byte[] SyntheticOverlay(int w, int hgt, int seed)
    {
        var t = Noise(w, hgt, seed);
        try { return t.EncodeToPNG(); }
        finally { Object.DestroyImmediate(t); }
    }

    /// <summary>
    /// The KCOX-grown fixture: BR-Chan wearing 240#2 with a synthetic main overlay twice the top's main texture (the
    /// default OriginalX2 limit grows the colour RT to 2×). The colour creator owns the grown RT (bound to the group's
    /// materials); a colour edit equals a fresh node (RT read-backs); a detail edit rebuilds the detail creators only and
    /// leaves the colour RT (with the overlay) as it was; the release destroys every creator's material and RT.
    /// </summary>
    /// <summary>The KCOX-grown fixture of the render and leak rows: a copy of <paramref name="basis"/> (BR-Chan) wearing 240#2
    /// with a synthetic KCOX main overlay of twice the top's main texture size (the default OriginalX2 limit grows the colour
    /// RT to 2×); null when 240#2 is not installed. <paramref name="mainW"/> / <paramref name="mainH"/>: the main texture's size.</summary>
    public static CharaContainer KcoxFixture(MakerCoreTest.Harness h, CharaContainer basis, out int mainW, out int mainH)
    {
        mainW = mainH = 0;
        var row = RowOf(h.Catalog, 240, TopBreak);
        var main = row != null ? h.Catalog.LoadAsset<Texture2D>(row, "MainAB", "MainTex") : null;
        if (basis == null || !main) return null;
        mainW = main.width; mainH = main.height;
        var c = new CharaContainer { Subtree = basis.Subtree.Clone() };
        c.Res.CopyFrom(basis.Res, c.Subtree.ReferencedResources());
        c.Subtree.Coord.Clothes.Parts[0].Item = TopBreak;
        c.Subtree.Chara.StudioOverlays.Clothes = true;
        string res = c.Res.Add(SyntheticOverlay(2 * mainW, 2 * mainH, 4401));
        c.Subtree.Coord.Clothes.Parts[0].Overlays = new ClothesOverlaySet { Main = new OverlayEntry { Image = res } };
        return c;
    }

    static void R3(MakerCoreTest.Harness h)
    {
        var basis = MakerEditTest.Import(h, FemaleCard, out string err);
        if (!Check("R3 BR-Chan imported", basis != null, err)) return;
        var c = KcoxFixture(h, basis, out int w, out int hgt);
        if (!Check("R3 240#2 and its main texture (the fixture)", c != null)) return;
        var n = MakerEditTest.NewNode(h, c, "BR-Chan (KCOX)");
        try
        {
            if (!Check("R3 node built", n.Ok, n.Error)) return;
            var ids = new[] { n.Id };
            var ctx = n.Real.Context;
            var col = ctx.Edit.ClothesColor[0, 0];
            var rep = ClothesReport.KindOf(ctx, 0);
            var g0 = rep.Materials.Where(m => m.Group == 0 && m.MainTex != null).ToList();
            Check("R3 the overlay grew the colour creator's own RT to 2× the main texture, bound to the group's materials",
                  col != null && col.Creator.Target && col.Creator.Target.width == 2 * w && col.Creator.Target.height == 2 * hgt && g0.Count > 0 && g0.All(m => m.MainIsCreator) && ctx.Stats.KcoxResized >= 1,
                  $"RT {(col?.Creator.Target ? col.Creator.Target.width + "x" + col.Creator.Target.height : "none")}, main {w}x{hgt}, materials on the creator RT {g0.Count(m => m.MainIsCreator)} of {g0.Count}, KcoxResized {ctx.Stats.KcoxResized}");
            void VsFreshNode(string what)
            {
                using (var fresh = MakerEditTest.NewNode(h, n.Session.Model.File.ExtractCharacter(n.Id), "BR-Chan (KCOX, fresh)"))
                {
                    if (!Check(what + ": fresh node built", fresh.Ok, fresh.Error)) return;
                    string cd = ClothesDiff(fresh.Real.Context, n.Real.Context, rts: true);
                    var st = fresh.Real.Context.Stats.KcoxResized;
                    Check(what + ": = a fresh node (clothes digest with the RT read-backs, KCOX statistics)", cd == null && st == n.Real.Context.Stats.KcoxResized, (cd ?? "") + $"; KcoxResized fresh {st}, live {n.Real.Context.Stats.KcoxResized}");
                }
            }
            VsFreshNode("R3 as built");
            var e1 = n.Ops.EditAppearance(ids, new List<(string, object)> { ("appearance.coord.clothes.top.colors[0].base", new Color4(0.25f, 0.55f, 0.85f, 1)) }, User); n.Apply();
            var rt = col.Creator.Target;
            Check("R3 a colour edit: the colour creator keeps its live grown RT (no new creator)", e1.Ok && ctx.Edit.ClothesColor[0, 0] == col && rt && rt.width == 2 * w, $"op {e1}");
            VsFreshNode("R3 after a colour edit");
            string colHash = FaceDigest.RtHash(col.Creator.Target);
            var e2 = n.Ops.EditAppearance(ids, new List<(string, object)> { ("appearance.coord.clothes.top.colors[0].gloss", 0.81f) }, User); n.Apply();
            var fr = n.Real.LastEditFlush;
            Check("R3 a gloss edit rebuilds the detail creators only and leaves the colour RT (with the overlay) as it was",
                  e2.Ok && fr != null && fr.CreatorsRebuilt.Count > 0 && fr.CreatorsRebuilt.All(x => x.StartsWith("create_clothes detail ", StringComparison.Ordinal)) && FaceDigest.RtHash(col.Creator.Target) == colHash,
                  $"rebuilt [{string.Join(", ", fr?.CreatorsRebuilt ?? new List<string>())}]");
            VsFreshNode("R3 after a gloss edit");
            // the release: every creator's material and RT destroyed
            var mats = ctx.Edit.Composites().Select(x => x.Creator.Mat).ToList();
            var rts = ctx.Edit.Composites().Select(x => x.Creator.Target).ToList();
            n.Dispose();
            Check("R3 after the release: every creator material and RT destroyed", mats.Count > 0 && mats.All(m => !m) && rts.All(t => !t), $"{mats.Count(m => m)} materials and {rts.Count(t => t)} RTs of {mats.Count} creators alive");
        }
        finally { n.Dispose(); }
    }

    /// <summary>Renders a part's bounds into a 64² RT (a temporary camera and light) and returns the SHA-256 of the read-back
    /// (numbers only; nothing is saved).</summary>
    static string RenderPart(GameObject go)
    {
        if (!go) return null;
        var rends = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
        if (rends.Count == 0) return null;
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.forceMatrixRecalculationPerRender = true;
        var b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        var cam = new GameObject("MakerClothesTest cam").AddComponent<Camera>();
        var key = new GameObject("MakerClothesTest key").AddComponent<Light>();
        var rt = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32) { name = "MakerClothesTest part" };
        try
        {
            key.type = LightType.Directional; key.intensity = 1f; key.transform.rotation = Quaternion.Euler(30, 200, 0);
            HS2Next.RenderingLayers.ApplyToLight(key);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            cam.fieldOfView = 30; cam.nearClipPlane = 0.05f; cam.farClipPlane = 100;
            float d = Mathf.Max(b.extents.magnitude, 0.1f) / Mathf.Tan(15 * Mathf.Deg2Rad);
            cam.transform.position = b.center + Vector3.forward * d;
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

    public static void RunTiming()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        int reps = Reps(20);
        Rep.AppendLine($"maker clothes timing (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName}), {reps} repetitions, warm, edit mode; GPU times between 1×1 copy fences; redirect {RedirectState()}");
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        try
        {
            using (var h = MakerCoreTest.Harness.Open())
            {
                Info("redirect after the catalog's settings: " + RedirectState(h.Catalog));
                if (!FaceDigest.HasDevice) { Check("graphics device present", false); return; }
                int wreps = Math.Max(reps, 60);
                using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
                    if (bb.B.Ok) WU1(h, bb.B, wreps, "BR-Chan's top", 2, 1);
                h.ReleaseBundles();
                // the worst vanilla item: the most compositing pixels over its groups (two 2048² groups or more)
                string worst = WorstVanillaTop(h, out string worstWhat);
                Info("WU1 worst vanilla item: " + worstWhat);
                if (worst != null)
                    using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan + " + worst)))
                        if (bb.B.Ok) { SetItem(bb.B, 0, worst); WU1(h, bb.B, wreps, "the worst vanilla item " + worst, 4, 0); }
                h.ReleaseBundles();
                Guard("WU2 / swaps (scene node, op + flush)", () => WU2(h, reps));
                h.ReleaseBundles();
            }
        }
        finally { FaceSetup.Seed = fseed; ReleaseFence(); }
        Finish("maker_clothes_timing_report.txt", "MakerClothesTest.timing (WU1-WU3)", sw);
    }

    /// <summary>The vanilla 240 row with the most compositing pixels (main texture size of the groups that have a colour
    /// mask), read from the list columns and the textures' sizes (no instantiation).</summary>
    static string WorstVanillaTop(MakerCoreTest.Harness h, out string what)
    {
        string[] mains = { "MainTex", "MainTex02", "MainTex03" }, masks = { "ColorMaskTex", "ColorMask02Tex", "ColorMask03Tex" };
        long best = 0; string bestRef = null; what = "none";
        foreach (var r in MakerCatalog.For(h.Catalog).Rows(240, 1).Where(r => r.IsVanilla))
        {
            var e = h.Catalog.GetEntry(240, r.Key);
            if (e == null || !HasTex(e, "MainTex") || !HasTex(e, "ColorMaskTex")) continue;
            long px = 0; var parts = new List<string>();
            for (int j = 0; j < 3; j++)
            {
                if (!HasTex(e, mains[j]) || !HasTex(e, masks[j])) continue;
                var t = h.Catalog.LoadAsset<Texture2D>(e, "MainAB", mains[j]);
                if (!t) continue;
                px += (long)t.width * t.height; parts.Add(t.width + "x" + t.height);
            }
            if (px > best) { best = px; bestRef = r.Key.ToString(); what = $"240:{bestRef} ({string.Join(" + ", parts)}, {px / 1048576.0:F1} Mpx)"; }
        }
        return bestRef;
    }

    /// <summary>WU1: the GPU time of a colour drag frame's composites (the colour creators of kind 0's groups rebuilt; the
    /// per-creator rule of a base-colour edit) and of a gloss frame's (the detail creators), each between 1×1 fences, against
    /// the targets; the drag frame as the flush runs it (preview flush + its GPU work, wall time between fences) and the
    /// flush's CPU time; the full pass (both creators of every group, 4.3's rule) for comparison. <paramref name="reps"/>
    /// repetitions, medians.</summary>
    static void WU1(MakerCoreTest.Harness h, MakerCoreTest.Built live, int reps, string what, double colourTarget, double detailTarget)
    {
        var ctx = live.Context;
        var ed = ctx.Editor;
        var p = PartOf(ctx, 0);
        var col = ctx.Edit.ClothesColor[0, 0];
        if (!Check($"WU1 {what}: creators present", col != null && col.Creator.Target, ClothesReport.KindOf(ctx, 0)?.ToString())) return;
        var rt = col.Creator.Target;
        var colours = Enumerable.Range(0, 3).Select(j => ctx.Edit.ClothesColor[0, j]).Where(c => c != null).ToList();
        var details = Enumerable.Range(0, 3).Select(j => ctx.Edit.ClothesGloss[0, j]).Where(c => c != null).ToList();
        Info($"WU1 {what}: {colours.Count} groups, {string.Join(", ", colours.Select(c => c.Creator.Target.width + "x" + c.Creator.Target.height))}");
        // warm-up (shader variants, mip chains)
        for (int w = 0; w < 3; w++) { foreach (var c in colours) c.Creator.Rebuild(); foreach (var c in details) c.Creator.Rebuild(); Fence(rt); }
        double colourGpu = GpuMs(() => { foreach (var c in colours) c.Creator.Rebuild(); }, rt, reps);
        double detailGpu = GpuMs(() => { foreach (var c in details) c.Creator.Rebuild(); }, details[0].Creator.Target, reps);
        double fullGpu = GpuMs(() => { for (int j = 0; j < colours.Count; j++) { colours[j].Creator.Rebuild(); details[j].Creator.Rebuild(); } }, rt, reps);
        Target($"WU1 {what}: colour drag frame, GPU (the colour creators)", colourGpu, colourTarget);
        if (detailTarget > 0) Target($"WU1 {what}: gloss drag frame, GPU (the detail creators)", detailGpu, detailTarget);
        else Info($"WU1 {what}: gloss drag frame, GPU (the detail creators) {F(detailGpu)} ms (no target)");
        Info($"WU1 {what}: full pass GPU (both creators of every group, 4.3's rule) {F(fullGpu)} ms; per-creator rebuilds save {F(fullGpu - colourGpu)} ms on a colour frame, {F(fullGpu - detailGpu)} ms on a gloss frame");
        // the drag frames as the flush runs them (preview flush + GPU, wall time between fences) and the flush CPU time
        int i = 0;
        var flushMs = new List<double>();
        var reports = new List<FlushReport>();
        double colourFrame = GpuMs(() =>
        {
            float v = 0.2f + 0.5f * ((i++) % 7) / 7f;
            p.Colors[0].Base = new Color4(v, 0.4f, 0.6f, 1);
            ed.MarkDirty(Doc(0) + ".colors[0].base");
            var r = ed.Flush(FlushMode.Preview);
            flushMs.Add(r.Milliseconds); reports.Add(r);
        }, rt, reps);
        bool colourOnly = reports.All(r => r.ClothesCreatorsRebuilt.All(c => c.Colour && !c.Detail));
        Target($"WU1 {what}: colour drag frame, flush (CPU)", Median(flushMs), 0.5);
        Check($"WU1 {what}: the colour drag frames rebuilt the colour creators only", colourOnly && reports.Count > 0);
        Info($"WU1 {what}: colour drag frame wall (flush + GPU between fences) {F(colourFrame)} ms");
        reports.Clear(); flushMs.Clear();
        double detailFrame = GpuMs(() =>
        {
            p.Colors[0].Gloss = 0.1f + 0.8f * ((i++) % 9) / 9f;
            ed.MarkDirty(Doc(0) + ".colors[0].gloss");
            var r = ed.Flush(FlushMode.Preview);
            flushMs.Add(r.Milliseconds); reports.Add(r);
        }, details[0].Creator.Target, reps);
        bool detailOnly = reports.All(r => r.ClothesCreatorsRebuilt.All(c => c.Detail && !c.Colour));
        Check($"WU1 {what}: the gloss drag frames rebuilt the detail creators only", detailOnly && reports.Count > 0);
        Info($"WU1 {what}: gloss drag frame wall {F(detailFrame)} ms, flush (CPU) {F(Median(flushMs))} ms");
        ed.MarkUnit(ClothesNames.ColorsUnit(0));
        ed.Flush();
    }

    /// <summary>WU2 and the other op timings on a scene node (op + flush): pattern picks (cold = the first pick of a
    /// row, warm = a picked row again), break drag frames (preview flush), item swaps (warm).</summary>
    static void WU2(MakerCoreTest.Harness h, int reps)
    {
        var c = MakerEditTest.Import(h, FemaleCard, out string err);
        if (!Check("WU2 BR-Chan imported", c != null, err)) return;
        using (var n = MakerEditTest.NewNode(h, c, "BR-Chan"))
        {
            if (!Check("WU2 node built", n.Ok, n.Error)) return;
            var ids = new[] { n.Id };
            if (ClothesReport.KindOf(n.Real.Context, 0).OnePiece || Groups(n.Real.Context, 0) == 0) { n.Ops.ChangeItem(ids, "appearance.coord.clothes.top.item", TopBreak, User); n.Apply(); }
            const string pat = "appearance.coord.clothes.top.colors[0].pattern";
            var rows = h.Catalog.EntriesOf(348).Where(e => e.IsVanilla && e.Key.Id > 0 && HasTex(e, "MainTex")).Select(e => e.Key.ToString()).ToList();
            var cold = new List<double>(); var warm = new List<double>();
            foreach (var r in rows.Take(Math.Min(reps, rows.Count))) cold.Add(TimeOp(n, () => n.Ops.ChangeItem(ids, pat, r, User), out _));
            for (int i = 0; i < reps; i++) warm.Add(TimeOp(n, () => n.Ops.ChangeItem(ids, pat, rows[i % Math.Min(4, rows.Count)], User), out _));
            Target("WU2 pattern pick (op + flush), warm", Median(warm), 3);
            Target("WU2 pattern pick (op + flush), cold (the first pick of a row)", Median(cold), 60);
            // break drag frames: preview flushes of breakRate on a useBreak top
            n.Ops.ChangeItem(ids, "appearance.coord.clothes.top.item", TopBreak, User); n.Apply();
            var real = n.Real;
            var flush = new List<double>();
            bool keep = CharaEditFlush.ForcePreview;
            CharaEditFlush.ForcePreview = true;
            try
            {
                var tx = n.Session.Commands.BeginOrJoin("Drag", new ExecuteOptions { Source = CommandSource.User, Label = "Drag" });
                try
                {
                    for (int i = 0; i < reps; i++)
                    {
                        n.Ops.EditAppearance(ids, new List<(string, object)> { ("appearance.coord.clothes.top.breakRate", 0.1f + 0.04f * i) }, User);
                        n.Apply();
                        if (real.LastEditFlush != null) flush.Add(real.LastEditFlush.Milliseconds);
                    }
                    tx.Commit();
                }
                finally { tx.Dispose(); }
            }
            finally { CharaEditFlush.ForcePreview = keep; }
            n.Apply();
            Target("break drag frame (preview flush)", Median(flush), 0.1);
            // item swaps (warm): two tops in turn
            var swaps = new List<double>();
            for (int i = 0; i < reps + 2; i++)
            {
                string item = i % 2 == 0 ? Top3 : TopBreak;
                double ms = TimeOp(n, () => n.Ops.ChangeItem(ids, "appearance.coord.clothes.top.item", item, User), out _);
                if (i >= 2) swaps.Add(ms);
            }
            Target("clothes item swap (op + flush, warm)", Median(swaps), 100);
            // «初期カラー» (op + flush): the top's three sets back to the prefab's after a gloss change
            var dc = new List<double>();
            for (int i = 0; i < reps; i++)
            {
                n.Ops.EditAppearance(ids, new List<(string, object)> { ("appearance.coord.clothes.top.colors[0].gloss", 0.2f + 0.01f * i) }, User); n.Apply();
                dc.Add(TimeOp(n, () => n.Ops.ResetClothesColours(ids, 0, User), out _));
            }
            Target("«初期カラー» (op + flush)", Median(dc), 5);
            // WU3: coordinate applies (op + flush, warm; undone between the repetitions)
            var src = n.Ops.LoadCoordinate(CoordFile("female", "ガーリーブラウス.png"), null, out _, out var lf);
            if (src == null) { Warn("WU3 coordinate apply", "the coordinate card did not load: " + lf); return; }
            var bus = n.Session.Commands;
            foreach (var mode in new[] { CoordinateApplyMode.All, CoordinateApplyMode.Clothes, CoordinateApplyMode.Accessories })
            {
                var l = new List<double>();
                int rb = real.EditRebuilds;
                for (int i = 0; i < reps + 1; i++)
                {
                    double ms = TimeOp(n, () => n.Ops.ApplyCoordinate(ids, src, mode, User), out var ar);
                    if (i > 0) l.Add(ms);
                    bus.Undo(); n.Apply();
                }
                Target($"WU3 coordinate apply {mode} (op + flush, warm; BR-Chan → ガーリーブラウス){(real.EditRebuilds != rb ? " — REBUILDS " + (real.EditRebuilds - rb) : "")}", Median(l), 300);
            }
        }
    }

    // ================================================================ RunS1Explain (headless, for T)

    sealed class Classes
    {
        public readonly SortedDictionary<string, int> Lines = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, SortedDictionary<string, int>> PerCard = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
        public void Add(string klass, string card)
        {
            Lines[klass] = (Lines.TryGetValue(klass, out var n) ? n : 0) + 1;
            if (!PerCard.TryGetValue(klass, out var d)) PerCard[klass] = d = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            d[card] = (d.TryGetValue(card, out var c) ? c : 0) + 1;
        }
    }

    public static void RunS1Explain()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        List<string> cards; string what;
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
        bool keepRedirect = Flag("-redirect");
        var cls = new Classes();
        var others = new List<string>();
        int equal = 0, differ = 0, missing = 0, failed = 0, notChara = 0, n = 0;
        bool? before = null;
        try
        {
            using (var h = MakerCoreTest.Harness.Open())
            {
                if (!keepRedirect) { before = RedirectSwitch(h.Catalog); if (before != null) { RedirectSwitch(h.Catalog, false); h.Catalog.UnloadAll(); } }
                Rep.AppendLine($"maker clothes S1 explain (Unity {Application.unityVersion}): {cards.Count} files, {what}; redirect {RedirectState(h.Catalog)}{(keepRedirect ? " (kept)" : before == null ? " (no switch yet)" : " (switched off for the run: its lines are R's explain)")}; baselines {MakerCoreTest.BaselineDir}");
                foreach (var file in cards)
                {
                    n++;
                    if (n % 25 == 0) Debug.Log($"MakerClothesTest S1 explain: {n}/{cards.Count}, {sw.Elapsed.TotalSeconds:F0} s");
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
                        Classify(b, basis, s, cls, others);
                    }
                    catch (Exception e) { failed++; Check(Path.GetFileName(file) + ": no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace)); }
                    finally { b?.Dispose(); }
                    if (n % 10 == 0) h.ReleaseBundles();
                }
                if (before != null) { RedirectSwitch(h.Catalog, before.Value); h.Catalog.UnloadAll(); }
            }
        }
        catch (Exception e) { Check("S1 explain: no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace)); }
        Info($"{equal} equal, {differ} differ, {missing} without a baseline, {failed} failed, {notChara} not character cards");
        foreach (var kv in cls.Lines) Info($"class {kv.Key}: {kv.Value} lines on {cls.PerCard[kv.Key].Count} cards");
        foreach (var kc in cls.PerCard) foreach (var oc in kc.Value) Info($"{kc.Key} card: {oc.Key} ({oc.Value} lines)");
        foreach (var o in Flag("-other") ? others : others.Take(40)) Info("other: " + o);
        Check("S1 explain: every differing line is classified (clothes.break, clothes.state, me.shaderRegistry); 0 other", failed == 0 && !cls.Lines.ContainsKey("other"),
              $"other {(cls.Lines.TryGetValue("other", out var on) ? on : 0)} lines");
        Finish("maker_clothes_s1explain_report.txt", "MakerClothesTest.RunS1Explain", sw);
    }

    /// <summary>
    /// The class of one differing S1 line of a built card (null when it is not a clothes change): "clothes.break" for a
    /// material line of a renderer of a clothes kind's break groups on a slot other than 0 whose differing tokens are
    /// _AlphaEx and / or the copy's name suffix " (Instance)" (4.4 writes the break on slot 0 only and no longer instantiates
    /// the other slots), "clothes.state" for an object line under a clothes part whose only
    /// difference is the activity. Public so that the redirect's explain can recognise these lines on the same cards.
    /// </summary>
    public static string ClothesClass(CharaContext ctx, Dictionary<Transform, string> paths, string key, string before, string after)
    {
        if (ctx?.Clothes == null || before == null || after == null) return null;
        if (key.StartsWith("mat|", StringComparison.Ordinal))
        {
            string rend = key.Substring(4); int br = rend.LastIndexOf('[');
            if (br <= 0 || !int.TryParse(rend.Substring(br + 1).TrimEnd(']'), out int slot)) return null;
            rend = rend.Substring(0, br);
            var tokens = TokenNames(before, after);
            // _AlphaEx (the break on slot 0 only) and the copy's name: slots past 0 are no longer instantiated by the break unit
            // (named "<name> (Instance)") but copied by the ownership step (the name kept), as the game leaves them shared
            if (slot < 1 || tokens.Count == 0 || tokens.Any(t => t != "_AlphaEx" && t != "name")) return null;
            if (tokens.Contains("name") && NameToken(before) != NameToken(after)) return null;
            for (int k = 0; k < ctx.Clothes.Length; k++)
            {
                var cmp = ctx.Clothes[k] ? ctx.Clothes[k].GetComponent<CmpClothes>() : null;
                if (cmp == null || cmp.useBreak == 0) continue;
                foreach (var r in ClothesBreakUnit.BreakRenderers(cmp))
                    if (paths.TryGetValue(r.transform, out var p) && p == rend) return "clothes.break";
            }
            return null;
        }
        if (key.StartsWith("obj|", StringComparison.Ordinal))
        {
            var tokens = TokenNames(before, after);
            if (tokens.Count == 0 || tokens.Any(t => t != "active")) return null;
            string path = key.Substring(4);
            foreach (var go in ctx.Clothes)
                if (go && paths.TryGetValue(go.transform, out var gp) && (path == gp || path.StartsWith(gp + "/", StringComparison.Ordinal))) return "clothes.state";
        }
        return null;
    }

    /// <summary>The material name token of a snapshot material line without the instance suffix ("_(Instance)").</summary>
    static string NameToken(string line)
    {
        int i = line.IndexOf("name=", StringComparison.Ordinal);
        if (i < 0) return null;
        int e = line.IndexOf(' ', i);
        string n = e < 0 ? line.Substring(i + 5) : line.Substring(i + 5, e - i - 5);
        const string suffix = "_(Instance)";
        return n.EndsWith(suffix, StringComparison.Ordinal) ? n.Substring(0, n.Length - suffix.Length) : n;
    }

    static void Classify(MakerCoreTest.Built b, CharaSnapshot basis, CharaSnapshot now, Classes cls, List<string> others)
    {
        string card = Path.GetFileName(b.File);
        var paths = SnapPaths(b.Root.transform);
        var a = Keyed(basis.Lines); var c = Keyed(now.Lines);
        foreach (var kv in a.Concat(c.Where(x => !a.ContainsKey(x.Key))))
        {
            a.TryGetValue(kv.Key, out var la); c.TryGetValue(kv.Key, out var lc);
            if (la == lc) continue;
            string klass = ClothesClass(b.Context, paths, kv.Key, la, lc) ?? MaterialEditorShaderTest.RegistryClass(b.Context, paths, a, c, kv.Key, la, lc) ?? "other";
            if (klass == "other") others.Add($"{card} {Short(kv.Key, 160)}: " + (la != null && lc != null ? "tokens [" + string.Join(",", TokenNames(la, lc)) + "] " : "") + $"{Short(la, 160)} <> {Short(lc, 160)}");
            cls.Add(klass, card);
        }
    }

    /// <summary>The snapshot path of a transform (CharaSnapshot's: root name, "/" + child name, "[k]" for the k-th
    /// same-named sibling).</summary>
    public static Dictionary<Transform, string> SnapPaths(Transform root)
    {
        var d = new Dictionary<Transform, string>();
        void Walk(Transform t, string p)
        {
            d[t] = p;
            var count = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < t.childCount; i++)
            {
                var ch = t.GetChild(i);
                int k = count.TryGetValue(ch.name, out var v) ? v + 1 : 0;
                count[ch.name] = k;
                Walk(ch, p + "/" + ch.name + (k == 0 ? "" : "[" + k + "]"));
            }
        }
        Walk(root, root.name);
        return d;
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

    /// <summary>The names of the "name=value" tokens that differ between two snapshot lines (tokens split on spaces outside
    /// parentheses; a token without '=' is its own name).</summary>
    static List<string> TokenNames(string a, string b)
    {
        Dictionary<string, string> T(string s)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            int depth = 0, start = 0;
            for (int i = 0; i <= s.Length; i++)
            {
                char ch = i < s.Length ? s[i] : ' ';
                if (ch == '(') depth++;
                else if (ch == ')') depth = Math.Max(0, depth - 1);
                else if (ch == ' ' && depth == 0)
                {
                    if (i > start) { string t = s.Substring(start, i - start); int e = t.IndexOf('='); d[e > 0 ? t.Substring(0, e) : t] = t; }
                    start = i + 1;
                }
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
