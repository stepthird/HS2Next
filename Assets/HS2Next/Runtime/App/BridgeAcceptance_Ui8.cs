// UI8 of the bridge acceptance (Phase 4.5 integrator T): the Accessories page of
// the Maker's Edit tab driven through the bridge's ui.* commands in Editor play (BridgeTest.RunPlayUi after UI7, or
// BridgeTest.RunPlayUi8 alone) and in the app player (-bridgeAccept with -ui). Without a running UI the case is skipped with a
// warning (ui.state enabled false).
// What is read how: the UI facts (a row or section shown, enabled, a dialog open, the handles) through ui.query and ui.state; the
// documents through chara.edit.get; the history through `history`; the accessory facts, anchors, transform sets, materials and
// chains through chara.edit.accessories (the item ref is `id`, the entry id `accessory`, the stored colours `colorInfo`, what
// «初期カラー» writes `colourDefaults`, the prefab's raw values `defaults`, the slot object's parent `anchor`); the last flush through
// chara.edit.info; the accessories digest through chara.edit.digest {parts: accessories}; the visibility through chara.accessories.
// Actions: the element is found by its name in the app's UI tree, revealed (scroll views scrolled to it, a toast over it waited
// for, as UI3–UI7) and clicked, typed or dragged through ui.input; list cells are found inside their list. Numbers only; nothing
// is captured. Slots are the 0-based build slots of the bridge; the page shows slot + 1 («06» = slot 5).
//   1 file.new, add.female (BR-Chan), Edit › Accessories (tab-edit, edit-tree-accessories): the slot list shows her worn slots
//     with their card slot numbers (the rows' badges = chara.edit.accessories' slots); documents and digest = the "step 1" state.
//   2 «＋» to her lowest free slot (01–05 are worn: «06»), type «眼鏡», 353#0 «スクエア» → one entry "Add accessory"; the slot list
//     shows «06»; facts: alpha renderers, set 1; colour 4 = the prefab default (alpha 0.102).
//   3 colour 4's alpha to 0 on the picker's alpha strip → one entry, the alpha renderers inactive; the strip's middle (0.5) →
//     active; «ツヤ» of colour 4 typed 30 → stored 0.3, `_Glossiness4` 0.3 where the shader declares it.
//   4 «質感» of colour 1 typed 90, then its ↺ → the prefab's defMetallicPower01 ("Reset value"); «初期カラー» → the defaults of the
//     used areas and the alpha colour, one entry.
//   5 the parent «額» (acc-parent-N_Hitai) → one entry, anchor N_Hitai, the flush ≤ 30 ms (a reload); «初期親» → N_Megane.
//   6 set 1: position x typed 12.34 → 12.3, 400 → 100; rotation y 45, then 359.6 → 0 (HS2's field text then the wrap, C-W45-1),
//     45 again, −30 → 0 (HS2's typed range 0..360); scale x 0 → 0.01; the normal rate, a held «＋» on position y for 0.5 s → one
//     entry of whole steps; ↺ x → 0 ("Reset value"); «初期化» → identity.
//   7 handles: the set 1 marker in ui.state gizmo.handles; a click selects it; a Y-arrow drag → one entry "Edit accessory",
//     position y on the 0.1 grid, x / z kept; the rotate tool, an X-ring drag → rotation x an integer in [0, 360).
//   8 «＋» to the next free slot («07»), type «頭», 351#40 «お団子髪» (hair-type) → the hair colour section instead of the colour
//     section; colours 1–4, smoothness, metallic = the back hair's; materials `Shader Forge/hair_07` with the row's TexD; colour 1
//     set apart on the picker, then «髪の毛の色を取得» «前髪» → the front hair's colours, one entry.
//   9 the Hair page: draw type «タイプ02» → the hair-type slot's materials hair_08 with TexC; «タイプ01» → hair_07 with TexD.
//  10 Accessories › «07»: «滑らかさ» typed 80 → stored 0.8, `_Smoothness` 0.8 on the first material; «揺らさない» on → stored, the
//     slot's chains off.
//  11 «06»'s type «耳» → item = row 0 of 352 («スター»), parent N_Earring_L, both sets identity, colours = the defaults, one entry.
//  12 the copy section (source «06»): «左右の親を変更» on, target «10», «枠へコピー…» → «10» = «スター» under N_Earring_R; rotation y
//     of «06» 30, «調整①を左右反転» to «10» → its rotation y 210; «複製» → the lowest free slot.
//  13 «なし» on «10» → the prompt, OK → removed, one entry "Remove accessory", its state.accessories key gone.
//  14 the State tab: each accessory row labelled with its build slot.
//  15 edit.undo back to step 1 (documents byte-identical, the accessories digest equal incl. objects), edit.redo to the end.
//  16 file.saveAs → file.new → file.open: documents and the accessories digest equal to the end state.
//  17 add.male (HERO107), Accessories: its worn slots; «＋» to its lowest free slot with 351#202 «袋頭巾» (Parent "null") → the
//     head-weighted note; «帽子» (N_Head_top) picked → stored, the object stays on the head bones (anchorUsed "head bones").
// Every step checks the history entry count and label and the document values; no chara.editFailed may be logged; the flushes'
// times are reported.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.App
{
    public static partial class BridgeAcceptance
    {
        const string U8EditAccessory = OpLabels.EditAccessory;
        const string U8Square = "illusion.hs2#0", U8HairBun = "illusion.hs2#40", U8Bag = "illusion.hs2#202";
        static readonly string[] U8DigestSections = { "materials", "objects" };

        sealed class U8State
        {
            public U4State S4;
            public UiHost Host => S4.Host;
            public UiContext Ui => S4.Ui;
            public string F, M;
            public int U1;
            public string Docs1, DocsEnd;
            public JObject Dig1, DigEnd;
            public int EditFailed;
            public int SA = -1, SH = -1, ST = -1;
            public string AA, AH;
        }

        public static IEnumerator Ui8(AcceptanceCtx c)
        {
            c.Id = "UI8";
            yield return c.Call(new { cmd = "ui.state" });
            if (c.Last == null || !c.Last.Ok || (bool?)c.J["enabled"] != true)
            {
                c.Warn("UI8 skipped: the Studio UI is not running (start with -ui; " + AcceptanceCtx.Short(c.Last?.ToString()) + ")");
                yield break;
            }
            var s = new U8State { S4 = new U4State { Host = UiHost.Current } };
            s.S4.Ui = s.S4.Host?.Context;
            if (!c.Check(s.Ui != null, "UiHost in the app's process")) yield break;
            Application.LogCallback onLog = (m, st, t) => { if (m != null && m.Contains("chara.editFailed")) s.EditFailed++; };
            Application.logMessageReceived += onLog;
            var tool0 = s.Ui.Tool;
            var total = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                yield return U8Female(c, s);
                c.Info($"UI8: female part {total.Elapsed.TotalSeconds:F1} s");
                if (s.F != null)
                {
                    total.Restart();
                    yield return U8Male(c, s);
                    c.Info($"UI8: male part {total.Elapsed.TotalSeconds:F1} s");
                }
            }
            finally
            {
                Application.logMessageReceived -= onLog;
                if (s.Ui != null) { s.Ui.SubSelection = null; s.Ui.Tool = tool0; }
            }
            c.Id = "UI8";
            c.Check(s.EditFailed == 0, $"no chara.editFailed logged during UI8 ({s.EditFailed})");
        }

        // ---------------------------------------------------------------- the female part (steps 1–16)

        static IEnumerator U8Female(AcceptanceCtx c, U8State s)
        {
            var ui = s.Ui;
            var e1 = new JObject[1];
            var n1 = new int[1];
            var v1 = new JObject[1];
            var a1 = new JObject[1];
            var ok = new bool[1];

            // ---- 1: a new scene with BR-Chan, Edit › Accessories
            c.Id = "UI8.1";
            yield return c.Call(new { cmd = "ui.action", action = "file.new", args = new { force = true } });
            c.CheckOk("ui.action file.new {force}");
            string card = Path.Combine(AppCore.Current.Paths.Hs2CharaDir(1) ?? "", c.FemaleRel ?? "");
            yield return c.Call(new { cmd = "ui.action", action = "add.female", args = new { path = card } }, 180);
            string id = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && !string.IsNullOrEmpty(id), "ui.action add.female {path} → a new character", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            yield return U5WaitBuilt(c, ui, id);
            if (!c.Check(ui.Runtime.FindChara(id)?.State == RealizationState.Built, "the character is built")) yield break;
            yield return U8Open(c, s, id, ok);
            if (!ok[0]) yield break;
            var all = new JObject[1];
            yield return U8Reply(c, id, all);
            var worn = U8Slots(all[0]).Select(x => (int)x["slot"]).ToList();
            var badBadges = new List<string>();
            foreach (int sl in worn)
            {
                var row = U8El(s, "acc-slot-" + U8NN(sl));
                string badge = row?.Q<Label>("badge")?.text;
                if (badge != U8NN(sl)) badBadges.Add(U8NN(sl) + " → " + (badge ?? "no row"));
            }
            c.Check(worn.Count > 0 && badBadges.Count == 0, $"the slot list shows the worn slots with their card slot numbers ({string.Join(" ", worn.Select(U8NN))})", string.Join(", ", badBadges));
            yield return U7Q(c, "acc-slot-add", e1);
            c.Check(U7Vis(e1[0]), "the «＋» row (acc-slot-add)");
            c.Info("UI8: BR-Chan's slots " + string.Join("; ", U8Slots(all[0]).Select(x => U8NN((int)x["slot"]) + " " + x["type"] + ":" + x["id"] + " parent " + x["parentKey"])));
            s.F = id;
            yield return U7Undo(c, n1);
            s.U1 = n1[0];
            var docs = new string[1];
            yield return U4Docs(c, id, docs);
            s.Docs1 = docs[0];
            var dig = new JObject[1];
            yield return U8Digest(c, id, dig);
            s.Dig1 = dig[0];
            if (!c.Check(s.Docs1 != null && s.Dig1 != null, "chara.edit.get and chara.edit.digest {parts: accessories} of step 1", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            var free = (all[0]?["freeSlots"] as JArray)?.Select(x => (int)x).ToList() ?? new List<int>();
            s.SA = free.Count > 0 ? free[0] : -1;
            if (s.SA != 4) c.Info($"UI8: BR-Chan's lowest free slot is {U8NN(s.SA)} (the spec's step 2 named «05»; slots 01–05 are worn, so «06» is expected)");
            c.Check(s.SA == 5, $"the lowest free slot is «06» (slot {s.SA})");
            if (s.SA < 0) yield break;

            // ---- 2: «＋» to «06», type «眼鏡», 353#0 «スクエア»
            c.Id = "UI8.2";
            {
                var na = new string[1];
                yield return U8Add(c, s, id, s.SA, 353, U8Square, "«スクエア» (353#0)", na);
                s.AA = na[0];
                if (s.AA == null) yield break;
                yield return U8Open(c, s, id, ok);
                yield return U7Q(c, "acc-slot-" + U8NN(s.SA), e1);
                var nameEl = U8El(s, "acc-slot-" + U8NN(s.SA))?.Q<Label>("name");
                c.Check(U7Vis(e1[0]) && U8El(s, "acc-slot-" + U8NN(s.SA))?.Q<Label>("badge")?.text == U8NN(s.SA), $"the slot list shows «{U8NN(s.SA)} {nameEl?.text}»");
                yield return U8Acc(c, id, s.SA, a1);
                var fx = a1[0]?["facts"];
                c.Check((int?)fx?["alphaRenderers"] > 0 && (bool?)fx?["move1"] == true, $"facts: alpha renderers {(int?)fx?["alphaRenderers"]}, set 1 {(bool?)fx?["move1"]}");
                var c4 = a1[0]?["colorInfo"]?[3]?["color"] as JArray;
                var d4 = a1[0]?["colourDefaults"]?[3]?["color"] as JArray;
                c.Check(c4 != null && d4 != null && U7Same(c4, d4) && Math.Abs((double)c4[3] - 0.102) < 1e-3, $"colour 4 = the prefab default ({U5T(c4)}; alpha 0.102)");
                yield return c.Call(new { cmd = "chara.accessories", key = id });
                c.Check((c.J["accessories"] as JObject)?[s.AA] != null, "state.accessories holds the new accessory (chara.accessories)");
            }

            // ---- 3: colour 4's alpha, «ツヤ» of colour 4
            c.Id = "UI8.3";
            yield return U8Select(c, s, id, s.SA, ok);
            if (!ok[0]) yield break;
            {
                yield return U8PickAlpha(c, s, id, s.AA, "colors[3].color", 0, "colour 4");
                yield return U7Get(c, id, new[] { U8Get(s.AA, "colors") }, v1);
                var col = v1[0]?[U8Get(s.AA, "colors")]?[3]?["color"] as JArray;
                c.Check(col != null && Math.Abs((double)col[3]) < 1e-6, $"alpha dragged below the strip → stored alpha 0 ({U5T(col)})");
                yield return U8Acc(c, id, s.SA, a1);
                var al = U8Mats(a1[0], "alpha");
                c.Check(al.Count > 0 && al.All(m => (bool?)m["active"] == false), $"alpha 0: the alpha renderers inactive ({string.Join(", ", al.Select(m => m["renderer"] + " " + m["active"]))})");
                yield return U8PickAlpha(c, s, id, s.AA, "colors[3].color", 0.5, "colour 4");
                yield return U7Get(c, id, new[] { U8Get(s.AA, "colors") }, v1);
                col = v1[0]?[U8Get(s.AA, "colors")]?[3]?["color"] as JArray;
                c.Check(col != null && Math.Abs((double)col[3] - 0.5) < 0.02, $"the strip's middle → stored alpha 0.5 ({U5T(col)})");
                yield return U8Acc(c, id, s.SA, a1);
                al = U8Mats(a1[0], "alpha");
                c.Check(al.Count > 0 && al.All(m => (bool?)m["active"] == true), $"alpha 0.5: the alpha renderers active ({string.Join(", ", al.Select(m => m["renderer"] + " " + m["active"]))})");
                yield return U8TypeRow(c, s, id, U8Field(s.AA, "colors[3].gloss"), "30", U8EditAccessory, "«ツヤ» of colour 4");
                yield return U7Get(c, id, new[] { U8Get(s.AA, "colors") }, v1);
                double g4 = (double?)v1[0]?[U8Get(s.AA, "colors")]?[3]?["gloss"] ?? double.NaN;
                c.Check(Math.Abs(g4 - 0.3) < 1e-6, $"typed 30 → colors[3].gloss 0.3 ({g4})");
                yield return U8Acc(c, id, s.SA, a1);
                var gl = U8Mats(a1[0], "alpha").Where(m => (int?)m["slot"] == 0).Select(m => m["props"]?["_Glossiness4"]).ToList();
                if (gl.Count > 0 && gl.All(x => x != null && x.Type != JTokenType.Null))
                    c.Check(gl.All(x => Math.Abs((double)x - 0.3) < 1e-6), $"_Glossiness4 0.3 on the alpha renderers' first material ({string.Join(" ", gl.Select(U5T))})");
                else c.Warn($"UI8.3: the alpha renderers' shader declares no _Glossiness4 ({string.Join(" ", gl.Select(U5T))}); the stored value is checked (MakerAccessoriesTest XT1 checks the material)");
                yield return c.Call(new { cmd = "chara.edit.info", key = id });
                U8Flush(c, c.J, "the typed gloss of colour 4");
            }

            // ---- 4: ↺ «質感» of colour 1, «初期カラー»
            c.Id = "UI8.4";
            {
                yield return U8Acc(c, id, s.SA, a1);
                var use = (a1[0]?["facts"]?["useColor"] as JArray)?.Select(x => (bool)x).ToList() ?? new List<bool>();
                int area = use.FindIndex(x => x);
                if (area < 0) c.Warn("UI8.4: 353#0 has no colour area 1–3; the ↺ of «質感» is taken on colour 4");
                int k = area < 0 ? 3 : area;
                if (area != 0) c.Info($"UI8: «質感» of colour {k + 1} (the first shown area)");
                double want = (double?)a1[0]?["defaults"]?[k]?["metallic"] ?? double.NaN;
                yield return U8TypeRow(c, s, id, U8Field(s.AA, "colors[" + k + "].metallic"), "90", U8EditAccessory, $"«質感» of colour {k + 1}");
                var row = U8El(s, U8Field(s.AA, "colors[" + k + "].metallic"));
                yield return U7Undo(c, n1);
                int u = n1[0];
                yield return U4Click(c, s.S4, row?.Q("reset"), $"«質感» {k + 1}'s ↺");
                yield return c.Frames(3);
                yield return U7Entry(c, u, OpLabels.ResetValue, "↺ «質感»");
                yield return U7Get(c, id, new[] { U8Get(s.AA, "colors") }, v1);
                double m = (double?)v1[0]?[U8Get(s.AA, "colors")]?[k]?["metallic"] ?? double.NaN;
                c.Check(!double.IsNaN(want) && Math.Abs(m - want) < 1e-6, $"↺ → the prefab's defMetallicPower0{k + 1} {want} ({m})");
                yield return U7Undo(c, n1);
                u = n1[0];
                yield return U8Click(c, s, "acc-default-colours-" + U8NN(s.SA), "«初期カラー»");
                yield return c.Frames(3);
                yield return U7Entry(c, u, U8EditAccessory, "«初期カラー»");
                yield return U8Acc(c, id, s.SA, a1);
                var bad = new List<string>();
                int alpha = (int?)a1[0]?["facts"]?["alphaRenderers"] ?? 0;
                for (int i = 0; i < 4; i++)
                {
                    bool used = i < 3 ? (i < use.Count && use[i]) : alpha > 0;
                    if (!used) continue;
                    var ci = a1[0]?["colorInfo"]?[i];
                    var cd = a1[0]?["colourDefaults"]?[i];
                    if (!U7Same(ci?["color"], cd?["color"]) || !U7Same(ci?["glossPower"], cd?["gloss"]) || !U7Same(ci?["metallicPower"], cd?["metallic"]))
                        bad.Add($"colour {i + 1}: {U5T(ci)} vs {U5T(cd)}");
                }
                c.Check(bad.Count == 0, "«初期カラー» → the used areas and the alpha colour = the prefab defaults (colourDefaults)", string.Join("; ", bad));
            }

            // ---- 5: the parent «額», «初期親»
            c.Id = "UI8.5";
            {
                yield return U8Pick(c, s, id, "acc-parent-N_Hitai", "«額» (acc-parent-N_Hitai)");
                yield return U8WaitAcc(c, id, s.SA, x => (string)x?["anchor"]?["name"] == "N_Hitai", 120, a1);
                c.Check((string)a1[0]?["anchor"]?["name"] == "N_Hitai", $"anchor.name = N_Hitai ({(string)a1[0]?["anchor"]?["name"]}; stored parent {(string)a1[0]?["parent"]})");
                yield return c.Call(new { cmd = "chara.edit.info", key = id });
                double ms = (double?)c.J["lastFlush"]?["ms"] ?? double.NaN;
                U8Flush(c, c.J, "the parent pick (a reload)");
                c.Check(!double.IsNaN(ms) && ms <= 30, $"the parent change's flush ≤ 30 ms ({ms:0.00} ms)");
                yield return U8Pick(c, s, id, "acc-parent-default", "«初期親» (acc-parent-default)");
                yield return U8WaitAcc(c, id, s.SA, x => (string)x?["anchor"]?["name"] == "N_Megane", 120, a1);
                c.Check((string)a1[0]?["anchor"]?["name"] == "N_Megane", $"«初期親» → N_Megane ({(string)a1[0]?["anchor"]?["name"]}; stored «{(string)a1[0]?["parent"]}»)");
            }

            // ---- 6: the rows of set 1
            c.Id = "UI8.6";
            {
                string pos = "moves[0].position", rot = "moves[0].rotation", scl = "moves[0].scale";
                yield return U8TypeVec(c, s, id, s.AA, pos, 0, "12.34", true, "position x");
                yield return U8Vec(c, id, s.AA, pos, v1);
                c.Check(U8C(v1[0], 0, 12.3), $"position x typed 12.34 → 12.3 ({U5T(v1[0]?["v"])})");
                yield return U8TypeVec(c, s, id, s.AA, pos, 0, "400", true, "position x");
                yield return U8Vec(c, id, s.AA, pos, v1);
                c.Check(U8C(v1[0], 0, 100), $"position x typed 400 → 100 (the limit) ({U5T(v1[0]?["v"])})");
                yield return U8TypeVec(c, s, id, s.AA, rot, 1, "45", true, "rotation y");
                yield return U8TypeVec(c, s, id, s.AA, rot, 1, "359.6", true, "rotation y");
                yield return U8Vec(c, id, s.AA, rot, v1);
                c.Check(U8C(v1[0], 1, 0), $"rotation y typed 359.6 → 0 (HS2's field text 360, wrapped; C-W45-1, XH5) ({U5T(v1[0]?["v"])})");
                yield return U8TypeVec(c, s, id, s.AA, rot, 1, "45", true, "rotation y");
                yield return U8TypeVec(c, s, id, s.AA, rot, 1, "-30", true, "rotation y");
                yield return U8Vec(c, id, s.AA, rot, v1);
                c.Check(U8C(v1[0], 1, 0), $"rotation y typed −30 → 0 (HS2's typed range 0..360) ({U5T(v1[0]?["v"])})");
                yield return U8TypeVec(c, s, id, s.AA, scl, 0, "0", true, "scale x");
                yield return U8Vec(c, id, s.AA, scl, v1);
                c.Check(U8C(v1[0], 0, 0.01), $"scale x typed 0 → 0.01 ({U5T(v1[0]?["v"])})");
                // the normal rate, then a held «＋» on position y
                yield return U8Click(c, s, "acc-rate-" + U8NN(s.SA) + "-1-normal", "the normal step rate of set 1");
                yield return c.Frames(2);
                yield return U8Vec(c, id, s.AA, pos, v1);
                double y0 = (double?)(v1[0]?["v"] as JArray)?[1] ?? double.NaN;
                var plus = U8El(s, U8Field(s.AA, pos))?.Q(U8Field(s.AA, pos) + "#1")?.Q("plus");
                if (c.Check(plus != null, "the «＋» button of position y"))
                {
                    var p = new Vector2[1];
                    yield return U4Reveal(c, s.S4, plus, "position y's «＋»", p);
                    yield return U7Undo(c, n1);
                    int u = n1[0];
                    yield return c.Call(new { cmd = "ui.input", frames = 1, events = new object[] { new { type = "pointer", phase = "down", x = p[0].x, y = p[0].y, button = 0 } } });
                    c.CheckOk("ui.input pointer down on position y's «＋»");
                    float t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < 0.55f) yield return c.Frames(1);
                    yield return c.Call(new { cmd = "ui.input", frames = 2, events = new object[] { new { type = "pointer", phase = "up", x = p[0].x, y = p[0].y, button = 0 } } });
                    c.CheckOk("ui.input pointer up after 0.5 s");
                    yield return c.Frames(3);
                    yield return U7Entry(c, u, U8EditAccessory, "the held «＋» on position y");
                    yield return U8Vec(c, id, s.AA, pos, v1);
                    double y1 = (double?)(v1[0]?["v"] as JArray)?[1] ?? double.NaN;
                    double steps = y1 - y0;
                    c.Check(steps >= 2 - 1e-4 && Math.Abs(steps - Math.Round(steps)) < 1e-4, $"held 0.5 s: position y {y0} → {y1} ({steps:0.###} steps of 1 at the normal rate: one now, repeats after 0.3 s every 0.05 s)");
                }
                var reset = U8El(s, U8Field(s.AA, pos))?.Q(U8Field(s.AA, pos) + "#0")?.Q("reset");
                yield return U7Undo(c, n1);
                int ur = n1[0];
                yield return U4Click(c, s.S4, reset, "position x's ↺");
                yield return c.Frames(3);
                yield return U7Entry(c, ur, OpLabels.ResetValue, "↺ position x");
                yield return U8Vec(c, id, s.AA, pos, v1);
                c.Check(U8C(v1[0], 0, 0), $"↺ x → 0 ({U5T(v1[0]?["v"])})");
                yield return U7Undo(c, n1);
                int ui0 = n1[0];
                yield return U8Click(c, s, "acc-reset-move-" + U8NN(s.SA) + "-1", "«初期化» of set 1");
                yield return c.Frames(3);
                yield return U7Entry(c, ui0, U8EditAccessory, "«初期化»");
                yield return U7Get(c, id, new[] { U8Get(s.AA, "moves") }, v1);
                var mv = v1[0]?[U8Get(s.AA, "moves")]?[0];
                c.Check(U8Ident(mv), $"«初期化» → set 1 identity ({U5T(mv)})");
            }

            // ---- 7: the set handles
            c.Id = "UI8.7";
            yield return U8Handles(c, s, id);

            // ---- 8: «＋» 351#40 «お団子髪» (hair-type) to the next free slot
            c.Id = "UI8.8";
            {
                yield return U8Reply(c, id, all);
                free = (all[0]?["freeSlots"] as JArray)?.Select(x => (int)x).ToList() ?? new List<int>();
                s.SH = free.Count > 0 ? free[0] : -1;
                c.Check(s.SH == 6, $"the next free slot is «07» (slot {s.SH})");
                if (s.SH < 0) yield break;
                var na = new string[1];
                yield return U8Add(c, s, id, s.SH, 351, U8HairBun, "«お団子髪» (351#40)", na);
                s.AH = na[0];
                if (s.AH == null) yield break;
                yield return U8Select(c, s, id, s.SH, ok);
                var left = new List<string>();
                yield return U7WaitRows(c, new[] { "edit-section-" + InspectorSchemas.AccessorySectionId(s.SH, "hairColour") }, true, 300, left);
                yield return U7Q(c, "edit-section-" + InspectorSchemas.AccessorySectionId(s.SH, "colour"), e1);
                c.Check(left.Count == 0 && !U7Vis(e1[0]), "the hair colour section instead of the colour section");
                yield return U8Acc(c, id, s.SH, a1);
                c.Check((bool?)a1[0]?["facts"]?["typeHair"] == true, "351#40 is hair-type (facts.typeHair)");
                yield return U8HairColours(c, id, s.AH, "back", "colours = the back hair's (non-hair → hair-type)");
                yield return U8HairShader(c, a1[0], "HS2Next/Shader Forge/hair_07", "TexD", "at the add (draw type 0)");
                // colour 1 set apart on the picker (BR-Chan's front hair shares the back hair's colours), then the fetch
                yield return U8ColourDrag(c, s, id, s.AH, "colors[0].color", true, "the hair colour «基本カラー»");
                yield return U7Undo(c, n1);
                int u = n1[0];
                yield return U8Click(c, s, "acc-hair-fetch-" + U8NN(s.SH) + "-front", "«髪の毛の色を取得» «前髪»");
                yield return c.Frames(3);
                yield return U7Entry(c, u, U8EditAccessory, "«髪の毛の色を取得» «前髪»");
                yield return U8HairColours(c, id, s.AH, "front", "«前髪» → colours = the front hair's");
            }

            // ---- 9: the Hair page's draw type
            c.Id = "UI8.9";
            {
                yield return U8Click(c, s, "edit-tree-hair", "the Hair node");
                yield return U7WaitShown(c, "field-appearance.chara.hair.shaderType", true, 120, e1);
                var choice = U8El(s, "field-appearance.chara.hair.shaderType");
                if (c.Check(choice != null, "the draw type row (field-…hair.shaderType)"))
                {
                    yield return U7Undo(c, n1);
                    int u = n1[0];
                    yield return U4Click(c, s.S4, choice.Q<Button>(choice.name + "-1"), "«タイプ02»");
                    yield return c.Frames(3);
                    yield return U7Entry(c, u, OpLabels.EditHair, "«タイプ02»");
                    yield return U8WaitAcc(c, id, s.SH, x => U8Mats(x, "normal").Any(m => ((string)m["shader"] ?? "").EndsWith("hair_08", StringComparison.Ordinal)), 120, a1);
                    yield return U8HairShader(c, a1[0], "HS2Next/Shader Forge/hair_08", "TexC", "after «タイプ02»");
                    yield return c.Call(new { cmd = "chara.edit.info", key = id });
                    U8Flush(c, c.J, "the draw type switch");
                    choice = U8El(s, "field-appearance.chara.hair.shaderType");
                    yield return U7Undo(c, n1);
                    u = n1[0];
                    yield return U4Click(c, s.S4, choice?.Q<Button>(choice.name + "-0"), "«タイプ01»");
                    yield return c.Frames(3);
                    yield return U7Entry(c, u, OpLabels.EditHair, "«タイプ01»");
                    yield return U8WaitAcc(c, id, s.SH, x => U8Mats(x, "normal").Any(m => ((string)m["shader"] ?? "").EndsWith("hair_07", StringComparison.Ordinal)), 120, a1);
                    yield return U8HairShader(c, a1[0], "HS2Next/Shader Forge/hair_07", "TexD", "back at «タイプ01»");
                }
            }

            // ---- 10: «滑らかさ» and «揺らさない» of the hair-type slot
            c.Id = "UI8.10";
            yield return U8Select(c, s, id, s.SH, ok);
            if (!ok[0]) yield break;
            {
                yield return U8TypeRow(c, s, id, U8Field(s.AH, "colors[0].smoothness", true), "80", U8EditAccessory, "«滑らかさ»");
                yield return U7Get(c, id, new[] { U8Get(s.AH, "colors") }, v1);
                double sm = (double?)v1[0]?[U8Get(s.AH, "colors")]?[0]?["smoothness"] ?? double.NaN;
                c.Check(Math.Abs(sm - 0.8) < 1e-6, $"typed 80 → colors[0].smoothness 0.8 ({sm})");
                yield return U8Acc(c, id, s.SH, a1);
                var first = U8Mats(a1[0], "normal").Where(m => (int?)m["slot"] == 0).Select(m => m["props"]?["_Smoothness"]).ToList();
                c.Check(first.Count > 0 && first.All(x => x != null && x.Type != JTokenType.Null && Math.Abs((double)x - 0.8) < 1e-6),
                    $"_Smoothness 0.8 on the first material of every hair renderer ({string.Join(" ", first.Select(U5T))})");
                var tg = U8El(s, U8Field(s.AH, "noShake"))?.Q<Toggle>("toggle");
                yield return U7Undo(c, n1);
                int u = n1[0];
                yield return U4Click(c, s.S4, tg?.Q("unity-checkmark") ?? tg, "«揺らさない»");
                yield return c.Frames(3);
                yield return U7Entry(c, u, U8EditAccessory, "«揺らさない» on");
                yield return U7Get(c, id, new[] { U8Get(s.AH, "noShake") }, v1);
                c.Check((bool?)v1[0]?[U8Get(s.AH, "noShake")] == true, "noShake stored true");
                yield return U8WaitAcc(c, id, s.SH, x => (bool?)x?["noShake"] == true, 60, a1);
                var chains = a1[0]?["chains"] as JArray;
                if (chains != null && chains.Count > 0)
                    c.Check(chains.All(ch => (bool?)ch["enabled"] == false), $"the slot's chains off ({string.Join(", ", chains.Select(ch => ch["root"] + " " + ch["enabled"]))})");
                else c.Info($"UI8: 351#40 carries no dynamic-bone chain ({U5T(chains)}): «揺らさない» is checked by the stored flag and the reply's noShake");
            }

            // ---- 11: «06»'s type «耳»
            c.Id = "UI8.11";
            yield return U8Select(c, s, id, s.SA, ok);
            if (!ok[0]) yield break;
            {
                yield return U7Undo(c, n1);
                int u = n1[0];
                yield return U8Click(c, s, "acc-type-" + U8NN(s.SA) + "-2", "the type «耳» (acc-type-" + U8NN(s.SA) + "-2)");
                yield return U8WaitAcc(c, id, s.SA, x => (int?)x?["type"] == 352 && (bool?)x?["loaded"] == true, 300, a1);
                yield return c.Frames(6);
                yield return U7Entry(c, u, OpLabels.ChangeItem, "the type change");
                var x1 = a1[0];
                c.Check((int?)x1?["type"] == 352 && (string)x1?["id"] == "illusion.hs2#0", $"type 352, item = row 0 «スター» ({(string)x1?["id"]})");
                c.Check((string)x1?["parentKey"] == "N_Earring_L" && (string)x1?["anchor"]?["name"] == "N_Earring_L", $"parent N_Earring_L (parentKey {(string)x1?["parentKey"]}, stored «{(string)x1?["parent"]}», anchor {(string)x1?["anchor"]?["name"]})");
                var am = x1?["addMove"] as JArray;
                c.Check(am != null && am.Count == 2 && am.All(U8Ident), $"both sets identity ({U5T(am)})");
                var use = (x1?["facts"]?["useColor"] as JArray)?.Select(b => (bool)b).ToList() ?? new List<bool>();
                var bad = new List<string>();
                for (int i = 0; i < 3; i++)
                    if (i < use.Count && use[i] && !U7Same(x1?["colorInfo"]?[i]?["color"], x1?["colourDefaults"]?[i]?["color"])) bad.Add("colour " + (i + 1));
                c.Check(bad.Count == 0, "colours = the prefab defaults (used areas)", string.Join(", ", bad));
            }

            // ---- 12: the copy section
            c.Id = "UI8.12";
            {
                yield return U8Reply(c, id, all);
                s.ST = U8Slots(all[0]).Any(x => (int)x["slot"] == 9) ? (all[0]?["freeSlots"] as JArray)?.Select(x => (int)x).FirstOrDefault(x => x >= 9) ?? -1 : 9;
                if (s.ST != 9) c.Info($"UI8: slot «10» is worn; the copy target is {U8NN(s.ST)}");
                yield return U8Select(c, s, id, s.SA, ok);
                if (!ok[0]) yield break;
                yield return U7Q(c, "acc-copy-mirror", e1);
                if ((bool?)e1[0]?["value"] != true)
                {
                    var tgl = U8El(s, "acc-copy-mirror") as Toggle;
                    yield return U4Click(c, s.S4, tgl?.Q("unity-checkmark") ?? tgl, "«左右の親を変更» (acc-copy-mirror)");
                    yield return c.Frames(2);
                    yield return U7Q(c, "acc-copy-mirror", e1);
                }
                c.Check((bool?)e1[0]?["value"] == true, "«左右の親を変更» on");
                yield return U8Click(c, s, "acc-target-slot-" + U8NN(s.ST), "the target slot «" + U8NN(s.ST) + "»");
                yield return c.Frames(2);
                yield return U7Q(c, "acc-copy-copyToSlot", e1);
                c.Check(U7En(e1[0]), "«枠へコピー…» enabled for an empty target");
                yield return U7Undo(c, n1);
                int u = n1[0];
                yield return U8Click(c, s, "acc-copy-copyToSlot", "«枠へコピー…»");
                yield return U8WaitAcc(c, id, s.ST, x => (bool?)x?["loaded"] == true && (string)x?["anchor"]?["name"] != null, 300, a1);
                yield return c.Frames(6);
                yield return U7Entry(c, u, OpLabels.CopyAccessory, "«枠へコピー…»");
                c.Check((string)a1[0]?["id"] == "illusion.hs2#0" && (string)a1[0]?["parent"] == "N_Earring_R" && (string)a1[0]?["anchor"]?["name"] == "N_Earring_R",
                    $"«{U8NN(s.ST)} スター» under N_Earring_R (item {(string)a1[0]?["id"]}, stored parent {(string)a1[0]?["parent"]}, anchor {(string)a1[0]?["anchor"]?["name"]})");
                // the mirror option back off; rotation y of the source 30, «調整①を左右反転» to the target
                yield return U8Select(c, s, id, s.SA, ok);
                if (!ok[0]) yield break;
                yield return U7Q(c, "acc-copy-mirror", e1);
                if ((bool?)e1[0]?["value"] == true)
                {
                    var tgl = U8El(s, "acc-copy-mirror") as Toggle;
                    yield return U4Click(c, s.S4, tgl?.Q("unity-checkmark") ?? tgl, "«左右の親を変更» off again");
                    yield return c.Frames(2);
                }
                yield return U8TypeVec(c, s, id, s.AA, "moves[0].rotation", 1, "30", true, "rotation y of «" + U8NN(s.SA) + "»");
                yield return U8Click(c, s, "acc-target-slot-" + U8NN(s.ST), "the target slot «" + U8NN(s.ST) + "»");
                yield return c.Frames(2);
                yield return U7Q(c, "acc-copy-copyMove1LeftRight", e1);
                c.Check(U7En(e1[0]), "«調整①を左右反転» enabled (both prefabs have set 1)", (string)e1[0]?["tooltip"]);
                yield return U7Undo(c, n1);
                u = n1[0];
                yield return U8Click(c, s, "acc-copy-copyMove1LeftRight", "«調整①を左右反転»");
                yield return c.Frames(3);
                yield return U7Entry(c, u, OpLabels.CopyAccessory, "«調整①を左右反転»");
                yield return U8Acc(c, id, s.ST, a1);
                var ry = (a1[0]?["addMove"]?[0]?["rotation"] as JArray)?[1];
                c.Check(ry != null && Math.Abs((double)ry - 210) < 1e-4, $"«{U8NN(s.ST)}» rotation y 210 (30 + 180) ({U5T(ry)})");
                // «複製»
                yield return U8Reply(c, id, all);
                int dupSlot = (all[0]?["freeSlots"] as JArray)?.Select(x => (int)x).FirstOrDefault() ?? -1;
                yield return U7Undo(c, n1);
                u = n1[0];
                yield return U8Click(c, s, "acc-copy-duplicate", "«複製»");
                yield return U8WaitAcc(c, id, dupSlot, x => (bool?)x?["loaded"] == true, 300, a1);
                yield return c.Frames(6);
                yield return U7Entry(c, u, OpLabels.CopyAccessory, "«複製»");
                c.Check((string)a1[0]?["id"] == "illusion.hs2#0" && (int?)a1[0]?["type"] == 352, $"«複製» → the lowest free slot «{U8NN(dupSlot)}» ({(string)a1[0]?["id"]})");
            }

            // ---- 13: «なし» on the copy target
            c.Id = "UI8.13";
            yield return U8Select(c, s, id, s.ST, ok);
            if (!ok[0]) yield break;
            {
                yield return U8Acc(c, id, s.ST, a1);
                string gone = (string)a1[0]?["accessory"];
                yield return U7Undo(c, n1);
                int u = n1[0];
                yield return U8Click(c, s, "acc-type-" + U8NN(s.ST) + "-0", "the type «なし»");
                yield return c.Frames(3);
                yield return c.Call(new { cmd = "ui.state" });
                c.Check((string)c.J["modal"] == "maker.acc.removeTitle", $"«なし» asks first (ui.state modal {(string)c.J["modal"]})");
                yield return U8Click(c, s, "dialog-button-ok", "the prompt's OK");
                for (int i = 0; i < 30; i++) yield return c.Frames(1);
                yield return U7Entry(c, u, OpLabels.RemoveAccessory, "«なし»");
                yield return U8Acc(c, id, s.ST, a1);
                c.Check((int?)a1[0]?["type"] == 350 && a1[0]?["accessory"]?.Type == JTokenType.Null, $"slot «{U8NN(s.ST)}» empty ({(int?)a1[0]?["type"]})");
                yield return c.Call(new { cmd = "chara.accessories", key = id });
                c.Check(gone != null && (c.J["accessories"] as JObject)?[gone] == null, "its state.accessories key pruned (chara.accessories)");
            }

            // ---- 14: the State tab labels
            c.Id = "UI8.14";
            {
                yield return U8Reply(c, id, all);
                yield return U8Click(c, s, "tab-state", "the State tab");
                yield return c.Frames(4);
                var labels = new List<string>();
                var missing = new List<string>();
                foreach (var x in U8Slots(all[0]))
                {
                    string acc = (string)x["accessory"], nn = U8NN((int)x["slot"]);
                    var row = U8El(s, "accessory-" + acc);
                    string txt = row?.Query<Label>().ToList().Select(l => l.text).FirstOrDefault(t => t != null && t.Contains(nn));
                    if (txt == null) missing.Add(nn + (row == null ? " (no row)" : ""));
                    else labels.Add(txt);
                }
                c.Check(missing.Count == 0 && labels.Count > 0, $"the State tab labels each accessory with its build slot ({string.Join(" | ", labels)})", string.Join(", ", missing));
                yield return U8Click(c, s, "tab-" + CharaEditTab.TabId, "the Edit tab");
                yield return c.Frames(3);
            }

            // ---- 15: undo to step 1, redo to the end
            c.Id = "UI8.15";
            yield return c.Frames(3);
            var docsE = new string[1];
            yield return U4Docs(c, id, docsE);
            s.DocsEnd = docsE[0];
            var digE = new JObject[1];
            yield return U8Digest(c, id, digE);
            s.DigEnd = digE[0];
            yield return c.Call(new { cmd = "history" });
            var undo = (c.J["undo"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
            int n15 = undo.Count - s.U1;
            c.Info($"UI8: {n15} history entries since step 1: {string.Join(" | ", undo.Take(Math.Max(0, n15)).Reverse())}");
            for (int i = 0; i < n15; i++)
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
            yield return U8Digest(c, id, dig);
            c.Check(U8SameDigest(s.Dig1, dig[0], out string d1), "undo to step 1 → the accessories digest equals step 1's (materials, objects)", d1);
            for (int i = 0; i < n15; i++)
            {
                yield return c.Call(new { cmd = "ui.action", action = "edit.redo" });
                if (!(c.Last != null && c.Last.Ok)) { c.CheckOk("ui.action edit.redo #" + (i + 1)); break; }
                yield return c.Frames(1);
            }
            yield return U5WaitBuilt(c, ui, id);
            yield return c.Frames(6);
            yield return U4Docs(c, id, docs);
            c.Check(docs[0] != null && docs[0] == s.DocsEnd, "redo to the end → the documents equal the end state", docs[0] == s.DocsEnd ? "" : U4JsonDiff(s.DocsEnd, docs[0]));
            yield return U8Digest(c, id, dig);
            c.Check(U8SameDigest(s.DigEnd, dig[0], out string d2), "redo to the end → the accessories digest equals the end state's", d2);

            // ---- 16: save as, new, open
            c.Id = "UI8.16";
            yield return c.Call(new { cmd = "ui.action", action = "file.saveAs", args = new { name = "ui8_acceptance", overwrite = true } }, 120);
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
            yield return U8Digest(c, id, dig);
            c.Check(U8SameDigest(s.DigEnd, dig[0], out string d3), "reopened: the accessories digest equals the end state's", d3);
        }

        // ---------------------------------------------------------------- step 7: the handles of set 1

        static IEnumerator U8Handles(AcceptanceCtx c, U8State s, string id)
        {
            var ui = s.Ui;
            var ok = new bool[1];
            var e1 = new JObject[1];
            var n1 = new int[1];
            var a1 = new JObject[1];
            yield return U8Select(c, s, id, s.SA, ok);
            if (!ok[0]) yield break;
            yield return U7Q(c, "edit-section-" + InspectorSchemas.AccessorySectionId(s.SA, "adjust"), e1);
            if ((bool?)e1[0]?["value"] == false)
            {
                var fo = U8El(s, "edit-section-" + InspectorSchemas.AccessorySectionId(s.SA, "adjust")) as Foldout;
                yield return U4Click(c, s.S4, fo?.Q<Toggle>(), "the Adjust section's head (opened)");
                yield return c.Frames(2);
            }
            yield return U8Acc(c, id, s.SA, a1);
            U8Frame(ui, id, a1[0]);
            yield return c.Frames(4);
            string want = StudioOps.AccessorySub(id, s.AA, 0);
            var marker = new JObject[1];
            yield return U6Handle(c, "marker", want, null, marker);
            if (!c.Check(marker[0] != null, $"ui.state gizmo.handles holds the marker of set 1 ({want})")) yield break;
            var mp = U6P(marker[0]);
            yield return c.Call(new { cmd = "ui.input", frames = 3, events = new object[] { new { type = "pointer", phase = "down", x = mp.x, y = mp.y, button = 0 }, new { type = "pointer", phase = "up", x = mp.x, y = mp.y, button = 0 } } });
            c.CheckOk("ui.input click on the marker");
            yield return c.Frames(2);
            yield return c.Call(new { cmd = "ui.state" });
            c.Check((string)c.J["sub"] == want, $"the marker pick → sub-selection {(string)c.J["sub"]} ({want})");
            yield return U8Click(c, s, "acc-handle-" + U8NN(s.SA) + "-1-move", "set 1's «移動» handle button");
            yield return c.Frames(2);
            c.Check(ui.Tool == GizmoTool.Move, "the move tool (" + ui.Tool + ")");
            var tip = new JObject[1];
            yield return U6Handle(c, "axis", want, "y", tip);
            yield return U6Handle(c, "marker", want, null, marker);
            if (c.Check(tip[0] != null && (string)tip[0]["tool"] == "move" && marker[0] != null, "the move handle's Y arrow in gizmo.handles"))
            {
                var gz = ui.Viewport.Gizmo;
                var mp0 = U6P(marker[0]);
                var tp = U6P(tip[0]);
                var dir = (tp - mp0).normalized;
                bool hitY = false;
                foreach (float f in new[] { 1f, 0.75f, 0.6f, 0.45f, 0.85f, 0.3f })
                {
                    var q = Vector2.Lerp(mp0, tp, f);
                    var h = gz.HitTest(q);
                    if (h.Kind == HS2Next.UI.Gizmos.HandleKind.MoveAxis && h.Axis == 1) { tp = q; hitY = true; break; }
                }
                c.Check(hitY, "the gizmo's hit test finds the Y arrow under the grab point");
                yield return U7Get(c, id, new[] { U8Get(s.AA, "moves") }, e1);
                var before = e1[0]?[U8Get(s.AA, "moves")]?[0]?["position"] as JArray;
                bool done = false;
                for (int attempt = 0; attempt < 2 && !done; attempt++)
                {
                    var d = attempt == 0 ? dir : -dir;
                    yield return U7Undo(c, n1);
                    int u = n1[0];
                    yield return c.Call(new
                    {
                        cmd = "ui.input", frames = 3,
                        events = new object[]
                        {
                            new { type = "pointer", phase = "down", x = tp.x, y = tp.y, button = 0 },
                            new { type = "pointer", phase = "move", x = tp.x + d.x * 8, y = tp.y + d.y * 8, button = 0 },
                            new { type = "pointer", phase = "move", x = tp.x + d.x * 16, y = tp.y + d.y * 16, button = 0 },
                            new { type = "pointer", phase = "move", x = tp.x + d.x * 24, y = tp.y + d.y * 24, button = 0 },
                            new { type = "pointer", phase = "up", x = tp.x + d.x * 24, y = tp.y + d.y * 24, button = 0 },
                        },
                    });
                    c.CheckOk("ui.input drag on the Y arrow" + (attempt > 0 ? " (reversed)" : ""));
                    yield return c.Frames(3);
                    yield return U7Get(c, id, new[] { U8Get(s.AA, "moves") }, e1);
                    var after = e1[0]?[U8Get(s.AA, "moves")]?[0]?["position"] as JArray;
                    if (attempt == 0 && after != null && before != null && Math.Abs((double)after[1] - (double)before[1]) < 1e-6)
                    {
                        c.Info($"UI8: the Y drag left position y at {after[1]} (a limit?); the reverse direction is dragged");
                        continue;
                    }
                    done = true;
                    yield return U7Entry(c, u, U8EditAccessory, "the Y-arrow drag");
                    double y = after != null ? (double)after[1] : double.NaN;
                    bool grid = Math.Abs(y * 10 - Math.Round(y * 10)) < 1e-3;
                    c.Check(after != null && before != null && Math.Abs(y - (double)before[1]) > 1e-6 && grid && y >= -100 && y <= 100
                            && Math.Abs((double)after[0] - (double)before[0]) < 1e-6 && Math.Abs((double)after[2] - (double)before[2]) < 1e-6,
                        $"position {U5T(before)} → {U5T(after)}: y on the 0.1 grid, x / z kept");
                }
                yield return c.Call(new { cmd = "chara.edit.info", key = id });
                U8Flush(c, c.J, "the handle drag");
                // the rotate tool, an X-ring drag
                yield return U8Click(c, s, "acc-handle-" + U8NN(s.SA) + "-1-rotate", "set 1's «回転» handle button");
                yield return c.Frames(3);
                c.Check(ui.Tool == GizmoTool.Rotate && ui.SubSelection == want, $"the rotate tool on set 1 ({ui.Tool}, {ui.SubSelection})");
                var ring = new JObject[1];
                yield return U6Handle(c, "axis", want, "x", ring);
                if (c.Check(ring[0] != null && (string)ring[0]["tool"] == "rotate", "the rotate handle's X ring in gizmo.handles"))
                {
                    var rp = U6P(ring[0]);
                    var hx = gz.HitTest(rp);
                    c.Check(hx.Kind == HS2Next.UI.Gizmos.HandleKind.RotateRing && hx.Axis == 0, "the X ring point hits the X ring (" + hx + ")");
                    yield return U7Get(c, id, new[] { U8Get(s.AA, "moves") }, e1);
                    var rb = e1[0]?[U8Get(s.AA, "moves")]?[0]?["rotation"] as JArray;
                    yield return U7Undo(c, n1);
                    int u = n1[0];
                    yield return c.Call(new
                    {
                        cmd = "ui.input", frames = 3,
                        events = new object[]
                        {
                            new { type = "pointer", phase = "down", x = rp.x, y = rp.y, button = 0 },
                            new { type = "pointer", phase = "move", x = rp.x + 10, y = rp.y + 10, button = 0 },
                            new { type = "pointer", phase = "move", x = rp.x + 30, y = rp.y + 30, button = 0 },
                            new { type = "pointer", phase = "up", x = rp.x + 30, y = rp.y + 30, button = 0 },
                        },
                    });
                    c.CheckOk("ui.input drag on the X ring");
                    yield return c.Frames(3);
                    yield return U7Entry(c, u, U8EditAccessory, "the X-ring drag");
                    yield return U7Get(c, id, new[] { U8Get(s.AA, "moves") }, e1);
                    var ra = e1[0]?[U8Get(s.AA, "moves")]?[0]?["rotation"] as JArray;
                    double rx = ra != null ? (double)ra[0] : double.NaN;
                    c.Check(ra != null && rb != null && rx >= 0 && rx < 360 && Math.Abs(rx - Math.Round(rx)) < 1e-4 && Math.Abs(rx - (double)rb[0]) > 1e-4
                            && Math.Abs((double)ra[1] - (double)rb[1]) < 1e-6 && Math.Abs((double)ra[2] - (double)rb[2]) < 1e-6,
                        $"rotation {U5T(rb)} → {U5T(ra)}: x an integer in [0, 360), y / z kept");
                }
            }
            ui.SubSelection = null;
            ui.Tool = GizmoTool.Move;
        }

        // ---------------------------------------------------------------- the male part (step 17)

        static IEnumerator U8Male(AcceptanceCtx c, U8State s)
        {
            var ui = s.Ui;
            var e1 = new JObject[1];
            var a1 = new JObject[1];
            var v1 = new JObject[1];
            var ok = new bool[1];
            c.Id = "UI8.17";
            string card = Path.Combine(AppCore.Current.Paths.Hs2CharaDir(0) ?? "", Ui2MaleRel ?? "");
            yield return c.Call(new { cmd = "ui.action", action = "add.male", args = new { path = card } }, 180);
            string id = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && !string.IsNullOrEmpty(id), "ui.action add.male {path} → a new character (HERO107)", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            yield return U5WaitBuilt(c, ui, id);
            if (!c.Check(ui.Runtime.FindChara(id)?.State == RealizationState.Built, "the male character is built")) yield break;
            s.M = id;
            yield return U8Open(c, s, id, ok);
            if (!ok[0]) yield break;
            var all = new JObject[1];
            yield return U8Reply(c, id, all);
            var worn = U8Slots(all[0]).Select(x => (int)x["slot"]).ToList();
            var shownRows = worn.Where(sl => U8El(s, "acc-slot-" + U8NN(sl)) != null).ToList();
            c.Check(shownRows.Count == worn.Count, $"HERO107's slot list = its worn slots ({worn.Count}: {string.Join(" ", worn.Select(U8NN))})");
            if (worn.Count == 0)
            {
                yield return U7Q(c, "acc-no-slots", e1);
                c.Check(U7Vis(e1[0]), "no worn slot: the note (acc-no-slots) and «＋»");
            }
            int slot = (all[0]?["freeSlots"] as JArray)?.Select(x => (int)x).FirstOrDefault() ?? 0;
            var na = new string[1];
            yield return U8Add(c, s, id, slot, 351, U8Bag, "«袋頭巾» (351#202)", na);
            string acc = na[0];
            if (acc == null) yield break;
            yield return U8Select(c, s, id, slot, ok);
            yield return U7WaitShown(c, "acc-parent-head-note", true, 300, e1);
            c.Check(U7Vis(e1[0]), "the parent section shows the head-weighted note (acc-parent-head-note)");
            yield return U8Acc(c, id, slot, a1);
            c.Check((bool?)a1[0]?["facts"]?["headWeighted"] == true, $"facts.headWeighted (Parent column {(string)a1[0]?["facts"]?["parentColumn"]})");
            yield return U8Pick(c, s, id, "acc-parent-N_Head_top", "«帽子» (acc-parent-N_Head_top)");
            yield return U7Get(c, id, new[] { U8Get(acc, "parent") }, v1);
            c.Check((string)v1[0]?[U8Get(acc, "parent")] == "N_Head_top", $"the picked key is stored ({(string)v1[0]?[U8Get(acc, "parent")]})");
            yield return U8WaitAcc(c, id, slot, x => (string)x?["parent"] == "N_Head_top", 120, a1);
            c.Check((string)a1[0]?["anchorUsed"] == "head bones", $"the object stays on the head bones (anchorUsed «{(string)a1[0]?["anchorUsed"]}», anchor {(string)a1[0]?["anchor"]?["name"]})");
        }

        // ---------------------------------------------------------------- helpers

        static string U8NN(int slot) => InspectorSchemas.AccessorySlotLabel(slot);

        /// <summary>The shown element of that name in the app's UI (under <paramref name="scope"/>), or null.</summary>
        static VisualElement U8El(U8State s, string name, VisualElement scope = null)
        {
            var root = scope ?? s.Host?.Root;
            return root?.Query<VisualElement>(name).Where(x => x.panel != null && x.worldBound.width > 0 && U6Shown(x)).First();
        }

        static IEnumerator U8Click(AcceptanceCtx c, U8State s, string name, string what) => U4Click(c, s.S4, U8El(s, name), what);

        /// <summary>The chara.edit.get path of an accessory member (coord.accessories["id"].member).</summary>
        static string U8Get(string acc, string member) => InspectorSchemas.AccessoryEditPath(acc, member).Substring("appearance.".Length);

        /// <summary>The row name of an accessory member (the hair colour section's rows carry "@hair").</summary>
        static string U8Field(string acc, string member, bool hair = false) => "field-" + InspectorSchemas.AccessoryEditPath(acc, member) + (hair ? "@hair" : "");

        static List<JObject> U8Slots(JObject reply) => (reply?["slots"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();

        /// <summary>The materials of a slot reply whose kind is (or contains) <paramref name="kind"/> (normal / alpha).</summary>
        static List<JObject> U8Mats(JObject slot, string kind) =>
            (slot?["materials"] as JArray)?.OfType<JObject>().Where(m => ((string)m["kind"] ?? "").Split('+').Contains(kind)).ToList() ?? new List<JObject>();

        static bool U8Ident(JToken mv)
        {
            if (!(mv is JObject o)) return false;
            bool Eq(string k, double v) => o[k] is JArray a && a.Count == 3 && a.All(x => Math.Abs((double)x - v) < 1e-6);
            return Eq("position", 0) && Eq("rotation", 0) && Eq("scale", 1);
        }

        /// <summary>Component <paramref name="i"/> of the vector of <see cref="U8Vec"/> equals <paramref name="want"/>.</summary>
        static bool U8C(JObject v, int i, double want) => v?["v"] is JArray a && a.Count == 3 && Math.Abs((double)a[i] - want) < 1e-4;

        /// <summary>A stored transform vector (chara.edit.get of the member) as {v: [x, y, z]}.</summary>
        static IEnumerator U8Vec(AcceptanceCtx c, string id, string acc, string member, JObject[] into)
        {
            var v = new JObject[1];
            yield return U7Get(c, id, new[] { U8Get(acc, member) }, v);
            into[0] = new JObject { ["v"] = v[0]?[U8Get(acc, member)]?.DeepClone() };
        }

        static IEnumerator U8Reply(AcceptanceCtx c, string id, JObject[] into)
        {
            yield return c.Call(new { cmd = "chara.edit.accessories", key = id, served = false }, 120);
            into[0] = c.Last != null && c.Last.Ok ? (JObject)c.J.DeepClone() : null;
            if (into[0] == null) c.Check(false, "chara.edit.accessories {key}", AcceptanceCtx.Short(c.Last?.ToString()));
        }

        static IEnumerator U8Acc(AcceptanceCtx c, string id, int slot, JObject[] into)
        {
            yield return c.Call(new { cmd = "chara.edit.accessories", key = id, slot, served = false }, 120);
            into[0] = c.Last != null && c.Last.Ok ? (c.J["slots"] as JArray)?.FirstOrDefault()?.DeepClone() as JObject : null;
            if (into[0] == null) c.Check(false, $"chara.edit.accessories {{key, slot: {slot}}}", AcceptanceCtx.Short(c.Last?.ToString()));
        }

        /// <summary>Polls chara.edit.accessories {slot} until <paramref name="until"/> holds (every 2 frames, up to <paramref name="frames"/>).</summary>
        static IEnumerator U8WaitAcc(AcceptanceCtx c, string id, int slot, Func<JObject, bool> until, int frames, JObject[] into)
        {
            into[0] = null;
            for (int i = 0; i < frames; i += 2)
            {
                yield return c.Call(new { cmd = "chara.edit.accessories", key = id, slot, served = false }, 120);
                into[0] = c.Last != null && c.Last.Ok ? (c.J["slots"] as JArray)?.FirstOrDefault()?.DeepClone() as JObject : null;
                if (into[0] != null && until(into[0])) yield break;
                yield return c.Frames(2);
            }
        }

        static IEnumerator U8Digest(AcceptanceCtx c, string id, JObject[] into)
        {
            yield return c.Call(new { cmd = "chara.edit.digest", key = id, parts = "accessories", text = true }, 120);
            into[0] = c.Last != null && c.Last.Ok ? (JObject)c.J.DeepClone() : null;
        }

        static bool U8SameDigest(JObject a, JObject b, out string detail)
        {
            detail = "";
            if (a == null || b == null) { detail = "no digest"; return false; }
            var bad = new List<string>();
            int compared = 0;
            foreach (var k in U8DigestSections)
            {
                if ((a[k] == null || a[k].Type == JTokenType.Null) && (b[k] == null || b[k].Type == JTokenType.Null)) continue;
                compared++;
                if (JToken.DeepEquals(a[k], b[k])) continue;
                string ta = (string)a["texts"]?[k], tb = (string)b["texts"]?[k];
                bad.Add(k + (ta != null && tb != null ? " (" + U4FirstLine(ta, tb) + ")" : ""));
            }
            if (compared == 0) { detail = "the digest has neither materials nor objects"; return false; }
            detail = bad.Count == 0 ? string.Join(", ", U8DigestSections.Where(k => a[k] != null && a[k].Type != JTokenType.Null)) + " equal" : "differ: " + string.Join("; ", bad);
            return bad.Count == 0;
        }

        static void U8Flush(AcceptanceCtx c, JObject info, string what)
        {
            if (info == null) return;
            c.Info($"UI8 timing ({U6Here()}): {what}'s flush {(double?)info["lastFlush"]?["ms"]:0.000} ms, mode {(string)info["lastFlush"]?["mode"]}, accessories {U5T(info["accessories"]?["lastFlush"])}");
        }

        /// <summary>Selects the character, shows the inspector's Edit tab and the Accessories node by clicks (ui.query: the slot list shown).</summary>
        static IEnumerator U8Open(AcceptanceCtx c, U8State s, string id, bool[] ok)
        {
            ok[0] = false;
            var e = new JObject[1];
            yield return c.Call(new { cmd = "ui.state" });
            if ((string)c.J["selection"]?["primary"] != id) { yield return c.Call(new { cmd = "select", key = id }); yield return c.Frames(3); }
            yield return c.Call(new { cmd = "ui.panel", panel = "inspector", op = "focus" });
            yield return c.Frames(3);
            yield return U7Q(c, "edit-tree-accessories", e);
            if (!U7Vis(e[0]))
            {
                yield return U8Click(c, s, "tab-" + CharaEditTab.TabId, "the Edit tab");
                yield return U7WaitShown(c, "edit-tree-accessories", true, 60, e);
            }
            if (!c.Check(U7Vis(e[0]), "the Edit tab of " + id + " is shown (edit-tree-accessories)")) yield break;
            yield return U7Q(c, "acc-slots", e);
            if (!U7Vis(e[0]))
            {
                yield return U8Click(c, s, "edit-tree-accessories", "the Accessories node");
                yield return U7WaitShown(c, "acc-slots", true, 60, e);
            }
            ok[0] = c.Check(U7Vis(e[0]), "Edit › Accessories is open (acc-slots)");
        }

        /// <summary>The Accessories page with build slot <paramref name="slot"/>'s group shown (its row clicked unless shown).</summary>
        static IEnumerator U8Select(AcceptanceCtx c, U8State s, string id, int slot, bool[] ok)
        {
            ok[0] = false;
            yield return U8Open(c, s, id, ok);
            if (!ok[0]) yield break;
            ok[0] = false;
            var e = new JObject[1];
            string group = "acc-slot-group-" + U8NN(slot);
            yield return U7Q(c, group, e);
            if (!U7Vis(e[0]))
            {
                yield return U8Click(c, s, "acc-slot-" + U8NN(slot), "the slot row «" + U8NN(slot) + "»");
                yield return U7WaitShown(c, group, true, 60, e);
            }
            ok[0] = c.Check(U7Vis(e[0]), "slot «" + U8NN(slot) + "» is shown (" + group + ")");
            yield return c.Frames(2);
        }

        /// <summary>
        /// «＋»: the add panel, the target slot, the type of <paramref name="category"/>, then <paramref name="reference"/>'s cell →
        /// one entry "Add accessory"; waits until the slot holds the realized item; gives the new accessory id (null on failure).
        /// </summary>
        static IEnumerator U8Add(AcceptanceCtx c, U8State s, string id, int slot, int category, string reference, string what, string[] newAcc)
        {
            newAcc[0] = null;
            var ok = new bool[1];
            var e = new JObject[1];
            yield return U8Open(c, s, id, ok);
            if (!ok[0]) yield break;
            yield return U7Q(c, "acc-add-panel", e);
            if (!U7Vis(e[0]))
            {
                yield return U8Click(c, s, "acc-slot-add", "«＋» (acc-slot-add)");
                yield return U7WaitShown(c, "acc-add-panel", true, 60, e);
            }
            if (!c.Check(U7Vis(e[0]), "the add panel is open (acc-add-panel)")) yield break;
            yield return U8Click(c, s, "acc-add-slot-" + U8NN(slot), "the add panel's slot «" + U8NN(slot) + "»");
            yield return c.Frames(2);
            int k = category - InspectorSchemas.AccessoryCategoryNone;
            yield return U8Click(c, s, "acc-add-type-" + k.ToString(CultureInfo.InvariantCulture), "the add panel's type " + category);
            yield return U7WaitShown(c, "acc-add-list", true, 120, e);
            yield return U7Settle(c, new U7State { S4 = s.S4 });
            var list = U8El(s, "acc-add-list") as PartList;
            if (!c.Check(list != null, "the type's item list (acc-add-list)")) yield break;
            var n = new int[1];
            yield return U7Undo(c, n);
            yield return U4ClickCell(c, s.S4, list, reference, what);
            var a = new JObject[1];
            yield return U8WaitAcc(c, id, slot, x => (string)x?["id"] == reference && (bool?)x?["loaded"] == true && (string)x?["accessory"] != null, 300, a);
            yield return c.Frames(6);   // the page selects the new accessory; live sections after the realizing flush
            yield return U7Entry(c, n[0], OpLabels.AddAccessory, what + " into «" + U8NN(slot) + "»");
            if (!c.Check((string)a[0]?["id"] == reference && (bool?)a[0]?["loaded"] == true, $"slot «{U8NN(slot)}» holds {reference} (loaded {(bool?)a[0]?["loaded"]}, accessory {(string)a[0]?["accessory"]})")) yield break;
            newAcc[0] = (string)a[0]["accessory"];
            yield return c.Call(new { cmd = "chara.edit.info", key = id });
            U8Flush(c, c.J, what + "'s add");
        }

        /// <summary>A click on a button of the page (parent picker key, «初期親») → one entry "Edit accessory".</summary>
        static IEnumerator U8Pick(AcceptanceCtx c, U8State s, string id, string name, string what)
        {
            var n = new int[1];
            yield return U7Undo(c, n);
            yield return U8Click(c, s, name, what);
            yield return c.Frames(3);
            yield return U7Entry(c, n[0], U8EditAccessory, what);
        }

        /// <summary>Types <paramref name="text"/> into a slider row's number field → one entry <paramref name="label"/>.</summary>
        static IEnumerator U8TypeRow(AcceptanceCtx c, U8State s, string id, string rowName, string text, string label, string what)
        {
            var row = U8El(s, rowName);
            if (!c.Check(row != null, what + " row (" + rowName + ")")) yield break;
            var n = new int[1];
            yield return U7Undo(c, n);
            yield return U4Type(c, s.S4, row.Q("field"), text, what + "'s number field");
            yield return c.Frames(3);
            yield return U7Entry(c, n[0], label, "typed " + text + " into " + what);
        }

        /// <summary>Types <paramref name="text"/> into axis <paramref name="axis"/> of a transform row (its "value" field, then
        /// Enter) → one entry "Edit accessory" when <paramref name="entry"/>.</summary>
        static IEnumerator U8TypeVec(AcceptanceCtx c, U8State s, string id, string acc, string member, int axis, string text, bool entry, string what)
        {
            string rn = U8Field(acc, member);
            var row = U8El(s, rn);
            var f = row?.Q(rn + "#" + axis.ToString(CultureInfo.InvariantCulture))?.Q<TextField>("value");
            if (!c.Check(f != null, what + "'s field (" + rn + "#" + axis + " value)")) yield break;
            var n = new int[1];
            yield return U7Undo(c, n);
            yield return U4Type(c, s.S4, f, text, what);
            yield return c.Frames(3);
            if (entry) yield return U7Entry(c, n[0], U8EditAccessory, "typed " + text + " into " + what);
        }

        /// <summary>
        /// A colour row's picker: alpha <paramref name="alpha"/> on its alpha strip (0: a drag from the strip's middle to below it,
        /// the value clamps at 0; else a click at the strip's middle), Enter → one entry "Edit accessory".
        /// </summary>
        static IEnumerator U8PickAlpha(AcceptanceCtx c, U8State s, string id, string acc, string member, double alpha, string what)
        {
            var row = U8El(s, U8Field(acc, member));
            if (!c.Check(row != null, what + " row")) yield break;
            var n = new int[1];
            yield return U7Undo(c, n);
            yield return U4Click(c, s.S4, row.Q("swatch"), what + " swatch");
            yield return c.Frames(4);
            var strip = new bool[1];
            yield return U7AlphaStrip(c, strip);
            if (!c.Check(strip[0], what + ": the picker offers the alpha strip")) { yield return c.Call(new { cmd = "ui.input", frames = 3, events = new object[] { new { type = "key", key = "Escape" } } }); yield break; }
            var st = s.Host.Root.Q("color-picker")?.Q("alpha");
            var p = new Vector2[1];
            yield return U4Reveal(c, s.S4, st, "the picker's alpha strip", p);
            var b = st.worldBound;
            var mid = new Vector2(b.center.x, b.yMin + b.height * 0.5f);
            object[] ev = alpha <= 0
                ? new object[]
                {
                    new { type = "pointer", phase = "down", x = mid.x, y = mid.y, button = 0 },
                    new { type = "pointer", phase = "move", x = mid.x, y = b.yMax + 10, button = 0 },
                    new { type = "pointer", phase = "move", x = mid.x, y = b.yMax + 40, button = 0 },
                    new { type = "pointer", phase = "up", x = mid.x, y = b.yMax + 40, button = 0 },
                }
                : new object[]
                {
                    new { type = "pointer", phase = "down", x = mid.x, y = mid.y, button = 0 },
                    new { type = "pointer", phase = "up", x = mid.x, y = mid.y, button = 0 },
                };
            yield return c.Call(new { cmd = "ui.input", frames = 3, events = ev });
            c.CheckOk("ui.input on the alpha strip (" + (alpha <= 0 ? "dragged below it" : "its middle") + ")");
            yield return c.Call(new { cmd = "ui.input", frames = 3, events = new object[] { new { type = "key", key = "Return" } } });
            yield return c.Frames(3);
            var e = new JObject[1];
            yield return U7Q(c, "color-picker", e);
            c.Check(!U7Vis(e[0]), "Enter closed the picker (ui.query color-picker)");
            yield return U7Entry(c, n[0], U8EditAccessory, what + "'s alpha " + alpha.ToString("0.#", CultureInfo.InvariantCulture));
        }

        /// <summary>A colour row's picker: a drag in the SV square over 3 frames, Enter → one entry "Edit accessory", the colour changed.</summary>
        static IEnumerator U8ColourDrag(AcceptanceCtx c, U8State s, string id, string acc, string member, bool hair, string what)
        {
            var row = U8El(s, U8Field(acc, member, hair));
            if (!c.Check(row != null, what + " row (" + U8Field(acc, member, hair) + ")")) yield break;
            var v = new JObject[1];
            yield return U7Get(c, id, new[] { U8Get(acc, "colors") }, v);
            int k = member.StartsWith("colors[", StringComparison.Ordinal) ? member[7] - '0' : 0;
            var c0 = v[0]?[U8Get(acc, "colors")]?[k]?["color"] as JArray;
            var n = new int[1];
            yield return U7Undo(c, n);
            yield return U4Click(c, s.S4, row.Q("swatch"), what + " swatch");
            yield return c.Frames(4);
            var sv = s.Host.Root.Q("color-picker")?.Q("sv");
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
            yield return c.Frames(3);
            yield return U7Entry(c, n[0], U8EditAccessory, what + " picker session");
            yield return U7Get(c, id, new[] { U8Get(acc, "colors") }, v);
            var c1 = v[0]?[U8Get(acc, "colors")]?[k]?["color"] as JArray;
            c.Check(c0 != null && c1 != null && !(U7Same(c0[0], c1[0]) && U7Same(c0[1], c1[1]) && U7Same(c0[2], c1[2])), $"{what}: the stored colour changed ({U5T(c0)} → {U5T(c1)})");
        }

        /// <summary>The accessory's colours 1–4 and colour 1's smoothness / metallic = hair part <paramref name="part"/>'s base / top /
        /// under / specular and its smoothness / metallic (chara.edit.get).</summary>
        static IEnumerator U8HairColours(AcceptanceCtx c, string id, string acc, string part, string what)
        {
            string hp = "chara.hair." + part + ".";
            var names = new[] { "baseColor", "topColor", "underColor", "specular", "smoothness", "metallic" };
            var v = new JObject[1];
            yield return U7Get(c, id, names.Select(x => hp + x).Concat(new[] { U8Get(acc, "colors") }).ToArray(), v);
            var cols = v[0]?[U8Get(acc, "colors")] as JArray;
            var bad = new List<string>();
            for (int i = 0; i < 4; i++)
                if (!U7Same(cols?[i]?["color"], v[0]?[hp + names[i]])) bad.Add($"colour {i + 1} {U5T(cols?[i]?["color"])} vs {names[i]} {U5T(v[0]?[hp + names[i]])}");
            if (!U7Same(cols?[0]?["smoothness"], v[0]?[hp + "smoothness"])) bad.Add($"smoothness {U5T(cols?[0]?["smoothness"])} vs {U5T(v[0]?[hp + "smoothness"])}");
            if (!U7Same(cols?[0]?["metallic"], v[0]?[hp + "metallic"])) bad.Add($"metallic {U5T(cols?[0]?["metallic"])} vs {U5T(v[0]?[hp + "metallic"])}");
            c.Check(cols != null && bad.Count == 0, what, string.Join("; ", bad));
        }

        /// <summary>Every rendNormal material of a hair-type slot has <paramref name="shader"/> and the row's <paramref name="texColumn"/> as _MainTex.</summary>
        static IEnumerator U8HairShader(AcceptanceCtx c, JObject slot, string shader, string texColumn, string when)
        {
            var mats = U8Mats(slot, "normal");
            string tex = (string)slot?["row"]?[texColumn];
            var bad = mats.Where(m => (string)m["shader"] != shader || (tex != null && (string)m["mainTex"] != tex)).Select(m => m["renderer"] + "[" + m["slot"] + "] " + m["shader"] + " " + m["mainTex"]).ToList();
            c.Check(mats.Count > 0 && tex != null && bad.Count == 0, $"{when}: the hair-type slot's materials {shader} with the row's {texColumn} {tex} ({mats.Count} material slots)", string.Join(", ", bad.Take(6)));
            yield break;
        }

        /// <summary>The view camera in front of set 1 of the slot (its world position from chara.edit.accessories; view state).</summary>
        static void U8Frame(UiContext ui, string id, JObject slot)
        {
            var r = ui.Runtime.FindChara(id);
            var wp = slot?["moves"]?[0]?["worldPosition"] as JArray;
            var pivot = wp != null && wp.Count == 3 ? new Vector3((float)wp[0], (float)wp[1], (float)wp[2]) : (r?.Root ? r.Root.transform.position + Vector3.up * 1.5f : Vector3.up * 1.5f);
            float yaw = r?.Root ? r.Root.transform.eulerAngles.y + 180f : 180f;
            ui.Viewport.Camera.SetView(pivot, new Vector3(10, yaw, 0), 1.2f, 23);
            ui.Viewport.Camera.Flush();
        }
    }
}
