// UiPanelsTest.play group MF (Phase 4.1 package W): the Maker widgets of
// on the Face pages of the Edit tab, in the app with the UI in Editor play mode (same harness and Ctx as UiPanelsTest).
//   sub-pages: the page strip, Beard hidden for females / shown for males, the male head list gate, first show of each page
//     (UF3); PartList: rows = MakerLists (head, skin by head, eyebrow), the current mark, the pinned not-listed cell, search
//     kept across a head change and a catalog reload, sort / filter, keyboard (End, Enter = pick), a pick = one entry
//     "Change item" (head change: shapes unchanged, skin snapped / listed), the asynchronous pre-open of a cold mod head
//     bundle; PushGrid: face preset (one entry "Face preset", no mark) and layout pushes (mole row 4, paint row 0 → the HS2
//     vectors, one entry "Edit face"); MakerColorRow: the sclera without alpha (stored alpha kept), a picker session = one
//     entry, Esc reverts; component sliders on two characters with different vectors (each keeps its other components);
//     SideSelector: {side} rows re-targeted, link toggle, copy buttons; ↺ = the reference value ("Reset value");
//     ListBrowser.Reload keeps the search (3.5 item browser); segmented buttons 20 px; bind / scroll timings of a 3,192-row
//     skin list and the head list with thumbnails loading (UF3, WARN above 20 ms); MakerLists.Warm in the Editor (UF4).
// Characters: the SFW targets of Tools/targets.json; thumbnails are drawn by the grid and counted, never viewed.
// Run alone: & Tools\unity_batch.ps1 -Method UiPanelsTest.Run -NoQuit -MethodArgs @('-only','MF')
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HS2Next.Content;
using HS2Next.Content.Maker;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiPanelsTest
{
    // The group joins UiPanelsTest.Groups after every domain reload (UiPanelsTest's static constructor runs first).
    [UnityEditor.InitializeOnLoadMethod]
    static void RegisterMakerFaceGroup()
    {
        if (!Groups.Any(g => g.id == "MF")) Groups.Add(("MF", MakerFaceGroup));
    }

    const string FaceRoot = "appearance.chara.face.";

    static object MfGet(Ctx c, string id, string path) => FieldAccessor.TryGet(c.App.Session.Model, id, path, out var v, out _) ? v : null;
    static string MfText(Ctx c, string id, string path) => InspectorValues.Text(MfGet(c, id, path));

    static CharaEditTab MfOpenEdit(Ctx c, string id)
    {
        var ui = c.Ui;
        if (ui.Selection.Primary != id || ui.Selection.Count != 1) ui.Ops.Select(new[] { id });
        var insp = Inspector(c);
        if (insp == null) return null;
        insp.Refresh(true);
        insp.Chara?.ShowTab(CharaEditTab.TabId);
        return insp.Chara?.TabBodies.TryGetValue(CharaEditTab.TabId, out var b) == true ? b as CharaEditTab : null;
    }

    static IEnumerator MfUndoTo(Ctx c, int n)
    {
        while (UndoCount(c) > n && c.App.Session.Commands.CanUndo) c.App.Session.Commands.Undo();
        yield return PlayModeHarness.WaitFrames(3);
    }

    static T MfQ<T>(VisualElement root, string name) where T : VisualElement => root?.Query<T>(name).ToList().FirstOrDefault();

    static IEnumerator MakerFaceGroup(Ctx c)
    {
        var ui = c.Ui;
        HS2Next.UI.Panels.Maker.PartThumbnails.LogLoads = false;   // true names each asynchronous thumbnail load in the log (a load that removes the GPU device)
        yield return NewScene(c);
        var ids = new List<string>();
        yield return AddBuiltChara(c, c.FemaleCard, ids);
        yield return AddBuiltChara(c, c.MaleCard, ids);
        if (ids.Count < 2) yield break;
        string f = ids[0], m = ids[1];
        var cat = c.App.Catalog;
        var lists = MakerCatalog.For(cat);
        c.Check(lists != null, "Maker lists of the catalog");
        if (lists == null) yield break;

        // ---------------------------------------------------------------- UF4 (Editor): MakerLists.Warm, first Rows after it
        {
            var fresh = new MakerLists(lists.Source);
            var sw = Stopwatch.StartNew();
            fresh.Warm(211, 111);
            double warm = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            ContentId.TryParse((string)MfGet(c, f, FaceRoot + "head") ?? "illusion.hs2#0", out var h0);
            int n0 = fresh.Rows(211, 1, ListContext.ForHead(h0)).Count;
            double first = sw.Elapsed.TotalMilliseconds;
            c.Info($"UF4 (Editor): MakerLists.Warm(211, 111) {warm:0.00} ms, first Rows(211, head) after it {first:0.000} ms ({n0} rows)");
            if (warm > 10) c.Warning($"MakerLists.Warm {warm:0.0} ms above the 10 ms target");
            c.Check(first <= 2 || fresh.IsWarm(211), "first Rows of a skin list after Warm ≤ 2 ms (" + first.ToString("0.000") + " ms)");
        }

        // ---------------------------------------------------------------- sub-pages
        var tab = MfOpenEdit(c, f);
        yield return PlayModeHarness.WaitFrames(4);
        c.Check(tab != null && tab.panel != null, "the female's Edit tab is shown");
        if (tab == null) yield break;
        c.Check(tab.HasSession && MakerServices.References.Has(f), "the tab holds the edit session and the reference of the character is captured");
        tab.ShowNode("face");
        string[] pages = { "head", "shape", "eyebrow", "eyes", "eyelashes", "makeup", "paint", "mole", "beard" };
        var missing = pages.Where(p => MfQ<Button>(tab, "edit-page-face-" + p) == null).ToList();
        c.Check(missing.Count == 0, "the Face page strip has the nine pages" + (missing.Count > 0 ? " (missing " + string.Join(", ", missing) + ")" : ""));
        var beardBtn = MfQ<Button>(tab, "edit-page-face-beard");
        c.Check(beardBtn != null && beardBtn.style.display == DisplayStyle.None, "Beard page hidden for a female");
        var buildMs = new List<string>();
        foreach (var p in pages.Where(p => p != "beard"))
        {
            tab.Reveal("face", p);
            yield return PlayModeHarness.WaitFrames(2);
            if (tab.PageBuildMs.TryGetValue("face." + p, out double ms)) buildMs.Add(p + " " + ms.ToString("0.0"));
            c.Check(tab.CurrentPage == p && tab.SubPages.ContainsKey("face." + p), "page " + p + " shown");
        }
        c.Info("UF3 first show of the Face pages (ms): " + string.Join(", ", buildMs));
        double eyesMs = tab.PageBuildMs.TryGetValue("face.eyes", out var em) ? em : -1;
        if (eyesMs > 50) c.Warning($"Eyes page first show {eyesMs:0.0} ms above the 50 ms target");
        c.Check(MfQ<VisualElement>(tab.SubPages["face.head"], "field-" + FaceRoot + "head") != null, "female: the head list is shown (maker.headList)");

        // ---------------------------------------------------------------- PartList: head list
        tab.Reveal("face", "head");
        yield return PlayModeHarness.WaitFrames(3);
        var head = MfQ<PartList>(tab, "field-" + FaceRoot + "head");
        var skin = MfQ<PartList>(tab, "field-" + FaceRoot + "skin");
        c.Check(head != null && skin != null, "head and skin lists built");
        if (head == null || skin == null) yield break;
        var headRows = lists.Rows(210, 1).Select(r => r.Key.ToString()).ToList();
        var headItems = head.Items.Where(i => !i.NotListed).Select(i => i.Ref).ToList();
        c.Check(headItems.SequenceEqual(headRows), "head list rows = MakerLists.Rows(210, female) in order (" + headItems.Count + " / " + headRows.Count + ")");
        string curHead = (string)MfGet(c, f, FaceRoot + "head");
        c.Check(head.CurrentRef == curHead && !head.Mixed && head.Grid.IsCurrent(head.Items.First(i => i.Ref == curHead || i.NotListed)), "the current head is marked (" + curHead + ")");
        c.Check(skin.HeadContext == curHead, "the skin list is filtered by the bound head (" + skin.HeadContext + ")");
        ContentId.TryParse(curHead, out var curHeadId);
        var skinRows = lists.Rows(211, 1, ListContext.ForHead(curHeadId)).Select(r => r.Key.ToString()).ToList();
        c.Check(skin.Items.Where(i => !i.NotListed).Select(i => i.Ref).SequenceEqual(skinRows), "skin rows = MakerLists.Rows(211, female, head) (" + skinRows.Count + ")");

        // pick head #2: one entry "Change item", shapes unchanged, skin snapped / listed for the new head
        int n = UndoCount(c);
        string shapes0 = MfText(c, f, FaceRoot + "shape");
        string head2 = "illusion.hs2#2";
        if (curHead == head2) head2 = "illusion.hs2#3";
        c.Check(head.Pick(head2), "the head row " + head2 + " is a cell");
        yield return WaitFor(() => head.PendingRef == null, 30, new bool[1]);
        yield return PlayModeHarness.WaitFrames(4);
        string newSkin = (string)MfGet(c, f, FaceRoot + "skin");
        ContentId.TryParse(head2, out var h2);
        bool skinOk = ContentId.TryParse(newSkin, out var sk) && (lists.IsListed(211, 1, sk, ListContext.ForHead(h2)) || lists.Rows(211, 1, ListContext.ForHead(h2)).Count == 0);
        c.Check(MfText(c, f, FaceRoot + "head") == InspectorValues.Text(head2) && UndoCount(c) == n + 1 && c.UndoLabel == OpLabels.ChangeItem,
            "head pick → face.head " + head2 + ", one entry \"" + c.UndoLabel + "\" (" + (UndoCount(c) - n) + ")");
        c.Check(MfText(c, f, FaceRoot + "shape") == shapes0, "face.shape unchanged by the head change (59 values)");
        c.Check(skinOk && skin.HeadContext == head2 && skin.CurrentRef == newSkin, "the skin list shows head " + head2 + "'s skins with the (snapped) skin " + newSkin + " current");
        c.Check(head.Pick(head2) && UndoCount(c) == n + 1, "re-picking the current row does nothing");
        yield return MfUndoTo(c, n);
        c.Check(MfText(c, f, FaceRoot + "head") == InspectorValues.Text(curHead), "undo restores the head");

        // search kept across a head change and a catalog reload
        var sr = skin.Items.Where(i => !i.NotListed).Skip(1).FirstOrDefault() ?? skin.Items.FirstOrDefault(i => !i.NotListed);
        string word = sr?.Name?.Split(new[] { ' ', '　', '_', '(' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (sr != null && word.Length > 0)
        {
            skin.Search(word);
            c.Check(skin.Items.Any(i => i.Ref == sr.Ref) && skin.Items.Count <= skin.AllItems.Count, "skin search \"" + word + "\" shows the row " + sr.Ref + " (" + skin.Items.Count + " of " + skin.AllItems.Count + ")");
            int b0 = skin.Builds;
            head.Pick(head2);
            yield return WaitFor(() => head.PendingRef == null, 30, new bool[1]);
            yield return PlayModeHarness.WaitFrames(4);
            c.Check(skin.Builds > b0 && skin.SearchText == word, "after a head change the skin rows were rebuilt and the search text stayed (" + skin.SearchText + ")");
            yield return MfUndoTo(c, n);
            int b1 = skin.Builds;
            MakerCatalog.Forget(cat);
            lists = MakerCatalog.For(cat);
            yield return PlayModeHarness.WaitFrames(40);
            c.Check(skin.Builds > b1 && skin.SearchText == word, "after a catalog list reload the rows were rebuilt and the search text stayed");
            skin.Search("");
            c.Check(skin.Items.Count == skin.AllItems.Count && skin.CurrentRef == (string)MfGet(c, f, FaceRoot + "skin"), "search cleared: every row back, the current skin marked");
        }
        else c.Warning("no second skin row with a name to search");

        // pinned "not listed" cell: a skin listed for another head only
        string otherSkin = lists.Rows(211, 1).Select(r => r.Key.ToString()).FirstOrDefault(r => !skinRows.Contains(r));
        if (otherSkin != null)
        {
            ui.ShowResult(ui.Ops.EditAppearance(new[] { f }, new List<(string, object)> { (FaceRoot + "skin", otherSkin) }, UiContext.User()));
            yield return PlayModeHarness.WaitFrames(4);
            var pin = skin.Items.FirstOrDefault();
            c.Check(pin != null && pin.NotListed && pin.Ref == otherSkin && skin.Items.Skip(1).All(i => !i.NotListed), "an unlisted skin shows the pinned \"not listed\" cell first (" + otherSkin + ")");
            int n1 = UndoCount(c);
            if (pin != null && pin.NotListed) { skin.Grid.Select(0); skin.Grid.Activate(); }
            c.Check(UndoCount(c) == n1, "clicking the not-listed cell does nothing");
            yield return MfUndoTo(c, n);
        }

        // ---------------------------------------------------------------- sort / filter, keyboard (eyebrow list)
        tab.Reveal("face", "eyebrow");
        yield return PlayModeHarness.WaitFrames(3);
        var brow = MfQ<PartList>(tab, "field-" + FaceRoot + "eyebrow.item");
        c.Check(brow != null, "eyebrow list built");
        if (brow != null)
        {
            var all = brow.Items.Where(i => !i.NotListed).ToList();
            c.Check(all.Select(i => i.Ref).SequenceEqual(lists.Rows(314, 1).Select(r => r.Key.ToString())), "eyebrow rows = MakerLists.Rows(314) (" + all.Count + ")");
            brow.SetFilter("vanilla");
            bool van = brow.Items.Where(i => !i.NotListed).All(i => i.Row.IsVanilla) && brow.Items.Count(i => !i.NotListed) == all.Count(i => i.Row.IsVanilla);
            brow.SetFilter("mods");
            bool mod = brow.Items.Where(i => !i.NotListed).All(i => !i.Row.IsVanilla) && brow.Items.Count(i => !i.NotListed) == all.Count(i => !i.Row.IsVanilla);
            brow.SetFilter("all");
            c.Check(van && mod && brow.Items.Count(i => !i.NotListed) == all.Count, "filter vanilla / mods / all");
            brow.SetSort("name");
            var names = brow.Items.Where(i => !i.NotListed).Select(i => i.Name).ToList();
            bool sorted = true;
            for (int i = 1; i < names.Count && sorted; i++) sorted = (L10n.Language == "ja" ? ListSorts.CompareJa(names[i - 1], names[i]) : ListSorts.CompareOrdinalIgnoreCase(names[i - 1], names[i])) <= 0;
            brow.SetSort("list");
            c.Check(sorted && brow.Items.Where(i => !i.NotListed).Select(i => i.Ref).SequenceEqual(all.Select(i => i.Ref)), "sort by name, back to list order");
            // keyboard: End selects the last cell, Enter picks it (one entry "Change item")
            int nk = UndoCount(c);
            brow.Grid.Focus();
            using (var e = KeyDownEvent.GetPooled('\0', KeyCode.End, EventModifiers.None)) { e.target = brow.Grid; brow.Grid.SendEvent(e); }
            yield return PlayModeHarness.WaitFrames(2);
            bool endOk = brow.Grid.SelectedPos == brow.Items.Count - 1;
            using (var e = KeyDownEvent.GetPooled('\0', KeyCode.Return, EventModifiers.None)) { e.target = brow.Grid; brow.Grid.SendEvent(e); }
            yield return WaitFor(() => brow.PendingRef == null, 30, new bool[1]);
            yield return PlayModeHarness.WaitFrames(3);
            string last = brow.Items.Last().Ref;
            c.Check(endOk && MfText(c, f, FaceRoot + "eyebrow.item") == InspectorValues.Text(last) && UndoCount(c) == nk + 1 && c.UndoLabel == OpLabels.ChangeItem,
                "keyboard End + Enter picks the last eyebrow (" + last + "), one entry \"" + c.UndoLabel + "\"");
            yield return MfUndoTo(c, n);
        }

        // ---------------------------------------------------------------- asynchronous pre-open of a cold mod head bundle
        tab.Reveal("face", "head");
        yield return PlayModeHarness.WaitFrames(2);
        bool tried = false;
        foreach (var cand in lists.Rows(210, 1).Where(r => !r.IsVanilla && r.ZipPath != null).Reverse().Take(8).ToList())
        {
            int a0 = head.AsyncPicks, nh = UndoCount(c);
            head.Pick(cand.Key.ToString());
            if (head.PendingRef == null) { yield return MfUndoTo(c, n); continue; }
            tried = true;
            c.Check(head.Grid.IsBusy(head.Items.First(i => i.Ref == cand.Key.ToString())), "the cold head's cell is marked busy while its bundle opens");
            var ok = new bool[1];
            yield return WaitFor(() => head.PendingRef == null, 60, ok);
            yield return PlayModeHarness.WaitFrames(3);
            c.Check(ok[0] && head.AsyncPicks == a0 + 1 && MfText(c, f, FaceRoot + "head") == InspectorValues.Text(cand.Key.ToString()) && UndoCount(c) == nh + 1,
                "cold mod head " + cand.Key + ": opened asynchronously in " + head.LastOpenMs.ToString("0") + " ms, then one entry (" + head.LastResult?.Code + ")");
            yield return MfUndoTo(c, n);
            break;
        }
        if (!tried) c.Warning("no cold mod head bundle among the last 8 mod heads (all open already): the asynchronous path was not exercised");

        // ---------------------------------------------------------------- PushGrid: face preset
        var preset = MfQ<PushGrid>(tab, "push-facePreset");
        c.Check(preset != null, "face preset grid built");
        if (preset != null)
        {
            var ph = lists.PresetHeads(1).Select(r => r.Key.ToString()).ToList();
            c.Check(preset.Items.Select(i => i.Ref).SequenceEqual(ph) && preset.Grid.IsCurrent == null, "preset cells = MakerLists.PresetHeads(female) (" + ph.Count + "), no current mark");
            int np = UndoCount(c);
            string h3 = "illusion.hs2#3";
            c.Check(preset.Push(h3), "push the preset of head 3");
            yield return PlayModeHarness.WaitFrames(3);
            c.Check(preset.LastResult != null && preset.LastResult.Ok && UndoCount(c) == np + 1 && c.UndoLabel == OpLabels.FacePreset && MfText(c, f, FaceRoot + "head") == InspectorValues.Text(h3),
                "preset push → one entry \"" + c.UndoLabel + "\", face.head " + MfText(c, f, FaceRoot + "head") + (preset.LastResult != null && !preset.LastResult.Ok ? " " + preset.LastResult.Code + ": " + preset.LastResult.Message : ""));
            yield return MfUndoTo(c, n);
        }

        // ---------------------------------------------------------------- PushGrid: layout pushes
        tab.Reveal("face", "paint");
        yield return PlayModeHarness.WaitFrames(3);
        var p1 = MfQ<VisualElement>(tab, "edit-section-face.paint.p1");
        var paintPush = p1?.Q<PushGrid>();
        c.Check(paintPush != null && paintPush.Path == FaceRoot + "makeup.paint[0].layout", "paint 1 layout grid (" + paintPush?.Path + ")");
        if (paintPush != null)
        {
            int nl = UndoCount(c);
            paintPush.Push("illusion.hs2#1");   // «右頬» (row 0 «左頬» = the paint layout of ill_Default_Female: a no-op on the target)
            yield return PlayModeHarness.WaitFrames(2);
            var v = InspectorValues.V4(MfGet(c, f, FaceRoot + "makeup.paint[0].layout"));
            c.Check(Math.Abs(v.X - 0.5f) < 1e-5 && Math.Abs(v.Y - 0.5f) < 1e-5 && Math.Abs(v.Z - 0.24f) < 1e-5 && Math.Abs(v.W - 0.35f) < 1e-5 && UndoCount(c) == nl + 1 && c.UndoLabel == OpLabels.EditFace,
                "paint 1 push «右頬» → layout " + InspectorValues.Text(MfGet(c, f, FaceRoot + "makeup.paint[0].layout")) + ", one entry \"" + c.UndoLabel + "\"" + (paintPush.LastResult != null && !paintPush.LastResult.Ok ? " " + paintPush.LastResult.Message : ""));
            yield return MfUndoTo(c, n);
        }
        tab.Reveal("face", "mole");
        yield return PlayModeHarness.WaitFrames(3);
        var molePush = MfQ<PushGrid>(tab, "push-layout:6");
        if (molePush != null)
        {
            int nl = UndoCount(c);
            molePush.Push("illusion.hs2#4");
            yield return PlayModeHarness.WaitFrames(2);
            var v = InspectorValues.V4(MfGet(c, f, FaceRoot + "mole.layout"));
            c.Check(Math.Abs(v.X - 0.33f) < 1e-5 && Math.Abs(v.Y - 0.33f) < 1e-5 && Math.Abs(v.Z - 0.75f) < 1e-5 && Math.Abs(v.W - 0.52f) < 1e-5 && UndoCount(c) == nl + 1,
                "mole push «左の目元» → layout " + InspectorValues.Text(MfGet(c, f, FaceRoot + "mole.layout")));
            var slider = MfQ<MakerSliderRow>(tab, "field-" + FaceRoot + "mole.layout#2");
            c.Check(slider != null && Math.Abs(slider.Control.Value - 75) < 1e-3, "the mole X slider follows the push (" + slider?.Control.Value + ")");
            yield return MfUndoTo(c, n);
        }
        else c.Check(false, "mole layout grid built");

        // ---------------------------------------------------------------- MakerColorRow
        tab.Reveal("face", "eyes");
        yield return PlayModeHarness.WaitFrames(3);
        var side = tab.Side;
        c.Check(side != null, "the Eyes page has the side selector");
        if (side != null && side.Side != SideSelector.Left) { side.SetSide(SideSelector.Left); yield return PlayModeHarness.WaitFrames(2); }
        var sclera = MfQ<MakerColorRow>(tab, "field-" + FaceRoot + "eyes.left.white");
        c.Check(sclera != null && !sclera.Swatch.Alpha, "sclera colour row without alpha");
        if (sclera != null && sclera.OpenPicker())
        {
            yield return PlayModeHarness.WaitFrames(2);
            var before = InspectorValues.C4(MfGet(c, f, FaceRoot + "eyes.left.white"));
            int nc = UndoCount(c);
            c.Check(sclera.Picker != null && sclera.Picker.Q("alpha") == null, "the sclera picker has no alpha strip");
            sclera.Write(new Color(0.2f, 0.4f, 0.6f, 0.3f));
            sclera.Write(new Color(0.25f, 0.45f, 0.65f, 0.3f));
            sclera.Picker?.Close(true);
            yield return PlayModeHarness.WaitFrames(2);
            var after = InspectorValues.C4(MfGet(c, f, FaceRoot + "eyes.left.white"));
            c.Check(Math.Abs(after.R - 0.25f) < 1e-5 && Math.Abs(after.B - 0.65f) < 1e-5 && after.A == before.A && UndoCount(c) == nc + 1 && c.UndoLabel == OpLabels.EditFace,
                "a picker session = one entry \"" + c.UndoLabel + "\", stored alpha kept (" + before.A + " → " + after.A + ")");
            yield return MfUndoTo(c, n);
        }
        else c.Check(false, "the sclera picker opens");
        var iris = MfQ<MakerColorRow>(tab, "field-" + FaceRoot + "eyes.left.iris.color");
        if (iris != null && iris.Swatch.Alpha && iris.OpenPicker())
        {
            yield return PlayModeHarness.WaitFrames(2);
            string b0 = MfText(c, f, FaceRoot + "eyes.left.iris.color");
            int nc = UndoCount(c);
            iris.Write(new Color(0.9f, 0.1f, 0.1f, 0.5f));
            bool moved = MfText(c, f, FaceRoot + "eyes.left.iris.color") != b0;
            iris.Picker?.Close(false);
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(moved && MfText(c, f, FaceRoot + "eyes.left.iris.color") == b0 && UndoCount(c) == nc, "iris colour (alpha): the preview changed the value, Esc / Cancel reverted it, no entry");
        }
        else c.Check(false, "iris colour row with alpha opens its picker");

        // ---------------------------------------------------------------- SideSelector
        if (side != null)
        {
            side.SetSide(SideSelector.Right);
            yield return PlayModeHarness.WaitFrames(3);
            c.Check(MfQ<VisualElement>(tab, "field-" + FaceRoot + "eyes.right.iris.color") != null && MfQ<VisualElement>(tab, "field-" + FaceRoot + "eyes.left.iris.color") == null
                && MfQ<VisualElement>(tab, "field-" + FaceRoot + "eyes.right.pupil.size#0") != null, "side right: the {side} rows edit the right eye");
            int ns = UndoCount(c);
            bool linked = MfGet(c, f, FaceRoot + "eyes.linked") is bool lb && lb;
            side.LinkToggle.value = !linked;
            yield return PlayModeHarness.WaitFrames(2);
            c.Check((MfGet(c, f, FaceRoot + "eyes.linked") is bool lb2 && lb2) == !linked && UndoCount(c) == ns + 1 && c.UndoLabel == OpLabels.EditFace, "link toggle → linked " + !linked + ", one entry");
            if (linked)
            {
                // unlinked now: change the right pupil width through its slider, then copy right → left
                var pw = MfQ<MakerSliderRow>(tab, "field-" + FaceRoot + "eyes.right.pupil.size#0");
                string leftBefore = MfText(c, f, FaceRoot + "eyes.left");
                var pr = pw?.Write(new[] { f }, 0.21, null);
                yield return PlayModeHarness.WaitFrames(2);
                c.Check(MfText(c, f, FaceRoot + "eyes.left") == leftBefore && Math.Abs(FieldValues.ToDouble(FieldValues.Component(MfGet(c, f, FaceRoot + "eyes.right.pupil.size"), 0)) - 0.21) < 1e-5,
                    "unlinked: the right pupil width changed, the left eye did not (row " + (pw != null) + ", " + pr?.Code + " " + pr?.Message + "; right size " + MfText(c, f, FaceRoot + "eyes.right.pupil.size")
                    + ", left " + (MfText(c, f, FaceRoot + "eyes.left") == leftBefore ? "unchanged" : "changed: " + leftBefore + " -> " + MfText(c, f, FaceRoot + "eyes.left")) + ")");
                int nc = UndoCount(c);
                side.Copy(SideSelector.Right, SideSelector.Left);
                yield return PlayModeHarness.WaitFrames(2);
                c.Check(MfText(c, f, FaceRoot + "eyes.left") == MfText(c, f, FaceRoot + "eyes.right") && UndoCount(c) == nc + 1 && c.UndoLabel == OpLabels.EditFace, "copy right → left: left = right, one entry \"" + c.UndoLabel + "\"");
            }
            yield return MfUndoTo(c, n);
            side.SetSide(SideSelector.Left);
            yield return PlayModeHarness.WaitFrames(2);
            // segmented buttons: 20 px against the runtime theme
            float sh = side.Buttons.Buttons[0].resolvedStyle.height;
            c.Check(Mathf.Abs(sh - 20f) < 0.6f, "segmented buttons resolve to 20 px (" + sh.ToString("0.0") + ")");
        }

        // ---------------------------------------------------------------- ↺ = reference value
        tab.Reveal("face", "eyebrow");
        yield return PlayModeHarness.WaitFrames(3);
        var tilt = MfQ<MakerSliderRow>(tab, "field-" + FaceRoot + "eyebrow.tilt");
        if (tilt != null && MakerServices.References.TryGet(f, FaceRoot + "eyebrow.tilt", out var refTilt))
        {
            tilt.Write(new[] { f }, 0.93, null);
            int nr = UndoCount(c);
            tilt.Reset();
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(MfText(c, f, FaceRoot + "eyebrow.tilt") == InspectorValues.Text(refTilt) && UndoCount(c) == nr + 1 && c.UndoLabel == OpLabels.ResetValue,
                "↺ on the eyebrow tilt → the reference value " + InspectorValues.Text(refTilt) + ", one entry \"" + c.UndoLabel + "\"");
            var bx = MfQ<MakerSliderRow>(tab, "field-" + FaceRoot + "eyebrow.layout#2");
            if (bx != null && MakerServices.References.TryGet(f, FaceRoot + "eyebrow.layout", out var refLayout))
            {
                bx.Write(new[] { f }, 0.11, null);
                var bxRow = MfQ<MakerSliderRow>(tab, "field-" + FaceRoot + "eyebrow.layout#0");
                bxRow?.Write(new[] { f }, 0.12, null);
                bx.Reset();
                var lv = InspectorValues.V4(MfGet(c, f, FaceRoot + "eyebrow.layout"));
                c.Check(Math.Abs(lv.Z - InspectorValues.V4(refLayout).Z) < 1e-6 && Math.Abs(lv.X - 0.12f) < 1e-6, "↺ on a component row resets that component only");
            }
            yield return MfUndoTo(c, n);
        }
        else c.Check(false, "eyebrow tilt row and its reference value");

        // ---------------------------------------------------------------- component sliders on two characters
        var ids2 = new List<string>();
        yield return AddBuiltChara(c, c.FemaleCard, ids2);
        if (ids2.Count == 1)
        {
            string f2 = ids2[0];
            ui.ShowResult(ui.Ops.EditAppearance(new[] { f }, new List<(string, object)> { (FaceRoot + "eyebrow.layout", new HS2Next.Save.Vec4(0.1f, 0.2f, 0.3f, 0.4f)) }, UiContext.User()));
            ui.ShowResult(ui.Ops.EditAppearance(new[] { f2 }, new List<(string, object)> { (FaceRoot + "eyebrow.layout", new HS2Next.Save.Vec4(0.5f, 0.6f, 0.7f, 0.8f)) }, UiContext.User()));
            ui.Ops.Select(new[] { f, f2 });
            var insp = Inspector(c);
            insp.Refresh(true);
            insp.Chara?.ShowTab(CharaEditTab.TabId);
            var tab2 = insp.Chara?.TabBodies[CharaEditTab.TabId] as CharaEditTab;
            tab2?.Reveal("face", "eyebrow");
            yield return PlayModeHarness.WaitFrames(4);
            var wrow = MfQ<MakerSliderRow>(tab2, "field-" + FaceRoot + "eyebrow.layout#2");
            c.Check(wrow != null && wrow.Control.Mixed, "two characters with different eyebrow widths: the component row is mixed");
            if (wrow != null)
            {
                int nw = UndoCount(c);
                wrow.Write(wrow.Binding.Read(ui).Targets, 0.25, null);
                yield return PlayModeHarness.WaitFrames(2);
                var a1 = InspectorValues.V4(MfGet(c, f, FaceRoot + "eyebrow.layout"));
                var a2 = InspectorValues.V4(MfGet(c, f2, FaceRoot + "eyebrow.layout"));
                c.Check(Math.Abs(a1.Z - 0.25f) < 1e-6 && Math.Abs(a2.Z - 0.25f) < 1e-6 && Math.Abs(a1.X - 0.1f) < 1e-6 && Math.Abs(a1.W - 0.4f) < 1e-6 && Math.Abs(a2.X - 0.5f) < 1e-6 && Math.Abs(a2.W - 0.8f) < 1e-6 && UndoCount(c) == nw + 1,
                    "a component write keeps each character's other components (" + InspectorValues.Text(MfGet(c, f, FaceRoot + "eyebrow.layout")) + " / " + InspectorValues.Text(MfGet(c, f2, FaceRoot + "eyebrow.layout")) + "), one entry");
            }
        }

        // ---------------------------------------------------------------- male: beard page, head list gate
        var tabM = MfOpenEdit(c, m);
        yield return PlayModeHarness.WaitFrames(4);
        if (tabM != null)
        {
            tabM.ShowNode("face");
            var bb = MfQ<Button>(tabM, "edit-page-face-beard");
            c.Check(bb != null && bb.style.display != DisplayStyle.None, "Beard page shown for a male");
            tabM.Reveal("face", "beard");
            yield return PlayModeHarness.WaitFrames(3);
            var beard = MfQ<PartList>(tabM, "field-" + FaceRoot + "beard.item");
            c.Check(beard != null && beard.Items.Where(i => !i.NotListed).Select(i => i.Ref).SequenceEqual(lists.Rows(121, 0).Select(r => r.Key.ToString())), "male beard list = MakerLists.Rows(121, male) (" + beard?.Items.Count + ")");
            tabM.Reveal("face", "head");
            yield return PlayModeHarness.WaitFrames(3);
            bool gate = ui.Ops.MakerGateHolds(SchemaGates.MakerHeadList, m);
            var mh = MfQ<PartList>(tabM, "field-" + FaceRoot + "head");
            c.Check((mh != null) == gate && gate == lists.Rows(110, 0).Count > 1, "male head list shown = more than one male head (" + lists.Rows(110, 0).Count + " rows, shown " + (mh != null) + ")");
            var mp = MfQ<PushGrid>(tabM, "push-facePreset");
            c.Check(mp != null && mp.Items.Count == lists.PresetHeads(0).Count, "male preset grid (" + mp?.Items.Count + " cells)");
        }
        var tabF = MfOpenEdit(c, f);
        yield return PlayModeHarness.WaitFrames(3);
        tabF?.ShowNode("face");
        c.Check(tabF != null && MfQ<Button>(tabF, "edit-page-face-beard")?.style.display == DisplayStyle.None, "Beard page hidden again after selecting the female");

        // ---------------------------------------------------------------- edit.show
        ui.Registry.Actions.Run(ActionIds.EditShow, new ActionInvocation { Args = new Dictionary<string, object> { ["node"] = "face", ["page"] = "mole", ["section"] = "face.mole.layout" } }, out var res);
        var tabS = CharaEditTab.Current;
        c.Check(tabS != null && tabS.CurrentNode == "face" && tabS.CurrentPage == "mole", "edit.show {face, mole, face.mole.layout} opens the page (" + res + ")");

        // ---------------------------------------------------------------- ListBrowser.Reload keeps the search (3.5 item browser)
        c.Ui.Shell?.FocusPanel("add.item");
        yield return PlayModeHarness.WaitFrames(3);
        var ib = AddModule.ItemBrowser;
        if (ib != null && ib.Index.Rows.Count > 0)
        {
            string q = ib.Index.Rows[0].Name?.Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            ib.Search(q);
            int hits = ib.Index.Indices.Count;
            ib.Reload();
            c.Check(ib.Index.Query == q && ib.Index.Indices.Count == hits, "ListBrowser.Reload keeps the search \"" + q + "\" (" + hits + " hits)");
            ib.Search("");
        }
        else c.Warning("item browser not built: ListBrowser.Reload not checked");

        // ---------------------------------------------------------------- UF3: bind / scroll timings with thumbnails loading
        tabF = MfOpenEdit(c, f);
        tabF?.Reveal("face", "head");
        yield return PlayModeHarness.WaitFrames(4);
        var skinSpec = InspectorSchemas.CharaEdit.SelectMany(s => s.Fields).FirstOrDefault(x => x.Kind == FieldKind.PartList && x.ListContext == FieldSpec.ListContextHead);
        var host = tabF?.SubPages.TryGetValue("face.head", out var hp) == true ? hp : null;
        if (skinSpec != null && host != null)
        {
            var spec = skinSpec.Clone();
            spec.ListContext = null;   // all ft_skin_f rows (no head filter)
            var big = new PartList(ui, spec, TargetSelector.Primary(NodeKind.Chara), FaceRoot + "skin") { name = "uf3-skin-all" };
            big.SetExpanded(true);
            host.Insert(0, big);
            yield return PlayModeHarness.WaitFrames(6);
            yield return MfScroll(c, big, "skin list without head filter (" + big.Items.Count + " rows), pass 1");
            yield return MfScroll(c, big, "skin list without head filter, pass 2 (warm)");
            big.RemoveFromHierarchy();
            var hl = MfQ<PartList>(host, "field-" + FaceRoot + "head");
            if (hl != null)
            {
                hl.SetExpanded(true);
                yield return PlayModeHarness.WaitFrames(3);
                yield return MfScroll(c, hl, "head list (" + hl.Items.Count + " rows)");
                hl.SetExpanded(false);
            }
            var pt = big.Thumbnails;
            if (pt != null)
                c.Info($"UF3 thumbnails: from bundles {pt.FromBundles}, from the disk cache {pt.FromDiskCount}, failed {pt.FailedCount} ({string.Join(", ", pt.FailReasons.Select(kv => kv.Key + " " + kv.Value))}), guard-skipped {pt.GuardSkipped}, mip<1 rejected {pt.BadMipRejected}, pending {pt.Pending}");
        }
        else c.Warning("UF3 skipped (no skin row spec or Head page)");
    }

    /// <summary>Scrolls a PartList grid from top to bottom one visible page per frame; frame times and bind times (UF3).</summary>
    static IEnumerator MfScroll(Ctx c, PartList pl, string what)
    {
        var grid = pl.Grid;
        int rows = Math.Max(1, (pl.Items.Count + Math.Max(1, grid.Columns) - 1) / Math.Max(1, grid.Columns));
        int binds0 = grid.BindMs.Count;
        var frames = new List<double>();
        var sw = Stopwatch.StartNew();
        int step = Math.Max(1, MakerThumbList.ExpandedRows);
        for (int r = 0; r < rows; r += step)
        {
            grid.List.ScrollToItem(Math.Min(r, rows - 1));
            sw.Restart();
            yield return null;
            frames.Add(Time.unscaledDeltaTime * 1000.0);
            if (frames.Count > 600) break;
        }
        // let the thumbnails finish for a moment (frames while loading)
        for (int i = 0; i < 30; i++) { yield return null; frames.Add(Time.unscaledDeltaTime * 1000.0); }
        var binds = grid.BindMs.Skip(binds0).ToList();
        double perCell = binds.Count > 0 ? binds.Average() / Math.Max(1, grid.Columns) : double.NaN;
        c.Info($"UF3 {what}: {frames.Count} frames, frame median {Median(frames):0.0} ms, p95 {Pct(frames, 0.95):0.0}, max {frames.DefaultIfEmpty(0).Max():0.0}; row binds {binds.Count}, median {Median(binds):0.000} ms per grid row ({grid.Columns} cells, {perCell:0.000} ms per cell), p95 {Pct(binds, 0.95):0.000}");
        if (Pct(frames, 0.95) > 20) c.Warning($"UF3 {what}: p95 frame {Pct(frames, 0.95):0.0} ms above 20 ms (Editor batch play frame times include the harness)");
        c.Check(binds.Count > 0, "UF3 " + what + ": the grid bound rows while scrolling");
    }
}
