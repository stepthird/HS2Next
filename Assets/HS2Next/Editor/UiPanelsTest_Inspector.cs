// UiPanelsTest.play, package I's groups: the inspector binds every schema row of character / item /
// light / camera / scene, editable (value set through the widget -> model), with the multi-selection mixed display and
// apply-to-all:
//   I character inspector: nine tabs; every visible, enabled FieldRow written through its widget path
//     (FieldWidgets.Write) -> model value, exactly one history entry with the row's label, undo restores the value;
//     clothes rows (supported states enabled, numbers only), accessories per worn slot, face / hands index rows (wrap),
//     FK on -> bone rows, the anime copy / paste buttons;
//   J item / light / camera / folder inspectors: the same row loop; item emission picker stores alpha 1 (T5);
//     light range rows by type; camera "set from view" and "look through";
//   M multi-selection: mixed display ("—"), one write to both characters, one entry; sub-selection opens FK / IK and marks
//     the IK target row; transform typed on a mixed selection keeps the other components;
//   S scene panel: the row loop over map / effects / chara light / sound rows, block reset, camera slot save / load marks,
//     the node eyedropper.
// Characters: the SFW targets of Tools/targets.json; clothes half / off states are checked by numbers only (no capture).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;
using UiField = HS2Next.UI.Core.FieldSpec;

public static partial class UiPanelsTest
{
    static partial void AddMoreGroups()
    {
        Groups.Add(("I", InspectorCharaGroup));
        Groups.Add(("J", InspectorKindsGroup));
        Groups.Add(("M", InspectorMultiGroup));
        Groups.Add(("S", ScenePanelGroup));
    }

    // ---------------------------------------------------------------- helpers

    static InspectorPanel Inspector(Ctx c) => Panel<InspectorPanel>(c, "inspector");

    static IEnumerator AddBuiltChara(Ctx c, string card, List<string> into)
    {
        var r = c.Ui.Ops.AddChara(card, null, null, -1, null, User);
        string id = r.NewIds.FirstOrDefault();
        var ok = new bool[1];
        yield return WaitFor(() => id != null && c.App.Runtime.FindChara(id)?.State == RealizationState.Built, 120, ok);
        c.Check(r.Ok && ok[0], "character added and built: " + System.IO.Path.GetFileName(card) + " (" + (r.Message ?? c.App.Runtime.FindChara(id ?? "")?.State.ToString()) + ")");
        if (id != null) into.Add(id);
    }

    static int UndoCount(Ctx c) => c.App.Session.Commands.UndoList.Count;

    static string Json(Ctx c, string id, string path) => FieldAccessor.TryGet(c.App.Session.Model, id, path, out var v, out _) ? InspectorValues.Text(v) : "<none>";

    /// <summary>A test value for a row (widget-side value) and the model check; false = the row is not testable here.</summary>
    static bool TestValue(Ctx c, FieldRow row, string id, out object widget, out Func<object, bool> expect, out string why)
    {
        widget = null; expect = null; why = null;
        var f = row.Spec;
        var m = c.App.Session.Model;
        string target = f.Target == FieldTarget.Scene ? null : id;
        FieldAccessor.TryGet(m, target, row.Path, out var cur, out _);
        if (FieldValue.IsAbsent(cur)) cur = null;
        switch (f.Kind)
        {
            case FieldKind.Float:
            case FieldKind.Int:
            {
                double lo = f.Min ?? f.TypedMin ?? 0, hi = f.Max ?? f.TypedMax ?? 1;
                if (!(hi > lo)) { why = "no range"; return false; }
                double now = f.Component >= 0 ? InspectorValues.Component(cur, f.Component) : InspectorValues.D(cur);
                double v = lo + 0.37 * (hi - lo);
                if (f.Whole) v = Math.Round(v);
                if (Math.Abs(v - now) < 1e-4) { v = lo + 0.63 * (hi - lo); if (f.Whole) v = Math.Round(v); }
                widget = v;
                int comp = f.Component;
                double want = v;
                expect = x => Math.Abs((comp >= 0 ? InspectorValues.Component(x, comp) : InspectorValues.D(x)) - want) < 1e-3;
                return true;
            }
            case FieldKind.Bool:
            {
                bool now = cur is bool b && b;
                widget = !now;
                expect = x => x is bool b2 && b2 == !now;
                return true;
            }
            case FieldKind.Enum:
            {
                if (f.Choices == null || f.Choices.Count < 2) { why = "one choice"; return false; }
                bool stringEnum = cur is string;
                int ci = 0;
                for (int i = 0; i < f.Choices.Count; i++) if (InspectorValues.Text(f.ChoiceValue(i, stringEnum)) == InspectorValues.Text(cur)) ci = i;
                int ni = (ci + 1) % f.Choices.Count;
                var v = f.ChoiceValue(ni, stringEnum);
                widget = v;
                string want = InspectorValues.Text(v);
                expect = x => InspectorValues.Text(x) == want;
                return true;
            }
            case FieldKind.Vec3:
            {
                var v = InspectorValues.V3(cur);
                var nv = new Vector4(v.X + 0.1f, v.Y + 0.2f, v.Z + 0.3f, 0f);
                if (row.Path == "transform.scale") nv = new Vector4(v.X * 1.1f, v.Y * 1.2f, v.Z * 1.3f, 0f);
                widget = nv;
                expect = x => { var a = InspectorValues.V3(x); return Math.Abs(a.X - nv.x) < 1e-3 && Math.Abs(a.Y - nv.y) < 1e-3 && Math.Abs(a.Z - nv.z) < 1e-3; };
                return true;
            }
            case FieldKind.Color:
            {
                var col = new Color4(0.2f, 0.4f, 0.6f, f.ColorAlpha ? 0.5f : 1f);
                widget = col;
                expect = x => { var a = InspectorValues.C4(x); return Math.Abs(a.R - 0.2f) < 1e-3 && Math.Abs(a.G - 0.4f) < 1e-3 && Math.Abs(a.B - 0.6f) < 1e-3; };
                return true;
            }
            case FieldKind.Ref:
            {
                if (row.Path == "map.ref") { why = "map load (heavy; maps have their own tests)"; return false; }
                var ch = InspectorSetup.RefChoices(c.Ui, f).Where(x => x.value != null && x.value != cur as string).ToList();
                if (ch.Count == 0) { why = "no choices"; return false; }
                string v = ch[0].value;
                widget = v;
                expect = x => x as string == v;
                return true;
            }
            case FieldKind.Node:
            {
                var n = m.PreOrder().FirstOrDefault();
                if (n == null) { why = "no node"; return false; }
                widget = n.Id;
                expect = x => x as string == n.Id;
                return true;
            }
            case FieldKind.Text:
            {
                widget = "inspector test";
                expect = x => x as string == "inspector test";
                return true;
            }
        }
        why = f.Kind.ToString();
        return false;
    }

