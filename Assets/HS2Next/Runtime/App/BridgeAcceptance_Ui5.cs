// UI5 of the bridge acceptance (Phase 4.2 integrator T): the body pages of the
// Maker's Edit tab driven through the bridge's ui.* commands in Editor play (BridgeTest.RunPlayUi after UI4, or
// BridgeTest.RunPlayUi5 alone) and in the app player (-bridgeAccept with -ui). Without a running UI the case is skipped
// with a warning (ui.state enabled false). chara.edit.get, .digest {parts: body}, .bust, .materials, .rtstats {part: body},
// .info, .list and bones.dump are the read-only checks. Numbers only; nothing is captured.
//   1 file.new, add.female (BR-Chan), Edit › Body (clicks on tab-edit, edit-tree-body): the Shape page is open (36 rows,
//     8 page buttons); the documents and the body digest after it are the "step 1" state.
//   2 Shape › Whole: a drag of «身長» (3 frames) → one entry "Edit body", body.shape[0] changed inside 0..1, cf_N_height
//     (bones.dump) and the digest's bones changed, the commit flush ran the deferred units (none left, mode normal).
//   3 150 typed into «頭サイズ» → stored 1.5; the bones changed; a duplicate (a fresh build of the same documents) has the
//     same body digest bones / normals / bust; the duplicate undone.
//   4 Breast: a drag of «胸の柔らかさ» → one entry; chara.edit.bust damping / elasticity / stiffness / gravity = the bust formulas with
//     the new softness. 30 typed into «胸サイズ» → stored 0.3; normals.rate 0.6, applied, sha256 changed; the chains follow b.
//   5 Skin: the second row of the skin list; the «タン» chip (push-colour:336-illusion.hs2#3) → body.skin.color =
//     HSVToRGB(Base) of row 3 with alpha 1, one entry; the colour row's picker (SV drag, Enter) → one entry, alpha kept;
//     80 typed into «ツヤ» → body and face _Gloss = lerp(0, 0.8, 0.8) + 0.2 × the gloss state.
//   6 Tan: sunburn row 1; its colour's alpha through the picker's alpha strip → one entry each.
//   7 Nipples: row 1, colour; Underhair: row 1; Nails: colour, 50 typed into «ツヤ» — documents and material values.
//   8 Paint 1: a paint row; region «背中・うなじ» (illusion.hs2#2) → layoutItem, layout and rotation kept, the body colour
//     creator's _Texture5UV = the layout's values for region 2; a drag of the width component (8 frames) → one entry, the throttle judged
//     W 2 and made frames wait; 25 typed into «回転».
//   9 the paint list searched for a word of a row name, cleared with Backspace (ui.input key ×n): every row and the current
//     mark come back.
//  10 Special: the female-only toggle profile.futanari → one entry "Edit body"; undo restores it and its row; redo.
//  11 ↺ on «ツヤ» (skin gloss) → the value of step 1 (the reference, one entry "Reset value").
//  12 edit.undo back to step 1 (documents byte-identical, body digest equal incl. normals and bust), edit.redo to the end.
//  13 file.saveAs → file.new → file.open: documents and body digest equal to the end state; the rows show 150, 30, 25.
//  14 add.male (HERO107): Body › Shape shows 29 rows (28 + the height row while MaleHeightPin075 is off) and none of the
//     female-only rows; no Special page; the male skin list = MakerLists rows of 131, its second skin picked; 80 typed into
//     «身長» → cf_N_height; chara.edit.bust: no normals, physics per BustOptions.MaleBustPhysics (on: the chain values of
//     the male card with the female formula).
// (The spec's steps 10–13 are 11–14 here: the futanari step of the user's Y9 decision is step 10.)
// Every step checks the history entry count and label and the document values; no chara.editFailed may be logged; the
// typed values' flush times are reported.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        const string U5Body = "appearance.chara.body.";
        const string U5Futanari = "appearance.chara.profile.futanari";
        const string U5EditBody = HS2Next.UI.Core.OpLabels.EditBody;

        /// <summary>The Body pages in HS2's order (InspectorSchemas.BodyPages; Special = the futanari toggle, female only).</summary>
        static readonly string[] U5Pages = { "shape", "skin", "sunburn", "nipple", "underhair", "nail", "paint", "special" };

        /// <summary>The female-only rows of the Shape page (gate chara.female).</summary>
        static readonly string[] U5FemaleOnly =
        {
            U5Body + "shape[5]", U5Body + "shape[6]", U5Body + "bust.softness", U5Body + "bust.weight", U5Body + "shape[7]",
            U5Body + "shape[8]", U5Body + "shape[32]",
        };

        sealed class U5State
        {
            public U4State S4;
            public UiContext Ui => S4.Ui;
            public string F, M;
            public int U1;
            public string Docs1, DocsEnd;
            public JObject Dig1, DigEnd;
            public float Gloss1;
            public int EditFailed;
        }

        public static IEnumerator Ui5(AcceptanceCtx c)
        {
            c.Id = "UI5";
            yield return c.Call(new { cmd = "ui.state" });
            if (c.Last == null || !c.Last.Ok || (bool?)c.J["enabled"] != true)
            {
                c.Warn("UI5 skipped: the Studio UI is not running (start with -ui; " + AcceptanceCtx.Short(c.Last?.ToString()) + ")");
                yield break;
            }
            var s = new U5State { S4 = new U4State { Host = UiHost.Current } };
            s.S4.Ui = s.S4.Host?.Context;
            if (!c.Check(s.Ui != null, "UiHost in the app's process")) yield break;
            Application.LogCallback onLog = (m, st, t) => { if (m != null && m.Contains("chara.editFailed")) s.EditFailed++; };
            Application.logMessageReceived += onLog;
            var total = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                yield return U5Female(c, s);
                c.Info($"UI5: female part {total.Elapsed.TotalSeconds:F1} s");
                if (s.F != null)
                {
                    total.Restart();
                    yield return U5Male(c, s);
                    c.Info($"UI5: male part {total.Elapsed.TotalSeconds:F1} s");
                }
            }
            finally { Application.logMessageReceived -= onLog; }
            c.Id = "UI5";
            c.Check(s.EditFailed == 0, $"no chara.editFailed logged during UI5 ({s.EditFailed})");
        }

        // ---------------------------------------------------------------- the female part (steps 1–13)

        static IEnumerator U5Female(AcceptanceCtx c, U5State s)
        {
            var ui = s.Ui;
            var s4 = s.S4;
            string Here() => Application.isEditor ? "Editor" : "player";

            // ---- 1: a new scene with BR-Chan, Edit › Body (the Shape page)
            c.Id = "UI5.1";
            yield return c.Call(new { cmd = "ui.action", action = "file.new", args = new { force = true } });
            c.CheckOk("ui.action file.new {force}");
            string card = Path.Combine(AppCore.Current.Paths.Hs2CharaDir(1) ?? "", c.FemaleRel ?? "");
            yield return c.Call(new { cmd = "ui.action", action = "add.female", args = new { path = card } }, 180);
            string id = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && !string.IsNullOrEmpty(id), "ui.action add.female {path} → a new character", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            yield return U5WaitBuilt(c, ui, id);
            if (!c.Check(ui.Runtime.FindChara(id)?.State == HS2Next.Scenes.Runtime.RealizationState.Built, "the character is built")) yield break;
            if (!c.Check(ui.Model.Find(id)?.Appearance?.Stored != null, "the character has stored documents (imported on add)")) yield break;
            var tab = new CharaEditTab[1];
            yield return U5OpenPage(c, s, id, null, tab);
            if (tab[0] == null) yield break;
            c.Check(tab[0].CurrentPage == "shape", $"Edit › Body opens on the Shape page ({tab[0].CurrentPage})");
            var shown = U5Pages.Where(p => tab[0].Q<Button>("edit-page-body-" + p) is Button b && b.style.display != DisplayStyle.None).ToList();
            c.Check(shown.SequenceEqual(U5Pages), $"female: the 8 Body page buttons in HS2's order ({string.Join(", ", shown)})");
            int shapeRows = U5Rows(tab[0], "shape").Count;
            c.Check(shapeRows == 36, $"female: the Shape page shows 36 rows ({shapeRows})");
            c.Check(ui.Session.MakerReferences.Has(id), "the Edit tab captured the reference documents of the character (this scene session's store)");
            s.F = id;
            s.U1 = U4Undo(ui);
            var docs = new string[1];
            yield return U4Docs(c, id, docs);
            s.Docs1 = docs[0];
            var dig = new JObject[1];
            yield return U5Digest(c, id, dig);
            s.Dig1 = dig[0];
            if (!c.Check(s.Docs1 != null && s.Dig1 != null, "chara.edit.get and chara.edit.digest {parts: body} of step 1", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            c.Info($"UI5: body digest of step 1 {U5DigestLine(s.Dig1)}");
            s.Gloss1 = Ui3Value(ui, id, U5Body + "skin.gloss");

            // ---- 2: Shape › Whole, a drag of the height
            c.Id = "UI5.2";
            var hRow = U4Slider(tab[0], U5Body + "shape[0]", -1);
            if (c.Check(hRow != null, "the height row (field-…body.shape[0])"))
            {
                float h0 = Ui3Value(ui, id, U5Body + "shape[0]");
                var nh = new JObject[1];
                yield return U5Bone(c, id, "cf_N_height", nh);
                int u = U4Undo(ui);
                yield return U4DragKnob(c, s4, hRow, h0, "the height slider");
                U4Entry(c, ui, u, U5EditBody, "the height drag");
                float h1 = Ui3Value(ui, id, U5Body + "shape[0]");
                c.Check(h1 != h0 && h1 >= 0 && h1 <= 1, $"body.shape[0] moved inside 0..1 ({h0} → {h1})");
                var nh1 = new JObject[1];
                yield return U5Bone(c, id, "cf_N_height", nh1);
                c.Check(nh[0] != null && nh1[0] != null && !JToken.DeepEquals(nh[0]["ls"], nh1[0]["ls"]),
                    $"cf_N_height (bones.dump) changed: scale {U5T(nh[0]?["ls"])} → {U5T(nh1[0]?["ls"])}");
                yield return U5Digest(c, id, dig);
                c.Check(dig[0] != null && (string)dig[0]["bones"] != (string)s.Dig1["bones"], "the body digest's bones changed");
                yield return c.Call(new { cmd = "chara.edit.info", key = id });
                var deferred = c.J["deferred"] as JArray;
                c.Check(c.Last != null && c.Last.Ok && (deferred == null || deferred.Count == 0) && (string)c.J["lastFlush"]?["mode"] != "preview",
                    $"the commit flush ran the deferred units (deferred {AcceptanceCtx.Short(deferred?.ToString(Newtonsoft.Json.Formatting.None))}, last flush {(string)c.J["lastFlush"]?["mode"]}, units {AcceptanceCtx.Short(c.J["lastFlush"]?["units"]?.ToString(Newtonsoft.Json.Formatting.None))})");
            }

            // ---- 3: 150 typed into the head size, a fresh build of the documents
            c.Id = "UI5.3";
            var head = U4Slider(tab[0], U5Body + "shape[9]", -1);
            if (c.Check(head != null, "the head size row (field-…body.shape[9])"))
            {
                yield return U5Digest(c, id, dig);
                string bones0 = (string)dig[0]?["bones"];
                int u = U4Undo(ui);
                yield return U4Type(c, s4, head.Control.Field, "150", "the head size number field");
                yield return c.Frames(3);
                float v = Ui3Value(ui, id, U5Body + "shape[9]");
                c.Check(v == 1.5f, $"typed 150 → stored {v} (1.5: typed −100..200, stored unclamped)");
                U4Entry(c, ui, u, U5EditBody, "the typed head size");
                c.Check(Math.Abs(head.Control.Value - 150) < 1e-6, $"the row shows {head.Control.Value} (150)");
                yield return U5FlushInfo(c, id, "the typed head size");
                yield return U5Digest(c, id, dig);
                var digTyped = dig[0];
                c.Check(digTyped != null && (string)digTyped["bones"] != bones0, "the body digest's bones changed (the head size extrapolated past 1)");
                yield return U5FreshBuild(c, s, id, digTyped);
                yield return U5OpenPage(c, s, id, "shape", tab);
                if (tab[0] == null) yield break;
            }

            // ---- 4: Breast: softness drag, bust size typed
            c.Id = "UI5.4";
            var soft = U4Slider(tab[0], U5Body + "bust.softness", -1);
            if (c.Check(soft != null, "the bust softness row"))
            {
                float s0 = Ui3Value(ui, id, U5Body + "bust.softness");
                int u = U4Undo(ui);
                yield return U4DragKnob(c, s4, soft, s0, "the bust softness");
                U4Entry(c, ui, u, U5EditBody, "the softness drag");
                float s1 = Ui3Value(ui, id, U5Body + "bust.softness");
                c.Check(s1 != s0 && s1 >= 0 && s1 <= 1, $"body.bust.softness moved inside 0..1 ({s0} → {s1})");
                yield return U5BustCheck(c, ui, id, "after the softness drag");
            }
            var bust = new JObject[1];
            yield return U5Bust(c, id, bust);
            string sha0 = (string)bust[0]?["normals"]?["sha256"];
            var size = U4Slider(tab[0], U5Body + "shape[1]", -1);
            if (c.Check(size != null, "the bust size row (field-…body.shape[1])"))
            {
                int u = U4Undo(ui);
                yield return U4Type(c, s4, size.Control.Field, "30", "the bust size number field");
                yield return c.Frames(3);
                float b = Ui3Value(ui, id, U5Body + "shape[1]");
                c.Check(b == 0.3f, $"typed 30 → stored {b} (0.3)");
                U4Entry(c, ui, u, U5EditBody, "the typed bust size");
                yield return U5FlushInfo(c, id, "the typed bust size");
                yield return U5Bust(c, id, bust);
                var nm = bust[0]?["normals"];
                double? rate = (double?)nm?["rate"];
                c.Check(rate != null && Math.Abs(rate.Value - 0.6) < 1e-6 && (bool?)nm?["applied"] == true && (string)nm?["sha256"] != null && (string)nm?["sha256"] != sha0,
                    $"chara.edit.bust normals: rate {rate} (0.6), applied {(bool?)nm?["applied"]}, sha256 {U5Short((string)nm?["sha256"])} (was {U5Short(sha0)}), source {AcceptanceCtx.Short(nm?["source"]?.ToString(Newtonsoft.Json.Formatting.None))}");
                yield return U5BustCheck(c, ui, id, "after the bust size 0.3");
            }

            // ---- 5: Skin: a skin row, the «タン» chip, the colour picker, the gloss typed
            c.Id = "UI5.5";
            yield return U5OpenPage(c, s, id, "skin", tab);
            if (tab[0] == null) yield break;
            yield return U5PickRow(c, s, U4List(tab[0], U5Body + "skin.item"), id, U5Body + "skin.item", 1, "the skin list");
            var chips = tab[0].Query<PushGrid>().Where(p => p.IsColour && p.ColourCategory == 336).First();
            if (c.Check(chips != null && chips.AllItems.Count == 7, $"the skin swatch grid push-colour:336 with 7 chips ({chips?.AllItems.Count})"))
            {
                string chip = "illusion.hs2#3";
                Color4 want = default;
                bool known = MakerServices.Rows != null && MakerServices.Rows.ColourPreset(336, chip, out want, out _);
                var unity = Color.HSVToRGB(0.058f, 0.21f, 0.7f);
                c.Check(known && Math.Abs(want.R - unity.r) < 1e-6 && Math.Abs(want.G - unity.g) < 1e-6 && Math.Abs(want.B - unity.b) < 1e-6 && want.A == 1f,
                    $"row 3 «タン» of 336 = HSVToRGB(0.058, 0.21, 0.7) with alpha 1 ({want}; Unity {unity})");
                var cur = U4Vec(ui, id, U5Body + "skin.color");
                if (cur != null && cur.Length == 4 && (float)cur[0] == want.R && (float)cur[1] == want.G && (float)cur[2] == want.B)
                {
                    c.Info("UI5: BR-Chan's skin colour already is «タン»; the «ピンク» chip is pushed instead");
                    chip = "illusion.hs2#2";
                    MakerServices.Rows.ColourPreset(336, chip, out want, out _);
                }
                int u = U4Undo(ui);
                yield return U4ClickCell(c, s4, chips, chip, "the chip " + chip);
                yield return c.Frames(3);
                U4Entry(c, ui, u, U5EditBody, "the swatch push");
                var got = U4Vec(ui, id, U5Body + "skin.color");
                c.Check(got != null && got.Length == 4 && (float)got[0] == want.R && (float)got[1] == want.G && (float)got[2] == want.B && got[3] == 1,
                    $"body.skin.color = HSVToRGB(Base) of {chip} with alpha 1 ({U4Num(got)}; {want})");
            }
            yield return U5ColourDrag(c, s, tab[0], id, U5Body + "skin.color", "the skin colour", false);
            var gloss = U4Slider(tab[0], U5Body + "skin.gloss", -1);
            if (c.Check(gloss != null, "the skin gloss row"))
            {
                int u = U4Undo(ui);
                yield return U4Type(c, s4, gloss.Control.Field, "80", "the skin gloss number field");
                yield return c.Frames(3);
                float g = Ui3Value(ui, id, U5Body + "skin.gloss");
                c.Check(g == 0.8f, $"typed 80 → stored {g} (0.8)");
                U4Entry(c, ui, u, U5EditBody, "the typed skin gloss");
                yield return U5FlushInfo(c, id, "the typed skin gloss");
                float state = U5GlossState(c, ui, id);
                float want = Mathf.Lerp(0f, 0.8f, 0.8f) + 0.2f * state;
                var mb = new JToken[1];
                yield return U5MatProp(c, id, "body", "_Gloss", mb);
                var mf = new JToken[1];
                yield return U5MatProp(c, id, "face", "_Gloss", mf);
                c.Check(mb[0] != null && mf[0] != null && Math.Abs((double)mb[0] - want) < 1e-5 && Math.Abs((double)mf[0] - want) < 1e-5,
                    $"body and face _Gloss = lerp(0, 0.8, 0.8) + 0.2 × gloss state {state} = {want} (body {U5T(mb[0])}, face {U5T(mf[0])})");
            }

            // ---- 6: Tan: a sunburn row, its colour's alpha
            c.Id = "UI5.6";
            yield return U5OpenPage(c, s, id, "sunburn", tab);
            if (tab[0] == null) yield break;
            yield return U5PickRow(c, s, U4List(tab[0], U5Body + "sunburn.item"), id, U5Body + "sunburn.item", 1, "the tan list");
            yield return U5ColourDrag(c, s, tab[0], id, U5Body + "sunburn.color", "the tan colour (alpha strip)", true);

            // ---- 7: Nipples, Underhair, Nails
            c.Id = "UI5.7";
            yield return U5OpenPage(c, s, id, "nipple", tab);
            if (tab[0] == null) yield break;
            yield return U5PickRow(c, s, U4List(tab[0], U5Body + "nipple.item"), id, U5Body + "nipple.item", 1, "the nipple list");
            yield return U5ColourDrag(c, s, tab[0], id, U5Body + "nipple.color", "the nipple colour", false);
            yield return U5OpenPage(c, s, id, "underhair", tab);
            if (tab[0] == null) yield break;
            yield return U5PickRow(c, s, U4List(tab[0], U5Body + "underhair.item"), id, U5Body + "underhair.item", 1, "the underhair list");
            yield return U5OpenPage(c, s, id, "nail", tab);
            if (tab[0] == null) yield break;
            yield return U5ColourDrag(c, s, tab[0], id, U5Body + "nail.color", "the nail colour", false);
            var ngloss = U4Slider(tab[0], U5Body + "nail.gloss", -1);
            if (c.Check(ngloss != null, "the nail gloss row"))
            {
                int u = U4Undo(ui);
                yield return U4Type(c, s4, ngloss.Control.Field, "50", "the nail gloss number field");
                yield return c.Frames(3);
                float g = Ui3Value(ui, id, U5Body + "nail.gloss");
                c.Check(g == 0.5f, $"typed 50 → stored {g} (0.5)");
                U4Entry(c, ui, u, U5EditBody, "the typed nail gloss");
                yield return U5FlushInfo(c, id, "the typed nail gloss");
            }
            yield return c.Call(new { cmd = "chara.edit.materials", key = id, part = "body", props = new[] { "_Color2", "_Texture2", "_Texture3", "_Color13", "_NailGloss" } });
            var props = U5BodyProps(c.J);
            if (c.Check(c.Last != null && c.Last.Ok && props != null, "chara.edit.materials {part: body} of the body draw"))
            {
                var nipC = U4Vec(ui, id, U5Body + "nipple.color");
                var nailC = U4Vec(ui, id, U5Body + "nail.color");
                c.Check(U5Same(props["_Color2"], nipC), $"_Color2 = body.nipple.color ({U5T(props["_Color2"])}; {U4Num(nipC)})");
                c.Check(U5Same(props["_Color13"], nailC), $"_Color13 = body.nail.color ({U5T(props["_Color13"])}; {U4Num(nailC)})");
                c.Check(props["_NailGloss"] != null && Math.Abs((double)props["_NailGloss"] - 0.5) < 1e-6, $"_NailGloss = 0.5 ({U5T(props["_NailGloss"])})");
                c.Check(props["_Texture2"] is JObject, $"_Texture2 holds the nipple row's texture ({U5T(props["_Texture2"])})");
                c.Info($"UI5: _Texture3 (underhair row) {U5T(props["_Texture3"])}");
            }

            // ---- 8: Paint 1: a paint row, region 2, the width drag, the rotation typed
            c.Id = "UI5.8";
            yield return U5OpenPage(c, s, id, "paint", tab);
            if (tab[0] == null) yield break;
            const string paint = U5Body + "paint[0].";
            yield return U5PickRow(c, s, U4List(tab[0], paint + "item"), id, paint + "item", 1, "the paint 1 list");
            var region = U4List(tab[0], paint + "layoutItem");
            if (c.Check(region != null, "the paint 1 region list"))
            {
                string layout0 = U4Json(ui, id, paint + "layout"), rot0 = U4Json(ui, id, paint + "rotation");
                string target = "illusion.hs2#2";
                if (U5Ref(ui, id, paint + "layoutItem") == target) c.Info("UI5: paint 1 already has region 2; picked again (no entry expected)");
                int u = U4Undo(ui);
                yield return U4ClickCell(c, s4, region, target, "the region cell «背中・うなじ» " + target);
                for (int i = 0; i < 120 && U5Ref(ui, id, paint + "layoutItem") != target; i++) yield return c.Frames(1);
                yield return c.Frames(3);
                U4Entry(c, ui, u, HS2Next.UI.Core.OpLabels.ChangeItem, "the region pick");
                c.Check(U5Ref(ui, id, paint + "layoutItem") == target && U4Json(ui, id, paint + "layout") == layout0 && U4Json(ui, id, paint + "rotation") == rot0,
                    $"body.paint[0].layoutItem = {U5Ref(ui, id, paint + "layoutItem")} ({target}); layout and rotation unchanged");
                yield return U5RegionUv(c, ui, id, target);
            }
            var width = U4Slider(tab[0], paint + "layout", 0);
            if (c.Check(width != null, "the paint 1 width component row (field-…paint[0].layout#0)"))
            {
                var l0 = U4Vec(ui, id, paint + "layout");
                yield return c.Call(new { cmd = "chara.edit.info", key = id });
                int th0 = (int?)c.J["throttle"]?["throttled"] ?? 0;
                int u = U4Undo(ui);
                yield return U5DragKnob(c, s, width, (float)(l0?[0] ?? 0.5), 8, "the paint 1 width");
                U4Entry(c, ui, u, U5EditBody, "the width drag");
                var l1 = U4Vec(ui, id, paint + "layout");
                c.Check(l0 != null && l1 != null && l1[0] != l0[0] && l1[0] >= 0 && l1[0] <= 1 && l1[1] == l0[1] && l1[2] == l0[2] && l1[3] == l0[3],
                    $"only the width (layout x) moved, inside 0..1 ({U4Num(l0)} → {U4Num(l1)})");
                yield return c.Call(new { cmd = "chara.edit.info", key = id });
                var th = c.J["throttle"];
                int judged = (int?)th?["judged"] ?? -1, tw = (int?)c.J["lastFlush"]?["throttleWeight"] ?? -1, th1 = (int?)th?["throttled"] ?? 0;
                bool enabled = (bool?)th?["enabled"] == true;
                c.Info($"UI5 ({Here()}): the width drag: throttle {AcceptanceCtx.Short(th?.ToString(Newtonsoft.Json.Formatting.None))}, lastFlush.throttleWeight {tw}");
                c.Check(judged == 2 || tw == 2, $"the drag's body rebuild weight W = 2 (both body creators; judged {judged}, last flush {tw})");
                if (enabled) c.Check(th1 - th0 >= 1, $"the throttle made drag frames wait (throttled {th0} → {th1}, mode {(string)th?["mode"]})");
                else c.Info("UI5: the body throttle's master switch is off here (CharaEditFlush.ThrottleBodyDuringDrags); waits not checked");
            }
            var rot = U4Slider(tab[0], paint + "rotation", -1);
            if (c.Check(rot != null, "the paint 1 rotation row"))
            {
                int u = U4Undo(ui);
                yield return U4Type(c, s4, rot.Control.Field, "25", "the paint 1 rotation number field");
                yield return c.Frames(3);
                float r = Ui3Value(ui, id, paint + "rotation");
                c.Check(r == 0.25f, $"typed 25 → stored {r} (0.25)");
                U4Entry(c, ui, u, U5EditBody, "the typed rotation");
                yield return U5FlushInfo(c, id, "the typed rotation");
            }

            // ---- 9: the paint list's search, cleared with Backspace
            c.Id = "UI5.9";
            var pl = U4List(tab[0], paint + "item");
            if (c.Check(pl != null, "the paint 1 list"))
            {
                string curPaint = U5Ref(ui, id, paint + "item");
                var rows = pl.AllItems.Where(x => !x.NotListed).ToList();
                var t = rows.Where(x => x.Ref != curPaint && !string.IsNullOrEmpty(x.Name)).Skip(2).FirstOrDefault() ?? rows.FirstOrDefault(x => !string.IsNullOrEmpty(x.Name));
                string word = (t?.Name ?? "").Split(new[] { ' ', '　', '_', '-' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                if (c.Check(t != null && word.Length > 0, $"a word of a paint row's name (\"{word}\" of {t?.Ref} {t?.Name})"))
                {
                    var search = pl.Q<TextField>("partlist-search");
                    yield return U4Type(c, s4, search, word, "the paint list's search field");
                    for (int i = 0; i < 60 && pl.Items.Count(x => !x.NotListed) >= rows.Count; i++) yield return c.Frames(1);   // the search's debounce
                    int filtered = pl.Items.Count(x => !x.NotListed);
                    c.Check(pl.SearchText == word && pl.PositionOf(t.Ref) >= 0 && filtered < rows.Count,
                        $"the search \"{word}\" shows {filtered} of {rows.Count} paints incl. {t.Ref}");
                    var bs = Enumerable.Range(0, word.Length + 1).Select(_ => (object)new { type = "key", key = "Backspace" }).ToArray();
                    yield return c.Call(new { cmd = "ui.input", frames = 3, events = bs });
                    c.CheckOk($"ui.input Backspace ×{bs.Length} in the search field");
                    for (int i = 0; i < 60 && (pl.SearchText != "" || pl.Items.Count(x => !x.NotListed) < rows.Count); i++) yield return c.Frames(1);
                    if (pl.SearchText != "")
                    {
                        c.Info($"UI5: the search field held \"{pl.SearchText}\" after Backspace (focus lost?); clicked and cleared again");
                        var p = new Vector2[1];
                        yield return U4Reveal(c, s4, search.Q("unity-text-input") ?? search, "the paint list's search field", p);
                        var ev = new List<object> { new { type = "pointer", phase = "click", x = p[0].x, y = p[0].y, button = 0 } };
                        ev.AddRange(Enumerable.Range(0, pl.SearchText.Length + 2).Select(_ => (object)new { type = "key", key = "Backspace" }));
                        yield return c.Call(new { cmd = "ui.input", frames = 3, events = ev.ToArray() });
                        for (int i = 0; i < 60 && (pl.SearchText != "" || pl.Items.Count(x => !x.NotListed) < rows.Count); i++) yield return c.Frames(1);
                    }
                    c.Check(pl.SearchText == "" && pl.Items.Count(x => !x.NotListed) == rows.Count,
                        $"Backspace cleared the search: every paint shown again ({pl.Items.Count(x => !x.NotListed)} of {rows.Count}; text \"{pl.SearchText}\")");
                    c.Check(U4Marked(pl, curPaint), "the current paint " + curPaint + " carries the current mark");
                }
            }

            // ---- 10: Special: the futanari toggle (the user's Y9 decision)
            c.Id = "UI5.10";
            yield return U5OpenPage(c, s, id, "special", tab);
            if (tab[0] == null) yield break;
            var fut = tab[0].Q<MakerToggleRow>("field-" + U5Futanari);
            if (c.Check(fut != null && fut.resolvedStyle.display != DisplayStyle.None, "the futanari toggle row (field-appearance.chara.profile.futanari)"))
            {
                bool f0 = FieldAccessor.Get(ui.Model, id, U5Futanari) is bool fb0 && fb0;
                c.Check(fut.Toggle.value == f0, $"the toggle shows the stored value {f0}");
                c.Check(!string.IsNullOrEmpty(fut.Toggle.tooltip), $"the toggle's tooltip (data only in Studio): \"{AcceptanceCtx.Short(fut.Toggle.tooltip)}\"");
                int u = U4Undo(ui);
                yield return U4Click(c, s4, fut.Toggle.Q("unity-checkmark") ?? fut.Toggle, "the futanari toggle");
                yield return c.Frames(3);
                U4Entry(c, ui, u, U5EditBody, "the futanari toggle");
                bool f1 = FieldAccessor.Get(ui.Model, id, U5Futanari) is bool fb1 && fb1;
                c.Check(f1 == !f0 && fut.Toggle.value == f1, $"profile.futanari = {f1} (was {f0}), the toggle shows it");
                yield return c.Call(new { cmd = "ui.action", action = "edit.undo" });
                yield return c.Frames(3);
                bool f2 = FieldAccessor.Get(ui.Model, id, U5Futanari) is bool fb2 && fb2;
                c.Check(f2 == f0 && fut.Toggle.value == f0 && U4Undo(ui) == u, $"undo restores profile.futanari {f2} and the toggle ({fut.Toggle.value})");
                yield return c.Call(new { cmd = "ui.action", action = "edit.redo" });
                yield return c.Frames(3);
                bool f3 = FieldAccessor.Get(ui.Model, id, U5Futanari) is bool fb3 && fb3;
                c.Check(f3 == f1 && U4Undo(ui) == u + 1, $"redo sets it again ({f3})");
            }

            // ---- 11: ↺ on the skin gloss
            c.Id = "UI5.11";
            yield return U5OpenPage(c, s, id, "skin", tab);
            if (tab[0] == null) yield break;
            var sg = U4Slider(tab[0], U5Body + "skin.gloss", -1);
            if (c.Check(sg != null, "the skin gloss row"))
            {
                float g0 = Ui3Value(ui, id, U5Body + "skin.gloss");
                c.Check(g0 != s.Gloss1, $"the gloss differs from the reference before ↺ ({g0}; reference {s.Gloss1})");
                int u = U4Undo(ui);
                yield return U4Click(c, s4, sg.Control.ResetButton, "the skin gloss row's ↺");
                yield return c.Frames(3);
                U4Entry(c, ui, u, U4ResetValue, "↺");
                float g1 = Ui3Value(ui, id, U5Body + "skin.gloss");
                c.Check(g1 == s.Gloss1, $"↺ → the value of step 1 {g1} ({s.Gloss1}, the reference)");
            }

            // ---- 12: undo to step 1, redo to the end
            c.Id = "UI5.12";
            yield return c.Frames(3);
            var docsE = new string[1];
            yield return U4Docs(c, id, docsE);
            s.DocsEnd = docsE[0];
            var digE = new JObject[1];
            yield return U5Digest(c, id, digE);
            s.DigEnd = digE[0];
            int n = U4Undo(ui) - s.U1;
            c.Info($"UI5: {n} history entries since step 1: {string.Join(" | ", ui.Session.Commands.UndoList.Take(n).Reverse().Select(e => e.Label))}");
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
            yield return U5Digest(c, id, dig);
            c.Check(U5SameDigest(s.Dig1, dig[0], out string d1), "undo to step 1 → the body digest equals step 1's", d1);
            for (int i = 0; i < n; i++)
            {
                yield return c.Call(new { cmd = "ui.action", action = "edit.redo" });
                if (!(c.Last != null && c.Last.Ok)) { c.CheckOk("ui.action edit.redo #" + (i + 1)); break; }
                yield return c.Frames(1);
            }
            yield return c.Frames(6);
            yield return U4Docs(c, id, docs);
            c.Check(docs[0] != null && docs[0] == s.DocsEnd, "redo to the end → the documents equal the end state", docs[0] == s.DocsEnd ? "" : U4JsonDiff(s.DocsEnd, docs[0]));
            yield return U5Digest(c, id, dig);
            c.Check(U5SameDigest(s.DigEnd, dig[0], out string d2), "redo to the end → the body digest equals the end state's", d2);

            // ---- 13: save as, new, open
            c.Id = "UI5.13";
            yield return c.Call(new { cmd = "ui.action", action = "file.saveAs", args = new { name = "ui5_acceptance", overwrite = true } }, 120);
            string file = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && file != null && File.Exists(file), "ui.action file.saveAs {name} → " + file)) yield break;
            yield return c.Call(new { cmd = "ui.action", action = "file.new", args = new { force = true } });
            c.CheckOk("ui.action file.new {force}");
            yield return c.Call(new { cmd = "ui.action", action = "file.open", args = new { path = file } }, 180);
            c.CheckOk("ui.action file.open {path}");
            yield return U5WaitBuilt(c, ui, id);
            if (!c.Check(ui.Runtime.FindChara(id)?.State == HS2Next.Scenes.Runtime.RealizationState.Built, "the reopened character is built (same node id)")) yield break;
            yield return c.Frames(3);
            yield return U4Docs(c, id, docs);
            c.Check(docs[0] != null && docs[0] == s.DocsEnd, "reopened: the documents equal the end state", docs[0] == s.DocsEnd ? "" : U4JsonDiff(s.DocsEnd, docs[0]));
            yield return U5Digest(c, id, dig);
            c.Check(U5SameDigest(s.DigEnd, dig[0], out string d3), "reopened: the body digest (materials, creators, bones, normals, bust" + (dig[0]?["rts"] != null ? ", RT hashes" : "") + ") equals the end state's", d3);
            yield return U5OpenPage(c, s, id, "shape", tab);
            if (tab[0] != null)
            {
                var h2 = U4Slider(tab[0], U5Body + "shape[9]", -1);
                var b2 = U4Slider(tab[0], U5Body + "shape[1]", -1);
                c.Check(h2 != null && b2 != null && Math.Abs(h2.Control.Value - 150) < 1e-4 && Math.Abs(b2.Control.Value - 30) < 1e-4,
                    $"reopened: Shape shows {h2?.Control.Value} on the head size (150) and {b2?.Control.Value} on the bust size (30)");
            }
            // Shape, then Paint: step 14 builds HERO107 while Body › Paint is shown - the regression check of W-1 (a build while
            // Body › Paint was shown hung the D3D12 device before W's fix; phase42_A_w1.txt, phase42_W_w1.txt)
            yield return U5OpenPage(c, s, id, "paint", tab);
            if (tab[0] != null)
            {
                var r2 = U4Slider(tab[0], paint + "rotation", -1);
                c.Check(r2 != null && Math.Abs(r2.Control.Value - 25) < 1e-6, $"reopened: Paint shows {r2?.Control.Value} on the paint 1 rotation (25)");
            }
        }

        // ---------------------------------------------------------------- the male part (step 14)

        static IEnumerator U5Male(AcceptanceCtx c, U5State s)
        {
            var ui = s.Ui;
            var lists = MakerCatalog.For(ui.App?.Catalog);
            c.Id = "UI5.14";
            string card = Path.Combine(AppCore.Current.Paths.Hs2CharaDir(0) ?? "", Ui2MaleRel ?? "");
            yield return c.Call(new { cmd = "ui.action", action = "add.male", args = new { path = card } }, 180);
            string id = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && !string.IsNullOrEmpty(id), "ui.action add.male {path} → a new character (HERO107)", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            yield return U5WaitBuilt(c, ui, id);
            if (!c.Check(ui.Runtime.FindChara(id)?.State == HS2Next.Scenes.Runtime.RealizationState.Built, "the male character is built")) yield break;
            s.M = id;
            var tab = new CharaEditTab[1];
            yield return U5OpenPage(c, s, id, "shape", tab);
            if (tab[0] == null) yield break;
            bool pinned = HS2Next.Chara.CharaAssembler.MaleHeightPin075;
            var rows = U5Rows(tab[0], "shape");
            var paths = rows.Select(r => r.Path).ToList();
            c.Check(!pinned && rows.Count == 29 && paths.Contains(U5Body + "shape[0]"),
                $"male: the Shape page shows 29 rows incl. the height (MaleHeightPin075 {pinned}; {rows.Count} rows)");
            var fem = U5FemaleOnly.Where(paths.Contains).ToList();
            c.Check(fem.Count == 0, "male: none of the female-only rows (" + (fem.Count == 0 ? "none" : string.Join(", ", fem)) + ")");
            var special = tab[0].Q<Button>("edit-page-body-special");
            c.Check(special == null || special.style.display == DisplayStyle.None, "male: no Special page (its button hidden)");

            // the male skin list (131)
            yield return U5OpenPage(c, s, id, "skin", tab);
            if (tab[0] == null) yield break;
            var skin = U4List(tab[0], U5Body + "skin.item");
            var want = lists?.Rows(131, 0);
            c.Check(skin != null && want != null && skin.Category == 131 && skin.AllItems.Where(x => !x.NotListed).Select(x => x.Ref).SequenceEqual(want.Select(r => r.Key.ToString())),
                $"male: the skin list = MakerLists rows of 131 ({skin?.AllItems.Count(x => !x.NotListed)} cells / {want?.Count} rows, category {skin?.Category})");
            yield return U5PickRow(c, s, skin, id, U5Body + "skin.item", 1, "the male skin list");

            // the height typed
            yield return U5OpenPage(c, s, id, "shape", tab);
            if (tab[0] == null) yield break;
            var hRow = U4Slider(tab[0], U5Body + "shape[0]", -1);
            if (c.Check(hRow != null, "male: the height row"))
            {
                var nh = new JObject[1];
                yield return U5Bone(c, id, "cf_N_height", nh);
                int u = U4Undo(ui);
                yield return U4Type(c, s.S4, hRow.Control.Field, "80", "the male height number field");
                yield return c.Frames(3);
                float h = Ui3Value(ui, id, U5Body + "shape[0]");
                c.Check(h == 0.8f, $"male: typed 80 → stored {h} (0.8)");
                U4Entry(c, ui, u, U5EditBody, "the typed male height");
                yield return U5FlushInfo(c, id, "the typed male height");
                var nh1 = new JObject[1];
                yield return U5Bone(c, id, "cf_N_height", nh1);
                c.Check(nh[0] != null && nh1[0] != null && !JToken.DeepEquals(nh[0]["ls"], nh1[0]["ls"]),
                    $"male: cf_N_height follows the height (scale {U5T(nh[0]?["ls"])} → {U5T(nh1[0]?["ls"])}; the 0.75 pin off)");
            }

            // chara.edit.bust: no normals, the chains per BustOptions.MaleBustPhysics
            var bust = new JObject[1];
            yield return U5Bust(c, id, bust);
            var nm = bust[0]?["normals"];
            c.Check(bust[0] != null && (nm?["source"] == null || nm["source"].Type == JTokenType.Null) && (!(nm?["entries"] is JArray en) || en.Count == 0) && (nm?["rate"] == null || nm["rate"].Type == JTokenType.Null),
                $"male: no breast normals (source {U5T(nm?["source"])}, entries {U5T(nm?["entries"])}, rate {U5T(nm?["rate"])})");
            var ph = bust[0]?["physics"];
            bool? enabled = (bool?)ph?["enabled"], mbp = (bool?)ph?["maleBustPhysics"];
            c.Check(enabled == true && mbp == true, $"male: bust physics enabled per BustOptions.MaleBustPhysics (enabled {enabled}, maleBustPhysics {mbp})");
            yield return U5BustCheck(c, ui, id, "male");
        }

        // ---------------------------------------------------------------- helpers

        static IEnumerator U5WaitBuilt(AcceptanceCtx c, UiContext ui, string id)
        {
            for (int i = 0; i < 600 && ui.Runtime.FindChara(id)?.State != HS2Next.Scenes.Runtime.RealizationState.Built; i++) yield return c.Frames(1);
        }

        static string U5Ref(UiContext ui, string id, string path)
        {
            try { return FieldAccessor.Get(ui.Model, id, path) as string; } catch (Exception) { return null; }
        }

        static string U5T(JToken t) => t == null ? "null" : AcceptanceCtx.Short(t.ToString(Newtonsoft.Json.Formatting.None));

        static string U5Short(string h) => h == null ? "null" : h.Length > 12 ? h.Substring(0, 12) : h;

        /// <summary>The Maker slider rows of a built Body page (in order).</summary>
        static List<MakerSliderRow> U5Rows(CharaEditTab tab, string page)
        {
            var sp = tab.Q("edit-subpage-body-" + page);
            return sp == null ? new List<MakerSliderRow>() : sp.Query<MakerSliderRow>().Where(r => r.resolvedStyle.display != DisplayStyle.None).ToList();
        }

        static bool U5Same(JToken prop, double[] v)
        {
            if (!(prop is JArray a) || v == null || a.Count != v.Length) return false;
            for (int i = 0; i < v.Length; i++) if (Math.Abs((double)a[i] - v[i]) > 1e-5) return false;
            return true;
        }

        /// <summary>The gloss state of the character (chara.state.gloss; 0 when it cannot be read).</summary>
        static float U5GlossState(AcceptanceCtx c, UiContext ui, string id)
        {
            try { if (FieldAccessor.Get(ui.Model, id, "chara.state.gloss") is float g) return g; }
            catch (Exception e) { c.Info("UI5: chara.state.gloss not readable (" + e.Message + "); 0 assumed"); }
            return 0f;
        }

        static IEnumerator U5Digest(AcceptanceCtx c, string id, JObject[] into)
        {
            yield return c.Call(new { cmd = "chara.edit.digest", key = id, parts = "body", text = true }, 120);
            into[0] = c.Last != null && c.Last.Ok ? (JObject)c.J.DeepClone() : null;
        }

        static IEnumerator U5Bust(AcceptanceCtx c, string id, JObject[] into)
        {
            yield return c.Call(new { cmd = "chara.edit.bust", key = id });
            into[0] = c.Last != null && c.Last.Ok ? (JObject)c.J.DeepClone() : null;
        }

        /// <summary>The bones.dump entry (root body) of a bone by name.</summary>
        static IEnumerator U5Bone(AcceptanceCtx c, string id, string name, JObject[] into)
        {
            yield return c.Call(new { cmd = "bones.dump", key = id, root = "body" }, 120);
            // the body rig's own bone: the shortest path of that name (clothes or uncensor rigs may hold a transform of the same name)
            into[0] = c.Last != null && c.Last.Ok && c.J["bones"] is JArray a
                ? a.OfType<JObject>().Where(b => (string)b["name"] == name).OrderBy(b => ((string)b["path"] ?? "").Length).FirstOrDefault() : null;
            if (into[0] != null) c.Info($"UI5: {name} = {(string)into[0]["path"]}, scale {U5T(into[0]["ls"])}");
        }

        static IEnumerator U5FlushInfo(AcceptanceCtx c, string id, string what)
        {
            yield return c.Call(new { cmd = "chara.edit.info", key = id });
            if (c.Last != null && c.Last.Ok)
                c.Info($"UI5 timing ({(Application.isEditor ? "Editor" : "player")}): {what}'s flush {(double?)c.J["lastFlush"]?["ms"]:0.000} ms, mode {(string)c.J["lastFlush"]?["mode"]}, units {AcceptanceCtx.Short(c.J["lastFlush"]?["units"]?.ToString(Newtonsoft.Json.Formatting.None))}, creators rebuilt {AcceptanceCtx.Short(c.J["lastFlush"]?["creatorsRebuilt"]?.ToString(Newtonsoft.Json.Formatting.None))}");
        }

        /// <summary>One property of the first slot holding it on the body draw (part body) or on o_head (part face) (chara.edit.materials).</summary>
        static IEnumerator U5MatProp(AcceptanceCtx c, string id, string part, string prop, JToken[] into)
        {
            into[0] = null;
            if (part == "face") yield return c.Call(new { cmd = "chara.edit.materials", key = id, renderers = new[] { "o_head" }, props = new[] { prop } });
            else yield return c.Call(new { cmd = "chara.edit.materials", key = id, part, props = new[] { prop } });
            if (c.Last == null || !c.Last.Ok || !(c.J["renderers"] is JArray rs)) yield break;
            foreach (var r in rs)
                foreach (var sl in (r["slots"] as JArray) ?? new JArray())
                    if (sl["props"]?[prop] != null) { into[0] = sl["props"][prop]; yield break; }
        }

        /// <summary>The props of the body draw (o_body_cf / o_body_cm slot 0) from a chara.edit.materials reply.</summary>
        static JObject U5BodyProps(JObject reply)
        {
            if (!(reply["renderers"] is JArray rs)) return null;
            var r = rs.FirstOrDefault(x => (string)x["name"] == "o_body_cf" || (string)x["name"] == "o_body_cm");
            return (r?["slots"] as JArray)?.FirstOrDefault()?["props"] as JObject;
        }

        static string U5DigestLine(JObject d)
        {
            if (d == null) return "none";
            return string.Join(", ", U5DigestSections.Where(k => d[k] != null && d[k].Type != JTokenType.Null).Select(k => k + " " + U5Short((string)d[k])));
        }

        static readonly string[] U5DigestSections = { "materials", "creators", "bones", "rts", "normals", "bust" };

        static bool U5SameDigest(JObject a, JObject b, out string detail)
        {
            detail = "";
            if (a == null || b == null) { detail = "no digest"; return false; }
            var bad = new List<string>();
            foreach (var k in U5DigestSections)
            {
                if ((a[k] == null || a[k].Type == JTokenType.Null) && (b[k] == null || b[k].Type == JTokenType.Null)) continue;
                if (JToken.DeepEquals(a[k], b[k])) continue;
                string ta = (string)a["texts"]?[k], tb = (string)b["texts"]?[k];
                bad.Add(k + (ta != null && tb != null ? " (" + U4FirstLine(ta, tb) + ")" : ""));
            }
            detail = bad.Count == 0 ? string.Join(", ", U5DigestSections.Where(k => a[k] != null && a[k].Type != JTokenType.Null)) + " equal" : "differ: " + string.Join("; ", bad);
            return bad.Count == 0;
        }

        /// <summary>Selects the character, opens the inspector's Edit tab, the Body node and <paramref name="page"/> (null = keep
        /// the page) by clicks.</summary>
        static IEnumerator U5OpenPage(AcceptanceCtx c, U5State s, string id, string page, CharaEditTab[] tab)
        {
            var ui = s.Ui;
            var host = s.S4.Host;
            tab[0] = null;
            if (ui.Selection.Primary != id) { yield return c.Call(new { cmd = "select", key = id }); yield return c.Frames(3); }
            yield return c.Call(new { cmd = "ui.panel", panel = "inspector", op = "focus" });
            yield return c.Frames(4);
            var body = host.Root.Q<CharaEditTab>();
            if (body == null || body.panel == null || body.PrimaryId != id)
            {
                var tb = host.Root.Q<Button>("tab-" + CharaEditTab.TabId);
                if (!c.Check(tb != null, "the character inspector's Edit tab")) yield break;
                yield return U4Click(c, s.S4, tb, "the Edit tab");
                body = host.Root.Q<CharaEditTab>();
            }
            if (!c.Check(body != null && body.panel != null && body.PrimaryId == id, "the Edit tab of " + id + " is shown")) yield break;
            if (body.CurrentNode != "body")
            {
                yield return U4Click(c, s.S4, body.Q<Button>("edit-tree-body"), "the Body node");
                yield return c.Frames(2);
                if (!c.Check(body.CurrentNode == "body", "Edit › Body is open (" + body.CurrentNode + ")")) yield break;
            }
            if (page != null && body.CurrentPage != page)
            {
                var pb = body.Q<Button>("edit-page-body-" + page);
                if (!c.Check(pb != null && pb.style.display != DisplayStyle.None, "the sub-page button edit-page-body-" + page + " is shown")) yield break;
                yield return U4Click(c, s.S4, pb, "edit-page-body-" + page);
                yield return c.Frames(2);
            }
            if (!c.Check(body.HasSession && (page == null || body.CurrentPage == page), $"Body › {page ?? body.CurrentPage} is open with the edit session ({body.CurrentPage})")) yield break;
            tab[0] = body;
        }

        /// <summary>Picks the row at <paramref name="index"/> of <paramref name="list"/>'s listed rows (the next one when it is the
        /// current value) → one entry "Change item" and the member holds it.</summary>
        static IEnumerator U5PickRow(AcceptanceCtx c, U5State s, PartList list, string id, string path, int index, string what)
        {
            var ui = s.Ui;
            if (!c.Check(list != null && list.resolvedStyle.display != DisplayStyle.None, what + " is shown")) yield break;
            string cur = U5Ref(ui, id, path);
            var rows = list.Items.Where(x => !x.NotListed && x.Row != null).ToList();
            var target = rows.Skip(index).FirstOrDefault(x => x.Ref != cur) ?? rows.FirstOrDefault(x => x.Ref != cur);
            if (!c.Check(target != null, $"{what}: row {index} (or the next) other than {cur} ({rows.Count} rows)")) yield break;
            int u = U4Undo(ui);
            yield return U4ClickCell(c, s.S4, list, target.Ref, what + " cell " + target.Ref);
            for (int i = 0; i < 120 && U5Ref(ui, id, path) != target.Ref; i++) yield return c.Frames(1);
            yield return c.Frames(3);
            U4Entry(c, ui, u, HS2Next.UI.Core.OpLabels.ChangeItem, what + " pick");
            c.Check(U5Ref(ui, id, path) == target.Ref, $"{path.Substring("appearance.chara.".Length)} = {U5Ref(ui, id, path)} ({target.Ref} {target.Name}; was {cur})");
        }

        /// <summary>A pointer drag on a slider's knob over <paramref name="moves"/> frames (20 % of the track, away from the nearer end).</summary>
        static IEnumerator U5DragKnob(AcceptanceCtx c, U5State s, MakerSliderRow r, float v0, int moves, string what)
        {
            var knob = r.Control.Slider?.Q("unity-dragger");
            if (!c.Check(knob != null, what + ": the slider's knob")) yield break;
            var kp = new Vector2[1];
            yield return U4Reveal(c, s.S4, knob, what + " knob", kp);
            float track = r.Control.Slider.Q("unity-drag-container")?.worldBound.width ?? 100f;
            float dx = (v0 > 0.5f ? -1 : 1) * Mathf.Max(8f, track * 0.2f);
            var p0 = kp[0];
            var ev = new List<object> { new { type = "pointer", phase = "down", x = p0.x, y = p0.y, button = 0 } };
            for (int i = 1; i <= moves; i++) ev.Add(new { type = "pointer", phase = "move", x = p0.x + dx * i / moves, y = p0.y, button = 0 });
            ev.Add(new { type = "pointer", phase = "up", x = p0.x + dx, y = p0.y, button = 0 });
            yield return c.Call(new { cmd = "ui.input", frames = 3, events = ev.ToArray() });
            c.CheckOk($"ui.input drag on {what} ({moves} frames)");
            yield return c.Frames(4);
            c.Check(!r.Dragging, what + ": no drag transaction left open");
        }

        /// <summary>A colour row's picker: a drag in the SV square (or, <paramref name="alphaStrip"/>, on the alpha strip) over
        /// 3 frames, Enter → one entry "Edit body"; the colour changed and the alpha kept (SV) or the alpha changed and the
        /// colour kept (alpha strip).</summary>
        static IEnumerator U5ColourDrag(AcceptanceCtx c, U5State s, VisualElement root, string id, string path, string what, bool alphaStrip)
        {
            var ui = s.Ui;
            var row = root.Query<MakerColorRow>().Where(r => r.Path == path).First();
            if (!c.Check(row != null, what + " row")) yield break;
            var c0 = U4Vec(ui, id, path);
            int u = U4Undo(ui);
            yield return U4Click(c, s.S4, row.Swatch.Button, what + " swatch");
            yield return c.Frames(4);
            var picker = row.Picker;
            if (!c.Check(picker != null && picker.panel != null, what + ": the colour picker is open")) yield break;
            var area = picker.Q(alphaStrip ? "alpha" : "sv");
            if (!c.Check(area != null && area.worldBound.width > 0, what + ": the picker's " + (alphaStrip ? "alpha strip" : "SV square"))) yield break;
            var b = area.worldBound;
            var p0 = alphaStrip ? new Vector2(b.center.x, b.yMin + b.height * 0.3f) : new Vector2(b.xMin + b.width * 0.35f, b.yMin + b.height * 0.35f);
            var d = alphaStrip ? new Vector2(0, b.height * 0.3f) : new Vector2(b.width * 0.3f, b.height * 0.3f);
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
            if (!c.Check(row.Picker == null, what + ": Enter closed the picker"))
            {
                row.Picker?.Close(true);
                yield return c.Frames(2);
            }
            var c1 = U4Vec(ui, id, path);
            U4Entry(c, ui, u, U5EditBody, what + " picker session");
            bool four = c0 != null && c1 != null && c0.Length == 4 && c1.Length == 4;
            if (alphaStrip)
                c.Check(four && c1[3] != c0[3] && Math.Abs(c1[0] - c0[0]) < 2e-3 && Math.Abs(c1[1] - c0[1]) < 2e-3 && Math.Abs(c1[2] - c0[2]) < 2e-3,
                    $"{what}: the stored alpha changed, the colour kept within the HSV round trip ({U4Num(c0)} → {U4Num(c1)})");
            else
                c.Check(four && (c1[0] != c0[0] || c1[1] != c0[1] || c1[2] != c0[2]) && c1[3] == c0[3], $"{what}: the stored colour changed, alpha kept ({U4Num(c0)} → {U4Num(c1)})");
        }

        static float U5Tree(float v0, float v1, float v2, float t) => t < 0.5f ? Mathf.Lerp(v0, v1, 2f * t) : Mathf.Lerp(v1, v2, 2f * t - 1f);

        /// <summary>chara.edit.bust's breast chains = the bust formulas of the stored softness, weight and bust size
        /// (Y10: the gravity lerp clamped, the softness unclamped in the product).</summary>
        static IEnumerator U5BustCheck(AcceptanceCtx c, UiContext ui, string id, string when)
        {
            var bust = new JObject[1];
            yield return U5Bust(c, id, bust);
            float sft = Ui3Value(ui, id, U5Body + "bust.softness"), w = Ui3Value(ui, id, U5Body + "bust.weight"), b = Ui3Value(ui, id, U5Body + "shape[1]");
            float t = Mathf.Clamp01(sft * b + 0.01f);
            float stiff = U5Tree(1f, 0.1f, 0.01f, t), elast = U5Tree(0.2f, 0.15f, 0.05f, t), damp = U5Tree(0.2f, 0.1f, 0.1f, t);
            float gy = Mathf.Lerp(0f, -0.05f, w) * b * sft * 0.5f;
            var chains = bust[0]?["physics"]?["chains"] as JArray;
            var bad = new List<string>();
            if (chains == null || chains.Count != 2) bad.Add("chains " + (chains?.Count.ToString() ?? "none"));
            else
                foreach (var ch in chains)
                {
                    string nm = (string)ch["name"];
                    if (Math.Abs(((double?)ch["damping"] ?? double.NaN) - damp) > 1e-5) bad.Add($"{nm} damping {(double?)ch["damping"]}");
                    if (Math.Abs(((double?)ch["elasticity"] ?? double.NaN) - elast) > 1e-5) bad.Add($"{nm} elasticity {(double?)ch["elasticity"]}");
                    if (Math.Abs(((double?)ch["stiffness"] ?? double.NaN) - stiff) > 1e-5) bad.Add($"{nm} stiffness {(double?)ch["stiffness"]}");
                    var g = ch["gravity"] as JArray;
                    if (g == null || g.Count != 3 || Math.Abs((double)g[1] - gy) > 1e-6 || (double)g[0] != 0 || (double)g[2] != 0) bad.Add($"{nm} gravity {U5T(g)}");
                }
            c.Check(bad.Count == 0, $"chara.edit.bust {when}: the breast chains = HS2's bust parameters of s {sft}, w {w}, b {b} (damping {damp:0.#####}, elasticity {elast:0.#####}, stiffness {stiff:0.#####}, gravity y {gy:0.########})",
                bad.Count == 0 ? "" : string.Join("; ", bad));
        }

        /// <summary>The body colour creator's _Texture5UV / _Texture5Rotator after a region pick = the layout's values from the region row's columns.</summary>
        static IEnumerator U5RegionUv(AcceptanceCtx c, UiContext ui, string id, string regionRef)
        {
            var cols = new[] { "CenterX", "MoveX", "CenterY", "MoveY", "CenterScale", "AddScale" };
            yield return c.Call(new { cmd = "chara.edit.list", category = 8, sex = 1, columns = cols, limit = 100 });
            var row = (c.J["rows"] as JArray)?.FirstOrDefault(r => (string)r["ref"] == regionRef);
            float[] v = row?["values"] is JObject vals ? cols.Select(k => float.TryParse((string)vals[k], NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : float.NaN).ToArray() : null;
            if (!c.Check(v != null && v.All(x => !float.IsNaN(x)), $"chara.edit.list 8: the columns of region {regionRef} ({U5T(row?["values"])})")) yield break;
            float cx = v[0], mx = v[1], cy = v[2], my = v[3], sc = v[4], ad = v[5];
            var l = U4Vec(ui, id, U5Body + "paint[0].layout");
            float rot = Ui3Value(ui, id, U5Body + "paint[0].rotation");
            if (l == null || l.Length != 4) { c.Check(false, "the paint 1 layout"); yield break; }
            var want = new[] { Mathf.Lerp(sc + ad, sc - ad, (float)l[0]), Mathf.Lerp(sc + ad, sc - ad, (float)l[1]), Mathf.Lerp(cx + mx, cx - mx, (float)l[2]), Mathf.Lerp(cy + my, cy - my, (float)l[3]) };
            float wantRot = Mathf.Lerp(1f, -1f, rot);
            yield return c.Call(new { cmd = "chara.edit.rtstats", key = id, part = "body", grid = 1 }, 120);
            if (c.Last != null && !c.Last.Ok && (string)c.J["code"] == "unavailable") { c.Info("UI5: rtstats unavailable (no graphics device); _Texture5UV not checked"); yield break; }
            var colour = (c.J["creators"] as JArray)?.FirstOrDefault(x => (string)x["name"] == "colour");
            var uv = colour?["props"]?["_Texture5UV"] as JArray;
            var rt = colour?["props"]?["_Texture5Rotator"];
            bool ok = uv != null && uv.Count == 4 && Enumerable.Range(0, 4).All(i => Math.Abs((double)uv[i] - want[i]) < 1e-5);
            c.Check(ok, $"the body colour creator's _Texture5UV = the paint layout's UV for region 2 ({U5T(uv)}; want ({string.Join(", ", want.Select(x => x.ToString("0.#####", CultureInfo.InvariantCulture)))}) from layout {U4Num(l)})");
            c.Check(rt != null && rt.Type != JTokenType.Null && Math.Abs((double)rt - wantRot) < 1e-5, $"_Texture5Rotator = lerp(1, −1, {rot}) = {wantRot} ({U5T(rt)})");
        }

        /// <summary>A duplicate of the character is a fresh build of the same documents: its body digest bones / normals / bust
        /// equal the edited character's; the duplicate is undone (one entry and back).</summary>
        static IEnumerator U5FreshBuild(AcceptanceCtx c, U5State s, string id, JObject edited)
        {
            var ui = s.Ui;
            int u = U4Undo(ui);
            yield return c.Call(new { cmd = "ui.action", action = "edit.duplicate" }, 180);
            c.CheckOk("ui.action edit.duplicate (a fresh build of the documents)");
            yield return c.Frames(2);
            string dup = ui.Selection.Primary;
            if (!c.Check(dup != null && dup != id && ui.Model.Find(dup) != null, $"the duplicate is a new node ({dup})")) yield break;
            yield return U5WaitBuilt(c, ui, dup);
            if (c.Check(ui.Runtime.FindChara(dup)?.State == HS2Next.Scenes.Runtime.RealizationState.Built, "the duplicate is built"))
            {
                yield return c.Frames(3);
                var d = new JObject[1];
                yield return U5Digest(c, dup, d);
                var bad = new[] { "bones", "normals", "bust" }.Where(k => d[0] == null || !JToken.DeepEquals(d[0][k], edited?[k])).ToList();
                c.Check(edited != null && d[0] != null && bad.Count == 0,
                    "a fresh build of the documents (the duplicate) has the same body digest bones, normals and bust (the extrapolation rule)",
                    bad.Count == 0 ? "" : string.Join("; ", bad.Select(k => k + " (" + U4FirstLine((string)edited?["texts"]?[k] ?? "", (string)d[0]?["texts"]?[k] ?? "") + ")")));
                c.Info($"UI5: duplicate vs edited: materials {(JToken.DeepEquals(d[0]?["materials"], edited?["materials"]) ? "equal" : "differ")}, creators {(JToken.DeepEquals(d[0]?["creators"], edited?["creators"]) ? "equal" : "differ")}");
            }
            yield return c.Call(new { cmd = "ui.action", action = "edit.undo" });
            yield return c.Frames(4);
            c.Check(U4Undo(ui) == u && ui.Model.Find(dup) == null, $"the duplicate undone ({U4Undo(ui) - u} entries left, node {(ui.Model.Find(dup) == null ? "gone" : "still there")})");
        }
    }
}
