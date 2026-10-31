// UI7 of the bridge acceptance (Phase 4.4 integrator T): the Clothes page of the
// Maker's Edit tab and Studio's Costume tab driven through the bridge's ui.* commands in Editor play (BridgeTest.RunPlayUi
// after UI6, or BridgeTest.RunPlayUi7 alone) and in the app player (-bridgeAccept with -ui). Without a running UI the case is
// skipped with a warning (ui.state enabled false).
// What is read how: the UI facts (a row or button shown, enabled, its tooltip, a dialog open) through ui.query and ui.state;
// the documents through chara.edit.get; the history through `history`; the clothes facts, creators, materials, objects and
// masks through chara.edit.clothes; the last flush through chara.edit.info; the clothes digest through chara.edit.digest
// {parts: clothes}; the served copy through content.redirect; the clothes state through chara.clothes. Actions: the element is
// found by its name in the app's UI tree, revealed (scroll views scrolled to it, a toast over it waited for, as UI3–UI6) and
// clicked, typed or dragged through ui.input; list cells are found inside their list among the shown cells, and the list's
// search results are read from the list (as UI6). Numbers only; nothing is captured.
//   1 file.new, add.female (BR-Chan), Edit › Clothes (tab-edit, edit-tree-clothes): the kind selector shows Top, eight kind
//     buttons; the documents and the clothes digest after it are the "step 1" state.
//   2 Top › Colour 1: 30 typed into «ツヤ» → colors[0].gloss 0.3, one entry "Edit clothes"; the flush rebuilt detail creators only.
//   3 «カラー» of set 1: picker, a drag in the SV square (3 frames), Enter → one entry; no alpha strip (BR-Chan's top has no
//     useColorA01); the stored alpha unchanged.
//   4 the pattern button of set 1 → its list; a vanilla pattern → one entry "Change item"; the pattern colour and the five
//     layout rows appear; 150 typed into «幅» → stored 1.5, _patternuv1.x = lerp(20, 1, clamp01(1.5)) = 1 on the colour creators.
//   5 ↺ on «ツヤ» → the prefab default (chara.edit.clothes defaults[0].gloss), one entry "Reset value".
//   6 «初期カラー» → the three sets = the prefab defaults, one entry "Edit clothes".
//   7 Bottom › 241#9 «ショートレギンス» (useColorA01) → one entry; the picker of set 1 offers alpha; a click at the middle of its
//     alpha strip, Enter → stored alpha 0.5, the colour creator's _Color.a 0.5; the earlier bottom again → no alpha strip, the stored alpha as the
//     item change wrote it (C7: the prefab's default colours) and alpha 1 on the creator.
//   8 Top › 240#1 «ワンピース» → the Bottom button disabled with its tooltip, the bottom skipped, the stored bottom kept; the
//     earlier top again → the Bottom button enabled, the bottom realized.
//   9 Top › 240#223 «道着» → «破れ具合» shown (content.redirect: served by Terable Kimonos' copy); a drag of the break slider and
//     40 typed → stored 0.4, _AlphaEx 0.6 on slot 0 of every rendNormal renderer, the masks' _alpha_c 1.
//  10 Top › 240#51 «クリスマスポンチョ»: «オプションパーツ01を表示» off → hideOptions[0] true, the option objects inactive; the
//     State tab's top «half»; the option back on → the top stays half (state 1, def / half objects unchanged).
//  11 Inner top › 242#15 «ワンピース» → the Inner bottom button disabled; the earlier inner top again → enabled.
//  12 the outfit-card button → the dialog; «ガーリーブラウス» (female folder) with «服読込み» → one entry "Load coordinate",
//     clothes = the card's, accessories unchanged; again with «全て読込み» → accessories = the card's, state.accessories holds
//     their ids.
//  13 the top list searched for «スーツ», cleared with Backspace: every row and the current mark back.
//  14 edit.undo back to step 1 (documents byte-identical, clothes digest equal incl. rts), edit.redo to the end.
//  15 file.saveAs → file.new → file.open: documents and the clothes digest equal to the end state.
//  16 add.male (HERO107), Clothes: four kind buttons (Top, Bottom, Gloves, Shoes), no break row on its items; Top › 140#201
//     «ロングコート» → the option toggles shown.
//  17 the Costume tab (tab-costume): «作業着» (male folder) loaded with «読込» → one entry "Load coordinate", clothes and
//     accessories = the card's (all), the clothes state kept.
// Every step checks the history entry count and label and the document values; no chara.editFailed may be logged; the
// flushes' times are reported.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Panels;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.App
{
    public static partial class BridgeAcceptance
    {
        const string U7Clothes = "appearance.coord.clothes.";
        const string U7EditClothes = HS2Next.UI.Core.OpLabels.EditClothes;
        const string U7LoadCoordinate = HS2Next.UI.Core.OpLabels.LoadCoordinate;

        static readonly string[] U7DigestSections = { "materials", "creators", "bones", "rts", "objects" };

        sealed class U7State
        {
            public U4State S4;
            public UiHost Host => S4.Host;
            public UiContext Ui => S4.Ui;
            public string F, M;
            public int U1;
            public string Docs1, DocsEnd;
            public JObject Dig1, DigEnd;
            public int EditFailed;
            public string Top0, Bottom0, InnerTop0;
        }

        public static IEnumerator Ui7(AcceptanceCtx c)
        {
            c.Id = "UI7";
            yield return c.Call(new { cmd = "ui.state" });
            if (c.Last == null || !c.Last.Ok || (bool?)c.J["enabled"] != true)
            {
                c.Warn("UI7 skipped: the Studio UI is not running (start with -ui; " + AcceptanceCtx.Short(c.Last?.ToString()) + ")");
                yield break;
            }
            var s = new U7State { S4 = new U4State { Host = UiHost.Current } };
            s.S4.Ui = s.S4.Host?.Context;
            if (!c.Check(s.Ui != null, "UiHost in the app's process")) yield break;
            Application.LogCallback onLog = (m, st, t) => { if (m != null && m.Contains("chara.editFailed")) s.EditFailed++; };
            Application.logMessageReceived += onLog;
            var total = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                yield return U7Female(c, s);
                c.Info($"UI7: female part {total.Elapsed.TotalSeconds:F1} s");
                if (s.F != null)
                {
                    total.Restart();
                    yield return U7Male(c, s);
                    c.Info($"UI7: male part {total.Elapsed.TotalSeconds:F1} s");
                }
            }
            finally
            {
                Application.logMessageReceived -= onLog;
                PartSelectorState.Clothes.Part = 0;
            }
            c.Id = "UI7";
            c.Check(s.EditFailed == 0, $"no chara.editFailed logged during UI7 ({s.EditFailed})");
        }

        // ---------------------------------------------------------------- the female part (steps 1–15)

        static IEnumerator U7Female(AcceptanceCtx c, U7State s)
        {
            var ui = s.Ui;
            var e1 = new JObject[1];
            var n1 = new int[1];
            var v1 = new JObject[1];

            // ---- 1: a new scene with BR-Chan, Edit › Clothes
            c.Id = "UI7.1";
            yield return c.Call(new { cmd = "ui.action", action = "file.new", args = new { force = true } });
            c.CheckOk("ui.action file.new {force}");
            string card = Path.Combine(AppCore.Current.Paths.Hs2CharaDir(1) ?? "", c.FemaleRel ?? "");
            yield return c.Call(new { cmd = "ui.action", action = "add.female", args = new { path = card } }, 180);
            string id = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && !string.IsNullOrEmpty(id), "ui.action add.female {path} → a new character", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            yield return U5WaitBuilt(c, ui, id);
            if (!c.Check(ui.Runtime.FindChara(id)?.State == RealizationState.Built, "the character is built")) yield break;
            if (PartSelectorState.Clothes.Part != 0) c.Info($"UI7: the clothes kind selector's view state was {PartSelectorState.Clothes.Part}; Top is selected by a click");
            var ok = new bool[1];
            yield return U7OpenClothes(c, s, id, 0, ok);
            if (!ok[0]) yield break;
            var shownKinds = new List<string>();
            for (int k = 0; k < 8; k++)
            {
                yield return U7Q(c, "clothes-kind-" + U7K(k), e1);
                if (U7Vis(e1[0])) shownKinds.Add(U7K(k));
            }
            c.Check(shownKinds.Count == 8, "eight kind buttons «トップス … 靴» shown (ui.query clothes-kind-*: " + string.Join(", ", shownKinds) + ")");
            yield return U7Q(c, "clothes-kind-group-top", e1);
            c.Check(U7Vis(e1[0]), "the kind selector shows Top (clothes-kind-group-top)");
            s.F = id;
            yield return U7Undo(c, n1);
            s.U1 = n1[0];
            var docs = new string[1];
            yield return U4Docs(c, id, docs);
            s.Docs1 = docs[0];
            var dig = new JObject[1];
            yield return U7Digest(c, id, dig);
            s.Dig1 = dig[0];
            if (!c.Check(s.Docs1 != null && s.Dig1 != null, "chara.edit.get and chara.edit.digest {parts: clothes} of step 1", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            c.Info($"UI7: clothes digest of step 1 {U7DigestLine(s.Dig1)}");
            yield return U7Get(c, id, new[] { "coord.clothes.top.item", "coord.clothes.bottom.item", "coord.clothes.innerTop.item" }, v1);
            s.Top0 = (string)v1[0]?["coord.clothes.top.item"];
            s.Bottom0 = (string)v1[0]?["coord.clothes.bottom.item"];
            s.InnerTop0 = (string)v1[0]?["coord.clothes.innerTop.item"];
            c.Info($"UI7: BR-Chan's top {s.Top0}, bottom {s.Bottom0}, inner top {s.InnerTop0}");

            // ---- 2: Top › Colour 1, 30 typed into «ツヤ»
            c.Id = "UI7.2";
            {
                var kr = new JObject[1];
                yield return U7Kind(c, id, "top", kr);
                c.Info("UI7: BR-Chan's top facts " + U5T(kr[0]?["facts"]?["useColorN"]) + " useColorA " + U5T(kr[0]?["facts"]?["useColorA"]) + ", prefab " + (string)kr[0]?["prefab"]);
                yield return U7TypeRow(c, s, id, "top.colors[0].gloss", "30", U7EditClothes, "«ツヤ» of set 1");
                yield return U7Get(c, id, new[] { "coord.clothes.top.colors[0].gloss" }, v1);
                double g = (double?)v1[0]?["coord.clothes.top.colors[0].gloss"] ?? double.NaN;
                c.Check(Math.Abs(g - 0.3) < 1e-6, $"typed 30 → colors[0].gloss 0.3 ({g})");
                yield return c.Call(new { cmd = "chara.edit.info", key = id });
                var rebuilt = c.J["clothes"]?["lastFlush"]?["creatorsRebuilt"] as JArray;
                c.Check(rebuilt != null && rebuilt.Count > 0 && rebuilt.All(x => (bool?)x["detail"] == true && (bool?)x["colour"] == false),
                    "chara.edit.info clothes.lastFlush.creatorsRebuilt = detail creators only (" + U5T(rebuilt) + ")");
                U7Flush(c, c.J, "the typed gloss");
            }

            // ---- 3: «カラー» of set 1: the picker
            c.Id = "UI7.3";
            {
                var kr = new JObject[1];
                yield return U7Kind(c, id, "top", kr);
                bool useA = (bool?)(kr[0]?["facts"]?["useColorA"] as JArray)?[0] == true;
                c.Check(!useA, "BR-Chan's top has no useColorA01 (" + U5T(kr[0]?["facts"]?["useColorA"]) + ")");
                yield return U7ColourDrag(c, s, id, "top.colors[0].base", "«カラー» of set 1", false);
            }

            // ---- 4: the pattern window of set 1
            c.Id = "UI7.4";
            {
                yield return U7Get(c, id, new[] { "coord.clothes.top.colors[0].pattern" }, v1);
                string pat0 = (string)v1[0]?["coord.clothes.top.colors[0].pattern"];
                string pick = pat0 == "illusion.hs2#1" ? "illusion.hs2#2" : "illusion.hs2#1";
                yield return U7Click(c, s, "clothes-pattern-top-1", "the pattern button of set 1 (clothes-pattern-top-1)");
                yield return U7WaitShown(c, U7Field("top.colors[0].pattern"), true, 120, e1);
                c.Check(U7Vis(e1[0]), "the button opens the 348 list (" + U7Field("top.colors[0].pattern") + ")");
                var list = U7El(s, U7Field("top.colors[0].pattern")) as MakerThumbList;
                yield return U7Undo(c, n1);
                int u = n1[0];
                yield return U4ClickCell(c, s.S4, list, pick, "the pattern cell " + pick);
                yield return U7WaitValue(c, id, "coord.clothes.top.colors[0].pattern", pick, 150);
                var layoutRows = new[] { "top.colors[0].patternColor", "top.colors[0].layout#0", "top.colors[0].layout#1", "top.colors[0].layout#2", "top.colors[0].layout#3", "top.colors[0].rotation" };
                var left = new List<string>();
                yield return U7WaitRows(c, layoutRows.Select(U7Field).ToArray(), true, 300, left);
                yield return U7Entry(c, u, HS2Next.UI.Core.OpLabels.ChangeItem, "the pattern pick");
                yield return U7Get(c, id, new[] { "coord.clothes.top.colors[0].pattern" }, v1);
                c.Check((string)v1[0]?["coord.clothes.top.colors[0].pattern"] == pick, $"colors[0].pattern = {pick} (was {pat0})");
                c.Check(left.Count == 0, "the pattern colour and the five layout rows appear", left.Count == 0 ? "" : "still hidden: " + string.Join(", ", left));
                yield return U7TypeRow(c, s, id, "top.colors[0].layout#0", "150", U7EditClothes, "«幅» of set 1");
                yield return U7Get(c, id, new[] { "coord.clothes.top.colors[0].layout" }, v1);
                var lay = v1[0]?["coord.clothes.top.colors[0].layout"] as JArray;
                c.Check(lay != null && Math.Abs((double)lay[0] - 1.5) < 1e-6, $"typed 150 → layout x stored 1.5 ({U5T(lay)})");
                var kr = new JObject[1];
                yield return U7Kind(c, id, "top", kr);
                var uvs = U7CreatorProps(kr[0], "colour", "_patternuv1").ToList();
                c.Check(uvs.Count > 0 && uvs.All(t => t is JArray a && a.Count == 4 && Math.Abs((double)a[0] - 1.0) < 1e-6),
                    $"_patternuv1.x = lerp(20, 1, clamp01(1.5)) = 1 on every colour creator ({uvs.Count}: {string.Join(" ", uvs.Select(U5T))})");
                yield return U7Click(c, s, "clothes-pattern-top-1", "the pattern button of set 1 (closing the list)");
            }

            // ---- 5: ↺ on «ツヤ»
            c.Id = "UI7.5";
            {
                var kr = new JObject[1];
                yield return U7Kind(c, id, "top", kr);
                double want = (double?)(kr[0]?["defaults"] as JArray)?[0]?["gloss"] ?? double.NaN;
                var row = U7El(s, U7Field("top.colors[0].gloss"));
                yield return U7Undo(c, n1);
                int u = n1[0];
                yield return U4Click(c, s.S4, row?.Q("reset"), "«ツヤ»'s ↺");
                yield return c.Frames(3);
                yield return U7Entry(c, u, HS2Next.UI.Core.OpLabels.ResetValue, "↺ «ツヤ»");
                yield return U7Get(c, id, new[] { "coord.clothes.top.colors[0].gloss" }, v1);
                double g = (double?)v1[0]?["coord.clothes.top.colors[0].gloss"] ?? double.NaN;
                c.Check(!double.IsNaN(want) && Math.Abs(g - want) < 1e-6, $"↺ → the prefab default defGloss01 {want} ({g})");
            }

            // ---- 6: «初期カラー»
            c.Id = "UI7.6";
            {
                yield return U7Undo(c, n1);
                int u = n1[0];
                yield return U7Click(c, s, "clothes-default-colours-top", "«初期カラー» (clothes-default-colours-top)");
                yield return c.Frames(3);
                yield return U7Entry(c, u, U7EditClothes, "«初期カラー»");
                var kr = new JObject[1];
                yield return U7Kind(c, id, "top", kr);
                yield return U7Get(c, id, new[] { "coord.clothes.top.colors" }, v1);
                var sets = v1[0]?["coord.clothes.top.colors"] as JArray;
                var defs = kr[0]?["defaults"] as JArray;
                var bad = new List<string>();
                for (int i = 0; i < 3; i++)
                {
                    var d = defs?[i] as JObject;
                    var sj = sets?[i] as JObject;
                    if (d == null || sj == null) { bad.Add("set " + (i + 1) + " missing"); continue; }
                    foreach (var m in new[] { "base", "pattern", "patternColor", "layout", "rotation", "gloss", "metallic" })
                        if (!U7Same(sj[m], d[m])) bad.Add($"set {i + 1} {m} {U5T(sj[m])} vs {U5T(d[m])}");
                }
                c.Check(bad.Count == 0, "the three sets = the prefab defaults (chara.edit.clothes defaults)", string.Join("; ", bad.Take(6)));
            }

            // ---- 7: Bottom › 241#9 «ショートレギンス»; the main colour's alpha
            c.Id = "UI7.7";
            yield return U7OpenClothes(c, s, id, 1, ok);
            if (!ok[0]) yield break;
            {
                var kr = new JObject[1];
                yield return U7Pick(c, s, id, 1, "illusion.hs2#9", "«ショートレギンス» (241#9)", kr);
                bool useA = (bool?)(kr[0]?["facts"]?["useColorA"] as JArray)?[0] == true;
                c.Check(useA, "241#9 has useColorA01 (" + U5T(kr[0]?["facts"]?["useColorA"]) + ")");
                var strip = new bool[1];
                yield return U7PickerAlpha(c, s, "bottom.colors[0].base", strip, true);
                c.Check(strip[0], "the picker of set 1 offers the alpha strip (useColorA01)");
                yield return U7Get(c, id, new[] { "coord.clothes.bottom.colors[0].base" }, v1);
                var col = v1[0]?["coord.clothes.bottom.colors[0].base"] as JArray;
                c.Check(col != null && Math.Abs((double)col[3] - 0.5) < 1e-4, $"the alpha strip's middle → stored alpha 0.5 ({U5T(col)})");
                yield return U7Kind(c, id, "bottom", kr);
                var ca = U7CreatorProps(kr[0], "colour", "_Color").ToList();
                c.Check(ca.Count > 0 && col != null && ca.All(t => t is JArray a && a.Count == 4 && Math.Abs((double)a[3] - (double)col[3]) < 1e-6), $"the colour creators' _Color.a = the stored alpha ({string.Join(" ", ca.Select(U5T))})");
                if (s.Bottom0 != null && s.Bottom0 != "illusion.hs2#9")
                {
                    yield return U7Pick(c, s, id, 1, s.Bottom0, "the earlier bottom " + s.Bottom0, kr);
                    bool useA0 = (bool?)(kr[0]?["facts"]?["useColorA"] as JArray)?[0] == true;
                    yield return U7PickerAlpha(c, s, "bottom.colors[0].base", strip, false);
                    c.Check(strip[0] == useA0 && !useA0, $"the earlier bottom: no alpha strip (useColorA01 {useA0})");
                    yield return U7Get(c, id, new[] { "coord.clothes.bottom.colors[0].base" }, v1);
                    var col2 = v1[0]?["coord.clothes.bottom.colors[0].base"] as JArray;
                    var def2 = (kr[0]?["defaults"] as JArray)?[0]?["base"] as JArray;
                    c.Check(col2 != null && def2 != null && Math.Abs((double)col2[3] - (double)def2[3]) < 1e-6,
                        $"the stored alpha is the one the item change wrote (C7: the prefab default {U5T(def2)}; stored {U5T(col2)}; nothing forces it to 1)");
                    var ca2 = U7CreatorProps(kr[0], "colour", "_Color").ToList();
                    c.Check(ca2.Count == 0 || ca2.All(t => t is JArray a && a.Count == 4 && Math.Abs((double)a[3] - 1) < 1e-6), $"alpha 1 on the creators without useColorA01 ({string.Join(" ", ca2.Select(U5T))})");
                }
                else c.Warn("UI7: BR-Chan's bottom is 241#9 already (or none): the way back was not taken");
            }

            // ---- 8: Top › 240#1 «ワンピース»: the bottom disabled
            c.Id = "UI7.8";
            yield return U7OpenClothes(c, s, id, 0, ok);
            if (!ok[0]) yield break;
            {
                yield return U7Get(c, id, new[] { "coord.clothes.bottom.item" }, v1);
                string bottom = (string)v1[0]?["coord.clothes.bottom.item"];
                var kr = new JObject[1];
                yield return U7Pick(c, s, id, 0, "illusion.hs2#1", "«ワンピース» (240#1)", kr);
                yield return U7WaitEnabled(c, "clothes-kind-bottom", false, 120, e1);
                string tip = (string)e1[0]?["tooltip"] ?? "";
                string want = HS2Next.UI.Core.L10n.Get(HS2Next.UI.Core.InspectorSchemas.ClothesOnePieceTipKey);
                c.Check(e1[0] != null && !U7En(e1[0]) && tip.Contains(want), $"the Bottom button is disabled with «{want}» (ui.query enabled {(bool?)e1[0]?["enabled"]}, tooltip «{tip}»)");
                var br = new JObject[1];
                yield return U7Kind(c, id, "bottom", br);
                yield return U7Get(c, id, new[] { "coord.clothes.bottom.item" }, v1);
                c.Check((bool?)br[0]?["skipped"] == true && (string)v1[0]?["coord.clothes.bottom.item"] == bottom,
                    $"chara.edit.clothes bottom skipped ({(bool?)br[0]?["skipped"]}), the stored bottom kept ({(string)v1[0]?["coord.clothes.bottom.item"]})");
                yield return U7Pick(c, s, id, 0, s.Top0, "the earlier top " + s.Top0, kr);
                yield return U7WaitEnabled(c, "clothes-kind-bottom", true, 120, e1);
                yield return U7Kind(c, id, "bottom", br);
                c.Check(U7En(e1[0]) && (bool?)br[0]?["skipped"] == false && (bool?)br[0]?["realized"] == true,
                    $"the earlier top: the Bottom button enabled, the bottom realized again (skipped {(bool?)br[0]?["skipped"]}, realized {(bool?)br[0]?["realized"]})");
            }

            // ---- 9: Top › 240#223 «道着»: the break (the redirect)
            c.Id = "UI7.9";
            {
                var kr = new JObject[1];
                yield return U7Pick(c, s, id, 0, "illusion.hs2#223", "«道着» (240#223)", kr);
                yield return c.Call(new { cmd = "content.redirect", row = new { category = 240, @ref = "illusion.hs2#223", column = "MainData" } });
                string guid = (string)c.J["served"]?["guid"];
                c.Check(c.Last != null && c.Last.Ok && guid != null && guid.IndexOf("TearableKimonos", StringComparison.OrdinalIgnoreCase) >= 0,
                    $"content.redirect: 240#223's MainData is served by Terable Kimonos' copy ({guid ?? AcceptanceCtx.Short(c.Last?.ToString())}; redirect {U5T(c.J["switch"])})");
                bool useBreak = (bool?)kr[0]?["facts"]?["useBreak"] == true;
                yield return U7WaitShown(c, U7Field("top.breakRate"), true, 300, e1);
                c.Check(useBreak && U7Vis(e1[0]), $"the Other section shows «破れ具合» (useBreak {useBreak})");
                var row = U7El(s, U7Field("top.breakRate")) as MakerSliderRow;
                if (row != null)
                {
                    yield return U7Undo(c, n1);
                    int u = n1[0];
                    yield return U4DragKnob(c, s.S4, row, (float)(row.Control.Value / 100.0), "the break slider");
                    yield return U7Entry(c, u, U7EditClothes, "the break drag");
                }
                yield return U7TypeRow(c, s, id, "top.breakRate", "40", U7EditClothes, "«破れ具合»");
                yield return U7Get(c, id, new[] { "coord.clothes.top.breakRate" }, v1);
                double b = (double?)v1[0]?["coord.clothes.top.breakRate"] ?? double.NaN;
                c.Check(Math.Abs(b - 0.4) < 1e-6, $"40 → breakRate 0.4 ({b})");
                var cl = new JObject[1];
                yield return c.Call(new { cmd = "chara.edit.clothes", key = id, kind = "top", served = false });
                cl[0] = c.Last != null && c.Last.Ok ? (JObject)c.J.DeepClone() : null;
                var mats = ((cl[0]?["kinds"] as JArray)?[0]?["materials"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
                var slot0 = mats.Where(m => (int?)m["group"] > 0 && (int?)m["slot"] == 0).ToList();
                c.Check(slot0.Count > 0 && slot0.All(m => m["alphaEx"] != null && m["alphaEx"].Type != JTokenType.Null && Math.Abs((double)m["alphaEx"] - 0.6) < 1e-6),
                    $"_AlphaEx 0.6 on slot 0 of every rendNormal renderer ({slot0.Count}: {string.Join(", ", slot0.Select(m => (string)m["renderer"] + " " + U5T(m["alphaEx"])))})");
                var later = mats.Where(m => (int?)m["group"] > 0 && (int?)m["slot"] > 0 && m["alphaEx"] != null && m["alphaEx"].Type != JTokenType.Null).Select(m => (string)m["renderer"] + "[" + (int?)m["slot"] + "] " + U5T(m["alphaEx"])).ToList();
                if (later.Count > 0) c.Info("UI7: slots past 0 keep their prefab _AlphaEx: " + string.Join(", ", later));
                var ac = cl[0]?["masks"]?["body"]?["_alpha_c"];
                c.Check(ac != null && ac.Type != JTokenType.Null && Math.Abs((double)ac - 1) < 1e-6, $"the masks' _alpha_c 1 on the body ({U5T(ac)})");
                yield return c.Call(new { cmd = "chara.edit.info", key = id });
                U7Flush(c, c.J, "the typed break");
            }

            // ---- 10: Top › 240#51 «クリスマスポンチョ»: an option part in the half state
            c.Id = "UI7.10";
            {
                var kr = new JObject[1];
                yield return U7Pick(c, s, id, 0, "illusion.hs2#51", "«クリスマスポンチョ» (240#51)", kr);
                int opt1 = (int?)kr[0]?["facts"]?["objOpt01"] ?? 0;
                yield return U7WaitShown(c, U7Field("top.hideOptions[0]"), true, 300, e1);
                c.Check(opt1 > 0 && U7Vis(e1[0]), $"«オプションパーツ01を表示» shown (option parts 01: {opt1})");
                yield return U7Toggle(c, s, id, "top.hideOptions[0]", "«オプションパーツ01を表示» off");
                yield return U7Get(c, id, new[] { "coord.clothes.top.hideOptions" }, v1);
                yield return U7Kind(c, id, "top", kr);
                var o1 = kr[0]?["objects"]?["opt1"] as JArray;
                c.Check((bool?)(v1[0]?["coord.clothes.top.hideOptions"] as JArray)?[0] == true && o1 != null && o1.Count > 0 && o1.All(x => (bool?)x == false),
                    $"hideOptions[0] true, the option objects inactive ({U5T(o1)})");
                // the State tab: top «half»
                yield return U7Click(c, s, "tab-state", "the State tab");
                yield return c.Frames(3);
                var half = U7El(s, "field-chara.state.clothes[0]")?.Q<Button>("seg-1");
                yield return U7Undo(c, n1);
                int u = n1[0];
                yield return U4Click(c, s.S4, half, "the top's «half» (field-chara.state.clothes[0] seg-1)");
                yield return c.Frames(3);
                yield return c.Call(new { cmd = "chara.clothes", key = id });
                var st = c.J["states"] as JArray;
                yield return U7Undo(c, n1);
                c.Check(st != null && (int)st[0] == 1 && n1[0] == u + 1, $"chara.clothes states[0] = 1 (half) after the State tab click, one entry ({U5T(st)})");
                yield return U7Kind(c, id, "top", kr);
                var obj0 = kr[0]?["objects"]?.DeepClone() as JObject;
                c.Info("UI7: the top's objects in the half state before the toggle: " + U5T(obj0));
                yield return U7OpenClothes(c, s, id, 0, ok);
                if (!ok[0]) yield break;
                yield return U7Toggle(c, s, id, "top.hideOptions[0]", "«オプションパーツ01を表示» on again");
                yield return c.Call(new { cmd = "chara.clothes", key = id });
                st = c.J["states"] as JArray;
                yield return U7Kind(c, id, "top", kr);
                var obj1 = kr[0]?["objects"] as JObject;
                bool sameDef = obj0 != null && obj1 != null && JToken.DeepEquals(obj0["topDef"], obj1["topDef"]) && JToken.DeepEquals(obj0["topHalf"], obj1["topHalf"])
                               && JToken.DeepEquals(obj0["botDef"], obj1["botDef"]) && JToken.DeepEquals(obj0["botHalf"], obj1["botHalf"]);
                c.Check(st != null && (int)st[0] == 1 && sameDef, $"the top stays half: state {U5T(st?[0])}, def / half objects unchanged ({U5T(obj1)})");
                yield return U7Get(c, id, new[] { "coord.clothes.top.hideOptions" }, v1);
                c.Check((bool?)(v1[0]?["coord.clothes.top.hideOptions"] as JArray)?[0] == false, "hideOptions[0] false again");
            }

            // ---- 11: Inner top › 242#15 «ワンピース»
            c.Id = "UI7.11";
            yield return U7OpenClothes(c, s, id, 2, ok);
            if (!ok[0]) yield break;
            {
                var kr = new JObject[1];
                yield return U7Pick(c, s, id, 2, "illusion.hs2#15", "«ワンピース» (242#15)", kr);
                yield return U7WaitEnabled(c, "clothes-kind-innerBottom", false, 120, e1);
                c.Check(e1[0] != null && !U7En(e1[0]), $"the Inner bottom button is disabled (tooltip «{(string)e1[0]?["tooltip"]}»)");
                if (s.InnerTop0 != null && s.InnerTop0 != "illusion.hs2#15")
                {
                    yield return U7Pick(c, s, id, 2, s.InnerTop0, "the earlier inner top " + s.InnerTop0, kr);
                    yield return U7WaitEnabled(c, "clothes-kind-innerBottom", true, 120, e1);
                    c.Check(U7En(e1[0]), "the earlier inner top: the Inner bottom button enabled");
                }
            }

            // ---- 12: the outfit card «ガーリーブラウス»
            c.Id = "UI7.12";
            yield return U7OpenClothes(c, s, id, 0, ok);
            if (!ok[0]) yield break;
            {
                string file = U7CoordFile(1, "ガーリーブラウス");
                if (file == null) c.Check(false, "the female coordinate «ガーリーブラウス» is in the install (UserData/coordinate/female)");
                else
                {
                    var src = ui.Ops.LoadCoordinate(file, new FindingList(), out _, out var fail);
                    if (!c.Check(src != null, "the card reads (the expected clothes and accessories)", fail?.Message)) yield break;
                    var want = U7CoordJson(src);
                    yield return U7Get(c, id, new[] { "coord.accessories" }, v1);
                    var acc0 = v1[0]?["coord.accessories"]?.DeepClone();
                    yield return U7LoadCard(c, s, id, "clothes-coordinate-load", "coordinate-dialog-browser", "ガーリーブラウス", "dialog-button-clothes", "«服読込み»");
                    yield return U7Get(c, id, new[] { "coord.clothes", "coord.accessories", "coord.name" }, v1);
                    c.Check(JToken.DeepEquals(v1[0]?["coord.clothes"], want?["clothes"]), "clothes = the card's", JToken.DeepEquals(v1[0]?["coord.clothes"], want?["clothes"]) ? "" : U4JsonDiff(want?["clothes"]?.ToString(Newtonsoft.Json.Formatting.None), v1[0]?["coord.clothes"]?.ToString(Newtonsoft.Json.Formatting.None)));
                    c.Check(JToken.DeepEquals(v1[0]?["coord.accessories"], acc0), "the accessories unchanged");
                    c.Info($"UI7: the coordinate name after «服読込み»: {(string)v1[0]?["coord.name"]} (the card's {(string)want?["name"]})");
                    yield return U5WaitBuilt(c, ui, id);
                    yield return U7LoadCard(c, s, id, "clothes-coordinate-load", "coordinate-dialog-browser", "ガーリーブラウス", "dialog-button-all", "«全て読込み»");
                    yield return U7Get(c, id, new[] { "coord.clothes", "coord.accessories" }, v1);
                    var accs = v1[0]?["coord.accessories"] as JArray;
                    c.Check(JToken.DeepEquals(accs, want?["accessories"]), $"accessories = the card's ({accs?.Count} entries)",
                        JToken.DeepEquals(accs, want?["accessories"]) ? "" : U4JsonDiff(want?["accessories"]?.ToString(Newtonsoft.Json.Formatting.None), accs?.ToString(Newtonsoft.Json.Formatting.None)));
                    yield return c.Call(new { cmd = "chara.accessories", key = id });
                    var vis = c.J["accessories"] as JObject;
                    var ids = (accs ?? new JArray()).Select(a => (string)a["id"]).Where(x => x != null).ToList();
                    var missing = ids.Where(x => vis == null || vis[x] == null).ToList();
                    c.Check(missing.Count == 0, $"state.accessories holds the new ids ({ids.Count}: {U5T(vis)})", string.Join(", ", missing));
                    yield return U5WaitBuilt(c, ui, id);
                }
            }

            // ---- 13: the top list's search, cleared with Backspace
            c.Id = "UI7.13";
            yield return U7OpenClothes(c, s, id, 0, ok);
            if (!ok[0]) yield break;
            {
                var pl = U7El(s, U7Field("top.item")) as PartList;
                if (c.Check(pl != null, "the top list"))
                {
                    yield return U7Get(c, id, new[] { "coord.clothes.top.item" }, v1);
                    string cur = (string)v1[0]?["coord.clothes.top.item"];
                    int all = pl.AllItems.Count(x => !x.NotListed);
                    const string word = "スーツ";
                    var search = pl.Q<TextField>("partlist-search");
                    yield return U4Type(c, s.S4, search, word, "the top list's search field");
                    for (int i = 0; i < 60 && pl.Items.Count(x => !x.NotListed) >= all; i++) yield return c.Frames(1);
                    int filtered = pl.Items.Count(x => !x.NotListed);
                    c.Check(pl.SearchText == word && filtered > 0 && filtered < all, $"the search \"{word}\" shows {filtered} of {all} tops");
                    var bs = Enumerable.Range(0, word.Length + 1).Select(_ => (object)new { type = "key", key = "Backspace" }).ToArray();
                    yield return c.Call(new { cmd = "ui.input", frames = 3, events = bs });
                    c.CheckOk($"ui.input Backspace ×{bs.Length} in the search field");
                    for (int i = 0; i < 60 && (pl.SearchText != "" || pl.Items.Count(x => !x.NotListed) < all); i++) yield return c.Frames(1);
                    if (pl.SearchText != "")
                    {
                        c.Info($"UI7: the search field held \"{pl.SearchText}\" after Backspace (focus lost?); clicked and cleared again");
                        var p = new Vector2[1];
                        yield return U4Reveal(c, s.S4, search.Q("unity-text-input") ?? search, "the top list's search field", p);
                        var ev = new List<object> { new { type = "pointer", phase = "click", x = p[0].x, y = p[0].y, button = 0 } };
                        ev.AddRange(Enumerable.Range(0, pl.SearchText.Length + 2).Select(_ => (object)new { type = "key", key = "Backspace" }));
                        yield return c.Call(new { cmd = "ui.input", frames = 3, events = ev.ToArray() });
                        for (int i = 0; i < 60 && (pl.SearchText != "" || pl.Items.Count(x => !x.NotListed) < all); i++) yield return c.Frames(1);
                    }
                    c.Check(pl.SearchText == "" && pl.Items.Count(x => !x.NotListed) == all, $"Backspace cleared the search: every top shown again ({pl.Items.Count(x => !x.NotListed)} of {all})");
                    c.Check(U4Marked(pl, cur), "the current top " + cur + " carries the current mark");
                }
            }

            // ---- 14: undo to step 1, redo to the end
            c.Id = "UI7.14";
            yield return c.Frames(3);
            var docsE = new string[1];
            yield return U4Docs(c, id, docsE);
            s.DocsEnd = docsE[0];
            var digE = new JObject[1];
            yield return U7Digest(c, id, digE);
            s.DigEnd = digE[0];
            yield return c.Call(new { cmd = "history" });
            var undo = (c.J["undo"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
            int n14 = undo.Count - s.U1;
            c.Info($"UI7: {n14} history entries since step 1: {string.Join(" | ", undo.Take(Math.Max(0, n14)).Reverse())}");
            for (int i = 0; i < n14; i++)
            {
                yield return c.Call(new { cmd = "ui.action", action = "edit.undo" });
                if (!(c.Last != null && c.Last.Ok)) { c.CheckOk("ui.action edit.undo #" + (i + 1)); break; }
                yield return c.Frames(1);
            }
            yield return U5WaitBuilt(c, ui, id);
            yield return c.Frames(6);
            yield return U7Undo(c, n1);
            c.Check(n1[0] == s.U1, $"undo back to step 1 ({n1[0]} entries, {s.U1} at step 1)");
            yield return U4Docs(c, id, docs);
            c.Check(docs[0] != null && docs[0] == s.Docs1, "undo to step 1 → the documents are byte-identical to step 1", docs[0] == s.Docs1 ? "" : U4JsonDiff(s.Docs1, docs[0]));
            yield return U7Digest(c, id, dig);
            c.Check(U7SameDigest(s.Dig1, dig[0], out string d1), "undo to step 1 → the clothes digest equals step 1's (incl. rts)", d1);
            for (int i = 0; i < n14; i++)
            {
                yield return c.Call(new { cmd = "ui.action", action = "edit.redo" });
                if (!(c.Last != null && c.Last.Ok)) { c.CheckOk("ui.action edit.redo #" + (i + 1)); break; }
                yield return c.Frames(1);
            }
            yield return U5WaitBuilt(c, ui, id);
            yield return c.Frames(6);
            yield return U4Docs(c, id, docs);
            c.Check(docs[0] != null && docs[0] == s.DocsEnd, "redo to the end → the documents equal the end state", docs[0] == s.DocsEnd ? "" : U4JsonDiff(s.DocsEnd, docs[0]));
            yield return U7Digest(c, id, dig);
            c.Check(U7SameDigest(s.DigEnd, dig[0], out string d2), "redo to the end → the clothes digest equals the end state's", d2);

            // ---- 15: save as, new, open
            c.Id = "UI7.15";
            yield return c.Call(new { cmd = "ui.action", action = "file.saveAs", args = new { name = "ui7_acceptance", overwrite = true } }, 120);
            string saved = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && saved != null && File.Exists(saved), "ui.action file.saveAs {name} → " + saved)) yield break;
            yield return c.Call(new { cmd = "ui.action", action = "file.new", args = new { force = true } });
            c.CheckOk("ui.action file.new {force}");
            yield return c.Call(new { cmd = "ui.action", action = "file.open", args = new { path = saved } }, 180);
            c.CheckOk("ui.action file.open {path}");
            yield return U5WaitBuilt(c, ui, id);
            if (!c.Check(ui.Runtime.FindChara(id)?.State == RealizationState.Built, "the reopened character is built (same node id)")) yield break;
            yield return c.Frames(3);
            yield return U4Docs(c, id, docs);
            c.Check(docs[0] != null && docs[0] == s.DocsEnd, "reopened: the documents equal the end state", docs[0] == s.DocsEnd ? "" : U4JsonDiff(s.DocsEnd, docs[0]));
            yield return U7Digest(c, id, dig);
            c.Check(U7SameDigest(s.DigEnd, dig[0], out string d3), "reopened: the clothes digest (materials, creators, objects" + (dig[0]?["rts"] != null ? ", RT hashes" : "") + ") equals the end state's", d3);
        }

        // ---------------------------------------------------------------- the male part (steps 16–17)

        static IEnumerator U7Male(AcceptanceCtx c, U7State s)
        {
            var ui = s.Ui;
            var e1 = new JObject[1];
            var n1 = new int[1];
            var v1 = new JObject[1];
            var ok = new bool[1];
            c.Id = "UI7.16";
            string card = Path.Combine(AppCore.Current.Paths.Hs2CharaDir(0) ?? "", Ui2MaleRel ?? "");
            yield return c.Call(new { cmd = "ui.action", action = "add.male", args = new { path = card } }, 180);
            string id = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && !string.IsNullOrEmpty(id), "ui.action add.male {path} → a new character (HERO107)", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            yield return U5WaitBuilt(c, ui, id);
            if (!c.Check(ui.Runtime.FindChara(id)?.State == RealizationState.Built, "the male character is built")) yield break;
            s.M = id;
            yield return U7OpenClothes(c, s, id, 0, ok);
            if (!ok[0]) yield break;
            var shown = new List<string>();
            for (int k = 0; k < 8; k++)
            {
                yield return U7Q(c, "clothes-kind-" + U7K(k), e1);
                if (U7Vis(e1[0])) shown.Add(U7K(k));
            }
            c.Check(shown.SequenceEqual(new[] { "top", "bottom", "gloves", "shoes" }), "four kind buttons: top, bottom, gloves, shoes (" + string.Join(", ", shown) + ")");
            var withBreak = new List<string>();
            foreach (int k in new[] { 0, 1, 4, 7 })
            {
                yield return U7OpenClothes(c, s, id, k, ok);
                if (!ok[0]) continue;
                var kr = new JObject[1];
                yield return U7Kind(c, id, U7K(k), kr);
                yield return U7Q(c, U7Field(U7K(k) + ".breakRate"), e1);
                if (U7Vis(e1[0]) || (bool?)kr[0]?["facts"]?["useBreak"] == true) withBreak.Add(U7K(k) + " " + (string)kr[0]?["item"]);
            }
            c.Check(withBreak.Count == 0, "no break row on HERO107's items", string.Join(", ", withBreak));
            yield return U7OpenClothes(c, s, id, 0, ok);
            if (ok[0])
            {
                var kr = new JObject[1];
                yield return U7Pick(c, s, id, 0, "illusion.hs2#201", "«ロングコート» (140#201)", kr);
                int o1 = (int?)kr[0]?["facts"]?["objOpt01"] ?? 0, o2 = (int?)kr[0]?["facts"]?["objOpt02"] ?? 0;
                var left = new List<string>();
                var want = new List<string>();
                if (o1 > 0) want.Add(U7Field("top.hideOptions[0]"));
                if (o2 > 0) want.Add(U7Field("top.hideOptions[1]"));
                yield return U7WaitRows(c, want.ToArray(), true, 300, left);
                c.Check(o1 > 0 && left.Count == 0, $"the option toggles shown (option parts {o1} / {o2})", string.Join(", ", left));
                c.Info($"UI7: 140#201's prefab {(string)kr[0]?["prefab"]}");
            }

            // ---- 17: the Costume tab
            c.Id = "UI7.17";
            {
                string file = U7CoordFile(0, "作業着");
                if (file == null) { c.Check(false, "the male coordinate «作業着» is in the install (UserData/coordinate/male)"); yield break; }
                var src = ui.Ops.LoadCoordinate(file, new FindingList(), out _, out var fail);
                if (!c.Check(src != null, "the card reads (the expected documents)", fail?.Message)) yield break;
                var want = U7CoordJson(src);
                yield return c.Call(new { cmd = "chara.clothes", key = id });
                var st0 = (c.J["states"] as JArray)?.DeepClone();
                yield return U7Click(c, s, "tab-costume", "the Costume tab (tab-costume)");
                yield return U7WaitShown(c, "costume-browser", true, 120, e1);
                c.Check(U7Vis(e1[0]), "the Costume tab shows the outfit-card browser (costume-browser)");
                yield return U7LoadCard(c, s, id, null, "costume-browser", "作業着", "costume-load", "«読込»");
                yield return U7Get(c, id, new[] { "coord.clothes", "coord.accessories" }, v1);
                bool cl = JToken.DeepEquals(v1[0]?["coord.clothes"], want?["clothes"]), ac = JToken.DeepEquals(v1[0]?["coord.accessories"], want?["accessories"]);
                c.Check(cl && ac, "the whole coordinate worn: clothes and accessories = the card's",
                    (cl ? "" : "clothes: " + U4JsonDiff(want?["clothes"]?.ToString(Newtonsoft.Json.Formatting.None), v1[0]?["coord.clothes"]?.ToString(Newtonsoft.Json.Formatting.None))) + (ac ? "" : " accessories differ"));
                yield return c.Call(new { cmd = "chara.clothes", key = id });
                var st1 = c.J["states"] as JArray;
                c.Check(st0 != null && JToken.DeepEquals(st0, st1), $"the clothes state kept ({U5T(st0)} → {U5T(st1)})");
                yield return U5WaitBuilt(c, ui, id);
                yield return U7Click(c, s, "tab-" + CharaEditTab.TabId, "the Edit tab");
            }
        }

        // ---------------------------------------------------------------- helpers

        static string U7K(int k) => HS2Next.UI.Core.InspectorSchemas.ClothesEditKinds[k];

        static string U7Field(string member) => "field-" + U7Clothes + member;

        static bool U7Vis(JObject e) => e != null && (bool?)e["visible"] == true;

        static bool U7En(JObject e) => e != null && (bool?)e["enabled"] == true;

        /// <summary>ui.query {name}: the first element's description (null when none).</summary>
        static IEnumerator U7Q(AcceptanceCtx c, string name, JObject[] into)
        {
            yield return c.Call(new { cmd = "ui.query", name });
            into[0] = c.Last != null && c.Last.Ok ? (c.J["elements"] as JArray)?.FirstOrDefault() as JObject : null;
        }

        /// <summary>Polls ui.query until the element's visibility is <paramref name="want"/> (every 2 frames, up to <paramref name="frames"/>).</summary>
        static IEnumerator U7WaitShown(AcceptanceCtx c, string name, bool want, int frames, JObject[] into)
        {
            for (int i = 0; i < frames; i += 2)
            {
                yield return U7Q(c, name, into);
                if (U7Vis(into[0]) == want) yield break;
                yield return c.Frames(2);
            }
        }

        /// <summary>Polls ui.query until the element is enabled (or disabled).</summary>
        static IEnumerator U7WaitEnabled(AcceptanceCtx c, string name, bool want, int frames, JObject[] into)
        {
            for (int i = 0; i < frames; i += 2)
            {
                yield return U7Q(c, name, into);
                if (into[0] != null && U7En(into[0]) == want) yield break;
                yield return c.Frames(2);
            }
        }

        /// <summary>Polls ui.query until every element of <paramref name="names"/> is shown (or hidden); <paramref name="left"/> = the others.</summary>
        static IEnumerator U7WaitRows(AcceptanceCtx c, string[] names, bool want, int frames, List<string> left)
        {
            var e = new JObject[1];
            for (int i = 0; i < frames; i += 2)
            {
                left.Clear();
                foreach (var n in names)
                {
                    yield return U7Q(c, n, e);
                    if (U7Vis(e[0]) != want) left.Add(n);
                }
                if (left.Count == 0) yield break;
                yield return c.Frames(2);
            }
        }

        /// <summary>The number of undo entries (the history command).</summary>
        static IEnumerator U7Undo(AcceptanceCtx c, int[] into)
        {
            yield return c.Call(new { cmd = "history" });
            into[0] = (c.J["undo"] as JArray)?.Count ?? -1;
        }

        /// <summary>One new history entry with <paramref name="label"/> since <paramref name="before"/> entries (the history command).</summary>
        static IEnumerator U7Entry(AcceptanceCtx c, int before, string label, string what)
        {
            yield return c.Call(new { cmd = "history" });
            var u = c.J["undo"] as JArray;
            int n = u?.Count ?? -1;
            string last = n > 0 ? (string)u[0] : "";
            c.Check(n == before + 1 && last == label, $"{what}: one history entry \"{label}\" ({n - before} entries, last \"{last}\")");
        }

        /// <summary>chara.edit.get {key, paths}: the values (null when the call failed).</summary>
        static IEnumerator U7Get(AcceptanceCtx c, string id, string[] paths, JObject[] into)
        {
            yield return c.Call(new { cmd = "chara.edit.get", key = id, paths }, 120);
            into[0] = c.Last != null && c.Last.Ok ? c.J["values"]?.DeepClone() as JObject : null;
            if (into[0] == null) c.Check(false, "chara.edit.get {key, paths: " + string.Join(", ", paths) + "}", AcceptanceCtx.Short(c.Last?.ToString()));
        }

        /// <summary>Polls chara.edit.get until <paramref name="path"/> holds <paramref name="want"/>.</summary>
        static IEnumerator U7WaitValue(AcceptanceCtx c, string id, string path, string want, int frames)
        {
            for (int i = 0; i < frames; i += 2)
            {
                yield return c.Call(new { cmd = "chara.edit.get", key = id, paths = new[] { path } });
                if (c.Last != null && c.Last.Ok && (string)c.J["values"]?[path] == want) yield break;
                yield return c.Frames(2);
            }
        }

        /// <summary>chara.edit.clothes {key, kind}: the kind's entry (null when the call failed).</summary>
        static IEnumerator U7Kind(AcceptanceCtx c, string id, string kind, JObject[] into)
        {
            yield return c.Call(new { cmd = "chara.edit.clothes", key = id, kind, served = false }, 120);
            into[0] = c.Last != null && c.Last.Ok ? (c.J["kinds"] as JArray)?.FirstOrDefault()?.DeepClone() as JObject : null;
            if (into[0] == null) c.Check(false, "chara.edit.clothes {key, kind: " + kind + "}", AcceptanceCtx.Short(c.Last?.ToString()));
        }

        /// <summary>A property of the create materials of every creator of <paramref name="which"/> (colour / detail) of a kind entry.</summary>
        static IEnumerable<JToken> U7CreatorProps(JObject kind, string which, string prop)
        {
            foreach (var cr in (kind?["creators"] as JArray) ?? new JArray())
                if ((string)cr["creator"] == which && cr["props"] is JObject p && p.ContainsKey(prop)) yield return p[prop];
        }

        static bool U7Same(JToken a, JToken b)
        {
            if (a == null || b == null) return a == b;
            if (a is JArray x && b is JArray y)
            {
                if (x.Count != y.Count) return false;
                for (int i = 0; i < x.Count; i++) if (!U7Same(x[i], y[i])) return false;
                return true;
            }
            if ((a.Type == JTokenType.Float || a.Type == JTokenType.Integer) && (b.Type == JTokenType.Float || b.Type == JTokenType.Integer))
                return Math.Abs((double)a - (double)b) < 1e-6;
            return JToken.DeepEquals(a, b);
        }

        static void U7Flush(AcceptanceCtx c, JObject info, string what)
        {
            if (info == null) return;
            c.Info($"UI7 timing ({U6Here()}): {what}'s flush {(double?)info["lastFlush"]?["ms"]:0.000} ms, mode {(string)info["lastFlush"]?["mode"]}, creators rebuilt {U5T(info["clothes"]?["lastFlush"]?["creatorsRebuilt"])}");
        }

        static IEnumerator U7Digest(AcceptanceCtx c, string id, JObject[] into)
        {
            yield return c.Call(new { cmd = "chara.edit.digest", key = id, parts = "clothes", text = true }, 120);
            into[0] = c.Last != null && c.Last.Ok ? (JObject)c.J.DeepClone() : null;
        }

        static string U7DigestLine(JObject d)
        {
            if (d == null) return "none";
            return string.Join(", ", U7DigestSections.Where(k => d[k] != null && d[k].Type != JTokenType.Null).Select(k => k + " " + U5Short(d[k].Type == JTokenType.String ? (string)d[k] : d[k].ToString(Newtonsoft.Json.Formatting.None))));
        }

        static bool U7SameDigest(JObject a, JObject b, out string detail)
        {
            detail = "";
            if (a == null || b == null) { detail = "no digest"; return false; }
            var bad = new List<string>();
            foreach (var k in U7DigestSections)
            {
                if ((a[k] == null || a[k].Type == JTokenType.Null) && (b[k] == null || b[k].Type == JTokenType.Null)) continue;
                if (JToken.DeepEquals(a[k], b[k])) continue;
                string ta = (string)a["texts"]?[k], tb = (string)b["texts"]?[k];
                bad.Add(k + (ta != null && tb != null ? " (" + U4FirstLine(ta, tb) + ")" : ""));
            }
            detail = bad.Count == 0 ? string.Join(", ", U7DigestSections.Where(k => a[k] != null && a[k].Type != JTokenType.Null)) + " equal" : "differ: " + string.Join("; ", bad);
            return bad.Count == 0;
        }

        /// <summary>The coordinate's documents in the save-format JSON form (coord.json).</summary>
        static JObject U7CoordJson(CoordContainer cc)
        {
            if (cc?.Coord == null) return null;
            try { return JObject.Parse(Encoding.UTF8.GetString(CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, cc.Coord.Write))); }
            catch (Exception) { return null; }
        }

        /// <summary>An HS2 coordinate card of the install (UserData/coordinate/&lt;sex&gt;/&lt;name&gt;.png), or null.</summary>
        static string U7CoordFile(int sex, string name)
        {
            string ud = AppCore.Current?.Paths?.Hs2UserData;
            if (ud == null) return null;
            string p = Path.Combine(ud, "coordinate", sex == 0 ? "male" : "female", name + ".png");
            return File.Exists(p) ? p : null;
        }

        /// <summary>The shown element of that name in the app's UI (under <paramref name="scope"/>), or null.</summary>
        static VisualElement U7El(U7State s, string name, VisualElement scope = null)
        {
            var root = scope ?? s.Host?.Root;
            return root?.Query<VisualElement>(name).Where(x => x.panel != null && x.worldBound.width > 0 && U6Shown(x)).First();
        }

        static IEnumerator U7Click(AcceptanceCtx c, U7State s, string name, string what, VisualElement scope = null)
        {
            yield return U4Click(c, s.S4, U7El(s, name, scope), what);
        }

        /// <summary>
        /// Selects the character, shows the inspector's Edit tab and the Clothes node by clicks, then kind <paramref name="kind"/>
        /// (clothes-kind-&lt;kind&gt;) unless its group is shown already; the result through ui.query (the group shown).
        /// </summary>
        static IEnumerator U7OpenClothes(AcceptanceCtx c, U7State s, string id, int kind, bool[] ok)
        {
            ok[0] = false;
            var e = new JObject[1];
            yield return c.Call(new { cmd = "ui.state" });
            if ((string)c.J["selection"]?["primary"] != id) { yield return c.Call(new { cmd = "select", key = id }); yield return c.Frames(3); }
            yield return c.Call(new { cmd = "ui.panel", panel = "inspector", op = "focus" });
            yield return c.Frames(3);
            yield return U7Q(c, "edit-tree-clothes", e);
            if (!U7Vis(e[0]))
            {
                yield return U7Click(c, s, "tab-" + CharaEditTab.TabId, "the Edit tab");
                yield return U7WaitShown(c, "edit-tree-clothes", true, 60, e);
            }
            if (!c.Check(U7Vis(e[0]), "the Edit tab of " + id + " is shown (edit-tree-clothes)")) yield break;
            yield return U7Q(c, "clothes-kinds", e);
            if (!U7Vis(e[0]))
            {
                yield return U7Click(c, s, "edit-tree-clothes", "the Clothes node");
                yield return U7WaitShown(c, "clothes-kinds", true, 60, e);
            }
            if (!c.Check(U7Vis(e[0]), "Edit › Clothes is open (clothes-kinds)")) yield break;
            string group = "clothes-kind-group-" + U7K(kind);
            yield return U7Q(c, group, e);
            if (!U7Vis(e[0]))
            {
                yield return U7Click(c, s, "clothes-kind-" + U7K(kind), "the kind button clothes-kind-" + U7K(kind));
                yield return U7WaitShown(c, group, true, 60, e);
            }
            ok[0] = c.Check(U7Vis(e[0]), "Clothes › " + U7K(kind) + " is shown (" + group + ")");
        }

        /// <summary>The queued thumbnails finish before an item swap (the 4.2 W-1 rule of thumb).</summary>
        static IEnumerator U7Settle(AcceptanceCtx c, U7State s)
        {
            var pt = HS2Next.UI.Panels.Maker.PartThumbnails.Shared(s.Ui.App?.Catalog);
            for (int i = 0; i < 1800 && pt != null && pt.Pending > 0; i++) yield return c.Frames(1);
            yield return c.Frames(2);
        }

        /// <summary>
        /// Picks <paramref name="reference"/> in kind k's item list → one entry "Change item"; waits until the document holds it
        /// and the realization wears it (chara.edit.clothes), then gives the kind's entry.
        /// </summary>
        static IEnumerator U7Pick(AcceptanceCtx c, U7State s, string id, int k, string reference, string what, JObject[] kind)
        {
            kind[0] = null;
            yield return U7Settle(c, s);
            var list = U7El(s, U7Field(U7K(k) + ".item")) as MakerThumbList;
            if (!c.Check(list != null, $"the {U7K(k)} item list ({U7Field(U7K(k) + ".item")})")) yield break;
            var n = new int[1];
            yield return U7Undo(c, n);
            yield return U4ClickCell(c, s.S4, list, reference, what);
            yield return U7WaitValue(c, id, "coord.clothes." + U7K(k) + ".item", reference, 300);
            var kr = new JObject[1];
            for (int i = 0; i < 150; i += 2)
            {
                yield return c.Call(new { cmd = "chara.edit.clothes", key = id, kind = U7K(k), served = false });
                kr[0] = c.Last != null && c.Last.Ok ? (c.J["kinds"] as JArray)?.FirstOrDefault()?.DeepClone() as JObject : null;
                if ((string)kr[0]?["item"] == reference) break;
                yield return c.Frames(2);
            }
            yield return c.Frames(6);   // the live gates re-evaluate after the realizing flush
            yield return U7Entry(c, n[0], HS2Next.UI.Core.OpLabels.ChangeItem, what + " pick");
            c.Check((string)kr[0]?["item"] == reference, $"{U7K(k)}.item = {reference}, worn: prefab {(string)kr[0]?["prefab"]}");
            yield return c.Call(new { cmd = "chara.edit.info", key = id });
            U7Flush(c, c.J, what);
            yield return U7Kind(c, id, U7K(k), kind);
        }

        /// <summary>Types <paramref name="text"/> into the number field of a slider row → one entry <paramref name="label"/>.</summary>
        static IEnumerator U7TypeRow(AcceptanceCtx c, U7State s, string id, string member, string text, string label, string what)
        {
            var row = U7El(s, U7Field(member));
            if (!c.Check(row != null, what + " row (" + U7Field(member) + ")")) yield break;
            var n = new int[1];
            yield return U7Undo(c, n);
            yield return U4Type(c, s.S4, row.Q("field"), text, what + "'s number field");
            yield return c.Frames(3);
            yield return U7Entry(c, n[0], label, "typed " + text + " into " + what);
        }

        /// <summary>A click on a toggle row's box → one entry "Edit clothes".</summary>
        static IEnumerator U7Toggle(AcceptanceCtx c, U7State s, string id, string member, string what)
        {
            var tg = U7El(s, U7Field(member))?.Q<Toggle>("toggle");
            var n = new int[1];
            yield return U7Undo(c, n);
            yield return U4Click(c, s.S4, tg?.Q("unity-checkmark") ?? tg, what);
            yield return c.Frames(3);
            yield return U7Entry(c, n[0], U7EditClothes, what);
        }

        /// <summary>
        /// A colour row's picker: a drag in the SV square over 3 frames, Enter → one entry "Edit clothes", the colour changed and
        /// the alpha kept; <paramref name="wantAlpha"/> = whether the picker must offer the alpha strip (ui.query).
        /// </summary>
        static IEnumerator U7ColourDrag(AcceptanceCtx c, U7State s, string id, string member, string what, bool wantAlpha)
        {
            var row = U7El(s, U7Field(member));
            if (!c.Check(row != null, what + " row")) yield break;
            var v = new JObject[1];
            yield return U7Get(c, id, new[] { "coord.clothes." + member }, v);
            var c0 = v[0]?["coord.clothes." + member] as JArray;
            var n = new int[1];
            yield return U7Undo(c, n);
            yield return U4Click(c, s.S4, row.Q("swatch"), what + " swatch");
            yield return c.Frames(4);
            var strip = new bool[1];
            yield return U7AlphaStrip(c, strip);
            c.Check(strip[0] == wantAlpha, $"{what}: the picker {(wantAlpha ? "offers" : "has no")} alpha strip (ui.query)");
            var picker = s.Host.Root.Q("color-picker");
            var sv = picker?.Q("sv");
            if (!c.Check(sv != null && sv.worldBound.width > 0, what + ": the picker's SV square")) yield break;
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
            c.CheckOk("ui.input drag in the picker of " + what);
            yield return c.Call(new { cmd = "ui.input", frames = 3, events = new object[] { new { type = "key", key = "Return" } } });
            c.CheckOk("ui.input Enter in the picker of " + what);
            yield return c.Frames(3);
            var e = new JObject[1];
            yield return U7Q(c, "color-picker", e);
            c.Check(!U7Vis(e[0]), what + ": Enter closed the picker (ui.query color-picker)");
            yield return U7Entry(c, n[0], U7EditClothes, what + " picker session");
            yield return U7Get(c, id, new[] { "coord.clothes." + member }, v);
            var c1 = v[0]?["coord.clothes." + member] as JArray;
            c.Check(c0 != null && c1 != null && c0.Count == 4 && c1.Count == 4 && !(U7Same(c0[0], c1[0]) && U7Same(c0[1], c1[1]) && U7Same(c0[2], c1[2])) && (double)c1[3] == (double)c0[3],
                $"{what}: the stored colour changed, alpha kept ({U5T(c0)} → {U5T(c1)})");
            yield return c.Call(new { cmd = "chara.edit.info", key = id });
            U7Flush(c, c.J, what);
        }

        /// <summary>Whether the open colour picker shows an alpha strip (ui.query: an element "alpha" inside the picker's bounds).</summary>
        static IEnumerator U7AlphaStrip(AcceptanceCtx c, bool[] into)
        {
            into[0] = false;
            var e = new JObject[1];
            yield return U7Q(c, "color-picker", e);
            if (!U7Vis(e[0])) yield break;
            var pb = e[0]["worldBound"] as JArray;
            yield return c.Call(new { cmd = "ui.query", name = "alpha", all = true });
            foreach (var a in (c.J["elements"] as JArray) ?? new JArray())
            {
                var ab = a["worldBound"] as JArray;
                if ((bool?)a["visible"] != true || ab == null || pb == null) continue;
                double ax = (double)ab[0] + (double)ab[2] / 2, ay = (double)ab[1] + (double)ab[3] / 2;
                if (ax >= (double)pb[0] && ax <= (double)pb[0] + (double)pb[2] && ay >= (double)pb[1] && ay <= (double)pb[1] + (double)pb[3]) { into[0] = true; yield break; }
            }
        }

        /// <summary>
        /// Opens a colour row's picker and reports its alpha strip; with <paramref name="pickHalf"/> clicks the strip's middle
        /// (alpha 0.5; Enter commits: one entry "Edit clothes"), without it closes the picker with Escape (no entry).
        /// </summary>
        static IEnumerator U7PickerAlpha(AcceptanceCtx c, U7State s, string member, bool[] strip, bool pickHalf)
        {
            strip[0] = false;
            var row = U7El(s, U7Field(member));
            if (!c.Check(row != null, member + " row")) yield break;
            var n = new int[1];
            yield return U7Undo(c, n);
            yield return U4Click(c, s.S4, row.Q("swatch"), member + " swatch");
            yield return c.Frames(4);
            yield return U7AlphaStrip(c, strip);
            var picker = s.Host.Root.Q("color-picker");
            if (pickHalf && strip[0])
            {
                // a click at the middle of the alpha strip (top = 1, bottom = 0) picks alpha 0.5; Enter commits
                var st = picker?.Q("alpha");
                var p = new Vector2[1];
                if (c.Check(st != null && st.worldBound.height > 0, "the picker's alpha strip"))
                {
                    yield return U4Reveal(c, s.S4, st, "the picker's alpha strip", p);
                    var b = st.worldBound;
                    var q = new Vector2(b.center.x, b.yMin + b.height * 0.5f);
                    yield return c.Call(new { cmd = "ui.input", frames = 3, events = new object[] { new { type = "pointer", phase = "down", x = q.x, y = q.y, button = 0 }, new { type = "pointer", phase = "up", x = q.x, y = q.y, button = 0 } } });
                    c.CheckOk("ui.input click at the middle of the alpha strip");
                }
                yield return c.Call(new { cmd = "ui.input", frames = 3, events = new object[] { new { type = "key", key = "Return" } } });
                yield return c.Frames(3);
                var e = new JObject[1];
                yield return U7Q(c, "color-picker", e);
                c.Check(!U7Vis(e[0]), "Enter closed the picker (ui.query color-picker)");
                yield return U7Entry(c, n[0], U7EditClothes, "the alpha picked on the strip");
            }
            else
            {
                yield return c.Call(new { cmd = "ui.input", frames = 3, events = new object[] { new { type = "key", key = "Escape" } } });
                yield return c.Frames(3);
                var e = new JObject[1];
                yield return U7Q(c, "color-picker", e);
                if (U7Vis(e[0])) { var cancel = U7El(s, "cancel", s.Host.Root.Q("color-picker")); yield return U4Click(c, s.S4, cancel, "the picker's cancel"); yield return c.Frames(3); }
                yield return U7Undo(c, n);
            }
        }

        /// <summary>Clicks a text field, selects all and types <paramref name="text"/> (Backspace for an empty text); no Enter (a
        /// dialog's default button must not fire).</summary>
        static IEnumerator U7TypeText(AcceptanceCtx c, U7State s, VisualElement field, string text, string what)
        {
            if (!c.Check(field != null, what + " exists")) yield break;
            var p = new Vector2[1];
            yield return U4Reveal(c, s.S4, field.Q("unity-text-input") ?? field, what, p);
            var ev = new List<object>
            {
                new { type = "pointer", phase = "click", x = p[0].x, y = p[0].y, button = 0 },
                new { type = "key", key = "A", mods = new[] { "ctrl" } },
            };
            if (string.IsNullOrEmpty(text)) ev.Add(new { type = "key", key = "Backspace" });
            else ev.Add(new { type = "text", text });
            yield return c.Call(new { cmd = "ui.input", frames = 3, events = ev.ToArray() });
            c.CheckOk("ui.input: \"" + text + "\" typed into " + what);
            yield return c.Frames(3);
        }

        /// <summary>
        /// Loads an outfit card through a coordinate browser: <paramref name="opener"/> clicked (null: the browser is shown), the
        /// card searched by name in the browser's search field, its cell clicked, then <paramref name="button"/> → one entry
        /// "Load coordinate"; a dialog closes (ui.state modal).
        /// </summary>
        static IEnumerator U7LoadCard(AcceptanceCtx c, U7State s, string id, string opener, string browser, string cardName, string button, string what)
        {
            var e = new JObject[1];
            if (opener != null)
            {
                yield return U7Click(c, s, opener, "the outfit-card button (" + opener + ")");
                yield return U7WaitShown(c, browser, true, 120, e);
                yield return c.Call(new { cmd = "ui.state" });
                c.Check(U7Vis(e[0]) && (string)c.J["modal"] == CoordinateDialog.DialogId, $"the coordinate dialog is open (ui.state modal {(string)c.J["modal"]})");
            }
            var br = U7El(s, browser);
            if (!c.Check(br != null, "the outfit-card browser (" + browser + ")")) yield break;
            // the scan of the folder finishes, then the card is searched by name
            string cellName = "coord-cell-" + cardName;   // the file name without its extension
            yield return U7TypeText(c, s, br.Q<TextField>("coord-search"), cardName, "the browser's search field");
            VisualElement cell = null;
            for (int i = 0; i < 600 && cell == null; i++) { yield return c.Frames(1); cell = U7El(s, cellName, br); }
            if (c.Check(cell != null, $"the card's cell {cellName} is shown"))
            {
                yield return U4Click(c, s.S4, cell, "the card " + cardName);
                yield return c.Frames(2);
                var n = new int[1];
                yield return U7Undo(c, n);
                yield return U7Click(c, s, button, what + " (" + button + ")");
                for (int i = 0; i < 60; i++) yield return c.Frames(1);
                yield return U7Entry(c, n[0], U7LoadCoordinate, what + " of " + cardName);
            }
            yield return c.Call(new { cmd = "ui.state" });
            bool left = (string)c.J["modal"] == CoordinateDialog.DialogId;
            c.Check(!left, $"no coordinate dialog left open (modal {(string)c.J["modal"] ?? "none"})");
            if (left)
            {
                // the dialog would cover every later click: cancelled
                yield return U7Click(c, s, "dialog-button-cancel", "the coordinate dialog's cancel");
                yield return c.Frames(3);
            }
            if (cell == null) yield break;
            yield return c.Call(new { cmd = "chara.edit.info", key = id });
            U7Flush(c, c.J, what);
            // the browser's search is cleared again (the field keeps its text per browser)
            var br2 = U7El(s, browser);
            if (br2 != null) yield return U7TypeText(c, s, br2.Q<TextField>("coord-search"), "", "the browser's search field (cleared)");
        }
    }
}