    /// <summary>Every visible, enabled FieldRow under <paramref name="root"/>: write through the widget path, one entry with the row's label, undo restores.</summary>
    static void RowLoop(Ctx c, VisualElement root, string id, string tag, HashSet<string> skipPaths = null)
    {
        var rows = root.Query<FieldRow>().ToList();
        int tested = 0, skipped = 0;
        var skips = new List<string>();
        foreach (var row in rows)
        {
            var f = row.Spec;
            if (f == null || f.Kind == FieldKind.Button || f.Target == FieldTarget.Option) { skipped++; continue; }
            if (row.style.display.value == DisplayStyle.None || !row.enabledSelf) { skipped++; skips.Add(row.Path + " (hidden / disabled)"); continue; }
            if (skipPaths != null && skipPaths.Contains(row.Path)) { skipped++; continue; }
            if (!TestValue(c, row, id, out var widget, out var expect, out var why)) { skipped++; skips.Add(row.Path + " (" + why + ")"); continue; }
            string target = f.Target == FieldTarget.Scene ? null : id;
            string before = Json(c, target, row.Path);
            int n0 = UndoCount(c);
            var r = FieldWidgets.Write(row, widget, EditPhase.Commit);
            FieldAccessor.TryGet(c.App.Session.Model, target, row.Path, out var after, out _);
            int n1 = UndoCount(c);
            string label = c.UndoLabel;
            bool labelOk = string.IsNullOrEmpty(f.OpLabel) || label == f.OpLabel;
            bool ok = r != null && r.Ok && expect(after) && n1 == n0 + 1 && labelOk;
            string restored = "";
            if (n1 > n0)
            {
                c.App.Session.Commands.Undo();
                restored = Json(c, target, row.Path) == before ? "" : " (undo did not restore: " + Json(c, target, row.Path) + " vs " + before + ")";
            }
            c.Check(ok && restored.Length == 0, tag + " row " + row.Path + (f.Component >= 0 ? "#" + f.Component : "") + ": widget -> model " + InspectorValues.Text(after) +
                ", entries +" + (n1 - n0) + " \"" + label + "\"" + (labelOk ? "" : " (want \"" + f.OpLabel + "\")") + (r != null && !r.Ok ? " " + r.Code + ": " + r.Message : "") + restored);
            tested++;
        }
        c.Info(tag + ": " + tested + " rows written through their widgets, " + skipped + " skipped" + (skips.Count > 0 ? " (" + string.Join("; ", skips.Take(30)) + ")" : ""));
    }

    // ---------------------------------------------------------------- I: character inspector

