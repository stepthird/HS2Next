// UiPanelsTest.play group MH (Phase 4.3 package W): the Hair page of the
// Edit tab, in the app with the UI in Editor play mode (same harness and Ctx as UiPanelsTest, helpers of the MF / MB groups).
//   page: one page — options (three MakerToggleRows, the draw type MakerChoiceRow), the part selector «後ろ髪 · 前髪 · 横髪 ·
//     エクステ» (`hair-part-*`), the selected part's group (Style, Colour, Decoration, Bundles, Mesh in HS2's order); groups
//     built on first selection and kept, only the selected one attached (names such as push-colour:305 unique); first show,
//     first part group and cached part switch (UH5: ≤ 50 / 50 / 5 ms; WARN above, FAIL above 2×);
//   rows against the prefab facts (A's HairInfo) and the document: top / under / gloss per useTop / useUnder and auto,
//     the skin-match button, decoration rows per useAcsColor0N and the acsDefColor count, the bundle list per shown
//     bundles, the mesh list and the colour / layout rows while a pattern is set (HairMeshforAll: every live rendHair);
//   live gates (J13): the auto toggle hides / shows HK2–HK4; an item change (back → «ボリュームポニー» #2) updates the
//     decoration and bundle sections after the realizing flush, undo restores them; mesh «なし» hides HK8 / HS9–HS12;
//   MakerChoiceRow: one entry "Edit hair", a click on the current choice adds none; the 305 chips: 7 cells (ids 0..6), a push =
//     one entry with the six members of the row on the four parts (same setting on);
//   the bundle list: titles «調整NN», shown bundles = facts, values = the effective rates, a component write / a slider drag
//     (one entry), ↺ = the prefab default ("Reset value"), «揺らさない», «全て初期化» (noShake kept), materialization of an
//     empty stored list on the first edit; the mesh layout's ↺ = HS2's constants (1, 1, 0, 0);
//   handles (J4, J5, J14): one marker per shown bundle (ui.state gizmo.handles), a marker pick through ui.input pointer →
//     the sub-selection `<id>/hair/back/<i>` with the Edit tab kept and the bundle row marked, a move drag of the Y arrow and
//     a rotate drag of the X ring through ui.input pointer → one entry "Edit hair" each, rates in 0..1, the other components
//     unchanged; «髪の調整をまとめて行う» on → the dragged component on every bundle; the option off hides the markers;
//   UH5 / UH8 on a 37-bundle mod hair (Roy12.Mila#69): the bundle list build, the markers' hit test per frame, the drag
//     frame (op + flush); UH5: the back style list (≈ 990 rows) scrolled with thumbnails loading (after the census guard
//     records of UH7);
//   male (HERO107): Front with auto off shows under / gloss but no top colour (the prefab has none).
// Characters: the SFW targets of Tools/targets.json; thumbnails are drawn by the grid and counted, never viewed; hair values
// are numbers only. Queued thumbnails are waited for before item swaps (the 4.2 W-1 rule of thumb).
// Run alone: & Tools\unity_batch.ps1 -Method UiPanelsTest.Run -NoQuit -MethodArgs @('-only','MH')
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AIChara;
using HS2Next.Content.Maker;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Bridge;
using HS2Next.UI.Core;
using HS2Next.UI.Gizmos;
using HS2Next.UI.Panels;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiPanelsTest
{
    // The group joins UiPanelsTest.Groups after every domain reload (UiPanelsTest's static constructor runs first).
    [UnityEditor.InitializeOnLoadMethod]
    static void RegisterMakerHairGroup()
    {
        if (!Groups.Any(g => g.id == "MH")) Groups.Add(("MH", MakerHairGroup));
    }

    const string HairRoot = "appearance.chara.hair.";
    const string Mila37 = "Roy12.Mila#69";   // a back hair with 37 shown bundles (census; UH5 / UH8)

    static string MhP(int k) => InspectorSchemas.HairParts[k];
    static HairPart MhPart(Ctx c, string id, int k) => c.App.Session.Model.Find(id)?.Appearance?.Stored?.Chara?.Hair?.Parts[k];
    static HairData MhHair(Ctx c, string id) => c.App.Session.Model.Find(id)?.Appearance?.Stored?.Chara?.Hair;
    static HairPartFacts MhFacts(Ctx c, string id, int k) => HairInfo.TryPart(c.App.Runtime, id, k, out var f) ? f : null;

    /// <summary>Attached, and no element up the tree hidden by an inline display none.</summary>
    static bool MhShown(VisualElement e)
    {
        if (e == null || e.panel == null) return false;
        for (var p = e; p != null; p = p.parent) if (p.style.display == DisplayStyle.None) return false;
        return true;
    }

    static VisualElement MhRow(CharaEditTab tab, string member) => MfQ<VisualElement>(tab, "field-" + HairRoot + member);
    static bool MhRowShown(CharaEditTab tab, string member) => MhShown(MhRow(tab, member));

    static IEnumerator MhSettle(Ctx c, CharaEditTab tab, double sec = 30)
    {
        // the queued thumbnails of the shown lists finish before the next swap
        var ok = new bool[1];
        yield return WaitFor(() => tab == null || tab.Query<PartList>().ToList().All(pl => pl.Thumbnails == null || pl.Thumbnails.Pending == 0), sec, ok);
        yield return PlayModeHarness.WaitFrames(2);
    }

    /// <summary>The rows of part k's group checked against the facts and the document: (what, expected, shown).</summary>
    static List<(string what, bool want, bool got)> MhExpect(Ctx c, CharaEditTab tab, string id, int k)
    {
        var l = new List<(string, bool, bool)>();
        var f = MhFacts(c, id, k);
        var hd = MhHair(c, id);
        var part = MhPart(c, id, k);
        bool prefab = f != null && f.HasPrefab, auto = hd?.Editor.AutoSetting ?? true;
        string p = MhP(k) + ".";
        l.Add(("style list", true, MhRowShown(tab, p + "item")));
        l.Add(("base colour", prefab, MhRowShown(tab, p + "baseColor")));
        l.Add(("top colour", prefab && f.UseTopColor && !auto, MhRowShown(tab, p + "topColor")));
        l.Add(("under colour", prefab && f.UseUnderColor && !auto, MhRowShown(tab, p + "underColor")));
        l.Add(("gloss colour", prefab && !auto, MhRowShown(tab, p + "specular")));
        l.Add(("metallic", prefab, MhRowShown(tab, p + "metallic")));
        l.Add(("smoothness", prefab, MhRowShown(tab, p + "smoothness")));
        l.Add(("colour samples", prefab, MhShown(MfQ<VisualElement>(tab, "push-colour:305"))));
        l.Add(("skin match", prefab && f.UseSameSkinColorButton, MhShown(MfQ<VisualElement>(tab, "hair-skin-" + MhP(k)))));
        for (int n = 1; n <= 3; n++) l.Add(("decoration " + n, prefab && f.AcsShown(n), MhRowShown(tab, p + "accessoryColors[" + (n - 1) + "]")));
        l.Add(("bundle list", prefab && f.AnyBundleShown, MhRowShown(tab, p + "bundles")));
        l.Add(("mesh pattern", prefab && f.UseMesh, MhRowShown(tab, p + "mesh.item")));
        bool set = prefab && f.UseMesh && part != null && part.Mesh.Item != "illusion.hs2#0";
        l.Add(("mesh colour", set, MhRowShown(tab, p + "mesh.color")));
        for (int i = 0; i < 4; i++) l.Add(("mesh layout " + i, set, MhShown(MfQ<VisualElement>(tab, "field-" + HairRoot + p + "mesh.layout#" + i))));
        return l;
    }

    static void MhCheckRows(Ctx c, CharaEditTab tab, string id, int k, string when)
    {
        var l = MhExpect(c, tab, id, k);
        var bad = l.Where(x => x.want != x.got).Select(x => x.what + " " + (x.want ? "missing" : "shown")).ToList();
        c.Check(bad.Count == 0, when + ": " + MhP(k) + " rows follow the prefab facts and the document (" + l.Count(x => x.got) + " shown" + (bad.Count > 0 ? "; " + string.Join(", ", bad) : "") + ")");
    }

    static IEnumerator MhWaitRows(Ctx c, CharaEditTab tab, string id, int k, Func<bool> extra = null, double sec = 20)
    {
        var ok = new bool[1];
        yield return WaitFor(() => MhExpect(c, tab, id, k).All(x => x.want == x.got) && (extra == null || extra()), sec, ok);
        yield return PlayModeHarness.WaitFrames(2);
    }

    static HairBundleList MhBundles(CharaEditTab tab, int k) => MfQ<HairBundleList>(tab, "field-" + HairRoot + MhP(k) + ".bundles");

    static void MhPointer(Ctx c, string phase, Vector2 p) =>
        UiCommands.Send(c.Host, JObject.FromObject(new { type = "pointer", phase, x = p.x, y = p.y, button = 0, mods = new string[0] }), out _);

    static List<Dictionary<string, object>> MhHandles(Ctx c) => c.Ui.Viewport.Gizmo.HairHandleStates();

    /// <summary>Fills the gizmo pass as the view camera's frame does (batch play renders no view frames of its own; only the
    /// pass's draw list is built, nothing is rendered), so HairMarkersDrawn and ShownTool are this frame's.</summary>
    static void MhBuildGizmo(Ctx c)
    {
        var cam = c.Ui.Viewport.ViewCamera;
        if (cam && c.Host.GizmoPass != null) c.Ui.Viewport.Gizmo.Build(c.Host.GizmoPass, cam);
    }

    static Vector2 MhPanel(Dictionary<string, object> d)
    {
        var a = d["panel"] as double[];
        return a == null ? new Vector2(-1e6f, -1e6f) : new Vector2((float)a[0], (float)a[1]);
    }

    static float MhC(Vec3 v, int a) => a == 0 ? v.X : a == 1 ? v.Y : v.Z;

    /// <summary>The view camera on the correction transforms of part k (front of the character).</summary>
    static IEnumerator MhFrame(Ctx c, string id, int k)
    {
        var r = c.App.Runtime.FindChara(id);
        var go = r?.Context?.Hair != null && k < r.Context.Hair.Length ? r.Context.Hair[k] : null;
        var cmp = go ? go.GetComponent<CmpHair>() : null;
        var pts = cmp?.boneInfo?.Where(b => b != null && b.trfCorrect).Select(b => b.trfCorrect.position).ToList() ?? new List<Vector3>();
        var pivot = pts.Count > 0 ? pts.Aggregate(Vector3.zero, (a, b) => a + b) / pts.Count : (r?.Root ? r.Root.transform.position + Vector3.up * 1.5f : Vector3.up * 1.5f);
        var cc = c.Ui.Viewport.Camera;
        float yaw = r?.Root ? r.Root.transform.eulerAngles.y + 180f : 180f;
        cc.SetView(pivot, new Vector3(10, yaw, 0), 2.2f, 23);
        cc.Flush();
        yield return PlayModeHarness.WaitFrames(4);
    }

    static IEnumerator MakerHairGroup(Ctx c)
    {
        var ui = c.Ui;
        PartSelector.Part = 0;
        ui.Options.Set(HairBundleList.HandlesOption, true);
        var tool0 = ui.Tool;
        yield return NewScene(c);
        var ids = new List<string>();
        yield return AddBuiltChara(c, c.FemaleCard, ids);
        yield return AddBuiltChara(c, c.MaleCard, ids);
        if (ids.Count < 2) yield break;
        string f = ids[0], m = ids[1];
        var lists = MakerCatalog.For(c.App.Catalog);
        c.Check(lists != null && MakerServices.Rows != null && MakerServices.Hair != null, "Maker lists, rows and the hair facts service");
        if (lists == null || MakerServices.Rows == null) yield break;
        int u00 = UndoCount(c);
        var fb = MhFacts(c, f, 0);
        var ff = MhFacts(c, f, 1);
        int shownBack = fb?.Bundles.Count(b => b.Shown) ?? -1, shownFront = ff?.Bundles.Count(b => b.Shown) ?? -1;
        c.Check(fb != null && fb.HasPrefab && ff != null && ff.HasPrefab && shownBack == 3 && shownFront == 3,
            "BR-Chan's back / front facts: prefabs " + fb?.PrefabName + " / " + ff?.PrefabName + ", shown bundles " + shownBack + " / " + shownFront + " (census: 3 / 3)");

        // ---------------------------------------------------------------- the page (female)
        var tab = MfOpenEdit(c, f);
        yield return PlayModeHarness.WaitFrames(4);
        c.Check(tab != null && tab.panel != null, "the female's Edit tab is shown");
        if (tab == null) yield break;
        int gc0 = GC.CollectionCount(0);
        var swShow = Stopwatch.StartNew();
        tab.ShowNode("hair");
        double showCall = swShow.Elapsed.TotalMilliseconds;
        int gcs = GC.CollectionCount(0) - gc0;
        yield return PlayModeHarness.WaitFrames(3);
        var page = tab.Pages.TryGetValue("hair", out var pg0) ? pg0 : null;
        c.Check(page != null && tab.CurrentNode == "hair" && tab.CurrentPage == null && MfQ<VisualElement>(tab, "edit-pages-hair") == null,
            "the Hair node is one page (no page strip)");
        double firstShow = tab.PageBuildMs.TryGetValue("hair.hair", out var fs) ? fs : -1;
        double firstGroup = tab.PageBuildMs.TryGetValue("hair.group.back", out var fg) ? fg : -1;
        var parts0 = tab.PageBuildMs.Where(kv => kv.Key.StartsWith("section.hair.hair.", StringComparison.Ordinal)).Select(kv => kv.Key.Substring("section.hair.hair.".Length) + " " + kv.Value.ToString("0.0")).ToList();
        c.Info($"UH5 first show breakdown (ms): {string.Join(", ", parts0)}; ShowNode call {showCall:0.0}; GC collections (gen 0) during it {gcs}");
        TimedShow(c, "UH5: Hair page first show", firstShow, gcs, PageTargetMs, $"back group {firstGroup:0.0} ms");
        var secs = page?.Query<Foldout>().ToList().Select(x => x.name).Where(n => n.StartsWith("edit-section-hair.hair.", StringComparison.Ordinal)).Select(n => n.Substring("edit-section-hair.hair.".Length)).ToList() ?? new List<string>();
        var wantSecs = new List<string> { "options", "part" }.Concat(InspectorSchemas.HairPartSections.Select(s => "back." + s)).Concat(new[] { "boneMods" }).ToList();
        c.Check(secs.SequenceEqual(wantSecs), "sections in order: options, part, back style / colour / decoration / bundles / mesh, the bone adjustments (" + string.Join(", ", secs) + ")");
        var sel = tab.HairParts;
        var partBtns = Enumerable.Range(0, 4).Select(k => MfQ<Button>(tab, "hair-part-" + MhP(k))).ToList();
        c.Check(sel != null && partBtns.All(b => b != null) && sel.Buttons.Index == 0 && PartSelector.Part == 0,
            "the part selector «後ろ髪 · 前髪 · 横髪 · エクステ» (hair-part-back … option) shows Back");
        c.Check(sel != null && !sel.IsNone(0) && !sel.IsNone(1) && sel.IsNone(2) && sel.IsNone(3), "side / extension (none rows) dimmed, back / front not");
        var toggles = new[] { "editor.sameSetting", "editor.autoSetting", "editor.ctrlTogether" }.Select(p => MfQ<MakerToggleRow>(tab, "field-" + HairRoot + p)).ToList();
        var choice = MfQ<MakerChoiceRow>(tab, "field-" + HairRoot + "shaderType");
        c.Check(toggles.All(t => t != null) && choice != null && MfQ<Button>(choice, choice.name + "-0") != null && MfQ<Button>(choice, choice.name + "-1") != null,
            "options: three toggles and the draw type (" + choice?.name + "-0 / -1)");
        var hd0 = MhHair(c, f);
        c.Check(hd0 != null && hd0.Editor.SameSetting && !hd0.Editor.AutoSetting && hd0.ShaderType == 0 && toggles[0].Toggle.value && !toggles[1].Toggle.value && choice?.Index == 0,
            "BR-Chan as stored: same setting on, auto off, draw type 0 (the rows show it)");
        MhCheckRows(c, tab, f, 0, "first show");

        // ---------------------------------------------------------------- part switch, detached groups
        int gcGroup0 = GC.CollectionCount(0);
        sel.Select(1);
        int gcsGroup = GC.CollectionCount(0) - gcGroup0;
        yield return PlayModeHarness.WaitFrames(2);
        double frontGroup = tab.PageBuildMs.TryGetValue("hair.group.front", out var fg1) ? fg1 : -1;
        TimedShow(c, "UH5: first Front group", frontGroup, gcsGroup, PageTargetMs);
        c.Check(tab.HairGroups[0] != null && tab.HairGroups[0].panel == null && tab.HairGroups[1]?.panel != null,
            "the Back group is detached (kept), the Front group attached");
        c.Check(tab.Query<VisualElement>("push-colour:305").ToList().Count == 1 && tab.Query<VisualElement>("hair-bundles-reset-back").ToList().Count == 0,
            "element names stay unique: one push-colour:305 in the tab, no Back element attached");
        MhCheckRows(c, tab, f, 1, "front");
        var switches = new List<(double ms, int gcs)>();
        for (int n = 0; n < 6; n++)
        {
            int gcSwitch0 = GC.CollectionCount(0);
            sel.Select(n % 2 == 0 ? 0 : 1);
            int gcsSwitch = GC.CollectionCount(0) - gcSwitch0;
            yield return PlayModeHarness.WaitFrames(2);
            switches.Add((tab.HairPartSwitchMs, gcsSwitch));
        }
        double cached = TimedMedian(c, "UH5: cached part switch", switches, 5);
        c.Check(sel.Selected == 1 && tab.HairGroups[1].panel != null, "after the cached switches the Front group is shown");
        sel.Select(0);
        yield return PlayModeHarness.WaitFrames(2);
        c.Info($"UH5 (Editor play): Hair page first show {firstShow:0.0} ms (incl. the Back group {firstGroup:0.0}), first Front group {frontGroup:0.0} ms, cached switch {cached:0.000} ms");

        // ---------------------------------------------------------------- live gates: the auto toggle
        int u0 = UndoCount(c);
        int rb0 = tab.LiveRebuilds;
        toggles[1].Write(true);
        yield return MhWaitRows(c, tab, f, 0, () => !MhRowShown(tab, "back.specular"), 5);
        c.Check(UndoCount(c) == u0 + 1 && c.UndoLabel == "Edit hair" && MhHair(c, f).Editor.AutoSetting, "auto on: one entry \"Edit hair\" (" + c.UndoLabel + ")");
        c.Check(!MhRowShown(tab, "back.topColor") && !MhRowShown(tab, "back.underColor") && !MhRowShown(tab, "back.specular") && MhRowShown(tab, "back.baseColor") && tab.LiveRebuilds > rb0,
            "auto on hides «根本» / «毛先» / «ツヤ» (live gate, rebuilds " + (tab.LiveRebuilds - rb0) + ")");
        toggles[1].Write(false);
        yield return MhWaitRows(c, tab, f, 0, () => MhRowShown(tab, "back.specular"), 5);
        c.Check(MhRowShown(tab, "back.topColor") && MhRowShown(tab, "back.underColor") && MhRowShown(tab, "back.specular"), "auto off shows them again");
        MhCheckRows(c, tab, f, 0, "after the auto toggles");

        // ---------------------------------------------------------------- draw type (MakerChoiceRow)
        int u1 = UndoCount(c);
        choice.Write(1);
        yield return PlayModeHarness.WaitFrames(2);
        bool t1 = MhHair(c, f).ShaderType == 1 && UndoCount(c) == u1 + 1 && c.UndoLabel == "Edit hair" && choice.Index == 1;
        choice.Write(1);
        yield return PlayModeHarness.WaitFrames(1);
        bool same = UndoCount(c) == u1 + 1;
        choice.Write(0);
        yield return PlayModeHarness.WaitFrames(2);
        c.Check(t1 && same && MhHair(c, f).ShaderType == 0 && UndoCount(c) == u1 + 2 && choice.Index == 0,
            "draw type «タイプ02» → shaderType 1, one entry \"Edit hair\"; the current choice adds none; back to «タイプ01»");

        // ---------------------------------------------------------------- the 305 chips
        var chips = MfQ<PushGrid>(tab, "push-colour:305");
        c.Check(chips != null && chips.IsColour && chips.ColourCategory == 305, "colour samples (push-colour:305) on the Colour section");
        if (chips != null)
        {
            var refs = chips.Items.Select(i => i.Ref).ToList();
            c.Check(refs.SequenceEqual(Enumerable.Range(0, 7).Select(i => "illusion.hs2#" + i)) && refs.All(r => chips.ChipColour(r) != null),
                "7 chips = ids 0..6 of 305 with swatch colours (" + string.Join(", ", refs) + ")");
            int u2 = UndoCount(c);
            bool have = MakerServices.Rows.HairColourPreset("illusion.hs2#5", out var pv);
            chips.Push("illusion.hs2#5");
            yield return PlayModeHarness.WaitFrames(2);
            var hd = MhHair(c, f);
            bool all4 = have && Enumerable.Range(0, 4).All(k =>
            {
                var hp = hd.Parts[k];
                return Mathf.Abs(hp.BaseColor.R - pv.Base.R) < 1e-6f && Mathf.Abs(hp.BaseColor.G - pv.Base.G) < 1e-6f && Mathf.Abs(hp.BaseColor.B - pv.Base.B) < 1e-6f
                    && Mathf.Abs(hp.Specular.B - pv.Specular.B) < 1e-6f && Mathf.Abs(hp.Metallic - pv.Metallic) < 1e-6f;
            });
            c.Check(all4 && UndoCount(c) == u2 + 1 && c.UndoLabel == "Edit hair", "«金髪» (#5) → base / gloss / metallic of the row on all four parts (same setting), one entry \"Edit hair\" (" + c.UndoLabel + ")");
        }

        // ---------------------------------------------------------------- the bundle list (Back)
        var bl = MhBundles(tab, 0);
        c.Check(bl != null && bl.Part == 0 && bl.Primary == f, "the Back bundle list (field-…back.bundles)");
        if (bl != null && fb != null)
        {
            var shownIdx = fb.Bundles.Where(b => b.Shown).Select(b => b.Index).ToList();
            c.Check(bl.ShownIndices.SequenceEqual(shownIdx), "shown bundles = the facts (" + string.Join(",", bl.ShownIndices) + ")");
            var titles = shownIdx.Select(i => bl.GroupOf(i)?.text).ToList();
            var wantTitles = fb.Bundles.Where(b => b.Shown).Select(b => L10n.Format("inspector.charaEdit.hair.bundleTitle", b.Title.ToString("00"))).ToList();
            c.Check(titles.SequenceEqual(wantTitles) && shownIdx.All(i => MfQ<Foldout>(tab, "hair-bundle-back-" + i) != null),
                "titles «調整NN» (" + string.Join(", ", titles) + ")");
            int i0 = shownIdx.Count > 0 ? shownIdx[0] : 0, i1 = shownIdx.Count > 1 ? shownIdx[1] : i0;
            // values = the effective rates (BR-Chan's back: stored, valid)
            var eff = bl.EffectiveOf(f);
            bool vals = eff != null && shownIdx.All(i =>
            {
                var s = bl.SliderOf(i, false, 1);
                var s2 = bl.SliderOf(i, true, 0);
                return s != null && s2 != null && Math.Abs(s.Control.Value - eff[i].Move.Y * 100) < 1e-3 && Math.Abs(s2.Control.Value - eff[i].Rotate.X * 100) < 1e-3;
            });
            var stored0 = MhPart(c, f, 0).Bundles;
            c.Check(vals && stored0.Count == fb.Bundles.Length, "slider rows show the effective rates (stored list of " + stored0.Count + ")");
            int u3 = UndoCount(c);
            bl.WriteComponent(i0, false, 1, 0.8);
            yield return PlayModeHarness.WaitFrames(2);
            var b0 = MhPart(c, f, 0).Bundles.FirstOrDefault(b => b.Index == i0);
            c.Check(b0 != null && Mathf.Abs(b0.Move.Y - 0.8f) < 1e-6f && UndoCount(c) == u3 + 1 && c.UndoLabel == "Edit hair", "«移動Y» of «調整01» = 80 → stored 0.8, one entry \"Edit hair\"");
            bl.ResetComponent(i0, false, 1);
            yield return PlayModeHarness.WaitFrames(2);
            b0 = MhPart(c, f, 0).Bundles.FirstOrDefault(b => b.Index == i0);
            var def0 = fb.Bundles[i0];
            c.Check(b0 != null && Mathf.Abs(b0.Move.Y - def0.DefaultMove.Y) < 1e-6f && UndoCount(c) == u3 + 2 && c.UndoLabel == "Reset value",
                $"↺ = the prefab default {def0.DefaultMove.Y:0.####} (\"{c.UndoLabel}\")");
            // a slider drag: one transaction (Begin, three previews, End) = one entry
            var s1 = bl.SliderOf(i1, true, 2);
            if (s1 != null)
            {
                s1.Control.Begin();
                s1.Control.Raise(HS2Next.UI.Widgets.EditPhase.Preview, 20);
                yield return PlayModeHarness.WaitFrames(1);
                s1.Control.Raise(HS2Next.UI.Widgets.EditPhase.Preview, 35);
                yield return PlayModeHarness.WaitFrames(1);
                s1.Control.Raise(HS2Next.UI.Widgets.EditPhase.Preview, 40);
                s1.Control.End();
                yield return PlayModeHarness.WaitFrames(2);
                var b1 = MhPart(c, f, 0).Bundles.FirstOrDefault(b => b.Index == i1);
                c.Check(b1 != null && Mathf.Abs(b1.Rotate.Z - 0.4f) < 1e-6f && UndoCount(c) == u3 + 3, "a «回転Z» drag (3 frames) → stored 0.4, one entry");
            }
            bl.WriteNoShake(i0, true);
            yield return PlayModeHarness.WaitFrames(2);
            bool ns = MhPart(c, f, 0).Bundles.First(b => b.Index == i0).NoShake;
            bl.WriteComponent(i1, false, 0, 0.1);
            bl.ResetAllBundles();
            yield return PlayModeHarness.WaitFrames(2);
            var after = MhPart(c, f, 0).Bundles;
            bool defs = after.All(b => b.Index >= fb.Bundles.Length || (FieldValuesSame(b.Move, fb.Bundles[b.Index].DefaultMove) && FieldValuesSame(b.Rotate, fb.Bundles[b.Index].DefaultRotate)));
            c.Check(ns && defs && after.First(b => b.Index == i0).NoShake && c.UndoLabel == "Edit hair",
                "«揺らさない» (one entry), «全て初期化» → every bundle at the defaults, noShake kept");
            yield return MfUndoTo(c, u3);
            c.Check(!MhPart(c, f, 0).Bundles.Any(b => b.NoShake), "undo restores the bundles");
        }

        // ---------------------------------------------------------------- handles (Back)
        yield return MhFrame(c, f, 0);
        var gz = ui.Viewport.Gizmo;
        ui.Tool = GizmoTool.Move;
        yield return PlayModeHarness.WaitFrames(2);
        MhBuildGizmo(c);
        var hs = MhHandles(c);
        var markers = hs.Where(h => (string)h["kind"] == "marker").ToList();
        c.Check(markers.Count == shownBack && gz.HairMarkersDrawn == shownBack, "markers: one per shown Back bundle (" + markers.Count + ", drawn " + gz.HairMarkersDrawn + ")");
        if (markers.Count > 0)
        {
            var mk = markers[0];
            string sub = (string)mk["sub"];
            var vb = ui.Viewport.Element.worldBound;
            var mp = MhPanel(mk);
            c.Info($"marker {sub} at {mp}, viewport {vb} (inside: {vb.Contains(mp)})");
            MhPointer(c, "down", mp);
            yield return PlayModeHarness.WaitFrames(1);
            MhPointer(c, "up", mp);
            yield return PlayModeHarness.WaitFrames(3);
            var insp = Inspector(c);
            int idx = StudioOps.TryParseHairSub(ui.SubSelection, out var sid, out int sp, out int si) ? si : -1;
            c.Check(ui.SubSelection == sub && sid == f && sp == 0, "a marker pick (ui.input pointer) → sub-selection " + ui.SubSelection);
            c.Check(insp?.Chara?.CurrentTab == CharaEditTab.TabId && CharaEditTab.Current == tab && bl?.GroupOf(idx)?.ClassListContains(HairBundleList.SelectedClass) == true,
                "the Edit tab stays (" + insp?.Chara?.CurrentTab + "), the bundle's row is marked");
            MhBuildGizmo(c);
            hs = MhHandles(c);
            var yTip = hs.FirstOrDefault(h => (string)h["kind"] == "axis" && (string)h["axis"] == "y");
            c.Check(yTip != null && (string)yTip["tool"] == "move" && gz.ShownTool == GizmoTool.Move, "the move handle of the bundle (axis tips in gizmo.handles)");
            if (yTip != null && idx >= 0)
            {
                var before = bl.EffectiveOf(f)[idx];
                int u4 = UndoCount(c);
                var gp = MhPanel(yTip);
                var dir = (gp - (Vector2)gz.ToPanel(gz.PivotWorld)).normalized;   // along the projected Y axis
                var dragMs = new List<double>();
                MhPointer(c, "down", gp);
                yield return PlayModeHarness.WaitFrames(1);
                for (int s = 1; s <= 3; s++)
                {
                    var sw = Stopwatch.StartNew();
                    MhPointer(c, "move", gp + dir * (8 * s));
                    dragMs.Add(sw.Elapsed.TotalMilliseconds);
                    yield return PlayModeHarness.WaitFrames(1);
                }
                MhPointer(c, "up", gp + dir * 24);
                yield return PlayModeHarness.WaitFrames(3);
                var a = MhPart(c, f, 0).Bundles.FirstOrDefault(b => b.Index == idx);
                bool in01 = a != null && new[] { a.Move.X, a.Move.Y, a.Move.Z }.All(v => v >= 0 && v <= 1);
                c.Check(a != null && UndoCount(c) == u4 + 1 && c.UndoLabel == "Edit hair" && in01 && Mathf.Abs(a.Move.Y - before.Move.Y) > 1e-4f
                        && Mathf.Abs(a.Move.X - before.Move.X) < 1e-6f && Mathf.Abs(a.Move.Z - before.Move.Z) < 1e-6f,
                    $"a Y-arrow drag (ui.input, 3 moves) → one entry \"{c.UndoLabel}\", move ({a?.Move.X:0.####}, {a?.Move.Y:0.####}, {a?.Move.Z:0.####}) from y {before.Move.Y:0.####}, rates in 0..1, x / z unchanged");
                c.Info($"UH8 (BR-Chan back): handle drag frame (ui.input move incl. op + flush) median {Median(dragMs):0.00} ms, max {dragMs.DefaultIfEmpty(0).Max():0.00}; last HairApply {gz.HairDragMs:0.000} ms");
                // rotate: the X ring
                ui.Tool = GizmoTool.Rotate;
                yield return PlayModeHarness.WaitFrames(2);
                hs = MhHandles(c);
                var xRing = hs.FirstOrDefault(h => (string)h["kind"] == "axis" && (string)h["axis"] == "x");
                var hitX = xRing != null ? gz.HitTest(MhPanel(xRing)) : default;
                c.Check(xRing != null && (string)xRing["tool"] == "rotate" && hitX.Kind == HandleKind.RotateRing && hitX.Axis == 0, "the rotate handle: the X ring point of gizmo.handles hits the X ring (" + hitX + ")");
                if (xRing != null)
                {
                    var rb = bl.EffectiveOf(f)[idx];
                    int u5 = UndoCount(c);
                    var rp = MhPanel(xRing);
                    MhPointer(c, "down", rp);
                    yield return PlayModeHarness.WaitFrames(1);
                    MhPointer(c, "move", rp + new Vector2(10, 10));
                    yield return PlayModeHarness.WaitFrames(1);
                    MhPointer(c, "move", rp + new Vector2(25, 25));
                    yield return PlayModeHarness.WaitFrames(1);
                    MhPointer(c, "up", rp + new Vector2(25, 25));
                    yield return PlayModeHarness.WaitFrames(3);
                    var ra = MhPart(c, f, 0).Bundles.FirstOrDefault(b => b.Index == idx);
                    c.Check(ra != null && UndoCount(c) == u5 + 1 && ra.Rotate.X >= 0 && ra.Rotate.X <= 1 && Mathf.Abs(ra.Rotate.X - rb.Rotate.X) > 1e-5f
                            && Mathf.Abs(ra.Rotate.Y - rb.Rotate.Y) < 1e-6f && Mathf.Abs(ra.Rotate.Z - rb.Rotate.Z) < 1e-6f,
                        $"an X-ring drag → one entry, rotate x {rb.Rotate.X:0.####} → {ra?.Rotate.X:0.####} (0..1), y / z unchanged");
                }
                // «髪の調整をまとめて行う» on: the dragged component on every bundle (J5)
                ui.Tool = GizmoTool.Move;
                toggles[2].Write(true);
                yield return PlayModeHarness.WaitFrames(3);
                hs = MhHandles(c);
                yTip = hs.FirstOrDefault(h => (string)h["kind"] == "axis" && (string)h["axis"] == "y");
                if (yTip != null)
                {
                    var gp2 = MhPanel(yTip);
                    var dir2 = (gp2 - (Vector2)gz.ToPanel(gz.PivotWorld)).normalized;
                    MhPointer(c, "down", gp2);
                    yield return PlayModeHarness.WaitFrames(1);
                    MhPointer(c, "move", gp2 - dir2 * 10);
                    yield return PlayModeHarness.WaitFrames(1);
                    MhPointer(c, "up", gp2 - dir2 * 10);
                    yield return PlayModeHarness.WaitFrames(3);
                    var bs = MhPart(c, f, 0).Bundles;
                    float y = bs.First(b => b.Index == idx).Move.Y;
                    c.Check(bs.Count == fb.Bundles.Length && bs.All(b => Mathf.Abs(b.Move.Y - y) < 1e-6f), "with «髪の調整をまとめて行う» the Y rate goes to every bundle (" + string.Join(", ", bs.Select(b => b.Move.Y.ToString("0.####"))) + ")");
                }
                // the option off hides the markers
                ui.Options.Set(HairBundleList.HandlesOption, false);
                yield return PlayModeHarness.WaitFrames(2);
                MhBuildGizmo(c);
                int off = MhHandles(c).Count, drawnOff = gz.HairMarkersDrawn;
                bool toggleOff = MfQ<Toggle>(tab, "hair-handles")?.value == false;
                ui.Options.Set(HairBundleList.HandlesOption, true);
                yield return PlayModeHarness.WaitFrames(2);
                MhBuildGizmo(c);
                c.Check(off == 0 && drawnOff == 0 && gz.HairMarkersDrawn == shownBack && toggleOff, "maker.hairHandles off → no markers (the footer toggle follows), on → back");
                yield return MfUndoTo(c, u4);
            }
            ui.SubSelection = null;
        }

        // ---------------------------------------------------------------- live gates: an item change (Back → «ボリュームポニー» #2)
        yield return MhSettle(c, tab);
        {
            var style = MfQ<PartList>(tab, "field-" + HairRoot + "back.item");
            string item0 = MhPart(c, f, 0).Item;
            int u6 = UndoCount(c);
            bool picked = style != null && style.Pick("illusion.hs2#2");
            var ok = new bool[1];
            yield return WaitFor(() => MhPart(c, f, 0).Item == "illusion.hs2#2", 30, ok);
            var f2 = (HairPartFacts)null;
            yield return WaitFor(() => (f2 = MhFacts(c, f, 0)) != null && f2.Item == "illusion.hs2#2", 10, ok);
            yield return MhWaitRows(c, tab, f, 0, () => MhBundles(tab, 0)?.ShownIndices.Count == f2?.Bundles.Count(b => b.Shown));
            c.Check(picked && MhPart(c, f, 0).Item == "illusion.hs2#2" && UndoCount(c) == u6 + 1 && c.UndoLabel == "Change item", "«ボリュームポニー» picked: one entry \"Change item\"");
            MhCheckRows(c, tab, f, 0, "after the item change (live gates)");
            var bl2 = MhBundles(tab, 0);
            c.Check(f2 != null && f2.AcsShown(1) && MhRowShown(tab, "back.accessoryColors[0]") && MhPart(c, f, 0).Bundles.Count == 0 && bl2 != null && bl2.ShownIndices.Count == f2.Bundles.Count(b => b.Shown),
                "decoration «カラー01» shown, the bundle list rebuilt for the new prefab (" + bl2?.ShownIndices.Count + " bundles), stored bundles []");
            if (bl2 != null && bl2.ShownIndices.Count > 0)
            {
                int j = bl2.ShownIndices[0];
                bl2.WriteComponent(j, true, 1, 0.25);
                yield return PlayModeHarness.WaitFrames(2);
                var st = MhPart(c, f, 0).Bundles;
                c.Check(st.Count == f2.Bundles.Length && Mathf.Abs(st.First(b => b.Index == j).Rotate.Y - 0.25f) < 1e-6f, "the first bundle edit materializes the list (" + st.Count + " entries)");
            }
            yield return MfUndoTo(c, u6);
            yield return WaitFor(() => MhPart(c, f, 0).Item == item0, 10, ok);
            yield return MhWaitRows(c, tab, f, 0, () => MhBundles(tab, 0)?.ShownIndices.Count == shownBack);
            MhCheckRows(c, tab, f, 0, "after the undo of the item change");
            c.Check(MhBundles(tab, 0)?.ShownIndices.Count == shownBack, "undo: the Back bundle list has " + shownBack + " bundles again");
        }

        // ---------------------------------------------------------------- mesh (Front)
        sel.Select(1);
        yield return PlayModeHarness.WaitFrames(3);
        yield return MhSettle(c, tab);
        {
            var meshList = MfQ<PartList>(tab, "field-" + HairRoot + "front.mesh.item");
            c.Check(meshList != null && meshList.Category == 306 && meshList.AllItems.Count == 12 && meshList.CurrentRef == "illusion.hs2#10",
                "Front › Mesh: the 306 list (" + meshList?.AllItems.Count + " rows), current «タイプ10» (" + meshList?.CurrentRef + ")");
            var w = MfQ<MakerSliderRow>(tab, "field-" + HairRoot + "front.mesh.layout#0");
            var px = MfQ<MakerSliderRow>(tab, "field-" + HairRoot + "front.mesh.layout#2");
            int u7 = UndoCount(c);
            if (w != null && px != null)
            {
                w.Write(new[] { f }, 0.3, null);
                px.Write(new[] { f }, 0.6, null);
                yield return PlayModeHarness.WaitFrames(2);
                bool set = Mathf.Abs(MhPart(c, f, 1).Mesh.Layout.X - 0.3f) < 1e-6f;
                w.Reset();
                px.Reset();
                yield return PlayModeHarness.WaitFrames(2);
                var lay = MhPart(c, f, 1).Mesh.Layout;
                c.Check(set && lay.X == 1f && lay.Z == 0f && c.UndoLabel == "Reset value", $"«幅» / «位置X» ↺ = HS2's constants 1 / 0 (layout ({lay.X}, {lay.Y}, {lay.Z}, {lay.W}), \"{c.UndoLabel}\")");
            }
            else c.Check(false, "Front › Mesh layout rows");
            bool pickedNone = meshList != null && meshList.Pick("illusion.hs2#0");
            var ok = new bool[1];
            yield return WaitFor(() => MhPart(c, f, 1).Mesh.Item == "illusion.hs2#0", 10, ok);
            yield return MhWaitRows(c, tab, f, 1, () => !MhRowShown(tab, "front.mesh.color"), 5);
            c.Check(pickedNone && !MhRowShown(tab, "front.mesh.color") && !MhShown(MfQ<VisualElement>(tab, "field-" + HairRoot + "front.mesh.layout#0")) && MhRowShown(tab, "front.mesh.item"),
                "«なし» hides the mesh colour and layout rows (the list stays)");
            yield return MfUndoTo(c, u7);
            yield return MhWaitRows(c, tab, f, 1, () => MhRowShown(tab, "front.mesh.color"), 5);
            c.Check(MhRowShown(tab, "front.mesh.color") && MhPart(c, f, 1).Mesh.Item == "illusion.hs2#10", "undo shows them again (pattern 10)");
        }
        sel.Select(0);
        yield return PlayModeHarness.WaitFrames(3);

        // ---------------------------------------------------------------- UH5 / UH8: a 37-bundle back hair
        yield return MhSettle(c, tab);
        if (lists.Rows(300, 1).Any(r => r.Key.ToString() == Mila37))
        {
            var style = MfQ<PartList>(tab, "field-" + HairRoot + "back.item");
            int u8 = UndoCount(c);
            style?.Pick(Mila37);
            var ok = new bool[1];
            yield return WaitFor(() => MhPart(c, f, 0).Item == Mila37, 60, ok);
            yield return WaitFor(() => MhBundles(tab, 0)?.ShownIndices.Count == 37, 20, ok);
            var bl37 = MhBundles(tab, 0);
            double build = bl37?.LastBuildMs ?? -1;
            c.Check(ok[0] && bl37 != null && build >= 0 && build <= 2 * PageTargetMs, $"UH5: the bundle list of {Mila37} (37 bundles, 4 open) built in {build:0.0} ms (≤ {2 * PageTargetMs})");
            if (build > PageTargetMs) c.Warning($"UH5: 37-bundle list {build:0.0} ms above the {PageTargetMs} ms target");
            if (bl37 != null)
            {
                var sw = Stopwatch.StartNew();
                foreach (int i in bl37.ShownIndices) bl37.SliderOf(i, false, 0);   // every group built (opened lazily otherwise)
                c.Info($"UH5: building the rows of the 33 collapsed groups {sw.Elapsed.TotalMilliseconds:0.0} ms");
            }
            yield return MhFrame(c, f, 0);
            ui.Tool = GizmoTool.Move;
            yield return PlayModeHarness.WaitFrames(3);
            MhBuildGizmo(c);
            c.Check(gz.HairMarkersDrawn == 37, "37 markers drawn (" + gz.HairMarkersDrawn + ")");
            var vb = ui.Viewport.Element.worldBound;
            var hit = new List<double>();
            var hitHair = new List<double>();
            for (int i = 0; i < 400; i++)
            {
                var p = new Vector2(vb.x + vb.width * ((i * 37) % 100) / 100f, vb.y + vb.height * ((i * 53) % 100) / 100f);
                var sw = Stopwatch.StartNew();
                gz.HitTest(p);
                hit.Add(sw.Elapsed.TotalMilliseconds);
                hitHair.Add(gz.HairHitMs);
            }
            double hitMed = Median(hit), hairMed = Median(hitHair);
            c.Check(hairMed <= 1.0, $"UH8: the markers' hit test (37) median {hairMed:0.000} ms (≤ 1.0; HitTest with the handles {hitMed:0.000} ms)");
            if (hairMed > 0.5) c.Warning($"UH8: marker hit test {hairMed:0.000} ms above the 0.5 ms target");
            // a drag on one of the 37 bundles (the API, as the pointer path does): frame = op + flush
            var mk = MhHandles(c).FirstOrDefault(h => (string)h["kind"] == "marker");
            var dms = new List<double>();
            if (mk != null)
            {
                ui.SubSelection = (string)mk["sub"];
                yield return PlayModeHarness.WaitFrames(2);
                var tip = MhHandles(c).FirstOrDefault(h => (string)h["kind"] == "axis" && (string)h["axis"] == "y");
                if (tip != null)
                {
                    int u9 = UndoCount(c);
                    var tp = MhPanel(tip);
                    bool began = gz.TryBegin(tp, EventModifiers.None);
                    for (int s = 1; s <= 8; s++)
                    {
                        var sw = Stopwatch.StartNew();
                        gz.Drag(tp + new Vector2(0, -4 * s), EventModifiers.None);
                        dms.Add(sw.Elapsed.TotalMilliseconds);
                        yield return PlayModeHarness.WaitFrames(1);
                    }
                    gz.End();
                    yield return PlayModeHarness.WaitFrames(2);
                    c.Check(began && UndoCount(c) == u9 + 1, "a drag on a 37-bundle hair: one entry");
                }
                ui.SubSelection = null;
            }
            c.Info($"UH8 (37 bundles, Editor play): markers hit test median {hairMed:0.000} ms (p95 {Pct(hitHair, 0.95):0.000}), HitTest {hitMed:0.000} ms; drag step (op + flush) median {Median(dms):0.00} ms, max {dms.DefaultIfEmpty(0).Max():0.00} ({dms.Count} steps)");
            yield return MhSettle(c, tab);
            yield return MfUndoTo(c, u8);
            yield return WaitFor(() => MhBundles(tab, 0)?.ShownIndices.Count == shownBack, 20, ok);
        }
        else c.Warning("UH5 / UH8: " + Mila37 + " is not listed here (no 37-bundle measurement)");

        // ---------------------------------------------------------------- UH5: the Back style list scrolled with thumbnails
        {
            var style = MfQ<PartList>(tab, "field-" + HairRoot + "back.item");
            if (style != null)
            {
                HS2Next.UI.Panels.Maker.PartThumbnails.LogLoads = true;   // names each asynchronous thumbnail load in the log (a load that removes the GPU device)
                style.SetExpanded(true);
                yield return PlayModeHarness.WaitFrames(3);
                yield return MhScroll(c, style, "Back style list (" + style.Items.Count + " rows), pass 1");
                yield return MhScroll(c, style, "Back style list, pass 2 (warm)");
                var pt = style.Thumbnails;
                var done = new bool[1];
                yield return WaitFor(() => pt == null || pt.Pending == 0, 60, done);
                c.Check(done[0], "the style list's queued thumbnails finished (" + pt?.Pending + " pending)");
                style.SetExpanded(false);
                style.ScrollToCurrent();
                HS2Next.UI.Panels.Maker.PartThumbnails.LogLoads = false;
                if (pt != null) c.Info($"UH5 thumbnails: from bundles {pt.FromBundles}, from the disk cache {pt.FromDiskCount}, failed {pt.FailedCount}, guard-skipped {pt.GuardSkipped}, mip<1 rejected {pt.BadMipRejected}, pending {pt.Pending}");
            }
        }

        // ---------------------------------------------------------------- male (HERO107): Front without a top colour
        yield return MhSettle(c, tab);
        {
            var tm = MfOpenEdit(c, m);
            yield return PlayModeHarness.WaitFrames(4);
            if (tm != null)
            {
                tm.Reveal("hair", null, null, 1);
                yield return PlayModeHarness.WaitFrames(3);
                var mf = MhFacts(c, m, 1);
                MhCheckRows(c, tm, m, 1, "male front, auto " + MhHair(c, m)?.Editor.AutoSetting);
                int um = UndoCount(c);
                var autoM = MfQ<MakerToggleRow>(tm, "field-" + HairRoot + "editor.autoSetting");
                autoM?.Write(false);
                yield return MhWaitRows(c, tm, m, 1, () => MhRowShown(tm, "front.specular"), 5);
                c.Check(mf != null && !mf.UseTopColor && !MhRowShown(tm, "front.topColor") && MhRowShown(tm, "front.underColor") && MhRowShown(tm, "front.specular"),
                    "HERO107 front with auto off: «毛先» and «ツヤ» shown, «根本» hidden (no top colour on the prefab)");
                yield return MfUndoTo(c, um);
            }
            else c.Check(false, "the male's Edit tab");
        }

        yield return MfUndoTo(c, u00);
        ui.Tool = tool0;
        PartSelector.Part = 0;
        ui.Options.Set(HairBundleList.HandlesOption, true);
    }

    static bool FieldValuesSame(Vec3 a, Vec3 b) => Mathf.Abs(a.X - b.X) < 1e-6f && Mathf.Abs(a.Y - b.Y) < 1e-6f && Mathf.Abs(a.Z - b.Z) < 1e-6f;

    static IEnumerator MhScroll(Ctx c, PartList pl, string what)
    {
        var grid = pl.Grid;
        int rows = Math.Max(1, (pl.Items.Count + Math.Max(1, grid.Columns) - 1) / Math.Max(1, grid.Columns));
        int binds0 = grid.BindMs.Count;
        var frames = new List<double>();
        int step = Math.Max(1, MakerThumbList.ExpandedRows);
        for (int r = 0; r < rows; r += step)
        {
            grid.List.ScrollToItem(Math.Min(r, rows - 1));
            yield return null;
            frames.Add(Time.unscaledDeltaTime * 1000.0);
            if (frames.Count > 600) break;
        }
        for (int i = 0; i < 30; i++) { yield return null; frames.Add(Time.unscaledDeltaTime * 1000.0); }
        var binds = grid.BindMs.Skip(binds0).ToList();
        double perCell = binds.Count > 0 ? binds.Average() / Math.Max(1, grid.Columns) : double.NaN;
        c.Info($"UH5 {what}: {frames.Count} frames, frame median {Median(frames):0.0} ms, p95 {Pct(frames, 0.95):0.0}, max {frames.DefaultIfEmpty(0).Max():0.0}; row binds {binds.Count}, median {Median(binds):0.000} ms per grid row ({grid.Columns} cells, {perCell:0.000} ms per cell)");
        if (Pct(frames, 0.95) > 20) c.Warning($"UH5 {what}: p95 frame {Pct(frames, 0.95):0.0} ms above 20 ms (Editor batch play frame times include the harness)");
        c.Check(binds.Count > 0, "UH5 " + what + ": the grid bound rows while scrolling");
    }
}
