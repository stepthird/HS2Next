// UI9 of the bridge acceptance (Phase 4.6 integrator T): the Edit tab's Character and File pages driven through the bridge's
// ui.* commands in Editor play (BridgeTest.RunPlayUi after UI8, or BridgeTest.RunPlayUi9 alone) and in the app player
// (-bridgeAccept with -ui). Without a running UI the case is skipped with a warning (ui.state enabled false).
// What is read how: the UI facts (a row or line shown, a dialog open, the preview, the load dialog's flags and file) through
// ui.query and ui.state (modal, makerFile); the documents through chara.edit.get; the history through `history`; the saved
// files through the save-format reader (manifest, PNG header and size of the thumbnail, canonical document bytes; no image is
// opened). Actions: the element is found by its name in the app's UI tree, revealed (scroll views scrolled to it, a toast over
// it waited for) and clicked or typed into through ui.input. The files go to the run's own UserData folder.
//   setup: file.new, add.female (BR-Chan), selected; the documents of the import kept (= the state after the setup).
//   1 edit.show each of the seven nodes: no "later" note anywhere (female).
//   2 the Character page: the sex line reads female, the futanari line and the game-parameter lines are read-only lines (no
//     input element), «なし» hidden (the card was imported), the provenance line names the card file; «名前» typed "UI9 Chara"
//     → one entry "Rename character", profile.name = "UI9 Chara".
//   3 the File page: «正面», «撮影し直す» → the preview 504 × 704 framing front; «現在の視点» → framing view.
//   4 «名前を付けて保存…»: the prompt's text is "UI9 Chara", OK → <UserData>/chara/UI9 Chara.hs2nchara: app.version =
//     Application.version, prerelease null, the thumbnail 504 × 704 and larger than the placeholder, every requires item of an
//     installed GUID named, chara.json / coord.json canonical bytes = the node's; no history entry.
//   5 «コーデを保存…»: «コーデ名» "UI9 Coord" → <UserData>/coordinate/female/UI9 Coord.hs2ncoord: the thumbnail 504 × 704 and not
//     the placeholder, coord.json = the node's with the typed name, import null.
//   6 edits: Face › Shape chin width drag, Hair › back base colour picked, Clothes › top colour 1 picked, Body › Shape height drag
//     (four entries).
//   7 «キャラを読込…» «UI9 Chara» with only «顔» → one entry "Load character"; face = the file's, the rest = step 6's.
//   8 the same with «髪», «体», «コーデ» (each its part only; then both documents = the file's); «名前» "X", then «キャラ設定»
//     only → profile = the file's (the name "UI9 Chara" again).
//   9 «キャラを読込…» the HS2 card with all five → the documents = the setup's import, the provenance names the card (the
//     reply of chara.edit.get "import" and the Character page's line).
//  10 edit.undo back to the setup (documents equal), edit.redo to the end (documents = step 9's).
//  11 «コーデを読込…» «UI9 Coord» «全て読込み» → coord = the file's (its name included); the dialog lists the saved file.
//  12 add.female {path: UI9 Chara.hs2nchara} → a new character whose documents' canonical bytes = the file's.
//  13 male: HERO107 added; the seven nodes without "later"; the sex line male, no futanari line; «名前を付けて保存…» "UI9 Hero"
//     and «コーデを保存…» "UI9 Hero Coord" (the male folder); a chin drag, then «顔» only from "UI9 Hero" → face = the file's;
//     the load dialog's five parts ticked again (the view-state option back to its default).
//  14 file.saveAs → file.new → file.open: the three characters' documents equal before / after.
//  15 the thumbnail isolation with a vanilla map loaded: BR-Chan in a new scene, the scene clock paused, the front character
//     thumbnail and the outfit thumbnail saved twice without a map (equal bytes), then with map 10 loaded: bytes equal to the
//     empty-scene saves.
// Every step checks the history entry count and label; no chara.editFailed may be logged.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Import;
using HS2Next.Save;
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
        const string U9CharaName = "UI9 Chara", U9CoordName = "UI9 Coord", U9HeroName = "UI9 Hero", U9HeroCoordName = "UI9 Hero Coord";
        const string U9Clock = "2026-10-26T00:00:00Z";
        /// <summary>The vanilla map of step 15 (a room the acceptance's item case loads too).</summary>
        public static int Ui9MapNo = 10;
        static readonly string[] U9Flags = { "face", "body", "hair", "coordinate", "profile" };
        const string U9ProfileSection = "edit-section-character.character.profile", U9FileSection = "edit-section-file.file.character";

        sealed class U9State
        {
            public U4State S4;
            public UiHost Host => S4.Host;
            public UiContext Ui => S4.Ui;
            public string F, M, Added;
            public string Card;
            public int U0;
            public JObject Docs0, Docs9;
            public string CharaFile, CoordFile;
            public int EditFailed;
        }

        public static IEnumerator Ui9(AcceptanceCtx c)
        {
            c.Id = "UI9";
            yield return c.Call(new { cmd = "ui.state" });
            if (c.Last == null || !c.Last.Ok || (bool?)c.J["enabled"] != true)
            {
                c.Warn("UI9 skipped: the Studio UI is not running (start with -ui; " + AcceptanceCtx.Short(c.Last?.ToString()) + ")");
                yield break;
            }
            var s = new U9State { S4 = new U4State { Host = UiHost.Current } };
            s.S4.Ui = s.S4.Host?.Context;
            if (!c.Check(s.Ui != null, "UiHost in the app's process")) yield break;
            Application.LogCallback onLog = (m, st, t) => { if (m != null && m.Contains("chara.editFailed")) s.EditFailed++; };
            Application.logMessageReceived += onLog;
            var total = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                U9Clean(c, s);
                yield return U9Female(c, s);
                c.Info($"UI9: female part {total.Elapsed.TotalSeconds:F1} s");
                if (s.F != null)
                {
                    total.Restart();
                    yield return U9Male(c, s);
                    c.Info($"UI9: male part {total.Elapsed.TotalSeconds:F1} s");
                    total.Restart();
                    yield return U9Scene(c, s);
                    c.Info($"UI9: scene part {total.Elapsed.TotalSeconds:F1} s");
                }
                total.Restart();
                yield return U9MapIsolation(c, s);
                c.Info($"UI9: map isolation {total.Elapsed.TotalSeconds:F1} s");
            }
            finally
            {
                Application.logMessageReceived -= onLog;
                PartSelectorState.Clothes.Part = 0;
            }
            c.Id = "UI9";
            c.Check(s.EditFailed == 0, $"no chara.editFailed logged during UI9 ({s.EditFailed})");
        }

        // ---------------------------------------------------------------- the female part (steps 1–12)

        static IEnumerator U9Female(AcceptanceCtx c, U9State s)
        {
            var ui = s.Ui;
            var e1 = new JObject[1];
            var n1 = new int[1];
            var v1 = new JObject[1];
            var ok = new bool[1];
            var paths = new string[1];

            // ---- setup: a new scene with BR-Chan
            c.Id = "UI9.0";
            yield return c.Call(new { cmd = "ui.action", action = "file.new", args = new { force = true } });
            c.CheckOk("ui.action file.new {force}");
            s.Card = Path.Combine(AppCore.Current.Paths.Hs2CharaDir(1) ?? "", c.FemaleRel ?? "");
            yield return c.Call(new { cmd = "ui.action", action = "add.female", args = new { path = s.Card } }, 180);
            string id = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && !string.IsNullOrEmpty(id), "ui.action add.female {path} → a new character (BR-Chan)", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            yield return U5WaitBuilt(c, ui, id);
            if (!c.Check(ui.Runtime.FindChara(id)?.State == RealizationState.Built, "the character is built")) yield break;
            yield return c.Call(new { cmd = "select", key = id });
            yield return c.Frames(3);
            s.F = id;
            yield return U7Undo(c, n1);
            s.U0 = n1[0];
            yield return U9Docs(c, id, v1);
            s.Docs0 = v1[0];
            if (!c.Check(s.Docs0?["chara"] != null && s.Docs0?["coord"] != null, "chara.edit.get of the setup (the card's import)")) yield break;

            // ---- 1: the seven nodes without "later"
            c.Id = "UI9.1";
            yield return U9Nodes(c, s, "female");

            // ---- 2: the Character page
            c.Id = "UI9.2";
            yield return U9Open(c, s, id, "character", U9ProfileSection, ok);
            if (!ok[0]) yield break;
            {
                var sexRow = U9El(s, "field-appearance.chara.profile.sex");
                string sexText = sexRow?.Q<Label>("value")?.text;
                c.Check(sexRow != null && sexText == L10n.Get("inspector.charaEdit.character.sex.female"), $"the sex line reads «{sexText}» (female)");
                c.Check(U9NoInput(sexRow), "the sex line holds no input element");
                var fut = U9El(s, "field-appearance.chara.profile.futanari");
                c.Check(fut != null && fut.Q<Label>("hint") != null && U9NoInput(fut), $"the futanari line exists, read-only with its hint ({fut?.Q<Label>("value")?.text})");
                var missing = new List<string>();
                var withInput = new List<string>();
                foreach (var (member, _) in InspectorSchemas.CharacterGameParameters)
                {
                    var row = U9El(s, "field-appearance.chara.profile.hs2." + member);
                    if (row == null) missing.Add(member);
                    else if (!U9NoInput(row)) withInput.Add(member);
                }
                c.Check(missing.Count == 0 && withInput.Count == 0, $"the {InspectorSchemas.CharacterGameParameters.Count} game-parameter lines are shown without an input element (the card was imported)",
                    "missing " + string.Join(", ", missing) + "; with input " + string.Join(", ", withInput));
                yield return U7Q(c, "character-hs2-none", e1);
                c.Check(!U7Vis(e1[0]), "«なし» (character-hs2-none) is hidden");
                string fileLine = U9El(s, "field-provenance.fileName")?.Q<Label>("value")?.text;
                c.Check(fileLine == Path.GetFileName(s.Card), $"the provenance line names the card file («{fileLine}»)");
                yield return U7Q(c, "character-provenance-none", e1);
                c.Check(!U7Vis(e1[0]), "«HS2Next で作成» (character-provenance-none) is hidden");
                yield return U9Rename(c, s, id, U9CharaName);
            }

            // ---- 3: the File page, the framing and the preview
            c.Id = "UI9.3";
            yield return U9Open(c, s, id, "file", U9FileSection, ok);
            if (!ok[0]) yield break;
            {
                var mf = new JObject[1];
                yield return U9WaitPreview(c, -1, mf);
                c.Info($"UI9: the preview after the page was shown: {U5T(mf[0]?["preview"])}");
                yield return U9Click(c, s, "field-maker.thumbnailFraming-0", "«正面» (field-maker.thumbnailFraming-0)");
                yield return c.Frames(3);
                yield return U9WaitPreview(c, -1, mf);
                int r0 = (int?)U9O(U9O(mf[0], "preview"), "renders") ?? 0;
                yield return U9Click(c, s, "file-retake", "«撮影し直す» (file-retake)");
                yield return U9WaitPreview(c, r0 + 1, mf);
                var p = U9O(mf[0], "preview");
                c.Check((string)mf[0]?["thumbnailFraming"] == "front" && (int?)p?["width"] == 504 && (int?)p?["height"] == 704 && (string)p?["framing"] == "front",
                    $"«正面» + «撮影し直す» → the preview 504 × 704, framing front ({U5T(p)})");
                c.Info($"UI9 timing ({U6Here()}): the front preview {(double?)p?["ms"]} ms, shown {(double?)p?["shownWidth"]} × {(double?)p?["shownHeight"]}");
                int r1 = (int?)p?["renders"] ?? 0;
                yield return U9Click(c, s, "field-maker.thumbnailFraming-1", "«現在の視点» (field-maker.thumbnailFraming-1)");
                yield return U9WaitPreview(c, r1 + 1, mf);
                p = U9O(mf[0], "preview");
                c.Check((string)mf[0]?["thumbnailFraming"] == "view" && (int?)p?["width"] == 504 && (int?)p?["height"] == 704 && (string)p?["framing"] == "view",
                    $"«現在の視点» → the preview renders again, framing view ({U5T(p)})");
                c.Info($"UI9 timing ({U6Here()}): the view preview {(double?)p?["ms"]} ms");
            }

            // ---- 4: «名前を付けて保存…»
            c.Id = "UI9.4";
            yield return U9SaveChara(c, s, id, U9CharaName, null, paths);
            s.CharaFile = paths[0];
            if (s.CharaFile == null) yield break;
            U9CheckCharaFile(c, s, id, s.CharaFile);

            // ---- 5: «コーデを保存…»
            c.Id = "UI9.5";
            yield return U9SaveCoord(c, s, id, U9CoordName, paths);
            s.CoordFile = paths[0];
            if (s.CoordFile != null) U9CheckCoordFile(c, s, id, s.CoordFile, U9CoordName);

            // ---- 6: four edits
            c.Id = "UI9.6";
            yield return U7Undo(c, n1);
            int u6 = n1[0];
            yield return U9Edits(c, s, id, true);
            yield return U7Undo(c, n1);
            c.Check(n1[0] == u6 + 4, $"four edits = four history entries ({n1[0] - u6})");
            yield return U9Docs(c, id, v1);
            var docs6 = v1[0];
            if (!c.Check(docs6 != null, "chara.edit.get after the edits")) yield break;
            var file = U9FileDocs(s.CharaFile);
            if (!c.Check(file != null, "the saved character file reads")) yield break;
            foreach (var part in new[] { "face", "hair", "body" })
                c.Check(!JToken.DeepEquals(docs6["chara"]?[part], file["chara"]?[part]), $"chara.{part} differs from the file after the edits");
            c.Check(!JToken.DeepEquals(docs6["coord"]?["clothes"], file["coord"]?["clothes"]), "coord.clothes differs from the file after the edits");

            // ---- 7: «顔» only from the file
            c.Id = "UI9.7";
            var before = docs6;
            yield return U9LoadAndCheck(c, s, id, U9CharaName, s.CharaFile, new[] { "face" }, before, file, ok);
            if (!ok[0]) yield break;

            // ---- 8: «髪», «体», «コーデ»; the name; «キャラ設定»
            c.Id = "UI9.8";
            foreach (var flag in new[] { "hair", "body", "coordinate" })
            {
                yield return U9Docs(c, id, v1);
                yield return U9LoadAndCheck(c, s, id, U9CharaName, s.CharaFile, new[] { flag }, v1[0], file, ok);
                if (!ok[0]) yield break;
            }
            yield return U9Docs(c, id, v1);
            c.Check(JToken.DeepEquals(v1[0]?["chara"], file["chara"]) && JToken.DeepEquals(v1[0]?["coord"], file["coord"]),
                "after «顔», «髪», «体», «コーデ» both documents equal the file's", JToken.DeepEquals(v1[0]?["chara"], file["chara"]) ? U4JsonDiff(file["coord"]?.ToString(Newtonsoft.Json.Formatting.None), v1[0]?["coord"]?.ToString(Newtonsoft.Json.Formatting.None)) : U4JsonDiff(file["chara"]?.ToString(Newtonsoft.Json.Formatting.None), v1[0]?["chara"]?.ToString(Newtonsoft.Json.Formatting.None)));
            yield return U9Open(c, s, id, "character", U9ProfileSection, ok);
            if (!ok[0]) yield break;
            yield return U9Rename(c, s, id, "X");
            yield return U9Docs(c, id, v1);
            yield return U9LoadAndCheck(c, s, id, U9CharaName, s.CharaFile, new[] { "profile" }, v1[0], file, ok);
            if (!ok[0]) yield break;
            yield return U7Get(c, id, new[] { "chara.profile.name" }, v1);
            c.Check((string)v1[0]?["chara.profile.name"] == U9CharaName, $"«キャラ設定» only → profile.name «{(string)v1[0]?["chara.profile.name"]}» again");

            // ---- 9: the HS2 card with all five
            c.Id = "UI9.9";
            yield return U9Load(c, s, id, Path.GetFileNameWithoutExtension(s.Card), s.Card, U9Flags, "the HS2 card with all five", ok);
            if (!ok[0]) yield break;
            yield return U9Docs(c, id, v1);
            s.Docs9 = v1[0];
            c.Check(JToken.DeepEquals(s.Docs9?["chara"], s.Docs0["chara"]) && JToken.DeepEquals(s.Docs9?["coord"], s.Docs0["coord"]),
                "all five from the card → both documents equal the setup's import",
                JToken.DeepEquals(s.Docs9?["chara"], s.Docs0["chara"]) ? U4JsonDiff(s.Docs0["coord"]?.ToString(Newtonsoft.Json.Formatting.None), s.Docs9?["coord"]?.ToString(Newtonsoft.Json.Formatting.None)) : U4JsonDiff(s.Docs0["chara"]?.ToString(Newtonsoft.Json.Formatting.None), s.Docs9?["chara"]?.ToString(Newtonsoft.Json.Formatting.None)));
            yield return U7Get(c, id, new[] { "import" }, v1);
            string impFile = (string)U9O(U9O(v1[0], "import"), "fileName");
            c.Check(impFile == Path.GetFileName(s.Card), $"the import provenance names the card (chara.edit.get import.fileName «{impFile}»)");
            yield return U9Open(c, s, id, "character", U9ProfileSection, ok);
            if (ok[0])
            {
                yield return c.Frames(2);
                string line = U9El(s, "field-provenance.fileName")?.Q<Label>("value")?.text;
                c.Check(line == Path.GetFileName(s.Card), $"the Character page's provenance line names the card («{line}»)");
            }

            // ---- 10: undo to the setup, redo to the end
            c.Id = "UI9.10";
            yield return c.Call(new { cmd = "history" });
            var undo = (c.J["undo"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
            int n10 = undo.Count - s.U0;
            c.Info($"UI9: {n10} history entries since the setup: {string.Join(" | ", undo.Take(Math.Max(0, n10)).Reverse())}");
            for (int i = 0; i < n10; i++)
            {
                yield return c.Call(new { cmd = "ui.action", action = "edit.undo" });
                if (!(c.Last != null && c.Last.Ok)) { c.CheckOk("ui.action edit.undo #" + (i + 1)); break; }
                yield return c.Frames(1);
            }
            yield return U5WaitBuilt(c, ui, id);
            yield return c.Frames(6);
            yield return U7Undo(c, n1);
            c.Check(n1[0] == s.U0, $"undo back to the setup ({n1[0]} entries, {s.U0} after the setup)");
            yield return U9Docs(c, id, v1);
            c.Check(JToken.DeepEquals(v1[0], s.Docs0), "undo to the setup → the documents equal the setup's",
                JToken.DeepEquals(v1[0], s.Docs0) ? "" : U4JsonDiff(s.Docs0.ToString(Newtonsoft.Json.Formatting.None), v1[0]?.ToString(Newtonsoft.Json.Formatting.None)));
            for (int i = 0; i < n10; i++)
            {
                yield return c.Call(new { cmd = "ui.action", action = "edit.redo" });
                if (!(c.Last != null && c.Last.Ok)) { c.CheckOk("ui.action edit.redo #" + (i + 1)); break; }
                yield return c.Frames(1);
            }
            yield return U5WaitBuilt(c, ui, id);
            yield return c.Frames(6);
            yield return U9Docs(c, id, v1);
            c.Check(JToken.DeepEquals(v1[0], s.Docs9), "redo to the end → the documents equal step 9's",
                JToken.DeepEquals(v1[0], s.Docs9) ? "" : U4JsonDiff(s.Docs9?.ToString(Newtonsoft.Json.Formatting.None), v1[0]?.ToString(Newtonsoft.Json.Formatting.None)));

            // ---- 11: «コーデを読込…» «UI9 Coord» «全て読込み»
            c.Id = "UI9.11";
            if (s.CoordFile != null)
            {
                yield return U9Open(c, s, id, "file", U9FileSection, ok);
                if (ok[0])
                {
                    var coordFile = U9FileCoord(s.CoordFile);
                    yield return U7LoadCard(c, new U7State { S4 = s.S4 }, id, "file-load-coord", "coordinate-dialog-browser", U9CoordName, "dialog-button-all", "«全て読込み»");
                    yield return U9Docs(c, id, v1);
                    c.Check(coordFile != null && JToken.DeepEquals(v1[0]?["coord"], coordFile), $"«全て読込み» of «{U9CoordName}» → coord = the file's (name «{(string)v1[0]?["coord"]?["name"]}»)",
                        coordFile == null ? "the file does not read" : U4JsonDiff(coordFile.ToString(Newtonsoft.Json.Formatting.None), v1[0]?["coord"]?.ToString(Newtonsoft.Json.Formatting.None)));
                }
            }

            // ---- 12: the saved character added as a new one
            c.Id = "UI9.12";
            yield return c.Call(new { cmd = "ui.action", action = "add.female", args = new { path = s.CharaFile } }, 180);
            string added = (string)c.J["result"];
            if (c.Check(c.Last != null && c.Last.Ok && !string.IsNullOrEmpty(added), $"ui.action add.female {{path: {Path.GetFileName(s.CharaFile)}}} → a new character", AcceptanceCtx.Short(c.Last?.ToString())))
            {
                yield return U5WaitBuilt(c, ui, added);
                c.Check(ui.Runtime.FindChara(added)?.State == RealizationState.Built, "the added character is built");
                s.Added = added;
                var st = ui.Model.Find(added)?.Appearance?.Stored;
                var lr = CharaContainer.Load(s.CharaFile);
                bool same = st != null && lr.Ok && U9Bytes(st.Chara).SequenceEqual(U9Bytes(lr.Value.Chara)) && U9Bytes(st.Coord).SequenceEqual(U9Bytes(lr.Value.Coord));
                c.Check(same, "its chara.json / coord.json canonical bytes = the file's");
                yield return c.Call(new { cmd = "select", key = id });
                yield return c.Frames(3);
            }
        }

        // ---------------------------------------------------------------- the male part (step 13)

        static IEnumerator U9Male(AcceptanceCtx c, U9State s)
        {
            var ui = s.Ui;
            var e1 = new JObject[1];
            var v1 = new JObject[1];
            var ok = new bool[1];
            var paths = new string[1];
            c.Id = "UI9.13";
            string card = Path.Combine(AppCore.Current.Paths.Hs2CharaDir(0) ?? "", Ui2MaleRel ?? "");
            yield return c.Call(new { cmd = "ui.action", action = "add.male", args = new { path = card } }, 180);
            string id = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && !string.IsNullOrEmpty(id), "ui.action add.male {path} → a new character (HERO107)", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            yield return U5WaitBuilt(c, ui, id);
            if (!c.Check(ui.Runtime.FindChara(id)?.State == RealizationState.Built, "the male character is built")) yield break;
            yield return c.Call(new { cmd = "select", key = id });
            yield return c.Frames(3);
            s.M = id;
            yield return U9Nodes(c, s, "male");
            yield return U9Open(c, s, id, "character", U9ProfileSection, ok);
            if (!ok[0]) yield break;
            string sexText = U9El(s, "field-appearance.chara.profile.sex")?.Q<Label>("value")?.text;
            c.Check(sexText == L10n.Get("inspector.charaEdit.character.sex.male"), $"the sex line reads «{sexText}» (male)");
            yield return U7Q(c, "field-appearance.chara.profile.futanari", e1);
            c.Check(!U7Vis(e1[0]), "no futanari line for the male (ui.query field-appearance.chara.profile.futanari)");
            yield return U9SaveChara(c, s, id, U9HeroName, InspectorFlows.CharaInitialName(ui, id), paths);
            string heroFile = paths[0];
            if (heroFile != null) U9CheckCharaFile(c, s, id, heroFile);
            yield return U9SaveCoord(c, s, id, U9HeroCoordName, paths);
            if (paths[0] != null)
            {
                string want = Path.Combine(CoordinateBrowser.OwnDir(ui, 0) ?? "", U9HeroCoordName + SaveFormats.CoordExtension);
                c.Check(string.Equals(Path.GetFullPath(paths[0]), Path.GetFullPath(want), StringComparison.OrdinalIgnoreCase), $"the male outfit file is in the male folder ({paths[0]})");
                U9CheckCoordFile(c, s, id, paths[0], U9HeroCoordName);
            }
            if (heroFile == null) yield break;
            yield return U9Edits(c, s, id, false);
            yield return U9Docs(c, id, v1);
            var file = U9FileDocs(heroFile);
            if (!c.Check(file != null, "the saved male file reads")) yield break;
            c.Check(!JToken.DeepEquals(v1[0]?["chara"]?["face"], file["chara"]?["face"]), "the face differs from the file after the chin drag");
            yield return U9LoadAndCheck(c, s, id, U9HeroName, heroFile, new[] { "face" }, v1[0], file, ok);
            // the load dialog's parts back to the default (all five), cancelled
            yield return U9Open(c, s, id, "file", U9FileSection, ok);
            if (!ok[0]) yield break;
            var n = new int[1];
            yield return U7Undo(c, n);
            yield return U9Click(c, s, "file-load-chara", "«キャラを読込…» (file-load-chara)");
            var modal = new string[1];
            yield return U9WaitModal(c, CharaLoadDialog.DialogId, 120, modal);
            if (c.Check(modal[0] == CharaLoadDialog.DialogId, $"the load dialog is open (modal {modal[0]})"))
            {
                yield return U9SetFlags(c, s, U9Flags);
                yield return U9Click(c, s, "dialog-button-cancel", "«キャンセル»");
                yield return U9WaitModal(c, null, 120, modal);
                var mf = new JObject[1];
                yield return U9FileState(c, mf);
                var flags = (mf[0]?["loadFlags"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
                c.Check(modal[0] == null && flags.SequenceEqual(U9Flags), $"«キャンセル» closes the dialog; the remembered parts are the five again ({string.Join(",", flags)})");
            }
            yield return U7Undo(c, new int[1]);
            yield return c.Call(new { cmd = "history" });
            c.Check(((c.J["undo"] as JArray)?.Count ?? -1) == n[0], "opening and cancelling the load dialog records nothing");
        }

        // ---------------------------------------------------------------- the scene part (step 14)

        static IEnumerator U9Scene(AcceptanceCtx c, U9State s)
        {
            var ui = s.Ui;
            var v1 = new JObject[1];
            c.Id = "UI9.14";
            var ids = new[] { s.F, s.Added, s.M }.Where(x => x != null).ToList();
            var before = new Dictionary<string, JObject>();
            foreach (var id in ids)
            {
                yield return U9Docs(c, id, v1);
                before[id] = v1[0];
            }
            yield return c.Call(new { cmd = "ui.action", action = "file.saveAs", args = new { name = "ui9_acceptance", overwrite = true } }, 120);
            string saved = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && saved != null && File.Exists(saved), "ui.action file.saveAs {name} → " + saved)) yield break;
            yield return c.Call(new { cmd = "ui.action", action = "file.new", args = new { force = true } });
            c.CheckOk("ui.action file.new {force}");
            yield return c.Call(new { cmd = "ui.action", action = "file.open", args = new { path = saved } }, 180);
            c.CheckOk("ui.action file.open {path}");
            foreach (var id in ids)
            {
                yield return U5WaitBuilt(c, ui, id);
                c.Check(ui.Runtime.FindChara(id)?.State == RealizationState.Built, $"{id} is built after the reopen (same node id)");
                yield return U9Docs(c, id, v1);
                c.Check(before[id] != null && JToken.DeepEquals(before[id], v1[0]), $"{id}: the documents equal before / after save → new → open",
                    JToken.DeepEquals(before[id], v1[0]) ? "" : U4JsonDiff(before[id]?.ToString(Newtonsoft.Json.Formatting.None), v1[0]?.ToString(Newtonsoft.Json.Formatting.None)));
            }
        }

        // ---------------------------------------------------------------- step 15: thumbnail isolation with a map

        static IEnumerator U9MapIsolation(AcceptanceCtx c, U9State s)
        {
            var ui = s.Ui;
            c.Id = "UI9.15";
            yield return c.Call(new { cmd = "ui.action", action = "file.new", args = new { force = true } });
            c.CheckOk("ui.action file.new {force}");
            string card = s.Card ?? Path.Combine(AppCore.Current.Paths.Hs2CharaDir(1) ?? "", c.FemaleRel ?? "");
            yield return c.Call(new { cmd = "ui.action", action = "add.female", args = new { path = card } }, 180);
            string id = (string)c.J["result"];
            if (!c.Check(c.Last != null && c.Last.Ok && !string.IsNullOrEmpty(id), "a new scene with BR-Chan", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            yield return U5WaitBuilt(c, ui, id);
            if (!c.Check(ui.Runtime.FindChara(id)?.State == RealizationState.Built, "the character is built")) yield break;
            yield return c.Frames(10);
            yield return c.Call(new { cmd = "scene.pause", paused = true });
            c.CheckOk("scene.pause {paused: true} (the scene clock stands still between the saves)");
            var shots = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            yield return U9IsoSave(c, id, "ui9_iso_1", shots);
            yield return U9IsoSave(c, id, "ui9_iso_2", shots);
            bool charaStable = U9Eq(shots, "chara.ui9_iso_1", "chara.ui9_iso_2"), coordStable = U9Eq(shots, "coord.ui9_iso_1", "coord.ui9_iso_2");
            c.Check(charaStable && coordStable, $"two saves without a map give equal thumbnails (character {U9H(shots, "chara.ui9_iso_1")} / {U9H(shots, "chara.ui9_iso_2")}, outfit {U9H(shots, "coord.ui9_iso_1")} / {U9H(shots, "coord.ui9_iso_2")})");
            yield return c.Call(new { cmd = "map.load", no = Ui9MapNo }, 300);
            if (!c.Check(c.Last != null && c.Last.Ok && (string)c.J["state"] == "Loaded", $"map.load {Ui9MapNo} (a vanilla map at the origin)", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            yield return c.Frames(10);
            yield return U5WaitBuilt(c, ui, id);
            yield return U9IsoSave(c, id, "ui9_iso_map", shots);
            c.Check(U9Eq(shots, "chara.ui9_iso_1", "chara.ui9_iso_map"), $"the front character thumbnail with map {Ui9MapNo} loaded = the empty scene's (PNG bytes; {U9H(shots, "chara.ui9_iso_map")})");
            c.Check(U9Eq(shots, "coord.ui9_iso_1", "coord.ui9_iso_map"), $"the outfit thumbnail with map {Ui9MapNo} loaded = the empty scene's (PNG bytes; {U9H(shots, "coord.ui9_iso_map")})");
            yield return c.Call(new { cmd = "scene.pause", paused = false });
            c.CheckOk("scene.pause {paused: false}");
            yield return c.Call(new { cmd = "ui.action", action = "file.new", args = new { force = true } });
            c.CheckOk("ui.action file.new {force} (the map unloaded)");
        }

        /// <summary>chara.save and coord.save of <paramref name="id"/> with rendered thumbnails and a fixed clock; the PNG prefixes
        /// of both files into <paramref name="into"/> ("chara.&lt;name&gt;", "coord.&lt;name&gt;").</summary>
        static IEnumerator U9IsoSave(AcceptanceCtx c, string id, string name, Dictionary<string, byte[]> into)
        {
            yield return c.Call(new { cmd = "chara.save", key = id, name, overwrite = true, thumb = "render", clock = U9Clock }, 120);
            string cp = (string)c.J["path"];
            if (c.Check(c.Last != null && c.Last.Ok && (string)c.J["thumbnail"] == "render" && cp != null, $"chara.save {{name: {name}, thumb: render}} → a rendered thumbnail (framing {(string)c.J["framing"]})", AcceptanceCtx.Short(c.Last?.ToString())))
            {
                var lr = CharaContainer.Load(cp);
                into["chara." + name] = lr.Ok ? lr.Value.Thumbnail : null;
            }
            yield return c.Call(new { cmd = "coord.save", key = id, name, coordName = "UI9 isolation", overwrite = true, thumb = "render", clock = U9Clock }, 120);
            string op = (string)c.J["path"];
            if (c.Check(c.Last != null && c.Last.Ok && (string)c.J["thumbnail"] == "render" && op != null, $"coord.save {{name: {name}, thumb: render}} → a rendered thumbnail", AcceptanceCtx.Short(c.Last?.ToString())))
            {
                var lr = CoordContainer.Load(op);
                into["coord." + name] = lr.Ok ? lr.Value.Thumbnail : null;
            }
        }

        /// <summary>A member of a JSON object (null when the token is not an object: a JSON null).</summary>
        static JToken U9O(JToken t, string k) => t is JObject o ? o[k] : null;

        static bool U9Eq(Dictionary<string, byte[]> d, string a, string b) =>
            d.TryGetValue(a, out var x) && d.TryGetValue(b, out var y) && x != null && y != null && x.SequenceEqual(y);

        static string U9H(Dictionary<string, byte[]> d, string k) =>
            d.TryGetValue(k, out var x) && x != null ? U5Short(ResStore.Sha256Hex(x)) + " " + x.Length + " B" : "none";

        // ---------------------------------------------------------------- helpers

        /// <summary>Deletes the files a previous UI9 run left in this run's own UserData folder (only these names; never in the
        /// HS2 install).</summary>
        static void U9Clean(AcceptanceCtx c, U9State s)
        {
            var paths = AppCore.Current?.Paths;
            if (paths?.CharaDir == null || paths.CoordDir == null) return;
            string hs2 = paths.Hs2UserData == null ? null : Path.GetFullPath(paths.Hs2UserData);
            if (hs2 != null && Path.GetFullPath(paths.UserData).StartsWith(hs2, StringComparison.OrdinalIgnoreCase)) { c.Info("UI9: UserData lies in the HS2 install; nothing deleted"); return; }
            var files = new List<string>
            {
                Path.Combine(paths.CharaDir, U9CharaName + SaveFormats.CharaExtension),
                Path.Combine(paths.CharaDir, U9HeroName + SaveFormats.CharaExtension),
                Path.Combine(paths.CoordDir, "female", U9CoordName + SaveFormats.CoordExtension),
                Path.Combine(paths.CoordDir, "male", U9HeroCoordName + SaveFormats.CoordExtension),
            };
            int n = 0;
            foreach (var f in files)
                if (File.Exists(f)) { File.Delete(f); n++; }
            if (n > 0)
            {
                c.Info($"UI9: {n} files of an earlier run deleted from the run's UserData");
                CoordinateBrowser.Invalidate(s.Ui, FileBrowserKind.Character, 0);
                CoordinateBrowser.Invalidate(s.Ui, FileBrowserKind.Character, 1);
                CoordinateBrowser.Invalidate(s.Ui, FileBrowserKind.Coordinate, 0);
                CoordinateBrowser.Invalidate(s.Ui, FileBrowserKind.Coordinate, 1);
            }
        }

        /// <summary>The shown element of that name in the app's UI (under <paramref name="scope"/>), or null.</summary>
        static VisualElement U9El(U9State s, string name, VisualElement scope = null)
        {
            var root = scope ?? s.Host?.Root;
            return root?.Query<VisualElement>(name).Where(x => x.panel != null && x.worldBound.width > 0 && U6Shown(x)).First();
        }

        static IEnumerator U9Click(AcceptanceCtx c, U9State s, string name, string what, VisualElement scope = null) => U4Click(c, s.S4, U9El(s, name, scope), what);

        /// <summary>A read-only line: only labels under the row (no text field, toggle, slider or button).</summary>
        static bool U9NoInput(VisualElement row) => row != null && row.Query<VisualElement>().ToList().All(e => e == row || e is Label);

        static byte[] U9Bytes(CharaDocument d) => d == null ? new byte[0] : CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, d.Write);
        static byte[] U9Bytes(CoordDocument d) => d == null ? new byte[0] : CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, d.Write);
        static JObject U9Json(byte[] b) { try { return JObject.Parse(Encoding.UTF8.GetString(b)); } catch (Exception) { return null; } }

        /// <summary>A character file's documents as chara.edit.get gives them ({chara, coord}); null when it does not read.</summary>
        static JObject U9FileDocs(string path)
        {
            if (path == null) return null;
            var lr = CharaContainer.Load(path);
            if (!lr.Ok) return null;
            return new JObject { ["chara"] = U9Json(U9Bytes(lr.Value.Chara)), ["coord"] = U9Json(U9Bytes(lr.Value.Coord)) };
        }

        /// <summary>An outfit file's coord.json; null when it does not read.</summary>
        static JObject U9FileCoord(string path)
        {
            if (path == null) return null;
            var lr = CoordContainer.Load(path);
            return lr.Ok ? U9Json(U9Bytes(lr.Value.Coord)) : null;
        }

        static IEnumerator U9Docs(AcceptanceCtx c, string id, JObject[] into)
        {
            yield return c.Call(new { cmd = "chara.edit.get", key = id }, 120);
            into[0] = c.Last != null && c.Last.Ok ? c.J["values"]?.DeepClone() as JObject : null;
        }

        static IEnumerator U9FileState(AcceptanceCtx c, JObject[] into)
        {
            yield return c.Call(new { cmd = "ui.state" });
            into[0] = c.Last != null && c.Last.Ok ? c.J["makerFile"]?.DeepClone() as JObject : null;
        }

        /// <summary>Polls ui.state until the open dialog is <paramref name="want"/> (null = none).</summary>
        static IEnumerator U9WaitModal(AcceptanceCtx c, string want, int frames, string[] into)
        {
            for (int i = 0; i < frames; i += 2)
            {
                yield return c.Call(new { cmd = "ui.state" });
                into[0] = (string)c.J["modal"];
                if (into[0] == want) yield break;
                yield return c.Frames(2);
            }
        }

        /// <summary>Polls ui.state until the preview is rendered (not pending) and has at least <paramref name="renders"/> renders.</summary>
        static IEnumerator U9WaitPreview(AcceptanceCtx c, int renders, JObject[] into)
        {
            for (int i = 0; i < 400; i += 2)
            {
                yield return U9FileState(c, into);
                var p = into[0]?["preview"];
                if (p != null && p.Type == JTokenType.Object && (bool?)p["pending"] == false && ((int?)p["renders"] ?? 0) >= Math.Max(1, renders)) yield break;
                yield return c.Frames(2);
            }
        }

        /// <summary>edit.show of each of the seven nodes: the node is shown and no note says that its page comes later.</summary>
        static IEnumerator U9Nodes(AcceptanceCtx c, U9State s, string who)
        {
            string later = L10n.Get("inspector.charaEdit.later");
            var bad = new List<string>();
            foreach (var node in InspectorSchemas.CharaEditTree)
            {
                yield return c.Call(new { cmd = "ui.action", action = "edit.show", args = new { node } });
                if (!(c.Last != null && c.Last.Ok)) { bad.Add(node + ": " + AcceptanceCtx.Short(c.Last?.ToString())); continue; }
                yield return c.Frames(3);
                var mf = new JObject[1];
                yield return U9FileState(c, mf);
                string shown = (string)U9O(mf[0], "node");
                if (shown != node) { bad.Add(node + ": the Edit tab shows " + (shown ?? "no node") + " (ui.state makerFile.node)"); continue; }
                yield return c.Call(new { cmd = "ui.query", name = ".hs-inspector__note", all = true });
                var notes = (c.J["elements"] as JArray) ?? new JArray();
                if (notes.Any(x => (bool?)x["visible"] == true && (string)x["text"] == later)) bad.Add(node + ": the \"later\" note");
            }
            c.Check(bad.Count == 0, $"{who}: edit.show of the seven nodes ({string.Join(", ", InspectorSchemas.CharaEditTree)}), none shows the \"later\" note", string.Join("; ", bad));
        }

        /// <summary>Selects the character, shows the inspector's Edit tab and a node by clicks until <paramref name="shownName"/>
        /// is shown (ui.query).</summary>
        static IEnumerator U9Open(AcceptanceCtx c, U9State s, string id, string node, string shownName, bool[] ok)
        {
            ok[0] = false;
            var e = new JObject[1];
            yield return c.Call(new { cmd = "ui.state" });
            if ((string)c.J["selection"]?["primary"] != id) { yield return c.Call(new { cmd = "select", key = id }); yield return c.Frames(3); }
            yield return c.Call(new { cmd = "ui.panel", panel = "inspector", op = "focus" });
            yield return c.Frames(3);
            yield return U7Q(c, "edit-tree-" + node, e);
            if (!U7Vis(e[0]))
            {
                yield return U9Click(c, s, "tab-" + CharaEditTab.TabId, "the Edit tab");
                yield return U7WaitShown(c, "edit-tree-" + node, true, 60, e);
            }
            if (!c.Check(U7Vis(e[0]), "the Edit tab of " + id + " is shown (edit-tree-" + node + ")")) yield break;
            yield return U7Q(c, shownName, e);
            if (!U7Vis(e[0]))
            {
                yield return U9Click(c, s, "edit-tree-" + node, "the " + node + " node");
                yield return U7WaitShown(c, shownName, true, 60, e);
            }
            ok[0] = c.Check(U7Vis(e[0]), "Edit › " + node + " is shown (" + shownName + ")");
            yield return c.Frames(2);
        }

        /// <summary>«名前» typed (Enter commits) → one entry "Rename character" and profile.name holds it.</summary>
        static IEnumerator U9Rename(AcceptanceCtx c, U9State s, string id, string name)
        {
            var tf = U9El(s, "field-appearance.chara.profile.name")?.Q<TextField>("text");
            if (!c.Check(tf != null, "the «名前» field (field-appearance.chara.profile.name)")) yield break;
            var n = new int[1];
            yield return U7Undo(c, n);
            yield return U4Type(c, s.S4, tf, name, "«名前»");
            yield return c.Frames(3);
            yield return U7Entry(c, n[0], OpLabels.RenameCharacter, $"«名前» typed «{name}»");
            var v = new JObject[1];
            yield return U7Get(c, id, new[] { "chara.profile.name" }, v);
            c.Check((string)v[0]?["chara.profile.name"] == name, $"profile.name = «{(string)v[0]?["chara.profile.name"]}»");
        }

        /// <summary>«名前を付けて保存…»: the prompt (its initial text = <paramref name="wantInitial"/> unless null), the typed
        /// name, OK; an overwrite question is answered OK. The file's path into <paramref name="path"/> (null on failure).</summary>
        static IEnumerator U9SaveChara(AcceptanceCtx c, U9State s, string id, string typed, string wantInitial, string[] path)
        {
            path[0] = null;
            var ok = new bool[1];
            yield return U9Open(c, s, id, "file", U9FileSection, ok);
            if (!ok[0]) yield break;
            var n = new int[1];
            yield return U7Undo(c, n);
            yield return U9Click(c, s, "file-save-chara", "«名前を付けて保存…» (file-save-chara)");
            var modal = new string[1];
            yield return U9WaitModal(c, InspectorFlows.SaveCharaPromptId, 120, modal);
            if (!c.Check(modal[0] == InspectorFlows.SaveCharaPromptId, $"the name prompt opens (modal {modal[0]})")) yield break;
            var e = new JObject[1];
            yield return U7Q(c, "dialog-input", e);
            string initial = (string)e[0]?["value"];
            c.Check(initial == (wantInitial ?? typed), $"the prompt's text is «{initial}» (the character's name)");
            if (initial != typed) yield return U7TypeText(c, new U7State { S4 = s.S4 }, U9El(s, "dialog-input"), typed, "the prompt's field");
            yield return U9Click(c, s, "dialog-button-ok", "the prompt's OK");
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "ui.state" });
            if ((string)c.J["modal"] == InspectorFlows.OverwriteQuestionId)
            {
                c.Info("UI9: the file existed; the overwrite question answered OK");
                yield return U9Click(c, s, "dialog-button-ok", "the overwrite question's OK");
            }
            yield return U9WaitModal(c, null, 120, modal);
            string full = Path.Combine(AppCore.Current.Paths.CharaDir, InspectorFlows.Safe(typed, "chara") + SaveFormats.CharaExtension);
            for (int i = 0; i < 120 && !File.Exists(full); i++) yield return c.Frames(1);
            if (!c.Check(modal[0] == null && File.Exists(full), $"OK → {Path.GetFileName(full)} written under <UserData>/chara")) yield break;
            yield return c.Call(new { cmd = "history" });
            c.Check(((c.J["undo"] as JArray)?.Count ?? -1) == n[0], "the save records no history entry");
            path[0] = full;
        }

        /// <summary>«コーデを保存…»: the prompt «コーデ名», the typed name, OK. The file's path into <paramref name="path"/>.</summary>
        static IEnumerator U9SaveCoord(AcceptanceCtx c, U9State s, string id, string typed, string[] path)
        {
            path[0] = null;
            var ok = new bool[1];
            yield return U9Open(c, s, id, "file", U9FileSection, ok);
            if (!ok[0]) yield break;
            var n = new int[1];
            yield return U7Undo(c, n);
            yield return U9Click(c, s, "file-save-coord", "«コーデを保存…» (file-save-coord)");
            var modal = new string[1];
            yield return U9WaitModal(c, InspectorFlows.SaveCoordPromptId, 120, modal);
            if (!c.Check(modal[0] == InspectorFlows.SaveCoordPromptId, $"the «コーデ名» prompt opens (modal {modal[0]})")) yield break;
            var e = new JObject[1];
            yield return U7Q(c, "dialog-input", e);
            string initial = (string)e[0]?["value"];
            c.Check(initial == InspectorFlows.CoordInitialName(s.Ui, id) || !string.IsNullOrEmpty(initial), $"the prompt's text «{initial}» (the worn coordinate's name or a stamp)");
            yield return U7TypeText(c, new U7State { S4 = s.S4 }, U9El(s, "dialog-input"), typed, "the prompt's field");
            yield return U9Click(c, s, "dialog-button-ok", "the prompt's OK");
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "ui.state" });
            if ((string)c.J["modal"] == InspectorFlows.OverwriteQuestionId)
            {
                c.Info("UI9: the outfit file existed; the overwrite question answered OK");
                yield return U9Click(c, s, "dialog-button-ok", "the overwrite question's OK");
            }
            yield return U9WaitModal(c, null, 120, modal);
            string full = InspectorFlows.CoordPath(s.Ui, id, typed);
            for (int i = 0; i < 120 && full != null && !File.Exists(full); i++) yield return c.Frames(1);
            if (!c.Check(modal[0] == null && full != null && File.Exists(full), $"OK → {full} written")) yield break;
            yield return c.Call(new { cmd = "history" });
            c.Check(((c.J["undo"] as JArray)?.Count ?? -1) == n[0], "the outfit save records no history entry");
            path[0] = full;
        }

        static (int w, int h, int type) U9Png(byte[] png)
        {
            if (png == null || png.Length < 26 || png[0] != 0x89 || png[1] != (byte)'P' || png[12] != (byte)'I' || png[13] != (byte)'H') return (-1, -1, -1);
            int Be(int o) => (png[o] << 24) | (png[o + 1] << 16) | (png[o + 2] << 8) | png[o + 3];
            return (Be(16), Be(20), png[25]);
        }

        /// <summary>A character file written by the UI: the manifest (app.version, prerelease, named requires), the thumbnail
        /// (504 × 704, larger than the placeholder) and the documents' canonical bytes = the node's.</summary>
        static void U9CheckCharaFile(AcceptanceCtx c, U9State s, string id, string path)
        {
            var lr = CharaContainer.Load(path);
            if (!c.Check(lr.Ok, $"{Path.GetFileName(path)} reads", lr.Error?.Message)) return;
            var cc = lr.Value;
            var m = cc.LoadedManifest;
            c.Check(m != null && m.AppVersion == AppSaveOptions.Version && m.Prerelease == null, $"manifest app.version «{m?.AppVersion}» = Application.version «{Application.version}», prerelease {(m?.Prerelease ?? "null")}");
            var (w, h, _) = U9Png(cc.Thumbnail);
            int ph = ThumbnailPlaceholder.Png().Length;
            c.Check(w == 504 && h == 704 && cc.Thumbnail.Length > ph, $"the thumbnail is {w} × {h} by its PNG header, {cc.Thumbnail?.Length ?? 0} bytes (the placeholder {ph})");
            var importer = ImportSetup.ImporterOf(AppCore.Current.Services) as Hs2Importer;
            var unnamed = new List<string>();
            int known = 0;
            foreach (var r in m?.Requires ?? new List<RequiresEntry>())
            {
                bool installed = false;
                try { installed = importer?.RequiresInfo(r.Guid) != null; } catch (Exception) { installed = false; }
                if (!installed) continue;
                known++;
                if (string.IsNullOrEmpty(r.Name)) unnamed.Add(r.Guid);
            }
            c.Check(unnamed.Count == 0, $"every requires item of an installed GUID is named ({known} installed of {m?.Requires.Count ?? 0} items)", string.Join(", ", unnamed));
            var st = s.Ui.Model.Find(id)?.Appearance?.Stored;
            c.Check(st != null && U9Bytes(st.Chara).SequenceEqual(U9Bytes(cc.Chara)) && U9Bytes(st.Coord).SequenceEqual(U9Bytes(cc.Coord)), "chara.json / coord.json canonical bytes = the node's");
            c.Info($"UI9: {Path.GetFileName(path)} {new FileInfo(path).Length} bytes, thumbnail {cc.Thumbnail?.Length} bytes, requires {m?.Requires.Count}");
        }

        /// <summary>An outfit file written by the UI: the thumbnail (504 × 704, not the placeholder), coord.json = the node's with
        /// the typed name, import null.</summary>
        static void U9CheckCoordFile(AcceptanceCtx c, U9State s, string id, string path, string typed)
        {
            var lr = CoordContainer.Load(path);
            if (!c.Check(lr.Ok, $"{Path.GetFileName(path)} reads", lr.Error?.Message)) return;
            var cc = lr.Value;
            var (w, h, _) = U9Png(cc.Thumbnail);
            c.Check(w == 504 && h == 704 && !cc.Thumbnail.SequenceEqual(ThumbnailPlaceholder.Png()), $"the outfit thumbnail is {w} × {h} by its PNG header and not the placeholder ({cc.Thumbnail?.Length ?? 0} bytes)");
            var node = U9Json(U9Bytes(s.Ui.Model.Find(id)?.Appearance?.Stored?.Coord));
            if (node != null) node["name"] = typed;
            var fileJ = U9Json(U9Bytes(cc.Coord));
            c.Check(node != null && JToken.DeepEquals(node, fileJ), $"coord.json = the node's with the name «{typed}»",
                node == null ? "no stored coordinate" : U4JsonDiff(node.ToString(Newtonsoft.Json.Formatting.None), fileJ?.ToString(Newtonsoft.Json.Formatting.None)));
            c.Check(cc.Import == null, "the outfit file's import provenance is null");
            c.Check(cc.LoadedManifest?.AppVersion == AppSaveOptions.Version && cc.LoadedManifest?.Prerelease == null, $"outfit manifest app.version «{cc.LoadedManifest?.AppVersion}», prerelease {(cc.LoadedManifest?.Prerelease ?? "null")}");
        }

        /// <summary>Step 6's edits on the female (chin, back hair base colour, top colour 1, height), or the chin alone.</summary>
        static IEnumerator U9Edits(AcceptanceCtx c, U9State s, string id, bool all)
        {
            var ui = s.Ui;
            var tab = new CharaEditTab[1];
            yield return U4OpenPage(c, s.S4, id, "shape", tab);
            if (tab[0] != null)
            {
                const string chin = "appearance.chara.face.shape[5]";
                var row = U4Slider(tab[0], chin, -1);
                if (c.Check(row != null, "the chin width row (field-…face.shape[5])"))
                {
                    float v0 = Ui3Value(ui, id, chin);
                    int u = U4Undo(ui);
                    yield return U4DragKnob(c, s.S4, row, v0, "the chin width slider");
                    U4Entry(c, ui, u, OpLabels.EditFace, "the chin width drag");
                    float v1 = Ui3Value(ui, id, chin);
                    c.Check(v1 != v0, $"face.shape[5] moved ({v0} → {v1})");
                }
            }
            if (!all) yield break;
            var s6 = new U6State { S4 = s.S4 };
            yield return U6OpenHair(c, s6, id, 0, tab);
            if (tab[0] != null) yield return U6ColourDrag(c, s6, tab[0], id, "appearance.chara.hair.back.baseColor", "the back hair's base colour");
            var s7 = new U7State { S4 = s.S4 };
            var ok = new bool[1];
            yield return U7OpenClothes(c, s7, id, 0, ok);
            if (ok[0]) yield return U7ColourDrag(c, s7, id, "top.colors[0].base", "the top's colour 1", false);
            var s5 = new U5State { S4 = s.S4 };
            yield return U5OpenPage(c, s5, id, "shape", tab);
            if (tab[0] != null)
            {
                const string height = "appearance.chara.body.shape[0]";
                var row = U4Slider(tab[0], height, -1);
                if (c.Check(row != null, "the height row (field-…body.shape[0])"))
                {
                    float v0 = Ui3Value(ui, id, height);
                    int u = U4Undo(ui);
                    yield return U4DragKnob(c, s.S4, row, v0, "the height slider");
                    U4Entry(c, ui, u, OpLabels.EditBody, "the height drag");
                    float v1 = Ui3Value(ui, id, height);
                    c.Check(v1 != v0, $"body.shape[0] moved ({v0} → {v1})");
                }
            }
        }

        /// <summary>Ticks exactly <paramref name="flags"/> in the open load dialog (a click on each checkbox that differs).</summary>
        static IEnumerator U9SetFlags(AcceptanceCtx c, U9State s, IReadOnlyCollection<string> flags)
        {
            var mf = new JObject[1];
            yield return U9FileState(c, mf);
            var cur = (U9O(U9O(mf[0], "loadDialog"), "flags") as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
            foreach (var f in U9Flags)
            {
                if (flags.Contains(f) == cur.Contains(f)) continue;
                var tg = U9El(s, "chara-load-flag-" + f) as Toggle;
                yield return U4Click(c, s.S4, tg, "the part «" + f + "» (chara-load-flag-" + f + ")");
                yield return c.Frames(2);
            }
            yield return U9FileState(c, mf);
            var now = (U9O(U9O(mf[0], "loadDialog"), "flags") as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
            c.Check(now.Count == flags.Count && flags.All(now.Contains), $"the ticked parts are {string.Join(", ", flags)} (ui.state makerFile.loadDialog.flags {string.Join(",", now)})");
        }

        /// <summary>
        /// «キャラを読込…» from the File page: the dialog of the primary's sex, the file searched by <paramref name="stem"/> and
        /// clicked, exactly <paramref name="flags"/> ticked, «読込» → one entry "Load character" and the dialog closed.
        /// </summary>
        static IEnumerator U9Load(AcceptanceCtx c, U9State s, string id, string stem, string fullPath, IReadOnlyCollection<string> flags, string what, bool[] ok)
        {
            ok[0] = false;
            yield return U9Open(c, s, id, "file", U9FileSection, ok);
            if (!ok[0]) yield break;
            ok[0] = false;
            yield return U9Click(c, s, "file-load-chara", "«キャラを読込…» (file-load-chara)");
            var modal = new string[1];
            yield return U9WaitModal(c, CharaLoadDialog.DialogId, 120, modal);
            if (!c.Check(modal[0] == CharaLoadDialog.DialogId, $"the load dialog is open (modal {modal[0]})")) yield break;
            var mf = new JObject[1];
            for (int i = 0; i < 600; i += 2)
            {
                yield return U9FileState(c, mf);
                if ((bool?)U9O(U9O(mf[0], "loadDialog"), "ready") == true) break;
                yield return c.Frames(2);
            }
            var ld = U9O(mf[0], "loadDialog");
            c.Check((bool?)ld?["ready"] == true, $"the dialog lists the files of the {(string)ld?["sex"]} sex ({(int?)ld?["files"]} files)");
            var br = U9El(s, "chara-load-browser");
            if (!c.Check(br != null, "the load dialog's browser (chara-load-browser)")) yield break;
            yield return U7TypeText(c, new U7State { S4 = s.S4 }, br.Q<TextField>("chara-load-search"), stem, "the load dialog's search field");
            VisualElement cell = null;
            for (int i = 0; i < 600 && cell == null; i++) { yield return c.Frames(1); cell = U9El(s, "chara-file-" + stem, br); }
            if (!c.Check(cell != null, $"the file's cell chara-file-{stem} is shown")) { yield return U9Click(c, s, "dialog-button-cancel", "the dialog's cancel"); yield break; }
            yield return U4Click(c, s.S4, cell, "the file " + stem);
            yield return c.Frames(2);
            yield return U9FileState(c, mf);
            string sel = (string)U9O(U9O(mf[0], "loadDialog"), "selectedPath");
            c.Check(sel != null && string.Equals(Path.GetFullPath(sel), Path.GetFullPath(fullPath), StringComparison.OrdinalIgnoreCase), $"the chosen file is {Path.GetFileName(fullPath)} ({(string)U9O(U9O(mf[0], "loadDialog"), "selectedKind")})");
            yield return U9SetFlags(c, s, flags);
            yield return U9FileState(c, mf);
            c.Check((bool?)U9O(U9O(mf[0], "loadDialog"), "loadEnabled") == true, "«読込» is enabled (a file and a part)");
            var n = new int[1];
            yield return U7Undo(c, n);
            yield return U9Click(c, s, "dialog-button-load", "«読込» (dialog-button-load)");
            yield return U9WaitModal(c, null, 120, modal);
            if (!c.Check(modal[0] == null, $"«読込» closes the dialog (modal {modal[0] ?? "none"}; note «{(string)U9O(U9O(mf[0], "loadDialog"), "note")}»)"))
            {
                yield return U9Click(c, s, "dialog-button-cancel", "the dialog's cancel");
                yield break;
            }
            yield return U7Entry(c, n[0], OpLabels.LoadCharacter, what);
            yield return U5WaitBuilt(c, s.Ui, id);
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "chara.edit.info", key = id });
            c.Info($"UI9 timing ({U6Here()}): {what}'s flush {(double?)c.J["lastFlush"]?["ms"]:0.000} ms, mode {(string)c.J["lastFlush"]?["mode"]}");
            ok[0] = true;
        }

        /// <summary>A part load from a character file and the documents after it: the loaded parts = the file's, the others =
        /// <paramref name="before"/>'s (face, body, hair, profile of chara.json; the coordinate = coord.json).</summary>
        static IEnumerator U9LoadAndCheck(AcceptanceCtx c, U9State s, string id, string stem, string path, string[] flags, JObject before, JObject file, bool[] ok)
        {
            string what = "«" + string.Join(" · ", flags) + "» from " + stem;
            yield return U9Load(c, s, id, stem, path, flags, what, ok);
            if (!ok[0]) yield break;
            var v = new JObject[1];
            yield return U9Docs(c, id, v);
            var bad = new List<string>();
            foreach (var (flag, doc, member) in new[] { ("face", "chara", "face"), ("body", "chara", "body"), ("hair", "chara", "hair"), ("profile", "chara", "profile"), ("coordinate", "coord", null) })
            {
                var got = member == null ? v[0]?[doc] : v[0]?[doc]?[member];
                var want = flags.Contains(flag) ? (member == null ? file?[doc] : file?[doc]?[member]) : (member == null ? before?[doc] : before?[doc]?[member]);
                if (!JToken.DeepEquals(got, want))
                    bad.Add((flags.Contains(flag) ? "taken " : "kept ") + flag + ": " + U4JsonDiff(want?.ToString(Newtonsoft.Json.Formatting.None), got?.ToString(Newtonsoft.Json.Formatting.None)));
            }
            c.Check(bad.Count == 0, $"{what}: the loaded parts = the file's, the others kept", string.Join("; ", bad));
        }
    }
}