    static IEnumerator InspectorCharaGroup(Ctx c)
    {
        var ui = c.Ui;
        yield return NewScene(c);
        var ids = new List<string>();
        yield return AddBuiltChara(c, c.FemaleCard, ids);
        if (ids.Count == 0) yield break;
        string id = ids[0];
        ui.Ops.Select(new[] { id });
        yield return PlayModeHarness.WaitFrames(3);
        var insp = Inspector(c);
        c.Check(insp != null, "inspector panel registered and built");
        if (insp == null) yield break;
        insp.Refresh(true);
        c.Check(insp.PrimaryId == id && insp.Kind == NodeKind.Chara && insp.Chara != null, "inspector shows the character");
        var ch = insp.Chara;
        c.Check(ch.TabBodies.Count == InspectorSchemas.Chara.Count + 2 && ch.TabBodies.ContainsKey(CharaEditTab.TabId) && ch.TabBodies.ContainsKey(CharaCostumeTab.TabId) && InspectorSchemas.Chara.All(t => ch.TabBodies.ContainsKey(t.Id)), "nine tabs + Costume + Edit (" + string.Join(", ", ch.TabBodies.Keys) + ")");
        c.Check(insp.Q<VisualElement>("section-transform") != null && insp.Q<Label>("kind") != null, "header and Transform section");
        // a fresh build of a character whose realization is read during the build: exactly one copy of each part
        ui.ShowResult(ui.Ops.SetClothesAll(new[] { id }, 0, User));
        insp.Refresh(true);
        c.Check(insp.Query<CharaInspector>().ToList().Count == 1 && insp.Query<VisualElement>("inspector-header").ToList().Count == 1,
            "one character inspector and one header after a rebuild (re-entrant refresh deferred)");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        insp.Refresh(true);
        c.Info("character inspector build " + sw.Elapsed.TotalMilliseconds.ToString("0.0") + " ms (" + insp.Query<FieldRow>().ToList().Count + " rows)");

        // the row loop on the header, transform and every tab (rows rebuilt by FK / IK toggles are re-queried per tab)
        RowLoop(c, insp.Q<VisualElement>("inspector-header"), id, "header");
        RowLoop(c, insp.Q<VisualElement>("section-transform"), id, "transform");
        foreach (var tab in InspectorSchemas.Chara)
        {
            insp.Refresh(true);
            var body = insp.Chara?.TabBodies[tab.Id];
            if (body != null) RowLoop(c, body, id, "tab " + tab.Id);
            yield return null;
        }
        insp.Refresh(true);
        ch = insp.Chara;

        // clothes rows: supported states only (numbers only, never a capture)
        var set = ui.Ops.ClothesStatesOf(id);
        var crow = ch.TabBodies["state"].Query<ClothesRow>().ToList();
        int kinds = Enumerable.Range(0, 8).Count(k => set.IsStateKind(k));
        c.Check(crow.Count == 8, "eight clothes rows (" + crow.Count + "), state kinds " + kinds);
        var top = crow.FirstOrDefault(r => r.name == "field-chara.state.clothes[0]");
        if (top != null && set.IsStateKind(0))
        {
            bool en = top.Buttons.Buttons.Select((b, i) => b.enabledSelf == set.Supports(0, i)).All(x => x);
            c.Check(en, "top row enables exactly the supported states");
            int n0 = UndoCount(c);
            top.Set(1);
            var st = (ui.Model.Find(id).State as CharaNodeState).State.Clothes;
            c.Check(top.LastResult.Ok && st[0] == set.Apply(new int[8], 0, 1)[0] && UndoCount(c) == n0 + 1 && c.UndoLabel == OpLabels.Clothes,
                "top half through the row -> stored " + st[0] + ", entry \"" + c.UndoLabel + "\"");
            ui.Session.Commands.Undo();
        }

        // accessories: one row per worn slot
        var acc = StudioOps.AccessoryIdsOf(ui.Model.Find(id));
        int accRows = ch.TabBodies["state"].Query<FieldRow>().Where(r => r.name.StartsWith("accessory-", StringComparison.Ordinal)).ToList().Count;
        c.Check(acc == null ? accRows == 0 : accRows == acc.Count, "accessory rows = worn slots (" + accRows + " / " + (acc?.Count ?? 0) + ")");

        // index rows: eyebrow by list position with wrap, hands
        var eb = ch.TabBodies["face"].Query<IndexRow>().Where(r => r.name == "field-chara.face.eyebrow").First();
        if (eb != null)
        {
            var lists = ui.Ops.ListsOf(ui.Model.Find(id));
            eb.Set(0);
            eb.Step(-1);
            int stored = (ui.Model.Find(id).State as CharaNodeState).Face.Eyebrow;
            c.Check(stored == lists.Eyebrow[lists.Eyebrow.Length - 1] && eb.Value == lists.Eyebrow.Length - 1,
                "eyebrow ◀ at position 0 wraps to the last list entry (id " + stored + ", position " + eb.Value + ")");
            eb.Step(+1);
            stored = (ui.Model.Find(id).State as CharaNodeState).Face.Eyebrow;
            c.Check(stored == lists.Eyebrow[0] && c.UndoLabel == OpLabels.Face, "▶ wraps to position 0 (id " + stored + "), entry \"" + c.UndoLabel + "\"");
        }
        var hand = ch.TabBodies["hands"].Query<IndexRow>().Where(r => r.name == "field-chara.hands[1]").First();
        if (hand != null)
        {
            hand.Set(3);
            var hs = (ui.Model.Find(id).State as CharaNodeState).Hands;
            c.Check(hs[1] == 3 && hs[0] == 0 && c.UndoLabel == OpLabels.Hands, "right hand pattern 3 through the row, left kept (" + hs[0] + ", " + hs[1] + ")");
        }

        // FK on -> bone rows of the applied groups
        int before = UndoCount(c);
        ui.ShowResult(ui.Ops.SetFkEnabled(id, true, User));
        yield return PlayModeHarness.WaitFrames(2);
        insp.Refresh();
        ch = insp.Chara;
        int bones = ch.TabBodies["kinematics"].Query<FieldRow>().Where(r => r.name.StartsWith("fk-", StringComparison.Ordinal)).ToList().Count;
        c.Check(bones > 0, "FK on -> " + bones + " FK bone rows");
        var boneRow = ch.TabBodies["kinematics"].Query<FieldRow>().Where(r => r.name.StartsWith("fk-", StringComparison.Ordinal)).First();
        if (boneRow != null)
        {
            var w = FieldWidgets.Write(boneRow, new Vector4(5, 6, 7, 0), EditPhase.Commit);
            var fk = (ui.Model.Find(id).State as CharaNodeState).Fk;
            string key = boneRow.name.Substring(3);
            c.Check(w.Ok && fk.Bones.TryGetValue(key, out var bv) && Math.Abs(bv.X - 5) < 1e-3 && c.UndoLabel == OpLabels.FkBones, "FK bone " + key + " through its row, entry \"" + c.UndoLabel + "\"");
        }
        // IK on as well: the bones the solve owns carry the "IK" mark
        ui.ShowResult(ui.Ops.SetIkEnabled(id, true, User));
        yield return PlayModeHarness.WaitFrames(3);
        insp.Refresh();
        int marks = insp.Chara.TabBodies["kinematics"].Query<FieldRow>().Where(r => r.name.StartsWith("fk-", StringComparison.Ordinal) && r.Q<Label>(className: "hs-row__badge") != null).ToList().Count;
        var ikc = HS2Next.Scenes.Runtime.IkDriver.Of(ui.Runtime.FindChara(id));
        c.Check(ikc == null || ikc.Owned.Count == 0 || marks > 0, "FK + IK on: " + marks + " FK bone rows marked IK-driven (owned " + (ikc?.Owned.Count ?? 0) + ")");
        while (UndoCount(c) > before) ui.Session.Commands.Undo();
        insp.Refresh();
        ch = insp.Chara;

        // IK target rows: the effective target shown while none is stored; a typed component stores the target
        var ikRow = ch.TabBodies["kinematics"].Query<IkTargetRow>().Where(r => r.name == "field-chara.ik.targets.LeftHand.pos").First();
        if (ikRow != null)
        {
            var shown = ikRow.ValueOf(id);
            int n2 = UndoCount(c);
            var wr = ikRow.Write(new Vector4(0, shown.y + 0.25f, 0, 0), 1);
            var t = (ui.Model.Find(id).State as CharaNodeState).Ik.Targets[(int)HS2Next.Motion.Ik.IkTargetId.LeftHand];
            c.Check(wr.Ok && t != null && Math.Abs(t.Pos.Y - (shown.y + 0.25f)) < 1e-3 && Math.Abs(t.Pos.X - shown.x) < 1e-3 && UndoCount(c) == n2 + 1 && c.UndoLabel == OpLabels.IkTargets,
                "IK LeftHand pos: typed Y stored (" + (t == null ? "null" : t.Pos.X + ", " + t.Pos.Y) + "), X kept from the shown value, entry \"" + c.UndoLabel + "\"");
            if (UndoCount(c) > n2) ui.Session.Commands.Undo();
        }
        else c.Check(false, "IK target row LeftHand.pos");

        // look target: shown with eye pattern 4 (target), typed in the character's local space
        ui.ShowResult(ui.Ops.SetLook(new[] { id }, 4, null, User));
        yield return PlayModeHarness.WaitFrames(2);
        insp.Refresh(true);
        ch = insp.Chara;
        var lookRow = ch.TabBodies["look"].Query<FieldRow>().Where(r => r.Path == "chara.look.target").First();
        c.Check(lookRow != null && lookRow.style.display.value != DisplayStyle.None, "look target row shown with eye pattern 4");
        if (lookRow != null)
        {
            int n3 = UndoCount(c);
            var lw = FieldWidgets.Write(lookRow, new Vector4(0.1f, 0.5f, 1.5f, 0), EditPhase.Commit);
            var lt = (ui.Model.Find(id).State as CharaNodeState).Look.Target;
            c.Check(lw.Ok && Math.Abs(lt.X - 0.1f) < 1e-3 && Math.Abs(lt.Y - 0.5f) < 1e-3 && Math.Abs(lt.Z - 1.5f) < 1e-3 && UndoCount(c) == n3 + 1 && c.UndoLabel == OpLabels.LookTarget,
                "look target typed local -> stored (" + lt.X + ", " + lt.Y + ", " + lt.Z + "), entry \"" + c.UndoLabel + "\"" + (lw.Ok ? "" : " " + lw.Message));
        }

        // anime copy / paste through the buttons (targets row run)
        var st0 = ui.Model.Find(id).State as CharaNodeState;
        var copy = ButtonActions.Run(ui, "ui.anime.copy", new[] { id });
        c.Check(copy.Ok && InspectorWrites.CopiedAnime != null && InspectorWrites.CopiedAnime.No == st0.Anime.Ref.No, "animation Copy remembers the primary's clip");
        // pose save through the flow (no dialog): file written under <UserData>/pose
        string posePath = System.IO.Path.Combine(c.App.Paths.PoseDir, "inspector_test.hs2npose");
        var saved = InspectorFlows.WritePose(ui, id, posePath, "inspector test", true);
        c.Check(saved.Ok && System.IO.File.Exists(posePath), "pose save writes " + System.IO.Path.GetFileName(posePath) + " (" + saved.Error + ")");
        // rows on B's 5ab12a5 ops: lip sync and hips offset are in the row loop above; hips position, save character here
        insp.Refresh(true);
        ch = insp.Chara;
        var lip = ch.TabBodies["face"].Query<FieldRow>().Where(r => r.Path == "chara.face.lipSync").First();
        var off = ch.TabBodies["kinematics"].Query<FieldRow>().Where(r => r.Path == "chara.fk.hipsOffset").First();
        c.Check(lip != null && lip.enabledSelf && off != null && off.enabledSelf, "lip sync and hips offset rows enabled");
        var hips = ch.TabBodies["kinematics"].Q<HipsPositionRow>();
        if (hips != null)
        {
            var shownHips = hips.ValueOf(id);
            int n4 = UndoCount(c);
            var hw = hips.Write(new Vector4(0, shownHips.y + 0.5f, 0, 0), 1);
            var fkst = (ui.Model.Find(id).State as CharaNodeState).Fk;
            c.Check(hw.Ok && fkst.HipsPosition is Vec3 hp && Math.Abs(hp.Y - (shownHips.y + 0.5f)) < 1e-4 && Math.Abs(hp.X - shownHips.x) < 1e-4 && UndoCount(c) == n4 + 1 && c.UndoLabel == OpLabels.FkBones,
                "hips position: typed Y stores the override (" + (fkst.HipsPosition?.Y.ToString() ?? "null") + "), X kept, entry \"" + c.UndoLabel + "\"");
            var hc = hips.Write(null);
            c.Check(hc.Ok && (ui.Model.Find(id).State as CharaNodeState).Fk.HipsPosition == null && UndoCount(c) == n4 + 2, "hips position Clear -> null, one entry");
            ui.Session.Commands.Undo();
            ui.Session.Commands.Undo();
        }
        else c.Check(false, "hips position row");
        string charaPath = System.IO.Path.Combine(c.App.Paths.CharaDir, "inspector_test" + StudioOps.CharaFileExtension);
        int n5 = UndoCount(c);
        var sc = InspectorFlows.WriteChara(ui, id, charaPath);
        c.Check(sc.Ok && System.IO.File.Exists(charaPath) && new System.IO.FileInfo(charaPath).Length > 0 && UndoCount(c) == n5,
            "save character writes " + System.IO.Path.GetFileName(charaPath) + " (" + (sc.Ok ? new System.IO.FileInfo(charaPath).Length + " bytes" : sc.Code + ": " + sc.Message) + "), not recorded");
        try { System.IO.File.Delete(charaPath); } catch (Exception) { }
    }

