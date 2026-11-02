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
//     WT13 (added after 1.0.0) parts drawn with the body's materials: the rules on objects made under the body top
//          (registration, a target owns no material, the ownership step and a release leave the body's materials, KCOX passes
//          over targets, the copy, destroyed targets dropped); the socks roy12.skindentations#1 on BR-Chan: o_socks_thigh2
//          draws the body's array (the same objects) after the load, a socks colour edit (the body's materials unchanged),
//          skin colour and gloss edits; the release keeps the body's materials; G1; edit = fresh build
//     WT14 (added after 1.0.0) bone implants: the rules on objects made under the body top (the implant moves trfSrc
//          under the skeleton bone named like trfDst with its local transform, a renderer bone neither implanted nor in the
//          skeleton becomes null, invalid pairs and unknown targets are skipped, a part without implants keeps its missing
//          bones, DestroyImplants); the top roy12.piggytop#0 on BR-Chan: cf_J_Belly_Pivot under cf_J_Kosi01_s with the
//          prefab's local transform, every renderer bone the skeleton's or implanted (20 slots on implanted bones), the skeleton
//          copy gone, the part's dynamic bone rooted on the implant; the release destroys the implant; G1; edit = fresh build
//     WT15 (added after 1.0.0) body cuts: the rules on objects made under the body top (registration, the switch, the
//          first slot and its replaced copy, a disabled component, the second cut body, Invisible Body, a destroyed part); the
//          top K044#725044 on a BR-Chan node: dressed = the body off and the cut body on with the body's first material; half
//          and off = the body on; dressed again; socks com.3dh9527.newsqueezestocking.x6#77778 on top of it (switch always
//          active); the top and then the socks taken off = the body on, no cuts; G1; node = a fresh node in each state
//     WT16 (added after 1.0.0) heels: the element's registration and parse rules and one joint's pose on synthetic
//          input; the first installed heel shoes with fixed toes and clamped rolls on BR-Chan, the motion stepped by hand: the
//          body lifted by root.y, the six foot joints = an independent computation from their bases, still steps do not
//          accumulate (also with a bone modifier on a foot joint), shoes off = no lift and the bases back, a body-height edit
//          then the card's shoes = no lift left behind; through a BR-Chan node the clothes state sets ShoesOn; G1
//     WT17 (added after 1.0.0) additional bones: the line rules of additional_bones and the move on objects made under
//          the body top and under the head bone (a listed bone moves under the transform named like its parent with its local
//          transform, a renderer keeps it unless the skeleton has its name, a bone whose parent name is nowhere stays in the
//          part, a part without a line keeps its missing bones, DestroyAdditionalBones); the head Roy12.Sarah#76 on BR-Chan:
//          cf_J_CustomBones and cf_J_CustomMouth under the head skeleton, every o_head bone the skeleton's or moved; the top
//          tAOb_Nico_Robin_NewWorld#444440986: three back bones under cf_J_Spine03 with their dynamic bones as chains; the
//          releases destroy the moved bones; G1; edit = fresh build
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
                if (Want("WT13")) Guard("WT13 parts drawn with the body's materials", () => WT13(h));
                if (Want("WT14")) Guard("WT14 bone implants", () => WT14(h));
                if (Want("WT15")) Guard("WT15 body cuts", () => WT15(h));
                if (Want("WT16")) Guard("WT16 heels", () => WT16(h));
                if (Want("WT17")) Guard("WT17 additional bones", () => WT17(h));
            }
        }
        finally
        {
            FaceSetup.Seed = fseed;
            ClothesPartUnit.RestoreChainsBeforeRelease = true;
            CharaStateRealizer.ReapplyAfterObjectsReset = true;
        }
        Finish("maker_clothes_report.txt", "MakerClothesTest UM, WT1-WT17", sw);
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

    // ---------------------------------------------------------------- WT17 (after 1.0.0)

    const string SarahHead = "Roy12.Sarah#76";                      // 210 "sarah": cf_J_CustomBones, cf_J_CustomMouth
    const string RobinTop = "tAOb_Nico_Robin_NewWorld#444440986";   // 240: eight arm bones and three back bones with dynamic bones

    static void WT17(MakerCoreTest.Harness h)
    {
        WT17Lines();
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            WT17Rules(live.Context);
            if (Installed(h, 210, SarahHead, "WT17 head")) WT17Head(h, live);
            if (Installed(h, 240, RobinTop, "WT17 top")) WT17Top(h, live);
        }
        h.ReleaseBundles();
    }

    /// <summary>Renderer bone slots of <paramref name="part"/>: on a transform of <paramref name="moved"/>, without a bone (null,
    /// or lost with the skeleton copy), and on any other transform outside <paramref name="skeleton"/>.</summary>
    static (int moved, int lost, int other) Slots(GameObject part, HashSet<Transform> moved, HashSet<Transform> skeleton)
    {
        int m = 0, l = 0, o = 0;
        foreach (var smr in part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            foreach (var b in smr.bones)
            {
                if (!b) l++;
                else if (moved.Contains(b)) m++;
                else if (!skeleton.Contains(b)) o++;
            }
        return (m, l, o);
    }

    static int NullSlots(GameObject prefab) =>
        prefab ? prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(r => r.bones.Count(b => !b)) : -1;

    static HashSet<Transform> Below(IEnumerable<Transform> roots) =>
        new HashSet<Transform>((roots ?? Enumerable.Empty<Transform>()).Where(r => r).SelectMany(r => r.GetComponentsInChildren<Transform>(true)));

    static void WT17Head(MakerCoreTest.Harness h, MakerCoreTest.Built live)
    {
        var ctx = live.Context;
        string was = ctx.Chara.Face.Head;
        var row = RowOf(h.Catalog, 210, SarahHead);
        var prefab = h.Catalog.LoadAsset<GameObject>(row, "MainAB", ctx.Edit.DataKey(row));
        SetM(live, "chara.face.head", () => ctx.Chara.Face.Head = SarahHead);
        var head = ctx.Head;
        var roots = ctx.Edit.AdditionalBonesOf(head)?.ToList() ?? new List<Transform>();
        ctx.HeadBones.TryGetValue("cf_J_FaceBase", out var faceBase);
        ctx.HeadBones.TryGetValue("cf_J_MouthMove", out var mouthMove);
        bool Placed(string name, Transform parent)
        {
            var r = roots.FirstOrDefault(t => t && t.name == name);
            var src = prefab ? EditContext.Find(prefab.transform, name) : null;
            return r && src && parent && r.parent == parent && !r.IsChildOf(head.transform)
                   && r.localPosition == src.localPosition && r.localRotation == src.localRotation && r.localScale == src.localScale;
        }
        Check("WT17 210:" + SarahHead + ": cf_J_CustomBones under the head skeleton's cf_J_FaceBase, cf_J_CustomMouth under its cf_J_MouthMove, with the prefab's local transforms",
              head && roots.Count == 2 && Placed("cf_J_CustomBones", faceBase) && Placed("cf_J_CustomMouth", mouthMove),
              string.Join(", ", roots.Select(r => r ? r.name + " under " + (r.parent ? r.parent.name : "-") : "destroyed")));
        var moved = Below(roots);
        var (m, l, o) = head ? Slots(head, moved, new HashSet<Transform>(ctx.HeadBones.Values)) : (0, -1, 0);
        int pn = NullSlots(prefab);
        Check("WT17 every renderer bone of the head is the head skeleton's or moved (113 slots on moved bones, none lost); the skeleton copy is gone",
              m == 113 && l == pn && o == 0 && head && !EditContext.Find(head.transform, "cf_J_FaceRoot"), $"moved {m}, without a bone {l} (prefab {pn}), other {o}");
        var s = live.Snapshot();
        Check("WT17 Sarah head: G1", s.G1Pass, s.G1Text);
        FreshOk("WT17 Sarah head", h, live);
        SetM(live, "chara.face.head", () => ctx.Chara.Face.Head = was);
        Check("WT17 Sarah head -> the card's head: the moved bones destroyed with the head",
              roots.All(r => !r) && faceBase && !EditContext.Find(faceBase, "cf_J_CustomBones") && mouthMove && !EditContext.Find(mouthMove, "cf_J_CustomMouth")
              && ctx.Edit.AdditionalBonesOf(ctx.Head) == null);
        s = live.Snapshot();
        Check("WT17 after the head release: G1", s.G1Pass, s.G1Text);
        FreshOk("WT17 head back", h, live);
    }

    static void WT17Top(MakerCoreTest.Harness h, MakerCoreTest.Built live)
    {
        var ctx = live.Context;
        string was = PartOf(ctx, 0).Item;
        SetItem(live, 0, RobinTop);
        var part = ctx.Clothes[0];
        var roots = ctx.Edit.AdditionalBonesOf(part)?.ToList() ?? new List<Transform>();
        Transform Bone(string n) => ctx.BodyBones.TryGetValue(n, out var t) ? t : null;
        var back = roots.Where(r => r && r.name.StartsWith("cf_J_back_01_", StringComparison.Ordinal)).ToList();
        var arms = roots.Where(r => r && r.name.StartsWith("cf_J_ArmOut", StringComparison.Ordinal)).ToList();
        Check("WT17 240:" + RobinTop + ": three back bones under the skeleton's cf_J_Spine03, eight arm bones under cf_J_ArmUp00_L / _R",
              part && roots.Count == 11 && back.Count == 3 && arms.Count == 8 && back.All(r => r.parent == Bone("cf_J_Spine03"))
              && arms.All(r => r.parent == Bone(r.name.EndsWith("_L", StringComparison.Ordinal) ? "cf_J_ArmUp00_L" : "cf_J_ArmUp00_R"))
              && roots.All(r => !r.IsChildOf(part.transform)),
              string.Join(", ", roots.Select(r => r ? r.name + " under " + (r.parent ? r.parent.name : "-") : "destroyed")));
        var moved = Below(roots);
        var (m, l, o) = part ? Slots(part, moved, new HashSet<Transform>(ctx.BodyBones.Values)) : (0, -1, 0);
        int pn = NullSlots(h.Catalog.LoadAsset<GameObject>(RowOf(h.Catalog, 240, RobinTop), "MainAB", ctx.Edit.DataKey(RowOf(h.Catalog, 240, RobinTop))));
        Check("WT17 the top's renderer slots on moved bones (44), none lost; the skeleton copy is gone",
              m == 44 && l == pn && part && !EditContext.Find(part.transform, "cf_J_Root"), $"moved {m}, without a bone {l} (prefab {pn}), other {o}");
        // the dynamic bones sit on the back bones and move with them under the skeleton: chains of the stage, outside the part
        var dbs = back.SelectMany(r => r.GetComponentsInChildren<global::DynamicBone>(true)).ToList();
        var stage = ctx.Motion?.Get<DynamicBoneStage>();
        Check("WT17 the three dynamic bones on the back bones are chains (category other: no longer under the part), rooted on moved bones",
              dbs.Count == 3 && stage != null && dbs.All(db => db.m_Root && moved.Contains(db.m_Root)
                  && stage.Chains.Any(ch => ch.Source == db && ch.Category == DynamicBoneCategory.Other)),
              string.Join(", ", dbs.Select(db => (db.m_Root ? db.m_Root.name : "null") + " " + (stage?.Chains.FirstOrDefault(ch => ch.Source == db)?.Category.ToString() ?? "no chain"))));
        var s = live.Snapshot();
        Check("WT17 Robin top: G1", s.G1Pass, s.G1Text);
        FreshOk("WT17 Robin top", h, live);
        var spine = Bone("cf_J_Spine03");
        SetItem(live, 0, was);
        Check("WT17 Robin top -> the card's top: the moved bones destroyed with the part, no chain left on them",
              roots.All(r => !r) && spine && !EditContext.Find(spine, "cf_J_back_01_C") && ctx.Edit.AdditionalBonesOf(part) == null
              && (stage == null || stage.Chains.All(ch => ch.Source)));
        s = live.Snapshot();
        Check("WT17 after the top release: G1", s.G1Pass, s.G1Text);
        FreshOk("WT17 top back", h, live);
    }

    /// <summary>line rules of additional_bones (Unity-free).</summary>
    static void WT17Lines()
    {
        const string text = "p_a\tx\ty\r\n\r\np_b\tz\t\np_b\tw\n";
        var a = AdditionalBones.Parse(text, "p_a");
        var b = AdditionalBones.Parse(text, "p_b");
        var only = AdditionalBones.Parse("p_a", "p_a");
        Check("WT17 lines: the first line naming the prefab (CRLF and LF, empty lines dropped, empty fields kept), names compared ordinally",
              a != null && a.SetEquals(new[] { "x", "y" }) && b != null && b.SetEquals(new[] { "z", "" }) && only != null && only.Count == 0
              && AdditionalBones.Parse(text, "P_A") == null && AdditionalBones.Parse(text, "p_c") == null && AdditionalBones.Parse("", "p_a") == null,
              $"p_a [{(a == null ? "null" : string.Join(",", a))}], p_b [{(b == null ? "null" : string.Join(",", b))}]");
    }

    /// <summary>move rules on objects made under the character's body top and head bone (parts with a skeleton copy,
    /// bones and a skinned renderer, rebound to the body or head skeleton).</summary>
    static void WT17Rules(CharaContext ctx)
    {
        var x = ctx.Edit;
        Transform Child(Transform p, string n) { var t = new GameObject(n).transform; t.SetParent(p, false); return t; }
        // a body part: cf_J_Root/cf_J_Kosi01_s/wt17_extra/{wt17_tip, cf_J_Kosi02}, cf_J_Root/wt17_nowhere/wt17_lost, wt17_stray
        var part = Child(ctx.BodyTop.transform, "wt17_part");
        var copy = Child(part, "cf_J_Root");
        var kosi = Child(copy, "cf_J_Kosi01_s");
        var extra = Child(kosi, "wt17_extra");
        extra.localPosition = new Vector3(0.1f, 0.2f, 0.3f); extra.localRotation = Quaternion.Euler(10, 20, 30); extra.localScale = new Vector3(1, 2, 1);
        var tip = Child(extra, "wt17_tip");
        var named = Child(extra, "cf_J_Kosi02");
        var lost = Child(Child(copy, "wt17_nowhere"), "wt17_lost");
        var stray = Child(part, "wt17_stray");
        var smr = Child(part, "o_wt17").gameObject.AddComponent<SkinnedMeshRenderer>();
        smr.bones = new[] { kosi, extra, tip, named, lost, stray, null };
        // a head part first under p_cf_head_bone, so a search through it would find its own cf_J_FaceBase first
        var head = Child(ctx.HeadBone.transform, "wt17_head");
        head.SetSiblingIndex(0);
        var ownBase = Child(Child(head, "cf_J_FaceRoot"), "cf_J_FaceBase");
        var face = Child(ownBase, "wt17_face");
        var hsmr = Child(head, "o_wt17_head").gameObject.AddComponent<SkinnedMeshRenderer>();
        hsmr.bones = new[] { ownBase, face };
        // a part without a line
        var plain = Child(ctx.BodyTop.transform, "wt17_plain");
        var plainExtra = Child(Child(Child(plain, "cf_J_Root"), "cf_J_Kosi01_s"), "wt17_plain_extra");
        var psmr = Child(plain, "o_wt17_plain").gameObject.AddComponent<SkinnedMeshRenderer>();
        psmr.bones = new[] { plainExtra };
        try
        {
            x.RegisterAdditionalBones(part.gameObject, new[] { "wt17_extra", "wt17_lost", "" });
            x.RegisterAdditionalBones(head.gameObject, new[] { "wt17_face" });
            x.Rebind(part.gameObject, ctx.BodyBones, "cf_J_Root", ctx.Hips);
            x.Rebind(head.gameObject, ctx.HeadBones, "cf_J_FaceRoot", null);
            x.Rebind(plain.gameObject, ctx.BodyBones, "cf_J_Root", ctx.Hips);
            ctx.BodyBones.TryGetValue("cf_J_Kosi01_s", out var skelKosi);
            ctx.BodyBones.TryGetValue("cf_J_Kosi02", out var skelKosi2);
            ctx.HeadBones.TryGetValue("cf_J_FaceBase", out var skelBase);
            var bones = smr.bones;
            Check("WT17 rules: a listed bone moves under the skeleton bone named like its parent with its local transform, its children with it",
                  extra && extra.parent == skelKosi && extra.localPosition == new Vector3(0.1f, 0.2f, 0.3f) && extra.localRotation == Quaternion.Euler(10, 20, 30)
                  && extra.localScale == new Vector3(1, 2, 1) && tip && tip.parent == extra && named && named.parent == extra
                  && x.AdditionalBonesOf(part.gameObject)?.Count == 1, $"extra under {(extra && extra.parent ? extra.parent.name : "-")}");
            Check("WT17 rules: the renderer keeps the moved bones, takes the skeleton's for a skeleton name (also inside a moved bone), keeps a missing bone outside the copy; a bone whose parent name is nowhere stays in the part and goes with the copy; a null stays null",
                  bones.Length == 7 && bones[0] == skelKosi && bones[1] == extra && bones[2] == tip && bones[3] == skelKosi2 && !bones[4] && bones[5] == stray && bones[6] == null,
                  string.Join(", ", bones.Select(b => ReferenceEquals(b, null) ? "null" : b ? b.name + (b.parent ? " < " + b.parent.name : "") : "destroyed")));
            Check("WT17 rules: a head part searches from p_cf_head_bone without entering itself: its bone under the head skeleton's cf_J_FaceBase",
                  face && face.parent == skelBase && hsmr.bones[0] == skelBase && hsmr.bones[1] == face && x.AdditionalBonesOf(head.gameObject)?.Count == 1,
                  $"face under {(face && face.parent ? face.parent.name + (face.parent == skelBase ? " (skeleton)" : " (other)") : "-")}");
            Check("WT17 rules: a part without a line moves nothing (its missing bone stays in its slot and goes with the copy, 1.0 behaviour)",
                  x.AdditionalBonesOf(plain.gameObject) == null && !plainExtra && !psmr.bones[0] && skelKosi && !EditContext.Find(skelKosi, "wt17_plain_extra"));
            int n = x.DestroyAdditionalBones(part.gameObject) + x.DestroyAdditionalBones(head.gameObject);
            Check("WT17 rules: DestroyAdditionalBones removes the moved bones from the skeletons", n == 2 && !extra && !face
                  && x.AdditionalBonesOf(part.gameObject) == null && x.AdditionalBonesOf(head.gameObject) == null);
        }
        finally
        {
            x.DestroyAdditionalBones(part.gameObject); x.DestroyAdditionalBones(head.gameObject);
            Object.DestroyImmediate(part.gameObject); Object.DestroyImmediate(head.gameObject); Object.DestroyImmediate(plain.gameObject);
        }
    }

    // ---------------------------------------------------------------- WT16 (after 1.0.0)

    static readonly string[] HeelJoints = { "cf_J_Foot01_L", "cf_J_Foot02_L", "cf_J_Foot01_R", "cf_J_Foot02_R", "cf_J_Toes01_L", "cf_J_Toes01_R" };

    static void WT16(MakerCoreTest.Harness h)
    {
        WT16Rules();
        var lists = MakerCatalog.For(h.Catalog);
        var rows = h.Catalog.EntriesOf(HeelsData.FemaleShoesCategory).Where(e => HeelsData.For(e) != null && lists.IsListed(247, 1, e.Key))
                    .OrderBy(e => e.Key.ToString(), StringComparer.Ordinal).ToList();
        var row = rows.FirstOrDefault(e => { var x = HeelsData.For(e); return x.Toes.Fixed && x.Ankle.RollMax != Vector3.zero && x.Root.y > 0; });
        Info($"WT16 female shoe rows with heel data {rows.Count}; using {row?.Key}");
        if (!Check("WT16 installed heel shoes with fixed toes and clamped rolls", row != null, rows.Count + " rows")) return;
        var heel = HeelsData.For(row);
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            var lift = ctx.Motion?.Get<HS2Next.Motion.Heels.HeelsLift>();
            var foot = ctx.Motion?.Get<HS2Next.Motion.Heels.HeelsFoot>();
            if (!Check("WT16 the heel stages are attached", lift != null && foot != null && lift.Height && foot.Joints.All(j => j))) return;
            string was = PartOf(ctx, 7).Item;
            ctx.Motion.Step(1f / 60f);
            Check("WT16 the card's shoes: no lift, the joints untouched", !lift.Lifted && !foot.Written && lift.Height.localPosition == Vector3.zero);
            SetItem(live, 7, row.Key.ToString());
            var joints = foot.Joints;
            var bp = joints.Select(j => j.localPosition).ToArray(); var br = joints.Select(j => j.localRotation).ToArray(); var bs = joints.Select(j => j.localScale).ToArray();
            ctx.Motion.Step(1f / 60f);
            Check($"WT16 247:{row.Key}: the body lifted by root.y", lift.Lifted && lift.Height.localPosition == new Vector3(0, heel.Root.y, 0), lift.Height.localPosition.ToString("F4"));
            string Expect(out bool ok)
            {
                // independent: local = base · AngleAxis(x, right) · AngleAxis(y, up) · AngleAxis(z, forward); "fixed" first takes the
                // parent's world rotation (toes: the posed foot), position + move, scale = the heel's
                ok = true;
                var sb = new StringBuilder();
                var parts = new[] { heel.Ankle, heel.Foot, heel.Ankle, heel.Foot, heel.Toes, heel.Toes };
                for (int i = 0; i < 6; i++)
                {
                    var j = parts[i];
                    bool clamp = j.RollMin != Vector3.zero || j.RollMax != Vector3.zero;
                    float C(float v, float lo, float hi) => clamp ? Mathf.Clamp(v, lo, hi) : v;
                    var start = br[i];
                    if (j.Fixed) start = Quaternion.Inverse(joints[i].parent.rotation) * Quaternion.Euler(joints[i].parent.eulerAngles);
                    var e = start * Quaternion.AngleAxis(C(j.Roll.x, j.RollMin.x, j.RollMax.x), Vector3.right) * Quaternion.AngleAxis(C(j.Roll.y, j.RollMin.y, j.RollMax.y), Vector3.up)
                            * Quaternion.AngleAxis(C(j.Roll.z, j.RollMin.z, j.RollMax.z), Vector3.forward);
                    float ang = Quaternion.Angle(e, joints[i].localRotation);
                    bool pos = (joints[i].localPosition - (bp[i] + j.Move)).sqrMagnitude < 1e-10f, scl = joints[i].localScale == j.Scale;
                    if (ang > 0.01f || !pos || !scl) { ok = false; sb.Append($"{HeelJoints[i]}: {ang:F4}° pos {pos} scale {scl}; "); }
                }
                return sb.ToString();
            }
            string d1 = Expect(out bool ok1);
            Check("WT16 the six foot joints = the independent computation from their bases", ok1 && foot.Written, d1);
            var after1 = joints.Select(j => j.localRotation).ToArray();
            for (int k = 0; k < 3; k++) ctx.Motion.Step(1f / 60f);
            Check("WT16 three more still steps: the joints and the lift unchanged (no accumulation)",
                  joints.Select(j => j.localRotation).Zip(after1, (a, b) => Quaternion.Angle(a, b)).All(a => a < 1e-3f) && lift.Height.localPosition.y == heel.Root.y);
            // a bone modifier on the left foot joint: the two latch-style writers do not feed each other on a still pose
            var mod = new HS2Next.Chara.Ext.BoneModifierApplier.Modifier { Target = joints[1], Bone = HeelJoints[1], Source = "wt16", Rotation = new Vector3(10, 0, 0) };
            bool own = !ctx.Bones;   // a card without bone modifiers has no component: one for the check, removed after it
            if (own) ctx.Bones = ctx.Root.AddComponent<HS2Next.Chara.Ext.BoneModifierApplier>();
            ctx.Bones.Add(mod);
            var seen = new List<Quaternion>();
            for (int k = 0; k < 4; k++) { ctx.Motion.Step(1f / 60f); ctx.Bones.ApplyNow(); seen.Add(joints[1].localRotation); }
            Check("WT16 with a bone modifier on cf_J_Foot02_L: steps and modifier passes alternate without accumulating",
                  seen.Skip(1).All(q => Quaternion.Angle(q, seen[0]) < 1e-3f), string.Join(", ", seen.Select(q => q.eulerAngles.ToString("F2"))));
            ctx.Bones.Remove("wt16");
            ctx.Bones.ApplyNow();   // the removal restores the bone at the next pass (the LateUpdate in play)
            if (own) { Object.DestroyImmediate(ctx.Bones); ctx.Bones = null; }
            ctx.Motion.Step(1f / 60f);
            string d2 = Expect(out bool ok2);
            Check("WT16 the modifier removed: the joints = the computation again", ok2, d2);
            lift.State.ShoesOn = false;
            ctx.Motion.Step(1f / 60f);
            Check("WT16 shoes off: no lift, the joints back to their bases", !lift.Lifted && lift.Height.localPosition == Vector3.zero && !foot.Written
                  && Enumerable.Range(0, 6).All(i => Quaternion.Angle(joints[i].localRotation, br[i]) < 1e-3f && joints[i].localScale == bs[i]));
            lift.State.ShoesOn = true;
            ctx.Motion.Step(1f / 60f);
            // a body-height edit re-captures the shape bones (cf_N_height among them) while the body is lifted
            SetM(live, "chara.body.shape[0]", () => ctx.Chara.Body.Shape[0] = 0.62f);
            SetItem(live, 7, was);
            for (int k = 0; k < 3; k++) ctx.Motion.Step(1f / 60f);
            Check("WT16 a height edit while lifted, then the card's shoes: no lift left behind", !lift.Lifted && lift.Height.localPosition == Vector3.zero,
                  lift.Height.localPosition.ToString("F4"));
            var s = live.Snapshot();
            Check("WT16 after the heels: G1", s.G1Pass, s.G1Text);
        }
        h.ReleaseBundles();
        // the scene's clothes state reaches the stage
        var c = MakerEditTest.Import(h, FemaleCard, out string err);
        if (!Check("WT16 BR-Chan imported", c != null, err)) return;
        var n = MakerEditTest.NewNode(h, c, "BR-Chan");
        try
        {
            if (!Check("WT16 node built", n.Ok, n.Error)) return;
            var ids = new[] { n.Id };
            n.Ops.ChangeItem(ids, "appearance.coord.clothes.shoes.item", row.Key.ToString(), User); n.Apply();
            var st = n.Real.Context.Motion.Get<HS2Next.Motion.Heels.HeelsLift>().State;
            bool on = st.ShoesOn && st.Active != null;
            n.Ops.SetClothes(ids, 7, ClothesStateSet.Off, false, User); n.Apply();
            bool off = !st.ShoesOn && st.Active == null;
            n.Ops.SetClothes(ids, 7, ClothesStateSet.On, false, User); n.Apply();
            Check("WT16 node: shoes on = heels in effect, off = none, on again = in effect", on && off && st.ShoesOn && st.Active == heel);
        }
        finally { n.Dispose(); h.ReleaseBundles(); }
    }

    /// <summary>rules on synthetic input: registration order and stops, the vector parse, defaults, and one joint's pose.</summary>
    static void WT16Rules()
    {
        string X(string heels) => "<AI_HeelsData>" + heels + "</AI_HeelsData>";
        const string H = "<root vec=\"0,0.5,0\"/><foot01><roll vec=\"20,0,0\" min=\"0,0,0\" max=\"30,0,0\"/></foot01><foot02><roll vec=\"-5,0,0\"/></foot02><toes01 fixed=\"1\"><roll vec=\"-10, 0 ,0\"/></toes01>";
        var ids = new HashSet<int> { 0, 3, 7 };
        var t1 = HeelsData.Read(X($"<heel id=\"3\">{H}</heel><heel id=\"-1\">{H}</heel><heel id=\"0\">{H}</heel><heel id=\"9\">{H}</heel><heel id=\"7\">{H}</heel>"), ids);
        Check("WT16 rules: entries register in order, a negative id is skipped, id 0 is a shoe, an id that is no shoe row stops the rest",
              t1.Count == 2 && t1.ContainsKey(3) && t1.ContainsKey(0) && !t1.ContainsKey(7), string.Join(",", t1.Keys));
        var t2 = HeelsData.Read(X($"<heel id=\"3\">{H}</heel><heel id=\"3\">{H}</heel><heel id=\"7\">{H}</heel>"), ids);
        var t3 = HeelsData.Read(X($"<heel id=\"x\">{H}</heel><heel id=\"7\">{H}</heel>"), ids);
        Check("WT16 rules: an id registered twice stops the rest; an id that is no integer stops the manifest", t2.Count == 1 && t3.Count == 0);
        var e = t1[3];
        Check("WT16 rules: root, roll with min / max, fixed, spaces in a vector, defaults (move 0, scale 1)",
              e.Root == new Vector3(0, 0.5f, 0) && e.Ankle.Roll == new Vector3(20, 0, 0) && e.Ankle.RollMax == new Vector3(30, 0, 0) && e.Foot.Roll.x == -5
              && e.Toes.Fixed && !e.Ankle.Fixed && e.Toes.Roll == new Vector3(-10, 0, 0) && e.Ankle.Move == Vector3.zero && e.Foot.Scale == Vector3.one);
        var t4 = HeelsData.Read(X("<heel id=\"7\"><root vec=\"0,1\"/><foot01><scale vec=\"1,1,1.2\"/></foot01></heel>"), ids);
        var f = t4[7];
        Check("WT16 rules: a vector of two numbers is the default; a missing joint element leaves the joint all zero (scale 0, as the plugin)",
              f.Root == Vector3.zero && f.Ankle.Scale == new Vector3(1, 1, 1.2f) && f.Foot.Scale == Vector3.zero && f.Toes.Scale == Vector3.zero);
        Check("WT16 rules: a male shoe row (147) and no row have no heel data",
              HeelsData.For(new ListEntry { Category = 147, Key = new ContentId("x", 3) }) == null && HeelsData.For(null) == null);
        // one joint: the roll clamps to [min, max] when either is set; the rolls go about the joint's own axes in x, y, z order
        var root = new GameObject("wt16_parent"); var child = new GameObject("wt16_joint"); child.transform.SetParent(root.transform, false);
        try
        {
            root.transform.rotation = Quaternion.Euler(0, 40, 0);
            child.transform.localRotation = Quaternion.Euler(5, 0, 0);
            var j = new HeelsData.Joint { Roll = new Vector3(-10, 50, 7), RollMin = new Vector3(0, 0, 0), RollMax = new Vector3(30, 20, 10), Scale = new Vector3(1, 1, 1.1f), Move = new Vector3(0, 0.01f, 0) };
            HS2Next.Motion.Heels.HeelsFoot.Pose(child.transform, j);
            var exp = Quaternion.Euler(5, 0, 0) * Quaternion.AngleAxis(0, Vector3.right) * Quaternion.AngleAxis(20, Vector3.up) * Quaternion.AngleAxis(7, Vector3.forward);
            Check("WT16 rules: one joint posed (clamped rolls about its own axes, move added, scale set)",
                  Quaternion.Angle(child.transform.localRotation, exp) < 1e-3f && child.transform.localScale == j.Scale && (child.transform.localPosition - j.Move).sqrMagnitude < 1e-10f,
                  child.transform.localRotation.eulerAngles.ToString("F3"));
            child.transform.localRotation = Quaternion.Euler(30, 0, 0);
            HS2Next.Motion.Heels.HeelsFoot.Pose(child.transform, new HeelsData.Joint { Fixed = true, Scale = Vector3.one });
            Check("WT16 rules: fixed = the parent's world rotation (no roll)", Quaternion.Angle(child.transform.rotation, root.transform.rotation) < 1e-3f);
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ---------------------------------------------------------------- WT15 (after 1.0.0)

    const string CutTop = "K044#725044";                                   // 240 "Code:Hanabi": a body cut switched by objTopDef
    const string CutSocks = "com.3dh9527.newsqueezestocking.x6#77778";     // 246: a body cut whose switch object is always active

    static void WT15(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
            if (bb.B.Ok) WT15Rules(bb.B.Context);
        h.ReleaseBundles();
        if (!Installed(h, 240, CutTop, "WT15 top")) return;
        var c = MakerEditTest.Import(h, FemaleCard, out string err);
        if (!Check("WT15 BR-Chan imported", c != null, err)) return;
        var n = MakerEditTest.NewNode(h, c, "BR-Chan");
        try
        {
            if (!Check("WT15 node built", n.Ok, n.Error)) return;
            var ids = new[] { n.Id };
            string top = PartOf(n.Real.Context, 0).Item, socks = PartOf(n.Real.Context, 6).Item;
            var r0 = n.Ops.ChangeItem(ids, "appearance.coord.clothes.top.item", CutTop, User); n.Apply();
            var ctx = n.Real.Context;
            Check("WT15 240:" + CutTop + ": one body cut registered", r0.Ok && ctx.BodyCuts.Count == 1, $"{r0}; cuts {ctx.BodyCuts.Count}");
            if (ctx.BodyCuts.Count != 1) return;
            var cut = ctx.BodyCuts[0].TopA[1].GetComponent<SkinnedMeshRenderer>();
            var body = ctx.BodyRenderer;
            string State() => $"body {body.enabled}, cut {cut.enabled}, cut slot 0 {(cut.sharedMaterials.Length > 0 && cut.sharedMaterials[0] ? cut.sharedMaterials[0].name : "null")}";
            bool Skin() => cut.sharedMaterials.Length > 0 && cut.sharedMaterials[0] == body.sharedMaterials[0];
            void Fresh(string what, int topState)
            {
                using (var fresh = MakerEditTest.NewNode(h, n.Session.Model.File.ExtractCharacter(n.Id), "BR-Chan (fresh)"))
                {
                    if (!Check("WT15 fresh node built", fresh.Ok, fresh.Error)) return;
                    if (topState != ClothesStateSet.On) { fresh.Ops.SetClothes(new[] { fresh.Id }, 0, topState, false, User); fresh.Apply(); }
                    var d = CharaSnapshot.Compare(fresh.Snapshot(), n.Snapshot(), 12);
                    Check("WT15 " + what + ": = a fresh node", d.Equal, d.ToString());
                }
            }
            Check("WT15 dressed: the body off, the cut body on with the body's first material", !body.enabled && cut.enabled && Skin(), State());
            var s = n.Snapshot();
            Check("WT15 dressed: G1", s.G1Pass, s.G1Text);
            Fresh("dressed", ClothesStateSet.On);
            n.Ops.SetClothes(ids, 0, ClothesStateSet.Half, false, User); n.Apply();
            if (n.Real.StateRealizer.Realized[0] == ClothesStateSet.Half)
            {
                Check("WT15 half (objTopDef inactive): the body on, the cut body off", body.enabled && !cut.enabled && Skin(), State());
                Fresh("half", ClothesStateSet.Half);
            }
            else Info("WT15 the top offers no half state (realized " + n.Real.StateRealizer.Realized[0] + "): skipped");
            n.Ops.SetClothes(ids, 0, ClothesStateSet.Off, false, User); n.Apply();
            Check("WT15 off (the part inactive): the body on, the cut body off", body.enabled && !cut.enabled, State());
            n.Ops.SetClothes(ids, 0, ClothesStateSet.On, false, User); n.Apply();
            Check("WT15 dressed again: the body off, the cut body on", !body.enabled && cut.enabled && Skin(), State());
            if (Installed(h, 246, CutSocks, "WT15 socks"))
            {
                n.Ops.ChangeItem(ids, "appearance.coord.clothes.socks.item", CutSocks, User); n.Apply();
                Check("WT15 + socks with a body cut: two cuts, the body off, both cut bodies on", ctx.BodyCuts.Count == 2 && !body.enabled
                      && ctx.BodyCuts.All(b => b.TopA[1].GetComponent<SkinnedMeshRenderer>().enabled), $"cuts {ctx.BodyCuts.Count}, {State()}");
                n.Ops.ChangeItem(ids, "appearance.coord.clothes.top.item", top, User); n.Apply();
                Check("WT15 the top taken off: one cut left (the socks'), the body still off", ctx.BodyCuts.Count == 1 && !body.enabled, $"cuts {ctx.BodyCuts.Count}, body {body.enabled}");
                Fresh("socks", ClothesStateSet.On);
                n.Ops.ChangeItem(ids, "appearance.coord.clothes.socks.item", socks, User); n.Apply();
            }
            else { n.Ops.ChangeItem(ids, "appearance.coord.clothes.top.item", top, User); n.Apply(); }
            var unit = ctx.Editor.Unit(UnitMap.BodyCut) as BodyCutUnit;
            Check("WT15 the cuts taken off: the body on, no cuts, the unit stops following", body.enabled && ctx.BodyCuts.Count == 0 && ctx.BodyCutRenderers.Count == 0 && unit != null && !unit.Watching,
                  $"body {body.enabled}, cuts {ctx.BodyCuts.Count}");
            s = n.Snapshot();
            Check("WT15 after the release: G1", s.G1Pass, s.G1Text);
            Fresh("cuts taken off", ClothesStateSet.On);
        }
        finally { n.Dispose(); h.ReleaseBundles(); }
    }

    /// <summary>rules on objects made under the character's body top.</summary>
    static void WT15Rules(CharaContext ctx)
    {
        var x = ctx.Edit;
        var body = ctx.BodyRenderer;
        var part = new GameObject("wt15_part"); part.transform.SetParent(ctx.BodyTop.transform, false);
        var sw = new GameObject("wt15_switch"); sw.transform.SetParent(part.transform, false);
        SkinnedMeshRenderer R(string name) { var g = new GameObject(name); g.transform.SetParent(part.transform, false); return g.AddComponent<SkinnedMeshRenderer>(); }
        var cut = R("wt15_cut"); var second = R("wt15_second");
        var own0 = x.Instance(ctx.BodyDraw); var own1 = x.Instance(ctx.BodyDraw);
        cut.sharedMaterials = new[] { own0, own1 };
        var noRenderer = new GameObject("wt15_plain"); noRenderer.transform.SetParent(part.transform, false);
        bool invisible = ctx.Stats.InvisibleBody;
        try
        {
            var c = part.AddComponent<MdgqBodyChange.BodyCut>();
            c.TopA = new List<GameObject> { sw, cut.gameObject };
            var inert = part.AddComponent<MdgqBodyChange.BodyCut>();
            inert.TopA = new List<GameObject> { sw, noRenderer };
            var off = part.AddComponent<MdgqBodyChange.BodyCut>();
            off.enabled = false; off.TopA = new List<GameObject> { sw, cut.gameObject };
            int n = x.RegisterBodyCuts(part);
            Check("WT15 rules: an enabled cut with its cut-body renderer registers (a cut without one and a disabled component not)",
                  n == 1 && ctx.BodyCuts.Count == 1 && ctx.DrawsBodySkin(cut) && !ctx.DrawsBodySkin(second), $"registered {n}");
            x.ApplyBodyCuts();
            Check("WT15 rules: switch active = the body off, the cut body on; its first slot the body's first material, the replaced copy destroyed, the second slot kept",
                  !body.enabled && cut.enabled && cut.sharedMaterials[0] == body.sharedMaterials[0] && cut.sharedMaterials[1] == own1 && !own0 && !ctx.OwnedMaterials.Contains(own0));
            sw.SetActive(false); x.ApplyBodyCuts();
            Check("WT15 rules: switch inactive = the body on, the cut body off", body.enabled && !cut.enabled);
            sw.SetActive(true); c.enabled = false; x.ApplyBodyCuts();
            Check("WT15 rules: a component disabled after the start = the body on, the cut body off", body.enabled && !cut.enabled);
            c.enabled = true;
            c.TopA = new List<GameObject> { sw, cut.gameObject, second.gameObject };
            sw.SetActive(false); x.ApplyBodyCuts();
            Check("WT15 rules: with a second cut body, switch inactive = the second on, the first off, the body off; switch active = the first on",
                  !body.enabled && !cut.enabled && second.enabled && second.sharedMaterials[0] == body.sharedMaterials[0]
                  && SwitchOn(sw, x) && cut.enabled && !second.enabled && !body.enabled);
            c.TopA = new List<GameObject> { sw, cut.gameObject };
            ctx.Stats.InvisibleBody = true; x.ApplyBodyCuts();
            Check("WT15 rules: a body the card hides (Invisible Body) stays hidden, and the cut body too", !body.enabled && !cut.enabled);
            ctx.Stats.InvisibleBody = invisible;
            Object.DestroyImmediate(part);
            int live = x.ApplyBodyCuts();
            Check("WT15 rules: the part destroyed = the body on, no cuts", live == 0 && body.enabled && ctx.BodyCuts.Count == 0 && ctx.BodyCutRenderers.Count == 0);
            Check("WT15 rules: the body draw material survives", ctx.BodyDraw && body.sharedMaterials[0] == ctx.BodyDraw);
        }
        finally { ctx.Stats.InvisibleBody = invisible; if (part) Object.DestroyImmediate(part); x.ApplyBodyCuts(); }
    }

    static bool SwitchOn(GameObject sw, EditContext x) { sw.SetActive(true); x.ApplyBodyCuts(); return true; }

    // ---------------------------------------------------------------- WT14 (after 1.0.0)

    const string PigTop = "roy12.piggytop#0";   // 240 "Pig Top": its belly bones cf_J_Belly_Pivot.. implant under cf_J_Kosi01_s

    static void WT14(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            WT14Rules(ctx);
            if (!Installed(h, 240, PigTop, "WT14 top")) return;
            string was = PartOf(ctx, 0).Item;
            var row = RowOf(h.Catalog, 240, PigTop);
            var prefab = h.Catalog.LoadAsset<GameObject>(row, "MainAB", ctx.Edit.DataKey(row));
            var proc = prefab ? prefab.GetComponentInChildren<ModBoneImplantor.BoneImplantProcess>(true) : null;
            SetItem(live, 0, PigTop);
            var part = ctx.Clothes[0];
            var roots = ctx.Edit.ImplantsOf(part);
            var root = roots != null && roots.Count == 1 ? roots[0] : null;
            ctx.BodyBones.TryGetValue("cf_J_Kosi01_s", out var kosi);
            Check("WT14 240:" + PigTop + ": one implant, cf_J_Belly_Pivot under the skeleton's cf_J_Kosi01_s with the prefab's local transform",
                  proc != null && root && root.name == "cf_J_Belly_Pivot" && kosi && root.parent == kosi && !root.IsChildOf(part.transform)
                  && root.localPosition == proc.trfSrc.localPosition && root.localRotation == proc.trfSrc.localRotation && root.localScale == proc.trfSrc.localScale,
                  $"implants {roots?.Count ?? 0}, root {(root ? root.name + " under " + root.parent.name : "-")}");
            var implanted = root ? new HashSet<Transform>(root.GetComponentsInChildren<Transform>(true)) : new HashSet<Transform>();
            var skeleton = new HashSet<Transform>(ctx.BodyBones.Values);
            int slots = 0, bad = 0;
            foreach (var smr in part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var b in smr.bones)
                {
                    if (b && implanted.Contains(b)) slots++;
                    else if (!b || !skeleton.Contains(b)) bad++;
                }
            Check("WT14 every renderer bone of the part is the skeleton's or implanted (20 slots on implanted bones); the skeleton copy is gone",
                  slots == 20 && bad == 0 && !EditContext.Find(part.transform, "cf_J_Root"), $"implanted slots {slots}, other {bad}");
            // the pig top's dynamic bone sits on the implanted bones and moves with them: the stage collects it under the root
            var dbs = part.GetComponentsInChildren<global::DynamicBone>(true).Concat(root ? root.GetComponentsInChildren<global::DynamicBone>(true) : new global::DynamicBone[0]).ToList();
            var stage = ctx.Motion?.Get<DynamicBoneStage>();
            Check("WT14 the part's dynamic bone is rooted on the implanted bones and has a chain", dbs.Count > 0 && dbs.All(d => d.m_Root && implanted.Contains(d.m_Root))
                  && (stage == null || dbs.All(d => stage.Chains.Any(ch => ch.Source == d))),
                  string.Join(", ", dbs.Select(d => (d.m_Root ? d.m_Root.name : "null") + (implanted.Contains(d.transform) ? " (on an implanted bone)" : ""))) + (stage == null ? "; no stage" : ""));
            var s = live.Snapshot();
            Check("WT14 with the implant: G1", s.G1Pass, s.G1Text);
            FreshOk("WT14 pig top", h, live);
            SetItem(live, 0, was);
            Check("WT14 pig top -> the card's top: the implanted bones destroyed with the part", !root && ctx.Edit.ImplantsOf(part) == null
                  && !EditContext.Find(kosi, "cf_J_Belly_Pivot"));
            s = live.Snapshot();
            Check("WT14 after the release: G1", s.G1Pass, s.G1Text);
            FreshOk("WT14 top back", h, live);
        }
        h.ReleaseBundles();
    }

    /// <summary>rules on objects made under the character's body top (a part with a skeleton copy cf_J_Root, bones and a
    /// skinned renderer, rebound to the body skeleton).</summary>
    static void WT14Rules(CharaContext ctx)
    {
        var x = ctx.Edit;
        GameObject Part(string name, bool implants)
        {
            var part = new GameObject(name); part.transform.SetParent(ctx.BodyTop.transform, false);
            var copy = new GameObject("cf_J_Root"); copy.transform.SetParent(part.transform, false);
            var kosi = new GameObject("cf_J_Kosi01_s"); kosi.transform.SetParent(copy.transform, false);
            var extra = new GameObject("wt14_extra"); extra.transform.SetParent(kosi.transform, false);
            extra.transform.localPosition = new Vector3(0.1f, 0.2f, 0.3f); extra.transform.localRotation = Quaternion.Euler(10, 20, 30);
            var tip = new GameObject("wt14_tip"); tip.transform.SetParent(extra.transform, false);
            var stray = new GameObject("wt14_stray"); stray.transform.SetParent(part.transform, false);
            var g = new GameObject("o_wt14"); g.transform.SetParent(part.transform, false);
            var smr = g.AddComponent<SkinnedMeshRenderer>();
            smr.bones = new[] { kosi.transform, extra.transform, tip.transform, stray.transform, null };
            if (implants)
            {
                var ok = part.AddComponent<ModBoneImplantor.BoneImplantProcess>(); ok.trfSrc = extra.transform; ok.trfDst = kosi.transform;
                var same = part.AddComponent<ModBoneImplantor.BoneImplantProcess>(); same.trfSrc = tip.transform; same.trfDst = tip.transform;
                var unknown = part.AddComponent<ModBoneImplantor.BoneImplantProcess>(); unknown.trfSrc = stray.transform; unknown.trfDst = stray.transform.parent;
            }
            return part;
        }
        var withImplants = Part("wt14_part", true);
        var without = Part("wt14_plain", false);
        try
        {
            var extra = EditContext.Find(withImplants.transform, "wt14_extra");
            var tip = EditContext.Find(withImplants.transform, "wt14_tip");
            x.Rebind(withImplants, ctx.BodyBones, "cf_J_Root", ctx.Hips);
            var bones = withImplants.GetComponentInChildren<SkinnedMeshRenderer>(true).bones;
            ctx.BodyBones.TryGetValue("cf_J_Kosi01_s", out var kosi);
            Check("WT14 rules: trfSrc moves under the skeleton bone named like trfDst with its local transform (its children with it)",
                  extra && extra.parent == kosi && extra.localPosition == new Vector3(0.1f, 0.2f, 0.3f) && tip && tip.parent == extra
                  && x.ImplantsOf(withImplants)?.Count == 1, $"extra under {(extra && extra.parent ? extra.parent.name : "-")}");
            Check("WT14 rules: the renderer keeps the skeleton's and the implanted bones; a bone neither implanted nor in the skeleton becomes null; a null stays null",
                  bones.Length == 5 && bones[0] == kosi && bones[1] == extra && bones[2] == tip && bones[3] == null && bones[4] == null,
                  string.Join(", ", bones.Select(b => b ? b.name : "null")));
            Check("WT14 rules: an invalid pair (trfSrc = trfDst) and a target outside the skeleton implant nothing; the skeleton copy is gone",
                  EditContext.Find(withImplants.transform, "wt14_stray") && !EditContext.Find(withImplants.transform, "cf_J_Root"));
            x.Rebind(without, ctx.BodyBones, "cf_J_Root", ctx.Hips);
            var plain = without.GetComponentInChildren<SkinnedMeshRenderer>(true).bones;
            Check("WT14 rules: a part without implants keeps a missing bone in its slot (1.0 behaviour)",
                  plain[0] == kosi && !plain[1] && plain[3] && plain[3].name == "wt14_stray" && x.ImplantsOf(without) == null,
                  string.Join(", ", plain.Select(b => b ? b.name : "null")));
            int n = x.DestroyImplants(withImplants);
            Check("WT14 rules: DestroyImplants removes the implanted bones from the skeleton", n == 1 && !extra && x.ImplantsOf(withImplants) == null);
        }
        finally { Object.DestroyImmediate(withImplants); Object.DestroyImmediate(without); }
    }

    // ---------------------------------------------------------------- WT13 (after 1.0.0)

    const string SkinSocks = "roy12.skindentations#1";   // 246 "Skindentation Socks 02": o_socks_thigh2 draws the body's materials

    /// <summary>The renderers the part's enabled ChaCopyBodyMaterial components name inside it.</summary>
    static List<Renderer> BodyTargetsOf(GameObject part) =>
        part ? part.GetComponentsInChildren<ChaCopyBodyMaterial>(true).Where(c => c.enabled && c.RendTarget != null).SelectMany(c => c.RendTarget)
                   .Where(r => r && r.transform.IsChildOf(part.transform)).Distinct().ToList() : new List<Renderer>();

    static BodyMaterialCopyUnit CopyUnit(CharaContext ctx) => ctx.Editor.Unit(UnitMap.BodyMaterialCopy) as BodyMaterialCopyUnit;

    /// <summary>on one part: every target is registered and draws the body renderer's material array (the same objects);
    /// the part's other renderers draw owned materials that are not the body's; the body draw keeps the skin RT; G1 passes.</summary>
    public static bool BodyMaterialsOk(string label, MakerCoreTest.Built live, GameObject part)
    {
        var ctx = live.Context;
        var targets = BodyTargetsOf(part);
        if (!Check(label + ": the part names targets", targets.Count > 0, part ? part.name : "no part")) return false;
        var body = ctx.BodyRenderer.sharedMaterials;
        var bad = new List<string>();
        foreach (var r in targets)
        {
            if (!ctx.DrawsBodyMaterials(r)) bad.Add(r.name + " not registered");
            if (!r.sharedMaterials.SequenceEqual(body)) bad.Add(r.name + " [" + string.Join(",", r.sharedMaterials.Select(m => m ? m.name : "null")) + "]");
        }
        foreach (var r in part.GetComponentsInChildren<Renderer>(true).Where(r => !targets.Contains(r)))
            foreach (var m in r.sharedMaterials)
                if (m && (body.Contains(m) || !ctx.OwnedMaterials.Contains(m))) bad.Add(r.name + ": " + m.name + (body.Contains(m) ? " is the body's" : " not owned"));
        var skin = ctx.Edit?.BodySkin?.Creator.Target;
        if (!(ctx.BodyDraw && skin && ctx.BodyDraw.HasProperty("_MainTex") && ctx.BodyDraw.GetTexture("_MainTex") == skin)) bad.Add("body draw _MainTex is not the skin RT");
        var s = live.Snapshot();
        if (!s.G1Pass) bad.Add("G1 " + s.G1Text);
        return Check(label + $": {targets.Count} targets draw the body's {body.Length} materials, the part's other renderers their own, body draw = the skin RT, G1",
                     bad.Count == 0, string.Join("; ", bad.Take(8)));
    }

    static void WT13(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            WT13Rules(ctx);
            Check("WT13 BR-Chan has no targets: copy.bodyMaterial does not follow the earlier steps", ctx.BodyMaterialTargets.Count == 0 && !CopyUnit(ctx).Watching);
            if (!Installed(h, 246, SkinSocks, "WT13 socks")) return;
            var p = PartOf(ctx, 6);
            string was = p.Item;
            var r = SetItem(live, 6, SkinSocks);
            Check("WT13 the item load ran copy.bodyMaterial", r.Units.Contains(UnitMap.BodyMaterialCopy), string.Join(",", r.Units));
            BodyMaterialsOk("WT13 246:" + SkinSocks, live, ctx.Clothes[6]);
            FreshOk("WT13 socks", h, live);
            // the clothes colour pass of the edit writes nothing into the body's materials (HS2 discards its writes to a target)
            string bodyBefore = FaceDigest.MaterialsText(ctx, "body");
            r = SetM(live, Doc(6) + ".colors[0].base", () => p.Colors[0].Base = new Color4(0.2f, 0.7f, 0.3f, 1f));
            string bodyAfter = FaceDigest.MaterialsText(ctx, "body");
            Check("WT13 a socks colour edit: copy.bodyMaterial ran, the body's materials unchanged", r.Units.Contains(UnitMap.BodyMaterialCopy) && bodyAfter == bodyBefore,
                  string.Join(",", r.Units) + "; " + FirstDiff(bodyBefore, bodyAfter));
            BodyMaterialsOk("WT13 after the colour edit", live, ctx.Clothes[6]);
            FreshOk("WT13 colour edit", h, live);
            // skin edits: the targets follow the body's materials
            var b = ctx.Chara.Body;
            SetM(live, "chara.body.skin.color", () => b.Skin.Color = new Color4(0.5f, 0.4f, 0.3f, 1f));
            BodyMaterialsOk("WT13 after a skin colour edit", live, ctx.Clothes[6]);
            SetM(live, "chara.body.skin.gloss", () => b.Skin.Gloss = 0.35f);
            BodyMaterialsOk("WT13 after a skin gloss edit", live, ctx.Clothes[6]);
            FreshOk("WT13 skin edits", h, live);
            // the release of the part keeps the body's materials alive; the targets are dropped and the unit stops following
            var bodyMats = ctx.BodyRenderer.sharedMaterials;
            SetItem(live, 6, was);
            Check("WT13 socks → the card's socks: the body's materials alive, owned and the body's; no targets left; the unit stops following",
                  bodyMats.All(m => m && ctx.OwnedMaterials.Contains(m)) && ctx.BodyRenderer.sharedMaterials.SequenceEqual(bodyMats)
                  && ctx.BodyMaterialTargets.Count == 0 && !CopyUnit(ctx).Watching, $"targets {ctx.BodyMaterialTargets.Count}");
            var s = live.Snapshot();
            Check("WT13 after the release: G1", s.G1Pass, s.G1Text);
            FreshOk("WT13 socks back", h, live);
            r = SetM(live, Doc(6) + ".colors[0].base", () => p.Colors[0].Base = new Color4(0.6f, 0.2f, 0.2f, 1f));
            Check("WT13 no targets: a colour edit runs no copy.bodyMaterial", !r.Units.Contains(UnitMap.BodyMaterialCopy), string.Join(",", r.Units));
        }
        h.ReleaseBundles();
    }

    /// <summary>rules on objects made under the character's body top: registration (an enabled component's non-null
    /// renderers inside the part; a null entry, a renderer outside the part and a disabled component's renderers not), a
    /// target owns no material (OwnMaterial / OwnMaterials / Mats give nothing, the ownership step copies none of its slots),
    /// the copy (the body's array, the same objects; other renderers untouched), KCOX's target choice passes over targets, a
    /// release destroys only the part's own materials, the destroyed targets are dropped by the unit's next run.</summary>
    static void WT13Rules(CharaContext ctx)
    {
        var x = ctx.Edit;
        var part = new GameObject("wt13_part"); part.transform.SetParent(ctx.BodyTop.transform, false);
        var outside = new GameObject("wt13_outside"); outside.transform.SetParent(ctx.BodyTop.transform, false);
        SkinnedMeshRenderer R(string n, Transform parent) { var g = new GameObject(n); g.transform.SetParent(parent, false); return g.AddComponent<SkinnedMeshRenderer>(); }
        var t1 = R("t1", part.transform); var t2 = R("t2", part.transform); var other = R("other", part.transform);
        var off = R("off", part.transform); var far = R("far", outside.transform);
        var foreign = new Material(ctx.BodyDraw) { name = "wt13_foreign" };
        try
        {
            var c = part.AddComponent<ChaCopyBodyMaterial>();
            c.RendTarget = new List<Renderer> { t1, null, far, t2 };
            var d = part.AddComponent<ChaCopyBodyMaterial>();
            d.enabled = false;
            d.RendTarget = new List<Renderer> { off };
            int n = x.RegisterBodyMaterialTargets(part);
            Check("WT13 rules: an enabled component's renderers inside the part register (a null entry, a renderer outside, a disabled component's not)",
                  n == 2 && ctx.DrawsBodyMaterials(t1) && ctx.DrawsBodyMaterials(t2) && !ctx.DrawsBodyMaterials(far) && !ctx.DrawsBodyMaterials(off) && !ctx.DrawsBodyMaterials(other)
                  && CopyUnit(ctx).Watching, $"registered {n}");
            t1.sharedMaterials = new Material[1];
            other.sharedMaterials = new Material[1];
            Check("WT13 rules: a target owns no material (OwnMaterial null, OwnMaterials / Mats none, its empty slot kept)",
                  x.OwnMaterial(t1) == null && x.OwnMaterials(t1).Length == 0 && x.Mats(t1).Length == 0 && t1.sharedMaterials.Length == 1 && !t1.sharedMaterials[0]);
            var om = x.OwnMaterial(other);
            Check("WT13 rules: another renderer's empty slot gets its owned instance", om && ctx.OwnedMaterials.Contains(om) && other.sharedMaterials[0] == om);
            t2.sharedMaterials = new[] { foreign };
            int owned0 = ctx.OwnedMaterials.Count;
            x.OwnAll(part);
            Check("WT13 rules: the ownership step copies no slot of a target", t2.sharedMaterial == foreign && ctx.OwnedMaterials.Count == owned0, $"owned {owned0} → {ctx.OwnedMaterials.Count}");
            int live0 = x.CopyBodyMaterials();
            var body = ctx.BodyRenderer.sharedMaterials;
            Check("WT13 rules: the copy gives the targets the body's array (the same objects); the other renderer keeps its own",
                  live0 == 2 && t1.sharedMaterials.SequenceEqual(body) && t2.sharedMaterials.SequenceEqual(body) && other.sharedMaterials.Length == 1 && other.sharedMaterials[0] == om,
                  $"live {live0}, body slots {body.Length}");
            var cmp = part.AddComponent<CmpClothes>();
            cmp.rendNormal01 = new List<Renderer> { t1, other };
            cmp.rendNormal02 = new List<Renderer> { t2 };
            var kt = HS2Next.Chara.Ext.ClothesOverlayApplier.Targets(ctx, cmp);
            Check("WT13 rules: KCOX passes over targets (rendNormal01's first other renderer; rendNormal02 holds a target only)", kt.Count == 1 && kt[0] == other,
                  string.Join(",", kt.Select(k => k.name)));
            int destroyed = x.DestroyOwnedMaterials(part);
            Check("WT13 rules: a release destroys the part's own material only; the body's stay alive and owned",
                  destroyed == 1 && !om && body.All(m => m && ctx.OwnedMaterials.Contains(m)), $"destroyed {destroyed}");
        }
        finally
        {
            Object.DestroyImmediate(part);
            Object.DestroyImmediate(outside);
            Object.DestroyImmediate(foreign);
        }
        ctx.Editor.MarkUnit(UnitMap.BodyMaterialCopy);
        var fr = ctx.Editor.Flush();
        Check("WT13 rules: the unit's next run drops the destroyed targets and stops following", ctx.BodyMaterialTargets.Count == 0 && !CopyUnit(ctx).Watching && fr.Units.Contains(UnitMap.BodyMaterialCopy),
              $"targets {ctx.BodyMaterialTargets.Count}, units {string.Join(",", fr.Units)}");
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
