// Maker accessory tests of Phase 4.5 package A (the accessory units and runtime): rows
// MakerAccessoriesTest, .render, .timing and RunS1Explain; measurements XU1–XU3.
//   Run (headless):
//     UM   the Unity-free unit map: importSlot marks the slot table, hair.shaderType marks the hair units and every accessory
//     XT1  colours: per used area the first material's _Color{,2,3} / _Glossiness{,2,3} / _Metallic{,2,3} (359#0, three areas),
//          the alpha renderers' _Color / _Glossiness4 / _Metallic4 and activity (353#0: alpha 0 → inactive, back → the
//          prefab's activity), a multi-slot renderer (361#30: slot 0 only); edit = fresh build
//     XT2  hair-type: 351#40 with draw type 0 and 1 (shader, _MainTex = TexD / TexC, queues kept, the hair colours on the first
//          material per useColor0n, _Specular, _Smoothness, _Metallic); a draw-type switch re-shades every hair-type slot incl.
//          slot 20 and no other slot; a hair-type row without TexAB keeps its own _MainTex
//     XT3  AccessoryReport facts of every vanilla row of 351..362 (one row at a time in a free slot of BR-Chan) = the census's
//          vanilla examples and counts (typeHair, alpha renderers, the sets, the colour tab, multi-slot renderers, the Parent
//          column) and the defaults = PartDefaults.TryAccessoryColors
//     XT4  parents: each of the 54 keys on 352#0 (anchor = the key's transform, the slot object keeps the prefab-local TRS),
//          «初期親» (the row's Parent), an unknown stored key (no anchor, logged), the "null" rows 351#202 / #214 (head-bound,
//          the stored key ignored)
//     XT5  transform sets: stored values → local TRS (× 0.1, Euler, scale, (0,0,0) → one), values outside the UI limits applied
//          as stored, a set the prefab lacks ignored
//     XT6  the slot table: entries with gaps realize at their preferred slots, an importSlot change moves the object (acc.list),
//          a MoreAccessories slot ≥ 20 loads; AddAccessory / CopyAccessory / MoveAccessoryToSlot land in the named slot and
//          AccessoryIdOfSlot follows the table
//     XT7  (CharaStateTest S11) the visibility fold
//     XT8  item / type consequences realized through the ops: noShake off → the chains; non-hair → hair-type takes the back
//          hair's colours, hair → hair keeps them; a type change → row 0, the row's parent, identity sets
//     XT9  copies: «複製» to the lowest free slot, to an occupied slot (id kept), «調整①を左右反転» (rotation.y + 180), the
//          mirrored parent, the plugin data with and without maker.copyAccessoryPluginData
//     XT11 «髪の毛の色を取得» from each of the four hair parts (documents, materials, one entry)
//     XT10 colliders after accessory swaps: the collider earring fijas.accessories.earrings01#1 in slot 0 and a chain accessory in
//          slot 3; swapping slot 3's item and slot 0's item give the chains (collider lists) of a fresh build
//     XT12 the accessory digest sections (materials, objects) change exactly when the edits say
//     XT13 skinned accessories (added after 1.0.0): the fingernails whinny.halloweenspecial02.accs#4 (361) in a free slot,
//          after an item swap and in slot 25 have every listed renderer on the character's rig (bone by bone against the
//          prefab and the merge table), the rig copy destroyed, the baked vertices at the hands (and the unmerged prefab away
//          from them); the mask [Blake]Monster Mask#0 (351) binds its face bones to the head rig; the rules on built objects
//          (a null entry keeps the copy, a copy that is the slot object stays, no component = untouched); edit = fresh build
//     XT14 (added after 1.0.0) the waist accessory roy12.skindentations#0 (358, a SkinnedAccessory and a
//          ChaCopyBodyMaterial): merged as XT13, its renderers draw the body's materials, a colour edit of the slot leaves the
//          body's materials unchanged, the removal keeps them alive; G1; edit = fresh build
//   RunRender (graphics): the materials of XT1 / XT2 after edits = a fresh build's; the hair-type materials use supported
//     replacement shaders; a 64² RT of the head region with 351#40 rendered twice hashes equal.
//   RunTiming (graphics, its own Unity session): XU1 (item swap warm / cold, type change, parent change), XU2 (colour drag
//     frame, transform step), XU3 (the draw-type switch with 4 hair-type slots and the 4 hair parts) with the 1 × 1 copy fence.
//   RunS1Explain (headless, for T): S1 targets + 20 (or -all / -targets) vs the baselines: every differing line classified
//     (acc.hairType: materials of a hair-type slot's renderers; acc.alpha4: _Glossiness4 / _Metallic4 of alpha renderers;
//     acc.visibility: slot object activity / the read show flag; acc.skinned: object, bone and renderer lines inside a slot
//     object with a SkinnedAccessory; part.bodyMaterial: renderer and material lines of a renderer that draws the
//     body's materials; part.bodyCut: renderer and material lines of the body and of a cut body;
//     face.headBundle: the face pass lines of a head with its own compositing materials; part.heels: the
//     cf_N_height and foot joint lines while heels are in effect; part.additionalBones: the lines of the
//     bones moved by a part's additional_bones, its renderers, the chains on those bones and the ABMX modifiers that now
//     find them) or printed.
// Run: -executeMethod MakerAccessoriesTest.Run [-only UM,XT1]; .RunRender; .RunTiming [-reps N]; .RunS1Explain [-all|-targets]
//      [-only a.png,b.png (or a.png+b.png)] [-limit N].
// Reports: Spike/out/maker/maker_accessories_report.txt, maker_accessories_render_report.txt,
// maker_accessories_timing_report.txt, maker_accessories_s1explain_report.txt. Content rule: numbers only (material values,
// transforms, chain states, hashes); nothing is rendered to a file or viewed; SFW targets; items appear as data names only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AIChara;
using HS2Next.Chara;
using HS2Next.Chara.Edit;
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
using static MakerTestKit;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class MakerAccessoriesTest
{
    // rows
    const string V = "illusion.hs2#";
    const string Backpack = V + "0";          // 359 three colour areas
    const string Glasses = V + "0";           // 353 alpha renderers (defColor04 alpha 0.102)
    const string MultiSlot = V + "30";        // 361 rendNormal with several material slots
    const string HairBun = V + "40";          // 351 hair-type
    const string HairBun2 = V + "41";         // 351 hair-type
    const string StarEar = V + "0";           // 352 row 0
    const string SilkHat = V + "35";          // 351 two transform sets
    const string RingChest = V + "0";         // 357 DynamicBones
    const string ColliderEarring = "fijas.accessories.earrings01#1";   // 352, one DynamicBoneCollider
    static readonly string[] HeadWeighted = { V + "202", V + "214" };  // 351 Parent "null"

    static string[] CP = { "_Color", "_Color2", "_Color3" }, GP = { "_Glossiness", "_Glossiness2", "_Glossiness3" }, MP = { "_Metallic", "_Metallic2", "_Metallic3" };
    static int idSeq;
    static PartDefaults defaults;

    // ================================================================ helpers

    static MakerCoreTest.Built BuildOk(MakerCoreTest.Harness h, string file, string label)
    {
        var b = h.Build(file);
        Check(label + ": built", b.Ok, b.Error);
        return b;
    }

    static bool Installed(MakerCoreTest.Harness h, int category, string reference, string what)
    {
        if (ContentId.TryParse(reference, out var id) && h.Catalog.GetEntry(category, id) != null) return true;
        Warn(what, $"row {category}:{reference} not installed: skipped");
        return false;
    }

    static AccessorySlotTableView Table(CharaContext ctx) => new AccessorySlotTableView(ctx);

    /// <summary>The build's slot table of a character (document builds).</summary>
    sealed class AccessorySlotTableView
    {
        readonly CharaContext ctx;
        public AccessorySlotTableView(CharaContext c) { ctx = c; }
        public bool Has(int slot) => ctx.Edit?.Input?.Slots != null && ctx.Edit.Input.Slots.EntryBySlot.ContainsKey(slot);
        public int FreeSlot(int from = 0) { int s = from; while (Has(s)) s++; return s; }
    }

    static string AP(string id, string member) => "coord.accessories[\"" + id + "\"]" + (string.IsNullOrEmpty(member) ? "" : "." + member);

    static AccessoryColor CopyColor(AccessoryColor c) => new AccessoryColor { Color = c.Color, Gloss = c.Gloss, Metallic = c.Metallic, Smoothness = c.Smoothness };

    /// <summary>Puts an accessory into build slot <paramref name="slot"/> of the live documents (the entry there removed, the
    /// new one with that preferred slot and the prefab's default colours, as the add op), then flushes.</summary>
    static AccessoryEntry Put(MakerCoreTest.Built live, int slot, int category, string item, string parent = "")
    {
        var ctx = live.Context;
        var coord = ctx.Coord;
        var table = ctx.Edit.Input.Slots;
        if (table != null && table.EntryBySlot.TryGetValue(slot, out var old)) coord.Accessories.Remove(old);
        var e = new AccessoryEntry
        {
            Id = JsonText.DerivedId("xt/" + slot.ToString(CultureInfo.InvariantCulture) + "/" + (++idSeq).ToString(CultureInfo.InvariantCulture)),
            Category = category, Item = item, Parent = parent ?? "", ImportSlot = slot,
        };
        if (defaults != null && defaults.TryAccessoryColors(category, item, out var cols)) e.Colors = cols.Select(CopyColor).ToArray();
        coord.Accessories.Add(e);
        ctx.Editor.MarkDirty("coord.accessories");
        ctx.Editor.Flush();
        return e;
    }

    /// <summary>Changes the item (and category) of an entry as the document (colours, parent, moves kept), then flushes.</summary>
    static FlushReport SetItem(MakerCoreTest.Built live, AccessoryEntry e, int category, string item)
    {
        e.Category = category; e.Item = item;
        live.Context.Editor.MarkDirty(AP(e.Id, "category"));
        live.Context.Editor.MarkDirty(AP(e.Id, "item"));
        return live.Context.Editor.Flush();
    }

    static FlushReport SetA(MakerCoreTest.Built live, AccessoryEntry e, string member, Action set, FlushMode mode = FlushMode.Normal)
    {
        set();
        live.Context.Editor.MarkDirty(AP(e.Id, member));
        return live.Context.Editor.Flush(mode);
    }

    static AccessorySlot SlotOf(CharaContext ctx, int slot) => slot < ctx.Accessories.Count ? ctx.Accessories[slot] : null;
    static GameObject ObjOf(CharaContext ctx, int slot) => SlotOf(ctx, slot)?.Object;
    static CmpAccessory CmpOf(CharaContext ctx, int slot) { var go = ObjOf(ctx, slot); return go ? go.GetComponent<CmpAccessory>() : null; }

    static List<Renderer> Own(List<Renderer> l, GameObject go) =>
        l == null || !go ? new List<Renderer>() : l.Where(r => r && r.transform.IsChildOf(go.transform)).Distinct().ToList();

    static bool Near(Color a, Color b, float eps = 1e-6f) => Mathf.Abs(a.r - b.r) <= eps && Mathf.Abs(a.g - b.g) <= eps && Mathf.Abs(a.b - b.b) <= eps && Mathf.Abs(a.a - b.a) <= eps;
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

    /// <summary>The accessory chains of the dynamic-bone stage: per chain (stage order) the slot, root and owner names, active,
    /// noShake, particle count and the colliders' paths. Empty without a stage.</summary>
    public static string AccessoryChainsText(CharaContext ctx)
    {
        var sb = new StringBuilder();
        var stage = ctx?.Motion != null ? ctx.Motion.Get<DynamicBoneStage>() : null;
        if (stage == null) return "dynbone none\n";
        foreach (var c in stage.Chains.Where(c => c != null && c.Category == DynamicBoneCategory.Accessory))
        {
            sb.Append("chain acc").Append(c.Slot).Append(" root ").Append(c.RootName).Append(" owner ").Append(c.Owner ? c.Owner.name : "-")
              .Append(" active ").Append(c.Active ? 1 : 0).Append(" noShake ").Append(c.NoShake ? 1 : 0).Append(" particles ").Append(c.ParticleCount).Append(" colliders ");
            for (int k = 0; k < c.ColliderCount; k++) sb.Append(k == 0 ? "" : ",").Append(stage.Colliders.Path(c.ColliderIndex(k)));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>The accessory digest texts and chains of two characters compared; null when equal.</summary>
    public static string AccDiff(CharaContext a, CharaContext b)
    {
        var l = new List<string>();
        void T(string name, string x, string y) { if (x != y) l.Add(name + " (" + FirstDiff(x, y) + ")"); }
        T("materials", FaceDigest.MaterialsText(a, "accessories"), FaceDigest.MaterialsText(b, "accessories"));
        T("objects", FaceDigest.ObjectsText(a, "accessories"), FaceDigest.ObjectsText(b, "accessories"));
        T("chains", AccessoryChainsText(a), AccessoryChainsText(b));
        return l.Count == 0 ? null : string.Join(", ", l);
    }

    /// <summary>The live character = a fresh build of its documents (the snapshot and the accessory digest texts).</summary>
    static bool FreshOk(string label, MakerCoreTest.Harness h, MakerCoreTest.Built live)
    {
        string ad = null;
        var d = VsFresh(h, live, out string err, (a, b) => ad = AccDiff(a.Context, b.Context));
        return Check(label + ": = a fresh build (snapshot, accessory digest)", d != null && d.Equal && ad == null,
                     d == null ? err : (d.Equal ? "" : d.ToString()) + (ad != null ? "; accessory digest differs: " + ad : ""));
    }

    static string TexName(Material m, string prop)
    {
        if (!m || !m.HasProperty(prop)) return null;
        var t = m.GetTexture(prop);
        return t ? t.name : null;
    }

    // ================================================================ Run (headless)

    public static void Run()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        Rep.AppendLine($"maker accessories (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        try
        {
            if (Want("UM")) Guard("UM unit map (Unity-free)", UM);
            using (var h = MakerCoreTest.Harness.Open())
            {
                defaults = new PartDefaults(h.Catalog);
                if (Want("XT1")) Guard("XT1 colours", () => XT1(h));
                if (Want("XT2")) Guard("XT2 hair-type accessories", () => XT2(h));
                if (Want("XT3")) Guard("XT3 facts = census, defaults", () => XT3(h));
                if (Want("XT4")) Guard("XT4 parents", () => XT4(h));
                if (Want("XT5")) Guard("XT5 transform sets", () => XT5(h));
                if (Want("XT6")) Guard("XT6 the slot table", () => XT6(h));
                if (Want("XT6")) Guard("XT6 the slot-targeted ops (Add / Copy / MoveToSlot)", () => XT6b(h));
                if (Want("XT8")) Guard("XT8 item / type consequences realized", () => XT8(h));
                if (Want("XT9")) Guard("XT9 copies", () => XT9(h));
                if (Want("XT11")) Guard("XT11 «髪の毛の色を取得»", () => XT11(h));
                if (Want("XT10")) Guard("XT10 colliders after accessory swaps", () => XT10(h));
                if (Want("XT12")) Guard("XT12 digest sections", () => XT12(h));
                if (Want("XT13")) Guard("XT13 skinned accessories", () => XT13(h));
                if (Want("XT14")) Guard("XT14 a skinned accessory drawn with the body's materials", () => XT14(h));
            }
        }
        finally
        {
            FaceSetup.Seed = fseed;
            defaults = null;
        }
        Finish("maker_accessories_report.txt", "MakerAccessoriesTest UM, XT1-XT6, XT8-XT14 (XT7 is CharaStateTest S11)", sw);
    }

    // ---------------------------------------------------------------- UM

    static void UM()
    {
        HashSet<string> Map(string path, IEnumerable<string> ids = null)
        {
            var u = new HashSet<string>(StringComparer.Ordinal);
            UnitMap.Map(path, u, out _, ids);
            return u;
        }
        string J(IEnumerable<string> s) => string.Join(", ", s.OrderBy(x => x, StringComparer.Ordinal));
        var ids = new[] { "a1", "b2" };
        var u1 = Map("appearance.coord.accessories[\"a1\"].importSlot", ids);
        Check("UM accessories[\"a1\"].importSlot: the slot table only", u1.SetEquals(new[] { UnitMap.AccList }), J(u1));
        var u2 = Map("appearance.chara.hair.shaderType", ids);
        var want2 = new HashSet<string>(UnitMap.HairUnits) { UnitMap.Accessory("a1"), UnitMap.Accessory("b2") };
        Check("UM chara.hair.shaderType: the four hair units and every accessory", u2.SetEquals(want2), J(u2));
        var u3 = Map("appearance.chara.hair.back.baseColor", ids);
        Check("UM chara.hair.back.baseColor: the back hair only (no accessory)", u3.SetEquals(new[] { "hair.back" }), J(u3));
        var u4 = Map("appearance.coord.accessories[\"a1\"].hideTiming", ids);
        Check("UM accessories[\"a1\"].hideTiming: stored only", u4.Count == 0, J(u4));
        var u5 = Map("appearance.coord.accessories[\"a1\"].colors[0].smoothness", ids);
        Check("UM accessories[\"a1\"].colors[0].smoothness: that accessory", u5.SetEquals(new[] { UnitMap.Accessory("a1") }), J(u5));
    }

    // ---------------------------------------------------------------- XT1

    static void XT1(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            Info("BR-Chan accessories: " + AccessoryReport.Of(ctx, false, false));
            // three areas
            if (Installed(h, 359, Backpack, "XT1 359#0"))
            {
                int s = Table(ctx).FreeSlot();
                var e = Put(live, s, 359, Backpack);
                var r = AccessoryReport.SlotOf(ctx, s);
                Check("XT1 359#0: three colour areas, no alpha renderers", r.Loaded && r.Facts.UseColor.All(x => x) && r.Facts.AlphaRenderers == 0, r.Facts.ToString());
                var vals = new[] { (new Color4(0.8f, 0.1f, 0.2f, 1f), 0.3f, 1.5f), (new Color4(0.1f, 0.7f, 0.3f, 0.5f), -0.5f, 0.25f), (new Color4(0.2f, 0.3f, 0.9f, 1f), 0.9f, 0f) };
                SetA(live, e, "colors", () => { for (int i = 0; i < 3; i++) { e.Colors[i].Color = vals[i].Item1; e.Colors[i].Gloss = vals[i].Item2; e.Colors[i].Metallic = vals[i].Item3; } });
                var bad = new List<string>(); int n = 0;
                var cmp = CmpOf(ctx, s);
                foreach (var rend in Own(cmp.rendNormal, cmp.gameObject))
                {
                    var m = rend.sharedMaterials[0]; n++;
                    for (int i = 0; i < 3; i++)
                    {
                        if (!Near(m.GetColor(CP[i]), U(vals[i].Item1))) bad.Add($"{rend.name} {CP[i]} {Cs(m.GetColor(CP[i]))}");
                        if (Bits(m.GetFloat(GP[i])) != Bits(vals[i].Item2)) bad.Add($"{rend.name} {GP[i]} {G(m.GetFloat(GP[i]))}");
                        if (Bits(m.GetFloat(MP[i])) != Bits(vals[i].Item3)) bad.Add($"{rend.name} {MP[i]} {G(m.GetFloat(MP[i]))}");
                    }
                }
                Check("XT1 359#0: every own rendNormal renderer's first material has the three areas' colours, gloss and metallic (out-of-range values as stored)", n > 0 && bad.Count == 0, $"{n} renderers; " + Short(string.Join(" | ", bad), 600));
                FreshOk("XT1 359#0 colours", h, live);
            }
            // alpha renderers
            if (Installed(h, 353, Glasses, "XT1 353#0"))
            {
                int s = Table(ctx).FreeSlot();
                var e = Put(live, s, 353, Glasses);
                var r = AccessoryReport.SlotOf(ctx, s);
                Check("XT1 353#0: alpha renderers; colour 4 = the prefab default (alpha 0.102)", r.Facts.AlphaRenderers > 0 && Math.Abs(e.Colors[3].Color.A - 0.102f) < 1e-3, r.Facts + " colour 4 alpha " + G(e.Colors[3].Color.A));
                var cmp = CmpOf(ctx, s);
                var alpha = Own(cmp.rendAlpha, cmp.gameObject);
                var was = alpha.Select(a => a.gameObject.activeSelf).ToArray();
                SetA(live, e, "colors[3]", () => { e.Colors[3].Color = new Color4(0.4f, 0.5f, 0.6f, 0.5f); e.Colors[3].Gloss = 0.3f; e.Colors[3].Metallic = 0.7f; });
                var bad = new List<string>();
                foreach (var a in alpha)
                {
                    var m = a.sharedMaterials[0];
                    if (!Near(m.GetColor("_Color"), new Color(0.4f, 0.5f, 0.6f, 0.5f))) bad.Add(a.name + " _Color " + Cs(m.GetColor("_Color")));
                    if (Bits(m.GetFloat("_Glossiness4")) != Bits(0.3f)) bad.Add(a.name + " _Glossiness4 " + G(m.GetFloat("_Glossiness4")));
                    if (Bits(m.GetFloat("_Metallic4")) != Bits(0.7f)) bad.Add(a.name + " _Metallic4 " + G(m.GetFloat("_Metallic4")));
                }
                Check("XT1 353#0: the alpha renderers' first material: _Color = colour 4, _Glossiness4 / _Metallic4 = its gloss / metallic", alpha.Count > 0 && bad.Count == 0, $"{alpha.Count} renderers; " + Short(string.Join(" | ", bad), 600));
                FreshOk("XT1 353#0 colour 4", h, live);
                SetA(live, e, "colors[3].color", () => e.Colors[3].Color = new Color4(0.4f, 0.5f, 0.6f, 0f));
                Check("XT1 353#0 alpha 0: every alpha renderer object inactive", alpha.All(a => !a.gameObject.activeSelf), string.Join(",", alpha.Select(a => a.gameObject.activeSelf ? 1 : 0)));
                FreshOk("XT1 353#0 alpha 0", h, live);
                SetA(live, e, "colors[3].color", () => e.Colors[3].Color = new Color4(0.4f, 0.5f, 0.6f, 0.5f));
                Check("XT1 353#0 alpha back to 0.5: the prefab's activity again", alpha.Select(a => a.gameObject.activeSelf).SequenceEqual(was), string.Join(",", alpha.Select(a => a.gameObject.activeSelf ? 1 : 0)));
                FreshOk("XT1 353#0 alpha 0.5", h, live);
            }
            // a multi-slot renderer: slot 0 only (the first of the census's multi-slot rows that uses a colour area)
            int ms = Table(ctx).FreeSlot();
            AccessoryEntry me = null;
            int area = -1; string mrow = null;
            foreach (var id in new[] { 30, 32, 33, 35, 36, 37 })
            {
                string reference = V + id.ToString(CultureInfo.InvariantCulture);
                if (!Installed(h, 361, reference, "XT1 361#" + id)) continue;
                if (me == null) me = Put(live, ms, 361, reference); else SetItem(live, me, 361, reference);
                var c0 = CmpOf(ctx, ms);
                area = c0 == null ? -1 : Array.FindIndex(new[] { c0.useColor01, c0.useColor02, c0.useColor03 }, u => u != 0);
                if (area >= 0) { mrow = "361#" + id; break; }
            }
            if (area < 0) Warn("XT1 multi-slot", "no multi-slot row of 361 uses a colour area: the slot-0 rule is not exercised");
            else
            {
                var cmp = CmpOf(ctx, ms);
                string prop = CP[area];
                var multi = Own(cmp.rendNormal, cmp.gameObject).Where(x => x.sharedMaterials.Length > 1).ToList();
                var before = multi.SelectMany(x => x.sharedMaterials.Skip(1)).Select(m => m && m.HasProperty(prop) ? m.GetColor(prop) : Color.clear).ToList();
                SetA(live, me, "colors[" + area + "].color", () => me.Colors[area].Color = new Color4(0.9f, 0.05f, 0.05f, 1f));
                var after = multi.SelectMany(x => x.sharedMaterials.Skip(1)).Select(m => m && m.HasProperty(prop) ? m.GetColor(prop) : Color.clear).ToList();
                bool first = multi.All(x => Near(x.sharedMaterials[0].GetColor(prop), new Color(0.9f, 0.05f, 0.05f, 1f)));
                Check($"XT1 {mrow}: a multi-slot renderer gets colour {area + 1} ({prop}) on slot 0 only (the other slots keep their values)", multi.Count > 0 && first && before.SequenceEqual(after),
                      $"{multi.Count} multi-slot renderers, slot 0 written {first}, other slots unchanged {before.SequenceEqual(after)}");
                FreshOk("XT1 " + mrow, h, live);
            }
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- XT2

    static bool HairColoursAsDocument(CharaContext ctx, int slot, AccessoryEntry e, out string detail)
    {
        var cmp = CmpOf(ctx, slot);
        var bad = new List<string>(); int n = 0;
        foreach (var r in Own(cmp.rendNormal, cmp.gameObject))
        {
            var m = r.sharedMaterials[0]; n++;
            if (cmp.useColor01 != 0 && !Near(m.GetColor("_Color"), U(e.Colors[0].Color))) bad.Add(r.name + " _Color " + Cs(m.GetColor("_Color")));
            if (cmp.useColor02 != 0 && m.HasProperty("_TopColor") && !Near(m.GetColor("_TopColor"), U(e.Colors[1].Color))) bad.Add(r.name + " _TopColor " + Cs(m.GetColor("_TopColor")));
            if (cmp.useColor03 != 0 && m.HasProperty("_UnderColor") && !Near(m.GetColor("_UnderColor"), U(e.Colors[2].Color))) bad.Add(r.name + " _UnderColor " + Cs(m.GetColor("_UnderColor")));
            if (m.HasProperty("_Specular") && !Near(m.GetColor("_Specular"), U(e.Colors[3].Color))) bad.Add(r.name + " _Specular " + Cs(m.GetColor("_Specular")));
            if (m.HasProperty("_Smoothness") && Bits(m.GetFloat("_Smoothness")) != Bits(e.Colors[0].Smoothness)) bad.Add(r.name + " _Smoothness " + G(m.GetFloat("_Smoothness")));
            if (m.HasProperty("_Metallic") && Bits(m.GetFloat("_Metallic")) != Bits(e.Colors[0].Metallic)) bad.Add(r.name + " _Metallic " + G(m.GetFloat("_Metallic")));
        }
        detail = $"{n} renderers; areas {cmp.useColor01}{cmp.useColor02}{cmp.useColor03}" + (bad.Count > 0 ? "; " + Short(string.Join(" | ", bad), 600) : "");
        return n > 0 && bad.Count == 0;
    }

    static void XT2(MakerCoreTest.Harness h)
    {
        if (!Installed(h, 351, HairBun, "XT2 351#40")) return;
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            var hair = ctx.Chara.Hair;
            int s = Table(ctx).FreeSlot();
            var e = Put(live, s, 351, HairBun);
            var rep0 = AccessoryReport.SlotOf(ctx, s);
            Check("XT2 351#40: hair-type, the draw type applied = the document's", rep0.Facts.TypeHair && rep0.ShaderType == hair.ShaderType, rep0.Facts + " shaderType " + rep0.ShaderType);
            // a non-hair slot and a slot >= 20 with a hair-type accessory
            int other = Table(ctx).FreeSlot();
            Put(live, other, 359, Backpack);
            var extra = Put(live, 20, 351, HairBun);
            bool extraLoaded = ObjOf(ctx, 20);
            Check("XT2 slot 20 (MoreAccessories) holds 351#40", extraLoaded, AccessoryReport.SlotOf(ctx, 20)?.ToString());
            string otherBefore = string.Join("\n", FaceDigest.AccessoryRenderersOf(ctx, other).SelectMany(r => r.sharedMaterials).Select(m => { var sb = new StringBuilder(); FaceDigest.MaterialText(sb, m); return sb.ToString(); }));
            SetA(live, e, "colors", () =>
            {
                e.Colors[0].Color = new Color4(0.7f, 0.2f, 0.1f, 1f); e.Colors[1].Color = new Color4(0.1f, 0.6f, 0.2f, 1f); e.Colors[2].Color = new Color4(0.2f, 0.2f, 0.8f, 1f);
                e.Colors[3].Color = new Color4(0.9f, 0.9f, 0.5f, 1f); e.Colors[0].Smoothness = 0.8f; e.Colors[0].Metallic = 0.35f;
            });
            Check("XT2 351#40 colours: the hair colours on the first material (_Color / _TopColor / _UnderColor per area, _Specular, _Smoothness, _Metallic)", HairColoursAsDocument(ctx, s, e, out string hd), hd);
            var cmp = CmpOf(ctx, s);
            var queues = Own(cmp.rendNormal, cmp.gameObject).SelectMany(r => r.sharedMaterials.Select((m, i) => (r, i, q: m ? m.renderQueue : -1))).ToList();
            var row = SlotOf(ctx, s).Entry;
            foreach (int st in new[] { 1, 0 })
            {
                var fr = Set(live, "chara.hair.shaderType", () => hair.ShaderType = st);
                string want = "HS2Next/Shader Forge/hair_0" + (st == 0 ? "7" : "8");
                cmp = CmpOf(ctx, s);
                var bad = new List<string>(); int n = 0;
                foreach (var r in Own(cmp.rendNormal, cmp.gameObject))
                    for (int i = 0; i < r.sharedMaterials.Length; i++)
                    {
                        var m = r.sharedMaterials[i]; n++;
                        if (!m || !m.shader || m.shader.name != want) bad.Add($"{r.name}[{i}] {(m && m.shader ? m.shader.name : "null")}");
                        var q0 = queues.FirstOrDefault(t => t.r == r && t.i == i);
                        if (q0.r && m && q0.q != m.renderQueue) bad.Add($"{r.name}[{i}] queue {m.renderQueue} (was {q0.q})");
                        string wantTex = row?[st == 0 ? "TexD" : "TexC"];
                        if (HasTex(row, "TexAB") && !string.IsNullOrEmpty(wantTex) && wantTex != "0" && TexName(m, "_MainTex") != wantTex) bad.Add($"{r.name}[{i}] _MainTex {TexName(m, "_MainTex")} (want {wantTex})");
                    }
                Check($"XT2 shaderType {st}: every material of 351#40's rendNormal renderers has {want}, queues kept, _MainTex = {(st == 0 ? "TexD" : "TexC")}", n > 0 && bad.Count == 0, $"{n} slots; " + Short(string.Join(" | ", bad), 600));
                Check($"XT2 shaderType {st}: the hair colours kept on the first material", HairColoursAsDocument(ctx, s, e, out string hd2), hd2);
                var shaded = fr.AccessoryHairShaded.OrderBy(x => x).ToList();
                var hairSlots = AccessoryReport.SlotNumbers(ctx).Where(x => AccessoryReport.SlotOf(ctx, x, false, false).Facts.TypeHair).OrderBy(x => x).ToList();
                Check($"XT2 shaderType {st}: the switch re-shaded every hair-type slot ({string.Join(",", hairSlots)}, incl. {s} and 20), no other slot",
                      hairSlots.Contains(s) && hairSlots.Contains(20) && shaded.SequenceEqual(hairSlots), string.Join(",", shaded));
                var cmp20 = CmpOf(ctx, 20);
                bool extraShader = cmp20 != null && Own(cmp20.rendNormal, cmp20.gameObject).SelectMany(r => r.sharedMaterials).All(m => m && m.shader && m.shader.name == want);
                Check($"XT2 shaderType {st}: slot 20's hair-type accessory follows", extraShader);
                string otherNow = string.Join("\n", FaceDigest.AccessoryRenderersOf(ctx, other).SelectMany(r => r.sharedMaterials).Select(m => { var sb = new StringBuilder(); FaceDigest.MaterialText(sb, m); return sb.ToString(); }));
                Check($"XT2 shaderType {st}: the non-hair accessory's materials unchanged", otherNow == otherBefore, FirstDiff(otherBefore, otherNow));
                FreshOk($"XT2 shaderType {st}", h, live);
            }
            // a hair-type row without TexAB keeps its _MainTex
            ListEntry noTex = null;
            int probes = 0;
            foreach (int c in new[] { 354, 363 })
            {
                foreach (var r in h.Catalog.EntriesOf(c))
                {
                    if (r.IsVanilla || HasTex(r, "TexAB") || probes > 200) continue;
                    probes++;
                    GameObject pf = null;
                    try { pf = h.Catalog.LoadAsset<GameObject>(r, "MainAB", "MainData"); } catch (Exception) { }
                    var ca = pf ? pf.GetComponent<CmpAccessory>() : null;
                    if (ca != null && ca.typeHair != 0) { noTex = r; break; }
                }
                if (noTex != null) break;
            }
            if (noTex == null) Warn("XT2 a hair-type row without TexAB", $"none found in {probes} probes of 354 / 363 (the census has 12 such mod rows)");
            else
            {
                int s2 = Table(ctx).FreeSlot();
                var pf = h.Catalog.LoadAsset<GameObject>(noTex, "MainAB", "MainData");
                var pc = pf.GetComponent<CmpAccessory>();
                var want = Own(pc.rendNormal, pf).SelectMany(r => r.sharedMaterials).Select(m => TexName(m, "_MainTex")).ToList();
                Put(live, s2, noTex.Category, noTex.Key.ToString());
                var c2 = CmpOf(ctx, s2);
                var got = Own(c2.rendNormal, c2.gameObject).SelectMany(r => r.sharedMaterials).Select(m => TexName(m, "_MainTex")).ToList();
                Check($"XT2 {noTex.Category}:{noTex.Key} (hair-type, no TexAB): the materials keep the prefab's _MainTex", got.Count > 0 && got.SequenceEqual(want), string.Join(",", got) + " / prefab " + string.Join(",", want));
                FreshOk("XT2 hair-type without TexAB", h, live);
            }
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- XT3

    static void XT3(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            int s = Table(ctx).FreeSlot();
            var typeHair = new List<string>(); var alpha = new List<string>(); var move2 = new List<string>(); var noSet = new List<string>();
            var hidden = new List<string>(); var three = new List<string>(); var multi = new List<string>(); var nullParent = new List<string>();
            int rows = 0, noCmp = 0, badDefaults = 0, loaded = 0, badFacts = 0, badInfo = 0;
            var badF = new List<string>();
            var badD = new List<string>();
            AccessoryEntry e = null;
            // the census's example set: the vanilla rows of 351..362 without the Possess 99 rows
            for (int c = 351; c <= 362; c++)
                foreach (var row in h.Catalog.EntriesOf(c).Where(r => r.IsVanilla && r["Possess"] != "99").ToList())
                {
                    rows++;
                    string key = c + "#" + row.Key.Id.ToString(CultureInfo.InvariantCulture);
                    if (e == null) e = Put(live, s, c, row.Key.ToString());
                    else SetItem(live, e, c, row.Key.ToString());
                    var r = AccessoryReport.SlotOf(ctx, s);
                    if (!r.Loaded) continue;
                    loaded++;
                    if (r.Facts.ParentColumn == "null") nullParent.Add(key);
                    if (!r.Facts.HasCmpAccessory) { noCmp++; continue; }
                    if (r.Facts.TypeHair) typeHair.Add(key);
                    if (r.Facts.AlphaRenderers > 0) alpha.Add(key);
                    if (r.Facts.Move2) move2.Add(key);
                    if (!r.Facts.Move1 && !r.Facts.Move2) noSet.Add(key);
                    if (!r.Facts.TypeHair && !r.Facts.UseColor.Any(x => x) && r.Facts.AlphaRenderers == 0) hidden.Add(key);
                    // (the census's example count leaves the hair-type rows out)
                    if (r.Facts.UseColor.All(x => x) && !r.Facts.TypeHair) three.Add(key);
                    var cmp = CmpOf(ctx, s);
                    if (Own(cmp.rendNormal, cmp.gameObject).Any(x => x.sharedMaterials.Length > 1)) multi.Add(key);
                    if (defaults.TryAccessoryColors(c, row.Key.ToString(), out var dc))
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            var a = r.Defaults[i]; var b = dc[i];
                            if (!(Near(U(a.Color), U(b.Color), 0) && Bits(a.Gloss) == Bits(b.Gloss) && Bits(a.Metallic) == Bits(b.Metallic))) { badDefaults++; if (badD.Count < 6) badD.Add(key + "[" + i + "]"); break; }
                        }
                    }
                    else { badDefaults++; if (badD.Count < 6) badD.Add(key + " no prefab defaults"); }
                    // the prefab facts without a realization (PartDefaults.TryAccessoryFacts) and the realization's (AccessoryInfo)
                    var rf = AccessoryInfo.ToFacts(r);
                    if (!defaults.TryAccessoryFacts(c, row.Key.ToString(), out var pf) || !SameFacts(pf, rf)) { badFacts++; if (badF.Count < 6) badF.Add(key + " " + (pf?.ToString() ?? "none") + " / " + rf); }
                    var cf = AccessoryInfo.CachedAccessoryFacts(ctx, s);
                    if (cf == null || !SameFacts(cf, rf)) badInfo++;
                }
            Info($"vanilla rows {rows} (351..362, Possess 99 left out), loaded {loaded}, without CmpAccessory {noCmp}");
            Info("typeHair: " + string.Join(" ", typeHair));
            Info($"alpha renderers {alpha.Count}: " + string.Join(" ", alpha));
            Info($"trfMove02 {move2.Count}; no set: " + string.Join(" ", noSet) + $"; colour tab hidden {hidden.Count}; three areas {three.Count}; multi-slot renderers: " + string.Join(" ", multi));
            Check("XT3 typeHair: exactly 351#40 … 351#48 (census: vanilla 9)", typeHair.SequenceEqual(Enumerable.Range(40, 9).Select(i => "351#" + i)), string.Join(" ", typeHair));
            Check("XT3 alpha renderers: 21 vanilla rows incl. 353#0, 351#72, #108, #109, 355#219, 356#204, #205 (census)",
                  alpha.Count == 21 && new[] { "353#0", "351#72", "351#108", "351#109", "355#219", "356#204", "356#205" }.All(alpha.Contains), alpha.Count + " rows");
            Check("XT3 trfMove02: 35 vanilla rows incl. 351#35, 351#39, 353#21 (census)", move2.Count == 35 && new[] { "351#35", "351#39", "353#21" }.All(move2.Contains), move2.Count + " rows");
            Check("XT3 no transform set: exactly 351#202 and 351#214 (census)", noSet.OrderBy(x => x).SequenceEqual(new[] { "351#202", "351#214" }), string.Join(" ", noSet));
            Check("XT3 Parent column \"null\": exactly 351#202 and 351#214 (census)", nullParent.OrderBy(x => x).SequenceEqual(new[] { "351#202", "351#214" }), string.Join(" ", nullParent));
            Check("XT3 multi-slot rendNormal renderers: 361#30, #32, #33, #35, #36, #37 (census)", multi.OrderBy(x => x).SequenceEqual(new[] { "361#30", "361#32", "361#33", "361#35", "361#36", "361#37" }.OrderBy(x => x)), string.Join(" ", multi));
            Check("XT3 colour tab hidden (no area, no alpha renderer): 21 vanilla rows incl. 361#27 (census)", hidden.Count == 21 && hidden.Contains("361#27"), hidden.Count + " rows");
            Check("XT3 three colour areas (not hair-type): 186 vanilla rows incl. 359#0, 352#2 (census)", three.Count == 186 && three.Contains("359#0") && three.Contains("352#2"), three.Count + " rows");
            Check("XT3 the report's defaults = PartDefaults.TryAccessoryColors on every vanilla row", badDefaults == 0, badDefaults + " differ " + string.Join(", ", badD));
            Check("XT3 PartDefaults.TryAccessoryFacts (the prefab, no realization) = the realization's facts on every vanilla row", badFacts == 0, badFacts + " differ " + string.Join(" | ", badF));
            Check("XT3 AccessoryInfo's cached facts of the slot = the report's on every vanilla row", badInfo == 0, badInfo + " differ");
        }
        h.ReleaseBundles();
    }

    /// <summary>Two facts agree on everything the gates and the page read (the defaults compared by value).</summary>
    static bool SameFacts(AccessoryFacts a, AccessoryFacts b)
    {
        if (a == null || b == null) return false;
        bool cols = (a.DefaultColors == null) == (b.DefaultColors == null);
        if (cols && a.DefaultColors != null)
            for (int i = 0; i < a.DefaultColors.Length && cols; i++)
                cols = Near(U(a.DefaultColors[i].Color), U(b.DefaultColors[i].Color), 0) && Bits(a.DefaultColors[i].Gloss) == Bits(b.DefaultColors[i].Gloss) && Bits(a.DefaultColors[i].Metallic) == Bits(b.DefaultColors[i].Metallic);
        return cols && a.HasCmpAccessory == b.HasCmpAccessory && a.TypeHair == b.TypeHair && a.UseColor.SequenceEqual(b.UseColor) && a.AlphaRenderers == b.AlphaRenderers
               && a.Move1 == b.Move1 && a.Move2 == b.Move2 && a.ParentColumn == b.ParentColumn && a.DynamicBones == b.DynamicBones && a.Colliders == b.Colliders
               && a.Category == b.Category && a.PrefabName == b.PrefabName;
    }

    // ---------------------------------------------------------------- XT4

    static void XT4(MakerCoreTest.Harness h)
    {
        if (!Installed(h, 352, StarEar, "XT4 352#0")) return;
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            int s = Table(ctx).FreeSlot();
            var e = Put(live, s, 352, StarEar);
            var row = SlotOf(ctx, s).Entry;
            var prefab = h.Catalog.LoadAsset<GameObject>(row, "MainAB", "MainData");
            var pl = prefab.transform.localPosition; var pr = prefab.transform.localRotation; var ps = prefab.transform.localScale;
            var keys = AttachPointTable.Vanilla.Groups.SelectMany(g => g.Points).ToList();
            var bad = new List<string>();
            foreach (var key in keys)
            {
                SetA(live, e, "parent", () => e.Parent = key);
                var go = ObjOf(ctx, s);
                var rp = AccessoryReport.SlotOf(ctx, s, false, false);
                var par = go ? go.transform.parent : null;
                bool inRig = par && (par.IsChildOf(ctx.Anim.transform) || par.IsChildOf(ctx.HeadBone.transform)) && !ctx.Accessories.Any(a => a != null && a.Slot != s && a.Object && par.IsChildOf(a.Object.transform));
                bool trs = go && go.transform.localPosition == pl && go.transform.localRotation == pr && go.transform.localScale == ps;
                if (!go || !par || par.name != key || rp.AnchorName != key || !inRig || !trs) bad.Add($"{key}: parent {(par ? par.name : "none")} report {rp.AnchorName} rig {inRig} trs {trs}");
            }
            Check($"XT4 352#0 on each of the {keys.Count} keys: the slot object under the key's transform of the skeleton, the prefab-local TRS kept, the report's anchor = the key",
                  keys.Count == 54 && bad.Count == 0, Short(string.Join(" | ", bad), 800));
            FreshOk("XT4 the last key", h, live);
            SetA(live, e, "parent", () => e.Parent = "");
            var rd = AccessoryReport.SlotOf(ctx, s, false, false);
            Check("XT4 «初期親» (parent \"\"): the row's Parent column", rd.AnchorName == row["Parent"] && rd.Facts.ParentColumn == row["Parent"], rd.AnchorName + " / " + row["Parent"]);
            int logBefore = ctx.Log.Count;
            SetA(live, e, "parent", () => e.Parent = "N_xt_unknown");
            var ru = AccessoryReport.SlotOf(ctx, s, false, false);
            bool logged = ctx.Log.Skip(logBefore).Any(l => l.Contains("anchor N_xt_unknown not found"));
            Check("XT4 an unknown stored key: kept in the document, no anchor (report \"none\"), logged", e.Parent == "N_xt_unknown" && ru.AnchorName == "none" && logged, ru.AnchorName + " logged " + logged);
            FreshOk("XT4 unknown key", h, live);
            SetA(live, e, "parent", () => e.Parent = "");
            foreach (var hw in HeadWeighted)
            {
                if (!Installed(h, 351, hw, "XT4 351 head-weighted")) continue;
                int s2 = Table(ctx).FreeSlot();
                var e2 = Put(live, s2, 351, hw, "N_Hitai");
                var r2 = AccessoryReport.SlotOf(ctx, s2, false, false);
                var go = ObjOf(ctx, s2);
                Check($"XT4 351:{hw} (Parent \"null\") with a stored key: head bones, under the body top, the key kept in the document",
                      go && r2.AnchorName == "head bones" && go.transform.parent == ctx.BodyTop.transform && e2.Parent == "N_Hitai" && r2.Facts.HeadWeighted, r2.AnchorName + " parent " + (go && go.transform.parent ? go.transform.parent.name : "-"));
                FreshOk($"XT4 351:{hw}", h, live);
            }
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- XT5

    static void XT5(MakerCoreTest.Harness h)
    {
        if (!Installed(h, 351, SilkHat, "XT5 351#35")) return;
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            int s = Table(ctx).FreeSlot();
            var e = Put(live, s, 351, SilkHat);
            var sets = new[] { (new Vec3(12.3f, -150f, 0.05f), new Vec3(-30f, 400f, 359.5f), new Vec3(0f, 0f, 0f)), (new Vec3(1f, 2f, 3f), new Vec3(10f, 20f, 30f), new Vec3(2f, 0.5f, 120f)) };
            SetA(live, e, "moves", () => { for (int c = 0; c < 2; c++) { e.Moves[c].Position = sets[c].Item1; e.Moves[c].Rotation = sets[c].Item2; e.Moves[c].Scale = sets[c].Item3; } });
            var cmp = CmpOf(ctx, s);
            var trfs = new[] { cmp.trfMove01, cmp.trfMove02 };
            var bad = new List<string>();
            for (int c = 0; c < 2; c++)
            {
                var t = trfs[c];
                if (!t) { bad.Add("set " + (c + 1) + " missing"); continue; }
                var p = EditContext.U(sets[c].Item1) * 0.1f;
                var q = Quaternion.Euler(EditContext.U(sets[c].Item2));
                var sc = EditContext.U(sets[c].Item3); if (sc == Vector3.zero) sc = Vector3.one;
                if (t.localPosition != p) bad.Add($"set {c + 1} position {t.localPosition}");
                if (Quaternion.Angle(t.localRotation, q) > 1e-3f && Quaternion.Angle(t.localRotation, new Quaternion(-q.x, -q.y, -q.z, -q.w)) > 1e-3f) bad.Add($"set {c + 1} rotation {t.localEulerAngles}");
                if (t.localScale != sc) bad.Add($"set {c + 1} scale {t.localScale}");
            }
            Check("XT5 351#35: both sets' local TRS = stored × 0.1 / Euler / scale ((0,0,0) → one), values outside the UI limits applied as stored", bad.Count == 0, string.Join(" | ", bad));
            var r = AccessoryReport.SlotOf(ctx, s, false, false);
            Check("XT5 the report's moves = the transforms", r.Moves[0] != null && r.Moves[1] != null && r.Moves[0].Position == trfs[0].localPosition && r.Moves[1].Scale == trfs[1].localScale);
            FreshOk("XT5 351#35", h, live);
            // a set the prefab lacks
            if (Installed(h, 352, StarEar, "XT5 352#0"))
            {
                int s2 = Table(ctx).FreeSlot();
                var e2 = Put(live, s2, 352, StarEar);
                var c2 = CmpOf(ctx, s2);
                if (c2.trfMove02) Warn("XT5 352#0", "has trfMove02; the lacking-set case is not exercised");
                else
                {
                    string before = FaceDigest.ObjectsText(ctx, "accessories");
                    SetA(live, e2, "moves[1]", () => { e2.Moves[1].Position = new Vec3(5, 5, 5); e2.Moves[1].Rotation = new Vec3(45, 0, 0); e2.Moves[1].Scale = new Vec3(3, 3, 3); });
                    Check("XT5 352#0 without trfMove02: a non-identity set 2 changes nothing realized", FaceDigest.ObjectsText(ctx, "accessories") == before);
                    FreshOk("XT5 set the prefab lacks", h, live);
                }
            }
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- XT6

    static void XT6(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            var used = new HashSet<int>(AccessoryReport.SlotNumbers(ctx));
            Info("BR-Chan's build slots: " + string.Join(",", used.OrderBy(x => x)));
            var gaps = new List<int>();
            for (int s = 2; gaps.Count < 3 && s < 20; s += 3) if (!used.Contains(s)) gaps.Add(s);
            var entries = new List<AccessoryEntry>();
            foreach (int g in gaps) entries.Add(Put(live, g, 352, StarEar));
            bool at = gaps.All(g => ObjOf(ctx, g) && ObjOf(ctx, g).name == $"ca_slot{g:00}" && AccessoryReport.SlotOf(ctx, g).Id == entries[gaps.IndexOf(g)].Id);
            Check("XT6 entries with gaps realize at their preferred slots (ca_slotNN, the report's slot = the table's)", gaps.Count == 3 && at, string.Join(",", gaps));
            FreshOk("XT6 gaps", h, live);
            int target = Enumerable.Range(0, 20).First(x => !used.Contains(x) && !gaps.Contains(x));
            var moved = entries[2];
            int from = gaps[2];
            var fr = SetA(live, moved, "importSlot", () => moved.ImportSlot = target);
            Check($"XT6 an importSlot change {from} → {target}: acc.list ran, the object moved, slot {from} empty",
                  fr.Units.Contains(UnitMap.AccList) && ObjOf(ctx, target) && !ObjOf(ctx, from) && AccessoryReport.SlotOfAccessory(ctx, moved.Id) == target,
                  string.Join(",", fr.Units));
            FreshOk("XT6 moved", h, live);
            var ex = Put(live, 25, 355, V + "0");
            var rx = AccessoryReport.SlotOf(ctx, 25);
            Check("XT6 slot 25 (MoreAccessories on): loaded, extra", rx.Loaded && rx.Extra && !rx.NotLoaded && ObjOf(ctx, 25).name == "ca_slot25", rx.ToString());
            FreshOk("XT6 slot 25", h, live);
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- XT10

    static void XT10(MakerCoreTest.Harness h)
    {
        if (!Installed(h, 352, ColliderEarring, "XT10 collider earring") || !Installed(h, 357, RingChest, "XT10 357#0")) return;
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            var e0 = Put(live, 0, 352, ColliderEarring);
            var r0 = AccessoryReport.SlotOf(ctx, 0);
            Check("XT10 slot 0: the earring carries a collider", r0.Facts.Colliders > 0, r0.Facts.ToString());
            var e3 = Put(live, 3, 357, RingChest);
            Info("chains: " + Short(AccessoryChainsText(ctx).Replace("\n", " | "), 600));
            FreshOk("XT10 slot 0 collider earring + slot 3 chain accessory", h, live);
            string alt = NextRow(h.Catalog, 357, RingChest, row => row.IsVanilla);
            if (alt != null)
            {
                SetItem(live, e3, 357, alt);
                FreshOk("XT10 slot 3's item swapped (" + alt + ")", h, live);
                SetItem(live, e3, 357, RingChest);
            }
            SetItem(live, e0, 352, StarEar);
            var c3 = AccessoryChainsText(ctx);
            FreshOk("XT10 slot 0's item swapped (no collider): slot 3's chains re-listed", h, live);
            Info("chains after: " + Short(c3.Replace("\n", " | "), 600));
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- XT12

    static void XT12(MakerCoreTest.Harness h)
    {
        if (!Installed(h, 359, Backpack, "XT12 359#0")) return;
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            int s = Table(ctx).FreeSlot();
            var e = Put(live, s, 359, Backpack);
            FaceDigest.Result D() => FaceDigest.Compute(ctx, "accessories", false);
            var d0 = D();
            Check("XT12 part accessories: materials and objects sections; no creators / bones (empty texts)",
                  d0.Materials != null && d0.Objects != null && d0.Creators == FaceDigest.Sha256("") && d0.Bones == FaceDigest.Sha256("") && FaceDigest.IsPart("accessories"), d0.ToString());
            SetA(live, e, "colors[0].color", () => e.Colors[0].Color = new Color4(0.3f, 0.6f, 0.9f, 1f));
            var d1 = D();
            Check("XT12 a colour edit: materials change, objects stay", d1.Materials != d0.Materials && d1.Objects == d0.Objects);
            SetA(live, e, "moves[0].position", () => e.Moves[0].Position = new Vec3(1, 2, 3));
            var d2 = D();
            Check("XT12 a transform edit: objects change, materials stay", d2.Objects != d1.Objects && d2.Materials == d1.Materials);
            SetA(live, e, "parent", () => e.Parent = "N_Waist");
            var d3 = D();
            Check("XT12 a parent change: objects change (the parent path)", d3.Objects != d2.Objects);
            Info("parent change: materials " + (d3.Materials == d2.Materials ? "unchanged" : "changed"));
            var go = ObjOf(ctx, s);
            go.SetActive(false);
            var d4 = D();
            go.SetActive(true);
            Check("XT12 the slot object's activity: objects change", d4.Objects != d3.Objects && D().Objects == d3.Objects);
            var all = FaceDigest.Compute(ctx, "all", false);
            Check("XT12 part all: the objects section covers the clothes and the accessories", all.Objects != null && all.Objects != FaceDigest.Compute(ctx, "clothes", false).Objects);
            FreshOk("XT12", h, live);
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- XT13

    const string SkinnedNails = "whinny.halloweenspecial02.accs#4";   // 361, default parent N_Hand_R (the 1.0.0 bug report)
    const string SkinnedMask = "[Blake]Monster Mask#0";               // 351, no rig copy, weighted on face bones of the head rig

    static void XT13(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok) return;
            var ctx = live.Context;
            if (Installed(h, 361, SkinnedNails, "XT13 nails"))
            {
                int s = Table(ctx).FreeSlot();
                var e = Put(live, s, 361, SkinnedNails);
                SkinnedOk($"XT13 361:{SkinnedNails} in slot {s}", h, ctx, s, true);
                NearHands($"XT13 361:{SkinnedNails}", h, ctx, s);
                FreshOk("XT13 nails", h, live);
                SetItem(live, e, 361, MultiSlot);
                Check("XT13 nails → 361#30: a plain row, no SkinnedAccessory", ObjOf(ctx, s) && ObjOf(ctx, s).GetComponent<SkinnedAccessory>() == null);
                FreshOk("XT13 nails → 361#30", h, live);
                SetItem(live, e, 361, SkinnedNails);
                SkinnedOk("XT13 361#30 → nails (the edit reload)", h, ctx, s, true);
                FreshOk("XT13 361#30 → nails", h, live);
                Put(live, 25, 361, SkinnedNails);
                SkinnedOk("XT13 nails in slot 25 (MoreAccessories)", h, ctx, 25, true);
                FreshOk("XT13 slot 25", h, live);
            }
            if (Installed(h, 351, SkinnedMask, "XT13 mask"))
            {
                int s = Table(ctx).FreeSlot();
                Put(live, s, 351, SkinnedMask);
                SkinnedOk($"XT13 351:{SkinnedMask} in slot {s}", h, ctx, s, false);
                var smr = ObjOf(ctx, s).GetComponent<SkinnedAccessory>().meshRenderers[0];
                var face = smr.bones.Where(b => b && b.name.StartsWith("cf_J_Face", StringComparison.Ordinal)).ToList();
                Check("XT13 mask: its face bones are the head rig's (p_cf_head_bone)",
                      face.Count > 0 && face.All(b => ctx.HeadBones.TryGetValue(b.name, out var hb) && hb == b),
                      string.Join(",", face.Select(b => b.name)));
                FreshOk("XT13 mask", h, live);
            }
            XT13Rules(ctx);
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- XT14

    const string SkinLegs = "roy12.skindentations#0";   // 358 "Skindentation Legs 00": a SkinnedAccessory and a ChaCopyBodyMaterial

    static void XT14(MakerCoreTest.Harness h)
    {
        using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
        {
            var live = bb.B;
            if (!live.Ok || !Installed(h, 358, SkinLegs, "XT14 legs")) return;
            var ctx = live.Context;
            int s = Table(ctx).FreeSlot();
            var e = Put(live, s, 358, SkinLegs);
            SkinnedOk($"XT14 358:{SkinLegs} in slot {s}", h, ctx, s, true);
            MakerClothesTest.BodyMaterialsOk($"XT14 358:{SkinLegs}", live, ObjOf(ctx, s));
            FreshOk("XT14 legs", h, live);
            string bodyBefore = FaceDigest.MaterialsText(ctx, "body");
            SetA(live, e, "colors[0].color", () => e.Colors[0].Color = new Color4(0.3f, 0.6f, 0.9f, 1f));
            Check("XT14 a colour edit of the slot: the body's materials unchanged", FaceDigest.MaterialsText(ctx, "body") == bodyBefore);
            MakerClothesTest.BodyMaterialsOk("XT14 after the colour edit", live, ObjOf(ctx, s));
            FreshOk("XT14 colour edit", h, live);
            var bodyMats = ctx.BodyRenderer.sharedMaterials;
            ctx.Coord.Accessories.Remove(e);
            ctx.Editor.MarkDirty("coord.accessories");
            ctx.Editor.Flush();
            var snap = live.Snapshot();
            Check("XT14 removed: the body's materials alive, owned and the body's; no targets left; G1",
                  !ObjOf(ctx, s) && bodyMats.All(m => m && ctx.OwnedMaterials.Contains(m)) && ctx.BodyRenderer.sharedMaterials.SequenceEqual(bodyMats)
                  && ctx.BodyMaterialTargets.Count == 0 && snap.G1Pass, $"targets {ctx.BodyMaterialTargets.Count}; G1 {snap.G1Text}");
            FreshOk("XT14 removed", h, live);
        }
        h.ReleaseBundles();
    }

    // The merge table states it, written again here: pre-order from cf_J_Root, the last transform of a name
    // wins, the subtrees of cf_t_root and the 54 attach points left out.
    static Dictionary<string, Transform> MergeTable(CharaContext ctx)
    {
        var stops = new HashSet<string>(DocEnum.AttachPoints) { "cf_t_root" };
        var d = new Dictionary<string, Transform>();
        void Walk(Transform t)
        {
            d[t.name] = t;
            foreach (Transform c in t) if (!stops.Contains(c.name)) Walk(c);
        }
        Walk(ctx.BodyBones["cf_J_Root"]);
        return d;
    }

    /// <summary>The slot's skinned accessory after the load: per listed renderer, every bone the prefab names with a name of the
    /// merge table is that table's transform and every other bone is the instance's own (destroyed with the rig copy when it
    /// lay inside it); root bone cf_J_Root, the wide bounds, enabled; the rig copy gone; no transform under the slot object
    /// carries a name of the body rig.</summary>
    static void SkinnedOk(string label, MakerCoreTest.Harness h, CharaContext ctx, int slot, bool hasCopy)
    {
        var go = ObjOf(ctx, slot);
        var sa = go ? go.GetComponent<SkinnedAccessory>() : null;
        if (!Check(label + ": loaded with a SkinnedAccessory", sa != null && sa.meshRenderers != null && sa.meshRenderers.Count > 0)) return;
        var prefab = h.Catalog.LoadAsset<GameObject>(SlotOf(ctx, slot).Entry, "MainAB", "MainData");
        var psa = prefab ? prefab.GetComponent<SkinnedAccessory>() : null;
        if (!Check(label + ": the prefab's component", psa != null && psa.meshRenderers.Count == sa.meshRenderers.Count)) return;
        var table = MergeTable(ctx);
        var jRoot = ctx.BodyBones["cf_J_Root"];
        var bad = new List<string>();
        int mapped = 0, keptGone = 0, keptOwn = 0;
        for (int k = 0; k < sa.meshRenderers.Count; k++)
        {
            var r = sa.meshRenderers[k];
            var pb = psa.meshRenderers[k].bones;
            var b = r.bones;
            if (b.Length != pb.Length) { bad.Add($"{r.name}: {b.Length} bones, prefab {pb.Length}"); continue; }
            for (int i = 0; i < b.Length; i++)
            {
                if (!pb[i]) { if ((object)b[i] != null && b[i]) bad.Add($"{r.name}[{i}] not null"); continue; }
                if (table.TryGetValue(pb[i].name, out var want)) { if (b[i] == want) mapped++; else bad.Add($"{r.name}[{i}] {pb[i].name}: not the rig's"); }
                else if (!b[i] && psa.skeleton && pb[i].IsChildOf(psa.skeleton.transform)) keptGone++;
                else if (b[i] && b[i].IsChildOf(go.transform) && b[i].name == pb[i].name) keptOwn++;
                else bad.Add($"{r.name}[{i}] {pb[i].name}: {(b[i] ? b[i].name : "null")}");
            }
            if (r.rootBone != jRoot) bad.Add($"{r.name}: rootBone {(r.rootBone ? r.rootBone.name : "null")}");
            if (r.localBounds.center != new Vector3(0, 10, 0) || r.localBounds.size != new Vector3(20, 20, 20)) bad.Add($"{r.name}: bounds {r.localBounds}");
            if (!r.enabled) bad.Add($"{r.name}: disabled");
        }
        Check($"{label}: {sa.meshRenderers.Count} renderers on the rig ({mapped} bones the rig's, {keptGone} kept and gone with the copy, {keptOwn} kept own), root cf_J_Root, the wide bounds",
              bad.Count == 0 && mapped > 0, Short(string.Join(" | ", bad), 800));
        // (without a copy the prefab's own bones stay under the slot object, names and all, as in the game)
        var rigNames = go.GetComponentsInChildren<Transform>(true).Where(t => t != go.transform && ctx.BodyBones.ContainsKey(t.name)).Select(t => t.name).ToList();
        Check(hasCopy ? $"{label}: the rig copy destroyed, no body-rig name under the slot object" : $"{label}: the prefab has no rig copy",
              !sa.skeleton && hasCopy == (bool)psa.skeleton && (!hasCopy || rigNames.Count == 0), Short(string.Join(",", rigNames), 300));
    }

    /// <summary>World positions of a renderer's vertices skinned on the CPU from its bones, bind poses and the four weights
    /// (null when the mesh is not readable; a weighted bone that is missing counts in <paramref name="lost"/>).</summary>
    static Vector3[] SkinnedVertices(SkinnedMeshRenderer r, out int lost)
    {
        lost = 0;
        var m = r.sharedMesh;
        if (!m || !m.isReadable) return null;
        var v = m.vertices; var bw = m.boneWeights; var bp = m.bindposes; var b = r.bones;
        if (bw.Length != v.Length || bp.Length != b.Length) return null;
        var mats = new Matrix4x4[b.Length];
        var have = new bool[b.Length];
        for (int i = 0; i < b.Length; i++) if (b[i]) { mats[i] = b[i].localToWorldMatrix * bp[i]; have[i] = true; }
        var o = new Vector3[v.Length];
        int missing = 0;
        for (int k = 0; k < v.Length; k++)
        {
            var w = bw[k];
            Vector3 p = Vector3.zero;
            void Add(int i, float x) { if (x <= 0) return; if (i < 0 || i >= have.Length || !have[i]) { missing++; return; } p += x * mats[i].MultiplyPoint3x4(v[k]); }
            Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
            o[k] = p;
        }
        lost = missing;
        return o;
    }

    /// <summary>The skinned vertices of the slot's first listed renderer lie at the hands, while the same prefab placed as
    /// 1.0.0 left it (its own rig copy under the anchor) lies away from them.</summary>
    static void NearHands(string label, MakerCoreTest.Harness h, CharaContext ctx, int slot)
    {
        var hand = ctx.BodyBones.Where(kv => kv.Key.StartsWith("cf_J_Hand_", StringComparison.Ordinal) && kv.Value).Select(kv => kv.Value.position).ToList();
        float span = Vector3.Distance(ctx.BodyBones["cf_J_Hand_R"].position, ctx.BodyBones["cf_J_Hand_Middle03_R"].position);
        (float mean, float max, int n) Dist(SkinnedMeshRenderer r)
        {
            var v = SkinnedVertices(r, out int lost);
            if (v == null || v.Length == 0 || lost > 0) return (float.NaN, float.NaN, 0);
            double sum = 0; float mx = 0;
            foreach (var w in v)
            {
                float d = hand.Min(q => Vector3.Distance(q, w));
                sum += d; mx = Mathf.Max(mx, d);
            }
            return ((float)(sum / v.Length), mx, v.Length);
        }
        var go = ObjOf(ctx, slot);
        // (the CPU skinning, because BakeMesh in a -nographics Editor returns the mesh unskinned)
        var now = Dist(go.GetComponent<SkinnedAccessory>().meshRenderers[0]);
        if (now.n == 0) { Warn(label, "the mesh is not readable or a weighted bone is missing: the position check is skipped"); return; }
        var prefab = h.Catalog.LoadAsset<GameObject>(SlotOf(ctx, slot).Entry, "MainAB", "MainData");
        var old = Object.Instantiate(prefab, go.transform.parent, false);
        (float mean, float max, int n) was;
        try
        {
            old.transform.localPosition = go.transform.localPosition; old.transform.localRotation = go.transform.localRotation; old.transform.localScale = go.transform.localScale;
            was = Dist(old.GetComponent<SkinnedAccessory>().meshRenderers[0]);
        }
        finally { Object.DestroyImmediate(old); }
        Info($"{label}: {now.n} vertices; nearest hand bone: merged mean {G(now.mean)} max {G(now.max)}, unmerged (1.0.0) mean {G(was.mean)} max {G(was.max)}; hand span {G(span)}");
        Check($"{label}: every vertex within one hand span of a hand bone (merged)", now.max <= span, $"max {G(now.max)} span {G(span)}");
        Check($"{label}: the unmerged prefab lies away from the hands (the 1.0.0 symptom)", was.mean > 2 * span, $"mean {G(was.mean)} span {G(span)}");
    }

    // The rules on built objects: a full list merges and destroys the copy; a null entry stops the merge there
    // and keeps the copy; a copy that is the slot object itself is never destroyed; an object without the component is
    // untouched.
    static void XT13Rules(CharaContext ctx)
    {
        var x = ctx.Edit;
        var jRoot = ctx.BodyBones["cf_J_Root"];
        var hips = ctx.BodyBones["cf_J_Hips"];
        var made = new List<GameObject>();
        (GameObject go, SkinnedAccessory sa, GameObject copy, Transform copyHips, Transform tip, List<SkinnedMeshRenderer> r) Make(string name, int renderers)
        {
            var go = new GameObject("xt13_" + name); made.Add(go);
            go.AddComponent<CmpAccessory>();
            var sa = go.AddComponent<SkinnedAccessory>();
            var copy = new GameObject("cf_J_Root"); copy.transform.SetParent(go.transform, false);
            var ch = new GameObject("cf_J_Hips").transform; ch.SetParent(copy.transform, false);
            var tip = new GameObject("xt13_tip").transform; tip.SetParent(ch, false);
            var rs = new List<SkinnedMeshRenderer>();
            for (int i = 0; i < renderers; i++)
            {
                var o = new GameObject("xt13_r" + i); o.transform.SetParent(go.transform, false);
                var r = o.AddComponent<SkinnedMeshRenderer>();
                r.bones = new[] { ch, tip, null };
                r.rootBone = ch;
                rs.Add(r);
            }
            sa.skeleton = copy;
            sa.meshRenderers = new List<SkinnedMeshRenderer>(rs);
            return (go, sa, copy, ch, tip, rs);
        }
        bool Merged(SkinnedMeshRenderer r) => r.rootBone == jRoot && r.bones[0] == hips && r.bones[2] == null;
        var self = new AccessorySlot { Slot = 99, Type = 361 };
        try
        {
            var a = Make("full", 2);
            x.MergeSkinnedAccessory(a.go, self);
            Check("XT13 rules: a full list: both renderers on the rig, the unknown bone kept and gone with the destroyed copy",
                  a.r.All(Merged) && a.r.All(r => !r.bones[1]) && !a.copy && a.go);
            var b = Make("null", 2);
            b.sa.meshRenderers.Insert(1, null);
            x.MergeSkinnedAccessory(b.go, self);
            Check("XT13 rules: a null entry: the renderer before it merged, the one after it untouched, the copy kept",
                  Merged(b.r[0]) && b.r[1].rootBone == b.copyHips && b.r[1].bones[0] == b.copyHips && b.copy && b.copy.transform.parent == b.go.transform);
            var c = Make("self", 1);
            c.sa.skeleton = c.go;
            x.MergeSkinnedAccessory(c.go, self);
            Check("XT13 rules: a copy that is the slot object: merged, the object kept", Merged(c.r[0]) && c.go && c.copy);
            var d = new GameObject("xt13_plain"); made.Add(d);
            d.AddComponent<CmpAccessory>();
            var dr = new GameObject("xt13_r").AddComponent<SkinnedMeshRenderer>(); dr.transform.SetParent(d.transform, false);
            var dch = new GameObject("cf_J_Hips").transform; dch.SetParent(d.transform, false);
            dr.bones = new[] { dch };
            x.MergeSkinnedAccessory(d, self);
            Check("XT13 rules: no SkinnedAccessory: untouched", dr.bones[0] == dch && dch);
            Check("XT13 rules: the body bones are unchanged (the table holds no slot object)", ctx.BodyBones["cf_J_Hips"] == hips && hips.IsChildOf(ctx.Anim.transform));
        }
        finally { foreach (var g in made) if (g) Object.DestroyImmediate(g); }
    }

    // ================================================================ the op-level cases (scene nodes; package B's ops)

    static TreeNode TN(MakerEditTest.Node n) => n.Session.Model.Find(n.Id);
    static AccessorySlotRule SlotsOf(MakerEditTest.Node n) => StudioOps.AccessorySlotsOf(TN(n));
    static AccessoryEntry EntryAt(MakerEditTest.Node n, int slot) => SlotsOf(n)?.EntryAt(slot);
    static AccessoryEntry EntryOf(MakerEditTest.Node n, string id) => n.Stored?.Coord?.FindAccessory(id);
    static GameObject NodeObj(MakerEditTest.Node n, int slot) { var c = n.Real?.Context; return c != null && slot >= 0 && slot < c.Accessories.Count ? c.Accessories[slot].Object : null; }
    static string NAP(string id, string member) => AppearancePaths.Accessory(id, member);

    static bool NodeFresh(string label, MakerCoreTest.Harness h, MakerEditTest.Node n)
    {
        var d = VsFresh(h, n);
        return Check(label + ": = a fresh node", d != null && d.Equal, d?.ToString());
    }

    static OpResult Edit(MakerEditTest.Node n, string id, string member, object value)
    {
        var r = n.Ops.EditAppearance(new[] { n.Id }, new List<(string, object)> { (NAP(id, member), value) }, User);
        n.Apply();
        return r;
    }

    static AccessoryEntry AddAt(MakerEditTest.Node n, int category, string item, int slot)
    {
        var r = n.Ops.AddAccessory(n.Id, category, item, null, null, User, slot);
        n.Apply();
        Check($"add {category}:{item} at slot {slot + 1:00}: ok", r.Ok, r.Message);
        return EntryAt(n, slot);
    }

    static Color4[] HairColours(HairPart p) => new[] { p.BaseColor, p.TopColor, p.UnderColor, p.Specular };

    static bool SameColour(Color4 a, Color4 b) => Bits(a.R) == Bits(b.R) && Bits(a.G) == Bits(b.G) && Bits(a.B) == Bits(b.B) && Bits(a.A) == Bits(b.A);

    // ---------------------------------------------------------------- XT6b: the slot-targeted ops

    static void XT6b(MakerCoreTest.Harness h)
    {
        using (var n = NodeOf(h, FemaleCard))
        {
            int s = SlotsOf(n).LowestFree();
            var e = AddAt(n, 352, StarEar, s);
            Check("XT6 AddAccessory with a slot: the entry in that slot (preferred slot = it), its object ca_slotNN there, AccessoryIdOfSlot = it",
                  e != null && e.ImportSlot == s && NodeObj(n, s) && NodeObj(n, s).name == $"ca_slot{s:00}" && StudioOps.AccessoryIdOfSlot(TN(n), s) == e.Id);
            var bad = n.Ops.AddAccessory(n.Id, 352, StarEar, null, null, User, s);
            Check("XT6 AddAccessory to a worn slot: bad_args on slot", !bad.Ok && bad.Arg == "slot", bad.Message);
            int t = SlotsOf(n).LowestFree();
            var rc = n.Ops.CopyAccessory(n.Id, e.Id, t, null, AccessoryCopyParts.Slot, false, MirrorAxis.None, User);
            n.Apply();
            var ce = EntryAt(n, t);
            Check($"XT6 CopyAccessory to free slot {t + 1:00}: a new entry there with the source's item, its object loaded", rc.Ok && ce != null && ce.Id != e.Id && ce.Item == e.Item && NodeObj(n, t), rc.Message);
            int u = SlotsOf(n).FreeSlots().First(x => x > t);
            var rm = n.Ops.MoveAccessoryToSlot(n.Id, e.Id, u, User);
            n.Apply();
            Check($"XT6 MoveAccessoryToSlot {s + 1:00} → {u + 1:00}: the slot table, the object and AccessoryIdOfSlot follow",
                  rm.Ok && StudioOps.AccessorySlotOf(TN(n), e.Id) == u && NodeObj(n, u) && !NodeObj(n, s) && StudioOps.AccessoryIdOfSlot(TN(n), u) == e.Id, rm.Message);
            NodeFresh("XT6 ops", h, n);
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- XT8: item / type consequences realized

    static void XT8(MakerCoreTest.Harness h)
    {
        using (var n = NodeOf(h, FemaleCard))
        {
            var stage = n.Real.Context.Motion?.Get<DynamicBoneStage>();
            int s = SlotsOf(n).LowestFree();
            var e = AddAt(n, 357, RingChest, s);
            Edit(n, e.Id, "noShake", true);
            bool chainsOff = stage != null && stage.Chains.Where(c => c.Category == DynamicBoneCategory.Accessory && c.Slot == s).All(c => c.NoShake);
            int chains = stage?.Chains.Count(c => c.Category == DynamicBoneCategory.Accessory && c.Slot == s) ?? 0;
            Check("XT8 357#0 with «揺らさない»: its chains carry noShake", chains > 0 && chainsOff, chains + " chains");
            string alt = NextRow(h.Catalog, 357, RingChest, r => r.IsVanilla);
            var ri = n.Ops.ChangeItem(new[] { n.Id }, NAP(e.Id, "item"), alt, User);
            n.Apply();
            e = EntryOf(n, e.Id);
            stage = n.Real.Context.Motion?.Get<DynamicBoneStage>();
            Check($"XT8 an item change (357:{alt}): noShake off in the document and on the slot's chains",
                  ri.Ok && e != null && !e.NoShake && (stage == null || stage.Chains.Where(c => c.Category == DynamicBoneCategory.Accessory && c.Slot == s).All(c => !c.NoShake)), ri.Message);
            NodeFresh("XT8 item change", h, n);
            // non-hair → hair-type: the back hair's colours; hair → hair keeps them
            int s2 = SlotsOf(n).LowestFree();
            var e2 = AddAt(n, 351, V + "0", s2);
            var rh = n.Ops.ChangeItem(new[] { n.Id }, NAP(e2.Id, "item"), HairBun, User);
            n.Apply();
            e2 = EntryOf(n, e2.Id);
            var back = n.Stored.Chara.Hair.Parts[0];
            var want = HairColours(back);
            bool cols = e2 != null && Enumerable.Range(0, 4).All(i => SameColour(e2.Colors[i].Color, want[i])) && Bits(e2.Colors[0].Smoothness) == Bits(back.Smoothness) && Bits(e2.Colors[0].Metallic) == Bits(back.Metallic);
            Check("XT8 351#0 → 351#40 (non-hair → hair-type): colours 1-4 = the back hair's base / top / under / specular, smoothness and metallic its own", rh.Ok && cols, rh.Message);
            Check("XT8 351#40 realized: the hair colours on the first material", HairColoursAsDocument(n.Real.Context, s2, e2, out string hd), hd);
            Edit(n, e2.Id, "colors[0].color", new Color4(0.6f, 0.3f, 0.2f, 1f));
            var kept = EntryOf(n, e2.Id).Colors.Select(c => c.Color).ToArray();
            n.Ops.ChangeItem(new[] { n.Id }, NAP(e2.Id, "item"), HairBun2, User);
            n.Apply();
            var e3 = EntryOf(n, e2.Id);
            Check("XT8 351#40 → 351#41 (hair → hair): colours kept", e3 != null && Enumerable.Range(0, 4).All(i => SameColour(e3.Colors[i].Color, kept[i])));
            NodeFresh("XT8 hair rule", h, n);
            // type change: row 0, the row's parent, identity sets, noShake off
            Edit(n, e.Id, "moves[0].position", new Vec3(3, 4, 5));
            var rt = n.Ops.ChangeItem(new[] { n.Id }, NAP(e.Id, "category"), "355", User);
            n.Apply();
            var et = EntryOf(n, e.Id);
            var rp = AccessoryReport.SlotOf(n.Real.Context, s, false, false);
            Check("XT8 a type change to 355: item = row 0, parent \"\" (the row's), the sets identity; anchor = the row's Parent",
                  rt.Ok && et != null && et.Category == 355 && et.Item == V + "0" && et.Parent == "" && et.Moves.All(m => m.Position.X == 0 && m.Position.Y == 0 && m.Position.Z == 0 && m.Scale.X == 1)
                  && rp.AnchorName == rp.Facts.ParentColumn, rt.Message + " anchor " + rp.AnchorName);
            NodeFresh("XT8 type change", h, n);
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- XT9: copies

    static void XT9(MakerCoreTest.Harness h)
    {
        bool keep = MakerServices.CopyAccessoryPluginData;
        try
        {
            using (var n = NodeOf(h, FemaleCard))
            {
                int s = SlotsOf(n).LowestFree();
                var e = AddAt(n, 352, StarEar, s);
                Edit(n, e.Id, "parent", "N_Earring_L");
                Edit(n, e.Id, "moves[0].rotation", new Vec3(0, 30, 0));
                Edit(n, e.Id, "colors[0].color", new Color4(0.2f, 0.4f, 0.8f, 1f));
                e = EntryOf(n, e.Id);
                int free = SlotsOf(n).LowestFree();
                var rd = n.Ops.CopyAccessory(n.Id, e.Id, null, null, AccessoryCopyParts.Slot, false, MirrorAxis.None, User);
                n.Apply();
                var d = EntryAt(n, free);
                Check("XT9 «複製»: a new entry in the lowest free slot with the source's item, parent, moves and colours; shown in the state",
                      rd.Ok && d != null && d.Id != e.Id && d.Item == e.Item && d.Parent == e.Parent && d.Moves[0].Rotation.Y == 30 && SameColour(d.Colors[0].Color, e.Colors[0].Color)
                      && ((CharaNodeState)TN(n).State).State.Accessories.TryGetValue(d.Id, out bool shown) && shown && NodeObj(n, free), rd.Message);
                // to an occupied slot: overwritten, its id kept
                int occ = SlotsOf(n).LowestFree();
                var o = AddAt(n, 355, V + "0", occ);
                var ro = n.Ops.CopyAccessory(n.Id, e.Id, occ, null, AccessoryCopyParts.Slot, false, MirrorAxis.None, User);
                n.Apply();
                var ot = EntryAt(n, occ);
                Check("XT9 copy to an occupied slot: the target's members overwritten, its id kept", ro.Ok && ot != null && ot.Id == o.Id && ot.Category == 352 && ot.Item == e.Item, ro.Message);
                // set copy with the left-right mirror
                var rm = n.Ops.CopyAccessory(n.Id, e.Id, occ, null, AccessoryCopyParts.Move1, false, MirrorAxis.LeftRight, User);
                n.Apply();
                var om = EntryAt(n, occ);
                var tr = CmpOf(n.Real.Context, occ)?.trfMove01;
                Check("XT9 «調整①を左右反転» to the occupied slot: rotation.y 30 → 210, realized", rm.Ok && om != null && Math.Abs(om.Moves[0].Rotation.Y - 210) < 1e-4 && tr && Quaternion.Angle(tr.localRotation, Quaternion.Euler(0, 210, 0)) < 1e-3, rm.Message);
                // mirrored parent
                int mp = SlotsOf(n).LowestFree();
                var rmp = n.Ops.CopyAccessory(n.Id, e.Id, mp, null, AccessoryCopyParts.Slot, true, MirrorAxis.None, User);
                n.Apply();
                var em = EntryAt(n, mp);
                var rep = AccessoryReport.SlotOf(n.Real.Context, mp, false, false);
                Check("XT9 copy with «左右の親を変更»: N_Earring_L → N_Earring_R, anchored there", rmp.Ok && em != null && em.Parent == "N_Earring_R" && rep.AnchorName == "N_Earring_R", rmp.Message + " " + rep.AnchorName);
                NodeFresh("XT9 copies", h, n);
                // plugin data: the AAC rules of the source follow only with maker.copyAccessoryPluginData
                var rr = n.Ops.EditAppearance(new[] { n.Id }, new List<(string, object)> { (NAP(e.Id, "rules"), new AccessoryRules { Visibility = { new AacRule { Rule = "STARTUP" } } }) }, User);
                n.Apply();
                if (!rr.Ok) Warn("XT9 plugin data", "the rules of an accessory cannot be set through EditAppearance (" + rr.Message + "): the switch is not exercised here (B's scene tests cover it)");
                else
                {
                    MakerServices.CopyAccessoryPluginData = false;
                    n.Ops.CopyAccessory(n.Id, e.Id, occ, null, AccessoryCopyParts.Slot, false, MirrorAxis.None, User);
                    n.Apply();
                    bool off = EntryAt(n, occ)?.Rules == null;
                    MakerServices.CopyAccessoryPluginData = true;
                    n.Ops.CopyAccessory(n.Id, e.Id, occ, null, AccessoryCopyParts.Slot, false, MirrorAxis.None, User);
                    n.Apply();
                    var on = EntryAt(n, occ)?.Rules;
                    Check("XT9 plugin data on a slot copy: switch off → the target's rules cleared; on → the source's", off && on != null && on.Visibility.Count == 1 && on.Visibility[0].Rule == "STARTUP");
                    NodeFresh("XT9 plugin data", h, n);
                }
            }
        }
        finally { MakerServices.CopyAccessoryPluginData = keep; }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- XT11: «髪の毛の色を取得»

    static void XT11(MakerCoreTest.Harness h)
    {
        if (!Installed(h, 351, HairBun, "XT11 351#40")) return;
        using (var n = NodeOf(h, FemaleCard))
        {
            int s = SlotsOf(n).LowestFree();
            var e = AddAt(n, 351, HairBun, s);
            int ok = 0;
            var bad = new List<string>();
            for (int k = 0; k < 4; k++)
            {
                int before = n.Session.Commands.UndoList.Count;
                var r = n.Ops.MatchAccessoryHairColour(n.Id, e.Id, k, User);
                n.Apply();
                var cur = EntryOf(n, e.Id);
                var part = n.Stored.Chara.Hair.Parts[k];
                var want = HairColours(part);
                bool docs = cur != null && Enumerable.Range(0, 4).All(i => SameColour(cur.Colors[i].Color, want[i])) && Bits(cur.Colors[0].Smoothness) == Bits(part.Smoothness) && Bits(cur.Colors[0].Metallic) == Bits(part.Metallic);
                bool mats = HairColoursAsDocument(n.Real.Context, s, cur, out string hd);
                bool entry = n.Session.Commands.UndoList.Count <= before + 1;
                if (r.Ok && docs && mats && entry) ok++; else bad.Add($"part {k}: ok {r.Ok} docs {docs} materials {mats} ({hd}) entries {n.Session.Commands.UndoList.Count - before}");
            }
            Check("XT11 «髪の毛の色を取得» from each of the four hair parts: colours 1-4 and colour 1's smoothness / metallic = the part's, realized, one entry each", ok == 4, string.Join(" | ", bad));
            var rn = n.Ops.MatchAccessoryHairColour(n.Id, EntryAt(n, SlotsOf(n).EntryBySlot.Keys.First(x => x != s))?.Id, 0, User);
            Info("XT11 on a non-hair accessory: " + (rn.Ok ? "ok" : rn.Code + " " + rn.Message));
            NodeFresh("XT11", h, n);
        }
        h.ReleaseBundles();
    }

    // ================================================================ RunRender (graphics)

    public static void RunRender()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        Rep.AppendLine($"maker accessories render (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName})");
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
                    defaults = new PartDefaults(h.Catalog);
                    var live = bb.B;
                    if (live.Ok)
                    {
                        var ctx = live.Context;
                        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HS2Next.App.AppPipeline.MeasureAssetPath);
                        Check("URP asset", asset != null);
                        GraphicsSettings.defaultRenderPipeline = asset; QualitySettings.renderPipeline = asset;
                        int s = Table(ctx).FreeSlot();
                        var e = Put(live, s, 351, HairBun);
                        int s2 = Table(ctx).FreeSlot();
                        var e2 = Put(live, s2, 359, Backpack);
                        int s3 = Table(ctx).FreeSlot();
                        var e3 = Put(live, s3, 353, Glasses);
                        SetA(live, e, "colors[0].smoothness", () => e.Colors[0].Smoothness = 0.8f);
                        Set(live, "chara.hair.shaderType", () => ctx.Chara.Hair.ShaderType = 1);
                        SetA(live, e2, "colors[1].color", () => e2.Colors[1].Color = new Color4(0.2f, 0.8f, 0.4f, 1f));
                        SetA(live, e3, "colors[3]", () => { e3.Colors[3].Color = new Color4(0.5f, 0.5f, 0.9f, 0.4f); e3.Colors[3].Gloss = 0.2f; });
                        var mats = FaceDigest.AccessoryRenderersOf(ctx, s).SelectMany(r => r.sharedMaterials).ToList();
                        int unsupported = mats.Count(m => !m || !m.shader || !m.shader.isSupported || !m.shader.name.StartsWith("HS2Next/", StringComparison.Ordinal));
                        Check("R1 the hair-type accessory's materials after the edits: supported replacement shaders", mats.Count > 0 && unsupported == 0, $"{mats.Count} materials, unsupported / not replaced {unsupported}");
                        // R3 before the camera draws of R2 (they update skinned-mesh bounds, which the snapshot's rend lines hold)
                        FreshOk("R3 the materials of XT1 / XT2 after the edits", h, live);
                        string a = RenderSlot(ctx, s), b = RenderSlot(ctx, s);
                        Check("R2 a 64² RT of the head region with 351#40 rendered twice: equal hashes", a != null && a == b, $"{Short(a, 16)} / {Short(b, 16)}");
                        Set(live, "chara.hair.shaderType", () => ctx.Chara.Hair.ShaderType = 0);
                        string ad = null;
                        VsFresh(h, live, out _, (x, y) => ad = AccDiff(x.Context, y.Context));
                        Check("R3 back to draw type 0 after the draws: the accessory digest = a fresh build's", ad == null, ad ?? "");
                    }
                }
        }
        finally
        {
            FaceSetup.Seed = fseed;
            defaults = null;
            GraphicsSettings.defaultRenderPipeline = keepRp; QualitySettings.renderPipeline = keepQ;
        }
        Finish("maker_accessories_render_report.txt", "MakerAccessoriesTest.render", sw);
    }

    /// <summary>Renders the bounds of a slot object's renderers into a 64² RT (a temporary camera and light) and returns the
    /// SHA-256 of the read-back (numbers only; nothing is saved).</summary>
    static string RenderSlot(CharaContext ctx, int slot)
    {
        var go = ObjOf(ctx, slot);
        if (!go) return null;
        var rends = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
        if (rends.Count == 0) return null;
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.forceMatrixRecalculationPerRender = true;
        var bounds = rends[0].bounds;
        foreach (var r in rends) bounds.Encapsulate(r.bounds);
        var cam = new GameObject("MakerAccessoriesTest cam").AddComponent<Camera>();
        var key = new GameObject("MakerAccessoriesTest key").AddComponent<Light>();
        var rt = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32) { name = "MakerAccessoriesTest slot" };
        try
        {
            key.type = LightType.Directional; key.intensity = 1f; key.transform.rotation = Quaternion.Euler(30, 200, 0);
            HS2Next.RenderingLayers.ApplyToLight(key);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            cam.fieldOfView = 30; cam.nearClipPlane = 0.05f; cam.farClipPlane = 100;
            float d = Mathf.Max(bounds.extents.magnitude, 0.1f) / Mathf.Tan(15 * Mathf.Deg2Rad);
            cam.transform.position = bounds.center + Vector3.forward * d;
            cam.transform.LookAt(bounds.center);
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

    static double FlushMs(MakerCoreTest.Built live, Action set, string path, FlushMode mode = FlushMode.Normal, List<FlushReport> reports = null)
    {
        set();
        live.Context.Editor.MarkDirty(path);
        var t = Stopwatch.StartNew();
        var r = live.Context.Editor.Flush(mode);
        double ms = t.Elapsed.TotalMilliseconds;
        reports?.Add(r);
        return ms;
    }

    public static void RunTiming()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        Rep.AppendLine($"maker accessories timing (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName}); medians, ms");
        int reps = Reps(20);
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        try
        {
            using (var h = MakerCoreTest.Harness.Open())
            using (var bb = new Disposer(BuildOk(h, FemaleCard, "BR-Chan")))
            {
                defaults = new PartDefaults(h.Catalog);
                var live = bb.B;
                if (!live.Ok) return;
                var ctx = live.Context;
                var hair = ctx.Chara.Hair;
                var fenceRt = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32) { name = "MakerAccessoriesTest fence" };
                fenceRt.Create();
                try
                {
                    int s = Table(ctx).FreeSlot();
                    // XU1 item swap: cold (first load of each row) and warm (alternating two loaded rows)
                    var e = Put(live, s, 352, StarEar);
                    var cold = new List<double>();
                    foreach (var r in h.Catalog.EntriesOf(352).Where(x => x.IsVanilla).Skip(1).Take(6))
                    {
                        Fence(fenceRt);
                        var t = Stopwatch.StartNew();
                        SetItem(live, e, 352, r.Key.ToString());
                        Fence(fenceRt);
                        cold.Add(t.Elapsed.TotalMilliseconds);
                    }
                    string a = StarEar, b = NextRow(h.Catalog, 352, StarEar, x => x.IsVanilla);
                    SetItem(live, e, 352, a); SetItem(live, e, 352, b);
                    var warm = new List<double>();
                    for (int i = 0; i < reps; i++)
                    {
                        Fence(fenceRt);
                        var t = Stopwatch.StartNew();
                        SetItem(live, e, 352, i % 2 == 0 ? a : b);
                        Fence(fenceRt);
                        warm.Add(t.Elapsed.TotalMilliseconds);
                    }
                    Target("XU1 accessory item swap, warm (352 rows)", Median(warm), 30);
                    Target("XU1 accessory item swap, cold (first load of a 352 row)", Median(cold), 100);
                    // type change (warm): 352#0 <-> 355#0 (row 0 of each, the moves and parent reset as the op)
                    var types = new List<double>();
                    SetItem(live, e, 355, V + "0"); SetItem(live, e, 352, StarEar);
                    for (int i = 0; i < reps; i++)
                    {
                        bool to355 = i % 2 == 0;
                        Fence(fenceRt);
                        var t = Stopwatch.StartNew();
                        e.Parent = ""; e.Moves[0] = new AccessoryMove(); e.Moves[1] = new AccessoryMove();
                        SetItem(live, e, to355 ? 355 : 352, V + "0");
                        Fence(fenceRt);
                        types.Add(t.Elapsed.TotalMilliseconds);
                    }
                    Target("XU1 type change, warm (352#0 <-> 355#0)", Median(types), 30);
                    // parent change (a reload)
                    SetItem(live, e, 352, StarEar);
                    var parents = new List<double>();
                    for (int i = 0; i < reps; i++)
                    {
                        string key = i % 2 == 0 ? "N_Earring_R" : "N_Earring_L";
                        Fence(fenceRt);
                        parents.Add(FlushMs(live, () => e.Parent = key, AP(e.Id, "parent")));
                        Fence(fenceRt);
                    }
                    Target("XU1 parent change, warm (a part reload; above it the re-anchor branch, XQ7)", Median(parents), 30);
                    // XU2 colour drag frame (a 3-area item, preview flushes) and a transform step
                    int s2 = Table(ctx).FreeSlot();
                    var e2 = Put(live, s2, 359, Backpack);
                    var drag = new List<double>();
                    for (int i = 0; i < reps; i++)
                    {
                        float v = 0.2f + 0.6f * (i % 10) / 10f;
                        drag.Add(FlushMs(live, () => e2.Colors[0].Color = new Color4(v, 0.3f, 0.4f, 1f), AP(e2.Id, "colors[0].color"), FlushMode.Preview));
                    }
                    live.Context.Editor.Flush();
                    Target("XU2 accessory colour drag frame (359#0, preview flush)", Median(drag), 0.3);
                    var step = new List<double>();
                    for (int i = 0; i < reps; i++)
                    {
                        float v = 0.1f * (i % 20);
                        step.Add(FlushMs(live, () => e2.Moves[0].Position = new Vec3(v, 0, 0), AP(e2.Id, "moves[0].position")));
                    }
                    Target("XU2 transform step (moves[0].position, flush)", Median(step), 0.5);
                    // XU3 the draw-type switch with 4 hair-type slots and the 4 hair parts (first without the hair-type slots: the
                    // hair parts and the other accessories alone)
                    var sh0 = new List<double>();
                    for (int i = 0; i < reps; i++) sh0.Add(FlushMs(live, () => hair.ShaderType = i % 2 == 0 ? 1 : 0, "chara.hair.shaderType"));
                    if (hair.ShaderType != 0) FlushMs(live, () => hair.ShaderType = 0, "chara.hair.shaderType");
                    Info($"XU3 reference: the switch without hair-type accessories (hair parts + {AccessoryReport.SlotNumbers(ctx).Count} other accessories), flush CPU {F(Median(sh0))} ms");
                    var hs = new List<int>();
                    for (int k = 0; k < 4; k++) { int f = Table(ctx).FreeSlot(); Put(live, f, 351, k % 2 == 0 ? HairBun : HairBun2); hs.Add(f); }
                    var sh = new List<double>(); var shCpu = new List<double>(); var shReports = new List<FlushReport>();
                    for (int i = 0; i < reps; i++)
                    {
                        int st = i % 2 == 0 ? 1 : 0;
                        Fence(fenceRt);
                        var t = Stopwatch.StartNew();
                        shCpu.Add(FlushMs(live, () => hair.ShaderType = st, "chara.hair.shaderType", FlushMode.Normal, shReports));
                        Fence(fenceRt);
                        sh.Add(t.Elapsed.TotalMilliseconds);
                    }
                    Info($"XU3 with the 4 hair-type accessories: flush CPU {F(Median(shCpu))} ms; units of the last flush: {string.Join(",", shReports.Last().Units)}");
                    if (hair.ShaderType != 0) FlushMs(live, () => hair.ShaderType = 0, "chara.hair.shaderType");
                    Info($"XU3 accessory slots re-shaded per switch: {string.Join(",", shReports.Select(r => r.AccessoryHairShaded.Count).Distinct())} (hair-type slots {string.Join(",", hs)})");
                    Target("XU3 hair.shaderType switch with 4 hair-type accessories and the 4 hair parts (flush + the URP mapping)", Median(sh), 5);
                    // XU5 the import fold per card (the targets + 20 sampled cards imported to documents): FoldAccessories on the worn
                    // coordinate; and the add path's card check (CardStateImport.HiddenAtCardLoad: the plugin data decoded, imported
                    // only when it can hide something)
                    var fold = new List<double>(); var addCheck = new List<double>();
                    int cards = 0, hiddenCards = 0;
                    foreach (var file in MakerCoreTest.SampleCards())
                    {
                        byte[] bytes;
                        try { bytes = File.ReadAllBytes(file); } catch (Exception) { continue; }
                        if (HS2Next.Import.Hs2Importer.Detect(bytes) != HS2Next.Import.Hs2SourceKind.Chara) continue;
                        string sha;
                        using (var shaAlg = System.Security.Cryptography.SHA256.Create()) sha = BitConverter.ToString(shaAlg.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                        var src = new CardAppearanceSource { FullPath = file, Sha256 = sha, ReadBytes = () => bytes, Name = System.IO.Path.GetFileNameWithoutExtension(file) };
                        CharaContainer cc;
                        try { cc = h.Importer.Import(src, new FindingList()); } catch (Exception) { continue; }
                        if (cc?.Coord == null) continue;
                        cards++;
                        var st = new CharaNodeState();
                        foreach (var ce in cc.Coord.Accessories) if (ce?.Id != null) st.State.Accessories[ce.Id] = true;
                        fold.Add(CpuMs(() => HS2Next.Import.CardStateImport.FoldAccessories(cc.Coord, new int[8], null, st), 50));
                        var card = HS2Next.Data.Card.Load(bytes, file);
                        IReadOnlyCollection<string> hid = null;
                        addCheck.Add(CpuMs(() => hid = HS2Next.Import.CardStateImport.HiddenAtCardLoad(card, src), 3));
                        if (hid != null) hiddenCards++;
                    }
                    Info($"XU5 {cards} cards; the add path's card check (decode + pre-check): median {F(Median(addCheck))} ms, max {F(addCheck.Count > 0 ? addCheck.Max() : double.NaN)} ms; cards with a hide at a Studio load {hiddenCards}");
                    Target("XU5 the import fold per card (FoldAccessories on the worn coordinate, median over the cards)", Median(fold), 0.1);
                }
                finally
                {
                    fenceRt.Release(); Object.DestroyImmediate(fenceRt);
                    ReleaseFence();
                }
            }
        }
        finally
        {
            FaceSetup.Seed = fseed;
            defaults = null;
        }
        Finish("maker_accessories_timing_report.txt", "MakerAccessoriesTest.timing (XU1-XU3)", sw);
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
            var names = new HashSet<string>(only.Split(',', '+').Select(x => x.Trim()).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
            cards = MakerCoreTest.AllCards().Where(f => names.Contains(System.IO.Path.GetFileName(f))).ToList();
            what = "the cards of -only (" + names.Count + " named)";
        }
        else if (Flag("-all")) { cards = MakerCoreTest.AllCards(); what = "every card"; }
        else if (Flag("-targets")) { cards = TestTargets.Load().VisualCards.Where(File.Exists).ToList(); what = "the visual targets"; }
        else { cards = MakerCoreTest.SampleCards(); what = "the visual targets + 20 sampled cards"; }
        int limit = int.Parse(Arg("-limit", int.MaxValue.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        if (cards.Count > limit) cards = cards.Take(limit).ToList();
        var cls = new Classes();
        var others = new List<string>();
        int equal = 0, differ = 0, missing = 0, failed = 0, notChara = 0, n = 0;
        try
        {
            using (var h = MakerCoreTest.Harness.Open())
            {
                Rep.AppendLine($"maker accessories S1 explain (Unity {Application.unityVersion}): {cards.Count} files, {what}; baselines {MakerCoreTest.BaselineDir}");
                foreach (var file in cards)
                {
                    n++;
                    if (n % 25 == 0) Debug.Log($"MakerAccessoriesTest S1 explain: {n}/{cards.Count}, {sw.Elapsed.TotalSeconds:F0} s");
                    MakerCoreTest.Built b = null;
                    try
                    {
                        b = h.Build(file);
                        if (b.NotCharacter) { notChara++; continue; }
                        if (!b.Ok) { failed++; Check(System.IO.Path.GetFileName(file) + ": built", false, b.Error); continue; }
                        var basis = MakerCoreTest.LoadBaseline(b.CardSha);
                        if (basis == null) { missing++; Info(System.IO.Path.GetFileName(file) + ": no baseline"); continue; }
                        var s = b.Snapshot();
                        if (s.Sha256 == basis.Sha256) { equal++; continue; }
                        differ++;
                        Classify(b, basis, s, cls, others);
                    }
                    catch (Exception e) { failed++; Check(System.IO.Path.GetFileName(file) + ": no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace)); }
                    finally { b?.Dispose(); }
                    if (n % 10 == 0) h.ReleaseBundles();
                }
            }
        }
        catch (Exception e) { Check("S1 explain: no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace)); }
        Info($"{equal} equal, {differ} differ, {missing} without a baseline, {failed} failed, {notChara} not character cards");
        foreach (var kv in cls.Lines) Info($"class {kv.Key}: {kv.Value} lines on {cls.PerCard[kv.Key].Count} cards");
        foreach (var kc in cls.PerCard) foreach (var oc in kc.Value) Info($"{kc.Key} card: {oc.Key} ({oc.Value} lines)");
        foreach (var o in Flag("-other") ? others : others.Take(40)) Info("other: " + o);
        Check("S1 explain: every differing line is classified (acc.hairType, acc.alpha4, acc.visibility, acc.skinned, part.bodyMaterial, part.bodyCut, part.heels, motion.stages, face.headBundle, part.additionalBones); 0 other", failed == 0 && !cls.Lines.ContainsKey("other"),
              $"other {(cls.Lines.TryGetValue("other", out var on) ? on : 0)} lines");
        Finish("maker_accessories_s1explain_report.txt", "MakerAccessoriesTest.RunS1Explain", sw);
    }

    /// <summary>
    /// The class of one differing S1 line of a built card (null when it is not an accessory change of Phase 4.5):
    /// "acc.hairType" for a material line of a renderer under a hair-type slot object (the hair shader, texture and colours);
    /// "acc.alpha4" for a material line of an alpha renderer of a non-hair slot whose differing tokens are _Glossiness4 and / or
    /// _Metallic4; "acc.visibility" for an object line of a slot object whose only difference is the activity, or the slot's
    /// context line whose only difference is the read show flag; "acc.skinned" (after 1.0.0) for an object, bone or renderer
    /// line inside a slot object with a SkinnedAccessory; "part.bodyMaterial" (after 1.0.0) for a renderer or
    /// material line of a renderer that draws the body's materials; "part.bodyCut" (after 1.0.0) for a renderer
    /// or material line of the body renderer or of a cut body while the character has body cuts; "face.headBundle" (after 1.0.0,
    /// 10) for a face pass line when the passes come from a head's own bundle. Public so that other explains can
    /// recognise these lines.
    /// </summary>
    public static string AccessoryClass(CharaContext ctx, Dictionary<Transform, string> paths, string key, string before, string after)
    {
        if (ctx?.Accessories == null) return null;
        // part.bodyMaterial (after 1.0.0): a renderer or material line of a renderer that draws the body's
        // materials (its slots and their count are the body's), also when the line is new or gone
        if (key.StartsWith("rend|", StringComparison.Ordinal) || key.StartsWith("mat|", StringComparison.Ordinal))
        {
            string rp = key.Substring(key.IndexOf('|') + 1);
            int dup = rp.LastIndexOf('#');
            if (dup > 0 && rp.Substring(dup + 1).All(char.IsDigit)) rp = rp.Substring(0, dup);
            if (key[0] == 'm') { int br = rp.LastIndexOf('['); if (br > 0) rp = rp.Substring(0, br); }
            foreach (var r in ctx.BodyMaterialTargets)
                if (r && paths.TryGetValue(r.transform, out var tp) && tp == rp) return "part.bodyMaterial";
            // part.bodyCut (after 1.0.0): the body renderer (switched off) and the cut bodies (switched on, the
            // body's first material in their first slot) while the character has body cuts
            if (ctx.BodyCuts.Count > 0)
                foreach (var r in ctx.BodyCutRenderers.Append(ctx.BodyRenderer))
                    if (r && paths.TryGetValue(r.transform, out var tp) && tp == rp) return "part.bodyCut";
        }
        // face.headBundle (after 1.0.0): the two face passes take their materials from the head's own bundle
        if (key.StartsWith("creator|face.", StringComparison.Ordinal) && ctx.FaceSkin != null
            && ctx.FaceSkin.Bundle != HS2Next.Content.Maker.HeadBundleRedirect.VanillaBundle)
            return "face.headBundle";
        // part.heels (after 1.0.0): the lifted cf_N_height and the posed foot joints while the worn shoes' heel
        // data is in effect
        if (key.StartsWith("bone|", StringComparison.Ordinal) && ctx.Motion?.Get<HS2Next.Motion.Heels.HeelsLift>()?.State.Active != null)
        {
            string bone = key.Substring(key.LastIndexOf('/') + 1);
            int hash = bone.LastIndexOf('#');
            if (hash > 0 && bone.Substring(hash + 1).All(char.IsDigit)) bone = bone.Substring(0, hash);
            if (bone == "cf_N_height" || bone.StartsWith("cf_J_Foot0", StringComparison.Ordinal) || bone.StartsWith("cf_J_Toes01_", StringComparison.Ordinal))
                return "part.heels";
        }
        // motion.stages (after 1.0.0): the stage list with the two heel stages inserted; a line whose stage on
        // either side is a stage the character still has (only the index moved) - a stage gone or renamed stays "other"
        if (key.StartsWith("motion|stage[", StringComparison.Ordinal) && ctx.Motion?.Get<HS2Next.Motion.Heels.HeelsLift>() != null)
        {
            var types = new HashSet<string>(ctx.Motion.Stages.Select(s => s.GetType().Name));
            string TypeOf(string line) { var p = line.Split('|'); return p.Length > 2 ? p[2].Split(' ')[0] : null; }
            if ((before == null || types.Contains(TypeOf(before))) && (after == null || types.Contains(TypeOf(after)))) return "motion.stages";
        }
        // part.additionalBones (after 1.0.0)
        var addClass = AdditionalBonesClass(ctx, paths, key, before, after);
        if (addClass != null) return addClass;
        // acc.skinned (after 1.0.0): an object, bone or renderer line inside a slot object that carries a SkinnedAccessory
        // (the rig copy's transforms gone, the listed renderers' bones, root bone and bounds), also when the line is gone
        if (key.StartsWith("obj|", StringComparison.Ordinal) || key.StartsWith("bone|", StringComparison.Ordinal) || key.StartsWith("rend|", StringComparison.Ordinal))
        {
            string path = key.Substring(key.IndexOf('|') + 1);
            int hash = path.LastIndexOf('#');
            if (hash > 0 && path.Substring(hash + 1).All(char.IsDigit)) path = path.Substring(0, hash);
            foreach (var a in ctx.Accessories)
                if (a?.Object && a.Object.GetComponent<SkinnedAccessory>() != null && paths.TryGetValue(a.Object.transform, out var sp)
                    && (path == sp || path.StartsWith(sp + "/", StringComparison.Ordinal)))
                    return "acc.skinned";
        }
        if (before == null || after == null) return null;
        var tokens = TokenNames(before, after);
        if (tokens.Count == 0) return null;
        // part.heels: a renderer's world bounds moved with the lifted body
        if (key.StartsWith("rend|", StringComparison.Ordinal) && tokens.All(x => x == "bounds")
            && ctx.Motion?.Get<HS2Next.Motion.Heels.HeelsLift>()?.State.Active != null)
            return "part.heels";
        // part.heels: the dynamic-bone chains are built in the lifted body's world frame, which rounds their
        // lengths, radii and end points differently in the last bits (every differing number within 1e-4 relative)
        if (key.StartsWith("motion|chain[", StringComparison.Ordinal) && ctx.Motion?.Get<HS2Next.Motion.Heels.HeelsLift>()?.State.Active != null
            && ChainRounding(before, after))
            return "part.heels";
        if (key.StartsWith("mat|", StringComparison.Ordinal))
        {
            string rend = key.Substring(4); int br = rend.LastIndexOf('[');
            if (br <= 0) return null;
            rend = rend.Substring(0, br);
            foreach (var a in ctx.Accessories)
            {
                var cmp = a?.Object ? a.Object.GetComponent<CmpAccessory>() : null;
                if (cmp == null) continue;
                if (cmp.typeHair != 0)
                {
                    foreach (var r in Own(cmp.rendNormal, a.Object))
                        if (paths.TryGetValue(r.transform, out var p) && p == rend) return "acc.hairType";
                    continue;
                }
                if (tokens.Any(t => t != "_Glossiness4" && t != "_Metallic4")) continue;
                foreach (var r in Own(cmp.rendAlpha, a.Object))
                    if (paths.TryGetValue(r.transform, out var p) && p == rend) return "acc.alpha4";
            }
            return null;
        }
        if (key.StartsWith("obj|", StringComparison.Ordinal))
        {
            if (tokens.Any(t => t != "active")) return null;
            string path = key.Substring(4);
            foreach (var a in ctx.Accessories)
                if (a?.Object && paths.TryGetValue(a.Object.transform, out var gp) && path == gp) return "acc.visibility";
            return null;
        }
        if (key.StartsWith("ctx|acc[", StringComparison.Ordinal) && tokens.All(t => t == "show")) return "acc.visibility";
        return null;
    }

    /// <summary>
    /// "part.additionalBones" (after 1.0.0) for a line of the bones a part's additional_bones moved under the
    /// skeleton: their object and bone lines (also new ones), a renderer of such a part whose bones and / or bounds differ, an
    /// object whose only difference is its sibling index under a transform that holds moved bones (the moved roots precede
    /// it), a dynamic-bone chain whose root or owner is a moved bone and its particles (also new chains), the body colliders a
    /// stage lists once it has chains on moved bones, the stage line whose differing tokens are the chain and collider counts.
    /// Null otherwise.
    /// </summary>
    static string AdditionalBonesClass(CharaContext ctx, Dictionary<Transform, string> paths, string key, string before, string after)
    {
        var parts = ctx?.Edit?.PartsWithAdditionalBones.ToList();
        if (parts == null || parts.Count == 0) return null;
        var roots = parts.SelectMany(p => ctx.Edit.AdditionalBonesOf(p)).Where(t => t).ToList();
        var rootPaths = roots.Select(t => paths.TryGetValue(t, out var p) ? p : null).Where(p => p != null).ToList();
        var stage = ctx.Motion?.Get<DynamicBoneStage>();
        bool Moved(Transform t) => t && roots.Any(r => t == r || t.IsChildOf(r));
        bool Under(string p, List<string> under) => p != null && under.Any(r => p == r || p.StartsWith(r + "/", StringComparison.Ordinal));
        // a path at the start of s, followed by '/', ' ' or the end
        bool Starts(string s, List<string> under) => s != null && under.Any(r => s.StartsWith(r, StringComparison.Ordinal)
                                                                    && (s.Length == r.Length || s[r.Length] == '/' || s[r.Length] == ' '));
        string KeyPath()
        {
            string p = key.Substring(key.IndexOf('|') + 1);
            int hash = p.LastIndexOf('#');
            return hash > 0 && p.Substring(hash + 1).All(char.IsDigit) ? p.Substring(0, hash) : p;
        }
        if ((key.StartsWith("bone|", StringComparison.Ordinal) || key.StartsWith("obj|", StringComparison.Ordinal)) && Under(KeyPath(), rootPaths))
            return "part.additionalBones";
        if (key.StartsWith("motion|chain[", StringComparison.Ordinal))
        {
            bool OnMoved(string line)
            {
                if (line == null) return false;
                if (key.Contains("].p["))
                {
                    int bar = line.IndexOf('|', "motion|".Length);
                    return bar > 0 && Starts(line.Substring(bar + 1), rootPaths);
                }
                foreach (var field in new[] { " root=", " owner=" })
                {
                    int i = line.IndexOf(field, StringComparison.Ordinal);
                    if (i >= 0 && Starts(line.Substring(i + field.Length), rootPaths)) return true;
                }
                return false;
            }
            if (OnMoved(before) || OnMoved(after)) return "part.additionalBones";
            // the chain as the stage holds it (a tip particle's line names no transform)
            int close = key.IndexOf(']');
            if (stage != null && close > 13 && int.TryParse(key.Substring(13, close - 13), NumberStyles.None, CultureInfo.InvariantCulture, out int ci)
                && ci < stage.Chains.Count && stage.Chains[ci] != null && (Moved(stage.Chains[ci].Root) || Moved(stage.Chains[ci].Owner)))
                return "part.additionalBones";
        }
        // the body colliders, listed once the stage has chains on moved bones (a stage without chains lists none)
        if (key.StartsWith("motion|collider[", StringComparison.Ordinal) && before == null && stage != null
            && stage.Chains.Any(ch => ch != null && (Moved(ch.Root) || Moved(ch.Owner))))
            return "part.additionalBones";
        if (before == null || after == null) return null;
        var tokens = TokenNames(before, after);
        if (tokens.Count == 0) return null;
        if (key.StartsWith("rend|", StringComparison.Ordinal) && tokens.All(t => t == "bones" || t == "bounds"))
        {
            var partPaths = parts.Select(p => paths.TryGetValue(p.transform, out var pp) ? pp : null).Where(p => p != null).ToList();
            if (Under(KeyPath(), partPaths)) return "part.additionalBones";
        }
        if (key.StartsWith("obj|", StringComparison.Ordinal) && tokens.All(t => t == "sib"))
        {
            string path = KeyPath();
            int slash = path.LastIndexOf('/');
            var holders = roots.Select(t => t.parent && paths.TryGetValue(t.parent, out var pp) ? pp : null).Where(p => p != null);
            if (slash > 0 && holders.Contains(path.Substring(0, slash))) return "part.additionalBones";
        }
        if (key == "motion|dynbone" && tokens.All(t => t == "chains" || t == "colliders")) return "part.additionalBones";
        return null;
    }

    // Two snapshot lines whose tokens differ only in numbers (hex float bits or decimals, comma-separated) that agree within
    // 1e-4 relative.
    static bool ChainRounding(string before, string after)
    {
        var a = before.Split(' '); var b = after.Split(' ');
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == b[i]) continue;
            int ea = a[i].IndexOf('='), eb = b[i].IndexOf('=');
            if (ea < 0 || ea != eb || string.CompareOrdinal(a[i], 0, b[i], 0, ea) != 0) return false;
            var va = a[i].Substring(ea + 1).Split(','); var vb = b[i].Substring(eb + 1).Split(',');
            if (va.Length != vb.Length) return false;
            for (int k = 0; k < va.Length; k++)
            {
                if (!Num(va[k], out double x) || !Num(vb[k], out double y)) return false;
                if (Math.Abs(x - y) > 1e-4 * Math.Max(1, Math.Max(Math.Abs(x), Math.Abs(y)))) return false;
            }
        }
        return true;
    }

    static bool Num(string s, out double v)
    {
        v = 0;
        if (s.Length == 8 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint bits))
        {
            v = BitConverter.Int32BitsToSingle(unchecked((int)bits));
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    static void Classify(MakerCoreTest.Built b, CharaSnapshot basis, CharaSnapshot now, Classes cls, List<string> others)
    {
        string card = System.IO.Path.GetFileName(b.File);
        var paths = MakerClothesTest.SnapPaths(b.Root.transform);
        var a = Keyed(basis.Lines); var c = Keyed(now.Lines);
        bool gained = BonemodsGainedOnMoved(b.Context, paths, a, c);
        foreach (var kv in a.Concat(c.Where(x => !a.ContainsKey(x.Key))))
        {
            a.TryGetValue(kv.Key, out var la); c.TryGetValue(kv.Key, out var lc);
            if (la == lc) continue;
            string klass = (gained && (kv.Key.StartsWith("ctx|bonemod[", StringComparison.Ordinal) || kv.Key == "ctx|ext" && AbmxCountsOnly(la, lc)
                                       || kv.Key.StartsWith("obj|", StringComparison.Ordinal) && la != null && lc != null && lc.Replace("BoneModifierApplier,", "") == la)
                            ? "part.additionalBones" : null)
                           ?? AccessoryClass(b.Context, paths, kv.Key, la, lc) ?? "other";
            if (klass == "other") others.Add($"{card} {Short(kv.Key, 160)}: " + (la != null && lc != null ? "tokens [" + string.Join(",", TokenNames(la, lc)) + "] " : "") + $"{Short(la, 160)} <> {Short(lc, 160)}");
            cls.Add(klass, card);
        }
    }

    // part.additionalBones (after 1.0.0): ABMX modifiers whose bone is a moved bone resolve now. The modifier
    // lines are sorted, so the lines after a new one shift: true when the new lines are the old ones plus lines on moved bones.
    static bool BonemodsGainedOnMoved(CharaContext ctx, Dictionary<Transform, string> paths, Dictionary<string, string> before, Dictionary<string, string> after)
    {
        var parts = ctx?.Edit?.PartsWithAdditionalBones.ToList();
        if (parts == null || parts.Count == 0) return false;
        var roots = parts.SelectMany(p => ctx.Edit.AdditionalBonesOf(p)).Where(t => t && paths.ContainsKey(t)).Select(t => paths[t]).ToList();
        List<string> Mods(Dictionary<string, string> d) =>
            d.Where(kv => kv.Key.StartsWith("ctx|bonemod[", StringComparison.Ordinal)).Select(kv => kv.Value.Substring(kv.Value.IndexOf('|', 4) + 1)).ToList();
        var added = Mods(after);
        foreach (var m in Mods(before)) if (!added.Remove(m)) return false;   // a line gone: something else changed
        return added.Count > 0 && added.All(m => roots.Any(r => m.StartsWith(r + "/", StringComparison.Ordinal) || m.StartsWith(r + " ", StringComparison.Ordinal)));
    }

    static readonly System.Text.RegularExpressions.Regex AbmxCounts = new System.Text.RegularExpressions.Regex(@"_abmx_\d+/\d+_\(identity_\d+,_unknown_\d+\)");

    // The extension summary lines differ only in ABMX's resolved / unknown counts.
    static bool AbmxCountsOnly(string a, string b) =>
        a != null && b != null && AbmxCounts.IsMatch(a) && AbmxCounts.Replace(a, "_abmx_") == AbmxCounts.Replace(b, "_abmx_");

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
