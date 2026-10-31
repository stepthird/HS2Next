// UI4 of the bridge acceptance (Phase 4.1 integrator T): the face pages of the
// Maker's Edit tab driven through the bridge's ui.* commands in Editor play (BridgeTest.RunPlayUi after UI3, or
// BridgeTest.RunPlayUi4 alone) and in the app player (-bridgeAccept with -ui). Without a running UI the case is skipped
// with a warning (ui.state enabled false). chara.edit.get, chara.edit.digest and chara.edit.info are the read-only checks.
//   1 file.new, add.female (BR-Chan), Edit › Face › Head & skin (clicks on tab-edit, edit-tree-face, edit-page-face-head);
//     the documents and the face digest after it are the "step 1" state.
//   2 the head list: a click on head 2 (head 1 when BR-Chan has head 2) → one entry "Change item"; the shapes kept bit for
//     bit, the skin snapped when it was not listed for the new head; the skin list follows the head with the current mark.
//   3 the preset grid: head 3 → one entry "Face preset"; the face block = the preset (canonical JSON of the face member).
//   4 the skin list: the first word of the second listed skin typed into its search, its cell clicked → face.skin; the
//     search cleared (every row back, the current mark on the picked skin).
//   5 Eyebrows: a row of the eyebrow list; its colour row: picker, a drag in the SV square over 3 frames, Enter → one entry,
//     the colour changed, alpha kept.
//   6 Eyes: link on (toggle when off), an iris row → both eyes; link off (one entry); the right side; a drag of the pupil
//     width slider → only face.eyes.right.pupil.size.x changed (inside 0..1); copy right → left → left = right.
//   7 Makeup: a lip row; 150 typed into the lip gloss field → stored 1.5 (the flush time reported).
//   8 Paint 1: a paint row; a layout push of row 0 «左頬» (row 1 when the paint already holds row 0's vector — the female
//     default) → (Scale, Scale, PosX, PosY) of the row; a drag of the rotation slider.
//   9 Mole: the layout push of row 4 «左の目元» → (0.33, 0.33, 0.75, 0.52) (row 5 when the mole already holds it).
//  10 Eyebrows: a drag of the tilt slider, then ↺ → the value of step 1 (the reference, one entry "Reset value").
//  11 edit.undo back to step 1 (documents and digest equal to step 1), edit.redo to the end (equal to the end state).
//  12 file.saveAs → file.new → file.open: documents and digest equal to the end state; Makeup shows 150 on the lip gloss.
//  13 add.male (HERO107): Beard page shown, a beard row and a colour change; Head & skin shows the male head list
//     (MakerLists rows of 110) and a male preset push = one entry "Face preset"; Beard hidden after selecting BR-Chan.
// Every step checks the history entry count and label and the document values; no chara.editFailed may be logged.
// Cells of the Maker lists are found through their list widget (the same reference is a cell of several lists).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Content;
using HS2Next.Content.Maker;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.UI;
using HS2Next.UI.Panels;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.App
{
    public static partial class BridgeAcceptance
    {
        const string U4Face = "appearance.chara.face.";
        const string U4ChangeItem = HS2Next.UI.Core.OpLabels.ChangeItem;
        const string U4FacePreset = HS2Next.UI.Core.OpLabels.FacePreset;
        const string U4ResetValue = HS2Next.UI.Core.OpLabels.ResetValue;

        sealed class U4State
        {
            public UiHost Host;
            public UiContext Ui;
            public string F, M;
            public int U1;
            public string Docs1, DocsEnd;
            public JObject Dig1, DigEnd;
            public float Tilt1;
            public int EditFailed;
        }

        public static IEnumerator Ui4(AcceptanceCtx c)
        {
            c.Id = "UI4";
            yield return c.Call(new { cmd = "ui.state" });
            if (c.Last == null || !c.Last.Ok || (bool?)c.J["enabled"] != true)
            {
                c.Warn("UI4 skipped: the Studio UI is not running (start with -ui; " + AcceptanceCtx.Short(c.Last?.ToString()) + ")");
                yield break;
            }
            var s = new U4State { Host = UiHost.Current };
            s.Ui = s.Host?.Context;
            if (!c.Check(s.Ui != null, "UiHost in the app's process")) yield break;
            Application.LogCallback onLog = (m, st, t) => { if (m != null && m.Contains("chara.editFailed")) s.EditFailed++; };
            Application.logMessageReceived += onLog;
            var total = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                yield return U4Female(c, s);
                c.Info($"UI4: female part {total.Elapsed.TotalSeconds:F1} s");
                if (s.F != null)
                {
                    total.Restart();
                    yield return U4Male(c, s);
                    c.Info($"UI4: male part {total.Elapsed.TotalSeconds:F1} s");
                }
                U4ImportCost(c, s);
            }
            finally { Application.logMessageReceived -= onLog; }
            c.Id = "UI4";
            c.Check(s.EditFailed == 0, $"no chara.editFailed logged during UI4 ({s.EditFailed})");
        }

        // ---------------------------------------------------------------- the female part (steps 1–12)

        static IEnumerator U4Female(AcceptanceCtx c, U4State s)
        {
            var ui = s.Ui;
            var host = s.Host;
            var lists = MakerCatalog.For(ui.App?.Catalog);
            if (!c.Check(lists != null, "the Maker lists of the catalog")) yield break;

            // ---- 1: a new scene with BR-Chan, Edit › Face › Head & skin
            c.Id = "UI4.1";
            yield return c.Call(new { cmd = "ui.action", action = "file.new", args = new { force = true } });
            c.CheckOk("ui.action file.new {force}");
            string card = Path.Combine(AppCore.Current.Paths.Hs2CharaDir(1) ?? "", c.FemaleRel ?? "");
            yield return c.Call(new { cmd = "ui.action", action = "add.female", args = new { path = card } }, 180);
            string id = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && !string.IsNullOrEmpty(id), "ui.action add.female {path} → a new character", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            for (int i = 0; i < 600 && ui.Runtime.FindChara(id)?.State != HS2Next.Scenes.Runtime.RealizationState.Built; i++) yield return c.Frames(1);
            if (!c.Check(ui.Runtime.FindChara(id)?.State == HS2Next.Scenes.Runtime.RealizationState.Built, "the character is built")) yield break;
            if (!c.Check(ui.Model.Find(id)?.Appearance?.Stored != null, "the character has stored documents (imported on add)")) yield break;
            var tab = new CharaEditTab[1];
            yield return U4OpenPage(c, s, id, "head", tab);
            if (tab[0] == null) yield break;
            c.Check(MakerServices.References.Has(id), "the Edit tab captured the reference documents of the character");
            s.F = id;
            s.U1 = U4Undo(ui);
            var docs = new string[1];
            yield return U4Docs(c, id, docs);
            s.Docs1 = docs[0];
            var dig = new JObject[1];
            yield return U4Digest(c, id, dig);
            s.Dig1 = dig[0];
            if (!c.Check(s.Docs1 != null && s.Dig1 != null, "chara.edit.get and chara.edit.digest of step 1", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            c.Info($"UI4: digest of step 1 {U4DigestLine(s.Dig1)}");
            s.Tilt1 = Ui3Value(ui, id, U4Face + "eyebrow.tilt");

            // ---- 2: the head list
            c.Id = "UI4.2";
            var head = U4List(tab[0], U4Face + "head");
            var skinL = U4List(tab[0], U4Face + "skin");
            if (!c.Check(head != null && skinL != null && head.resolvedStyle.display != DisplayStyle.None, "Head & skin shows the head and skin lists")) yield break;
            string head0 = U4Ref(ui, id, "head"), skin0 = U4Ref(ui, id, "skin"), shapes0 = U4Json(ui, id, U4Face + "shape");
            string headT = head0 == "illusion.hs2#2" ? "illusion.hs2#1" : "illusion.hs2#2";
            int u = U4Undo(ui);
            yield return U4ClickCell(c, s, head, headT, "the head list cell " + headT);
            for (int i = 0; i < 300 && U4Ref(ui, id, "head") != headT; i++) yield return c.Frames(1);
            yield return c.Frames(3);
            U4Entry(c, ui, u, U4ChangeItem, "the head pick");
            c.Check(U4Ref(ui, id, "head") == headT, $"face.head = {U4Ref(ui, id, "head")} ({headT}; was {head0})");
            c.Check(U4Json(ui, id, U4Face + "shape") == shapes0, "the 59 face shapes are unchanged bit for bit after the head change");
            ContentId.TryParse(headT, out var headTId);
            var skinRowsT = lists.Rows(211, 1, ListContext.ForHead(headTId));
            bool skinListed = ContentId.TryParse(skin0, out var skin0Id) && lists.IsListed(211, 1, skin0Id, ListContext.ForHead(headTId));
            string skinWant = skinListed || skinRowsT.Count == 0 ? skin0 : skinRowsT[0].Key.ToString();
            c.Check(U4Ref(ui, id, "skin") == skinWant, $"face.skin = {U4Ref(ui, id, "skin")} ({skinWant}: {(skinListed ? "listed for the new head, kept" : "snapped to the head's first skin")})");
            yield return c.Frames(2);
            c.Check(skinL.HeadContext == headT && skinL.AllItems.Where(x => !x.NotListed).Select(x => x.Ref).SequenceEqual(skinRowsT.Select(r => r.Key.ToString())),
                $"the skin list shows head {headT}'s {skinRowsT.Count} skins ({skinL.HeadContext}, {skinL.AllItems.Count(x => !x.NotListed)} cells)");
            c.Check(U4Marked(skinL, U4Ref(ui, id, "skin")), "the skin list marks the current skin " + U4Ref(ui, id, "skin"));

            // ---- 3: the preset grid, head 3
            c.Id = "UI4.3";
            var preset = tab[0].Query<PushGrid>().Where(p => p.Source == HS2Next.UI.Core.PushSources.FacePreset).First();
            if (!c.Check(preset != null, "Head & skin shows the face preset grid")) yield break;
            const string head3 = "illusion.hs2#3";
            FaceData pf = null;
            bool hasPreset = MakerServices.Presets != null && MakerServices.Presets.TryLoad(1, head3, out pf, new List<string>());
            c.Check(hasPreset && pf != null, "head 3's preset loads (MakerServices.Presets)");
            u = U4Undo(ui);
            yield return U4ClickCell(c, s, preset, head3, "the preset cell of head 3");
            for (int i = 0; i < 300 && U4Ref(ui, id, "head") != head3; i++) yield return c.Frames(1);
            yield return c.Frames(3);
            U4Entry(c, ui, u, U4FacePreset, "the preset push");
            if (pf != null)
            {
                string want = U4FaceText(pf), got = U4FaceText(FieldAccessor.Get(ui.Model, id, "appearance.chara.face") as FaceData);
                c.Check(want != null && want == got, "the face block = head 3's preset (canonical JSON of face)", want == got ? "" : U4JsonDiff(want, got));
            }

            // ---- 4: the skin list's search
            c.Id = "UI4.4";
            yield return c.Frames(2);
            string skinCur = U4Ref(ui, id, "skin");
            var skinRows = skinL.AllItems.Where(x => !x.NotListed).ToList();
            var skinTarget = skinRows.Skip(1).FirstOrDefault(x => x.Ref != skinCur) ?? skinRows.FirstOrDefault(x => x.Ref != skinCur);
            if (!c.Check(skinTarget != null, $"head 3 lists another skin ({skinRows.Count} rows)")) yield break;
            string word = (skinTarget.Name ?? "").Split(new[] { ' ', '　', '　' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? skinTarget.Ref;
            var search = skinL.Q<TextField>("partlist-search");
            yield return U4Type(c, s, search, word, "the skin list's search field");
            c.Check(skinL.SearchText == word && skinL.PositionOf(skinTarget.Ref) >= 0 && skinL.Items.Count <= skinRows.Count,
                $"the search \"{word}\" shows {skinL.Items.Count} of {skinRows.Count} skins incl. {skinTarget.Ref} ({skinTarget.Name})");
            u = U4Undo(ui);
            yield return U4ClickCell(c, s, skinL, skinTarget.Ref, "the skin cell " + skinTarget.Ref);
            yield return c.Frames(3);
            U4Entry(c, ui, u, U4ChangeItem, "the skin pick");
            c.Check(U4Ref(ui, id, "skin") == skinTarget.Ref, $"face.skin = {U4Ref(ui, id, "skin")} ({skinTarget.Ref})");
            yield return U4Type(c, s, search, "", "the skin list's search field (cleared)");
            yield return c.Frames(2);
            c.Check(skinL.SearchText.Trim() == "" && skinL.Items.Count(x => !x.NotListed) == skinRows.Count, $"the cleared search shows every skin again ({skinL.Items.Count(x => !x.NotListed)} of {skinRows.Count})");
            c.Check(U4Marked(skinL, skinTarget.Ref), "the picked skin carries the current mark");

            // ---- 5: Eyebrows: a row and its colour
            c.Id = "UI4.5";
            yield return U4OpenPage(c, s, id, "eyebrow", tab);
            if (tab[0] == null) yield break;
            yield return U4PickOther(c, s, U4List(tab[0], U4Face + "eyebrow.item"), id, "eyebrow.item", "the eyebrow list");
            yield return U4ColourDrag(c, s, tab[0], id, U4Face + "eyebrow.color", "the eyebrow colour");

            // ---- 6: Eyes
            c.Id = "UI4.6";
            yield return U4OpenPage(c, s, id, "eyes", tab);
            if (tab[0] == null) yield break;
            var side = tab[0].Side;
            if (!c.Check(side != null, "the Eyes page shows the side selector")) yield break;
            if (side.Side != SideSelector.Left)
            {
                yield return U4Click(c, s, side.Buttons.Buttons[0], "side-left");
                yield return c.Frames(2);
            }
            if (!(FieldAccessor.Get(ui.Model, id, U4Face + "eyes.linked") is bool lk && lk))
            {
                u = U4Undo(ui);
                yield return U4Click(c, s, side.LinkToggle, "the link toggle (on)");
                yield return c.Frames(2);
                U4Entry(c, ui, u, HS2Next.UI.Core.OpLabels.EditFace, "link on");
            }
            c.Check(FieldAccessor.Get(ui.Model, id, U4Face + "eyes.linked") is bool lk1 && lk1, "face.eyes.linked = true");
            var iris = U4List(tab[0], U4Face + "eyes.left.iris.item");
            string irisPicked = null;
            if (c.Check(iris != null, "the left iris list"))
            {
                var pick = new string[1];
                yield return U4PickOther(c, s, iris, id, "eyes.left.iris.item", "the iris list (linked)", pick);
                irisPicked = pick[0];
                if (irisPicked != null)
                    c.Check(U4Ref(ui, id, "eyes.left.iris.item") == irisPicked && U4Ref(ui, id, "eyes.right.iris.item") == irisPicked,
                        $"linked: both eyes take the iris {irisPicked} (left {U4Ref(ui, id, "eyes.left.iris.item")}, right {U4Ref(ui, id, "eyes.right.iris.item")})");
            }
            u = U4Undo(ui);
            yield return U4Click(c, s, side.LinkToggle, "the link toggle (off)");
            yield return c.Frames(2);
            U4Entry(c, ui, u, HS2Next.UI.Core.OpLabels.EditFace, "link off");
            c.Check(FieldAccessor.Get(ui.Model, id, U4Face + "eyes.linked") is bool lk2 && !lk2, "face.eyes.linked = false");
            u = U4Undo(ui);
            yield return U4Click(c, s, side.Buttons.Buttons[1], "side-right");
            yield return c.Frames(3);
            c.Check(side.Side == SideSelector.Right && U4Undo(ui) == u, $"the right eye is shown, no history entry ({side.Side}, {U4Undo(ui) - u})");
            var pupil = U4Slider(tab[0], U4Face + "eyes.right.pupil.size", 0);
            string leftEye0 = U4Json(ui, id, U4Face + "eyes.left");
            var rp0 = U4Vec(ui, id, U4Face + "eyes.right.pupil.size");
            if (c.Check(pupil != null && rp0 != null && rp0.Length == 2, "the right pupil width slider (field-…eyes.right.pupil.size#0)"))
            {
                c.Info($"UI4: the right pupil width row shows {pupil.Control.Value} (stored x {rp0[0]})");
                u = U4Undo(ui);
                yield return U4DragKnob(c, s, pupil, (float)rp0[0], "the right pupil width");
                var rp1 = U4Vec(ui, id, U4Face + "eyes.right.pupil.size");
                U4Entry(c, ui, u, HS2Next.UI.Core.OpLabels.EditFace, "the pupil width drag");
                if (U4Undo(ui) == u + 2)
                {
                    // which of the two entries is the drag: undo the last one, read, redo
                    var vEnd = U4Vec(ui, id, U4Face + "eyes.right.pupil.size");
                    yield return c.Call(new { cmd = "ui.action", action = "edit.undo" });
                    yield return c.Frames(2);
                    var vMid = U4Vec(ui, id, U4Face + "eyes.right.pupil.size");
                    yield return c.Call(new { cmd = "ui.action", action = "edit.redo" });
                    yield return c.Frames(2);
                    c.Info($"UI4: the pupil drag left 2 entries: before {U4Num(rp0)}, after the first entry {U4Num(vMid)}, after the second {U4Num(vEnd)}");
                }
                c.Check(rp1 != null && rp1[0] != rp0[0] && rp1[0] >= 0 && rp1[0] <= 1 && rp1[1] == rp0[1] && U4Json(ui, id, U4Face + "eyes.left") == leftEye0,
                    $"only face.eyes.right.pupil.size.x changed, inside 0..1 ({U4Num(rp0)} → {U4Num(rp1)}; left eye {(U4Json(ui, id, U4Face + "eyes.left") == leftEye0 ? "unchanged" : "CHANGED")})");
            }
            var copyRl = side.CopyRightToLeft;
            u = U4Undo(ui);
            yield return U4Click(c, s, copyRl, "eyes-copy-rl");
            yield return c.Frames(3);
            U4Entry(c, ui, u, HS2Next.UI.Core.OpLabels.EditFace, "copy right → left");
            c.Check(U4Json(ui, id, U4Face + "eyes.left") == U4Json(ui, id, U4Face + "eyes.right"), "after copy right → left the left eye equals the right eye");
            // diagnosis (information): history entries of one drag on another component row and on a plain slider of the page
            foreach (var (dp, dc, dn) in new[] { (U4Face + "eyes.right.pupil.size", 1, "the right pupil height (component)"), (U4Face + "eyes.offsetY", -1, "the eye height (plain)") })
            {
                var dr = U4Slider(tab[0], dp, dc);
                if (dr == null) { c.Info("UI4: no row " + dp + "#" + dc); continue; }
                int du0 = U4Undo(ui);
                yield return U4DragKnob(c, s, dr, (float)(dr.Control.Value / 100.0), dn);
                c.Info($"UI4: a drag of {dn} left {U4Undo(ui) - du0} history entries");
            }

            // ---- 7: Makeup › Lip
            c.Id = "UI4.7";
            yield return U4OpenPage(c, s, id, "makeup", tab);
            if (tab[0] == null) yield break;
            yield return U4PickOther(c, s, U4List(tab[0], U4Face + "makeup.lip.item"), id, "makeup.lip.item", "the lip list");
            var gloss = U4Slider(tab[0], U4Face + "makeup.lip.gloss", -1);
            if (c.Check(gloss != null, "the lip gloss row"))
            {
                u = U4Undo(ui);
                yield return U4Type(c, s, gloss.Control.Field, "150", "the lip gloss number field");
                yield return c.Frames(3);
                float g = Ui3Value(ui, id, U4Face + "makeup.lip.gloss");
                c.Check(g == 1.5f, $"typed 150 → stored {g} (1.5: typed −100..200, stored unclamped)");
                U4Entry(c, ui, u, HS2Next.UI.Core.OpLabels.EditFace, "the typed lip gloss");
                c.Check(Math.Abs(gloss.Control.Value - 150) < 1e-6, $"the row shows {gloss.Control.Value} (150)");
                yield return c.Call(new { cmd = "chara.edit.info", key = id });
                if (c.Last != null && c.Last.Ok)
                    c.Info($"UI4 timing ({(Application.isEditor ? "Editor" : "player")}): the typed lip gloss's flush {(double?)c.J["lastFlush"]?["ms"]:0.000} ms, mode {(string)c.J["lastFlush"]?["mode"]}, units {AcceptanceCtx.Short(c.J["lastFlush"]?["units"]?.ToString(Newtonsoft.Json.Formatting.None))}, creators {AcceptanceCtx.Short(c.J["creators"]?.ToString(Newtonsoft.Json.Formatting.None))}");
            }

            // ---- 8: Paint 1
            c.Id = "UI4.8";
            yield return U4OpenPage(c, s, id, "paint", tab);
            if (tab[0] == null) yield break;
            const string paint = U4Face + "makeup.paint[0].";
            yield return U4PickOther(c, s, U4List(tab[0], paint + "item"), id, "makeup.paint[0].item", "the paint 1 list");
            var pgrid = tab[0].Query<PushGrid>().Where(p => p.Path == paint + "layout").First();
            if (c.Check(pgrid != null, "the paint 1 layout push grid (push-layout:7 of paint[0])"))
                yield return U4LayoutPush(c, s, pgrid, id, 7, paint + "layout", new[] { "illusion.hs2#0", "illusion.hs2#1" }, "paint 1");
            var rot = U4Slider(tab[0], paint + "rotation", -1);
            if (c.Check(rot != null, "the paint 1 rotation slider"))
            {
                float r0 = Ui3Value(ui, id, paint + "rotation");
                u = U4Undo(ui);
                yield return U4DragKnob(c, s, rot, r0, "the paint 1 rotation");
                float r1 = Ui3Value(ui, id, paint + "rotation");
                U4Entry(c, ui, u, HS2Next.UI.Core.OpLabels.EditFace, "the rotation drag");
                c.Check(r1 != r0 && r1 >= 0 && r1 <= 1, $"the rotation moved inside 0..1 ({r0} → {r1})");
            }

            // ---- 9: Mole
            c.Id = "UI4.9";
            yield return U4OpenPage(c, s, id, "mole", tab);
            if (tab[0] == null) yield break;
            var mgrid = tab[0].Query<PushGrid>().Where(p => p.Path == U4Face + "mole.layout").First();
            if (c.Check(mgrid != null, "the mole layout push grid (push-layout:6)"))
                yield return U4LayoutPush(c, s, mgrid, id, 6, U4Face + "mole.layout", new[] { "illusion.hs2#4", "illusion.hs2#5" }, "mole");

            // ---- 10: ↺ on the eyebrow tilt
            c.Id = "UI4.10";
            yield return U4OpenPage(c, s, id, "eyebrow", tab);
            if (tab[0] == null) yield break;
            var tilt = U4Slider(tab[0], U4Face + "eyebrow.tilt", -1);
            if (c.Check(tilt != null, "the eyebrow tilt row"))
            {
                float t0 = Ui3Value(ui, id, U4Face + "eyebrow.tilt");
                u = U4Undo(ui);
                yield return U4DragKnob(c, s, tilt, t0, "the eyebrow tilt");
                U4Entry(c, ui, u, HS2Next.UI.Core.OpLabels.EditFace, "the tilt drag");
                float t1 = Ui3Value(ui, id, U4Face + "eyebrow.tilt");
                c.Check(t1 != s.Tilt1, $"the drag moved the tilt away from the reference ({t0} → {t1}; reference {s.Tilt1})");
                u = U4Undo(ui);
                yield return U4Click(c, s, tilt.Control.ResetButton, "the tilt row's ↺");
                yield return c.Frames(3);
                U4Entry(c, ui, u, U4ResetValue, "↺");
                float t2 = Ui3Value(ui, id, U4Face + "eyebrow.tilt");
                c.Check(t2 == s.Tilt1, $"↺ → the value of step 1 {t2} ({s.Tilt1}, the reference)");
            }

            // ---- 11: undo to step 1, redo to the end
            c.Id = "UI4.11";
            yield return c.Frames(3);
            var docsE = new string[1];
            yield return U4Docs(c, id, docsE);
            s.DocsEnd = docsE[0];
            var digE = new JObject[1];
            yield return U4Digest(c, id, digE);
            s.DigEnd = digE[0];
            int n = U4Undo(ui) - s.U1;
            c.Info($"UI4: {n} history entries since step 1: {string.Join(" | ", ui.Session.Commands.UndoList.Take(n).Reverse().Select(e => e.Label))}");
            for (int i = 0; i < n; i++)
            {
                yield return c.Call(new { cmd = "ui.action", action = "edit.undo" });
                if (!(c.Last != null && c.Last.Ok)) { c.CheckOk("ui.action edit.undo #" + (i + 1)); break; }
                yield return c.Frames(1);
            }
            yield return c.Frames(6);
            c.Check(U4Undo(ui) == s.U1, $"undo back to step 1 ({U4Undo(ui)} entries, {s.U1} at step 1)");
            yield return U4Docs(c, id, docs);
            c.Check(docs[0] != null && docs[0] == s.Docs1, "undo to step 1 → the documents are byte-identical to step 1", docs[0] == s.Docs1 ? "" : U4JsonDiff(s.Docs1, docs[0]));
            yield return U4Digest(c, id, dig);
            c.Check(U4SameDigest(s.Dig1, dig[0], out string d1), "undo to step 1 → the face digest equals step 1's", d1);
            for (int i = 0; i < n; i++)
            {
                yield return c.Call(new { cmd = "ui.action", action = "edit.redo" });
                if (!(c.Last != null && c.Last.Ok)) { c.CheckOk("ui.action edit.redo #" + (i + 1)); break; }
                yield return c.Frames(1);
            }
            yield return c.Frames(6);
            yield return U4Docs(c, id, docs);
            c.Check(docs[0] != null && docs[0] == s.DocsEnd, "redo to the end → the documents equal the end state", docs[0] == s.DocsEnd ? "" : U4JsonDiff(s.DocsEnd, docs[0]));
            yield return U4Digest(c, id, dig);
            c.Check(U4SameDigest(s.DigEnd, dig[0], out string d2), "redo to the end → the face digest equals the end state's", d2);

            // ---- 12: save as, new, open
            c.Id = "UI4.12";
            yield return c.Call(new { cmd = "ui.action", action = "file.saveAs", args = new { name = "ui4_acceptance", overwrite = true } }, 120);
            string file = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && file != null && File.Exists(file), "ui.action file.saveAs {name} → " + file)) yield break;
            yield return c.Call(new { cmd = "ui.action", action = "file.new", args = new { force = true } });
            c.CheckOk("ui.action file.new {force}");
            yield return c.Call(new { cmd = "ui.action", action = "file.open", args = new { path = file } }, 180);
            c.CheckOk("ui.action file.open {path}");
            for (int i = 0; i < 600 && ui.Runtime.FindChara(id)?.State != HS2Next.Scenes.Runtime.RealizationState.Built; i++) yield return c.Frames(1);
            if (!c.Check(ui.Runtime.FindChara(id)?.State == HS2Next.Scenes.Runtime.RealizationState.Built, "the reopened character is built (same node id)")) yield break;
            yield return c.Frames(3);
            yield return U4Docs(c, id, docs);
            c.Check(docs[0] != null && docs[0] == s.DocsEnd, "reopened: the documents equal the end state", docs[0] == s.DocsEnd ? "" : U4JsonDiff(s.DocsEnd, docs[0]));
            yield return U4Digest(c, id, dig);
            c.Check(U4SameDigest(s.DigEnd, dig[0], out string d3), "reopened: the face digest (materials, creators, bones" + (dig[0]?["rts"] != null ? ", RT hashes" : "") + ") equals the end state's", d3);
            yield return U4OpenPage(c, s, id, "makeup", tab);
            if (tab[0] != null)
            {
                var g2 = U4Slider(tab[0], U4Face + "makeup.lip.gloss", -1);
                c.Check(g2 != null && Math.Abs(g2.Control.Value - 150) < 1e-6, $"reopened: Makeup shows {g2?.Control.Value} on the lip gloss (150)");
            }
        }

        // ---------------------------------------------------------------- the male part (step 13)

        static IEnumerator U4Male(AcceptanceCtx c, U4State s)
        {
            var ui = s.Ui;
            var lists = MakerCatalog.For(ui.App?.Catalog);
            c.Id = "UI4.13";
            string card = Path.Combine(AppCore.Current.Paths.Hs2CharaDir(0) ?? "", Ui2MaleRel ?? "");
            yield return c.Call(new { cmd = "ui.action", action = "add.male", args = new { path = card } }, 180);
            string id = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && !string.IsNullOrEmpty(id), "ui.action add.male {path} → a new character (HERO107)", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            for (int i = 0; i < 600 && ui.Runtime.FindChara(id)?.State != HS2Next.Scenes.Runtime.RealizationState.Built; i++) yield return c.Frames(1);
            if (!c.Check(ui.Runtime.FindChara(id)?.State == HS2Next.Scenes.Runtime.RealizationState.Built, "the male character is built")) yield break;
            s.M = id;
            var tab = new CharaEditTab[1];
            yield return U4OpenPage(c, s, id, "beard", tab);
            if (tab[0] == null) yield break;
            yield return U4PickOther(c, s, U4List(tab[0], U4Face + "beard.item"), id, "beard.item", "the beard list");
            yield return U4ColourDrag(c, s, tab[0], id, U4Face + "beard.color", "the beard colour");

            yield return U4OpenPage(c, s, id, "head", tab);
            if (tab[0] == null) yield break;
            var head = U4List(tab[0], U4Face + "head");
            int maleHeads = lists?.Rows(110, 0).Count ?? -1;
            c.Check(head != null && head.resolvedStyle.display != DisplayStyle.None && head.Category == 110 && head.AllItems.Count(x => !x.NotListed) == maleHeads,
                $"male: Head & skin shows the head list with the {maleHeads} male heads ({head?.AllItems.Count(x => !x.NotListed)} cells, category {head?.Category})");
            var preset = tab[0].Query<PushGrid>().Where(p => p.Source == HS2Next.UI.Core.PushSources.FacePreset).First();
            var presetHeads = lists?.PresetHeads(0);
            if (c.Check(preset != null && presetHeads != null && preset.AllItems.Count == presetHeads.Count, $"male: the preset grid has a cell per male preset head ({preset?.AllItems.Count} / {presetHeads?.Count})"))
            {
                string headNow = U4Ref(ui, id, "head");
                var cell = preset.AllItems.FirstOrDefault(x => x.Ref != headNow && x.Row != null && !x.Row.IsVanilla) ?? preset.AllItems.FirstOrDefault(x => x.Ref != headNow);
                if (c.Check(cell != null, "male: a preset cell of another head"))
                {
                    string before = U4Json(ui, id, "appearance.chara.face");
                    int u = U4Undo(ui);
                    yield return U4ClickCell(c, s, preset, cell.Ref, "the male preset cell " + cell.Ref);
                    for (int i = 0; i < 300 && U4Json(ui, id, "appearance.chara.face") == before; i++) yield return c.Frames(1);
                    yield return c.Frames(3);
                    U4Entry(c, ui, u, U4FacePreset, "the male preset push");
                    c.Info($"UI4: male preset push {cell.Ref} ({cell.Name}) → face.head {U4Ref(ui, id, "head")}, warnings {AcceptanceCtx.Short(string.Join("; ", (IEnumerable<string>)preset.LastResult?.Warnings ?? Array.Empty<string>()))}");
                }
            }

            // Beard hidden after selecting BR-Chan
            yield return c.Call(new { cmd = "select", key = s.F });
            yield return c.Frames(4);
            yield return U4OpenPage(c, s, s.F, null, tab);
            if (tab[0] != null)
            {
                var beardBtn = tab[0].Q<Button>("edit-page-face-beard");
                c.Check(beardBtn != null && beardBtn.style.display == DisplayStyle.None, "the Beard page is hidden after selecting BR-Chan");
            }
        }

        /// <summary>B's open point: the importer cost the first edit session of a card-backed node pays (report only).</summary>
        static void U4ImportCost(AcceptanceCtx c, U4State s)
        {
            c.Id = "UI4";
            var n = s.F != null ? s.Ui.Model.Find(s.F) : null;
            var cardSrc = n?.Appearance?.Card;
            if (cardSrc == null || CharaImport.Importer == null) { c.Info("UI4: importer cost not measured (no card source on the node)"); return; }
            var ms = new List<double>();
            for (int i = 0; i < 3; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try { CharaImport.Importer.Import(cardSrc, new FindingList()); } catch (Exception e) { c.Info("UI4: importer threw " + e.GetType().Name + ": " + e.Message); return; }
                ms.Add(sw.Elapsed.TotalMilliseconds);
            }
            c.Info($"UI4 timing ({(Application.isEditor ? "Editor" : "player")}): the card import a first edit session of a card-backed node runs (EditReferences.CaptureNode, BR-Chan): {string.Join(" / ", ms.Select(x => x.ToString("0.0")))} ms");
        }

        // ---------------------------------------------------------------- helpers

        static int U4Undo(UiContext ui) => ui.Session.Commands.UndoList.Count;

        static void U4Entry(AcceptanceCtx c, UiContext ui, int before, string label, string what)
        {
            var l = ui.Session.Commands.UndoList;
            c.Check(l.Count == before + 1 && l[0].Label == label, $"{what}: one history entry \"{label}\" ({l.Count - before} entries, last \"{(l.Count > 0 ? l[0].Label : "")}\")");
        }

        static string U4Ref(UiContext ui, string id, string member)
        {
            try { return FieldAccessor.Get(ui.Model, id, U4Face + member) as string; } catch (Exception) { return null; }
        }

        static string U4Json(UiContext ui, string id, string path)
        {
            try { return FieldAccessor.GetJson(ui.Model, id, path); } catch (Exception) { return null; }
        }

        static double[] U4Vec(UiContext ui, string id, string path)
        {
            try { return JToken.Parse(FieldAccessor.GetJson(ui.Model, id, path)) is JArray a ? a.Select(x => (double)x).ToArray() : null; }
            catch (Exception) { return null; }
        }

        static string U4Num(double[] v) => v == null ? "null" : "(" + string.Join(", ", v.Select(x => x.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture))) + ")";

        /// <summary>The canonical JSON of a face block (the member "face" of a document holding it).</summary>
        static string U4FaceText(FaceData face)
        {
            if (face == null) return null;
            var doc = new CharaDocument { Face = face };
            var j = JObject.Parse(Encoding.UTF8.GetString(CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, doc.Write)));
            return j["face"]?.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>The first differing leaves of two JSON texts (report).</summary>
        static string U4JsonDiff(string a, string b)
        {
            if (a == null || b == null) return a == null ? "first is null" : "second is null";
            JToken ja, jb;
            try { ja = JToken.Parse(a); jb = JToken.Parse(b); } catch (Exception) { return "not JSON"; }
            var diffs = new List<string>();
            void Walk(JToken x, JToken y, string p)
            {
                if (diffs.Count >= 6) return;
                if (x is JObject ox && y is JObject oy)
                {
                    foreach (var k in ox.Properties().Select(q => q.Name).Union(oy.Properties().Select(q => q.Name)))
                        Walk(ox[k], oy[k], p + "." + k);
                    return;
                }
                if (x is JArray ax && y is JArray ay && ax.Count == ay.Count)
                {
                    for (int i = 0; i < ax.Count; i++) Walk(ax[i], ay[i], p + "[" + i + "]");
                    return;
                }
                if (!JToken.DeepEquals(x, y))
                    diffs.Add(p + ": " + AcceptanceCtx.Short(x?.ToString(Newtonsoft.Json.Formatting.None) ?? "-") + " vs " + AcceptanceCtx.Short(y?.ToString(Newtonsoft.Json.Formatting.None) ?? "-"));
            }
            Walk(ja, jb, "");
            return diffs.Count == 0 ? "equal JSON, different text" : string.Join("; ", diffs);
        }

        static IEnumerator U4Docs(AcceptanceCtx c, string id, string[] into)
        {
            yield return c.Call(new { cmd = "chara.edit.get", key = id }, 120);
            into[0] = c.Last != null && c.Last.Ok ? c.J["values"]?.ToString(Newtonsoft.Json.Formatting.None) : null;
        }

        static IEnumerator U4Digest(AcceptanceCtx c, string id, JObject[] into)
        {
            yield return c.Call(new { cmd = "chara.edit.digest", key = id, parts = "face", text = true }, 120);
            into[0] = c.Last != null && c.Last.Ok ? (JObject)c.J.DeepClone() : null;
        }

        static readonly string[] U4DigestSections = { "materials", "creators", "bones", "rts" };

        static string U4DigestLine(JObject d)
        {
            if (d == null) return "none";
            return string.Join(", ", U4DigestSections.Where(k => d[k] != null).Select(k =>
            {
                string v = d[k].Type == JTokenType.String ? (string)d[k] : d[k].ToString(Newtonsoft.Json.Formatting.None);
                return k + " " + (v.Length > 12 ? v.Substring(0, 12) : v);
            }));
        }

        static bool U4SameDigest(JObject a, JObject b, out string detail)
        {
            detail = "";
            if (a == null || b == null) { detail = "no digest"; return false; }
            var bad = new List<string>();
            int looks = 0;
            foreach (var k in U4DigestSections)
            {
                if (a[k] == null && b[k] == null) continue;
                if (JToken.DeepEquals(a[k], b[k])) continue;
                string ta = (string)a["texts"]?[k], tb = (string)b["texts"]?[k];
                if (k == "bones" && ta != null && tb != null && U4SameBonesButLook(ta, tb, out int look)) { looks += look; continue; }
                bad.Add(k + (ta != null && tb != null ? " (" + U4FirstLine(ta, tb) + ")" : ""));
            }
            detail = bad.Count == 0 ? string.Join(", ", U4DigestSections.Where(k => a[k] != null)) + " equal" + (looks > 0 ? $" (bones: {looks} lines of the eye-look bones cf_J_look_* left out: the look stage writes them every frame)" : "") : "differ: " + string.Join("; ", bad);
            return bad.Count == 0;
        }

        /// <summary>The bone texts are equal except the lines of the eye-look bones (written by the look stage each frame).</summary>
        static bool U4SameBonesButLook(string a, string b, out int look)
        {
            look = 0;
            var la = a.Split('\n');
            var lb = b.Split('\n');
            if (la.Length != lb.Length) return false;
            for (int i = 0; i < la.Length; i++)
            {
                if (la[i] == lb[i]) continue;
                if (!la[i].Contains(" cf_J_look_") || !lb[i].Contains(" cf_J_look_")) return false;
                look++;
            }
            return true;
        }

        static string U4FirstLine(string a, string b)
        {
            var la = a.Split('\n');
            var lb = b.Split('\n');
            for (int i = 0; i < Math.Max(la.Length, lb.Length); i++)
            {
                string x = i < la.Length ? la[i] : "-", y = i < lb.Length ? lb[i] : "-";
                if (x != y) return $"line {i + 1}: {AcceptanceCtx.Short(x)} vs {AcceptanceCtx.Short(y)}";
            }
            return "texts equal";
        }

        static PartList U4List(VisualElement root, string path) => root?.Query<PartList>().Where(p => p.Path == path).First();

        static MakerSliderRow U4Slider(VisualElement root, string path, int component) =>
            root?.Query<MakerSliderRow>().Where(r => r.Path == path && r.Spec.Component == component).First();

        static bool U4Marked(PartList l, string reference)
        {
            var it = l?.Items.FirstOrDefault(x => !x.NotListed && x.Ref == reference);
            return it != null && l.CurrentRef == reference && l.Grid.IsCurrent != null && l.Grid.IsCurrent(it);
        }

        /// <summary>Scrolls every scroll view around <paramref name="e"/> to it (inner first) and gives its centre.</summary>
        static IEnumerator U4Reveal(AcceptanceCtx c, U4State s, VisualElement e, string what, Vector2[] point) => RevealPastToasts(c, s.Host, e, what, point);

        /// <summary>Scrolls every scroll view around <paramref name="e"/> to it (inner first), waits for a toast over it to go
        /// (up to about 10 s) and gives its centre (UI3 and UI4).</summary>
        static IEnumerator RevealPastToasts(AcceptanceCtx c, UiHost host, VisualElement e, string what, Vector2[] point)
        {
            for (var x = e.hierarchy.parent; x != null; x = x.hierarchy.parent)
                if (x is ScrollView sv) sv.ScrollTo(e);
            yield return c.Frames(3);
            point[0] = e.worldBound.center;
            var picked = host.Root?.panel?.Pick(point[0]);
            bool hit = picked != null && (picked == e || e.Contains(picked));
            // a toast over the target goes away by itself: wait for it (up to about 10 s)
            for (int i = 0; i < 600 && !hit && U4InToast(picked); i++)
            {
                if (i == 0) c.Info($"{c.Id}: {what} is under a toast (\"{AcceptanceCtx.Short((picked as TextElement)?.text)}\"); waiting for it");
                yield return c.Frames(1);
                point[0] = e.worldBound.center;
                picked = host.Root?.panel?.Pick(point[0]);
                hit = picked != null && (picked == e || e.Contains(picked));
            }
            if (!hit) c.Info($"{c.Id}: {what} at {point[0]} is covered by {(picked == null ? "nothing" : (picked.name ?? "") + " (" + picked.GetType().Name + ")")}");
        }

        static bool U4InToast(VisualElement e)
        {
            for (var x = e; x != null; x = x.hierarchy.parent)
                if ((x.name != null && x.name.IndexOf("toast", StringComparison.OrdinalIgnoreCase) >= 0) || x.ClassListContains("hs-toast")) return true;
            return false;
        }

        static IEnumerator U4Click(AcceptanceCtx c, U4State s, VisualElement e, string what)
        {
            if (!c.Check(e != null, what + " exists")) yield break;
            var p = new Vector2[1];
            yield return U4Reveal(c, s, e, what, p);
            yield return c.Call(new { cmd = "ui.input", frames = 4, events = new object[] { new { type = "pointer", phase = "click", x = p[0].x, y = p[0].y, button = 0 } } });
            c.CheckOk("ui.input click on " + what);
        }

        /// <summary>Clicks the cell of <paramref name="reference"/> in a Maker list or push grid: the list into view, its grid row
        /// into view, then a pointer click on the cell.</summary>
        static IEnumerator U4ClickCell(AcceptanceCtx c, U4State s, MakerThumbList list, string reference, string what)
        {
            if (!c.Check(list != null, what + ": the list exists")) yield break;
            int pos = list.PositionOf(reference);
            if (!c.Check(pos >= 0, $"{what}: the cell is shown ({list.Items.Count} cells)")) yield break;
            yield return U4Reveal(c, s, list, what + " (its list)", new Vector2[1]);
            list.Grid.ScrollToPos(pos);
            VisualElement cell = null;
            for (int i = 0; i < 30; i++)
            {
                yield return c.Frames(1);
                // the shown cell of that name (cells of unused grid columns keep an earlier name while hidden)
                string cellName = list.CellOf(reference)?.name;
                cell = cellName == null ? null : list.Grid.Query<VisualElement>(cellName).Where(x => x.worldBound.width > 0 && x.resolvedStyle.display != DisplayStyle.None && x.resolvedStyle.visibility == Visibility.Visible).First();
                if (cell != null) break;
            }
            if (!c.Check(cell != null && cell.worldBound.width > 0, what + ": the cell element is bound",
                $"pos {pos}, columns {list.Grid.Columns}, grid {list.Grid.worldBound}, list view {list.Grid.List.worldBound}, cell {(cell == null ? "null" : cell.worldBound.ToString())}; bound: " +
                string.Join(", ", list.Grid.Query<VisualElement>(className: "hs-card").Where(x => x.resolvedStyle.visibility == Visibility.Visible).ToList().Select(x => x.name).Take(12)))) yield break;
            yield return U4Click(c, s, cell, what);
        }

        /// <summary>Picks a row of <paramref name="list"/> other than the current value (the first such shown cell) → one entry
        /// "Change item" and the member holds it.</summary>
        static IEnumerator U4PickOther(AcceptanceCtx c, U4State s, PartList list, string id, string member, string what, string[] picked = null)
        {
            var ui = s.Ui;
            if (!c.Check(list != null && list.resolvedStyle.display != DisplayStyle.None, what + " is shown")) yield break;
            string cur = U4Ref(ui, id, member);
            var target = list.Items.Where(x => !x.NotListed && x.Row != null && x.Ref != cur).Skip(1).FirstOrDefault() ?? list.Items.FirstOrDefault(x => !x.NotListed && x.Row != null && x.Ref != cur);
            if (!c.Check(target != null, what + ": another row than " + cur)) yield break;
            int u = U4Undo(ui);
            yield return U4ClickCell(c, s, list, target.Ref, what + " cell " + target.Ref);
            for (int i = 0; i < 120 && U4Ref(ui, id, member) != target.Ref && list.PendingRef != null; i++) yield return c.Frames(1);
            yield return c.Frames(3);
            U4Entry(c, ui, u, U4ChangeItem, what + " pick");
            c.Check(U4Ref(ui, id, member) == target.Ref, $"face.{member} = {U4Ref(ui, id, member)} ({target.Ref} {target.Name}; was {cur})");
            if (picked != null) picked[0] = target.Ref;
        }

        /// <summary>A pointer drag on a slider's knob (20 % of the track, away from the nearer end).</summary>
        static IEnumerator U4DragKnob(AcceptanceCtx c, U4State s, MakerSliderRow r, float v0, string what)
        {
            var knob = r.Control.Slider?.Q("unity-dragger");
            if (!c.Check(knob != null, what + ": the slider's knob")) yield break;
            var kp = new Vector2[1];
            yield return U4Reveal(c, s, knob, what + " knob", kp);
            float track = r.Control.Slider.Q("unity-drag-container")?.worldBound.width ?? 100f;
            float dx = (v0 > 0.5f ? -1 : 1) * Mathf.Max(8f, track * 0.2f);
            var p0 = kp[0];
            yield return c.Call(new
            {
                cmd = "ui.input", frames = 3,
                events = new object[]
                {
                    new { type = "pointer", phase = "down", x = p0.x, y = p0.y, button = 0 },
                    new { type = "pointer", phase = "move", x = p0.x + dx / 3, y = p0.y, button = 0 },
                    new { type = "pointer", phase = "move", x = p0.x + 2 * dx / 3, y = p0.y, button = 0 },
                    new { type = "pointer", phase = "move", x = p0.x + dx, y = p0.y, button = 0 },
                    new { type = "pointer", phase = "up", x = p0.x + dx, y = p0.y, button = 0 },
                },
            });
            c.CheckOk("ui.input drag on " + what);
            yield return c.Frames(4);
            c.Check(!r.Dragging, what + ": no drag transaction left open");
        }

        /// <summary>Clicks a text field, selects all, types <paramref name="text"/> (Backspace for an empty text) and presses Enter.</summary>
        static IEnumerator U4Type(AcceptanceCtx c, U4State s, VisualElement field, string text, string what)
        {
            if (!c.Check(field != null, what + " exists")) yield break;
            var p = new Vector2[1];
            yield return U4Reveal(c, s, field.Q("unity-text-input") ?? field, what, p);
            var ev = new List<object>
            {
                new { type = "pointer", phase = "click", x = p[0].x, y = p[0].y, button = 0 },
                new { type = "key", key = "A", mods = new[] { "ctrl" } },
            };
            // an empty text: a space (ui.input's Delete / Backspace keys do not edit a text field; the field selects all on focus
            // and the normalised search of a blank text matches every row)
            if (string.IsNullOrEmpty(text)) ev.Add(new { type = "text", text = " " });
            else ev.Add(new { type = "text", text });
            ev.Add(new { type = "key", key = "Return" });
            yield return c.Call(new { cmd = "ui.input", frames = 3, events = ev.ToArray() });
            c.CheckOk("ui.input: \"" + text + "\" typed into " + what);
            yield return c.Frames(3);
            if (field is TextField tf && tf.value.Trim() != text) c.Info($"UI4: {what} holds \"{tf.value}\" after typing \"{text}\"");
        }

        /// <summary>A colour row's picker: a drag in the SV square over 3 frames, Enter → one entry, the colour changed, alpha kept.</summary>
        static IEnumerator U4ColourDrag(AcceptanceCtx c, U4State s, VisualElement root, string id, string path, string what)
        {
            var ui = s.Ui;
            var row = root.Query<MakerColorRow>().Where(r => r.Path == path).First();
            if (!c.Check(row != null, what + " row")) yield break;
            var c0 = U4Vec(ui, id, path);
            int u = U4Undo(ui);
            yield return U4Click(c, s, row.Swatch.Button, what + " swatch");
            yield return c.Frames(4);
            var picker = row.Picker;
            if (!c.Check(picker != null && picker.panel != null, what + ": the colour picker is open")) yield break;
            var sv = picker.Q("sv");
            if (!c.Check(sv != null && sv.worldBound.width > 0, what + ": the SV square")) yield break;
            var b = sv.worldBound;
            var p0 = new Vector2(b.xMin + b.width * 0.35f, b.yMin + b.height * 0.35f);
            var d = new Vector2(b.width * 0.3f, b.height * 0.3f);
            yield return c.Call(new
            {
                cmd = "ui.input", frames = 3,
                events = new object[]
                {
                    new { type = "pointer", phase = "down", x = p0.x, y = p0.y, button = 0 },
                    new { type = "pointer", phase = "move", x = p0.x + d.x / 3, y = p0.y + d.y / 3, button = 0 },
                    new { type = "pointer", phase = "move", x = p0.x + 2 * d.x / 3, y = p0.y + 2 * d.y / 3, button = 0 },
                    new { type = "pointer", phase = "move", x = p0.x + d.x, y = p0.y + d.y, button = 0 },
                    new { type = "pointer", phase = "up", x = p0.x + d.x, y = p0.y + d.y, button = 0 },
                },
            });
            c.CheckOk("ui.input drag in the SV square of " + what);
            yield return c.Call(new { cmd = "ui.input", frames = 3, events = new object[] { new { type = "key", key = "Return" } } });
            c.CheckOk("ui.input Enter in the picker of " + what);
            yield return c.Frames(3);
            if (!c.Check(row.Picker == null, what + ": Enter closed the picker"))
            {
                row.Picker?.Close(true);
                yield return c.Frames(2);
            }
            var c1 = U4Vec(ui, id, path);
            U4Entry(c, ui, u, HS2Next.UI.Core.OpLabels.EditFace, what + " picker session");
            c.Check(c0 != null && c1 != null && c0.Length == 4 && c1.Length == 4 && (c1[0] != c0[0] || c1[1] != c0[1] || c1[2] != c0[2]) && c1[3] == c0[3],
                $"{what}: the stored colour changed, alpha kept ({U4Num(c0)} → {U4Num(c1)})");
        }

        /// <summary>A layout push of the first of <paramref name="rows"/> whose vector differs from the stored layout → one entry
        /// "Edit face" and the layout = (Scale, Scale, PosX, PosY) of the row.</summary>
        static IEnumerator U4LayoutPush(AcceptanceCtx c, U4State s, PushGrid grid, string id, int category, string path, string[] rows, string what)
        {
            var ui = s.Ui;
            var cur = U4Vec(ui, id, path);
            string pick = null;
            double[] want = null;
            foreach (var r in rows)
            {
                if (MakerServices.Rows == null || !MakerServices.Rows.LayoutValues(category, r, out float sc, out float x, out float y)) continue;
                var w = new double[] { sc, sc, x, y };
                if (cur != null && cur.Length == 4 && Enumerable.Range(0, 4).All(i => Math.Abs(cur[i] - w[i]) < 1e-6)) { c.Info($"UI4: {what} already holds row {r}'s layout {U4Num(w)}; the next row is pushed"); continue; }
                pick = r; want = w;
                break;
            }
            if (!c.Check(pick != null, $"{what}: a layout row whose vector differs from the stored {U4Num(cur)}")) yield break;
            int u = U4Undo(ui);
            yield return U4ClickCell(c, s, grid, pick, $"the {what} layout cell {pick}");
            yield return c.Frames(3);
            U4Entry(c, ui, u, HS2Next.UI.Core.OpLabels.EditFace, what + " layout push");
            var got = U4Vec(ui, id, path);
            c.Check(got != null && got.Length == 4 && Enumerable.Range(0, 4).All(i => Math.Abs(got[i] - want[i]) < 1e-6), $"{what}: layout = row {pick}'s (S, S, X, Y) {U4Num(want)} ({U4Num(got)})");
        }

        /// <summary>Selects the character, opens the inspector's Edit tab, the Face node and <paramref name="page"/> (null = keep the
        /// page) by clicks.</summary>
        static IEnumerator U4OpenPage(AcceptanceCtx c, U4State s, string id, string page, CharaEditTab[] tab)
        {
            var ui = s.Ui;
            var host = s.Host;
            tab[0] = null;
            if (ui.Selection.Primary != id) { yield return c.Call(new { cmd = "select", key = id }); yield return c.Frames(3); }
            yield return c.Call(new { cmd = "ui.panel", panel = "inspector", op = "focus" });
            yield return c.Frames(4);
            var body = host.Root.Q<CharaEditTab>();
            if (body == null || body.panel == null || body.PrimaryId != id)
            {
                var tb = host.Root.Q<Button>("tab-" + CharaEditTab.TabId);
                if (!c.Check(tb != null, "the character inspector's Edit tab")) yield break;
                yield return U4Click(c, s, tb, "the Edit tab");
                body = host.Root.Q<CharaEditTab>();
            }
            if (!c.Check(body != null && body.panel != null && body.PrimaryId == id, "the Edit tab of " + id + " is shown")) yield break;
            if (body.CurrentNode != "face")
            {
                yield return U4Click(c, s, body.Q<Button>("edit-tree-face"), "the Face node");
                if (!c.Check(body.CurrentNode == "face", "Edit › Face is open (" + body.CurrentNode + ")")) yield break;
            }
            if (page != null && body.CurrentPage != page)
            {
                var pb = body.Q<Button>("edit-page-face-" + page);
                if (!c.Check(pb != null && pb.style.display != DisplayStyle.None, "the sub-page button edit-page-face-" + page + " is shown")) yield break;
                yield return U4Click(c, s, pb, "edit-page-face-" + page);
                yield return c.Frames(2);
            }
            if (page != null && !c.Check(body.CurrentPage == page && body.HasSession, $"Face › {page} is open with the edit session ({body.CurrentPage})")) yield break;
            tab[0] = body;
        }
    }
}
