// UiPanelsTest.play group MA (Phase 4.5 package W): the Accessories page of the Edit tab, in the app with the UI in Editor
// play mode (same harness and Ctx as UiPanelsTest, helpers of the MF / MH / MC groups).
//   page: one page — the slot list (the worn slots by build slot «NN», the «＋» row), the selected slot's group (Type · Colour
//     or Hair colour · Parent · Adjust in HS2's order), the Copy section; first show, first slot group, cached slot switch
//     and the slot list rebuild with 20 worn slots (XU4: ≤ 50 / 50 / 5 / 5 ms; WARN above, FAIL above 2×; the first show and the
//     cached switch through TimedShow / TimedMedian: a window with a garbage collection gives a WARN with its number);
//   «＋»: the free slots (lowest chosen), a type, the type's list; a pick = one entry "Add accessory" in the chosen slot (a gap
//     below it shown with its number), a slot ≥ 20 labelled «21» with the MoreAccessories badge per the switch;
//   rows against the accessory facts (MakerServices.Accessories) — colour areas 1–3, the alpha colour, the hair colour section
//     of a hair-type item, the sets; live: an item change switches normal ↔ hair colour after the realizing flush, a type
//     change rebuilds the group (row 0 of the type, sets identity, one entry "Change item");
//   writes: «ツヤ» typed (one entry "Edit accessory"), ↺ «質感» = the prefab default, «初期カラー» (one entry, the defaults),
//     «髪の毛の色を取得» (one entry, the hair part's colours), the parent picker (54 keys in 10 groups; a pick = one entry; «初期親»),
//     the transform rows (typed values on HS2's grids and limits incl. 359.6 → 0, steps with the set's rate and wrap, one entry
//     per press-and-hold, the x buttons and label drags signed like y / z, axis-label drags at HS2's rate (screen px × step),
//     ↺ per axis (position x's clicked through ui.input), «初期化»; the axes never overlap, at the inspector's width and in a
//     narrow row they wrap);
//   the copy section (disabled rules, «枠へコピー…» with «左右の親を変更», the overwrite prompt, «調整①を左右反転», «複製» to the
//     lowest free slot), «なし» (prompt, one entry "Remove accessory", the state key pruned), multi-selection disables the page,
//     at most 8 slot groups kept, edit.show {slot | section}, the State tab labels by build slot;
//   handles (XU7): the markers of the selected slot's sets (option maker.accessoryHandles), a marker pick (ui.input pointer),
//     a Y-arrow drag (one entry, position on the 0.1 grid), an X-ring drag (rotation x an integer in [0, 360)), option off;
//   XU6: the thumbnails of the lists shown (after step 0's guard records): guard-skipped and mip < 1 rejected counts;
//   male (HERO107): its slots, the head-weighted row 351#202 shows the note and stores a picked key;
//   FU2 (6.3a): an add, a removal, a copy into a new slot and their undo keep the inspector (InspectorPanel.Builds), both pickers'
//     choices and the Edit tab; the State tab's accessory-<id> rows match the document, shown or not; what a rebuild used to do for
//     them: the group of an accessory that left is dropped (another one takes its section ids), the removal's next slot, the focus of
//     a closed add panel, the sections marked after a list change, the clothes and FK rows after a coordinate load.
// Characters: the SFW targets of Tools/targets.json; items: vanilla rows named (353#0, 351#40,
// 351#0, 352#0, 351#202) by data name only; thumbnails are drawn by the grids and counted, never viewed; values are numbers only.
// Run alone: & Tools\unity_batch.ps1 -Method UiPanelsTest.Run -NoQuit -MethodArgs @('-only','MA')
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
using HS2Next.UI.Binding;
using HS2Next.UI.Bridge;
using HS2Next.UI.Core;
using HS2Next.UI.Gizmos;
using HS2Next.UI.Panels;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiPanelsTest
{
    [UnityEditor.InitializeOnLoadMethod]
    static void RegisterMakerAccessoriesGroup()
    {
        if (!Groups.Any(g => g.id == "MA")) Groups.Add(("MA", MakerAccessoriesGroup));
    }

    const double AccSwitchTargetMs = 5, AccListTargetMs = 5;
    const string AccHandlesOption = "maker.accessoryHandles";

    static TreeNode MaNode(Ctx c, string id) => c.App.Session.Model.Find(id);
    static CoordDocument MaCoord(Ctx c, string id) => MaNode(c, id)?.Appearance?.Stored?.Coord;
    static AccessoryEntry MaEntry(Ctx c, string id, string acc) => MaCoord(c, id)?.FindAccessory(acc);
    static AccessorySlotRule MaTable(Ctx c, string id) => StudioOps.AccessorySlotsOf(MaNode(c, id));
    static AccessoryFacts MaFacts(string id, int slot) => MakerServices.Accessories != null && MakerServices.Accessories.TrySlot(id, slot, out var f) ? f : null;
    static string MaPath(string acc, string member) => InspectorSchemas.AccessoryEditPath(acc, member);
    static VisualElement MaRow(CharaEditTab tab, string acc, string member, bool hair = false) => MfQ<VisualElement>(tab, "field-" + MaPath(acc, member) + (hair ? "@hair" : ""));
    static bool MaShown(CharaEditTab tab, string acc, string member, bool hair = false) => MhShown(MaRow(tab, acc, member, hair));
    static string MaNN(int slot) => InspectorSchemas.AccessorySlotLabel(slot);
    static bool MaSection(CharaEditTab tab, int slot, string s) => MhShown(MfQ<VisualElement>(tab, "edit-section-" + InspectorSchemas.AccessorySectionId(slot, s)));

    /// <summary>The rows of the shown slot group checked against the facts: (what, expected, shown).</summary>
    static List<(string what, bool want, bool got)> MaExpect(Ctx c, CharaEditTab tab, string id, string acc, int slot)
    {
        var l = new List<(string, bool, bool)>();
        var f = MaFacts(id, slot);
        bool svc = MakerServices.Accessories != null && f != null;
        bool hair = svc && f.TypeHair;
        l.Add(("type", true, MaShown(tab, acc, "category")));
        l.Add(("item list", true, MaShown(tab, acc, "item")));
        for (int n = 1; n <= 3; n++)
        {
            bool area = svc ? f.AreaShown(n) : true;
            l.Add(("colour " + n, area, MaShown(tab, acc, "colors[" + (n - 1) + "].color")));
            l.Add(("gloss " + n, area, MaShown(tab, acc, "colors[" + (n - 1) + "].gloss")));
        }
        bool alpha = svc ? f.AlphaShown : true;
        l.Add(("colour 4 (alpha)", alpha, MaShown(tab, acc, "colors[3].color")));
        l.Add(("«初期カラー»", svc ? f.ColourShown : true, MhShown(MfQ<VisualElement>(tab, "acc-default-colours-" + MaNN(slot)))));
        l.Add(("hair base colour", hair, MaShown(tab, acc, "colors[0].color", true)));
        l.Add(("hair root colour", hair && f.HairAreaShown(2), MaShown(tab, acc, "colors[1].color", true)));
        l.Add(("hair smoothness", hair, MaShown(tab, acc, "colors[0].smoothness", true)));
        l.Add(("hair fetch «前髪»", hair, MhShown(MfQ<VisualElement>(tab, "acc-hair-fetch-" + MaNN(slot) + "-front"))));
        l.Add(("parent picker", !svc || !f.Fixed, MaShown(tab, acc, "parent")));
        l.Add(("«揺らさない»", true, MaShown(tab, acc, "noShake")));
        l.Add(("set 1 position", !svc || f.Move1, MaShown(tab, acc, "moves[0].position")));
        l.Add(("set 2 position", svc && f.Move2, MaShown(tab, acc, "moves[1].position")));
        return l;
    }

    static void MaCheckRows(Ctx c, CharaEditTab tab, string id, string acc, int slot, string when)
    {
        var l = MaExpect(c, tab, id, acc, slot);
        var bad = l.Where(x => x.want != x.got).Select(x => x.what + " " + (x.want ? "missing" : "shown")).ToList();
        c.Check(bad.Count == 0, when + ": slot " + MaNN(slot) + " rows follow the accessory facts (" + l.Count(x => x.got) + " shown" + (bad.Count > 0 ? "; " + string.Join(", ", bad) : "") + ")");
    }

    static IEnumerator MaWaitRows(Ctx c, CharaEditTab tab, string id, string acc, int slot, double sec = 20)
    {
        var ok = new bool[1];
        yield return WaitFor(() => tab.AccessoryShownId == acc && MaExpect(c, tab, id, acc, slot).All(x => x.want == x.got), sec, ok);
        yield return PlayModeHarness.WaitFrames(2);
    }

    /// <summary>The Edit tab shown for character <paramref name="id"/> on the Accessories node (a finding or a build can still
    /// rebuild the inspector during an operation, so the tab is taken again after each of them).</summary>
    static CharaEditTab MaTab(Ctx c, string id, CharaEditTab fallback)
    {
        var t = CharaEditTab.Current;
        if (t == null || t.PrimaryId != id || t.panel == null) t = MfOpenEdit(c, id) ?? fallback;
        if (t != null && t.CurrentNode != "accessories") t.ShowNode("accessories");
        return t;
    }

    /// <summary>«＋» into <paramref name="slot"/>: type <paramref name="category"/>, pick <paramref name="reference"/>; the new
    /// accessory id (null on failure) once its slot is realized.</summary>
    static IEnumerator MaAdd(Ctx c, CharaEditTab tab, string id, int slot, int category, string reference, string[] newId)
    {
        newId[0] = null;
        tab.OpenAccessoryAdd(true);
        yield return PlayModeHarness.WaitFrames(2);
        var ps = tab.AccessoryAddSlot;
        bool chosen = ps != null && ps.Choose(slot);
        var list = tab.ShowAccessoryAddList(category);
        yield return PlayModeHarness.WaitFrames(3);
        var before = new HashSet<string>(MaCoord(c, id).Accessories.Select(a => a.Id));
        bool picked = list != null && list.Pick(reference);
        var ok = new bool[1];
        yield return WaitFor(() => MaCoord(c, id).Accessories.Any(a => !before.Contains(a.Id)), 60, ok);
        var e = MaCoord(c, id).Accessories.FirstOrDefault(a => !before.Contains(a.Id));
        c.Check(chosen && picked && e != null && MaTable(c, id).SlotOfId(e.Id) == slot && e.Item == reference && e.Category == category,
            "«＋» slot " + MaNN(slot) + ", type " + category + ", pick " + reference + " → in slot " + (e != null ? MaNN(MaTable(c, id).SlotOfId(e.Id)) : "-"));
        if (e == null) yield break;
        yield return WaitFor(() => MaFacts(id, slot) != null && MaTab(c, id, tab)?.AccessoryShownId == e.Id, 30, ok);
        c.Check(ok[0], "the new accessory " + MaNN(slot) + " is realized and selected on the Edit tab shown (" + MaTab(c, id, tab)?.AccessoryShownId + ")");
        yield return MhSettle(c, MaTab(c, id, tab));
        newId[0] = e.Id;
    }

    /// <summary>The overlaps of the transform rows' axes (T's UI8 finding): a part outside its axis, or two axes of one
    /// line overlapping; empty = the layout holds.</summary>
    static List<string> MaAxisOverlaps(IEnumerable<MakerVectorRow> rows)
    {
        var bad = new List<string>();
        foreach (var r in rows)
        {
            if (r == null) continue;
            var axes = Enumerable.Range(0, 3).Select(i => MfQ<VisualElement>(r, "field-" + r.Path + "#" + i)).ToList();
            if (axes.Any(a => a == null)) { bad.Add(r.Path + ": an axis is missing"); continue; }
            for (int i = 0; i < 3; i++)
            {
                var ab = axes[i].worldBound;
                foreach (var part in axes[i].Children())
                {
                    var pb = part.worldBound;
                    if (pb.xMin < ab.xMin - 0.5f || pb.xMax > ab.xMax + 0.5f) bad.Add(r.Path + "#" + i + " " + part.name + " " + pb.xMin.ToString("0.0") + ".." + pb.xMax.ToString("0.0") + " outside " + ab.xMin.ToString("0.0") + ".." + ab.xMax.ToString("0.0"));
                }
                for (int j = i + 1; j < 3; j++)
                {
                    var bb = axes[j].worldBound;
                    if (Mathf.Abs(ab.y - bb.y) < 1f && ab.xMax > bb.xMin + 0.5f && bb.xMax > ab.xMin + 0.5f) bad.Add(r.Path + " axes " + i + " and " + j + " overlap");
                }
            }
        }
        return bad;
    }

    /// <summary>A ui.input pointer click at the centre of <paramref name="e"/> after scrolling it into view and waiting for toasts
    /// over it; the element the panel picks there (null = none).</summary>
    static IEnumerator MaInputClick(Ctx c, VisualElement e, VisualElement[] picked)
    {
        picked[0] = null;
        if (e == null || e.panel == null) yield break;
        e.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(e);
        yield return PlayModeHarness.WaitFrames(3);
        var p = e.worldBound.center;
        for (int i = 0; i < 300; i++)
        {
            picked[0] = e.panel.Pick(p);
            if (picked[0] == e || (picked[0] != null && picked[0].FindCommonAncestor(e) == e)) break;
            yield return null;   // a toast over the target goes away within its seconds
        }
        UiCommands.Send(c.Host, JObject.FromObject(new { type = "pointer", phase = "click", x = p.x, y = p.y, button = 0 }), out _);
        yield return PlayModeHarness.WaitFrames(4);
    }

    /// <summary>Presses a button as the keyboard's submit does (the button's click action runs).</summary>
    static void MaPress(Button b)
    {
        if (b == null) return;
        using (var e = NavigationSubmitEvent.GetPooled()) { e.target = b; b.SendEvent(e); }
    }

    static IEnumerator MaFrameOn(Ctx c, Transform t)
    {
        var cc = c.Ui.Viewport.Camera;
        var r = t ? t.root : null;
        float yaw = r ? r.eulerAngles.y + 180f : 180f;
        cc.SetView(t ? t.position : Vector3.up * 1.5f, new Vector3(10, yaw, 0), 1.2f, 23);
        cc.Flush();
        yield return PlayModeHarness.WaitFrames(4);
    }

    static Transform MaSetTransform(Ctx c, string id, int slot, int set)
    {
        var go = c.App.Runtime.FindChara(id)?.Context?.AccessoryObject(slot);
        var cmp = go ? go.GetComponent<CmpAccessory>() : null;
        var t = cmp == null ? null : set == 0 ? cmp.trfMove01 : cmp.trfMove02;
        return t ? t : null;
    }

    // ---------------------------------------------------------------- FU2 (6.3a, item 9)

    /// <summary>The State tab's accessory rows against the document: one `accessory-&lt;id&gt;` per worn accessory in slot order, each
    /// labelled with its slot (and <paramref name="part"/> on its own accessory); what differs, empty = they match.</summary>
    static List<string> MaStateDiff(Ctx c, InspectorPanel insp, string id)
    {
        var bad = new List<string>();
        var body = insp?.Chara?.TabBodies["state"];
        if (body == null) { bad.Add("no State tab"); return bad; }
        var rows = body.Query<FieldRow>().ToList().Where(r => r.name != null && r.name.StartsWith("accessory-", StringComparison.Ordinal)).ToList();
        var want = MaTable(c, id).EntryBySlot.ToList();
        var names = want.Select(kv => "accessory-" + kv.Value.Id).ToList();
        if (!rows.Select(r => r.name).SequenceEqual(names)) bad.Add(rows.Count + " rows for " + names.Count + " worn, or another order");
        foreach (var kv in want)
        {
            var row = rows.FirstOrDefault(r => r.name == "accessory-" + kv.Value.Id);
            if (row != null && !row.Query<Label>().ToList().Any(l => l.text != null && l.text.Contains(MaNN(kv.Key)))) bad.Add("no slot " + MaNN(kv.Key) + " in its label");
        }
        bool note = MfQ<VisualElement>(body, "state-no-accessories") != null;
        if (note != (want.Count == 0)) bad.Add("the no-accessories note " + (note ? "shown" : "missing"));
        return bad;
    }

    /// <summary>What every step of FU2 keeps on the Edit tab: the inspector not built again, the same tab, both pickers' choices, the
    /// free slots offered, the slot list = the slot table, a worn slot selected. The State tab's rows are read by showing the tab.</summary>
    static IEnumerator MaFu2Check(Ctx c, string when, InspectorPanel insp, int builds, CharaEditTab tab, string id, int addChoice, int target)
    {
        yield return PlayModeHarness.WaitFrames(3);
        var table = MaTable(c, id);
        var list = tab.AccessorySlots?.Slots.Select(s => s.Id).ToList() ?? new List<string>();
        var worn = table.EntryBySlot.Values.Select(e => e.Id).ToList();
        c.Check(insp.Builds == builds && CharaEditTab.Current == tab, $"FU2 {when}: the inspector is not built again (builds {insp.Builds}, was {builds}), the same Edit tab");
        c.Check(tab.AccessoryAddSlot.Selected == addChoice && tab.AccessoryTargetPicker.Selected == target,
            $"FU2 {when}: both pickers keep their choices (＋ {tab.AccessoryAddSlot.Selected}, wanted {addChoice}; target {tab.AccessoryTargetPicker.Selected}, wanted {target})");
        c.Check(tab.AccessoryAddSlot.Offered.SequenceEqual(table.FreeSlots()) && tab.AccessoryTargetPicker.IsOccupied(worn.Count > 0 ? table.SlotOfId(worn[0]) : -1) == (worn.Count > 0),
            $"FU2 {when}: the pickers follow the slot table (＋ offers the {table.FreeSlots().Count()} free slots)");
        c.Check(list.SequenceEqual(worn) && (worn.Count == 0 ? tab.AccessoryShownId == null : worn.Contains(tab.AccessoryShownId)),
            $"FU2 {when}: the slot list = the slot table ({list.Count}), a worn slot is shown ({MaNN(tab.AccessoryShownSlot)})");
        insp.Chara.ShowTab("state");
        var bad = MaStateDiff(c, insp, id);
        c.Check(bad.Count == 0, $"FU2 {when}: the State tab's rows match the document as soon as it is shown ({worn.Count} worn)" + (bad.Count > 0 ? "; " + string.Join(", ", bad) : ""));
        insp.Chara.ShowTab(CharaEditTab.TabId);
        yield return PlayModeHarness.WaitFrames(3);
    }

    /// <summary>A step of FU2 with the State tab shown: the rows follow without showing it again and the inspector stays.</summary>
    static IEnumerator MaFu2State(Ctx c, string when, InspectorPanel insp, int builds, string id)
    {
        yield return PlayModeHarness.WaitFrames(3);
        var bad = MaStateDiff(c, insp, id);
        c.Check(bad.Count == 0 && insp.Builds == builds && insp.Chara.CurrentTab == "state",
            $"FU2 {when}, State tab shown: the rows match the document ({MaTable(c, id).Count} worn), builds {insp.Builds} (was {builds})" + (bad.Count > 0 ? "; " + string.Join(", ", bad) : ""));
    }

    /// <summary>The clothes rows of the State tab against the realization's state set: which rows show, which buttons are enabled.</summary>
    static List<string> MaClothesDiff(Ctx c, InspectorPanel insp, string id)
    {
        var bad = new List<string>();
        var set = c.Ui.Ops.ClothesStatesOf(id);
        var body = insp.Chara.TabBodies["state"];
        for (int k = 0; k < ClothesStateSet.KindCount; k++)
        {
            var row = MfQ<ClothesRow>(body, "field-chara.state.clothes[" + k + "]");
            if (row == null) { bad.Add("kind " + k + " has no row"); continue; }
            bool has = !set.Known || set.IsStateKind(k);
            if ((row.style.display != DisplayStyle.None) != has) bad.Add(ClothesStateSet.KindNames[k] + " " + (has ? "hidden" : "shown"));
            else if (has)
                for (int s = 0; s < 3; s++)
                    if (row.Buttons.Buttons[s].enabledSelf != (!set.Known || set.Supports(k, s))) bad.Add(ClothesStateSet.KindNames[k] + " state " + s);
        }
        return bad;
    }

    /// <summary>The FK rows of the kinematics tab against the character's bone table, group by group (the applied groups' keys).</summary>
    static List<string> MaFkDiff(Ctx c, InspectorPanel insp, string id)
    {
        var bad = new List<string>();
        var real = c.App.Runtime.FindChara(id);
        var st = MaNode(c, id).State as CharaNodeState;
        var table = HS2Next.Scenes.Runtime.FkDriver.TableFor(real);
        if (table == null || st == null) { bad.Add("no bone table"); return bad; }
        var want = new HashSet<string>();
        var g = st.Fk.Groups;
        foreach (HS2Next.Motion.Fk.FkGroup x in Enum.GetValues(typeof(HS2Next.Motion.Fk.FkGroup)))
        {
            bool on = x == HS2Next.Motion.Fk.FkGroup.Hair ? g.Hair : x == HS2Next.Motion.Fk.FkGroup.Neck ? g.Neck : x == HS2Next.Motion.Fk.FkGroup.Breast ? g.Breast : x == HS2Next.Motion.Fk.FkGroup.Body ? g.Body
                : x == HS2Next.Motion.Fk.FkGroup.RightHand ? g.RightHand : x == HS2Next.Motion.Fk.FkGroup.LeftHand ? g.LeftHand : g.Skirt;
            if (!on) continue;
            foreach (var b in table.Of(x)) if (b?.Key != null) want.Add(b.Key);
        }
        var got = new HashSet<string>(insp.Chara.TabBodies["kinematics"].Query<FieldRow>().ToList().Where(r => r.name != null && r.name.StartsWith("fk-", StringComparison.Ordinal)).Select(r => r.name.Substring(3)));
        var missing = want.Except(got).ToList();
        var extra = got.Except(want).ToList();
        if (missing.Count > 0) bad.Add(missing.Count + " bones without a row (" + string.Join(", ", missing.Take(3)) + ")");
        if (extra.Count > 0) bad.Add(extra.Count + " rows without a bone (" + string.Join(", ", extra.Take(3)) + ")");
        return bad;
    }

    static int MaFkRows(InspectorPanel insp) => insp.Chara.TabBodies["kinematics"].Query<FieldRow>().ToList().Count(r => r.name != null && r.name.StartsWith("fk-", StringComparison.Ordinal));

    static int MaBones(Ctx c, string id) => HS2Next.Scenes.Runtime.FkDriver.TableFor(c.App.Runtime.FindChara(id))?.Of(HS2Next.Motion.Fk.FkGroup.Skirt).Count ?? -1;

    static IEnumerator MaFu2(Ctx c, string f, string m)
    {
        var ui = c.Ui;
        const string item = "illusion.hs2#0";
        int u0 = UndoCount(c);
        var tab = MfOpenEdit(c, f);
        tab.ShowNode("accessories");
        yield return PlayModeHarness.WaitFrames(3);
        var insp = Inspector(c);
        int builds = insp.Builds;
        var frees = Enumerable.Range(0, 20).Where(s => MaTable(c, f).IsFree(s)).ToList();
        if (frees.Count < 6) { c.Warning("FU2: fewer than 6 free slots on BR-Chan (" + frees.Count + "): not taken"); yield break; }
        // the choices FU2 keeps: the two highest free slots below 20; the operations use the lowest ones
        int addChoice = frees[frees.Count - 1], target = frees[frees.Count - 2];
        tab.OpenAccessoryAdd(true);
        yield return PlayModeHarness.WaitFrames(2);
        c.Check(tab.AccessoryAddSlot.Choose(addChoice) && tab.AccessoryTargetPicker.Choose(target), $"FU2: the choices: ＋ {MaNN(addChoice)}, copy target {MaNN(target)}");
        tab.OpenAccessoryAdd(false);
        var ok = new bool[1];

        // ---- add, remove, copy to a new slot, undo, redo on the Edit tab
        int s1 = MaTable(c, f).LowestFree();
        int u1 = UndoCount(c);
        var r1 = tab.AddAccessoryNow(352, item, s1);
        string a1 = (r1.Value as MakerResult)?.NewAccessoryIds.FirstOrDefault();
        c.Check(r1.Ok && a1 != null && UndoCount(c) == u1 + 1 && tab.AccessoryShownId == a1 && c.UndoLabel == "Add accessory", $"FU2 add: slot {MaNN(s1)}, one entry \"{c.UndoLabel}\", selected at once ({tab.AccessoryShownId == a1})");
        int marked = tab.AccessoryPending, sections = tab.AccessoryLive.Entries.Values.Count(x => x.Section.Watch != null);
        c.Check(marked > sections, $"FU2 add: every live section is looked at again after the flush that realizes the change ({marked} marks, {sections} sections with a watch)");
        yield return MaFu2Check(c, "add", insp, builds, tab, f, addChoice, target);
        yield return MaWaitRows(c, tab, f, a1, s1);
        MaCheckRows(c, tab, f, a1, s1, "FU2 add");

        var r2 = tab.CopyAccessorySlot("copyToSlot");
        string a2 = (r2.Value as MakerResult)?.NewAccessoryIds.FirstOrDefault();
        c.Check(r2.Ok && a2 != null && MaTable(c, f).SlotOfId(a2) == target && tab.AccessoryShownId == a1 && tab.AccessoryTargetPicker.Selected == target,
            $"FU2 copy: a new accessory in the target {MaNN(target)}, the source stays selected, the target stays chosen");
        yield return MaFu2Check(c, "copy to a new slot", insp, builds, tab, f, addChoice, target);
        c.Check(tab.AccessoryTargetPicker.IsOccupied(target) && MfQ<Button>(tab, "acc-target-slot-" + MaNN(target))?.ClassListContains(AccessorySlotPicker.OccupiedClass) == true, "FU2 copy: the target is marked occupied");

        var r3 = tab.RemoveAccessoryNow(a1);
        c.Check(r3.Ok && MaEntry(c, f, a1) == null && c.UndoLabel == "Remove accessory", "FU2 remove: gone, one entry \"" + c.UndoLabel + "\"");
        yield return MaFu2Check(c, "remove", insp, builds, tab, f, addChoice, target);

        int undoFloor = u1;
        string[] names = { "undo of the removal", "undo of the copy", "undo of the add" };
        for (int i = 0; i < 3; i++)
        {
            c.App.Session.Commands.Undo();
            yield return MaFu2Check(c, names[i], insp, builds, tab, f, addChoice, target);
        }
        c.Check(UndoCount(c) == undoFloor && MaEntry(c, f, a1) == null && MaEntry(c, f, a2) == null, "FU2: undone to the start");
        for (int i = 0; i < 3; i++) c.App.Session.Commands.Redo();
        yield return MaFu2Check(c, "redo of the three", insp, builds, tab, f, addChoice, target);
        c.Check(MaEntry(c, f, a1) == null && MaEntry(c, f, a2) != null, "FU2: redo gives the end state (the copy worn, the first add removed)");
        yield return MfUndoTo(c, u1);
        yield return MaFu2Check(c, "undone again", insp, builds, tab, f, addChoice, target);

        // ---- a pick in the kept list: into the chosen slot (the choice falls back to the lowest free), the same row again
        tab.OpenAccessoryAdd(true);
        var addList = tab.ShowAccessoryAddList(352);
        yield return PlayModeHarness.WaitFrames(3);
        addList.Grid.Focus();
        yield return PlayModeHarness.WaitFrames(1);
        int u2 = UndoCount(c);
        c.Check(addList.Pick(item), "FU2: a pick in the add panel's list");
        yield return WaitFor(() => UndoCount(c) == u2 + 1, 30, ok);
        var fe = tab.panel?.focusController?.focusedElement as VisualElement;
        c.Check(ok[0] && tab.AccessoryAddPanel.style.display == DisplayStyle.None && MaTable(c, f).EntryAt(addChoice) != null && (fe == null || !tab.AccessoryAddPanel.Contains(fe)),
            $"FU2: the pick went into the chosen slot {MaNN(addChoice)}, the panel closed, the focus is not left in it ({(fe == null ? "none" : fe.name)})");
        int lowest = MaTable(c, f).LowestFree();
        yield return MaFu2Check(c, "a pick into the chosen slot (it falls back to the lowest free)", insp, builds, tab, f, lowest, target);
        tab.OpenAccessoryAdd(true);
        yield return PlayModeHarness.WaitFrames(2);
        int u3 = UndoCount(c);
        c.Check(tab.AccessoryAddList == addList && addList.Pick(item), "FU2: the add panel's list is kept and the same row can be picked again");
        yield return WaitFor(() => UndoCount(c) == u3 + 1, 30, ok);
        c.Check(ok[0] && MaTable(c, f).EntryAt(lowest) != null, $"FU2: the second pick of the row adds another accessory in {MaNN(lowest)}");
        yield return MfUndoTo(c, u1);
        tab.AccessoryAddSlot.Choose(addChoice);
        yield return MaFu2Check(c, "both picks undone", insp, builds, tab, f, addChoice, target);

        // ---- the add panel stays open over a removal, with the slot it freed offered
        {
            int sx = MaTable(c, f).LowestFree();
            var rx = ui.Ops.AddAccessory(f, 352, item, null, null, UiContext.User(OpLabels.AddAccessory), sx);
            string ax = (rx.Value as MakerResult)?.NewAccessoryIds.FirstOrDefault();
            yield return PlayModeHarness.WaitFrames(3);
            tab.OpenAccessoryAdd(true);
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(!tab.AccessoryAddSlot.Offered.Contains(sx), $"FU2: slot {MaNN(sx)} is worn: not offered");
            ui.Ops.RemoveAccessory(f, ax, UiContext.User(OpLabels.RemoveAccessory));
            yield return MaFu2Check(c, "an operation's removal, add panel open", insp, builds, tab, f, addChoice, target);
            c.Check(tab.AccessoryAddPanel.style.display == DisplayStyle.Flex && tab.AccessoryAddSlot.Offered.Contains(sx), $"FU2: the add panel stays open and offers the freed slot {MaNN(sx)}");
            tab.OpenAccessoryAdd(false);
            yield return MfUndoTo(c, u1);
        }

        // ---- the slot after a removal: the next worn one, else the lowest (the rebuild always gave the lowest)
        {
            var worn = MaTable(c, f).EntryBySlot.Keys.ToList();
            int top = worn[worn.Count - 1];
            int mid = frees.First(s => s > worn[0]);
            int after = worn.First(s => s > mid);
            var rm = ui.Ops.AddAccessory(f, 352, item, null, null, UiContext.User(OpLabels.AddAccessory), mid);
            string am = (rm.Value as MakerResult)?.NewAccessoryIds.FirstOrDefault();
            yield return PlayModeHarness.WaitFrames(3);
            tab.SelectAccessory(am);
            yield return PlayModeHarness.WaitFrames(2);
            tab.RemoveAccessoryNow(am);
            yield return PlayModeHarness.WaitFrames(3);
            c.Check(tab.AccessoryShownSlot == after, $"FU2: removing the selected {MaNN(mid)} selects the next worn slot {MaNN(after)} ({MaNN(tab.AccessoryShownSlot)})");
            yield return MfUndoTo(c, u1);
            int high = Enumerable.Range(top + 1, 19 - top).FirstOrDefault(s => s != addChoice && s != target);
            if (high > top)
            {
                var rh = ui.Ops.AddAccessory(f, 352, item, null, null, UiContext.User(OpLabels.AddAccessory), high);
                string ah = (rh.Value as MakerResult)?.NewAccessoryIds.FirstOrDefault();
                yield return PlayModeHarness.WaitFrames(3);
                tab.SelectAccessory(ah);
                yield return PlayModeHarness.WaitFrames(2);
                tab.RemoveAccessoryNow(ah);
                yield return PlayModeHarness.WaitFrames(3);
                c.Check(tab.AccessoryShownSlot == worn[0], $"FU2: removing the selected highest slot {MaNN(high)} selects the lowest {MaNN(worn[0])} ({MaNN(tab.AccessoryShownSlot)})");
                yield return MfUndoTo(c, u1);
            }
            else c.Warning("FU2: no free slot above the highest worn: the lowest-slot case was not taken");
        }

        // ---- a handle on the accessory that leaves; its group; another accessory in its slot, then undo twice: the group is its own
        {
            int sa = MaTable(c, f).LowestFree();
            var ra = ui.Ops.AddAccessory(f, 353, item, null, null, UiContext.User(OpLabels.AddAccessory), sa);
            string aa = (ra.Value as MakerResult)?.NewAccessoryIds.FirstOrDefault();
            yield return PlayModeHarness.WaitFrames(3);
            tab.SelectAccessory(aa);
            yield return MaWaitRows(c, tab, f, aa, sa);
            tab.SelectAccessoryHandle(aa, 1, GizmoTool.Move);
            yield return PlayModeHarness.WaitFrames(2);
            string sub = ui.SubSelection;
            int u4 = UndoCount(c);
            tab.RemoveAccessoryNow(aa);
            yield return PlayModeHarness.WaitFrames(3);
            c.Check(sub == StudioOps.AccessorySub(f, aa, 0) && ui.SubSelection == null && tab.AccessoryShownId != aa, "FU2: the handle of the accessory that left is no longer the sub-selection (" + (ui.SubSelection ?? "none") + ")");
            var rb = tab.AddAccessoryNow(352, item, sa);
            string ab = (rb.Value as MakerResult)?.NewAccessoryIds.FirstOrDefault();
            yield return MaWaitRows(c, tab, f, ab, sa);
            MaCheckRows(c, tab, f, ab, sa, "FU2 another accessory in the slot that was freed");
            c.App.Session.Commands.Undo();
            yield return PlayModeHarness.WaitFrames(3);
            c.App.Session.Commands.Undo();
            yield return PlayModeHarness.WaitFrames(3);
            c.Check(UndoCount(c) == u4 && MaEntry(c, f, aa) != null, "FU2: undo twice brings the first accessory back");
            tab.SelectAccessory(aa);
            yield return MaWaitRows(c, tab, f, aa, sa);
            MaCheckRows(c, tab, f, aa, sa, "FU2 the accessory that came back");
            var live = tab.AccessoryLive?.Entries;
            string adj = InspectorSchemas.AccessorySectionId(sa, "adjust");
            bool own = live != null && live.TryGetValue(adj, out var e) && tab.AccessoryGroup != null && tab.AccessoryGroup.Contains(e.Fold);
            c.Check(own, "FU2: its sections are live on the group shown (its own, not a kept copy's)");
            bool target0 = tab.AccessoryHandleTarget(out var hc, out var ha, out int hs);
            c.Check(target0 && hc == f && ha == aa && hs == sa, $"FU2: its handles follow the group shown ({target0}, slot {MaNN(hs)})");
            yield return MfUndoTo(c, u1);
        }
        yield return MaFu2Check(c, "after the slot cases", insp, builds, tab, f, addChoice, target);

        // ---- the State tab shown: every step follows at once
        {
            insp.Chara.ShowTab("state");
            yield return PlayModeHarness.WaitFrames(3);
            var bad0 = MaStateDiff(c, insp, f);
            c.Check(bad0.Count == 0, "FU2: the State tab at the start" + (bad0.Count > 0 ? ": " + string.Join(", ", bad0) : ""));
            int sb = MaTable(c, f).LowestFree();
            var ra = ui.Ops.AddAccessory(f, 352, item, null, null, UiContext.User(OpLabels.AddAccessory), sb);
            string aa = (ra.Value as MakerResult)?.NewAccessoryIds.FirstOrDefault();
            yield return MaFu2State(c, "an add", insp, builds, f);
            int sc = MaTable(c, f).LowestFree();
            var rc = ui.Ops.CopyAccessory(f, aa, sc, null, AccessoryCopyParts.Slot, false, MirrorAxis.None, UiContext.User(OpLabels.CopyAccessory));
            string ac = (rc.Value as MakerResult)?.NewAccessoryIds.FirstOrDefault();
            c.Check(rc.Ok && ac != null, "FU2: a copy into the new slot " + MaNN(sc));
            yield return MaFu2State(c, "a copy to a new slot", insp, builds, f);
            // a refilled row writes: one entry, the state key; undo shows it again
            var row = MfQ<FieldRow>(insp.Chara.TabBodies["state"], "accessory-" + ac);
            var toggle = row?.Control as Toggle;
            int uw = UndoCount(c);
            if (toggle != null) toggle.value = false;
            yield return PlayModeHarness.WaitFrames(2);
            var stMap = (MaNode(c, f).State as CharaNodeState)?.State.Accessories;
            c.Check(toggle != null && stMap != null && stMap.TryGetValue(ac, out bool vis) && !vis && UndoCount(c) == uw + 1 && c.UndoLabel == OpLabels.Accessories,
                "FU2: the new row hides its accessory, one entry \"" + c.UndoLabel + "\"");
            c.App.Session.Commands.Undo();
            yield return PlayModeHarness.WaitFrames(3);
            c.Check(toggle != null && toggle.value, "FU2: undo shows the row on again");
            // the row names the worn item: an item change renames it, without a new row's position changing
            var other = MakerCatalog.For(c.App.Catalog)?.Rows(352, 1).Skip(1).FirstOrDefault();
            if (other != null)
            {
                string wantName = c.App.Catalog.GetEntry(352, other.Key)?["Name"];
                ui.Ops.ChangeItem(new[] { f }, MaPath(ac, "item"), other.Key.ToString(), UiContext.User(OpLabels.ChangeItem));
                yield return MaFu2State(c, "an item change", insp, builds, f);
                var row2 = MfQ<FieldRow>(insp.Chara.TabBodies["state"], "accessory-" + ac);
                c.Check(wantName != null && row2 != null && row2.Query<Label>().ToList().Any(l => l.text != null && l.text.Contains(wantName)), "FU2: the row names the new item (" + wantName + ")");
                c.App.Session.Commands.Undo();
                yield return MaFu2State(c, "the item change undone", insp, builds, f);
            }
            ui.Ops.RemoveAccessory(f, aa, UiContext.User(OpLabels.RemoveAccessory));
            yield return MaFu2State(c, "a removal", insp, builds, f);
            c.App.Session.Commands.Undo();
            yield return MaFu2State(c, "the removal undone", insp, builds, f);
            c.App.Session.Commands.Undo();
            yield return MaFu2State(c, "the copy undone", insp, builds, f);
            c.App.Session.Commands.Undo();
            yield return MaFu2State(c, "the add undone", insp, builds, f);
            c.App.Session.Commands.Redo();
            yield return MaFu2State(c, "the add redone", insp, builds, f);
            yield return MfUndoTo(c, u1);
        }

        // ---- a coordinate load and its undo, the State tab shown: accessories and clothes rows follow; the FK rows of the load
        string girly = McCoordFile(c, 1, "ガーリーブラウス");
        if (girly != null)
        {
            ui.Ops.SetFkEnabled(f, true, User);
            ui.Session.Commands.Set(f, "chara.fk.groups.skirt", true, User);
            yield return PlayModeHarness.WaitFrames(3);
            insp.Refresh();
            insp.Chara.ShowTab("state");
            builds = insp.Builds;
            yield return PlayModeHarness.WaitFrames(3);
            string set0 = ui.Ops.ClothesStatesOf(f).ToString();
            int fk0 = MaFkRows(insp), bones0 = MaBones(c, f);
            var load = ui.Ops.ApplyCoordinateFile(new[] { f }, girly, CoordinateApplyMode.All, UiContext.User(OpLabels.LoadCoordinate));
            yield return WaitFor(() => c.App.Runtime.FindChara(f)?.State == RealizationState.Built, 30, ok);
            yield return WaitFor(() => MaClothesDiff(c, insp, f).Count == 0 && MaStateDiff(c, insp, f).Count == 0, 3, ok);
            string set1 = ui.Ops.ClothesStatesOf(f).ToString();
            var dc = MaClothesDiff(c, insp, f);
            var ds = MaStateDiff(c, insp, f);
            c.Info($"FU2 a coordinate load: inspector builds {insp.Builds} (was {builds}), clothes state set {set0} -> {set1}; FK rows {fk0} -> {MaFkRows(insp)}, skirt bones of the table {bones0} -> {MaBones(c, f)}");
            c.Check(load.Ok && dc.Count == 0, "FU2: a coordinate load with the State tab shown: the clothes rows follow the worn outfit" + (dc.Count > 0 ? ": " + string.Join(", ", dc) : ""));
            c.Check(ds.Count == 0, "FU2: a coordinate load with the State tab shown: the accessory rows match the document" + (ds.Count > 0 ? ": " + string.Join(", ", ds) : ""));
            yield return WaitFor(() => MaFkDiff(c, insp, f).Count == 0, 3, ok);
            var dk = MaFkDiff(c, insp, f);
            c.Check(dk.Count == 0, "FU2: a coordinate load with FK on: the FK rows match the bone table" + (dk.Count > 0 ? ": " + string.Join(", ", dk) : ""));
            c.App.Session.Commands.Undo();
            yield return WaitFor(() => MaClothesDiff(c, insp, f).Count == 0 && MaStateDiff(c, insp, f).Count == 0, 3, ok);
            dc = MaClothesDiff(c, insp, f);
            ds = MaStateDiff(c, insp, f);
            c.Check(dc.Count == 0 && ds.Count == 0, "FU2: its undo: the clothes and accessory rows follow" + (dc.Count + ds.Count > 0 ? ": " + string.Join(", ", dc.Concat(ds)) : ""));
            yield return WaitFor(() => MaFkDiff(c, insp, f).Count == 0, 3, ok);
            dk = MaFkDiff(c, insp, f);
            c.Check(dk.Count == 0, "FU2: its undo: the FK rows match the bone table" + (dk.Count > 0 ? ": " + string.Join(", ", dk) : ""));
            string jersey = McCoordFile(c, 1, "ジャージ");
            if (jersey != null)
            {
                int jb = MaBones(c, f);
                ui.Ops.ApplyCoordinateFile(new[] { f }, jersey, CoordinateApplyMode.All, UiContext.User(OpLabels.LoadCoordinate));
                yield return WaitFor(() => MaFkDiff(c, insp, f).Count == 0 && MaClothesDiff(c, insp, f).Count == 0, 3, ok);
                dk = MaFkDiff(c, insp, f);
                dc = MaClothesDiff(c, insp, f);
                c.Info($"FU2 a second coordinate: inspector builds {insp.Builds} (was {builds}), FK rows {MaFkRows(insp)}, skirt bones of the table {jb} -> {MaBones(c, f)}");
                c.Check(dk.Count == 0 && dc.Count == 0, "FU2: a second coordinate: the FK and clothes rows follow" + (dk.Count + dc.Count > 0 ? ": " + string.Join(", ", dk.Concat(dc)) : ""));
                c.App.Session.Commands.Undo();
                yield return WaitFor(() => MaFkDiff(c, insp, f).Count == 0 && MaClothesDiff(c, insp, f).Count == 0, 3, ok);
                dk = MaFkDiff(c, insp, f);
                dc = MaClothesDiff(c, insp, f);
                c.Check(dk.Count == 0 && dc.Count == 0, "FU2: its undo: the FK and clothes rows follow" + (dk.Count + dc.Count > 0 ? ": " + string.Join(", ", dk.Concat(dc)) : ""));
            }
            yield return MfUndoTo(c, u1);
        }
        else c.Warning("FU2: the female coordinate «ガーリーブラウス» is not in the install (the coordinate case was not taken)");

        // ---- the Advanced pose tab keeps its Pick over an add and a removal
        {
            yield return PlayModeHarness.WaitFrames(2);
            insp.Refresh();
            builds = insp.Builds;
            insp.Chara.ShowTab(AdvancedPoseTab.TabId);
            yield return PlayModeHarness.WaitFrames(3);
            var adv = AdvancedPoseTab.Current;
            adv?.SetPick(true);
            var rp = ui.Ops.AddAccessory(f, 352, item, null, null, UiContext.User(OpLabels.AddAccessory), MaTable(c, f).LowestFree());
            yield return PlayModeHarness.WaitFrames(3);
            c.Check(adv != null && rp.Ok && adv.PickOn && AdvancedPoseTab.Current == adv && insp.Builds == builds, $"FU2: the Advanced pose tab keeps its Pick over an add (builds {insp.Builds}, was {builds})");
            c.App.Session.Commands.Undo();
            yield return PlayModeHarness.WaitFrames(3);
            c.Check(adv != null && adv.PickOn && insp.Builds == builds, "FU2: and over its undo");
            adv?.SetPick(false);
        }

        // ---- no accessory at all: the note, then the rows again (the male)
        {
            ui.Ops.Select(new[] { m });
            yield return PlayModeHarness.WaitFrames(3);
            insp = Inspector(c);
            insp.Chara.ShowTab("state");
            yield return PlayModeHarness.WaitFrames(3);
            builds = insp.Builds;
            int um = UndoCount(c);
            var worn0 = MaTable(c, m).EntryBySlot.Values.Select(e => e.Id).ToList();
            foreach (var a in worn0) ui.Ops.RemoveAccessory(m, a, UiContext.User(OpLabels.RemoveAccessory));
            yield return MaFu2State(c, "every accessory of the male removed", insp, builds, m);
            c.Check(MaTable(c, m).Count == 0 && MfQ<VisualElement>(insp.Chara.TabBodies["state"], "state-no-accessories") != null, $"FU2: no accessory left ({worn0.Count} removed), the note is shown");
            var rn = ui.Ops.AddAccessory(m, 352, item, null, null, UiContext.User(OpLabels.AddAccessory), null);
            yield return MaFu2State(c, "the first accessory added", insp, builds, m);
            c.Check(rn.Ok && MaTable(c, m).Count == 1 && MfQ<VisualElement>(insp.Chara.TabBodies["state"], "state-no-accessories") == null, "FU2: one accessory, its row instead of the note");
            c.App.Session.Commands.Undo();
            yield return MaFu2State(c, "its undo", insp, builds, m);
            c.Check(MfQ<VisualElement>(insp.Chara.TabBodies["state"], "state-no-accessories") != null, "FU2: the note again");
            yield return MfUndoTo(c, um);
            yield return MaFu2State(c, "back at the start", insp, builds, m);
            ui.Ops.Select(new[] { f });
            yield return PlayModeHarness.WaitFrames(3);
        }
        c.Check(UndoCount(c) == u0, $"FU2: the history is back at the start ({UndoCount(c)}, {u0})");
    }

    static IEnumerator MakerAccessoriesGroup(Ctx c)
    {
        var ui = c.Ui;
        ui.Options.Set(AccHandlesOption, true);
        ui.Options.Set(InspectorSchemas.AccessoryMirrorParentOption, false);
        ui.Options.Set(CharaEditTab.AccessoryStepOptionPrefix + "1", "normal");
        ui.Options.Set(CharaEditTab.AccessoryStepOptionPrefix + "2", "normal");
        var tool0 = ui.Tool;
        yield return NewScene(c);
        var ids = new List<string>();
        yield return AddBuiltChara(c, c.FemaleCard, ids);
        yield return AddBuiltChara(c, c.MaleCard, ids);
        if (ids.Count < 2) yield break;
        string f = ids[0], m = ids[1];
        var lists = MakerCatalog.For(c.App.Catalog);
        c.Check(lists != null && MakerServices.Rows != null, "Maker lists and rows");
        if (MakerServices.Accessories == null) c.Warning("MakerServices.Accessories is not registered: the rows are checked against the plain rule (every prefab feature)");
        int u00 = UndoCount(c);
        var cat = c.App.Catalog;
        var pt = HS2Next.UI.Panels.Maker.PartThumbnails.Shared(cat);
        int guard0 = pt?.GuardSkipped ?? 0, badMip0 = pt?.BadMipRejected ?? 0;
        c.Info($"start: open bundles {cat.OpenBundleCount}, pending thumbnails {pt?.Pending}, guard-skipped {guard0}, mip<1 rejected {badMip0}");

        // ---------------------------------------------------------------- the page (female)
        var tab = MfOpenEdit(c, f);
        yield return PlayModeHarness.WaitFrames(4);
        c.Check(tab != null && tab.panel != null, "the female's Edit tab is shown");
        if (tab == null) yield break;
        // XU4's targets are warm: the Hair page first, so the Maker row widgets' first use is not in the measurement when
        // this group runs alone
        tab.ShowNode("hair");
        yield return PlayModeHarness.WaitFrames(3);
        int gc0 = GC.CollectionCount(0), ge0 = tab.GateEvaluations;
        tab.ShowNode("accessories");
        int gcs = GC.CollectionCount(0) - gc0, gates = tab.GateEvaluations - ge0;
        yield return PlayModeHarness.WaitFrames(3);
        var page = tab.Pages.TryGetValue("accessories", out var pg0) ? pg0 : null;
        c.Check(page != null && tab.CurrentNode == "accessories" && tab.CurrentPage == null && MfQ<VisualElement>(tab, "edit-pages-accessories") == null && tab.AccessorySlots != null,
            "the Accessories node is one page (no page strip) with the slot list");
        var table = MaTable(c, f);
        var worn = table?.EntryBySlot.Keys.ToList() ?? new List<int>();
        var rows = tab.AccessorySlots?.Slots.Select(s => s.Slot).ToList() ?? new List<int>();
        var badges = worn.Select(s => MfQ<VisualElement>(tab, "acc-slot-" + MaNN(s))).ToList();
        c.Check(worn.Count > 0 && rows.SequenceEqual(worn) && badges.All(b => b != null && b.Q<Label>("badge")?.text == MaNN(worn[badges.IndexOf(b)])) && MfQ<VisualElement>(tab, "acc-slot-add") != null,
            "the slot list = the slot table's worn slots with their numbers (" + string.Join(" ", worn.Select(MaNN)) + ") and the «＋» row");
        int s0 = worn.Count > 0 ? worn[0] : -1;
        string a0 = s0 >= 0 ? table.EntryAt(s0).Id : null;
        double firstShow = tab.PageBuildMs.TryGetValue("accessories.accessories", out var fs) ? fs : -1;
        double firstGroup = s0 >= 0 && tab.PageBuildMs.TryGetValue("accessories.group." + MaNN(s0), out var fg) ? fg : -1;
        c.Check(tab.AccessoryShownId == a0 && tab.AccessoryGroup?.name == "acc-slot-group-" + MaNN(s0), "the lowest worn slot is selected and its group shown (" + tab.AccessoryGroup?.name + ")");
        TimedShow(c, "XU4: Accessories page first show", firstShow, gcs, PageTargetMs, $"incl. the first slot group {firstGroup:0.0} ms");
        c.Info("XU4 first show breakdown (ms): " + string.Join(", ", tab.PageBuildMs.Where(kv => kv.Key.StartsWith("section.accessories.", StringComparison.Ordinal)).Select(kv => kv.Key.Substring("section.accessories.accessories.".Length) + " " + kv.Value.ToString("0.0")))
            + "; type list rows built " + (MfQ<PartList>(tab, "field-" + MaPath(a0, "item"))?.LastBuildMs.ToString("0.0") ?? "-") + " ms; GC collections (gen 0) during it " + gcs + "; gate evaluations " + gates);
        var secs = page?.Query<Foldout>().ToList().Select(x => x.name).Where(n => n.StartsWith("edit-section-accessories.accessories.", StringComparison.Ordinal) && MhShown(page.Q<Foldout>(n))).Select(n => n.Substring("edit-section-accessories.accessories.".Length)).ToList() ?? new List<string>();
        var f0 = MaFacts(f, s0);
        var wantSecs = new List<string> { "slots", MaNN(s0) + ".type" };
        if (f0 == null || f0.ColourShown) wantSecs.Add(MaNN(s0) + ".colour");
        if (f0 != null && f0.HairShown) wantSecs.Add(MaNN(s0) + ".hairColour");
        if (f0 == null || !f0.Fixed) wantSecs.Add(MaNN(s0) + ".parent");
        wantSecs.Add(MaNN(s0) + ".adjust");
        wantSecs.Add("copy");
        c.Check(secs.SequenceEqual(wantSecs), "sections in order: slots, the slot's type / colour / parent / adjust, copy (" + string.Join(", ", secs) + ")");
        MaCheckRows(c, tab, f, a0, s0, "first show");
        c.Info("BR-Chan's slots: " + string.Join("; ", worn.Select(s => { var e = table.EntryAt(s); var fx = MaFacts(f, s); return MaNN(s) + " " + e.Category + ":" + e.Item + " hair " + fx?.TypeHair + " areas " + (fx == null ? "?" : string.Concat(fx.UseColor.Select(b => b ? "1" : "0"))) + " alpha " + fx?.AlphaRenderers + " sets " + fx?.Move1 + "/" + fx?.Move2; })));

        // ---------------------------------------------------------------- «＋» 353#0 into the lowest free slot (normal, alpha renderers)
        int free0 = table.LowestFree();
        tab.OpenAccessoryAdd(true);
        yield return PlayModeHarness.WaitFrames(2);
        c.Check(tab.AccessoryAddPanel?.style.display == DisplayStyle.Flex && tab.AccessoryAddSlot?.Selected == free0 && tab.AccessoryAddSlot.Offered.SequenceEqual(table.FreeSlots())
                && Enumerable.Range(1, 13).All(k => MfQ<Button>(tab, "acc-add-type-" + k) != null) && MfQ<Button>(tab, "acc-add-type-0") == null,
            "«＋» opens the add panel: the free slots (lowest " + MaNN(free0) + " chosen), 13 types without «なし»");
        tab.OpenAccessoryAdd(false);
        int u1 = UndoCount(c);
        var nid = new string[1];
        yield return MaAdd(c, tab, f, free0, 353, "illusion.hs2#0", nid);
        string ag = nid[0];
        if (ag == null) yield break;
        tab = MaTab(c, f, tab);
        c.Check(UndoCount(c) == u1 + 1 && c.UndoLabel == "Add accessory" && tab.AccessoryAddPanel.style.display == DisplayStyle.None && tab.AccessorySlots.SelectedId == ag
                && MfQ<VisualElement>(tab, "acc-slot-" + MaNN(free0)) != null,
            "the pick = one entry \"" + c.UndoLabel + "\"; the panel closes, the new slot " + MaNN(free0) + " is listed and selected");
        yield return MaWaitRows(c, tab, f, ag, free0);
        MaCheckRows(c, tab, f, ag, free0, "353#0");
        var fg353 = MaFacts(f, free0);
        c.Info("353#0: areas " + (fg353 == null ? "?" : string.Concat(fg353.UseColor.Select(b => b ? "1" : "0"))) + ", alpha renderers " + fg353?.AlphaRenderers + ", sets " + fg353?.Move1 + "/" + fg353?.Move2 + ", parent column " + fg353?.ParentColumn);

        // a gap: «＋» to slot 10 (index 9) leaves 08 / 09 empty and the list shows the number
        int gapSlot = 9;
        bool gapFree = MaTable(c, f).IsFree(gapSlot);
        string agap = null;
        if (gapFree)
        {
            yield return MaAdd(c, tab, f, gapSlot, 352, "illusion.hs2#0", nid);
            agap = nid[0];
            tab = MaTab(c, f, tab);
            var rows2 = tab.AccessorySlots.Slots.Select(s => s.Slot).ToList();
            c.Check(agap != null && rows2.Contains(gapSlot) && rows2.SequenceEqual(MaTable(c, f).EntryBySlot.Keys) && MfQ<VisualElement>(tab, "acc-slot-10")?.Q<Label>("badge")?.text == "10",
                "a slot with a gap below it is listed with its own number «10» (" + string.Join(" ", rows2.Select(MaNN)) + ")");
        }
        else c.Warning("slot 10 is worn on BR-Chan: the gap case was not taken");
        tab.SelectAccessory(ag);
        yield return PlayModeHarness.WaitFrames(3);

        // ---------------------------------------------------------------- colour rows (353#0)
        var fx353 = MaFacts(f, free0);
        if (fx353 != null && fx353.AlphaShown)
        {
            int u2 = UndoCount(c);
            var g4 = MfQ<MakerSliderRow>(tab, "field-" + MaPath(ag, "colors[3].gloss"));
            var c4 = MfQ<MakerColorRow>(tab, "field-" + MaPath(ag, "colors[3].color"));
            c.Check(g4 != null && g4.Spec.ResetKind == ResetKind.PrefabDefault && c4 != null && c4.AlphaShown, "colour 4 (the alpha renderers) with alpha, its «ツヤ» resets to the prefab default");
            if (g4 != null)
            {
                g4.Write(new[] { f }, 0.3, "ma.typed");
                yield return PlayModeHarness.WaitFrames(2);
                c.Check(Mathf.Abs(MaEntry(c, f, ag).Colors[3].Gloss - 0.3f) < 1e-6f && UndoCount(c) == u2 + 1 && c.UndoLabel == "Edit accessory", "«ツヤ» of colour 4 typed 30 → stored 0.3, one entry \"" + c.UndoLabel + "\"");
            }
            int area = Enumerable.Range(1, 3).FirstOrDefault(n => fx353.AreaShown(n));
            if (area > 0)
            {
                var met = MfQ<MakerSliderRow>(tab, "field-" + MaPath(ag, "colors[" + (area - 1) + "].metallic"));
                met?.Write(new[] { f }, 0.9, "ma.typed2");
                yield return PlayModeHarness.WaitFrames(2);
                met?.Reset();
                yield return PlayModeHarness.WaitFrames(2);
                float want = fx353.DefaultColors != null && fx353.DefaultColors.Length >= area ? fx353.DefaultColors[area - 1].Metallic : float.NaN;
                float got = MaEntry(c, f, ag).Colors[area - 1].Metallic;
                c.Check(met != null && c.UndoLabel == "Reset value" && (float.IsNaN(want) || Mathf.Abs(got - want) < 1e-6f), $"↺ «質感» of colour {area} = the prefab default {want:0.####} (stored {got:0.####}, \"{c.UndoLabel}\")");
            }
            int u3 = UndoCount(c);
            var defBtn = MfQ<Button>(tab, "acc-default-colours-" + MaNN(free0));
            MaPress(defBtn);
            yield return PlayModeHarness.WaitFrames(3);
            var cols = MaEntry(c, f, ag).Colors;
            bool eq = fx353.DefaultColors != null && Mathf.Abs(cols[3].Gloss - fx353.DefaultColors[3].Gloss) < 1e-6f && Mathf.Abs(cols[3].Color.A - fx353.DefaultColors[3].Color.A) < 1e-6f;
            c.Check(defBtn != null && UndoCount(c) == u3 + 1 && c.UndoLabel == "Edit accessory" && eq,
                $"«初期カラー» (acc-default-colours-{MaNN(free0)}) → the prefab defaults (colour 4 gloss {cols[3].Gloss:0.###}, alpha {cols[3].Color.A:0.###}), one entry");
        }
        else c.Warning("353#0 shows no alpha colour here (facts " + (fx353 == null ? "missing" : "without alpha renderers") + "): the colour 4 steps were not taken");

        // ---------------------------------------------------------------- the parent picker
        {
            var pick = MfQ<AttachPointPicker>(tab, "field-" + MaPath(ag, "parent"));
            var groupsShown = pick?.Query<VisualElement>().ToList().Count(e => e.name != null && e.name.StartsWith("acc-parent-group-", StringComparison.Ordinal)) ?? 0;
            c.Check(pick != null && pick.Keys.Count == 54 && groupsShown == 10 && pick.Keys.Keys.All(k => MfQ<Button>(tab, "acc-parent-" + k) != null),
                "the parent picker: 54 keys (acc-parent-<key>) in 10 groups");
            if (pick != null)
            {
                int u4 = UndoCount(c);
                var r = pick.Pick("N_Hitai");
                var ok = new bool[1];
                yield return WaitFor(() => MaEntry(c, f, ag).Parent == "N_Hitai", 10, ok);
                yield return PlayModeHarness.WaitFrames(3);
                pick = MfQ<AttachPointPicker>(tab, "field-" + MaPath(ag, "parent")) ?? pick;
                c.Check(r.Ok && UndoCount(c) == u4 + 1 && c.UndoLabel == "Edit accessory" && pick.Marked == "N_Hitai", "a pick «額» (N_Hitai) = one entry \"" + c.UndoLabel + "\", marked");
                var anchor = c.App.Runtime.FindChara(f)?.Context?.Accessories.ElementAtOrDefault(free0)?.Anchor;
                c.Check(anchor != null && anchor.name == "N_Hitai", "the slot is anchored under N_Hitai (" + (anchor ? anchor.name : "none") + ")");
                var defP = MfQ<Button>(tab, "acc-parent-default");
                MaPress(defP);
                yield return WaitFor(() => MaEntry(c, f, ag).Parent != "N_Hitai", 10, ok);
                string dp = MakerEdits.AccessoryDefaultParent(MakerServices.Rows, 353, "illusion.hs2#0");
                c.Check(defP != null && MaEntry(c, f, ag).Parent == dp && c.UndoLabel == "Edit accessory", "«初期親» → the row's default parent «" + dp + "» (stored «" + MaEntry(c, f, ag).Parent + "»)");
            }
        }

        // ---------------------------------------------------------------- the transform rows (set 1)
        var pos = MfQ<MakerVectorRow>(tab, "field-" + MaPath(ag, "moves[0].position"));
        var rot = MfQ<MakerVectorRow>(tab, "field-" + MaPath(ag, "moves[0].rotation"));
        var scl = MfQ<MakerVectorRow>(tab, "field-" + MaPath(ag, "moves[0].scale"));
        c.Check(pos != null && rot != null && scl != null && MfQ<VisualElement>(tab, "acc-rate-" + MaNN(free0) + "-1") != null && MfQ<VisualElement>(tab, "acc-set-" + MaNN(free0) + "-1") != null,
            "set 1: position / rotation / scale rows, the set head with its rate choice (acc-rate-" + MaNN(free0) + "-1)");
        if (pos != null && rot != null && scl != null)
        {
            Vec3 P() => MaEntry(c, f, ag).Moves[0].Position;
            Vec3 R() => MaEntry(c, f, ag).Moves[0].Rotation;
            Vec3 S() => MaEntry(c, f, ag).Moves[0].Scale;
            int u5 = UndoCount(c);
            pos.Type(0, 12.34);
            c.Check(Mathf.Abs(P().X - 12.3f) < 1e-4f, "position x typed 12.34 → 12.3 (" + P().X + ")");
            pos.Type(0, 12.35);
            c.Check(Mathf.Abs(P().X - 12.4f) < 1e-4f, "position x typed 12.35 → 12.4 (HS2's field text) (" + P().X + ")");
            pos.Type(0, 400);
            c.Check(Mathf.Abs(P().X - 100f) < 1e-4f, "position x typed 400 → 100 (limit) (" + P().X + ")");
            pos.Type(0, -0.05);
            c.Check(Mathf.Abs(P().X + 0.1f) < 1e-4f, "position x typed −0.05 → −0.1 (" + P().X + ")");
            c.Check(UndoCount(c) == u5 + 4 && c.UndoLabel == "Edit accessory", "each typed value is one entry \"Edit accessory\" (" + (UndoCount(c) - u5) + " for 4)");
            rot.Type(1, 359.6);
            c.Check(Mathf.Abs(R().Y) < 1e-4f, "rotation y typed 359.6 → 0 (rounded to 360 as HS2's text, wrapped; C-W45-1) (" + R().Y + ")");
            rot.Type(1, -30);
            c.Check(Mathf.Abs(R().Y) < 1e-4f, "rotation y typed −30 → 0 (typed range 0..360) (" + R().Y + ")");
            rot.Type(1, 12.35);
            c.Check(Mathf.Abs(R().Y - 12f) < 1e-4f, "rotation y typed 12.35 → 12 (" + R().Y + ")");
            scl.Type(0, 0);
            c.Check(Mathf.Abs(S().X - 0.01f) < 1e-5f, "scale x typed 0 → 0.01 (" + S().X + ")");
            scl.Type(0, 12.344);
            c.Check(Mathf.Abs(S().X - 12.34f) < 1e-4f, "scale x typed 12.344 → 12.34 (" + S().X + ")");
            // steps: one entry per press-and-hold, the normal rate (position 1), repeats after 0.3 s every 0.05 s
            int u6 = UndoCount(c);
            float y0 = P().Y;
            pos.BeginHold(1, +1);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.5f) yield return null;
            pos.EndHold(false);
            yield return PlayModeHarness.WaitFrames(2);
            int steps = pos.HoldSteps;
            c.Check(UndoCount(c) == u6 + 1 && steps >= 3 && Mathf.Abs(P().Y - (y0 + steps * 1f)) < 1e-3f,
                $"a held «＋» on position y for 0.5 s → one entry, {steps} steps of 1 (y {y0} → {P().Y})");
            int u7 = UndoCount(c);
            float x0 = P().X;
            pos.BeginHold(0, +1);
            pos.EndHold(false);
            c.Check(UndoCount(c) == u7 + 1 && P().X > x0, $"«＋» on position x increases x like y and z (XQ14: {x0} → {P().X})");
            rot.Type(1, 359);
            rot.BeginHold(1, +1);
            rot.EndHold(false);
            c.Check(Mathf.Abs(R().Y - 4f) < 1e-4f, "rotation y 359 + 5 (normal rate) → 4 (wrap) (" + R().Y + ")");
            ui.Options.Set(CharaEditTab.AccessoryStepOptionPrefix + "1", "coarse");
            float z0 = P().Z;
            pos.BeginHold(2, -1);
            pos.EndHold(false);
            c.Check(Mathf.Abs(P().Z - (z0 - 10f)) < 1e-3f, $"coarse rate: «−» on position z = −10 ({z0} → {P().Z})");
            ui.Options.Set(CharaEditTab.AccessoryStepOptionPrefix + "1", "normal");
            // an axis-label drag: the drag's screen pixels × the step (HS2's amount), one entry; x counts like y and z (XQ14)
            int u8 = UndoCount(c);
            float zd = P().Z;
            pos.BeginDrag(2, 100);
            pos.DragTo(102);
            pos.DragTo(103);
            pos.EndHold(false);
            int pz = pos.ScreenPixels(3);
            c.Check(UndoCount(c) == u8 + 1 && pz > 0 && Mathf.Abs(P().Z - Mathf.Clamp(zd + pz * 1f, -100, 100)) < 1e-3f,
                $"a drag of 3 logical px ({pz} screen px) on the z label → + {pz} × 1, one entry ({zd} → {P().Z})");
            float xd = P().X;
            pos.BeginDrag(0, 100);
            pos.DragTo(102);
            pos.EndHold(false);
            int px2 = pos.ScreenPixels(2);
            c.Check(Mathf.Abs(P().X - Mathf.Clamp(xd + px2 * 1f, -100, 100)) < 1e-3f, $"a drag to the right on the position x label → + {px2} × 1, as y and z (XQ14; {xd} → {P().X})");
            float ry = R().Y;
            rot.BeginDrag(1, 100);
            rot.DragTo(102);
            rot.EndHold(false);
            int pr = rot.ScreenPixels(2);
            c.Check(Mathf.Abs(R().Y - (float)((ry + pr * 5) % 360)) < 1e-3f, $"a drag on the rotation y label → + {pr} × 5, wrapped ({ry} → {R().Y})");
            // the layout holds at this inspector width (no part under the next axis), and position x's ↺ is clicked through ui.input
            yield return PlayModeHarness.WaitFrames(2);
            var overlaps = MaAxisOverlaps(new[] { pos, rot, scl });
            var line = MfQ<VisualElement>(pos, "axes");
            int lines = line == null ? 0 : Enumerable.Range(0, 3).Select(i => Mathf.RoundToInt(MfQ<VisualElement>(pos, "field-" + pos.Path + "#" + i)?.worldBound.y ?? -1)).Distinct().Count();
            c.Check(overlaps.Count == 0, $"the transform rows' axes keep their parts at the inspector's width {pos.worldBound.width:0} px ({lines} line(s))" + (overlaps.Count > 0 ? "; " + string.Join(", ", overlaps.Take(6)) : ""));
            var rx = MfQ<VisualElement>(pos, "field-" + pos.Path + "#0")?.Q<Button>("reset");
            int ux = UndoCount(c);
            var pk = new VisualElement[1];
            yield return MaInputClick(c, rx, pk);
            c.Check(rx != null && pk[0] == rx && Mathf.Abs(P().X) < 1e-6f && UndoCount(c) == ux + 1 && c.UndoLabel == "Reset value",
                "↺ position x clicked through ui.input → 0, one entry \"" + c.UndoLabel + "\" (picked " + (pk[0]?.name ?? "none") + ")");
            // the narrow player inspector (247 logical px): the axes wrap instead of overlapping
            pos.style.width = 247 - 8;
            yield return PlayModeHarness.WaitFrames(3);
            var narrow = MaAxisOverlaps(new[] { pos });
            int nlines = Enumerable.Range(0, 3).Select(i => Mathf.RoundToInt(MfQ<VisualElement>(pos, "field-" + pos.Path + "#" + i)?.worldBound.y ?? -1)).Distinct().Count();
            c.Check(narrow.Count == 0, $"in a {pos.worldBound.width:0} px row the axes wrap ({nlines} line(s)) and keep their parts" + (narrow.Count > 0 ? "; " + string.Join(", ", narrow.Take(6)) : ""));
            pos.style.width = StyleKeyword.Null;
            yield return PlayModeHarness.WaitFrames(2);
            scl.Reset(0);
            c.Check(Mathf.Abs(S().X - 1f) < 1e-6f, "↺ scale x → 1");
            int u9 = UndoCount(c);
            var init = MfQ<Button>(tab, "acc-reset-move-" + MaNN(free0) + "-1");
            MaPress(init);
            yield return PlayModeHarness.WaitFrames(2);
            var mv = MaEntry(c, f, ag).Moves[0];
            c.Check(init != null && UndoCount(c) == u9 + 1 && mv.Position.X == 0 && mv.Position.Y == 0 && mv.Position.Z == 0 && mv.Rotation.Y == 0 && mv.Scale.X == 1 && mv.Scale.Y == 1 && mv.Scale.Z == 1,
                "«初期化» of set 1 → identity, one entry");
            pos.Field(1).SetValueWithoutNotify("");
        }

        // ---------------------------------------------------------------- handles (XU7)
        {
            var gz = ui.Viewport.Gizmo;
            var trf = MaSetTransform(c, f, free0, 0);
            yield return MaFrameOn(c, trf);
            ui.Tool = GizmoTool.Move;
            yield return PlayModeHarness.WaitFrames(2);
            MhBuildGizmo(c);
            var hs = gz.AccessoryHandleStates();
            var markers = hs.Where(h => (string)h["kind"] == "marker").ToList();
            int sets = (MaSetTransform(c, f, free0, 0) ? 1 : 0) + (MaSetTransform(c, f, free0, 1) ? 1 : 0);
            c.Check(markers.Count == sets && gz.AccessoryMarkersDrawn == sets && sets > 0 && MhHandles(c).Count(h => (string)h["kind"] == "marker") == sets,
                $"markers: one per set of the selected slot ({markers.Count}, drawn {gz.AccessoryMarkersDrawn}, sets {sets}; listed in gizmo.handles)");
            if (markers.Count > 0)
            {
                var mk = markers.First(h => (int)h["set"] == 0);
                string sub = (string)mk["sub"];
                var mp = MhPanel(mk);
                var swHit = Stopwatch.StartNew();
                MhPointer(c, "down", mp);
                yield return PlayModeHarness.WaitFrames(1);
                MhPointer(c, "up", mp);
                yield return PlayModeHarness.WaitFrames(3);
                c.Check(ui.SubSelection == sub && sub == StudioOps.AccessorySub(f, ag, 0) && CharaEditTab.Current == tab, "a marker pick (ui.input pointer) → sub-selection " + ui.SubSelection);
                c.Info($"XU7: marker hit test {gz.AccessoryHitMs:0.000} ms; marker at {mp}");
                MhBuildGizmo(c);
                hs = gz.AccessoryHandleStates();
                var yTip = hs.FirstOrDefault(h => (string)h["kind"] == "axis" && (string)h["axis"] == "y");
                c.Check(yTip != null && (string)yTip["tool"] == "move" && gz.ShownTool == GizmoTool.Move, "the move handle of set 1 (axis tips in gizmo.handles)");
                if (yTip != null)
                {
                    var before = MaEntry(c, f, ag).Moves[0].Position;
                    int uh = UndoCount(c);
                    var gp = MhPanel(yTip);
                    var dir = (gp - (Vector2)gz.ToPanel(gz.PivotWorld)).normalized;
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
                    var a = MaEntry(c, f, ag).Moves[0].Position;
                    bool onGrid = Math.Abs(a.Y * 10 - Math.Round(a.Y * 10)) < 1e-3;
                    c.Check(UndoCount(c) == uh + 1 && c.UndoLabel == "Edit accessory" && Mathf.Abs(a.Y - before.Y) > 1e-4f && onGrid && a.Y >= -100 && a.Y <= 100
                            && Mathf.Abs(a.X - before.X) < 1e-6f && Mathf.Abs(a.Z - before.Z) < 1e-6f,
                        $"a Y-arrow drag (ui.input, 3 moves) → one entry \"{c.UndoLabel}\", position ({a.X}, {a.Y}, {a.Z}) from y {before.Y}: on the 0.1 grid, x / z unchanged");
                    c.Info($"XU7 (BR-Chan, 353#0 set 1): handle drag frame (ui.input move incl. op + flush) median {Median(dragMs):0.00} ms, max {dragMs.DefaultIfEmpty(0).Max():0.00}; last AccessoryApply {gz.AccessoryDragMs:0.000} ms");
                    ui.Tool = GizmoTool.Rotate;
                    yield return PlayModeHarness.WaitFrames(2);
                    hs = gz.AccessoryHandleStates();
                    var xRing = hs.FirstOrDefault(h => (string)h["kind"] == "axis" && (string)h["axis"] == "x");
                    var hitX = xRing != null ? gz.HitTest(MhPanel(xRing)) : default;
                    c.Check(xRing != null && (string)xRing["tool"] == "rotate" && hitX.Kind == HandleKind.RotateRing && hitX.Axis == 0, "the rotate handle: the X ring point of gizmo.handles hits the X ring (" + hitX + ")");
                    if (xRing != null)
                    {
                        var rb = MaEntry(c, f, ag).Moves[0].Rotation;
                        int ur = UndoCount(c);
                        var rp = MhPanel(xRing);
                        MhPointer(c, "down", rp);
                        yield return PlayModeHarness.WaitFrames(1);
                        MhPointer(c, "move", rp + new Vector2(10, 10));
                        yield return PlayModeHarness.WaitFrames(1);
                        MhPointer(c, "move", rp + new Vector2(30, 30));
                        yield return PlayModeHarness.WaitFrames(1);
                        MhPointer(c, "up", rp + new Vector2(30, 30));
                        yield return PlayModeHarness.WaitFrames(3);
                        var ra = MaEntry(c, f, ag).Moves[0].Rotation;
                        c.Check(UndoCount(c) == ur + 1 && ra.X >= 0 && ra.X < 360 && Mathf.Abs(ra.X - Mathf.Round(ra.X)) < 1e-4f && Mathf.Abs(ra.X - rb.X) > 1e-4f
                                && Mathf.Abs(ra.Y - rb.Y) < 1e-6f && Mathf.Abs(ra.Z - rb.Z) < 1e-6f,
                            $"an X-ring drag → one entry, rotation x {rb.X} → {ra.X} (an integer in [0, 360)), y / z unchanged");
                    }
                    ui.Tool = GizmoTool.Move;
                }
                ui.Options.Set(AccHandlesOption, false);
                yield return PlayModeHarness.WaitFrames(2);
                MhBuildGizmo(c);
                int off = gz.AccessoryHandleStates().Count, drawnOff = gz.AccessoryMarkersDrawn;
                ui.Options.Set(AccHandlesOption, true);
                yield return PlayModeHarness.WaitFrames(2);
                MhBuildGizmo(c);
                c.Check(off == 0 && drawnOff == 0 && gz.AccessoryMarkersDrawn == sets, "maker.accessoryHandles off → no markers, on → back");
                var hbtn = MfQ<Button>(tab, "acc-handle-" + MaNN(free0) + "-1-rotate");
                MaPress(hbtn);
                yield return PlayModeHarness.WaitFrames(2);
                c.Check(hbtn != null && ui.SubSelection == StudioOps.AccessorySub(f, ag, 0) && ui.Tool == GizmoTool.Rotate, "the set head's «回転» button selects set 1's handle with the rotate tool");
                ui.SubSelection = null;
                ui.Tool = GizmoTool.Move;
            }
        }

        // ---------------------------------------------------------------- hair-type 351#40 (the hair colour section)
        int hairSlot = MaTable(c, f).LowestFree();
        yield return MaAdd(c, tab, f, hairSlot, 351, "illusion.hs2#40", nid);
        string ah = nid[0];
        tab = MaTab(c, f, tab);
        if (ah != null)
        {
            yield return MaWaitRows(c, tab, f, ah, hairSlot);
            MaCheckRows(c, tab, f, ah, hairSlot, "351#40");
            var hf = MaFacts(f, hairSlot);
            c.Check(hf != null && hf.TypeHair && MaSection(tab, hairSlot, "hairColour") && !MaSection(tab, hairSlot, "colour") && tab.AccessorySlots.Slots.First(s => s.Id == ah).HairType
                    && MhShown(MfQ<VisualElement>(tab, "acc-slot-" + MaNN(hairSlot))?.Q<Label>("hair")),
                "351#40 is hair-type: the hair colour section instead of the colour section, the slot row's hair mark");
            var back = c.App.Session.Model.Find(f).Appearance.Stored.Chara.Hair.Parts[0];
            var e0 = MaEntry(c, f, ah);
            bool backHair = Mathf.Abs(e0.Colors[0].Color.R - back.BaseColor.R) < 1e-5f && Mathf.Abs(e0.Colors[0].Smoothness - back.Smoothness) < 1e-5f
                && Mathf.Abs(e0.Colors[3].Color.G - back.Specular.G) < 1e-5f;
            // HS2 reaches a hair-type item of an empty slot through the type (row 0) and then the item pick: non-hair → hair-type
            // takes the back hair's colours (B's 25260ab after W's finding, decided)
            c.Check(backHair, $"a new hair-type accessory: colours 1 / 4 and smoothness = the back hair's (colour 1 R {e0.Colors[0].Color.R:0.###} vs {back.BaseColor.R:0.###})");
            // colour 1 set apart first, so that the fetch changes something even when the front hair's colours equal the back's
            ui.Ops.EditAppearance(new[] { f }, new List<(string, object)> { (MaPath(ah, "colors[0].color"), new Color4(0.123f, 0.456f, 0.789f, 1f)) }, UiContext.User(OpLabels.EditAccessory));
            yield return PlayModeHarness.WaitFrames(2);
            int uf = UndoCount(c);
            var fetch = MfQ<Button>(tab, "acc-hair-fetch-" + MaNN(hairSlot) + "-front");
            MaPress(fetch);
            yield return PlayModeHarness.WaitFrames(3);
            var front = c.App.Session.Model.Find(f).Appearance.Stored.Chara.Hair.Parts[1];
            var e1 = MaEntry(c, f, ah);
            c.Check(fetch != null && UndoCount(c) == uf + 1 && c.UndoLabel == "Edit accessory" && Mathf.Abs(e1.Colors[0].Color.G - front.BaseColor.G) < 1e-5f && Mathf.Abs(e1.Colors[3].Color.B - front.Specular.B) < 1e-5f,
                "«髪の毛の色を取得» «前髪» → colours 1 / 4 = the front hair's base / specular, one entry");
            var sm = MfQ<MakerSliderRow>(tab, "field-" + MaPath(ah, "colors[0].smoothness") + "@hair");
            sm?.Write(new[] { f }, 0.8, "ma.typed3");
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(sm != null && Mathf.Abs(MaEntry(c, f, ah).Colors[0].Smoothness - 0.8f) < 1e-6f, "«滑らかさ» typed 80 → stored 0.8 (field-…colors[0].smoothness@hair)");
            // live: an item change to a non-hair head item brings the colour section back after the flush
            var list = MfQ<PartList>(tab, "field-" + MaPath(ah, "item"));
            yield return MhSettle(c, tab);
            yield return PfCheck(c, list, "accessories", null);
            bool picked = list != null && list.Pick("illusion.hs2#0");
            var ok = new bool[1];
            yield return WaitFor(() => MaEntry(c, f, ah).Item == "illusion.hs2#0", 30, ok);
            yield return MaWaitRows(c, tab, f, ah, hairSlot);
            c.Check(picked && c.UndoLabel == "Change item" && !MaSection(tab, hairSlot, "hairColour") && MaFacts(f, hairSlot)?.TypeHair == false,
                "an item change to 351#0 (not hair-type) → the colour rows of the new prefab after the flush (live, \"" + c.UndoLabel + "\")");
            MaCheckRows(c, tab, f, ah, hairSlot, "351#0");
        }

        // ---------------------------------------------------------------- the type choice (353 → 352)
        {
            tab.SelectAccessory(ag);
            yield return PlayModeHarness.WaitFrames(3);
            int builds0 = tab.AccessoryGroupBuilds, ut = UndoCount(c);
            c.Check(Enumerable.Range(0, 14).All(k => MfQ<Button>(tab, "acc-type-" + MaNN(free0) + "-" + k) != null), "the type choice: 14 buttons acc-type-" + MaNN(free0) + "-0 … -13");
            var r = tab.ChangeAccessoryType(ag, 352);
            var ok = new bool[1];
            yield return WaitFor(() => MaEntry(c, f, ag)?.Category == 352 && tab.AccessoryGroupBuilds > builds0 && MaFacts(f, free0)?.Category == 352, 30, ok);
            yield return MaWaitRows(c, tab, f, ag, free0);
            var e = MaEntry(c, f, ag);
            var row0 = lists.Rows(352, 1).FirstOrDefault();
            bool identity = e.Moves.All(mv => mv.Position.X == 0 && mv.Position.Y == 0 && mv.Position.Z == 0 && mv.Rotation.X == 0 && mv.Rotation.Y == 0 && mv.Rotation.Z == 0 && mv.Scale.X == 1);
            c.Check(r.Ok && UndoCount(c) == ut + 1 && c.UndoLabel == "Change item" && e.Category == 352 && row0 != null && e.Item == row0.Key.ToString() && identity && !e.NoShake && tab.AccessoryGroupBuilds > builds0,
                "type «耳» → item = row 0 of 352 (" + e.Item + "), sets identity, «揺らさない» off, the group rebuilt, one entry \"" + c.UndoLabel + "\"");
            MaCheckRows(c, tab, f, ag, free0, "352 row 0");
        }

        // ---------------------------------------------------------------- the copy section (source = the 352 slot)
        {
            var tp = tab.AccessoryTargetPicker;
            var btn = tab.AccessoryCopyButtonsByName;
            c.Check(tp != null && btn.Count == 8 && InspectorSchemas.AccessoryCopyButtons.All(x => MfQ<Button>(tab, "acc-copy-" + x.name) != null) && MfQ<Toggle>(tab, "acc-copy-mirror") != null,
                "the copy section: the target slot picker (acc-target-slot), «左右の親を変更» (acc-copy-mirror) and the 8 buttons");
            if (tp != null && btn.Count == 8)
            {
                var pick = MfQ<AttachPointPicker>(tab, "field-" + MaPath(ag, "parent"));
                pick?.Pick("N_Earring_L");
                yield return PlayModeHarness.WaitFrames(3);
                tp.Choose(free0);
                c.Check(!btn["copyToSlot"].enabledSelf && btn["duplicate"].enabledSelf, "target = the source slot: «枠へコピー…» disabled, «複製» enabled");
                int t10 = Enumerable.Range(10, 10).First(s => MaTable(c, f).IsFree(s));
                tp.Choose(t10);
                c.Check(btn["copyToSlot"].enabledSelf && !btn["copyMove1"].enabledSelf && !btn["copyMove1LeftRight"].enabledSelf, "an empty target slot " + MaNN(t10) + ": «枠へコピー…» enabled, the adjustment copies disabled («" + btn["copyMove1"].tooltip + "»)");
                ui.Options.Set(InspectorSchemas.AccessoryMirrorParentOption, true);
                int uc = UndoCount(c);
                var r1 = tab.CopyAccessorySlot("copyToSlot");
                var ok = new bool[1];
                yield return WaitFor(() => MaTable(c, f).EntryAt(t10) != null, 30, ok);
                var te = MaTable(c, f).EntryAt(t10);
                yield return PlayModeHarness.WaitFrames(2);
                tab = MaTab(c, f, tab);
                tab.SelectAccessory(ag);
                yield return PlayModeHarness.WaitFrames(2);
                tp = tab.AccessoryTargetPicker;
                btn = tab.AccessoryCopyButtonsByName;
                c.Check(r1.Ok && te != null && te.Item == MaEntry(c, f, ag).Item && te.Parent == "N_Earring_R" && UndoCount(c) == uc + 1 && c.UndoLabel == "Copy accessory",
                    "«枠へコピー…» to the empty slot " + MaNN(t10) + " with «左右の親を変更» → a new accessory, parent N_Earring_R, one entry \"" + c.UndoLabel + "\"");
                ui.Options.Set(InspectorSchemas.AccessoryMirrorParentOption, false);
                yield return PlayModeHarness.WaitFrames(3);
                // the set mirror needs both prefabs with trfMove01
                var rotRow = MfQ<MakerVectorRow>(tab, "field-" + MaPath(ag, "moves[0].rotation"));
                rotRow?.Type(1, 30);
                tp.Choose(t10);
                yield return PlayModeHarness.WaitFrames(2);
                bool both = MaFacts(f, free0)?.Move1 == true && MaFacts(f, t10)?.Move1 == true;
                c.Check(btn["copyMove1LeftRight"].enabledSelf == both && btn["copyMove2"].enabledSelf == (MaFacts(f, free0)?.Move2 == true && MaFacts(f, t10)?.Move2 == true),
                    "the adjustment copies follow both prefabs' sets (set 1 " + both + ")");
                if (both)
                {
                    var r2 = tab.CopyAccessorySlot("copyMove1LeftRight");
                    yield return PlayModeHarness.WaitFrames(2);
                    var tr = MaTable(c, f).EntryAt(t10).Moves[0].Rotation;
                    c.Check(r2.Ok && Mathf.Abs(tr.Y - 210f) < 1e-4f && c.UndoLabel == "Copy accessory", "«調整①を左右反転» to slot " + MaNN(t10) + " after rotation y 30 → 210 (" + tr.Y + ")");
                }
                // an occupied target asks first
                var r3 = tab.CopyAccessorySlot("copyToSlot");
                yield return PlayModeHarness.WaitFrames(2);
                bool asked = ui.Dialogs != null && ui.Dialogs.IsModalOpen && ui.Dialogs.Current?.Spec.Id == "maker.acc.overwriteTitle";
                int uo = UndoCount(c);
                ui.Dialogs?.Press("ok");
                yield return PlayModeHarness.WaitFrames(3);
                c.Check(asked && UndoCount(c) == uo + 1 && MaTable(c, f).EntryAt(t10)?.Id == te.Id, "«枠へコピー…» onto an occupied slot asks «スロット NN の「…」を上書きしますか»; OK overwrites it (its id kept)");
                int free1 = MaTable(c, f).LowestFree();
                var r4 = tab.CopyAccessorySlot("duplicate");
                yield return WaitFor(() => MaTable(c, f).EntryAt(free1) != null, 30, ok);
                c.Check(r4.Ok && MaTable(c, f).EntryAt(free1)?.Item == MaEntry(c, f, ag).Item, "«複製» → the lowest free slot " + MaNN(free1));
                yield return PlayModeHarness.WaitFrames(2);
                tab = MaTab(c, f, tab);
                // «なし» on the copy: asks, then removes (one entry), the state key pruned
                tab.SelectAccessory(te.Id);
                yield return PlayModeHarness.WaitFrames(3);
                tab.ConfirmRemoveAccessory(te.Id);
                yield return PlayModeHarness.WaitFrames(2);
                bool askedRm = ui.Dialogs != null && ui.Dialogs.IsModalOpen && ui.Dialogs.Current?.Spec.Id == "maker.acc.removeTitle";
                int urm = UndoCount(c);
                ui.Dialogs?.Press("ok");
                yield return PlayModeHarness.WaitFrames(3);
                tab = MaTab(c, f, tab);
                bool pruned = (MaNode(c, f).State as CharaNodeState)?.State.Accessories.ContainsKey(te.Id) == false;
                c.Check(askedRm && UndoCount(c) == urm + 1 && c.UndoLabel == "Remove accessory" && MaEntry(c, f, te.Id) == null && pruned && tab.AccessoryShownId != te.Id,
                    "«なし» asks (" + askedRm + "), then removes: entries +" + (UndoCount(c) - urm) + " \"" + c.UndoLabel + "\", removed " + (MaEntry(c, f, te.Id) == null) + ", the state key pruned " + pruned + ", another slot selected (" + MaNN(tab.AccessoryShownSlot) + ")");
            }
        }

        // ---------------------------------------------------------------- slot switches, at most 8 groups, a slot ≥ 20, 20 worn slots
        {
            var all = MaTable(c, f).EntryBySlot.Values.Select(e => e.Id).ToList();
            foreach (var a in all) { tab.SelectAccessory(a); yield return PlayModeHarness.WaitFrames(1); }
            c.Check(tab.AccessoryGroupCount <= CharaEditTab.AccessoryGroupsKept, $"after selecting each of {all.Count} slots: {tab.AccessoryGroupCount} slot groups kept (≤ {CharaEditTab.AccessoryGroupsKept})");
            var switches = new List<(double ms, int gcs)>();
            for (int n = 0; n < 8; n++)
            {
                int g0 = GC.CollectionCount(0);
                tab.SelectAccessory(n % 2 == 0 ? all[all.Count - 1] : all[all.Count - 2]);
                int g = GC.CollectionCount(0) - g0;
                yield return PlayModeHarness.WaitFrames(1);
                if (n >= 2) switches.Add((tab.AccessorySlotSwitchMs, g));
            }
            TimedMedian(c, "XU4: cached slot switch", switches, AccSwitchTargetMs);
            // slot 21 (index 20) and up to 20 worn slots through the op (one cheap item), then the list rebuild
            int ub = UndoCount(c);
            var r20 = ui.Ops.AddAccessory(f, 352, "illusion.hs2#0", null, null, UiContext.User(OpLabels.AddAccessory), 20);
            while (MaTable(c, f).Count < 20)
            {
                var rr = ui.Ops.AddAccessory(f, 352, "illusion.hs2#0", null, null, UiContext.User(OpLabels.AddAccessory), null);
                if (!rr.Ok) break;
            }
            var ok = new bool[1];
            yield return PlayModeHarness.WaitFrames(3);
            tab = MaTab(c, f, tab);
            yield return WaitFor(() => tab.AccessorySlots.Slots.Count == MaTable(c, f).Count, 30, ok);
            yield return PlayModeHarness.WaitFrames(3);
            var s20 = tab.AccessorySlots.Slots.FirstOrDefault(s => s.Slot == 20);
            bool extraOn = MakerServices.MoreAccessoriesLoaded?.Invoke() ?? true;
            var row21 = MfQ<VisualElement>(tab, "acc-slot-21");
            c.Check(r20.Ok && s20 != null && row21?.Q<Label>("badge")?.text == "21" && s20.NotLoaded == !extraOn && MhShown(row21?.Q<Label>("notLoaded")) == !extraOn,
                "a slot ≥ 20 is listed as «21», the MoreAccessories badge follows the switch (on: " + extraOn + ")");
            var sw = Stopwatch.StartNew();
            tab.AccessorySlots.SetSlots(tab.AccessorySlots.Slots.ToList(), tab.AccessorySlots.SelectedId);
            double rebuild = tab.AccessorySlots.LastRebuildMs;
            c.Check(tab.AccessorySlots.Slots.Count >= 20 && rebuild <= 2 * AccListTargetMs, $"XU4: the slot list rebuild with {tab.AccessorySlots.Slots.Count} worn slots {rebuild:0.000} ms (≤ {2 * AccListTargetMs})");
            if (rebuild > AccListTargetMs) c.Warning($"XU4: slot list rebuild {rebuild:0.00} ms above the {AccListTargetMs} ms target");
            yield return MfUndoTo(c, ub);
            tab = MaTab(c, f, tab);
            yield return WaitFor(() => tab.AccessorySlots.Slots.Count == MaTable(c, f).Count, 30, ok);
        }

        // ---------------------------------------------------------------- edit.show {slot | section}, the State tab labels
        {
            var worn2 = MaTable(c, f).EntryBySlot.Keys.ToList();
            int sA = worn2[worn2.Count - 1], sB = worn2[0];
            ui.Registry.Actions.Run(ActionIds.EditShow, new ActionInvocation { Args = new Dictionary<string, object> { ["node"] = "accessories", ["slot"] = sA } }, out var res);
            yield return PlayModeHarness.WaitFrames(3);
            var et = CharaEditTab.Current ?? tab;
            var d = res as Dictionary<string, object>;
            c.Check(et.AccessoryShownSlot == sA && d != null && d.TryGetValue("slot", out var sv) && Convert.ToInt32(sv) == sA,
                "edit.show {node: accessories, slot: " + sA + "} shows slot " + MaNN(sA) + " (reply slot " + (d != null && d.TryGetValue("slot", out var s2) ? s2 : "-") + ")");
            ui.Registry.Actions.Run(ActionIds.EditShow, new ActionInvocation { Args = new Dictionary<string, object> { ["node"] = "accessories", ["section"] = InspectorSchemas.AccessorySectionId(sB, "parent") } }, out _);
            yield return PlayModeHarness.WaitFrames(3);
            c.Check(et.AccessoryShownSlot == sB, "edit.show {section: " + InspectorSchemas.AccessorySectionId(sB, "parent") + "} selects slot " + MaNN(sB));
            var insp = Inspector(c);
            insp?.Refresh(true);
            insp?.Chara?.ShowTab("state");
            yield return PlayModeHarness.WaitFrames(3);
            var labels = worn2.Select(s => { var id2 = MaTable(c, f).EntryAt(s).Id; var row = insp?.Chara?.Q<VisualElement>("accessory-" + id2); return (s, text: row?.Query<Label>().ToList().Select(x => x.text).FirstOrDefault(x => x != null && x.Contains(MaNN(s)))); }).ToList();
            c.Check(insp?.Chara != null && labels.All(x => x.text != null), "the State tab labels each accessory with its build slot (" + string.Join(", ", labels.Select(x => MaNN(x.s) + (x.text != null ? " ok" : " missing"))) + ")");
            tab = MfOpenEdit(c, f) ?? tab;
            tab.ShowNode("accessories");
            yield return PlayModeHarness.WaitFrames(3);
        }

        // ---------------------------------------------------------------- multi-selection disables the page
        {
            ui.Ops.Select(new[] { f, m });
            yield return PlayModeHarness.WaitFrames(3);
            var et = CharaEditTab.Current ?? tab;
            bool disabled = et.AccessoriesMultiDisabled && MhShown(MfQ<VisualElement>(et, "acc-multi-note")) && MfQ<VisualElement>(et, "acc-page-body")?.enabledSelf == false;
            c.Check(et.CurrentNode != "accessories" || disabled, "two characters selected: the page is disabled with «アクセサリは 1 体ずつ編集します» (" + et.CurrentNode + ", " + disabled + ")");
            ui.Ops.Select(new[] { f });
            yield return PlayModeHarness.WaitFrames(3);
            tab = MfOpenEdit(c, f) ?? tab;
            tab.ShowNode("accessories");
            yield return PlayModeHarness.WaitFrames(3);
            c.Check(!tab.AccessoriesMultiDisabled, "one character selected again: the page is enabled");
        }

        // ---------------------------------------------------------------- FU2: the list of accessories changes, the inspector stays
        yield return MaFu2(c, f, m);
        tab = MfOpenEdit(c, f) ?? tab;
        tab.ShowNode("accessories");
        yield return PlayModeHarness.WaitFrames(3);

        // ---------------------------------------------------------------- XU6: the 351 list scrolled (thumbnails, after the guard records)
        {
            tab.OpenAccessoryAdd(true);
            var list = tab.ShowAccessoryAddList(351);
            yield return PlayModeHarness.WaitFrames(3);
            if (list != null)
            {
                int open0 = cat.OpenBundleCount;
                list.SetExpanded(true);
                yield return PlayModeHarness.WaitFrames(3);
                yield return McScroll(c, list, "accessory list 351 (" + list.Items.Count + " rows)");
                var tpx = list.Thumbnails;
                int pendingPeak = tpx?.Pending ?? 0;
                var okp = new bool[1];
                yield return WaitFor(() => tpx == null || tpx.Pending == 0, 90, okp);
                c.Check(okp[0], "the 351 list's queued thumbnails finished (" + tpx?.Pending + " pending; " + pendingPeak + " after the scroll)");
                list.SetExpanded(false);
                c.Info($"XU6: thumbnails guard-skipped {(tpx?.GuardSkipped ?? 0) - guard0}, mip<1 rejected {(tpx?.BadMipRejected ?? 0) - badMip0} during MA (totals {tpx?.GuardSkipped} / {tpx?.BadMipRejected}); from bundles {tpx?.FromBundles}, disk {tpx?.FromDiskCount}, loose {tpx?.FromLoose}, failed {tpx?.FailedCount}; open bundles {open0} → {cat.OpenBundleCount}");
                c.Check((tpx?.BadMipRejected ?? 0) - badMip0 == 0, "XU6: no mip-less thumbnail reached a load in MA (mip<1 rejected " + ((tpx?.BadMipRejected ?? 0) - badMip0) + ")");
            }
            tab.OpenAccessoryAdd(false);
        }

        // ---------------------------------------------------------------- male (HERO107): its slots, the head-weighted row 351#202
        {
            var tm = MfOpenEdit(c, m);
            yield return PlayModeHarness.WaitFrames(4);
            tm?.ShowNode("accessories");
            yield return PlayModeHarness.WaitFrames(3);
            var mt = MaTable(c, m);
            c.Check(tm != null && tm.AccessorySlots != null && tm.AccessorySlots.Slots.Select(s => s.Slot).SequenceEqual(mt.EntryBySlot.Keys), "HERO107: the slot list = its slot table (" + mt.Count + " worn)");
            if (tm != null)
            {
                int ms = mt.LowestFree();
                yield return MaAdd(c, tm, m, ms, 351, "illusion.hs2#202", nid);
                string hw = nid[0];
                tm = MaTab(c, m, tm);
                if (hw != null)
                {
                    yield return MaWaitRows(c, tm, m, hw, ms);
                    var hf = MaFacts(m, ms);
                    c.Check(hf != null && hf.HeadWeighted && MhShown(MfQ<VisualElement>(tm, "acc-parent-head-note")) && MaShown(tm, hw, "parent"),
                        "351#202 (Parent \"null\"): the parent section with the head-weighted note");
                    var pick = MfQ<AttachPointPicker>(tm, "field-" + MaPath(hw, "parent"));
                    pick?.Pick("N_Head_top");
                    yield return PlayModeHarness.WaitFrames(3);
                    c.Check(MaEntry(c, m, hw).Parent == "N_Head_top", "a picked key «帽子» is stored (N_Head_top; the mesh stays on the head bones)");
                }
            }
        }

        yield return MfUndoTo(c, u00);
        ui.Tool = tool0;
        ui.SubSelection = null;
        c.Info($"end: open bundles {cat.OpenBundleCount}, pending thumbnails {pt?.Pending}, guard-skipped {pt?.GuardSkipped}, mip<1 rejected {pt?.BadMipRejected}");
    }
}
