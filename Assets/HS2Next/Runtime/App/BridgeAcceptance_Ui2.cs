// UI2 of the bridge acceptance (Phase 3.6 integrator G): "build a scene from scratch
// through the UI" in Editor play (BridgeTest.RunPlayUi after UI1) and in the app player (-bridgeAccept with -ui, after UI1).
// Every action goes through the bridge's ui.* commands (ui.action, ui.panel, ui.input clicks / typing / drags / keys); the
// checks read the model through the bridge (history, selection, scene, scene.doc, route.state, camera.get, scene.images,
// chara.clothes) and, like UI1, the element geometry and a few values in-process (the case runs inside the app's process in
// both hosts). Content: the SFW targets (BR-Chan, HERO107); clothes states by numbers only, no capture of them is viewed.
//   1 file.new; Add ▸ Female / Male in the card browser by search text (double click) → at the camera pivot, selected
//   2 the anime tab's Choose → the animation browser: Idle_00 for each; BR-Chan's speed typed 0.5 (one entry)
//   3 the item browser (a vanilla prop by name) → its tree row dropped on BR-Chan's row → attach-point menu → Right hand;
//     position / rotation typed in the Transform rows
//   4 the map browser: map 10 (loaded); the scene panel's map lights off and on
//   5 Add ▸ Light ▸ a spot light; intensity and spot angle typed; a gizmo drag on move X (one entry)
//   6 effects: bloom intensity typed, vignette off; sky on with pattern 3 (the pattern picker)
//   7 BR-Chan's Face tab: eyes open 0.5, mouth ▶; State tab: the top "half" (numbers)
//   8 FK / IK tab: IK on, the RightHand target sub-selected and dragged with the gizmo (one entry); FK neck group on
//   9 Add ▸ Route; Add point × 3 from the inspector (each continues the path, R8); point 2 Curve; point 3 moved with the
//     gizmo; Add ▸ Folder dropped on the route's row (rides the child root); Play
//  10 the toolbar's Pause; Step × 30; τ advanced by 30 × 1/60 exactly; Resume
//  11 camera.frame; slot 1 saved from the scene panel; the view changed with the wheel; slot 1 loaded (the view equal)
//  12 the scene panel's background = the first DefaultData/bg image, frame = the first UserData/frame image (when present)
//  13 Shift+F11 → the dialog: scale 2, supersample 1 → Take → a PNG of 2 × the fit size; scale 1; F11 → 1 ×
//  14 File ▸ Save As "ui2"; (15) the history list: back to step 9's document, redo all → the saved document again;
//     File ▸ New; File ▸ Open "ui2" → scene.doc equal to the saved one (without UI1's captured members)
//  16 the language menu en → ja → en; F1 opens the key overlay (Esc closes it)
// Step 15 runs before New / Open (the history does not survive opening a file). Timings are printed per step.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Routes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.App
{
    public static partial class BridgeAcceptance
    {
        /// <summary>UI2's male card, relative to the HS2 male card folder (BridgeTest sets it from Tools/targets.json).</summary>
        public static string Ui2MaleRel = "HERO107.png";

        /// <summary>The state UI2's steps share.</summary>
        sealed class U2
        {
            public AcceptanceCtx C;
            public UiHost Host;
            public UiContext Ui;
            public string Female, Male, Item, Light, Route, Folder;
            public int HistN;
            public string HistTop;
            public bool Ok;
            /// <summary>Step 9's end: the undo count and the document digest (step 15 returns there).</summary>
            public int Undo9 = -1;
            public string Digest9;
            /// <summary>The document saved in step 14.</summary>
            public string SavedDigest, SavedViewless, SavedPath;
            public JToken SavedDoc;
        }

        public static IEnumerator Ui2(AcceptanceCtx c)
        {
            c.Id = "UI2";
            yield return c.Call(new { cmd = "ui.state" });
            if (c.Last == null || !c.Last.Ok || (bool?)c.J["enabled"] != true)
            {
                c.Warn("UI2 skipped: the Studio UI is not running (start with -ui; " + AcceptanceCtx.Short(c.Last?.ToString()) + ")");
                yield break;
            }
            var s = new U2 { C = c, Host = UiHost.Current, Ui = UiHost.Current?.Context };
            if (!c.Check(s.Ui != null, "UiHost in the app's process")) yield break;
            c.Info($"UI2: panel scale {(double?)c.J["panelScale"]}, screen {Screen.width} x {Screen.height}, preset {(string)c.J["preset"]}");
            var steps = new List<(string name, Func<U2, IEnumerator> run)>
            {
                ("characters", U2Step1), ("animation", U2Step2), ("item", U2Step3), ("map", U2Step4), ("light", U2Step5),
                ("effects", U2Step6), ("face / state", U2Step7), ("FK / IK", U2Step8), ("route", U2Step9), ("pause / step", U2Step10),
                ("camera", U2Step11), ("background / frame", U2Step12), ("screenshot", U2Step13), ("save / history / open", U2Step14),
                ("language / help", U2Step16),
            };
            var total = Stopwatch.StartNew();
            var times = new List<string>();
            for (int i = 0; i < steps.Count; i++)
            {
                string no = i == steps.Count - 1 ? "16" : (i + 1).ToString();   // step 15 runs inside 14
                c.Id = "UI2." + no;
                var sw = Stopwatch.StartNew();
                yield return Guard(c, "UI2." + no, steps[i].run(s));
                // a step that left a dialog or a menu open must not block the next one
                if (s.Ui.Dialogs != null && s.Ui.Dialogs.IsModalOpen) { c.Info($"UI2.{no}: closing the dialog left open ({s.Ui.Dialogs.Current?.Spec.Id})"); s.Ui.Dialogs.CloseCurrent(); }
                if (s.Ui.Menus != null && s.Ui.Menus.IsOpen) s.Ui.Menus.CloseAll();
                times.Add($"{no} {steps[i].name} {sw.Elapsed.TotalSeconds:0.0} s");
                c.Info($"UI2 timing: step {no} ({steps[i].name}) {sw.Elapsed.TotalSeconds:0.00} s");
            }
            c.Id = "UI2";
            // leave the app as the next case expects it: running, English
            var rt = s.Ui.Runtime;
            if (rt != null && rt.Paused) yield return U2Act(s, "time.pause", new { paused = false });
            if (s.Ui.L10n.Language != "en") yield return U2Act(s, "ui.language", new { language = "en" });
            c.Info($"UI2 timing: total {total.Elapsed.TotalSeconds:0.0} s ({string.Join("; ", times)})");
        }

        // ================================================================ steps

        /// <summary>1: file.new; both characters through the card browser (search text, double click) at the pivot, selected.</summary>
        static IEnumerator U2Step1(U2 s)
        {
            var c = s.C;
            var ui = s.Ui;
            yield return U2Act(s, "file.new", new { force = true });
            c.CheckOk("ui.action file.new {force}");
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "scene" });
            c.Check((c.J["objects"] as JArray)?.Count == 0, "the scene is empty");
            foreach (int sex in new[] { 1, 0 })
            {
                string rel = sex == 1 ? c.FemaleRel : Ui2MaleRel;
                string what = sex == 1 ? "female" : "male";
                yield return U2Act(s, sex == 1 ? "add.female" : "add.male");
                c.CheckOk("ui.action add." + what + " (no path: the card browser of that sex)");
                yield return U2Wait(s, () => CharaBrowser.Current != null && CharaBrowser.Current.Sex == sex && CharaBrowser.Current.Ready, 120);
                var cb = CharaBrowser.Current;
                if (!c.Check(s.Ok, "the " + what + " card browser scanned its cards", cb?.Library?.Entries.Count + " cards")) continue;
                string full = Path.GetFullPath(Path.Combine(AppCore.Current.Paths.Hs2CharaDir(sex) ?? "", rel ?? ""));
                var entry = cb.Library.Entries.FirstOrDefault(e => string.Equals(Path.GetFullPath(e.FullPath), full, StringComparison.OrdinalIgnoreCase));
                if (!c.Check(entry != null, "the " + what + " target card is in the browser", full)) continue;
                string query = Path.GetFileNameWithoutExtension(rel);
                yield return U2Type(s, () => cb.Q<TextField>("chara-search"), query, "the card search field");
                int pos = -1;
                var swq = Stopwatch.StartNew();
                while (swq.Elapsed.TotalSeconds < 10)
                {
                    pos = -1;
                    var idx = cb.Index;
                    for (int i = 0; idx != null && i < idx.Indices.Count; i++) if (idx.Rows[idx.Indices[i]] == entry) { pos = i; break; }
                    if (pos >= 0 && idx.Indices.Count <= 20) break;
                    yield return c.Frames(2);
                }
                if (!c.Check(pos >= 0, $"search \"{query}\" lists the card", $"{cb.Index?.Indices.Count} results")) continue;
                c.Info($"UI2.1: search \"{query}\": {cb.Index.Indices.Count} of {cb.Index.Rows.Count} cards, the target at {pos}");
                cb.Grid.List.ScrollToItem(pos / Math.Max(1, cb.Grid.Columns));
                yield return c.Frames(4);
                yield return U2Hist(s);
                int h0 = s.HistN;
                yield return c.Call(new { cmd = "camera.get" });
                var pivot = U2V(c.J["camera"]?["target"]);
                int p = pos;
                yield return U2Click(s, () => cb.Grid.Q("card-" + p), "the card cell (double click)", clicks: 2, frames: 4);
                yield return U2Wait(s, () => ui.Session.Commands.UndoList.Count > h0, 120);
                yield return U2Hist(s);
                c.Check(s.HistN == h0 + 1 && s.HistTop == OpLabels.AddCharacter, $"one entry \"{OpLabels.AddCharacter}\"", s.HistN + " / " + s.HistTop);
                yield return c.Call(new { cmd = "selection" });
                string id = (string)c.J["primary"];
                c.Check(id != null && ui.Model.Find(id)?.Kind == NodeKind.Chara && (c.J["selected"] as JArray)?.Count == 1, "the new character is the selection (U14)", id);
                if (id == null) continue;
                if (sex == 1) s.Female = id; else s.Male = id;
                yield return U2Wait(s, () => ui.Runtime.FindChara(id)?.State == RealizationState.Built, 180);
                c.Check(s.Ok, "the " + what + " character is built");
                var pos3 = ui.Model.Find(id).Doc.Transform.Pos;
                c.Check(Math.Abs(pos3.X - pivot.x) < 1e-4 && Math.Abs(pos3.Y - pivot.y) < 1e-4 && Math.Abs(pos3.Z - pivot.z) < 1e-4,
                    "it lands at the camera pivot (option add.placement pivot)", $"{pos3.X}, {pos3.Y}, {pos3.Z} vs {pivot}");
            }
            yield return U2Type(s, () => CharaBrowser.Current?.Q<TextField>("chara-search"), "", "clearing the card search");
        }

        /// <summary>2: Idle_00 for each character from the animation browser; BR-Chan's speed typed 0.5 (one entry).</summary>
        static IEnumerator U2Step2(U2 s)
        {
            var c = s.C;
            var ui = s.Ui;
            if (!U2Need(s, s.Female, s.Male)) yield break;
            foreach (var id in new[] { s.Female, s.Male })
            {
                yield return U2SelectRow(s, id);
                yield return U2Tab(s, "anime");
                yield return U2Click(s, () => U2Inspector(s)?.Q("field-chara.anime.ref")?.Q<Button>("choose"), "the anime tab's Choose button");
                yield return c.Frames(4);
                var ab = AddModule.AnimeBrowser;
                if (!c.Check(ab != null && ab.panel != null, "Choose shows the animation browser")) yield break;
                var row = ab.Index.Rows.FirstOrDefault(a => a.Group == 19 && a.Category == 0 && a.No == 0);
                if (!c.Check(row != null, "the animation 19/0/0 (Idle_00) is listed")) yield break;
                yield return U2PickListRow(s, ab.List, () => ab.Q<TextField>("anime-search"), row.Name, r => r == row, ab.Index.Rows, ab.Index.Indices, "the Idle row");
                yield return U2Hist(s);
                int h0 = s.HistN;
                yield return U2Click(s, () => ab.Q<Button>("anime-action"), "the browser's Apply button");
                yield return U2Hist(s);
                var a = U2Get(s, id, "chara.anime.ref") as AnimeRef;
                c.Check(a != null && a.Group == 19 && a.Category == 0 && a.No == 0 && s.HistN == h0 + 1 && s.HistTop == OpLabels.Animation,
                    $"{(id == s.Female ? "BR-Chan" : "HERO107")} plays 19/0/0 \"{row.Name}\", one entry \"{OpLabels.Animation}\"", $"{a?.Group}/{a?.Category}/{a?.No}, {s.HistN - h0} / {s.HistTop}");
            }
            yield return U2SelectRow(s, s.Female);
            yield return U2Tab(s, "anime");
            yield return U2Hist(s);
            int h1 = s.HistN;
            yield return U2Type(s, () => U2Number(U2Inspector(s), "chara.anime.speed"), "0.5", "the speed slider's field");
            yield return U2Hist(s);
            var sp = U2Get(s, s.Female, "chara.anime.speed");
            c.Check(U2Num(sp) == 0.5 && s.HistN == h1 + 1 && s.HistTop == OpLabels.AnimationSpeed, $"BR-Chan's speed 0.5, one entry \"{OpLabels.AnimationSpeed}\"", $"{sp}, {s.HistN - h1} / {s.HistTop}");
        }

        /// <summary>3: a vanilla prop from the item browser, dropped on BR-Chan's tree row → attach point Right hand; Transform typed.</summary>
        static IEnumerator U2Step3(U2 s)
        {
            var c = s.C;
            var ui = s.Ui;
            if (!U2Need(s, s.Female)) yield break;
            yield return U2Act(s, "add.item");
            c.CheckOk("ui.action add.item (no ref: the item browser)");
            yield return c.Frames(4);
            var ib = AddModule.ItemBrowser;
            if (!c.Check(ib != null && ib.panel != null, "the item browser is shown")) yield break;
            var row = ib.Index.Rows.FirstOrDefault(r => r.IsVanilla && r.Key.Id == 399) ?? ib.Index.Rows.FirstOrDefault(r => r.IsVanilla);
            if (!c.Check(row != null, "a vanilla item row", row?.Name)) yield break;
            yield return U2PickListRow(s, ib.List, () => ib.Q<TextField>("items-search"), row.Name, r => r == row, ib.Index.Rows, ib.Index.Indices, "the item row");
            yield return U2Hist(s);
            int h0 = s.HistN;
            yield return U2Click(s, () => ib.Q<Button>("items-action"), "the browser's Add button");
            yield return U2Hist(s);
            yield return c.Call(new { cmd = "selection" });
            s.Item = (string)c.J["primary"];
            c.Check(s.Item != null && ui.Model.Find(s.Item)?.Kind == NodeKind.Item && s.HistN == h0 + 1 && s.HistTop == OpLabels.AddItem,
                $"item {row.Key} \"{row.Name}\" added and selected, one entry \"{OpLabels.AddItem}\"", s.Item + ", " + s.HistTop);
            if (s.Item == null) yield break;
            yield return c.Frames(4);
            // the item's tree row onto BR-Chan's row → the attach-point menu
            yield return U2Show(s, WorkspacePanel.PanelId);
            yield return U2TreeVisible(s, s.Item);
            yield return U2TreeVisible(s, s.Female);
            var from = U2TreeRow(s, s.Item);
            var to = U2TreeRow(s, s.Female);
            if (!c.Check(from != null && to != null, "both tree rows are shown")) yield break;
            yield return U2Hist(s);
            h0 = s.HistN;
            var a = from.worldBound.center;
            var b = to.worldBound.center;
            yield return U2Drag(s, a, b);
            yield return c.Frames(3);
            const string point = "N_Hand_R";
            var table = ui.Model.AttachPoints ?? AttachPointTable.Vanilla;
            var g = table.GroupOf(point);
            var groupRow = s.Host.Root.Q("menu-item-group-" + g?.No);
            if (!c.Check(ui.Menus.IsOpen && groupRow != null, "the drop on a character row opens the attach-point menu", DragController.LastOutcome)) yield break;
            // hover the group (its submenu opens on pointer enter), then click the point
            var gc = groupRow.worldBound.center;
            yield return c.Call(new { cmd = "ui.input", frames = 3, events = new object[] { new { type = "pointer", phase = "move", x = gc.x - 4, y = gc.y }, new { type = "pointer", phase = "move", x = gc.x, y = gc.y } } });
            yield return U2Click(s, () => s.Host.Root.Q("menu-item-point-" + point), "the Right hand item of the group's submenu");
            yield return U2Hist(s);
            var n = ui.Model.Find(s.Item);
            c.Check(n?.Parent?.Id == s.Female && n.Point == point && s.HistN == h0 + 1 && s.HistTop == OpLabels.Parent,
                $"the item is attached to BR-Chan's {point}, one entry \"{OpLabels.Parent}\"", $"{n?.Parent?.Id} / {n?.Point}, {s.HistN - h0} / {s.HistTop}");
            // Transform: typed values
            yield return U2SelectRow(s, s.Item);
            yield return U2Show(s, "inspector");
            yield return U2Hist(s);
            h0 = s.HistN;
            yield return U2Type(s, () => U2Row(U2Inspector(s), "transform.pos")?.Q<FloatField>("c1"), "0.25", "the position's Y field");
            yield return U2Type(s, () => U2Row(U2Inspector(s), "transform.rot")?.Q<FloatField>("c2"), "30", "the rotation's Z field");
            yield return U2Hist(s);
            var t = ui.Model.Find(s.Item).Doc.Transform;
            c.Check(Math.Abs(t.Pos.Y - 0.25f) < 1e-5 && Math.Abs(t.Rot.Z - 30f) < 1e-4 && s.HistN == h0 + 2 && s.HistTop == OpLabels.Transform,
                $"typed transform: pos.y 0.25, rot.z 30, two entries \"{OpLabels.Transform}\"", $"{t.Pos.Y} / {t.Rot.Z}, {s.HistN - h0} / {s.HistTop}");
        }

        /// <summary>4: map 10 from the map browser; the scene panel's map lights off and on.</summary>
        static IEnumerator U2Step4(U2 s)
        {
            var c = s.C;
            var ui = s.Ui;
            yield return U2Show(s, "add.map");
            var mb = AddModule.MapBrowser;
            if (!c.Check(mb != null && mb.panel != null, "the map browser is shown")) yield break;
            var row = mb.Index.Rows.FirstOrDefault(r => r.Map != null && r.Map.IsVanilla && r.Map.Key.Id == 10);
            if (!c.Check(row != null, "map 10 is listed", row?.Name)) yield break;
            yield return U2PickListRow(s, mb.List, () => mb.Q<TextField>("maps-search"), row.Name, r => r == row, mb.Index.Rows, mb.Index.Indices, "the map row");
            yield return U2Hist(s);
            int h0 = s.HistN;
            var sw = Stopwatch.StartNew();
            yield return U2Click(s, () => mb.Q<Button>("maps-action"), "the browser's Load button");
            JToken sc = null;
            while (sw.Elapsed.TotalSeconds < 150)
            {
                yield return c.Call(new { cmd = "scene" });
                sc = c.J["map"];
                if ((string)sc?["state"] == "Loaded" || (string)sc?["state"] == "Failed") break;
                yield return c.Frames(10);
            }
            yield return U2Hist(s);
            c.Check((string)sc?["state"] == "Loaded" && (int?)sc?["no"] == 10 && s.HistN == h0 + 1 && s.HistTop == OpLabels.Map,
                $"map 10 \"{row.Name}\" loaded in {sw.Elapsed.TotalSeconds:0.0} s, one entry \"{OpLabels.Map}\"", AcceptanceCtx.Short(sc?.ToString(Newtonsoft.Json.Formatting.None)) + ", " + s.HistTop);
            yield return U2Show(s, "scene");
            yield return c.Frames(4);
            bool before = U2Get(s, null, "map.lights") as bool? ?? true;
            var toggle = U2Row(U2PanelRoot(s, "scene"), "map.lights")?.Q<Toggle>();
            if (!c.Check(toggle != null && toggle.enabledInHierarchy, "the scene panel's map lights toggle is enabled", toggle == null ? "missing" : "disabled (the map's light block?)")) yield break;
            h0 = s.HistN;
            yield return U2Click(s, () => U2Row(U2PanelRoot(s, "scene"), "map.lights")?.Q<Toggle>(), "map lights (off)");
            bool off = U2Get(s, null, "map.lights") as bool? ?? true;
            yield return U2Click(s, () => U2Row(U2PanelRoot(s, "scene"), "map.lights")?.Q<Toggle>(), "map lights (on)");
            bool on = U2Get(s, null, "map.lights") as bool? ?? false;
            yield return U2Hist(s);
            c.Check(before && !off && on && s.HistN == h0 + 2, "map lights off and on: two entries", $"{before} → {off} → {on}, {s.HistN - h0} / {s.HistTop}");
        }

        /// <summary>5: Add ▸ Light ▸ a spot light; intensity and spot angle typed; a gizmo drag on move X.</summary>
        static IEnumerator U2Step5(U2 s)
        {
            var c = s.C;
            var ui = s.Ui;
            var db = ui.App.Db;
            var lights = db?.Lights.Enumerate().ToList() ?? new List<StudioLight>();
            var spot = lights.FirstOrDefault(l => (l.Name ?? "").IndexOf("スポット", StringComparison.Ordinal) >= 0 || (l.Name ?? "").IndexOf("spot", StringComparison.OrdinalIgnoreCase) >= 0
                                                  || (l.FileName ?? "").IndexOf("spot", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!c.Check(spot != null, "a spot light row", string.Join(", ", lights.Select(l => l.Name + " / " + l.FileName)))) yield break;
            yield return U2Hist(s);
            int h0 = s.HistN;
            yield return U2Menu(s, "tb-add", "add.light", "light-" + spot.Key);
            yield return U2Hist(s);
            yield return c.Call(new { cmd = "selection" });
            s.Light = (string)c.J["primary"];
            c.Check(s.Light != null && ui.Model.Find(s.Light)?.Kind == NodeKind.Light && s.HistN == h0 + 1 && s.HistTop == OpLabels.AddLight,
                $"Add ▸ Light ▸ \"{spot.Name}\": a light, selected, one entry \"{OpLabels.AddLight}\"", s.Light + ", " + s.HistTop);
            if (s.Light == null) yield break;
            yield return U2Wait(s, () => (ui.Runtime.Find(s.Light) as LightRealization)?.Light != null, 30);
            var lr = ui.Runtime.Find(s.Light) as LightRealization;
            c.Check(lr != null && lr.Light && lr.Light.type == LightType.Spot, "it is a spot light", lr?.Light ? lr.Light.type.ToString() : "not realized");
            yield return U2Show(s, "inspector");
            yield return c.Frames(6);
            yield return U2Hist(s);
            h0 = s.HistN;
            yield return U2Type(s, () => U2Number(U2Inspector(s), "light.intensity"), "1.5", "the intensity field");
            yield return U2Type(s, () => U2Number(U2Inspector(s), "light.spotAngle"), "45", "the spot angle field");
            yield return U2Hist(s);
            var it = U2Num(U2Get(s, s.Light, "light.intensity"));
            var an = U2Num(U2Get(s, s.Light, "light.spotAngle"));
            c.Check(Math.Abs(it - 1.5) < 1e-5 && Math.Abs(an - 45) < 1e-4 && s.HistN == h0 + 2, "typed intensity 1.5 and spot angle 45: two entries", $"{it} / {an}, {s.HistN - h0} / {s.HistTop}");
            yield return U2GizmoMoveX(s, s.Light, "the light", OpLabels.Transform);
        }

        /// <summary>6: bloom intensity typed, vignette off; sky on with pattern 3.</summary>
        static IEnumerator U2Step6(U2 s)
        {
            var c = s.C;
            var ui = s.Ui;
            yield return U2Show(s, "scene");
            yield return c.Frames(4);
            VisualElement Scene() => U2PanelRoot(s, "scene");
            yield return U2Hist(s);
            int h0 = s.HistN;
            yield return U2Type(s, () => U2Number(Scene(), "effects.bloom.intensity"), "4.5", "the bloom intensity field");
            yield return U2Hist(s);
            double bi = U2Num(U2Get(s, null, "effects.bloom.intensity"));
            c.Check(Math.Abs(bi - 4.5) < 1e-5 && s.HistN == h0 + 1 && s.HistTop == OpLabels.Effects, $"bloom intensity 4.5, one entry \"{OpLabels.Effects}\"", $"{bi}, {s.HistN - h0} / {s.HistTop}");
            h0 = s.HistN;
            bool vig = U2Get(s, null, "effects.vignette.enabled") as bool? ?? false;
            if (vig) yield return U2Click(s, () => U2Row(Scene(), "effects.vignette.enabled")?.Q<Toggle>(), "vignette (off)");
            yield return U2Hist(s);
            bool vig2 = U2Get(s, null, "effects.vignette.enabled") as bool? ?? true;
            c.Check(!vig2 && s.HistN == h0 + (vig ? 1 : 0), "vignette off" + (vig ? " (one entry)" : " (was off already)"), $"{vig} → {vig2}");
            h0 = s.HistN;
            bool sky = U2Get(s, null, "effects.sky.enabled") as bool? ?? false;
            if (!sky) yield return U2Click(s, () => U2Row(Scene(), "effects.sky.enabled")?.Q<Toggle>(), "sky (on)");
            var skyRow = ui.App.Db?.Skies.All.Where(r => r.Visible).FirstOrDefault(r => r.Key.Id == 3);
            if (!c.Check(skyRow != null, "sky pattern 3 exists")) yield break;
            yield return U2Click(s, () => U2Row(Scene(), "effects.sky.pattern")?.Q<Button>("ref"), "the sky pattern picker");
            yield return c.Frames(4);
            var list = s.Host.Root.Q<ListView>("ref-list");
            if (!c.Check(list != null, "the pattern list opens")) yield break;
            var src = list.itemsSource as IList<(string value, string label)>;
            int idx = -1;
            for (int i = 0; src != null && i < src.Count; i++) if (src[i].value == skyRow.Key.ToString()) { idx = i; break; }
            if (!c.Check(idx >= 0, "the list has pattern 3", skyRow.Name)) yield break;
            list.ScrollToItem(idx);
            yield return c.Frames(4);
            yield return U2Click(s, () => s.Host.Root.Q<ListView>("ref-list")?.Query<Label>().Where(l => l.text == src[idx].label && l.worldBound.height > 0).First(), "the pattern 3 row");
            var openList = s.Host.Root.Q<ListView>("ref-list");
            if (openList != null)
            {
                var cap = PointerCaptureHelper.GetCapturingElement(s.Host.Root.panel, PointerId.mousePointerId) as VisualElement;
                c.Info($"UI2.6: the pattern list is still open after the click (selected {openList.selectedIndex}, wanted {idx}; pointer captured by {(cap == null ? "nothing" : cap.name + " (" + cap.GetType().Name + ")")}); pressing and releasing in separate frames");
                var lab = openList.Query<Label>().Where(l => l.text == src[idx].label && l.worldBound.height > 0).First();
                if (lab != null)
                {
                    var lc = lab.worldBound.center;
                    yield return c.Call(new { cmd = "ui.input", frames = 3, events = new object[] { new { type = "pointer", phase = "down", x = lc.x, y = lc.y, button = 0 }, new { type = "pointer", phase = "up", x = lc.x, y = lc.y, button = 0 } } });
                }
                openList = s.Host.Root.Q<ListView>("ref-list");
                if (openList != null) c.Info($"UI2.6: still open (selected {openList.selectedIndex}, focusable {openList.focusable}, picking {lab?.pickingMode}, panel {(lab?.panel == s.Host.Root.panel)}, row {lab?.worldBound}, root {s.Host.Root.worldBound}, popup {s.Host.Root.panel.visualTree.Q("ref-popup")?.worldBound})");
            }
            yield return U2Hist(s);
            bool sky2 = U2Get(s, null, "effects.sky.enabled") as bool? ?? false;
            string pat = U2Get(s, null, "effects.sky.pattern") as string;
            c.Check(sky2 && pat == skyRow.Key.ToString() && s.HistN == h0 + (sky ? 1 : 2) && s.HistTop == OpLabels.Sky,
                $"sky on with pattern 3 \"{skyRow.Name}\" ({(sky ? 1 : 2)} entries \"{OpLabels.Sky}\")", $"{sky2} / {pat}, {s.HistN - h0} / {s.HistTop}");
        }

        /// <summary>7: Face tab (eyes open 0.5, mouth ▶) and State tab (the top half; numbers only).</summary>
        static IEnumerator U2Step7(U2 s)
        {
            var c = s.C;
            if (!U2Need(s, s.Female)) yield break;
            yield return U2SelectRow(s, s.Female);
            yield return U2Tab(s, "face");
            yield return U2Hist(s);
            int h0 = s.HistN;
            yield return U2Type(s, () => U2Number(U2Inspector(s), "chara.face.eyesOpen"), "0.5", "the eyes-open field");
            int m0 = (int)U2Num(U2Get(s, s.Female, "chara.face.mouth"));
            yield return U2Click(s, () => U2Inspector(s)?.Q("field-chara.face.mouth")?.Q<Button>("next"), "the mouth pattern's ▶");
            yield return U2Hist(s);
            double eo = U2Num(U2Get(s, s.Female, "chara.face.eyesOpen"));
            int m1 = (int)U2Num(U2Get(s, s.Female, "chara.face.mouth"));
            c.Check(Math.Abs(eo - 0.5) < 1e-5 && m1 != m0 && s.HistN == h0 + 2, "eyes open 0.5 and the next mouth pattern: two entries", $"{eo}, mouth {m0} → {m1}, {s.HistN - h0} / {s.HistTop}");
            yield return U2Tab(s, "state");
            h0 = s.HistN;
            yield return U2Click(s, () => U2Inspector(s)?.Q("field-chara.state.clothes[0]")?.Q<Button>("seg-1"), "the top's \"half\" button");
            yield return c.Call(new { cmd = "chara.clothes", key = s.Female });
            var st = c.J["states"] as JArray;
            yield return U2Hist(s);
            c.Check(st != null && (int)st[0] == 1 && s.HistN == h0 + 1, "chara.clothes states[0] = 1 (half), one entry (numbers only)", AcceptanceCtx.Short(st?.ToString(Newtonsoft.Json.Formatting.None)));
        }

        /// <summary>8: IK on, the RightHand target dragged with the gizmo, FK neck group on.</summary>
        static IEnumerator U2Step8(U2 s)
        {
            var c = s.C;
            var ui = s.Ui;
            if (!U2Need(s, s.Female)) yield break;
            yield return U2SelectRow(s, s.Female);
            yield return U2Tab(s, "kinematics");
            yield return U2Hist(s);
            int h0 = s.HistN;
            yield return U2Click(s, () => U2Row(U2Inspector(s), "chara.ik.enabled")?.Q<Toggle>(), "IK enabled");
            yield return c.Frames(6);
            yield return U2Hist(s);
            c.Check(U2Get(s, s.Female, "chara.ik.enabled") as bool? == true && s.HistN == h0 + 1, "IK on (one entry)", $"{U2Get(s, s.Female, "chara.ik.enabled")}, {s.HistTop}");
            yield return U2Click(s, () => U2Inspector(s)?.Q("ik-RightHand")?.Q<Label>(), "the RightHand target's title");
            yield return c.Call(new { cmd = "selection" });
            string sub = (string)c.J["sub"];
            c.Check(sub == s.Female + "/ik/RightHand", "the RightHand target is sub-selected", sub);
            var before = U2Get(s, s.Female, "chara.ik.targets.RightHand.pos");
            yield return U2GizmoMoveX(s, s.Female, "the RightHand IK target", null);
            var after = U2Get(s, s.Female, "chara.ik.targets.RightHand.pos");
            c.Check(!Equals(before, after), "the target's stored position changed", $"{before} → {after}");
            yield return U2Hist(s);
            h0 = s.HistN;
            // the neck group (on by default as in HS2: clicked only when off) and FK itself
            int clicks = 0;
            if (U2Get(s, s.Female, "chara.fk.groups.neck") as bool? != true) { clicks++; yield return U2Click(s, () => U2Row(U2Inspector(s), "chara.fk.groups.neck")?.Q<Toggle>(), "FK neck group"); }
            if (U2Get(s, s.Female, "chara.fk.enabled") as bool? != true) { clicks++; yield return U2Click(s, () => U2Row(U2Inspector(s), "chara.fk.enabled")?.Q<Toggle>(), "FK enabled"); }
            yield return c.Frames(4);
            yield return U2Hist(s);
            c.Check(U2Get(s, s.Female, "chara.fk.groups.neck") as bool? == true && U2Get(s, s.Female, "chara.fk.enabled") as bool? == true && s.HistN == h0 + clicks,
                $"FK on with the neck group on ({clicks} click(s), one entry each)", $"neck {U2Get(s, s.Female, "chara.fk.groups.neck")}, fk {U2Get(s, s.Female, "chara.fk.enabled")}, {s.HistN - h0} / {s.HistTop}");
        }

        /// <summary>9: Add ▸ Route, three points from the inspector, point 2 Curve, point 3 moved, a folder riding it, Play.</summary>
        static IEnumerator U2Step9(U2 s)
        {
            var c = s.C;
            var ui = s.Ui;
            yield return U2Hist(s);
            int h0 = s.HistN;
            yield return U2Menu(s, "tb-add", "add.route");
            yield return U2Hist(s);
            if (s.HistN == h0)
            {
                c.Check(false, "Add ▸ Route from the toolbar's Add menu adds a route (the menu item is disabled?)");
                yield return U2Act(s, "add.route");
                c.CheckOk("ui.action add.route (fallback)");
                yield return U2Hist(s);
            }
            yield return c.Call(new { cmd = "selection" });
            s.Route = (string)c.J["primary"];
            if (!c.Check(s.Route != null && ui.Model.Find(s.Route)?.Kind == NodeKind.Route && s.HistN == h0 + 1 && s.HistTop == OpLabels.AddRoute,
                    $"a route, selected, one entry \"{OpLabels.AddRoute}\"", s.Route + ", " + s.HistTop)) yield break;
            RouteNodeState St() => ui.Model.Find(s.Route)?.State as RouteNodeState;
            for (int k = 1; k <= 3; k++)
            {
                // the inspector shares the left region with the workspace (selecting the route's row shows the workspace)
                yield return U2Show(s, "inspector");
                yield return c.Frames(6);
                var expect = RouteGeometry.NextPointPosition(St());
                h0 = s.HistN;
                yield return U2Click(s, () => U2Inspector(s)?.Q("field-" + ActionIds.RouteAddPoint)?.Q<Button>(), "the inspector's Add point (" + k + ")");
                yield return U2Hist(s);
                var st = St();
                var p = st.Points.Count > k ? st.Points[k].Pos : Vec3.Zero;
                c.Check(st.Points.Count == k + 1 && Math.Abs(p.X - expect.X) < 1e-5 && Math.Abs(p.Y - expect.Y) < 1e-5 && Math.Abs(p.Z - expect.Z) < 1e-5 && s.HistN == h0 + 1 && s.HistTop == OpLabels.AddRoutePoint,
                    $"point {k} added where the path continues (R8), one entry \"{OpLabels.AddRoutePoint}\"", $"{st.Points.Count} points, ({p.X}, {p.Y}, {p.Z}) vs ({expect.X}, {expect.Y}, {expect.Z}), {s.HistTop}");
                // the new point is sub-selected; select the route itself again for the next Add point
                yield return U2SelectRow(s, s.Route);
            }
            if (!c.Check(St()?.Points.Count == 4, "the route has 4 points")) yield break;
            // point 2: Curve
            yield return U2Show(s, WorkspacePanel.PanelId);
            string p2 = St().Points[2].Id, p3 = St().Points[3].Id;
            yield return U2TreeVisible(s, s.Route, "-rp-" + p2);
            yield return U2Click(s, () => U2PanelRoot(s, WorkspacePanel.PanelId)?.Q("tree-row-" + s.Route + "-rp-" + p2), "point 2's tree row");
            yield return c.Call(new { cmd = "selection" });
            c.Check((string)c.J["sub"] == s.Route + "/points/" + p2, "point 2 is sub-selected", (string)c.J["sub"]);
            yield return U2Show(s, "inspector");
            yield return c.Frames(6);
            h0 = s.HistN;
            var connRow = U2RowBySpec(U2Inspector(s), "route.points[{point}].connection");
            c.Info($"UI2.9: point 2's connection row {connRow?.name} ({connRow?.GetType().Name}), seg-1 enabled {connRow?.Q<Button>("seg-1")?.enabledInHierarchy}, sub {ui.SubSelection}");
            yield return U2Click(s, () => U2RowBySpec(U2Inspector(s), "route.points[{point}].connection")?.Q<Button>("seg-1"), "point 2's Curve button");
            yield return U2Toasts(s, "UI2.9 after the Curve click");
            yield return U2Hist(s);
            var pt2 = St().Points[2];
            c.Check(pt2.Connection == "curve" && pt2.Aid != null && s.HistN == h0 + 1 && s.HistTop == OpLabels.RoutePoint, $"point 2 Curve with its midpoint aid, one entry \"{OpLabels.RoutePoint}\"", $"{pt2.Connection}, aid {(pt2.Aid != null)}, {s.HistTop}");
            // point 3: the move gizmo
            yield return U2Show(s, WorkspacePanel.PanelId);
            yield return U2TreeVisible(s, s.Route, "-rp-" + p3);
            yield return U2Click(s, () => U2PanelRoot(s, WorkspacePanel.PanelId)?.Q("tree-row-" + s.Route + "-rp-" + p3), "point 3's tree row");
            var q0 = St().Points[3].Pos;
            yield return U2GizmoMoveX(s, s.Route, "point 3", OpLabels.RoutePoint);
            var q1 = St().Points[3].Pos;
            c.Check(!q0.Equals(q1) && Math.Abs(q1.Y - q0.Y) < 1e-4, "point 3's stored position moved", $"({q0.X}, {q0.Y}, {q0.Z}) → ({q1.X}, {q1.Y}, {q1.Z})");
            // a folder on the route's row: it rides the child root
            yield return U2Hist(s);
            h0 = s.HistN;
            yield return U2Menu(s, "tb-add", "add.folder");
            yield return c.Call(new { cmd = "selection" });
            s.Folder = (string)c.J["primary"];
            yield return U2Hist(s);
            if (!c.Check(s.Folder != null && ui.Model.Find(s.Folder)?.Kind == NodeKind.Folder && s.HistN == h0 + 1, "Add ▸ Folder: a folder, selected, one entry", s.Folder + ", " + s.HistTop)) yield break;
            yield return U2Show(s, WorkspacePanel.PanelId);
            yield return U2TreeVisible(s, s.Folder);
            yield return U2TreeVisible(s, s.Route);
            var fr = U2TreeRow(s, s.Folder);
            var rr = U2TreeRow(s, s.Route);
            if (!c.Check(fr != null && rr != null, "the folder and route rows are shown")) yield break;
            h0 = s.HistN;
            yield return U2Drag(s, fr.worldBound.center, rr.worldBound.center);
            yield return c.Frames(4);
            yield return U2Hist(s);
            var fn = ui.Model.Find(s.Folder);
            var real = ui.Ops.RouteReal(s.Route);
            var fh = ui.Runtime.HolderOf(s.Folder);
            c.Check(fn?.Parent?.Id == s.Route && s.HistN == h0 + 1 && s.HistTop == OpLabels.Parent, $"the folder dropped on the route's row is its child, one entry \"{OpLabels.Parent}\"", $"{fn?.Parent?.Id}, {DragController.LastOutcome}, {s.HistTop}");
            c.Check(fh && real?.ChildRoot && fh.parent == real.ChildRoot, "the folder's holder rides the route's child root", fh ? fh.parent?.name : "no holder");
            // Play from the inspector
            yield return U2SelectRow(s, s.Route);
            yield return U2Show(s, "inspector");
            yield return c.Frames(6);
            h0 = s.HistN;
            yield return U2Click(s, () => U2Inspector(s)?.Q("field-" + ActionIds.RoutePlay)?.Q<Button>(), "the inspector's Play");
            yield return c.Frames(10);
            yield return c.Call(new { cmd = "route.state", key = s.Route, line = false });
            double tau = (double?)c.J["time"] ?? double.NaN;
            bool stOk = c.Last.Ok;
            string state = (string)c.J["state"];
            yield return U2Hist(s);
            c.Check(stOk && state == "playing" && tau > 0 && s.HistN == h0, "Play: route.state playing, τ advancing, no history entry (R7)", $"{state}, τ {tau:0.###}, {s.HistN - h0}");
            // step 15 returns here
            yield return c.Call(new { cmd = "scene.doc" });
            s.Digest9 = U2Digest(c.J);
            yield return U2Hist(s);
            s.Undo9 = s.HistN;
            c.Info($"UI2.9: the document after step 9: {s.Undo9} history entries, digest {s.Digest9?.Substring(0, 12)}");
        }

        /// <summary>10: the toolbar's Pause; Step × 30 (τ + 30 × 1/60 exactly); Resume.</summary>
        static IEnumerator U2Step10(U2 s)
        {
            var c = s.C;
            if (!U2Need(s, s.Route)) yield break;
            yield return U2Click(s, () => s.Host.Root.Q<Button>("tb-time.pause"), "the toolbar's Pause");
            yield return c.Call(new { cmd = "scene.pause" });
            c.Check(c.J["paused"]?.Value<bool>() == true, "scene.pause reads paused");
            yield return c.Call(new { cmd = "route.state", key = s.Route, line = false });
            double t0 = (double?)c.J["time"] ?? double.NaN;
            yield return c.Frames(20);
            yield return c.Call(new { cmd = "route.state", key = s.Route, line = false });
            double t1 = (double?)c.J["time"] ?? double.NaN;
            c.Check(t0 == t1, "paused: τ holds over 20 frames", $"{t0:R} / {t1:R}");
            var step = s.Host.Root.Q<Button>("tb-time.step");
            if (!c.Check(step != null && step.enabledInHierarchy, "the toolbar's Step is enabled while paused")) yield break;
            var pt = new Vector2[1];
            yield return U2Reveal(s, step, "the Step button", pt);
            var clicks = new List<object>();
            for (int i = 0; i < 30; i++) clicks.Add(new { type = "pointer", phase = "click", x = pt[0].x, y = pt[0].y, button = 0 });
            var sw = Stopwatch.StartNew();
            yield return c.Call(new { cmd = "ui.input", frames = 3, events = clicks.ToArray() });
            double ms = sw.Elapsed.TotalMilliseconds;
            yield return c.Call(new { cmd = "route.state", key = s.Route, line = false });
            double t2 = (double?)c.J["time"] ?? double.NaN;
            double expect = t1;
            for (int i = 0; i < 30; i++) expect += 1f / 60f;
            c.Check(t2 == expect, "Step × 30: τ advanced by 30 × 1/60 exactly", $"τ {t1:R} → {t2:R} (expected {expect:R}, Δ {t2 - t1:R}); 30 clicks in {ms:0} ms");
            yield return U2Click(s, () => s.Host.Root.Q<Button>("tb-time.pause"), "the toolbar's Resume");
            yield return c.Call(new { cmd = "scene.pause" });
            c.Check(c.J["paused"]?.Value<bool>() == false, "resumed");
        }

        /// <summary>11: frame the selection, save slot 1, change the view, load slot 1.</summary>
        static IEnumerator U2Step11(U2 s)
        {
            var c = s.C;
            yield return U2Act(s, "camera.frame");
            c.CheckOk("ui.action camera.frame");
            yield return c.Frames(4);
            yield return c.Call(new { cmd = "camera.get" });
            var a = c.J["camera"]?.DeepClone();
            yield return U2Show(s, "scene");
            yield return c.Frames(4);
            yield return U2Hist(s);
            int h0 = s.HistN;
            yield return U2Click(s, () => U2PanelRoot(s, "scene")?.Q("camera-slot-0")?.Q<Button>("save"), "camera slot 1's Save");
            yield return U2Hist(s);
            c.Check(s.HistN == h0 + 1 && s.HistTop == OpLabels.CameraSlot, $"slot 1 saved: one entry \"{OpLabels.CameraSlot}\"", s.HistTop);
            var vp = s.Ui.Viewport.Element.worldBound.center;
            yield return c.Call(new { cmd = "ui.input", frames = 3, events = new object[] { new { type = "wheel", x = vp.x, y = vp.y, delta = 6 }, new { type = "wheel", x = vp.x, y = vp.y, delta = 6 } } });
            yield return c.Call(new { cmd = "camera.get" });
            var b = c.J["camera"]?.DeepClone();
            c.Check(Math.Abs(U2Num(a?["dist"]) - U2Num(b?["dist"])) > 1e-3, "the wheel changed the view", $"dist {a?["dist"]} → {b?["dist"]}");
            h0 = s.HistN;
            yield return U2Click(s, () => U2PanelRoot(s, "scene")?.Q("camera-slot-0")?.Q<Button>("load"), "camera slot 1's Load");
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "camera.get" });
            var d = c.J["camera"];
            yield return U2Hist(s);
            double dt = (U2V(a?["target"]) - U2V(d?["target"])).magnitude, dr = (U2V(a?["rot"]) - U2V(d?["rot"])).magnitude;
            double dd = Math.Abs(U2Num(a?["dist"]) - U2Num(d?["dist"])), df = Math.Abs(U2Num(a?["fov"]) - U2Num(d?["fov"]));
            c.Check(dt < 1e-4 && dr < 1e-3 && dd < 1e-4 && df < 1e-4 && s.HistN == h0, "slot 1 loaded: the view equals the saved one (no entry)", $"Δtarget {dt:G3}, Δrot {dr:G3}, Δdist {dd:G3}, Δfov {df:G3}");
        }

        /// <summary>12: background = the first DefaultData/bg image, frame = the first UserData/frame image (when present).</summary>
        static IEnumerator U2Step12(U2 s)
        {
            var c = s.C;
            var paths = s.Ui.App.Paths;
            yield return U2Show(s, "scene");
            yield return c.Frames(4);
            foreach (var field in new[] { "background", "frame" })
            {
                var folders = ImageBrowser.FoldersOf(s.Ui, field);
                var first = ImageBrowser.Scan(folders).FirstOrDefault();
                string wantDir = field == "background" ? (paths.Hs2Root != null ? Path.Combine(paths.Hs2Root, "DefaultData", "bg") : null) : (paths.Hs2UserData != null ? Path.Combine(paths.Hs2UserData, "frame") : null);
                if (first == null || wantDir == null || !string.Equals(Path.GetDirectoryName(first.FullPath), wantDir, StringComparison.OrdinalIgnoreCase))
                {
                    c.Info($"UI2.12: no {field} image in {wantDir ?? "-"} (first listed: {first?.FullPath ?? "none"}): {field} skipped");
                    if (field == "background") c.Warn("no DefaultData/bg image: the background row is not driven");
                    continue;
                }
                yield return U2Hist(s);
                int h0 = s.HistN;
                yield return U2Click(s, () => U2PanelRoot(s, "scene")?.Q("images-" + field + "-picker")?.Q<Button>("browse"), "the " + field + " picker's …");
                yield return c.Frames(6);
                var br = ImageBrowser.Open;
                if (!c.Check(br != null && s.Ui.Dialogs.Current?.Spec.Id == ImageBrowser.DialogId, "the image browser opens for " + field)) continue;
                br.Grid.List.ScrollToItem(0);
                yield return c.Frames(4);
                yield return U2Click(s, () => ImageBrowser.Open?.Grid.Q("card-0"), "the first image");
                c.Check(ImageBrowser.Open?.Grid.Selected?.FullPath == first.FullPath, "the first image is selected", Path.GetFileName(first.FullPath));
                yield return U2Click(s, () => s.Host.Root.Q<Button>("dialog-button-ok"), "the image browser's OK");
                yield return c.Frames(4);
                yield return c.Call(new { cmd = "scene.images" });
                var r = c.J[field];
                yield return U2Hist(s);
                string label = field == "frame" ? OpLabels.Frame : OpLabels.Background;
                bool shape = field == "background" ? ((string)r?["hs2"])?.EndsWith("DefaultData/bg/" + Path.GetFileName(first.FullPath), StringComparison.OrdinalIgnoreCase) == true : r?["res"] != null;
                c.Check(c.Last.Ok && shape && s.HistN == h0 + 1 && s.HistTop == label,
                    $"{field} = {Path.GetFileName(first.FullPath)} stored as {(field == "background" ? "an hs2 reference" : "an embedded copy")}, one entry \"{label}\"", AcceptanceCtx.Short(r?.ToString(Newtonsoft.Json.Formatting.None)) + ", " + s.HistTop);
            }
        }

        /// <summary>13: the screenshot dialog (scale 2, supersample 1, Take: 2 × the fit size), then F11 at 1 ×.</summary>
        static IEnumerator U2Step13(U2 s)
        {
            var c = s.C;
            var ui = s.Ui;
            yield return c.Call(new { cmd = "ui.input", frames = 4, events = new object[] { new { type = "key", key = "F11", mods = new[] { "shift" } } } });
            yield return U2Keys(s, "UI2.13 after Shift+F11");
            var dlg = ScreenshotDialog.Open;
            if (!c.Check(dlg != null && ui.Dialogs.Current?.Spec.Id == ScreenshotDialog.DialogId, "Shift+F11 opens the screenshot dialog", ui.Dialogs.Current?.Spec.Id)) yield break;
            yield return U2Click(s, () => U2Row(ScreenshotDialog.Open, OptionDefs.CaptureMode)?.Q<Button>("seg-0"), "size mode Fit");
            var fit1 = ScreenshotDialog.Open.Request;
            yield return U2Type(s, () => U2Number(ScreenshotDialog.Open, OptionDefs.CaptureScale), "2", "the scale field");
            yield return U2Click(s, () => U2Row(ScreenshotDialog.Open, OptionDefs.CaptureSupersample)?.Q<Button>("seg-0"), "supersample 1");
            var req = ScreenshotDialog.Open.Request;
            c.Check(ui.Options.Float(OptionDefs.CaptureScale) == 2f && ui.Options.String(OptionDefs.CaptureSupersample) == "1" && Math.Abs(req.Width - 2 * fit1.Width) <= 1 && Math.Abs(req.Height - 2 * fit1.Height) <= 1,
                "scale 2, supersample 1: the request is twice the fit size", $"{fit1.Width} x {fit1.Height} → {req.Width} x {req.Height} (x{req.Supersample})");
            string last0 = ScreenshotModule.LastFile;
            var sw = Stopwatch.StartNew();
            int w2 = 0, h2 = 0;
            string refusal = ScreenshotDialog.Open.Refusal;
            if (refusal != null)
            {
                // a large window: twice the fit size is above the pixel cap (capture.maxPixels): the dialog says so and Take is off
                var takeBtn = s.Host.Root.Q<Button>("dialog-button-take");
                var warn = ScreenshotDialog.Open.Q<Label>("capture-warning");
                c.Check(takeBtn != null && !takeBtn.enabledInHierarchy && warn != null && warn.resolvedStyle.display == DisplayStyle.Flex && !string.IsNullOrEmpty(warn.text),
                    $"scale 2 of this window ({req.Width} x {req.Height}) is above the cap: the dialog warns and Take is disabled", refusal);
                yield return U2Click(s, () => s.Host.Root.Q<Button>("dialog-button-take"), "Take (disabled)");
                yield return c.Frames(10);
                c.Check(ScreenshotModule.LastFile == last0, "the disabled Take writes nothing");
            }
            else
            {
                yield return U2Click(s, () => s.Host.Root.Q<Button>("dialog-button-take"), "Take");
                yield return U2Wait(s, () => ScreenshotModule.LastFile != last0 && File.Exists(ScreenshotModule.LastFile), 60);
                string f2 = ScreenshotModule.LastFile;
                (w2, h2) = U2PngSize(f2);
                c.Check(s.Ok && w2 == req.Width && h2 == req.Height && Path.GetFullPath(f2).StartsWith(Path.GetFullPath(ui.App.Paths.CapDir), StringComparison.OrdinalIgnoreCase),
                    $"Take writes <UserData>/cap/{Path.GetFileName(f2)} of {w2} x {h2} ({sw.Elapsed.TotalMilliseconds:0} ms)", f2);
            }
            yield return U2Type(s, () => U2Number(ScreenshotDialog.Open, OptionDefs.CaptureScale), "1", "the scale field (back to 1)");
            yield return U2Click(s, () => s.Host.Root.Q<Button>("dialog-button-close"), "Close");
            yield return c.Frames(3);
            c.Check(!ui.Dialogs.IsModalOpen && ui.Options.Float(OptionDefs.CaptureScale) == 1f, "the dialog is closed, scale 1");
            var req1 = HS2Next.Screenshots.CaptureSettings.Current(ui.Runtime?.GetPart<SceneViewCamera>()?.Camera);
            string last1 = ScreenshotModule.LastFile;
            sw.Restart();
            yield return c.Call(new { cmd = "ui.input", frames = 4, events = new object[] { new { type = "key", key = "F11" } } });
            yield return U2Wait(s, () => ScreenshotModule.LastFile != last1 && File.Exists(ScreenshotModule.LastFile), 60);
            string f1 = ScreenshotModule.LastFile;
            var (w1, h1) = U2PngSize(f1);
            c.Check(s.Ok && w1 == req1.Width && h1 == req1.Height && (w2 == 0 || (Math.Abs(w2 - 2 * w1) <= 1 && Math.Abs(h2 - 2 * h1) <= 1)),
                $"F11 writes {Path.GetFileName(f1)} of {w1} x {h1} (1 x; the dialog's file is 2 x) ({sw.Elapsed.TotalMilliseconds:0} ms)", f1);
        }

        /// <summary>14 (with 15): Save As "ui2"; the history list back to step 9 and redo all; New; Open "ui2" → the same document.</summary>
        static IEnumerator U2Step14(U2 s)
        {
            var c = s.C;
            var ui = s.Ui;
            var files = SceneFiles.Current;
            if (!c.Check(files != null, "the scene files controller")) yield break;
            yield return U2Menu(s, "tb-file", ActionIds.FileSaveAs);
            if (!c.Check(ui.Dialogs.Current?.Spec.Id == "saveAs", "File ▸ Save As… opens the dialog", ui.Dialogs.Current?.Spec.Id)) yield break;
            yield return U2Type(s, () => s.Host.Root.Q<TextField>("save-as-name"), "ui2", "the file name field", enter: false);
            yield return U2Click(s, () => s.Host.Root.Q<Button>("dialog-button-save"), "Save");
            yield return c.Frames(3);
            if (ui.Dialogs.Current?.Spec.Id == "dialog.overwrite.title")
            {
                c.Info("UI2.14: ui2 exists from an earlier run: the overwrite confirmation → OK");
                yield return U2Click(s, () => s.Host.Root.Q<Button>("dialog-button-ok"), "Overwrite OK");
            }
            yield return U2Wait(s, () => files.LastOutcome == "saved" && !ui.Dialogs.IsModalOpen, 60);
            s.SavedPath = files.CurrentPath;
            c.Check(s.Ok && s.SavedPath != null && Path.GetFileNameWithoutExtension(s.SavedPath) == "ui2" && File.Exists(s.SavedPath), "File ▸ Save As \"ui2\" writes the file", s.SavedPath + " / " + files.LastOutcome);
            yield return c.Call(new { cmd = "scene.doc" });
            s.SavedDigest = Digest(c.J);
            s.SavedViewless = U2Digest(c.J);
            s.SavedDoc = DocOf(c.J);

            // 15: the history list back to step 9's document, then redo all
            c.Id = "UI2.15";
            var sw15 = Stopwatch.StartNew();
            if (s.Undo9 >= 0 && s.Digest9 != null)
            {
                yield return U2Show(s, HistoryPanel.Id);
                yield return U2Hist(s);
                int back = s.HistN - s.Undo9;
                c.Info($"UI2.15: {s.HistN} entries now, {back} after step 9");
                yield return U2Click(s, () => U2PanelRoot(s, HistoryPanel.Id)?.Q<Button>("history-undo-" + back), "the history row " + back);
                yield return c.Frames(6);
                yield return U2Hist(s);
                yield return c.Call(new { cmd = "scene.doc" });
                string d9 = U2Digest(c.J);
                c.Check(s.HistN == s.Undo9 && d9 == s.Digest9, "the history list undoes back to step 9's document (without the view, camera.current: not recorded)", $"{s.HistN} entries; {(d9 == s.Digest9 ? "equal" : "differs")}");
                yield return c.Call(new { cmd = "history" });
                int redo = (c.J["redo"] as JArray)?.Count ?? 0;
                yield return U2Click(s, () => U2PanelRoot(s, HistoryPanel.Id)?.Q<Button>("history-redo-" + (redo - 1)), "the history's last redo row");
                yield return c.Frames(6);
                yield return c.Call(new { cmd = "scene.doc" });
                string dr = U2Digest(c.J);
                c.Check(dr == s.SavedViewless, "redo all: the document equals the saved one again (without the view)", dr == s.SavedViewless ? "" : FirstDiff(Strip(s.SavedDoc), Strip(DocOf(c.J)), ""));
            }
            else c.Check(false, "step 9 recorded its document (needed by step 15)");
            c.Info($"UI2 timing: step 15 (history) {sw15.Elapsed.TotalSeconds:0.00} s");
            c.Id = "UI2.14";

            // New, Open "ui2"
            yield return U2Menu(s, "tb-file", ActionIds.FileNew);
            yield return c.Frames(3);
            if (ui.Dialogs.Current?.Spec.Id == "unsaved") yield return U2Click(s, () => s.Host.Root.Q<Button>("dialog-button-discard"), "Don't save");
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "scene" });
            c.Check((c.J["objects"] as JArray)?.Count == 0 && files.CurrentPath == null, "File ▸ New: an empty untitled scene");
            yield return U2Menu(s, "tb-file", ActionIds.FileOpen);
            var sb = SceneBrowser.Open;
            if (!c.Check(sb != null && ui.Dialogs.Current?.Spec.Id == SceneBrowser.DialogId, "File ▸ Open… shows the scene browser")) yield break;
            yield return U2Wait(s, () => SceneBrowser.Open != null && SceneBrowser.Open.Ready, 30);
            yield return U2Type(s, () => s.Host.Root.Q<TextField>("scene-search"), "ui2", "the scene search field");
            yield return c.Frames(4);
            int pos = -1;
            var swq = Stopwatch.StartNew();
            while (swq.Elapsed.TotalSeconds < 10)
            {
                var idx = SceneBrowser.Open?.Index;
                pos = -1;
                for (int i = 0; idx != null && i < idx.Indices.Count; i++)
                    if (string.Equals(idx.Rows[idx.Indices[i]].FullPath, s.SavedPath, StringComparison.OrdinalIgnoreCase)) { pos = i; break; }
                if (pos >= 0) break;
                yield return c.Frames(2);
            }
            if (!c.Check(pos >= 0, "the scene browser lists ui2")) yield break;
            int p = pos;
            var sw = Stopwatch.StartNew();
            yield return U2Click(s, () => SceneBrowser.Open?.Q("card-" + p), "the ui2 card (double click)", clicks: 2, frames: 4);
            yield return U2Wait(s, () => files.LastOutcome == "loaded" && files.CurrentPath != null, 120);
            c.Check(s.Ok && string.Equals(files.CurrentPath, s.SavedPath, StringComparison.OrdinalIgnoreCase), $"File ▸ Open \"ui2\" loads it ({sw.Elapsed.TotalSeconds:0.0} s)", files.LastOutcome);
            yield return U2Wait(s, () => ui.Model.PreOrder().Where(n => n.Kind == NodeKind.Chara).All(n => ui.Runtime.FindChara(n.Id)?.State == RealizationState.Built), 180);
            yield return c.Frames(10);
            yield return c.Call(new { cmd = "scene.doc" });
            string after = Digest(c.J);
            c.Check(after == s.SavedDigest, "the opened scene.doc equals the saved one (without anime.time, look.neckFix, look.eyeRotations)", after == s.SavedDigest ? "" : FirstDiff(Strip(s.SavedDoc), Strip(DocOf(c.J)), ""));
        }

        /// <summary>16: the language menu en → ja → en; F1 opens the key overlay.</summary>
        static IEnumerator U2Step16(U2 s)
        {
            var c = s.C;
            var ui = s.Ui;
            yield return c.Call(new { cmd = "ui.query", name = "#tb-file" });
            string en = (string)c.J["elements"]?[0]?["text"];
            yield return U2Menu(s, "tb-language", "lang-ja");
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "ui.query", name = "#tb-file" });
            string ja = (string)c.J["elements"]?[0]?["text"];
            c.Check(ui.L10n.Language == "ja" && en != null && ja != null && en != ja, $"Language ▸ 日本語: File '{en}' → '{ja}' ({s.Host.LastLanguageSwitchMs:0.0} ms)");
            yield return U2Menu(s, "tb-language", "lang-en");
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "ui.query", name = "#tb-file" });
            c.Check(ui.L10n.Language == "en" && (string)c.J["elements"]?[0]?["text"] == en, $"Language ▸ English: back to '{en}' ({s.Host.LastLanguageSwitchMs:0.0} ms)");
            yield return c.Call(new { cmd = "ui.input", frames = 4, events = new object[] { new { type = "key", key = "F1" } } });
            yield return U2Keys(s, "UI2.16 after F1");
            yield return c.Call(new { cmd = "ui.state" });
            c.Check((string)c.J["modal"] == KeyOverlay.DialogId && KeyOverlay.Open != null, "F1 opens the key overlay", (string)c.J["modal"]);
            yield return c.Call(new { cmd = "ui.input", frames = 4, events = new object[] { new { type = "key", key = "Escape" } } });
            yield return U2Keys(s, "UI2.16 after Esc");
            yield return c.Call(new { cmd = "ui.state" });
            c.Check(c.J["modal"] == null || c.J["modal"].Type == JTokenType.Null, "Esc closes it", (string)c.J["modal"]);
        }

        // ================================================================ helpers (ui.* for actions, in-process geometry)

        static bool U2Need(U2 s, params string[] ids)
        {
            if (ids.All(x => x != null)) return true;
            s.C.Check(false, "skipped: an earlier step did not create the nodes this step needs");
            return false;
        }

        static IEnumerator U2Act(U2 s, string action, object args = null, double timeout = 180)
        {
            if (args == null) yield return s.C.Call(new { cmd = "ui.action", action }, timeout);
            else yield return s.C.Call(new { cmd = "ui.action", action, args }, timeout);
        }

        static IEnumerator U2Show(U2 s, string panel)
        {
            yield return s.C.Call(new { cmd = "ui.panel", panel, op = "show" });
            if (!s.C.Last.Ok) s.C.Check(false, "ui.panel show " + panel, AcceptanceCtx.Short(s.C.Last?.ToString()));
            yield return s.C.Frames(4);
        }

        static IEnumerator U2Wait(U2 s, Func<bool> done, double seconds)
        {
            var sw = Stopwatch.StartNew();
            s.Ok = false;
            while (true)
            {
                bool d;
                try { d = done(); } catch (Exception) { d = false; }
                if (d) { s.Ok = true; yield break; }
                if (sw.Elapsed.TotalSeconds > seconds) yield break;
                yield return s.C.Frames(1);
            }
        }

        /// <summary>The history through the bridge: s.HistN (undo entries), s.HistTop (the newest label).</summary>
        static IEnumerator U2Hist(U2 s)
        {
            yield return s.C.Call(new { cmd = "history" });
            var u = s.C.J["undo"] as JArray;
            s.HistN = u?.Count ?? -1;
            s.HistTop = u != null && u.Count > 0 ? (string)u[0] : null;
        }

        static object U2Get(U2 s, string id, string path)
        {
            try { return FieldAccessor.Get(s.Ui.Model, id, path); } catch (Exception) { return null; }
        }

        static double U2Num(object v)
        {
            if (v is JToken t) return t.Type == JTokenType.Integer || t.Type == JTokenType.Float ? (double)t : double.NaN;
            try { return v == null ? double.NaN : Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); } catch (Exception) { return double.NaN; }
        }

        static Vector3 U2V(JToken t) => t is JArray a && a.Count >= 3 ? new Vector3((float)a[0], (float)a[1], (float)a[2]) : new Vector3(float.NaN, float.NaN, float.NaN);

        static VisualElement U2PanelRoot(U2 s, string panel) => s.Ui.Shell?.PanelRoot(panel);

        static VisualElement U2Inspector(U2 s) => U2PanelRoot(s, "inspector");

        /// <summary>A field row by its path (the element name "field-&lt;path&gt;").</summary>
        static VisualElement U2Row(VisualElement root, string path) => root?.Q("field-" + path);

        /// <summary>A field row by its schema path (template rows keep the placeholder in their spec).</summary>
        static VisualElement U2RowBySpec(VisualElement root, string specPath) =>
            root?.Query<FieldRow>().Where(r => r.Spec != null && r.Spec.Path == specPath && r.resolvedStyle.display != DisplayStyle.None).First();

        /// <summary>The typed field of a number row (NumberSlider's FloatField).</summary>
        static VisualElement U2Number(VisualElement root, string path) => U2Row(root, path)?.Q<FloatField>("field");

        /// <summary>Opens every collapsed foldout above <paramref name="e"/> by clicking its header (outermost first).</summary>
        static IEnumerator U2Open(U2 s, Func<VisualElement> find)
        {
            for (int guard = 0; guard < 6; guard++)
            {
                var e = find();
                if (e == null) yield break;
                Foldout closed = null;
                for (var x = e.hierarchy.parent; x != null; x = x.hierarchy.parent)
                    if (x is Foldout f && !f.value) closed = f;   // the outermost closed one
                if (closed == null) yield break;
                var t = closed.Q<Toggle>(className: Foldout.toggleUssClassName) ?? closed.Q<Toggle>();
                if (t == null) yield break;
                var pt = new Vector2[1];
                yield return U2Reveal(s, t, "the foldout " + closed.name, pt);
                yield return s.C.Call(new { cmd = "ui.input", frames = 3, events = new object[] { new { type = "pointer", phase = "click", x = pt[0].x, y = pt[0].y, button = 0 } } });
            }
        }

        /// <summary>Scrolls <paramref name="e"/> into its ScrollView, waits for the layout and gives its centre (the element under
        /// that point is reported when it is not <paramref name="e"/> or inside it).</summary>
        static IEnumerator U2Reveal(U2 s, VisualElement e, string what, Vector2[] point)
        {
            for (var x = e.hierarchy.parent; x != null; x = x.hierarchy.parent)
                if (x is ScrollView sv) { sv.ScrollTo(e); break; }
            yield return s.C.Frames(3);
            point[0] = e.worldBound.center;
            var picked = s.Host.Root?.panel?.Pick(point[0]);
            bool hit = picked != null && (picked == e || e.Contains(picked));
            if (!hit) s.C.Info($"UI2: {what} at {point[0]} is covered by {(picked == null ? "nothing" : (picked.name ?? "") + " (" + picked.GetType().Name + ")")}");
        }

        /// <summary>A click (ui.input pointer click) on the element <paramref name="find"/> returns (foldouts opened, scrolled into view).</summary>
        static IEnumerator U2Click(U2 s, Func<VisualElement> find, string what, int clicks = 1, int button = 0, int frames = 3)
        {
            var c = s.C;
            VisualElement e = null;
            try { e = find(); } catch (Exception) { }
            if (e == null) { c.Check(false, what + ": the element exists"); yield break; }
            yield return U2Open(s, find);
            try { e = find(); } catch (Exception) { e = null; }
            if (e == null || e.panel == null) { c.Check(false, what + ": the element exists after opening its section"); yield break; }
            if (!e.enabledInHierarchy) c.Info("UI2: " + what + " is disabled");
            var pt = new Vector2[1];
            yield return U2Reveal(s, e, what, pt);
            yield return c.Call(new { cmd = "ui.input", frames, events = new object[] { new { type = "pointer", phase = "click", x = pt[0].x, y = pt[0].y, button, clicks } } });
            if (!c.Last.Ok) c.Check(false, what + ": ui.input click", AcceptanceCtx.Short(c.Last?.ToString()));
        }

        /// <summary>Types into a text / number field: a click into its text input, Ctrl+A, the text, Return.</summary>
        static IEnumerator U2Type(U2 s, Func<VisualElement> find, string text, string what, bool enter = true)
        {
            var c = s.C;
            VisualElement Input() { var f = find(); return f?.Q("unity-text-input") ?? f; }
            yield return U2Click(s, Input, what);
            var events = new List<object> { new { type = "key", key = "A", mods = new[] { "ctrl" } } };
            if (text.Length > 0) events.Add(new { type = "text", text });
            else events.Add(new { type = "key", key = "Delete" });
            if (enter) events.Add(new { type = "key", key = "Return" });
            yield return c.Call(new { cmd = "ui.input", frames = 4, events = events.ToArray() });
            if (!c.Last.Ok) c.Check(false, what + ": ui.input typing", AcceptanceCtx.Short(c.Last?.ToString()));
        }

        /// <summary>A pointer drag: down, a move past the drag threshold, the middle, the end, up.</summary>
        static IEnumerator U2Drag(U2 s, Vector2 a, Vector2 b)
        {
            var m = (a + b) / 2;
            yield return s.C.Call(new
            {
                cmd = "ui.input", frames = 3,
                events = new object[]
                {
                    new { type = "pointer", phase = "down", x = a.x, y = a.y, button = 0 },
                    new { type = "pointer", phase = "move", x = a.x + 10, y = a.y, button = 0 },
                    new { type = "pointer", phase = "move", x = m.x, y = m.y, button = 0 },
                    new { type = "pointer", phase = "move", x = b.x, y = b.y, button = 0 },
                    new { type = "pointer", phase = "up", x = b.x, y = b.y, button = 0 },
                },
            });
        }

        /// <summary>A toolbar menu: its button, then the items of <paramref name="path"/> (a submenu opens on hover).</summary>
        static IEnumerator U2Menu(U2 s, string button, params string[] path)
        {
            var c = s.C;
            yield return U2Click(s, () => s.Host.Root.Q<Button>(button), "the toolbar's " + button);
            yield return c.Frames(2);
            for (int i = 0; i < path.Length; i++)
            {
                string id = "menu-item-" + path[i];
                var el = s.Host.Root.Q(id);
                if (el == null) { c.Check(false, "the menu item " + path[i] + " is shown"); s.Ui.Menus?.CloseAll(); yield break; }
                if (el.ClassListContains("hs-menu__item--disabled")) c.Check(false, "the menu item " + path[i] + " is enabled");
                var p = el.worldBound.center;
                if (i < path.Length - 1)
                    yield return c.Call(new { cmd = "ui.input", frames = 3, events = new object[] { new { type = "pointer", phase = "move", x = p.x - 4, y = p.y }, new { type = "pointer", phase = "move", x = p.x, y = p.y } } });
                else yield return U2Click(s, () => s.Host.Root.Q(id), "the menu item " + path[i]);
            }
            yield return c.Frames(3);
            if (s.Ui.Menus != null && s.Ui.Menus.IsOpen) s.Ui.Menus.CloseAll();
        }

        static VisualElement U2TreeRow(U2 s, string id, string suffix = "") => U2PanelRoot(s, WorkspacePanel.PanelId)?.Q("tree-row-" + id + suffix);

        /// <summary>Scrolls the tree to a node's row (or one of its point rows) so it is bound.</summary>
        static IEnumerator U2TreeVisible(U2 s, string id, string suffix = "")
        {
            var ws = WorkspacePanel.Current;
            if (ws == null) yield break;
            ws.Reveal(id);
            yield return s.C.Frames(2);
            var row = ws.Rows.RowOfNode(id);
            if (suffix.StartsWith("-rp-", StringComparison.Ordinal))
            {
                string pid = suffix.Substring(4);
                var pr = FindRow(row, r => r.Kind == TreeRowKind.RoutePoint && r.Point == pid);
                if (pr != null) row = pr;
            }
            if (row != null) { try { ws.Tree.ScrollToItemById(row.Id); } catch (Exception) { } }
            yield return s.C.Frames(4);
        }

        static TreeRow FindRow(TreeRow r, Func<TreeRow, bool> want)
        {
            if (r == null) return null;
            if (want(r)) return r;
            foreach (var ch in r.Children) { var f = FindRow(ch, want); if (f != null) return f; }
            return null;
        }

        /// <summary>Clicks a node's tree row (selects it alone).</summary>
        static IEnumerator U2SelectRow(U2 s, string id)
        {
            yield return U2Show(s, WorkspacePanel.PanelId);
            yield return U2TreeVisible(s, id);
            yield return U2Click(s, () => U2TreeRow(s, id), "the tree row of " + id);
            yield return s.C.Frames(3);
            if (s.Ui.Selection.Primary != id || s.Ui.SubSelection != null)
            {
                // a click on the sole selected row keeps a sub-selection: click again after another row
                yield return U2Click(s, () => U2TreeRow(s, id), "the tree row of " + id + " (again)");
                yield return s.C.Frames(3);
            }
            s.C.Check(s.Ui.Selection.Primary == id, "a click on its tree row selects " + id, s.Ui.Selection.Primary + " / sub " + s.Ui.SubSelection);
        }

        /// <summary>The character inspector's tab.</summary>
        static IEnumerator U2Tab(U2 s, string tab)
        {
            yield return U2Show(s, "inspector");
            yield return U2Click(s, () => U2Inspector(s)?.Q<Button>("tab-" + tab), "the " + tab + " tab");
            yield return s.C.Frames(4);
        }

        /// <summary>A list browser row: the name typed into the search field, the row scrolled to and clicked.</summary>
        static IEnumerator U2PickListRow<T>(U2 s, BaseVerticalCollectionView list, Func<VisualElement> search, string query, Func<T, bool> want, IReadOnlyList<T> rows, IReadOnlyList<int> indices, string what) where T : class
        {
            var c = s.C;
            yield return U2Type(s, search, query, "the search field");
            int pos = -1;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 5)
            {
                pos = -1;
                for (int i = 0; i < indices.Count; i++) if (want(rows[indices[i]])) { pos = i; break; }
                if (pos >= 0) break;
                yield return c.Frames(2);
            }
            if (!c.Check(pos >= 0, $"search \"{query}\" lists {what}", indices.Count + " results")) yield break;
            list.ScrollToItem(pos);
            yield return c.Frames(4);
            T target = rows[indices[pos]];
            yield return U2Click(s, () => list.Query<Label>().Where(l => ReferenceEquals(l.userData, target) && l.worldBound.height > 0).First(), what);
            c.Check(list.selectedIndex == pos, what + " is selected", list.selectedIndex + " / " + pos);
        }

        /// <summary>
        /// A drag on the move gizmo's X handle (tool.move) of the current selection / sub-selection: the pivot moves along the
        /// handle's axis by the axis parameter under the pointer (1e-3) and the history grows by one (label when given).
        /// </summary>
        static IEnumerator U2GizmoMoveX(U2 s, string frameId, string what, string label)
        {
            var c = s.C;
            var ui = s.Ui;
            yield return U2Act(s, "tool.move");
            ui.Viewport.Camera.Frame(new[] { frameId });
            yield return c.Frames(6);
            U2RenderGizmos(s);
            var g = ui.Viewport.Gizmo;
            {
                // centre the view on the gizmo (a sub-object, e.g. a route point, can lie outside the framed node's view)
                var cv = ui.Viewport.Camera.Current;
                ui.Viewport.Camera.SetView(g.PivotWorld, new Vector3(cv.Rot.X, cv.Rot.Y, cv.Rot.Z), Mathf.Max(cv.Distance, 25f), cv.Fov);
                yield return c.Frames(6);
                U2RenderGizmos(s);
            }
            if (!c.Check(g.Visible, "the move gizmo is shown on " + what)) yield break;
            var p0 = g.PivotWorld;
            var axis = (g.Axes * Vector3.right).normalized;
            float L = g.LengthWorld;
            Vector2 a0 = g.ToPanel(p0), aT = g.ToPanel(p0 + axis * L);
            Vector2 grab = default;
            bool found = false;
            foreach (float f in new[] { 0.6f, 0.75f, 0.45f, 0.85f, 0.3f })
            {
                grab = Vector2.Lerp(a0, aT, f);
                var hit = g.HitTest(grab);
                if (hit.Kind == HS2Next.UI.Gizmos.HandleKind.MoveAxis && hit.Axis == 0) { found = true; break; }
            }
            if (!c.Check(found, "the move-X handle under the grab point on " + what,
                    $"{g.HitTest(Vector2.Lerp(a0, aT, 0.6f))}; pivot {p0}, length {L}, axis {axis}, panel {a0} → {aT}, viewport {ui.Viewport.Element.worldBound}, tool {g.ShownTool}, sub {ui.SubSelection}")) yield break;
            Vector2 end = grab + (aT - a0).normalized * 40f;
            float expect = AxisParam(ui.Viewport.RayAt(end), p0, axis) - AxisParam(ui.Viewport.RayAt(grab), p0, axis);
            yield return U2Hist(s);
            int h0 = s.HistN;
            yield return c.Call(new
            {
                cmd = "ui.input", frames = 4,
                events = new object[]
                {
                    new { type = "pointer", phase = "down", x = grab.x, y = grab.y, button = 0 },
                    new { type = "pointer", phase = "move", x = (grab.x + end.x) / 2, y = (grab.y + end.y) / 2, button = 0 },
                    new { type = "pointer", phase = "move", x = end.x, y = end.y, button = 0 },
                    new { type = "pointer", phase = "up", x = end.x, y = end.y, button = 0 },
                },
            });
            yield return c.Frames(4);
            U2RenderGizmos(s);
            var p1 = g.PivotWorld;
            var d = p1 - p0;
            float along = Vector3.Dot(d, axis);
            yield return U2Hist(s);
            c.Check(Mathf.Abs(along - expect) < 1e-3f && (d - axis * along).magnitude < 1e-3f && s.HistN == h0 + 1 && (label == null || s.HistTop == label),
                $"a drag on move X moves {what} along the axis by {along:0.####} (expected {expect:0.####}), one entry{(label != null ? " \"" + label + "\"" : "")}", $"off-axis {(d - axis * along).magnitude:G3}, {s.HistN - h0} / {s.HistTop}");
        }

        /// <summary>Renders the view camera once with the gizmo pass (an Editor batch session renders the view only on demand, so
        /// the gizmo's state is built by a render; the player renders every frame anyway).</summary>
        static void U2RenderGizmos(U2 s)
        {
            var cam = s.Ui.Viewport?.ViewCamera;
            if (!cam) return;
            int w = Mathf.Max(64, cam.pixelWidth), h = Mathf.Max(64, cam.pixelHeight);
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var prevT = cam.targetTexture;
            var prevR = cam.rect;
            s.Host.GizmosIntoTargets = true;
            try
            {
                cam.targetTexture = rt;
                cam.rect = new Rect(0, 0, 1, 1);
                cam.Render();
            }
            finally
            {
                cam.targetTexture = prevT;
                cam.rect = prevR;
                s.Host.GizmosIntoTargets = false;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>The shortcut router's last resolutions and the focus (diagnostics).</summary>
        static IEnumerator U2Keys(U2 s, string when)
        {
            yield return s.C.Call(new { cmd = "ui.state" });
            s.C.Info($"{when}: focus {(string)s.C.J["focus"]} ({(string)s.C.J["focusKind"]}), modal {(string)s.C.J["modal"]}, keys {s.C.J["keys"]?.ToString(Newtonsoft.Json.Formatting.None)}");
        }

        /// <summary>The last toasts (diagnostics: a write the UI refused shows one).</summary>
        static IEnumerator U2Toasts(U2 s, string when)
        {
            yield return s.C.Call(new { cmd = "ui.state" });
            s.C.Info($"{when}: toasts {s.C.J["toasts"]?.ToString(Newtonsoft.Json.Formatting.None)}");
        }

        /// <summary>The digest of UI1's rule without the view (scene.camera.current: a view change is not recorded, so undo
        /// does not restore it).</summary>
        static string U2Digest(JObject reply)
        {
            var doc = DocOf(reply)?.DeepClone();
            if (doc == null) return null;
            doc.SelectToken("scene.camera.current")?.Parent?.Remove();
            foreach (var t in Captured(doc)) t.Parent?.Remove();
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(doc.ToString(Newtonsoft.Json.Formatting.None)))).Replace("-", "");
        }

        /// <summary>A PNG's size from its header (the image is not decoded).</summary>
        static (int w, int h) U2PngSize(string path)
        {
            try
            {
                if (path == null || !File.Exists(path)) return (0, 0);
                var b = new byte[24];
                using (var f = File.OpenRead(path)) if (f.Read(b, 0, 24) < 24) return (0, 0);
                int Be(int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
                return (Be(16), Be(20));
            }
            catch (Exception) { return (0, 0); }
        }
    }
}