    // ---------------------------------------------------------------- J: item, light, camera, folder

    static IEnumerator InspectorKindsGroup(Ctx c)
    {
        var ui = c.Ui;
        yield return NewScene(c);
        var insp = Inspector(c);
        if (insp == null) { c.Check(false, "inspector panel"); yield break; }

        // item
        var ir = ui.Ops.AddItem(StudioDb.FallbackItem, null, null, -1, User);
        string item = ir.NewIds.FirstOrDefault();
        var ok = new bool[1];
        yield return WaitFor(() => item != null && ui.Runtime.FindItem(item)?.State == RealizationState.Built, 60, ok);
        c.Check(ir.Ok && ok[0], "item added and built");
        ui.Ops.Select(new[] { item });
        insp.Refresh(true);
        c.Check(insp.Kind == NodeKind.Item && insp.Q<VisualElement>("section-colors") != null && insp.Q<VisualElement>("section-motion") != null, "item foldouts");
        RowLoop(c, insp, item, "item");
        var feat = InspectorGates.Features(ui.Runtime, item);
        var emSpec = InspectorSchemas.Find(InspectorSchemas.Item, "item.emission.color");
        if (feat != null && feat.EmissionColor && emSpec != null)
        {
            var r = InspectorWrites.WriteEmission(ui, emSpec, new[] { item }, new Color4(0.3f, 0.2f, 0.1f, 0.4f), null);
            var em = (ui.Model.Find(item).State as ItemNodeState).Emission;
            c.Check(r.Ok && Math.Abs(em.Color.A - 1f) < 1e-6 && Math.Abs(em.Color.R - 0.3f) < 1e-4, "emission picker stores alpha 1 (T5): " + InspectorValues.Text(em.Color));
        }
        else c.Info("fallback item has no emission colour: T5 alpha check skipped (feature " + (feat?.EmissionColor.ToString() ?? "none") + ")");

        // panel rows: hidden on the non-panel fallback item, written on the vanilla panel item 975 (ItemTest M4)
        bool PanelRow(FieldRow r) => r.Spec?.LabelKey != null && r.Spec.LabelKey.StartsWith("inspector.item.panel", StringComparison.Ordinal) && r.Spec.LabelKey != "inspector.item.panel";
        var shownPanel = insp.Query<FieldRow>().Where(r => PanelRow(r) && r.style.display.value != DisplayStyle.None).ToList().Count;
        c.Check(feat == null || feat.Panel || shownPanel == 0, "panel rows hidden on a non-panel item (" + shownPanel + " shown)");
        var pr2 = ui.Ops.AddItem(ContentId.Vanilla(975), null, null, -1, User);
        string panelItem = pr2.NewIds.FirstOrDefault();
        yield return WaitFor(() => panelItem != null && ui.Runtime.FindItem(panelItem)?.State == RealizationState.Built, 60, ok);
        var pf = InspectorGates.Features(ui.Runtime, panelItem);
        c.Check(pr2.Ok && ok[0] && pf != null && pf.Panel, "vanilla panel item 975 added, built, panel offered");
        if (pf != null && pf.Panel)
        {
            ui.Ops.Select(new[] { panelItem });
            insp.Refresh(true);
            var rows = insp.Query<FieldRow>().Where(r => PanelRow(r)).ToList();
            c.Check(rows.Count == 7 && rows.All(r => r.style.display.value != DisplayStyle.None), "seven panel rows shown (" + rows.Count + ")");
            var pick = new List<FieldRow>
            {
                rows.FirstOrDefault(r => r.Spec.LabelKey == "inspector.item.panelColor"),
                rows.FirstOrDefault(r => r.Spec.LabelKey == "inspector.item.panelUs"),
                rows.FirstOrDefault(r => r.Spec.LabelKey == "inspector.item.panelRot"),
                rows.FirstOrDefault(r => r.Spec.LabelKey == "inspector.item.panelClamp"),
            };
            foreach (var row in pick)
            {
                if (row == null) { c.Check(false, "panel row missing"); continue; }
                if (!TestValue(c, row, panelItem, out var widget, out var expect, out var why)) { c.Check(false, "panel row " + row.Spec.LabelKey + " untestable: " + why); continue; }
                string before = Json(c, panelItem, row.Path);
                int n0p = UndoCount(c);
                var wr = FieldWidgets.Write(row, widget, EditPhase.Commit);
                FieldAccessor.TryGet(ui.Model, panelItem, row.Path, out var after, out _);
                string want = StudioOps.Labels.ForItemPath(row.Path.Substring(5));
                bool good = wr.Ok && expect(after) && UndoCount(c) == n0p + 1 && c.UndoLabel == want;
                string label = c.UndoLabel;
                if (UndoCount(c) > n0p) ui.Session.Commands.Undo();
                c.Check(good && Json(c, panelItem, row.Path) == before, "panel row " + row.Spec.LabelKey + ": " + InspectorValues.Text(after) + ", entry \"" + label + "\" (want \"" + want + "\"), undo restores");
            }
        }

        // lights: one per row until a point and a spot were seen
        var seen = new HashSet<LightType>();
        foreach (var row in c.App.Db.Lights.Enumerate().Take(9))
        {
            var lr = ui.Ops.AddLight(row.Key, null, null, -1, User);
            string light = lr.NewIds.FirstOrDefault();
            yield return WaitFor(() => light != null && (ui.Runtime.Find(light) as LightRealization)?.Light != null, 20, ok);
            if (light == null) continue;
            var t = InspectorGates.TypeOf(ui.Ops, ui.Model.Find(light));
            if (!seen.Add(t)) continue;
            ui.Ops.Select(new[] { light });
            insp.Refresh(true);
            var ranges = insp.Query<FieldRow>().Where(x => x.Path == "light.range" && x.style.display.value != DisplayStyle.None).ToList();
            c.Check(t == LightType.Directional ? ranges.Count == 0 : ranges.Count == 1 && ranges[0].Spec.Min == (t == LightType.Spot ? 0.5 : 0.1),
                "light " + t + ": " + ranges.Count + " range row(s) shown" + (ranges.Count > 0 ? " (min " + ranges[0].Spec.Min + ")" : ""));
            RowLoop(c, insp, light, "light " + t);
            if (seen.Contains(LightType.Point) && seen.Contains(LightType.Spot)) break;
        }

        // camera
        ui.Registry.Run(ActionIds.AddCamera, "test");
        string cam = ui.Selection.Primary;
        c.Check(ui.Model.Find(cam)?.Kind == NodeKind.Camera, "camera added");
        insp.Refresh(true);
        RowLoop(c, insp, cam, "camera");
        ui.ShowResult(ui.Ops.SetTransform(new[] { cam }, new Vector3(3, 4, 5), new Vector3(10, 20, 0), null, User));
        int n0 = UndoCount(c);
        var fv = ButtonActions.Run(ui, "ops.CameraFromView", new[] { cam });
        var view = ui.Runtime.GetPart<SceneViewCamera>()?.Camera;
        var pos = (Vec3)FieldAccessor.Get(ui.Model, cam, "transform.pos");
        c.Check(fv.Ok && view != null && Vector3.Distance(InspectorValues.U3(pos), view.transform.position) < 1e-3 && UndoCount(c) == n0 + 1 && c.UndoLabel == OpLabels.Camera,
            "Set from view -> the node at the view position, entry \"" + c.UndoLabel + "\"");
        var lt = ButtonActions.Run(ui, "ops.SetActiveCamera", new[] { cam });
        c.Check(lt.Ok && (ui.Model.Find(cam).State as CameraNodeState).Active && c.UndoLabel == OpLabels.ActiveCamera, "Look through = active camera");
        ui.ShowResult(ui.Ops.SetActiveCamera(null, User));

        // folder: the name only
        ui.Registry.Run(ActionIds.AddFolder, "test");
        string folder = ui.Selection.Primary;
        insp.Refresh(true);
        c.Check(insp.Kind == NodeKind.Folder && insp.Q<VisualElement>("section-transform") == null && insp.Query<FieldRow>().ToList().Count == 1, "folder: header name only");
        RowLoop(c, insp, folder, "folder");
    }

