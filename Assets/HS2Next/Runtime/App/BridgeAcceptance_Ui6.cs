// UI6 of the bridge acceptance (Phase 4.3 integrator T): the Hair page of the
// Maker's Edit tab driven through the bridge's ui.* commands in Editor play (BridgeTest.RunPlayUi after UI5, or
// BridgeTest.RunPlayUi6 alone) and in the app player (-bridgeAccept with -ui). Without a running UI the case is skipped with
// a warning (ui.state enabled false). chara.edit.get, .hair, .digest {parts: hair}, .info and ui.state (the bundle handles'
// panel positions) are the read-only checks; the view camera is put in front of the hair in process (view state, as UI2 /
// group MH). Numbers only; nothing is captured.
//   1 file.new, add.female (BR-Chan), Edit › Hair (clicks on tab-edit, edit-tree-hair): the part selector shows Back; the
//     documents and the hair digest after it are the "step 1" state.
//   2 options as stored (same setting on, auto off): Back › Colour shows base, top, under, gloss; 30 typed into «質感» →
//     metallic 0.3 on the four parts, one entry "Edit hair"; _Metallic 0.3 on the back and front materials.
//   3 Back › Style «ボリュームポニー» (illusion.hs2#2) → one entry "Change item"; «カラー01» = the prefab's acsDefColor[0]
//     (0.38915089, 0.41066587, 0.51886785; H's HS2 record, not the spec's 300#58 value); Bundles shows «調整01» with the
//     prefab default rates; the stored bundles [].
//   4 «基本カラー» picker (SV drag, Enter) → one entry; the four base colours equal; the top colours unchanged (auto off).
//   5 auto on (one entry) → «根本» / «毛先» / «ツヤ» hidden; the base colour again → top / under / gloss of the four parts =
//     MakerEdits.Derive (stored alpha kept).
//   6 «金髪» (push-colour:305-illusion.hs2#5) → the six members of row 5 on the four parts, one entry, no derivation.
//   7 «カラー01» picker → one entry; _Color of every slot of the decoration renderers = the stored RGB, alpha 1.
//   8 a drag of «調整01» «移動Y» (three moves, chara.edit.info between them) → one entry; the bundles materialized; the
//     correction transform's local y = LerpUnclamped(posMin.y, posMax.y, r); no full chain rebuild during the drag, the
//     commit's chain step light.
//   9 Front (hair-part-front); «髪の調整をまとめて行う» on (one entry); a drag of «調整02» «回転X» → one entry; rotate x equal
//     on the three bundles, y / z unchanged.
//  10 handle: «調整01»'s marker (ui.state gizmo.handles) clicked through ui.input → the sub-selection <id>/hair/front/<i>,
//     the Edit tab kept; the move tool (hair-handle-move); a drag of the Y arrow → one entry; move y in 0..1 and equal on the
//     three bundles (together on), x / z unchanged.
//  11 «揺らさない» on «調整03» → one entry; its chains disabled and inactive; the chain step full.
//  12 ↺ on «調整02» «回転X» → the prefab default ("Reset value"); «全て初期化» → every bundle at the defaults, «調整03»'s
//     noShake kept, one entry "Edit hair".
//  13 Front › Mesh as stored (pattern 10: mesh_t_10, scale (0.5, 0.5), offset (0.5, 1.5)); «タイプ05» → mesh_t_05; 150 typed
//     into «幅» → stored 1.5, scale x 1 (clamped lerp); «なし» → the colour and layout rows hidden, the texture null.
//  14 draw type «タイプ02» → hair_08 replacement on every hair material, _MainTex = the rows' TexC, render queues kept, one
//     entry; «タイプ01» → hair_07 and TexD again.
//  15 Back › Style «オールバックポニー» (illusion.hs2#58) → «肌の色に合わせる» shown; a click → the back under colour and
//     smoothness (MakerEdits.SkinMatchSets), the front unchanged although same setting is on.
//  16 the back style list searched for «ポニー», cleared with Backspace (ui.input key ×n): every row and the current mark back.
//  17 edit.undo back to step 1 (documents byte-identical, hair digest equal incl. chains), edit.redo to the end.
//  18 file.saveAs → file.new → file.open: documents and hair digest equal to the end state; the rows show the values.
//  19 add.male (HERO107): Hair › Front with auto off (a toggle) shows under and gloss but no top colour; Back › Style #51
//     (p_cm_hair_b_hage01: the skin-match button as its prefab says; H measured it on HS2), then #22 → p_cm_hair_b_19_00,
//     no Bundles section.
// Every step checks the history entry count and label and the document values; no chara.editFailed may be logged; the
// typed values' flush times are reported.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        const string U6Hair = "appearance.chara.hair.";
        const string U6EditHair = HS2Next.UI.Core.OpLabels.EditHair;

        /// <summary>300#2's acsDefColor[0] as HS2 and the census read it (package H).</summary>
        static readonly float[] U6AcsDef2 = { 0.38915089f, 0.41066587f, 0.51886785f };

        static readonly string[] U6DigestSections = { "materials", "creators", "bones", "chains", "rts" };

        sealed class U6State
        {
            public U4State S4;
            public UiContext Ui => S4.Ui;
            public string F, M;
            public int U1;
            public string Docs1, DocsEnd;
            public JObject Dig1, DigEnd;
            public int EditFailed;
        }

        public static IEnumerator Ui6(AcceptanceCtx c)
        {
            c.Id = "UI6";
            yield return c.Call(new { cmd = "ui.state" });
            if (c.Last == null || !c.Last.Ok || (bool?)c.J["enabled"] != true)
            {
                c.Warn("UI6 skipped: the Studio UI is not running (start with -ui; " + AcceptanceCtx.Short(c.Last?.ToString()) + ")");
                yield break;
            }
            var s = new U6State { S4 = new U4State { Host = UiHost.Current } };
            s.S4.Ui = s.S4.Host?.Context;
            if (!c.Check(s.Ui != null, "UiHost in the app's process")) yield break;
            Application.LogCallback onLog = (m, st, t) => { if (m != null && m.Contains("chara.editFailed")) s.EditFailed++; };
            Application.logMessageReceived += onLog;
            var tool0 = s.Ui.Tool;
            var total = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                yield return U6Female(c, s);
                c.Info($"UI6: female part {total.Elapsed.TotalSeconds:F1} s");
                if (s.F != null)
                {
                    total.Restart();
                    yield return U6Male(c, s);
                    c.Info($"UI6: male part {total.Elapsed.TotalSeconds:F1} s");
                }
            }
            finally
            {
                Application.logMessageReceived -= onLog;
                s.Ui.SubSelection = null;
                s.Ui.Tool = tool0;
                PartSelector.Part = 0;
            }
            c.Id = "UI6";
            c.Check(s.EditFailed == 0, $"no chara.editFailed logged during UI6 ({s.EditFailed})");
        }

        // ---------------------------------------------------------------- the female part (steps 1–18)

        static IEnumerator U6Female(AcceptanceCtx c, U6State s)
        {
            var ui = s.Ui;
            var s4 = s.S4;

            // ---- 1: a new scene with BR-Chan, Edit › Hair
            c.Id = "UI6.1";
            yield return c.Call(new { cmd = "ui.action", action = "file.new", args = new { force = true } });
            c.CheckOk("ui.action file.new {force}");
            string card = Path.Combine(AppCore.Current.Paths.Hs2CharaDir(1) ?? "", c.FemaleRel ?? "");
            yield return c.Call(new { cmd = "ui.action", action = "add.female", args = new { path = card } }, 180);
            string id = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && !string.IsNullOrEmpty(id), "ui.action add.female {path} → a new character", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            yield return U5WaitBuilt(c, ui, id);
            if (!c.Check(ui.Runtime.FindChara(id)?.State == RealizationState.Built, "the character is built")) yield break;
            if (!c.Check(U6Doc(ui, id) != null, "the character has stored documents (imported on add)")) yield break;
            if (PartSelector.Part != 0) { c.Info($"UI6: the part selector's view state was {PartSelector.Part}; Back is selected by a click"); }
            var tab = new CharaEditTab[1];
            yield return U6OpenHair(c, s, id, 0, tab);
            if (tab[0] == null) yield break;
            c.Check(tab[0].CurrentNode == CharaEditTab.HairNode && tab[0].CurrentPage == null && tab[0].HairParts != null && tab[0].HairParts.Selected == 0,
                $"Edit › Hair is one page with the part selector on Back (node {tab[0].CurrentNode}, page {tab[0].CurrentPage ?? "-"}, part {tab[0].HairParts?.Selected})");
            var secs = tab[0].Query<Foldout>().ToList().Select(x => x.name).Where(n => n != null && n.StartsWith("edit-section-hair.hair.", StringComparison.Ordinal) && U6Shown(tab[0].Q<Foldout>(n)))
                .Select(n => n.Substring("edit-section-hair.hair.".Length)).ToList();
            c.Info("UI6: Hair sections shown on Back: " + string.Join(", ", secs));
            s.F = id;
            s.U1 = U4Undo(ui);
            var docs = new string[1];
            yield return U4Docs(c, id, docs);
            s.Docs1 = docs[0];
            var dig = new JObject[1];
            yield return U6Digest(c, id, dig);
            s.Dig1 = dig[0];
            if (!c.Check(s.Docs1 != null && s.Dig1 != null, "chara.edit.get and chara.edit.digest {parts: hair} of step 1", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            c.Info($"UI6: hair digest of step 1 {U6DigestLine(s.Dig1)}");

            // ---- 2: options as stored; 30 typed into «質感»
            c.Id = "UI6.2";
            var hd = U6Doc(ui, id);
            c.Check(hd.Editor.SameSetting && !hd.Editor.AutoSetting && hd.ShaderType == 0, $"BR-Chan as stored: same setting {hd.Editor.SameSetting}, auto {hd.Editor.AutoSetting}, draw type {hd.ShaderType} (on, off, 0)");
            var colourRows = new[] { "baseColor", "topColor", "underColor", "specular" }.Select(m => (m, U6Shown(U6Row(tab[0], "back." + m)))).ToList();
            c.Check(colourRows.All(x => x.Item2), "Back › Colour shows base, top, under and gloss (" + string.Join(", ", colourRows.Select(x => x.m + (x.Item2 ? "" : " hidden"))) + ")");
            var met = U4Slider(tab[0], U6Hair + "back.metallic", -1);
            if (c.Check(met != null && U6Shown(met), "the «質感» row (field-…back.metallic)"))
            {
                int u = U4Undo(ui);
                yield return U4Type(c, s4, met.Control.Field, "30", "the metallic number field");
                yield return c.Frames(3);
                U4Entry(c, ui, u, U6EditHair, "the typed metallic");
                var hp = U6Doc(ui, id).Parts;
                c.Check(hp.All(p => p.Metallic == 0.3f), $"typed 30 → metallic 0.3 on the four parts (same setting): {string.Join(", ", hp.Select(p => p.Metallic.ToString("0.####", CultureInfo.InvariantCulture)))}");
                yield return U6FlushInfo(c, id, "the typed metallic");
                var hr = new JObject[1];
                yield return U6HairReply(c, id, hr);
                foreach (int k in new[] { 0, 1 })
                {
                    var vals = U6MatProps(hr[0], k, "_Metallic").ToList();
                    c.Check(vals.Count > 0 && vals.All(v => v != null && v.Type != JTokenType.Null && Math.Abs((double)v - 0.3) < 1e-6),
                        $"_Metallic = 0.3 on every {U6PartName(k)} hair material slot ({vals.Count}: {string.Join(", ", vals.Select(U5T).Distinct())})");
                }
            }

            // ---- 3: Back › Style «ボリュームポニー»
            c.Id = "UI6.3";
            yield return U6Settle(c, tab[0]);
            var facts = new HairPartFacts[1];
            yield return U6PickItem(c, s, tab[0], id, 0, "illusion.hs2#2", "«ボリュームポニー» (illusion.hs2#2)", facts);
            if (facts[0] != null)
            {
                var f2 = facts[0];
                var acs = U6Part(ui, id, 0).AccessoryColors[0];
                c.Check(f2.AcsShown(1) && U6Shown(U6Row(tab[0], "back.accessoryColors[0]")), $"Decoration shows «カラー01» (useAcsColor01 {f2.UseAcsColor01}, acsDefColor count {f2.AcsDefColorCount})");
                c.Check(Math.Abs(acs.R - U6AcsDef2[0]) < 1e-6 && Math.Abs(acs.G - U6AcsDef2[1]) < 1e-6 && Math.Abs(acs.B - U6AcsDef2[2]) < 1e-6,
                    $"«カラー01» = 300#2's acsDefColor[0] (0.38915089, 0.41066587, 0.51886785) ({FieldJson.ToText(acs)})");
                c.Check(U6Part(ui, id, 0).Bundles.Count == 0, $"the stored bundles [] after the item change ({U6Part(ui, id, 0).Bundles.Count})");
                var bl = U6Bundles(tab[0], 0);
                var shownF = f2.Bundles.Where(b => b.Shown).ToList();
                if (c.Check(bl != null && U6Shown(bl) && shownF.Count > 0 && bl.ShownIndices.SequenceEqual(shownF.Select(b => b.Index)),
                    $"Bundles shows the prefab's shown bundles ({(bl == null ? "no list" : string.Join(",", bl.ShownIndices))}; facts {string.Join(",", shownF.Select(b => b.Index))})"))
                {
                    var b0 = shownF[0];
                    string title = bl.GroupOf(b0.Index)?.text;
                    string want = HS2Next.UI.Core.L10n.Format("inspector.charaEdit.hair." + HS2Next.UI.Core.InspectorSchemas.HairBundleTitleName, b0.Title.ToString("00", CultureInfo.InvariantCulture));
                    c.Check(title == want && b0.Title == 1, $"the first group is «調整01» (\"{title}\")");
                    var bad = new List<string>();
                    for (int k = 0; k < 6; k++)
                    {
                        var sl = bl.SliderOf(b0.Index, k >= 3, k % 3);
                        float d = U6C(k >= 3 ? b0.DefaultRotate : b0.DefaultMove, k % 3);
                        if (sl == null || Math.Abs(sl.Control.Value - d * 100) > 1e-3) bad.Add($"{(k >= 3 ? "rotate" : "move")} {k % 3}: {sl?.Control.Value} vs {d * 100}");
                    }
                    c.Check(bad.Count == 0, $"«調整01»'s six rows show the prefab default rates (move {U6V(b0.DefaultMove)}, rotate {U6V(b0.DefaultRotate)})", string.Join("; ", bad));
                }
            }

            // ---- 4: «基本カラー» picker
            c.Id = "UI6.4";
            {
                var tops = U6Doc(ui, id).Parts.Select(p => p.TopColor).ToArray();
                yield return U6ColourDrag(c, s, tab[0], id, U6Hair + "back.baseColor", "the base colour");
                var hp = U6Doc(ui, id).Parts;
                c.Check(hp.All(p => U6SameRgba(p.BaseColor, hp[0].BaseColor)), "the four base colours equal (same setting): " + string.Join(" ", hp.Select(p => FieldJson.ToText(p.BaseColor))));
                c.Check(Enumerable.Range(0, 4).All(k => U6SameRgba(hp[k].TopColor, tops[k])), "the top colours unchanged (auto off)");
            }

            // ---- 5: auto on, the base colour again
            c.Id = "UI6.5";
            {
                var auto = tab[0].Q<MakerToggleRow>("field-" + U6Hair + "editor.autoSetting");
                if (c.Check(auto != null && U6Shown(auto), "the auto colour toggle (field-…editor.autoSetting)"))
                {
                    int u = U4Undo(ui);
                    yield return U4Click(c, s4, auto.Toggle.Q("unity-checkmark") ?? auto.Toggle, "the auto colour toggle");
                    yield return c.Frames(3);
                    U4Entry(c, ui, u, U6EditHair, "the auto colour toggle");
                    c.Check(U6Doc(ui, id).Editor.AutoSetting, "hair.editor.autoSetting = true");
                    for (int i = 0; i < 60 && (U6Shown(U6Row(tab[0], "back.topColor")) || U6Shown(U6Row(tab[0], "back.specular"))); i++) yield return c.Frames(1);
                    c.Check(!U6Shown(U6Row(tab[0], "back.topColor")) && !U6Shown(U6Row(tab[0], "back.underColor")) && !U6Shown(U6Row(tab[0], "back.specular")) && U6Shown(U6Row(tab[0], "back.baseColor")),
                        "auto on hides «根本» / «毛先» / «ツヤ» (live gates), the base colour stays");
                }
                var before = U6Doc(ui, id).Parts.Select(p => (p.TopColor.A, p.UnderColor.A, p.Specular.A)).ToArray();
                yield return U6ColourDrag(c, s, tab[0], id, U6Hair + "back.baseColor", "the base colour (auto on)", 0.75f, 0.25f, -0.4f, 0.35f);   // another end point than step 4
                var hp = U6Doc(ui, id).Parts;
                var bad = new List<string>();
                for (int k = 0; k < 4; k++)
                {
                    MakerEdits.Derive(hp[k].BaseColor, out var top, out var under, out var spec);
                    if (!U6SameRgb(hp[k].TopColor, top) || !U6SameRgb(hp[k].UnderColor, under) || !U6SameRgb(hp[k].Specular, spec)) bad.Add($"{U6PartName(k)}: top {FieldJson.ToText(hp[k].TopColor)} vs {FieldJson.ToText(top)}");
                    if (hp[k].TopColor.A != before[k].Item1 || hp[k].UnderColor.A != before[k].Item2 || hp[k].Specular.A != before[k].Item3) bad.Add(U6PartName(k) + ": an alpha changed");
                }
                c.Check(bad.Count == 0, "top / under / gloss of the four parts = MakerEdits.Derive of the base, stored alpha kept", string.Join("; ", bad));
            }

            // ---- 6: «金髪»
            c.Id = "UI6.6";
            {
                var chips = tab[0].Query<PushGrid>().Where(p => p.IsColour && p.ColourCategory == 305).First();
                if (c.Check(chips != null && U6Shown(chips) && chips.AllItems.Count == 7, $"the colour samples push-colour:305 with 7 chips ({chips?.AllItems.Count})"))
                {
                    HairColourPresetValues pv = default;
                    bool have = MakerServices.Rows != null && MakerServices.Rows.HairColourPreset("illusion.hs2#5", out pv);
                    c.Check(have, "row 5 «金髪» of 305 " + (have ? pv.ToString() : ""));
                    var alphas = U6Doc(ui, id).Parts.Select(p => new[] { p.BaseColor.A, p.TopColor.A, p.UnderColor.A, p.Specular.A }).ToArray();
                    int u = U4Undo(ui);
                    yield return U4ClickCell(c, s4, chips, "illusion.hs2#5", "the chip «金髪» illusion.hs2#5");
                    yield return c.Frames(3);
                    U4Entry(c, ui, u, U6EditHair, "the «金髪» push");
                    var hp = U6Doc(ui, id).Parts;
                    var bad = new List<string>();
                    if (have)
                        for (int k = 0; k < 4; k++)
                        {
                            var p = hp[k];
                            if (!U6SameRgb(p.BaseColor, pv.Base) || !U6SameRgb(p.TopColor, pv.Top) || !U6SameRgb(p.UnderColor, pv.Under) || !U6SameRgb(p.Specular, pv.Specular)
                                || Math.Abs(p.Metallic - pv.Metallic) > 1e-6 || Math.Abs(p.Smoothness - pv.Smoothness) > 1e-6)
                                bad.Add($"{U6PartName(k)}: {FieldJson.ToText(p.BaseColor)} {FieldJson.ToText(p.TopColor)} m {p.Metallic}");
                            if (p.BaseColor.A != alphas[k][0] || p.TopColor.A != alphas[k][1] || p.UnderColor.A != alphas[k][2] || p.Specular.A != alphas[k][3]) bad.Add(U6PartName(k) + ": an alpha changed");
                        }
                    c.Check(have && bad.Count == 0, "the six members of row 5 on the four parts (same setting), no derivation (top = the row's Top although auto is on), stored alpha kept", string.Join("; ", bad));
                }
            }

            // ---- 7: «カラー01»
            c.Id = "UI6.7";
            {
                yield return U6ColourDrag(c, s, tab[0], id, U6Hair + "back.accessoryColors[0]", "«カラー01»");
                var acs = U6Part(ui, id, 0).AccessoryColors[0];
                var hr = new JObject[1];
                yield return U6HairReply(c, id, hr);
                var deco = U6PartJ(hr[0], 0)?["decoration"] as JArray;
                var bad = new List<string>();
                int n = 0;
                foreach (var d in deco ?? new JArray())
                {
                    n++;
                    var col = (d["colors"] as JArray)?[0] as JArray;
                    if (col == null || col.Count != 4 || Math.Abs((double)col[0] - acs.R) > 1e-6 || Math.Abs((double)col[1] - acs.G) > 1e-6 || Math.Abs((double)col[2] - acs.B) > 1e-6 || Math.Abs((double)col[3] - 1) > 1e-6)
                        bad.Add($"{(string)d["renderer"]}[{(int?)d["slot"]}] {U5T(col)}");
                }
                c.Check(n > 0 && bad.Count == 0, $"_Color of every slot of the decoration renderers = {FieldJson.ToText(acs)} RGB with alpha 1 ({n} slots)", string.Join("; ", bad));
            }

            // ---- 8: «調整01» «移動Y» drag
            c.Id = "UI6.8";
            {
                var bl = U6Bundles(tab[0], 0);
                if (c.Check(bl != null && bl.ShownIndices.Count > 0 && bl.Facts != null, "the Back bundle list with a shown bundle"))
                {
                    int i0 = bl.ShownIndices[0];
                    var fb = bl.FactsOfBundle(i0);
                    var sl = bl.SliderOf(i0, false, 1);
                    bl.TryValue(i0, false, 1, out float r0);
                    int n = bl.Facts.Bundles.Length;
                    int u = U4Undo(ui);
                    var during = new List<string>();
                    yield return U6DragSplit(c, s, sl?.Control, r0, "«調整01» «移動Y»", id, during);
                    U4Entry(c, ui, u, U6EditHair, "the «移動Y» drag");
                    var st = U6Part(ui, id, 0).Bundles;
                    var b = st.FirstOrDefault(x => x.Index == i0);
                    c.Check(st.Count == n && b != null, $"the bundles materialized: {st.Count} entries (the prefab's {n})");
                    float r1 = b?.Move.Y ?? float.NaN;
                    c.Check(b != null && r1 != r0 && r1 >= 0 && r1 <= 1 && Math.Abs(b.Move.X - fb.DefaultMove.X) < 1e-6 && Math.Abs(b.Move.Z - fb.DefaultMove.Z) < 1e-6,
                        $"move y {r0:0.####} → {r1:0.####} (inside 0..1), x / z the defaults");
                    var hr = new JObject[1];
                    yield return U6HairReply(c, id, hr);
                    var bj = U6BundleJ(hr[0], 0, i0);
                    var lp = bj?["local"]?["position"] as JArray;
                    float want = Mathf.LerpUnclamped(fb.PosMin.Y, fb.PosMax.Y, r1);
                    c.Check(lp != null && Math.Abs((double)lp[1] - want) < 1e-5, $"the correction transform's local y = LerpUnclamped(posMin.y {fb.PosMin.Y}, posMax.y {fb.PosMax.Y}, {r1:0.####}) = {want:0.######} ({U5T(lp)})");
                    c.Check(during.Count > 0 && during.All(x => x != "full"), $"no full chain rebuild during the drag (chain steps between the moves: {string.Join(", ", during)})");
                    yield return c.Call(new { cmd = "chara.edit.info", key = id });
                    string dyn = U6DynBone(c.J);
                    c.Check(dyn == "light" && (bool?)c.J["hair"]?["deferred"] != true, $"the commit's chain step is light ({dyn}; pending {(bool?)c.J["hair"]?["deferred"]})");
                }
            }

            // ---- 9: Front; «髪の調整をまとめて行う» on; «調整02» «回転X» drag
            c.Id = "UI6.9";
            yield return U6OpenHair(c, s, id, 1, tab);
            if (tab[0] == null) yield break;
            {
                var together = tab[0].Q<MakerToggleRow>("field-" + U6Hair + "editor.ctrlTogether");
                if (c.Check(together != null && U6Shown(together), "the «髪の調整をまとめて行う» toggle"))
                {
                    int u = U4Undo(ui);
                    yield return U4Click(c, s4, together.Toggle.Q("unity-checkmark") ?? together.Toggle, "the together toggle");
                    yield return c.Frames(3);
                    U4Entry(c, ui, u, U6EditHair, "the together toggle");
                    c.Check(U6Doc(ui, id).Editor.CtrlTogether, "hair.editor.ctrlTogether = true");
                }
                var bl = U6Bundles(tab[0], 1);
                if (c.Check(bl != null && U6Shown(bl) && bl.ShownIndices.Count >= 3, $"the Front bundle list ({bl?.ShownIndices.Count} shown; BR-Chan's front: 3)"))
                {
                    int i1 = bl.ShownIndices[1];
                    var eff0 = bl.EffectiveOf(id).Select(x => (x.Move, x.Rotate)).ToList();
                    var sl = bl.SliderOf(i1, true, 0);
                    int u = U4Undo(ui);
                    yield return U6DragSplit(c, s, sl?.Control, eff0[i1].Rotate.X, "«調整02» «回転X»", id, null);
                    U4Entry(c, ui, u, U6EditHair, "the «回転X» drag");
                    var eff1 = bl.EffectiveOf(id);
                    float x = eff1[i1].Rotate.X;
                    c.Check(x != eff0[i1].Rotate.X && eff1.All(e => e.Rotate.X == x), $"rotate x {eff0[i1].Rotate.X:0.####} → {x:0.####} on every bundle ({string.Join(", ", eff1.Select(e => e.Rotate.X.ToString("0.####", CultureInfo.InvariantCulture)))})");
                    c.Check(Enumerable.Range(0, eff1.Count).All(k => eff1[k].Rotate.Y == eff0[k].Rotate.Y && eff1[k].Rotate.Z == eff0[k].Rotate.Z && U6Same(eff1[k].Move, eff0[k].Move)),
                        "rotate y / z and the moves unchanged");
                }
            }

            // ---- 10: the handle of «調整01»
            c.Id = "UI6.10";
            {
                var bl = U6Bundles(tab[0], 1);
                if (bl != null && bl.ShownIndices.Count >= 3)
                {
                    int i0 = bl.ShownIndices[0];
                    U6Frame(ui, id, 1);
                    yield return c.Frames(4);
                    string want = StudioOps.HairSub(id, 1, i0);
                    var marker = new JObject[1];
                    yield return U6Handle(c, "marker", want, null, marker);
                    if (c.Check(marker[0] != null, $"ui.state gizmo.handles holds the marker of «調整01» ({want})"))
                    {
                        var mp = U6P(marker[0]);
                        yield return c.Call(new { cmd = "ui.input", frames = 3, events = new object[] { new { type = "pointer", phase = "down", x = mp.x, y = mp.y, button = 0 }, new { type = "pointer", phase = "up", x = mp.x, y = mp.y, button = 0 } } });
                        c.CheckOk("ui.input click on the marker");
                        yield return c.Frames(2);
                        yield return c.Call(new { cmd = "ui.state" });
                        c.Check((string)c.J["sub"] == want, $"the marker pick → sub-selection {(string)c.J["sub"]} ({want})");
                        c.Check(CharaEditTab.Current == tab[0] && tab[0].panel != null && tab[0].CurrentNode == CharaEditTab.HairNode && bl.GroupOf(i0)?.ClassListContains(HairBundleList.SelectedClass) == true,
                            "the Edit tab stays on the Hair page, «調整01»'s group is marked");
                        var mv = tab[0].Q<Button>("hair-handle-move");
                        if (mv != null && U6Shown(mv)) yield return U4Click(c, s4, mv, "the move tool button (hair-handle-move)");
                        else { yield return c.Call(new { cmd = "ui.action", action = "tool.move" }); c.CheckOk("ui.action tool.move"); }
                        yield return c.Frames(2);
                        c.Check(ui.Tool == HS2Next.UI.GizmoTool.Move, "the move tool (" + ui.Tool + ")");
                        // the view centred on «調整01»'s correction transform: the Y arrow (constant screen length) stays inside the
                        // viewport also at the player's UI scale
                        U6Frame(ui, id, 1, i0);
                        yield return c.Frames(4);
                        var eff0 = bl.EffectiveOf(id).Select(x => x.Move).ToList();
                        var tip = new JObject[1];
                        yield return U6Handle(c, "axis", want, "y", tip);
                        yield return U6Handle(c, "marker", want, null, marker);
                        if (c.Check(tip[0] != null && (string)tip[0]["tool"] == "move" && marker[0] != null, "the move handle's Y arrow in gizmo.handles"))
                        {
                            var gz = ui.Viewport.Gizmo;
                            var mp0 = U6P(marker[0]);
                            var tp = U6P(tip[0]);
                            var dir = (tp - mp0).normalized;
                            // the grab point: the tip of gizmo.handles, else a point along the arrow, where the gizmo's hit test
                            // finds the Y arrow
                            bool hitY = false;
                            foreach (float f in new[] { 1f, 0.75f, 0.6f, 0.45f, 0.85f, 0.3f })
                            {
                                var q = Vector2.Lerp(mp0, tp, f);
                                var h = gz.HitTest(q);
                                if (h.Kind == HS2Next.UI.Gizmos.HandleKind.MoveAxis && h.Axis == 1) { tp = q; hitY = true; break; }
                            }
                            var vb = ui.Viewport.Element.worldBound;
                            var picked = s.S4.Host.Root?.panel?.Pick(tp);
                            c.Info($"UI6: the Y arrow grab point {tp} (marker {mp0}, tip {U6P(tip[0])}), viewport {vb} (inside {vb.Contains(tp)}), hit test {gz.HitTest(tp)}, picked {(picked == null ? "nothing" : (picked.name ?? "") + " (" + picked.GetType().Name + ")")}");
                            c.Check(hitY, "the gizmo's hit test finds the Y arrow under the grab point");
                            bool done = false;
                            for (int attempt = 0; attempt < 2 && !done; attempt++)
                            {
                                var d = attempt == 0 ? dir : -dir;
                                int u = U4Undo(ui);
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
                                var eff1 = bl.EffectiveOf(id).Select(x => x.Move).ToList();
                                if (eff1[i0].Y == eff0[i0].Y && attempt == 0)
                                {
                                    c.Info($"UI6: the Y drag left move y at {eff1[i0].Y} ({U4Undo(ui) - u} entries; a limit?); the reverse direction is dragged");
                                    eff0 = eff1;
                                    continue;
                                }
                                done = true;
                                U4Entry(c, ui, u, U6EditHair, "the Y-arrow drag");
                                float y = eff1[i0].Y;
                                c.Check(y != eff0[i0].Y && y >= 0 && y <= 1 && eff1.All(m => m.Y == y), $"move y {eff0[i0].Y:0.####} → {y:0.####} (0..1) on every bundle (together on): {string.Join(", ", eff1.Select(m => m.Y.ToString("0.####", CultureInfo.InvariantCulture)))}");
                                c.Check(Enumerable.Range(0, eff1.Count).All(k => eff1[k].X == eff0[k].X && eff1[k].Z == eff0[k].Z), "move x / z unchanged");
                            }
                            c.Info($"UI6 timing ({U6Here()}): the handle's last drag step (op + flush) {ui.Viewport.Gizmo.HairDragMs:0.000} ms");
                        }
                    }
                }
                else c.Check(false, "the Front bundle list for the handle step");
            }

            // ---- 11: «揺らさない» on «調整03»
            c.Id = "UI6.11";
            {
                var bl = U6Bundles(tab[0], 1);
                if (bl != null && bl.ShownIndices.Count >= 3)
                {
                    int i2 = bl.ShownIndices[2];
                    var hr = new JObject[1];
                    yield return U6HairReply(c, id, hr);
                    var ch0 = U6BundleJ(hr[0], 1, i2)?["chains"] as JArray;
                    c.Info($"UI6: «調整03»'s chains before: {string.Join(", ", (ch0 ?? new JArray()).Select(x => (string)x["root"] + " enabled " + (bool?)x["enabled"] + " active " + (bool?)x["active"]))}");
                    var row = tab[0].Q<VisualElement>("field-" + U6Hair + "front.bundles[" + i2 + "].noShake");
                    var tg = row?.Q<Toggle>("toggle");
                    if (c.Check(tg != null && U6Shown(tg) && !tg.value, "«調整03»'s «揺らさない» toggle (off)"))
                    {
                        int u = U4Undo(ui);
                        yield return U4Click(c, s4, tg.Q("unity-checkmark") ?? tg, "«調整03»'s «揺らさない»");
                        yield return c.Frames(3);
                        U4Entry(c, ui, u, U6EditHair, "«揺らさない»");
                        c.Check(U6Part(ui, id, 1).Bundles.FirstOrDefault(b => b.Index == i2)?.NoShake == true, "bundles[" + i2 + "].noShake = true");
                        yield return c.Call(new { cmd = "chara.edit.info", key = id });
                        string dyn = U6DynBone(c.J);
                        c.Check(dyn == "full", $"the chain step of the toggle is full ({dyn})");
                        yield return U6HairReply(c, id, hr);
                        var bj = U6BundleJ(hr[0], 1, i2);
                        var ch = bj?["chains"] as JArray;
                        c.Check(ch != null && ch.Count > 0 && ch.All(x => (bool?)x["enabled"] == false && (bool?)x["active"] == false && (bool?)x["noShake"] == true) && (bool?)bj["effective"]?["noShake"] == true,
                            $"«調整03»'s chains disabled and inactive ({string.Join(", ", (ch ?? new JArray()).Select(x => (string)x["root"] + " enabled " + (bool?)x["enabled"] + " active " + (bool?)x["active"]))})");
                    }
                }
            }

            // ---- 12: ↺ on «調整02» «回転X»; «全て初期化»
            c.Id = "UI6.12";
            {
                var bl = U6Bundles(tab[0], 1);
                if (bl != null && bl.ShownIndices.Count >= 3)
                {
                    int i1 = bl.ShownIndices[1], i2 = bl.ShownIndices[2];
                    var f1 = bl.FactsOfBundle(i1);
                    var eff0 = bl.EffectiveOf(id).Select(x => x.Rotate.X).ToList();
                    var sl = bl.SliderOf(i1, true, 0);
                    int u = U4Undo(ui);
                    yield return U4Click(c, s4, sl?.Control.ResetButton, "«調整02» «回転X»'s ↺");
                    yield return c.Frames(3);
                    U4Entry(c, ui, u, U4ResetValue, "↺");
                    var eff1 = bl.EffectiveOf(id);
                    c.Check(Math.Abs(eff1[i1].Rotate.X - f1.DefaultRotate.X) < 1e-6, $"↺ → the prefab default rotate x {f1.DefaultRotate.X:0.####} ({eff1[i1].Rotate.X:0.####})");
                    c.Check(Enumerable.Range(0, eff1.Count).Where(k => k != i1).All(k => eff1[k].Rotate.X == eff0[k]), "the reset did not propagate (the other bundles' rotate x unchanged)");
                    var reset = tab[0].Q<Button>("hair-bundles-reset-front");
                    u = U4Undo(ui);
                    yield return U4Click(c, s4, reset, "«全て初期化» (hair-bundles-reset-front)");
                    yield return c.Frames(3);
                    U4Entry(c, ui, u, U6EditHair, "«全て初期化»");
                    var st = U6Part(ui, id, 1).Bundles;
                    var bad = st.Where(b => b.Index < bl.Facts.Bundles.Length && !(U6Same(b.Move, bl.Facts.Bundles[b.Index].DefaultMove) && U6Same(b.Rotate, bl.Facts.Bundles[b.Index].DefaultRotate))).Select(b => b.Index).ToList();
                    c.Check(st.Count == bl.Facts.Bundles.Length && bad.Count == 0, $"every bundle at the prefab defaults ({st.Count} stored; off: {string.Join(",", bad)})");
                    c.Check(st.FirstOrDefault(b => b.Index == i2)?.NoShake == true && st.Where(b => b.Index != i2).All(b => !b.NoShake), "«調整03»'s noShake kept (the others off)");
                }
            }

            // ---- 13: Front › Mesh
            c.Id = "UI6.13";
            {
                var hr = new JObject[1];
                yield return U6HairReply(c, id, hr);
                var mesh = U6PartJ(hr[0], 1)?["mesh"];
                c.Check(U6Part(ui, id, 1).Mesh.Item == "illusion.hs2#10" && (string)mesh?["texture"] == "mesh_t_10" && U6Arr(mesh?["scale"], 0.5, 0.5) && U6Arr(mesh?["offset"], 0.5, 1.5),
                    $"Front › Mesh as stored: pattern {U6Part(ui, id, 1).Mesh.Item}, texture {(string)mesh?["texture"]}, scale {U5T(mesh?["scale"])}, offset {U5T(mesh?["offset"])} (mesh_t_10, (0.5, 0.5), (0.5, 1.5))");
                var texs = U6MatProps(hr[0], 1, "_MeshColorMask_B").ToList();
                var sts = U6MatProps(hr[0], 1, "_MeshColorMask_B_ST").ToList();
                c.Check(texs.Count > 0 && texs.All(t => t is JObject o && (string)o["name"] == "mesh_t_10") && sts.All(t => U6Arr(t, 0.5, 0.5, 0.5, 1.5)),
                    $"_MeshColorMask_B mesh_t_10 with ST (0.5, 0.5, 0.5, 1.5) on every front hair material ({texs.Count})");
                var ml = U4List(tab[0], U6Hair + "front.mesh.item");
                if (c.Check(ml != null && U6Shown(ml) && ml.Category == 306, "the Front mesh pattern list (306)"))
                {
                    int u = U4Undo(ui);
                    yield return U4ClickCell(c, s4, ml, "illusion.hs2#5", "the pattern cell «タイプ05»");
                    for (int i = 0; i < 120 && U6Part(ui, id, 1).Mesh.Item != "illusion.hs2#5"; i++) yield return c.Frames(1);
                    yield return c.Frames(3);
                    U4Entry(c, ui, u, HS2Next.UI.Core.OpLabels.ChangeItem, "the «タイプ05» pick");
                    yield return U6HairReply(c, id, hr);
                    c.Check((string)U6PartJ(hr[0], 1)?["mesh"]?["texture"] == "mesh_t_05", $"the pattern texture mesh_t_05 ({(string)U6PartJ(hr[0], 1)?["mesh"]?["texture"]})");
                }
                var w = U4Slider(tab[0], U6Hair + "front.mesh.layout", 0);
                if (c.Check(w != null && U6Shown(w), "the «幅» row (field-…front.mesh.layout#0)"))
                {
                    int u = U4Undo(ui);
                    yield return U4Type(c, s4, w.Control.Field, "150", "the mesh width number field");
                    yield return c.Frames(3);
                    U4Entry(c, ui, u, U6EditHair, "the typed mesh width");
                    var lay = U6Part(ui, id, 1).Mesh.Layout;
                    c.Check(lay.X == 1.5f, $"typed 150 → layout x {lay.X} (1.5, stored unclamped)");
                    yield return U6FlushInfo(c, id, "the typed mesh width");
                    yield return U6HairReply(c, id, hr);
                    var sc = U6PartJ(hr[0], 1)?["mesh"]?["scale"] as JArray;
                    c.Check(sc != null && Math.Abs((double)sc[0] - 1.0) < 1e-6, $"the pattern scale x = lerp(0.5, 1, 1.5) clamped = 1 ({U5T(sc)})");
                }
                if (ml != null)
                {
                    int u = U4Undo(ui);
                    yield return U4ClickCell(c, s4, ml, "illusion.hs2#0", "the pattern cell «なし»");
                    for (int i = 0; i < 120 && U6Part(ui, id, 1).Mesh.Item != "illusion.hs2#0"; i++) yield return c.Frames(1);
                    U4Entry(c, ui, u, HS2Next.UI.Core.OpLabels.ChangeItem, "the «なし» pick");
                    var meshRows = new[] { "front.mesh.color" }.Concat(Enumerable.Range(0, 4).Select(k => "front.mesh.layout#" + k)).ToArray();
                    List<string> still() => meshRows.Where(m => U6Shown(tab[0].Q<VisualElement>("field-" + U6Hair + m))).ToList();
                    int waited = 0;
                    for (; waited < 300 && still().Count > 0; waited++) yield return c.Frames(1);
                    var left = still();
                    var ml2 = U4List(tab[0], U6Hair + "front.mesh.item");   // the live gate rebuilt the section: a new list element
                    c.Check(left.Count == 0 && U6Shown(ml2), $"«なし» hides the mesh colour and the four layout rows, the list stays (after {waited} frames; pending {tab[0].HairPending})", left.Count == 0 ? "" : "still shown: " + string.Join(", ", left));
                    yield return U6HairReply(c, id, hr);
                    var m2 = U6PartJ(hr[0], 1)?["mesh"];
                    var t2 = U6MatProps(hr[0], 1, "_MeshColorMask_B").ToList();
                    c.Check((m2?["texture"] == null || m2["texture"].Type == JTokenType.Null) && t2.All(t => t == null || t.Type == JTokenType.Null),
                        $"the pattern texture null on every front hair material ({U5T(m2?["texture"])})");
                }
            }

            // ---- 14: draw type
            c.Id = "UI6.14";
            {
                var choice = tab[0].Q<MakerChoiceRow>("field-" + U6Hair + "shaderType");
                if (c.Check(choice != null && U6Shown(choice), "the draw type row (field-…shaderType)"))
                {
                    var hr = new JObject[1];
                    yield return U6HairReply(c, id, hr);
                    var q0 = U6Queues(hr[0]);
                    int u = U4Undo(ui);
                    yield return U4Click(c, s4, choice.Q<Button>(choice.name + "-1"), "«タイプ02» (" + choice.name + "-1)");
                    yield return c.Frames(3);
                    U4Entry(c, ui, u, U6EditHair, "«タイプ02»");
                    c.Check(U6Doc(ui, id).ShaderType == 1, "hair.shaderType = 1");
                    yield return U6FlushInfo(c, id, "the draw type");
                    yield return U6HairReply(c, id, hr);
                    yield return U6DrawTypeCheck(c, hr[0], q0, "HS2Next/Shader Forge/hair_08", "texC", "«タイプ02»");
                    u = U4Undo(ui);
                    yield return U4Click(c, s4, choice.Q<Button>(choice.name + "-0"), "«タイプ01» (" + choice.name + "-0)");
                    yield return c.Frames(3);
                    U4Entry(c, ui, u, U6EditHair, "«タイプ01»");
                    yield return U6FlushInfo(c, id, "the draw type back to «タイプ01»");
                    yield return U6HairReply(c, id, hr);
                    yield return U6DrawTypeCheck(c, hr[0], q0, "HS2Next/Shader Forge/hair_07", "texD", "«タイプ01»");
                }
            }

            // ---- 15: Back › Style «オールバックポニー»; «肌の色に合わせる»
            c.Id = "UI6.15";
            yield return U6OpenHair(c, s, id, 0, tab);
            if (tab[0] == null) yield break;
            yield return U6Settle(c, tab[0]);
            {
                var facts58 = new HairPartFacts[1];
                yield return U6PickItem(c, s, tab[0], id, 0, "illusion.hs2#58", "«オールバックポニー» (illusion.hs2#58)", facts58);
                for (int i = 0; i < 60 && !U6Shown(tab[0].Q<Button>("hair-skin-back")); i++) yield return c.Frames(1);
                var skin = tab[0].Q<Button>("hair-skin-back");
                if (c.Check(facts58[0] != null && facts58[0].UseSameSkinColorButton && U6Shown(skin), $"«肌の色に合わせる» shown (useSameSkinColorButton {facts58[0]?.UseSameSkinColorButton})"))
                {
                    var front0 = U6Part(ui, id, 1);
                    var fu = front0.UnderColor;
                    float fs = front0.Smoothness;
                    var sets = MakerEdits.SkinMatchSets(U6Doc(ui, id) == null ? null : ui.Model.Find(id).Appearance.Stored.Chara, 0);
                    var wantUnder = (Color4)sets[0].value;
                    float wantSmooth = (float)sets[1].value;
                    int u = U4Undo(ui);
                    yield return U4Click(c, s4, skin, "«肌の色に合わせる» (hair-skin-back)");
                    yield return c.Frames(3);
                    U4Entry(c, ui, u, U6EditHair, "the skin match");
                    var bp = U6Part(ui, id, 0);
                    c.Check(U6SameRgba(bp.UnderColor, wantUnder) && Math.Abs(bp.Smoothness - wantSmooth) < 1e-6,
                        $"back under colour {FieldJson.ToText(bp.UnderColor)} = the hair skin-match of the skin colour ({FieldJson.ToText(wantUnder)}), smoothness {bp.Smoothness} = max(0, gloss − 0.3) {wantSmooth}");
                    var fp = U6Part(ui, id, 1);
                    c.Check(U6SameRgba(fp.UnderColor, fu) && fp.Smoothness == fs, "the front unchanged although same setting is on (HS2: the shown part only)");
                }
            }

            // ---- 16: the back style list's search, cleared with Backspace
            c.Id = "UI6.16";
            {
                var pl = U4List(tab[0], U6Hair + "back.item");
                if (c.Check(pl != null && U6Shown(pl), "the back style list"))
                {
                    string cur = U5Ref(ui, id, U6Hair + "back.item");
                    int all = pl.AllItems.Count(x => !x.NotListed);
                    const string word = "ポニー";
                    var search = pl.Q<TextField>("partlist-search");
                    yield return U4Type(c, s4, search, word, "the back style list's search field");
                    for (int i = 0; i < 60 && pl.Items.Count(x => !x.NotListed) >= all; i++) yield return c.Frames(1);   // the search's debounce
                    int filtered = pl.Items.Count(x => !x.NotListed);
                    c.Check(pl.SearchText == word && filtered > 0 && filtered < all && pl.PositionOf(cur) >= 0, $"the search \"{word}\" shows {filtered} of {all} styles incl. the current {cur}");
                    var bs = Enumerable.Range(0, word.Length + 1).Select(_ => (object)new { type = "key", key = "Backspace" }).ToArray();
                    yield return c.Call(new { cmd = "ui.input", frames = 3, events = bs });
                    c.CheckOk($"ui.input Backspace ×{bs.Length} in the search field");
                    for (int i = 0; i < 60 && (pl.SearchText != "" || pl.Items.Count(x => !x.NotListed) < all); i++) yield return c.Frames(1);
                    if (pl.SearchText != "")
                    {
                        c.Info($"UI6: the search field held \"{pl.SearchText}\" after Backspace (focus lost?); clicked and cleared again");
                        var p = new Vector2[1];
                        yield return U4Reveal(c, s4, search.Q("unity-text-input") ?? search, "the back style list's search field", p);
                        var ev = new List<object> { new { type = "pointer", phase = "click", x = p[0].x, y = p[0].y, button = 0 } };
                        ev.AddRange(Enumerable.Range(0, pl.SearchText.Length + 2).Select(_ => (object)new { type = "key", key = "Backspace" }));
                        yield return c.Call(new { cmd = "ui.input", frames = 3, events = ev.ToArray() });
                        for (int i = 0; i < 60 && (pl.SearchText != "" || pl.Items.Count(x => !x.NotListed) < all); i++) yield return c.Frames(1);
                    }
                    c.Check(pl.SearchText == "" && pl.Items.Count(x => !x.NotListed) == all,
                        $"Backspace cleared the search: every style shown again ({pl.Items.Count(x => !x.NotListed)} of {all}; text \"{pl.SearchText}\")");
                    c.Check(U4Marked(pl, cur), "the current style " + cur + " carries the current mark");
                }
            }

            // ---- 17: undo to step 1, redo to the end
            c.Id = "UI6.17";
            yield return c.Frames(3);
            var docsE = new string[1];
            yield return U4Docs(c, id, docsE);
            s.DocsEnd = docsE[0];
            var digE = new JObject[1];
            yield return U6Digest(c, id, digE);
            s.DigEnd = digE[0];
            int n17 = U4Undo(ui) - s.U1;
            c.Info($"UI6: {n17} history entries since step 1: {string.Join(" | ", ui.Session.Commands.UndoList.Take(n17).Reverse().Select(e => e.Label))}");
            for (int i = 0; i < n17; i++)
            {
                yield return c.Call(new { cmd = "ui.action", action = "edit.undo" });
                if (!(c.Last != null && c.Last.Ok)) { c.CheckOk("ui.action edit.undo #" + (i + 1)); break; }
                yield return c.Frames(1);
            }
            yield return U5WaitBuilt(c, ui, id);
            yield return c.Frames(6);
            c.Check(U4Undo(ui) == s.U1, $"undo back to step 1 ({U4Undo(ui)} entries, {s.U1} at step 1)");
            yield return U4Docs(c, id, docs);
            c.Check(docs[0] != null && docs[0] == s.Docs1, "undo to step 1 → the documents are byte-identical to step 1", docs[0] == s.Docs1 ? "" : U4JsonDiff(s.Docs1, docs[0]));
            yield return U6Digest(c, id, dig);
            c.Check(U6SameDigest(s.Dig1, dig[0], out string d1), "undo to step 1 → the hair digest equals step 1's (incl. chains)", d1);
            for (int i = 0; i < n17; i++)
            {
                yield return c.Call(new { cmd = "ui.action", action = "edit.redo" });
                if (!(c.Last != null && c.Last.Ok)) { c.CheckOk("ui.action edit.redo #" + (i + 1)); break; }
                yield return c.Frames(1);
            }
            yield return U5WaitBuilt(c, ui, id);
            yield return c.Frames(6);
            yield return U4Docs(c, id, docs);
            c.Check(docs[0] != null && docs[0] == s.DocsEnd, "redo to the end → the documents equal the end state", docs[0] == s.DocsEnd ? "" : U4JsonDiff(s.DocsEnd, docs[0]));
            yield return U6Digest(c, id, dig);
            c.Check(U6SameDigest(s.DigEnd, dig[0], out string d2), "redo to the end → the hair digest equals the end state's", d2);

            // ---- 18: save as, new, open
            c.Id = "UI6.18";
            yield return c.Call(new { cmd = "ui.action", action = "file.saveAs", args = new { name = "ui6_acceptance", overwrite = true } }, 120);
            string file = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && file != null && File.Exists(file), "ui.action file.saveAs {name} → " + file)) yield break;
            yield return c.Call(new { cmd = "ui.action", action = "file.new", args = new { force = true } });
            c.CheckOk("ui.action file.new {force}");
            yield return c.Call(new { cmd = "ui.action", action = "file.open", args = new { path = file } }, 180);
            c.CheckOk("ui.action file.open {path}");
            yield return U5WaitBuilt(c, ui, id);
            if (!c.Check(ui.Runtime.FindChara(id)?.State == RealizationState.Built, "the reopened character is built (same node id)")) yield break;
            yield return c.Frames(3);
            yield return U4Docs(c, id, docs);
            c.Check(docs[0] != null && docs[0] == s.DocsEnd, "reopened: the documents equal the end state", docs[0] == s.DocsEnd ? "" : U4JsonDiff(s.DocsEnd, docs[0]));
            yield return U6Digest(c, id, dig);
            c.Check(U6SameDigest(s.DigEnd, dig[0], out string d3), "reopened: the hair digest (materials, creators, bones, chains" + (dig[0]?["rts"] != null ? ", RT hashes" : "") + ") equals the end state's", d3);
            yield return U6OpenHair(c, s, id, 0, tab);
            if (tab[0] != null)
            {
                var pl = U4List(tab[0], U6Hair + "back.item");
                var met18 = U4Slider(tab[0], U6Hair + "back.metallic", -1);
                var sm18 = U4Slider(tab[0], U6Hair + "back.smoothness", -1);
                var bp = U6Part(ui, id, 0);
                c.Check(pl != null && U4Marked(pl, "illusion.hs2#58"), "reopened: the back style list marks «オールバックポニー» (illusion.hs2#58)");
                c.Check(met18 != null && sm18 != null && Math.Abs(met18.Control.Value - bp.Metallic * 100) < 1e-3 && Math.Abs(sm18.Control.Value - bp.Smoothness * 100) < 1e-3,
                    $"reopened: «質感» {met18?.Control.Value} / «滑らかさ» {sm18?.Control.Value} show the stored {bp.Metallic * 100:0.###} / {bp.Smoothness * 100:0.###}");
                yield return U6OpenHair(c, s, id, 1, tab);
                if (tab[0] != null)
                {
                    var ml = U4List(tab[0], U6Hair + "front.mesh.item");
                    c.Check(ml != null && U4Marked(ml, "illusion.hs2#0") && !U6Shown(U6Row(tab[0], "front.mesh.color")), "reopened: Front › Mesh marks «なし», the colour row hidden");
                    var bl = U6Bundles(tab[0], 1);
                    int i2 = bl != null && bl.ShownIndices.Count >= 3 ? bl.ShownIndices[2] : -1;
                    var tg = i2 < 0 ? null : tab[0].Q<VisualElement>("field-" + U6Hair + "front.bundles[" + i2 + "].noShake")?.Q<Toggle>("toggle");
                    c.Check(tg != null && tg.value, "reopened: «調整03»'s «揺らさない» is on");
                }
            }
        }

        // ---------------------------------------------------------------- the male part (step 19)

        static IEnumerator U6Male(AcceptanceCtx c, U6State s)
        {
            var ui = s.Ui;
            c.Id = "UI6.19";
            string card = Path.Combine(AppCore.Current.Paths.Hs2CharaDir(0) ?? "", Ui2MaleRel ?? "");
            yield return c.Call(new { cmd = "ui.action", action = "add.male", args = new { path = card } }, 180);
            string id = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && !string.IsNullOrEmpty(id), "ui.action add.male {path} → a new character (HERO107)", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            yield return U5WaitBuilt(c, ui, id);
            if (!c.Check(ui.Runtime.FindChara(id)?.State == RealizationState.Built, "the male character is built")) yield break;
            s.M = id;
            var tab = new CharaEditTab[1];
            yield return U6OpenHair(c, s, id, 1, tab);
            if (tab[0] == null) yield break;
            var auto = tab[0].Q<MakerToggleRow>("field-" + U6Hair + "editor.autoSetting");
            if (c.Check(auto != null && U6Shown(auto), "male: the auto colour toggle"))
            {
                bool a0 = U6Doc(ui, id).Editor.AutoSetting;
                c.Info($"UI6: HERO107's auto colour as stored: {a0}");
                if (a0)
                {
                    int u = U4Undo(ui);
                    yield return U4Click(c, s.S4, auto.Toggle.Q("unity-checkmark") ?? auto.Toggle, "the male's auto colour toggle");
                    yield return c.Frames(3);
                    U4Entry(c, ui, u, U6EditHair, "the male's auto colour toggle");
                }
                for (int i = 0; i < 60 && !U6Shown(U6Row(tab[0], "front.specular")); i++) yield return c.Frames(1);
                bool hasFacts = HairInfo.TryPart(ui.Runtime, id, 1, out var ff) && ff != null && ff.HasPrefab;
                c.Check(!U6Doc(ui, id).Editor.AutoSetting && hasFacts && !ff.UseTopColor && !U6Shown(U6Row(tab[0], "front.topColor")) && U6Shown(U6Row(tab[0], "front.underColor")) && U6Shown(U6Row(tab[0], "front.specular")),
                    $"male front with auto off: «毛先» and «ツヤ» shown, «根本» hidden (useTopColor {ff?.UseTopColor}; prefab {ff?.PrefabName})");
            }
            yield return U6OpenHair(c, s, id, 0, tab);
            if (tab[0] == null) yield break;
            yield return U6Settle(c, tab[0]);
            var f51 = new HairPartFacts[1];
            yield return U6PickItem(c, s, tab[0], id, 0, "illusion.hs2#51", "male back #51", f51);
            var hr = new JObject[1];
            yield return U6HairReply(c, id, hr);
            string prefab51 = (string)U6PartJ(hr[0], 0)?["prefab"];
            bool skinShown = U6Shown(tab[0].Q<Button>("hair-skin-back"));
            c.Check(prefab51 == "p_cm_hair_b_hage01" && f51[0] != null && skinShown == f51[0].UseSameSkinColorButton,
                $"male back #51 → prefab {prefab51} (p_cm_hair_b_hage01); «肌の色に合わせる» {(skinShown ? "shown" : "hidden")} as the prefab's flag {f51[0]?.UseSameSkinColorButton}");
            if (f51[0] != null && !f51[0].UseSameSkinColorButton)
                c.Warn("UI6: HS2Next's p_cm_hair_b_hage01 (abdata) has no skin-match button; HS2 shows it on its served copy — the zipmod per-asset redirect of 4.4");
            yield return U6Settle(c, tab[0]);
            var f22 = new HairPartFacts[1];
            yield return U6PickItem(c, s, tab[0], id, 0, "illusion.hs2#22", "male back #22", f22);
            yield return U6HairReply(c, id, hr);
            string prefab22 = (string)U6PartJ(hr[0], 0)?["prefab"];
            for (int i = 0; i < 60 && U6Shown(tab[0].Q<Foldout>("edit-section-hair.hair.back.bundles")); i++) yield return c.Frames(1);
            c.Check(prefab22 == "p_cm_hair_b_19_00" && f22[0] != null && !f22[0].AnyBundleShown && !U6Shown(tab[0].Q<Foldout>("edit-section-hair.hair.back.bundles")),
                $"male back #22 → prefab {prefab22} (p_cm_hair_b_19_00), no Bundles section ({f22[0]?.Bundles.Length} bundles)");
        }

        // ---------------------------------------------------------------- helpers

        static string U6Here() => Application.isEditor ? "Editor" : "player";

        static string U6PartName(int k) => HS2Next.UI.Core.InspectorSchemas.HairParts[k];

        static HairData U6Doc(UiContext ui, string id) => ui.Model.Find(id)?.Appearance?.Stored?.Chara?.Hair;

        static HairPart U6Part(UiContext ui, string id, int k) => U6Doc(ui, id)?.Parts[k];

        static VisualElement U6Row(VisualElement root, string member) => root?.Q<VisualElement>("field-" + U6Hair + member);

        static HairBundleList U6Bundles(VisualElement root, int k) => root?.Q<HairBundleList>("field-" + U6Hair + U6PartName(k) + ".bundles");

        /// <summary>Attached and no element up the tree hidden (inline display none, as the page's gates hide rows).</summary>
        static bool U6Shown(VisualElement e)
        {
            if (e == null || e.panel == null) return false;
            for (var p = e; p != null; p = p.hierarchy.parent) if (p.style.display == DisplayStyle.None) return false;
            return true;
        }

        static float U6C(Vec3 v, int c) => c == 0 ? v.X : c == 1 ? v.Y : v.Z;
        static string U6V(Vec3 v) => "(" + string.Join(", ", new[] { v.X, v.Y, v.Z }.Select(x => x.ToString("0.####", CultureInfo.InvariantCulture))) + ")";
        static bool U6Same(Vec3 a, Vec3 b) => Math.Abs(a.X - b.X) < 1e-6f && Math.Abs(a.Y - b.Y) < 1e-6f && Math.Abs(a.Z - b.Z) < 1e-6f;
        static bool U6SameRgb(Color4 a, Color4 b) => Math.Abs(a.R - b.R) < 1e-6f && Math.Abs(a.G - b.G) < 1e-6f && Math.Abs(a.B - b.B) < 1e-6f;
        static bool U6SameRgba(Color4 a, Color4 b) => U6SameRgb(a, b) && Math.Abs(a.A - b.A) < 1e-6f;

        static bool U6Arr(JToken t, params double[] want)
        {
            if (!(t is JArray a) || a.Count != want.Length) return false;
            for (int i = 0; i < want.Length; i++) if (a[i].Type == JTokenType.Null || Math.Abs((double)a[i] - want[i]) > 1e-6) return false;
            return true;
        }

        static Vector2 U6P(JToken h) => h?["panel"] is JArray a && a.Count == 2 ? new Vector2((float)a[0], (float)a[1]) : new Vector2(-1e6f, -1e6f);

        /// <summary>The chain step of the last flush (chara.edit.info hair.lastFlush.dynBone, else lastFlush.dynBone).</summary>
        static string U6DynBone(JObject info) => (string)info?["hair"]?["lastFlush"]?["dynBone"] ?? (string)info?["lastFlush"]?["dynBone"] ?? "?";

        static IEnumerator U6HairReply(AcceptanceCtx c, string id, JObject[] into)
        {
            yield return c.Call(new { cmd = "chara.edit.hair", key = id }, 120);
            into[0] = c.Last != null && c.Last.Ok ? (JObject)c.J.DeepClone() : null;
            if (into[0] == null) c.Check(false, "chara.edit.hair {key}", AcceptanceCtx.Short(c.Last?.ToString()));
        }

        static JToken U6PartJ(JObject reply, int k) => (reply?["parts"] as JArray)?.FirstOrDefault(p => (int?)p["part"] == k);

        static JToken U6BundleJ(JObject reply, int k, int index) => (U6PartJ(reply, k)?["bundles"] as JArray)?.FirstOrDefault(b => (int?)b["index"] == index);

        /// <summary>A property of every hair material slot of part <paramref name="k"/> that has it (chara.edit.hair materials[].props).</summary>
        static IEnumerable<JToken> U6MatProps(JObject reply, int k, string prop)
        {
            foreach (var m in (U6PartJ(reply, k)?["materials"] as JArray) ?? new JArray())
                if (m["props"] is JObject p && p.ContainsKey(prop)) yield return p[prop];
        }

        /// <summary>(part, renderer, slot) → render queue of every hair material slot.</summary>
        static Dictionary<string, int> U6Queues(JObject reply)
        {
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var p in (reply?["parts"] as JArray) ?? new JArray())
                foreach (var m in (p["materials"] as JArray) ?? new JArray())
                    d[(int?)p["part"] + "/" + (string)m["renderer"] + "/" + (int?)m["slot"]] = (int?)m["renderQueue"] ?? -1;
            return d;
        }

        static IEnumerator U6DrawTypeCheck(AcceptanceCtx c, JObject reply, Dictionary<string, int> q0, string shader, string texColumn, string what)
        {
            var bad = new List<string>();
            int n = 0;
            foreach (var p in (reply?["parts"] as JArray) ?? new JArray())
            {
                string tex = (string)p[texColumn];
                foreach (var m in (p["materials"] as JArray) ?? new JArray())
                {
                    n++;
                    string key = (int?)p["part"] + "/" + (string)m["renderer"] + "/" + (int?)m["slot"];
                    if ((string)m["shader"] != shader) bad.Add(key + " shader " + (string)m["shader"]);
                    if (tex != null && (string)m["mainTex"] != tex) bad.Add(key + " _MainTex " + (string)m["mainTex"] + " vs " + tex);
                    if (!q0.TryGetValue(key, out int q) || q != ((int?)m["renderQueue"] ?? -1)) bad.Add(key + " queue " + (int?)m["renderQueue"] + " vs " + (q0.TryGetValue(key, out int q1) ? q1.ToString() : "-"));
                }
            }
            c.Check(n > 0 && bad.Count == 0, $"{what}: {shader} on every hair material ({n} slots), _MainTex = the rows' {(texColumn == "texC" ? "TexC" : "TexD")}, render queues kept", string.Join("; ", bad.Take(8)));
            yield break;
        }

        static IEnumerator U6Digest(AcceptanceCtx c, string id, JObject[] into)
        {
            yield return c.Call(new { cmd = "chara.edit.digest", key = id, parts = "hair", text = true }, 120);
            into[0] = c.Last != null && c.Last.Ok ? (JObject)c.J.DeepClone() : null;
        }

        static string U6DigestLine(JObject d)
        {
            if (d == null) return "none";
            return string.Join(", ", U6DigestSections.Where(k => d[k] != null && d[k].Type != JTokenType.Null).Select(k => k + " " + U5Short(d[k].Type == JTokenType.String ? (string)d[k] : d[k].ToString(Newtonsoft.Json.Formatting.None))));
        }

        static bool U6SameDigest(JObject a, JObject b, out string detail)
        {
            detail = "";
            if (a == null || b == null) { detail = "no digest"; return false; }
            var bad = new List<string>();
            foreach (var k in U6DigestSections)
            {
                if ((a[k] == null || a[k].Type == JTokenType.Null) && (b[k] == null || b[k].Type == JTokenType.Null)) continue;
                if (JToken.DeepEquals(a[k], b[k])) continue;
                string ta = (string)a["texts"]?[k], tb = (string)b["texts"]?[k];
                bad.Add(k + (ta != null && tb != null ? " (" + U4FirstLine(ta, tb) + ")" : ""));
            }
            detail = bad.Count == 0 ? string.Join(", ", U6DigestSections.Where(k => a[k] != null && a[k].Type != JTokenType.Null)) + " equal" : "differ: " + string.Join("; ", bad);
            return bad.Count == 0;
        }

        static IEnumerator U6FlushInfo(AcceptanceCtx c, string id, string what)
        {
            yield return c.Call(new { cmd = "chara.edit.info", key = id });
            if (c.Last != null && c.Last.Ok)
                c.Info($"UI6 timing ({U6Here()}): {what}'s flush {(double?)c.J["lastFlush"]?["ms"]:0.000} ms, mode {(string)c.J["lastFlush"]?["mode"]}, chain step {U6DynBone(c.J)}, units {AcceptanceCtx.Short(c.J["lastFlush"]?["units"]?.ToString(Newtonsoft.Json.Formatting.None))}");
        }

        /// <summary>The queued thumbnails of the shown lists finish before an item swap (the 4.2 W-1 rule of thumb).</summary>
        static IEnumerator U6Settle(AcceptanceCtx c, CharaEditTab tab)
        {
            for (int i = 0; i < 1800 && tab != null && !tab.Query<PartList>().ToList().All(pl => pl.Thumbnails == null || pl.Thumbnails.Pending == 0); i++) yield return c.Frames(1);
            yield return c.Frames(2);
        }

        /// <summary>The view camera in front of part <paramref name="k"/>'s correction transforms (view state, as group MH).</summary>
        static void U6Frame(UiContext ui, string id, int k, int index = -1)
        {
            var r = ui.Runtime.FindChara(id);
            var go = r?.Context?.Hair != null && k < r.Context.Hair.Length ? r.Context.Hair[k] : null;
            var cmp = go ? go.GetComponent<AIChara.CmpHair>() : null;
            var pts = cmp?.boneInfo?.Where((b, i) => b != null && b.trfCorrect && (index < 0 || i == index)).Select(b => b.trfCorrect.position).ToList() ?? new List<Vector3>();
            var pivot = pts.Count > 0 ? pts.Aggregate(Vector3.zero, (a, b) => a + b) / pts.Count : (r?.Root ? r.Root.transform.position + Vector3.up * 1.5f : Vector3.up * 1.5f);
            float yaw = r?.Root ? r.Root.transform.eulerAngles.y + 180f : 180f;
            ui.Viewport.Camera.SetView(pivot, new Vector3(10, yaw, 0), 2.2f, 23);
            ui.Viewport.Camera.Flush();
        }

        /// <summary>A handle of ui.state gizmo.handles: kind marker / axis, the sub-selection address, the axis (axis kind).</summary>
        static IEnumerator U6Handle(AcceptanceCtx c, string kind, string sub, string axis, JObject[] into)
        {
            into[0] = null;
            yield return c.Call(new { cmd = "ui.state" });
            var hs = c.J["gizmo"]?["handles"] as JArray;
            into[0] = hs?.OfType<JObject>().FirstOrDefault(h => (string)h["kind"] == kind && (string)h["sub"] == sub && (axis == null || (string)h["axis"] == axis));
            if (into[0] == null) c.Info($"UI6: gizmo.handles {AcceptanceCtx.Short(hs?.ToString(Newtonsoft.Json.Formatting.None))}");
        }

        /// <summary>Selects the character, opens the inspector's Edit tab and the Hair node by clicks, then part <paramref name="part"/>
        /// (hair-part-&lt;p&gt;; -1 keeps the part).</summary>
        static IEnumerator U6OpenHair(AcceptanceCtx c, U6State s, string id, int part, CharaEditTab[] tab)
        {
            var ui = s.Ui;
            var host = s.S4.Host;
            tab[0] = null;
            if (ui.Selection.Primary != id) { yield return c.Call(new { cmd = "select", key = id }); yield return c.Frames(3); }
            yield return c.Call(new { cmd = "ui.panel", panel = "inspector", op = "focus" });
            yield return c.Frames(4);
            var et = host.Root.Q<CharaEditTab>();
            if (et == null || et.panel == null || et.PrimaryId != id)
            {
                var tb = host.Root.Q<Button>("tab-" + CharaEditTab.TabId);
                if (!c.Check(tb != null, "the character inspector's Edit tab")) yield break;
                yield return U4Click(c, s.S4, tb, "the Edit tab");
                et = host.Root.Q<CharaEditTab>();
            }
            if (!c.Check(et != null && et.panel != null && et.PrimaryId == id, "the Edit tab of " + id + " is shown")) yield break;
            if (et.CurrentNode != CharaEditTab.HairNode)
            {
                yield return U4Click(c, s.S4, et.Q<Button>("edit-tree-hair"), "the Hair node");
                yield return c.Frames(2);
                if (!c.Check(et.CurrentNode == CharaEditTab.HairNode, "Edit › Hair is open (" + et.CurrentNode + ")")) yield break;
            }
            if (part >= 0 && (et.HairParts == null || et.HairParts.Selected != part))
            {
                var pb = et.Q<Button>("hair-part-" + U6PartName(part));
                yield return U4Click(c, s.S4, pb, "hair-part-" + U6PartName(part));
                yield return c.Frames(2);
            }
            if (!c.Check(et.HasSession && et.HairParts != null && (part < 0 || (et.HairParts.Selected == part && et.HairGroups[part]?.panel != null)),
                $"Hair › {(part < 0 ? "-" : U6PartName(part))} is shown with the edit session (part {et.HairParts?.Selected})")) yield break;
            tab[0] = et;
        }

        /// <summary>Picks <paramref name="reference"/> in part <paramref name="k"/>'s style list → one entry "Change item"; waits for
        /// the realization (the facts name the new item) and the page's live gates; gives the new facts.</summary>
        static IEnumerator U6PickItem(AcceptanceCtx c, U6State s, CharaEditTab tab, string id, int k, string reference, string what, HairPartFacts[] facts)
        {
            var ui = s.Ui;
            facts[0] = null;
            var pl = U4List(tab, U6Hair + U6PartName(k) + ".item");
            if (!c.Check(pl != null && U6Shown(pl), $"the {U6PartName(k)} style list")) yield break;
            string cur = U5Ref(ui, id, U6Hair + U6PartName(k) + ".item");
            if (cur == reference) c.Info($"UI6: {what} is already the current style (no entry expected)");
            int u = U4Undo(ui);
            yield return U4ClickCell(c, s.S4, pl, reference, what);
            for (int i = 0; i < 300 && U5Ref(ui, id, U6Hair + U6PartName(k) + ".item") != reference; i++) yield return c.Frames(1);
            HairPartFacts f = null;
            for (int i = 0; i < 300 && !(HairInfo.TryPart(ui.Runtime, id, k, out f) && f != null && f.Item == reference); i++) yield return c.Frames(1);
            // the live gates: the next frames re-evaluate the marked sections after the realizing flush
            for (int i = 0; i < 120 && tab.HairPending > 0; i++) yield return c.Frames(1);
            yield return c.Frames(3);
            U4Entry(c, ui, u, HS2Next.UI.Core.OpLabels.ChangeItem, what + " pick");
            c.Check(U5Ref(ui, id, U6Hair + U6PartName(k) + ".item") == reference && f != null && f.Item == reference,
                $"hair.{U6PartName(k)}.item = {U5Ref(ui, id, U6Hair + U6PartName(k) + ".item")} ({reference}; was {cur}), realized: prefab {f?.PrefabName}");
            facts[0] = f;
        }

        /// <summary>A colour row's picker: a drag in the SV square over 3 frames, Enter → one entry "Edit hair", the colour
        /// changed and the alpha kept.</summary>
        static IEnumerator U6ColourDrag(AcceptanceCtx c, U6State s, VisualElement root, string id, string path, string what,
            float fx = 0.35f, float fy = 0.35f, float dfx = 0.3f, float dfy = 0.3f)
        {
            var ui = s.Ui;
            var row = root.Query<MakerColorRow>().Where(r => r.Path == path).First();
            if (!c.Check(row != null && U6Shown(row), what + " row")) yield break;
            var c0 = U4Vec(ui, id, path);
            int u = U4Undo(ui);
            yield return U4Click(c, s.S4, row.Swatch.Button, what + " swatch");
            yield return c.Frames(4);
            var picker = row.Picker;
            if (!c.Check(picker != null && picker.panel != null, what + ": the colour picker is open")) yield break;
            var area = picker.Q("sv");
            if (!c.Check(area != null && area.worldBound.width > 0, what + ": the picker's SV square")) yield break;
            var b = area.worldBound;
            var p0 = new Vector2(b.xMin + b.width * fx, b.yMin + b.height * fy);
            var d = new Vector2(b.width * dfx, b.height * dfy);
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
            U4Entry(c, ui, u, U6EditHair, what + " picker session");
            c.Check(c0 != null && c1 != null && c0.Length == 4 && c1.Length == 4 && (c1[0] != c0[0] || c1[1] != c0[1] || c1[2] != c0[2]) && c1[3] == c0[3],
                $"{what}: the stored colour changed, alpha kept ({U4Num(c0)} → {U4Num(c1)})");
        }

        /// <summary>
        /// A pointer drag on a slider's knob in three moves (20 % of the track, away from the nearer end), sent as separate
        /// ui.input calls (down + move, move, move + up) so that <paramref name="during"/> (when given) collects the chain step
        /// of the last flush between them (chara.edit.info).
        /// </summary>
        static IEnumerator U6DragSplit(AcceptanceCtx c, U6State s, HS2Next.UI.Widgets.NumberSlider ctl, float v0, string what, string id, List<string> during)
        {
            var knob = ctl?.Slider?.Q("unity-dragger");
            if (!c.Check(knob != null, what + ": the slider's knob")) yield break;
            var kp = new Vector2[1];
            yield return U4Reveal(c, s.S4, knob, what + " knob", kp);
            float track = ctl.Slider.Q("unity-drag-container")?.worldBound.width ?? 100f;
            float dx = (v0 > 0.5f ? -1 : 1) * Mathf.Max(8f, track * 0.2f);
            var p0 = kp[0];
            var calls = new[]
            {
                new object[] { new { type = "pointer", phase = "down", x = p0.x, y = p0.y, button = 0 }, new { type = "pointer", phase = "move", x = p0.x + dx / 3, y = p0.y, button = 0 } },
                new object[] { new { type = "pointer", phase = "move", x = p0.x + 2 * dx / 3, y = p0.y, button = 0 } },
                new object[] { new { type = "pointer", phase = "move", x = p0.x + dx, y = p0.y, button = 0 }, new { type = "pointer", phase = "up", x = p0.x + dx, y = p0.y, button = 0 } },
            };
            for (int i = 0; i < calls.Length; i++)
            {
                yield return c.Call(new { cmd = "ui.input", frames = 2, events = calls[i] });
                c.CheckOk($"ui.input drag on {what} ({i + 1} of 3)");
                if (during != null && i < calls.Length - 1)
                {
                    yield return c.Call(new { cmd = "chara.edit.info", key = id });
                    during.Add(U6DynBone(c.J) + ((bool?)c.J["hair"]?["deferred"] == true ? " (light pending)" : ""));
                }
            }
            yield return c.Frames(4);
            c.Check(!ctl.Dragging, what + ": no drag transaction left open");
        }
    }
}