    // ---------------------------------------------------------------- M: multi-selection and sub-selection

    static IEnumerator InspectorMultiGroup(Ctx c)
    {
        var ui = c.Ui;
        yield return NewScene(c);
        var ids = new List<string>();
        yield return AddBuiltChara(c, c.FemaleCard, ids);
        yield return AddBuiltChara(c, c.MaleCard, ids);
        if (ids.Count < 2) yield break;
        ui.ShowResult(ui.Ops.SetFace(new[] { ids[0] }, new FaceEdit { EyesOpen = 0.2f }, User));
        ui.ShowResult(ui.Ops.SetFace(new[] { ids[1] }, new FaceEdit { EyesOpen = 0.8f }, User));
        ui.Ops.Select(ids);
        var insp = Inspector(c);
        insp.Refresh(true);
        yield return PlayModeHarness.WaitFrames(2);
        var row = insp.Chara?.TabBodies["face"].Query<FieldRow>().Where(r => r.Path == "chara.face.eyesOpen").First();
        c.Check(row != null && row.Shown.Mixed, "eyes open differs -> mixed display");
        c.Check(insp.Q<Label>("selection-count") != null, "header shows the selection count");
        if (row != null)
        {
            int n0 = UndoCount(c);
            var r = FieldWidgets.Write(row, 0.5, EditPhase.Commit);
            var a = (ui.Model.Find(ids[0]).State as CharaNodeState).Face.EyesOpen;
            var b = (ui.Model.Find(ids[1]).State as CharaNodeState).Face.EyesOpen;
            c.Check(r.Ok && Math.Abs(a - 0.5f) < 1e-5 && Math.Abs(b - 0.5f) < 1e-5 && UndoCount(c) == n0 + 1, "one write applies to both characters (U25), one entry");
            row.Binding.MarkDirty();
        }
        // transform typed on a mixed selection: x for both, y / z kept per node
        ui.ShowResult(ui.Ops.SetTransform(new[] { ids[0] }, new Vector3(1, 2, 3), null, null, User));
        ui.ShowResult(ui.Ops.SetTransform(new[] { ids[1] }, new Vector3(4, 5, 6), null, null, User));
        var pos = insp.Query<FieldRow>().Where(r => r.Path == "transform.pos").First();
        if (pos?.Control is VectorField vf)
        {
            vf.Fields[0].value = 9f;   // typed X (delayed field: commit on value change)
            var p0 = (Vec3)FieldAccessor.Get(ui.Model, ids[0], "transform.pos");
            var p1 = (Vec3)FieldAccessor.Get(ui.Model, ids[1], "transform.pos");
            c.Check(Math.Abs(p0.X - 9) < 1e-4 && Math.Abs(p1.X - 9) < 1e-4 && Math.Abs(p0.Y - 2) < 1e-4 && Math.Abs(p1.Y - 5) < 1e-4,
                "typed X on a mixed transform: x = 9 on both, y kept (" + p0.Y + ", " + p1.Y + ")");
        }
        else c.Warning("transform.pos row not found");
        // sub-selection: an IK target opens FK / IK and marks its row
        ui.SubSelection = ids[0] + "/ik/LeftHand";
        yield return PlayModeHarness.WaitFrames(2);
        insp.Refresh();
        var chi = insp.Chara;
        var box = chi?.TabBodies["kinematics"].Q<VisualElement>("ik-LeftHand");
        c.Check(ui.Selection.Count == 1 && chi != null && chi.CurrentTab == "kinematics" && box != null && box.ClassListContains(InspectorUi.RowSelected),
            "sub-selection of LeftHand: sole character, FK / IK tab, row marked");
        ui.SubSelection = null;
    }

    // ---------------------------------------------------------------- S: scene panel

    static IEnumerator ScenePanelGroup(Ctx c)
    {
        var ui = c.Ui;
        yield return NewScene(c);
        var sp = Panel<ScenePanel>(c, "scene");
        c.Check(sp != null, "scene panel registered and built");
        if (sp == null) yield break;
        int blocks = sp.Query<Foldout>().Where(f => f.name.StartsWith("section-effects.", StringComparison.Ordinal)).ToList().Count;
        c.Check(blocks == 12, "twelve effect blocks (" + blocks + ")");
        var folder = ui.Ops.AddFolder(null, null, -1, User).NewIds.FirstOrDefault();
        var ok0 = new bool[1];
        RowLoop(c, sp, null, "scene");
        // block reset
        ui.ShowResult(ui.Ops.SetEffects(new List<(string, object)> { ("effects.bloom.intensity", 7f) }, User));
        var reset = sp.Q<VisualElement>("section-effects.bloom")?.Query<FieldRow>().Where(r => r.Spec.Action == "ops.ResetEffects:bloom").First();
        c.Check(reset?.Control is Button, "bloom block has its reset button");
        var rr = ButtonActions.Run(ui, "ops.ResetEffects:bloom", new string[] { null });
        float bi = (float)FieldAccessor.Get(ui.Model, null, "effects.bloom.intensity");
        c.Check(rr.Ok && Math.Abs(bi - 2f) < 1e-5 && c.UndoLabel == OpLabels.ResetEffects, "bloom reset -> intensity " + bi + ", entry \"" + c.UndoLabel + "\"");
        // camera slots
        var slot = sp.Q<VisualElement>("camera-slot-3");
        var mark = slot?.Q<Label>("slot-mark");
        string before = mark?.text;
        int n0 = UndoCount(c);
        c.Check(slot?.Q<Button>("save") != null && slot.Q<Button>("load") != null, "slot row has Save and Load");
        InspectorWrites.RunButton(ui, InspectorSetup.ButtonSpec("ops.SaveCameraSlot:3"), null, null);
        yield return PlayModeHarness.WaitFrames(2);
        var slots = ui.Model.Settings.Camera.Slots;
        c.Check(slots.Count > 3 && slots[3] != null && UndoCount(c) == n0 + 1 && c.UndoLabel == OpLabels.CameraSlot, "slot 4 saved, one entry \"" + c.UndoLabel + "\"");
        c.Check(mark != null && mark.text == L10n.Get("inspector.scene.cameraSlots.saved") && mark.text != before, "slot mark: " + before + " -> " + mark?.text);
        int n1 = UndoCount(c);
        var ld = ButtonActions.Run(ui, "ops.LoadCameraSlot:3", new string[] { null });
        c.Check(ld.Ok && UndoCount(c) == n1, "slot load is a view change (no entry)");
        // map rows (gated by a loaded map): the first listed map
        var mapRow = c.App.Db.Maps.Enumerate().FirstOrDefault();
        var map = ui.Runtime.GetPart<MapRealization>();
        if (mapRow != null && map != null)
        {
            ui.ShowResult(ui.Ops.LoadMap(mapRow.Key.ToString(), User));
            yield return WaitFor(() => map.State != MapState.Loading && map.State != MapState.None, 180, ok0);
            c.Check(map.State == MapState.Loaded, "map " + mapRow.Name + " loaded (" + map.State + ")");
            yield return PlayModeHarness.WaitFrames(3);
            if (map.State == MapState.Loaded)
            {
                foreach (var r in sp.Q<VisualElement>("section-map").Query<FieldRow>().ToList()) r.Binding?.MarkDirty();
                yield return PlayModeHarness.WaitFrames(2);
                RowLoop(c, sp.Q<VisualElement>("section-map"), null, "map");
                var name = sp.Q<Label>("map-name");
                c.Check(name != null && name.text == mapRow.Name, "map header shows the map name (" + name?.text + ")");
            }
        }

        // eyedropper: pick the folder as the DOF focus
        var dofSpec = InspectorSchemas.Find(InspectorSchemas.Scene, "effects.depthOfField.focus");
        if (dofSpec != null && folder != null)
        {
            ui.Ops.Select(Array.Empty<string>());
            sp.BeginPick(dofSpec);
            ui.Ops.Select(new[] { folder });
            yield return PlayModeHarness.WaitFrames(3);
            string focus = FieldAccessor.Get(ui.Model, null, "effects.depthOfField.focus") as string;
            c.Check(focus == folder && ui.Selection.Count == 0 && sp.Picking == null, "eyedropper: the picked node is the DOF focus (" + focus + " / " + folder + "), the selection restored (" + ui.Selection.Count + ")");
        }
    }
}
