// UiPanelsTest.play group MI (Phase 4.6 package W): the Character and File pages of the Edit tab, in the app with the UI in
// Editor play mode (same harness and Ctx as UiPanelsTest, helpers of the MF / MH / MA groups).
//   every tree node of the female and the male shows sections and no "later" note;
//   Character page: the section, «名前» = one entry "Rename character" (a second typed name merges or adds one), the
//     read-only lines without an input element (sex, futanari female only, the game parameters of an imported card, the
//     provenance naming the card file, the SHA-256 cut to 12 digits), first show (target 30 ms);
//   File page: the two sections and their elements, first show without the preview (target 30 ms); the preview renders
//     504 × 704 after the page is shown, shows 126 × 176, follows the framing buttons («正面» front, «現在の視点» view),
//     «撮影し直す» renders again (target 60 ms), an edit renders nothing, hiding the page destroys the texture; ui.state
//     makerFile reports it;
//   save flows: the character prompt's initial text (profile.name, file-safe; the node name when the name is empty), the
//     file <UserData>/chara/<name>.hs2nchara (app.version = Application.version, prerelease null, 504 × 704 thumbnail by its PNG
//     header, documents = the node's, no entry), the overwrite question (cancel keeps the file, OK rewrites it); the coordinate
//     prompt (initial coord.name), <UserData>/coordinate/female/<name>.hs2ncoord (coord.name = the typed name, the node's
//     unchanged, import null, 504 × 704), the Clothes page's «保存…» opens the same prompt;
//   the load dialog: the female files only (the saved file, no male file), the five flags (default on, remembered in
//     maker.loadFlags across dialogs), «読込» disabled without a file or a flag (pressing it keeps the dialog open with the
//     note), «顔» only = one entry "Load character" restoring the face while the hair and body edits stay, «キャラ設定» only
//     restores the name, «髪» only from the HS2 card = the card's hair; «コーデを読込…» lists the saved coordinate and loads it
//     (coord.name included);
//   multi-selection disables the page with its note; the male: no futanari line, its own folders, «顔» only from its file.
// Files written: <UserData> of the run (Spike/out/ui_panels/userdata) — chara/MI Chara, MI Male; coordinate/female/MI Coord,
// coordinate/male/MI Coord M (removed at the start and the end). Thumbnails are checked by their PNG header and size only.
// Run alone: & Tools\unity_batch.ps1 -Method UiPanelsTest.Run -NoQuit -MethodArgs @('-only','MI')
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiPanelsTest
{
    // The group joins UiPanelsTest.Groups after every domain reload (UiPanelsTest's static constructor runs first).
    [UnityEditor.InitializeOnLoadMethod]
    static void RegisterMakerFileGroup()
    {
        if (!Groups.Any(g => g.id == "MI")) Groups.Add(("MI", MakerFileGroup));
    }

    const double MiPageTargetMs = 30, MiPreviewTargetMs = 60;
    const string MiChara = "MI Chara", MiMale = "MI Male", MiCoord = "MI Coord", MiCoordM = "MI Coord M";

    static CharaSubtree MiStored(Ctx c, string id) => c.App.Session.Model.Find(id)?.Appearance?.Stored;

    static JObject MiDoc(CharaDocument d) => d == null ? null : JObject.Parse(Encoding.UTF8.GetString(CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, d.Write)));
    static JObject MiDoc(CoordDocument d) => d == null ? null : JObject.Parse(Encoding.UTF8.GetString(CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, d.Write)));
    static bool MiSame(JToken a, JToken b) => a != null && b != null && JToken.DeepEquals(a, b);

    /// <summary>No element a user could type into or click inside <paramref name="e"/>.</summary>
    static bool MiNoInput(VisualElement e) =>
        e != null && e.Query<TextField>().ToList().Count == 0 && e.Query<Toggle>().ToList().Count == 0 && e.Query<Button>().ToList().Count == 0
        && e.Query<Slider>().ToList().Count == 0 && e.Query<SliderInt>().ToList().Count == 0 && e.Query<DropdownField>().ToList().Count == 0
        && e.Query<FloatField>().ToList().Count == 0 && e.Query<IntegerField>().ToList().Count == 0;

    static string MiValue(CharaEditTab tab, string name) => MfQ<VisualElement>(tab, name)?.Q<Label>("value")?.text;

    /// <summary>The (width, height) of the PNG at the start of a container file (its IHDR), or (0, 0).</summary>
    static (int w, int h) MiPngSize(string path)
    {
        try
        {
            var b = new byte[24];
            using (var fs = File.OpenRead(path)) if (fs.Read(b, 0, 24) != 24) return (0, 0);
            if (b[0] != 0x89 || b[1] != (byte)'P' || b[12] != (byte)'I' || b[13] != (byte)'H') return (0, 0);
            int W(int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
            return (W(16), W(20));
        }
        catch (Exception) { return (0, 0); }
    }

    static void MiDelete(params string[] files)
    {
        foreach (var f in files) try { if (f != null && File.Exists(f)) File.Delete(f); } catch (Exception) { }
    }

    /// <summary>The text field of the open prompt (null when the current dialog is not <paramref name="id"/>).</summary>
    static TextField MiPrompt(Ctx c, string id) => c.Ui.Dialogs.Current?.Spec.Id == id ? c.Ui.Dialogs.Current.Spec.Content as TextField : null;

    /// <summary>Members the group edits (a face slider, the back hair's base colour, a body slider).</summary>
    const string MiFacePath = "appearance.chara.face.shape[0]", MiHairPath = "appearance.chara.hair.back.baseColor", MiBodyPath = "appearance.chara.body.shape[1]";

    /// <summary>Changes one member (a float by 0.1 inside 0..1, a colour to another) on <paramref name="id"/>: one entry.</summary>
    static OpResult MiEdit(Ctx c, string id, string path)
    {
        if (!FieldAccessor.TryGet(c.App.Session.Model, id, path, out var v, out _)) return OpResult.NothingToDo();
        object nv;
        if (v is Color4 col) nv = new Color4(col.R * 0.5f + 0.13f, col.G * 0.25f + 0.31f, 1 - col.B * 0.5f, col.A);
        else { double d = FieldValues.ToDouble(v); nv = (float)(d < 0.6 ? d + 0.17 : d - 0.21); }
        return c.Ui.ShowResult(c.Ui.Ops.EditAppearance(new[] { id }, new List<(string, object)> { (path, nv) }, UiContext.User()));
    }

    static IEnumerator MiNoLater(Ctx c, CharaEditTab tab, string who)
    {
        string later = L10n.Get("inspector.charaEdit.later");
        var bad = new List<string>();
        var counts = new List<string>();
        foreach (var node in InspectorSchemas.CharaEditTree)
        {
            tab.ShowNode(node);
            yield return PlayModeHarness.WaitFrames(2);
            var pg = tab.Pages.TryGetValue(node, out var p) ? p : null;
            int secs = pg?.Query<Foldout>().ToList().Count(x => x.name != null && x.name.StartsWith("edit-section-", StringComparison.Ordinal)) ?? 0;
            bool hasLater = pg != null && pg.Query<Label>().ToList().Any(l => l.text == later);
            if (hasLater || secs == 0) bad.Add(node + (hasLater ? " shows «later»" : " has no section"));
            counts.Add(node + " " + secs);
        }
        c.Check(bad.Count == 0, who + ": every tree node shows sections and no \"later\" note (" + string.Join(", ", counts) + ")" + (bad.Count > 0 ? "; " + string.Join(", ", bad) : ""));
    }

    static void MiTiming(Ctx c, string what, double ms, double target)
    {
        c.Check(ms >= 0 && ms <= 2 * target, $"{what} {ms:0.0} ms (target {target}; ≤ {2 * target})");
        if (ms > target) c.Warning($"{what} {ms:0.0} ms above the {target} ms target");
    }

    static IEnumerator MiWaitPreview(Ctx c, CharaEditTab tab, int rendersBefore, double sec = 30)
    {
        var ok = new bool[1];
        yield return WaitFor(() => tab.FilePreview != null && tab.FilePreview.Renders > rendersBefore && !tab.FilePreview.Pending, sec, ok);
        yield return PlayModeHarness.WaitFrames(2);
    }

    /// <summary>Answers the open prompt <paramref name="id"/> with <paramref name="text"/> (null = keep the initial text).</summary>
    static string MiAnswer(Ctx c, string id, string text)
    {
        var tf = MiPrompt(c, id);
        if (tf == null) return null;
        string initial = tf.value;
        if (text != null) tf.value = text;
        c.Ui.Dialogs.Press("ok");
        return initial;
    }

    static IEnumerator MakerFileGroup(Ctx c)
    {
        var ui = c.Ui;
        yield return NewScene(c);
        var ids = new List<string>();
        yield return AddBuiltChara(c, c.FemaleCard, ids);
        yield return AddBuiltChara(c, c.MaleCard, ids);
        if (ids.Count < 2) yield break;
        string f = ids[0], m = ids[1];
        int u00 = UndoCount(c);
        var paths = c.App.Paths;
        string charaF = Path.Combine(paths.CharaDir, MiChara + StudioOps.CharaFileExtension), charaM = Path.Combine(paths.CharaDir, MiMale + StudioOps.CharaFileExtension);
        string coordF = Path.Combine(CoordinateBrowser.OwnDir(ui, 1), MiCoord + SaveFormats.CoordExtension), coordM = Path.Combine(CoordinateBrowser.OwnDir(ui, 0), MiCoordM + SaveFormats.CoordExtension);
        MiDelete(charaF, charaM, coordF, coordM);
        ui.Options.Set(OptionDefs.MakerThumbnailFraming, "view");
        ui.Options.Set(OptionDefs.MakerLoadFlags, OptionDefs.MakerLoadFlagsAll);
        var fCard = MiDoc(MiStored(c, f)?.Chara);
        c.Check(fCard != null && MiStored(c, f)?.Import != null, "BR-Chan is imported (documents and provenance)");

        // ---------------------------------------------------------------- every node, female
        var tab = MfOpenEdit(c, f);
        yield return PlayModeHarness.WaitFrames(4);
        c.Check(tab != null && tab.panel != null, "the female's Edit tab is shown");
        if (tab == null) yield break;
        yield return MiNoLater(c, tab, "BR-Chan");

        // ---------------------------------------------------------------- Character page (female)
        tab = MfOpenEdit(c, f);
        tab.ShowNode("character");
        yield return PlayModeHarness.WaitFrames(3);
        MiTiming(c, "Character page first show", tab.PageBuildMs.TryGetValue("character.character", out var cms) ? cms : -1, MiPageTargetMs);
        var cpage = tab.Pages.TryGetValue("character", out var cp) ? cp : null;
        c.Check(cpage != null && MfQ<Foldout>(cpage, "edit-section-" + InspectorSchemas.CharacterProfileSection) != null && MfQ<VisualElement>(tab, "edit-pages-character") == null,
            "the Character node is one page with the section «キャラ設定» (" + InspectorSchemas.CharacterProfileSection + ")");
        string pName = InspectorSchemas.CharaEditPath("profile.name");
        var nameRow = MfQ<VisualElement>(tab, "field-" + pName);
        c.Check(nameRow != null && tab.CharacterNameField != null && tab.CharacterNameField.value == (MiStored(c, f)?.Chara?.Profile?.Name ?? ""),
            "«名前» shows profile.name («" + tab.CharacterNameField?.value + "»)");
        var stF = MiStored(c, f);
        var sexLine = MfQ<VisualElement>(tab, "field-" + InspectorSchemas.CharaEditPath("profile.sex"));
        var futLine = MfQ<VisualElement>(tab, "field-" + InspectorSchemas.CharaEditPath("profile.futanari"));
        c.Check(sexLine != null && MiNoInput(sexLine) && MiValue(tab, sexLine.name) == L10n.Get("inspector.charaEdit.character.sex.female"),
            "the sex line reads «" + MiValue(tab, sexLine?.name) + "», no input element");
        c.Check(futLine != null && MhShown(futLine) && MiNoInput(futLine) && futLine.Q<Label>("hint") != null
            && MiValue(tab, futLine.name) == L10n.Get(stF.Chara.Profile.Futanari ? "inspector.charaEdit.character.futanariOn" : "inspector.charaEdit.character.futanariOff"),
            "the futanari line reads «" + MiValue(tab, futLine?.name) + "» with the Body › Special hint, no input element");
        var roLines = InspectorSchemas.CharaEdit.Where(s => s.Id == InspectorSchemas.CharacterProfileSection).SelectMany(s => s.Fields).Where(x => x.ReadOnly).ToList();
        var shownRo = roLines.Select(x => MfQ<VisualElement>(tab, "field-" + x.Path)).Where(MhShown).ToList();
        c.Check(shownRo.Count == roLines.Count && shownRo.All(MiNoInput), $"every read-only line is shown without an input element ({shownRo.Count} / {roLines.Count})");
        var hs2 = stF.Chara.Profile.Hs2;
        c.Check(hs2 != null && !MhShown(MfQ<VisualElement>(tab, "character-hs2-none")) && MiValue(tab, "field-" + InspectorSchemas.CharaEditPath("profile.hs2.personality")) == hs2.Personality.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "the game parameter lines of the imported card (personality «" + MiValue(tab, "field-" + InspectorSchemas.CharaEditPath("profile.hs2.personality")) + "», birthday «"
            + MiValue(tab, "field-" + InspectorSchemas.CharaEditPath("profile.hs2.birthMonth")) + "», voice «" + MiValue(tab, "field-" + InspectorSchemas.CharaEditPath("profile.hs2.voiceRate")) + "»)");
        string provFile = MiValue(tab, "field-" + InspectorSchemas.ProvenancePrefix + "fileName");
        c.Check(stF.Import != null && provFile == stF.Import.FileName && string.Equals(provFile, Path.GetFileName(c.FemaleCard), StringComparison.OrdinalIgnoreCase)
            && !MhShown(MfQ<VisualElement>(tab, "character-provenance-none")),
            "the provenance names the card file «" + provFile + "»");
        string sha = MiValue(tab, "field-" + InspectorSchemas.ProvenancePrefix + "sha256");
        c.Check(sha != null && sha.Length == 12 && stF.Import.Sha256.StartsWith(sha, StringComparison.Ordinal), "the SHA-256 line shows the first 12 hex digits («" + sha + "»)");
        c.Info("provenance lines: imported «" + MiValue(tab, "field-provenance.importedUtc") + "», importer «" + MiValue(tab, "field-provenance.importer") + "», GUIDs «" + MiValue(tab, "field-provenance.sideloaderGuids") + "»");
        int u1 = UndoCount(c);
        string name0 = stF.Chara.Profile.Name;
        var wr = tab.WriteCharacterName(MiChara);
        yield return PlayModeHarness.WaitFrames(2);
        c.Check(wr.Ok && MiStored(c, f).Chara.Profile.Name == MiChara && UndoCount(c) == u1 + 1 && c.UndoLabel == "Rename character" && tab.CharacterNameField.value == MiChara,
            "«名前» «" + MiChara + "» → profile.name, one entry \"" + c.UndoLabel + "\"");
        tab.CharacterNameField.value = MiChara + " 2";   // through the field (a delayed text field commits the value)
        yield return PlayModeHarness.WaitFrames(2);
        int typedEntries = UndoCount(c) - u1;
        c.Check(MiStored(c, f).Chara.Profile.Name == MiChara + " 2" && c.UndoLabel == "Rename character" && typedEntries <= 2,
            "a second typed name through the field → profile.name, " + (typedEntries == 1 ? "merged into the same entry" : "one more entry"));
        tab.WriteCharacterName(MiChara);
        yield return PlayModeHarness.WaitFrames(2);
        int uName = UndoCount(c);
        c.Info("name edits: " + typedEntries + " entries after two typed names (Maker edits merge by key: " + (typedEntries == 1) + "); original name «" + name0 + "»");

        // ---------------------------------------------------------------- File page (female): sections, preview, framing
        tab.ShowNode("file");
        var pv = tab.FilePreview;
        c.Check(pv != null && pv.Pending && pv.Renders == 0, "the preview is not rendered in the frame that shows the page (pending)");
        yield return PlayModeHarness.WaitFrames(1);
        MiTiming(c, "File page first show (without the preview)", tab.PageBuildMs.TryGetValue("file.file", out var fms) ? fms : -1, MiPageTargetMs);
        var fpage = tab.Pages.TryGetValue("file", out var fp) ? fp : null;
        var fsecs = fpage?.Query<Foldout>().ToList().Select(x => x.name).Where(n => n.StartsWith("edit-section-", StringComparison.Ordinal)).ToList() ?? new List<string>();
        c.Check(fsecs.SequenceEqual(new[] { "edit-section-" + InspectorSchemas.FileCharacterSection, "edit-section-" + InspectorSchemas.FileCoordinateSection }),
            "the File node is one page with «キャラクター» and «コーデ» (" + string.Join(", ", fsecs) + ")");
        var names = new[] { "file-save-chara", "field-maker.thumbnailFraming", "field-maker.thumbnailFraming-0", "field-maker.thumbnailFraming-1", "file-thumb-preview", "file-thumb-image", "file-retake", "file-load-chara", "file-save-coord", "file-load-coord" };
        var missing = names.Where(n => MfQ<VisualElement>(tab, n) == null).ToList();
        c.Check(missing.Count == 0, "the File page's elements: " + string.Join(", ", names) + (missing.Count > 0 ? "; missing " + string.Join(", ", missing) : ""));
        yield return MiWaitPreview(c, tab, 0);
        c.Check(pv != null && pv.Texture != null && pv.Texture.width == 504 && pv.Texture.height == 704 && pv.Framing == CharaThumbnailFraming.View,
            $"the preview renders after the page is shown: {pv?.Texture?.width} × {pv?.Texture?.height}, framing {pv?.Framing} ({pv?.LastMs:0.0} ms; reason «{pv?.Reason}»)");
        c.Check(pv != null && Mathf.Abs(pv.ImageElement.resolvedStyle.width - ThumbnailPreview.Width) < 0.5f && Mathf.Abs(pv.ImageElement.resolvedStyle.height - ThumbnailPreview.Height) < 0.5f,
            $"the preview is shown at 126 × 176 ({pv?.ImageElement.resolvedStyle.width:0.#} × {pv?.ImageElement.resolvedStyle.height:0.#})");
        c.Check(tab.FileFraming != null && tab.FileFraming.Index == 1, "«現在の視点» is the framing shown (the option's default view)");
        if (pv != null)
        {
            var st = CharaEditTab.FileUiState(ui);
            var ps = st["preview"] as Dictionary<string, object>;
            c.Check(ps != null && (int)ps["width"] == 504 && (int)ps["height"] == 704 && (string)ps["framing"] == "view" && (int)ps["renderedAtFrame"] == pv.RenderedAtFrame && (string)st["thumbnailFraming"] == "view",
                "ui.state makerFile.preview = {504, 704, view, frame " + pv.RenderedAtFrame + "}");
            var tex0 = pv.Texture;
            int r0 = pv.Renders, frame0 = pv.RenderedAtFrame;
            MaPress(MfQ<Button>(tab, "field-maker.thumbnailFraming-0"));
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(ui.Options.String(OptionDefs.MakerThumbnailFraming) == "front" && pv.Renders == r0 + 1 && pv.Framing == CharaThumbnailFraming.Front && pv.RenderedAtFrame > frame0
                && pv.Texture != null && pv.Texture.width == 504 && tex0 == null && tab.FileFraming.Index == 0,
                "«正面» → option front, the preview renders the front framing, the old texture destroyed (" + pv.LastMs.ToString("0.0") + " ms)");
            var tex1 = pv.Texture;
            MaPress(MfQ<Button>(tab, "field-maker.thumbnailFraming-1"));
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(ui.Options.String(OptionDefs.MakerThumbnailFraming) == "view" && pv.Renders == r0 + 2 && pv.Framing == CharaThumbnailFraming.View && tex1 == null && tab.FileFraming.Index == 1,
                "«現在の視点» → option view, the preview renders the view framing (reason «" + pv.Reason + "»)");
            var times = new List<double>();
            for (int i = 0; i < 3; i++)
            {
                int ri = pv.Renders;
                MaPress(pv.Retake);
                yield return PlayModeHarness.WaitFrames(2);
                if (pv.Renders == ri + 1) times.Add(pv.LastMs);
            }
            c.Check(times.Count == 3 && pv.Texture != null && pv.Texture.width == 504 && pv.Texture.height == 704, "«撮影し直す» renders again each time (" + string.Join(", ", times.Select(t => t.ToString("0.0"))) + " ms)");
            MiTiming(c, "preview render («撮影し直す», view, median)", times.Count > 0 ? Median(times) : -1, MiPreviewTargetMs);
            int r1 = pv.Renders;
            int ue = UndoCount(c);
            MiEdit(c, f, MiFacePath);
            yield return PlayModeHarness.WaitFrames(6);
            c.Check(UndoCount(c) == ue + 1 && pv.Renders == r1, "an edit (" + MiFacePath + ") renders no preview");
            yield return MfUndoTo(c, ue);
            var tex2 = pv.Texture;
            tab.ShowNode("face");
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(pv.Texture == null && tex2 == null && pv.panel == null && ThumbnailPreview.Current == null, "hiding the File node destroys the preview's texture");
            tab.ShowNode("file");
            yield return MiWaitPreview(c, tab, pv.Renders);
            c.Check(pv.Texture != null && ThumbnailPreview.Current == pv, "showing the File node again renders the preview again");
        }

        // ---------------------------------------------------------------- character save (female)
        int us = UndoCount(c);
        MaPress(MfQ<Button>(tab, "file-save-chara"));
        yield return PlayModeHarness.WaitFrames(2);
        var tfc = MiPrompt(c, InspectorFlows.SaveCharaPromptId);
        c.Check(tfc != null && tfc.value == MiChara, "«名前を付けて保存…» opens the prompt " + InspectorFlows.SaveCharaPromptId + " with the character's name «" + tfc?.value + "»");
        MiAnswer(c, InspectorFlows.SaveCharaPromptId, null);
        yield return PlayModeHarness.WaitFrames(2);
        var sr = InspectorFlows.LastCharaSave?.Value as CharaSaveResult;
        c.Check(File.Exists(charaF) && InspectorFlows.LastCharaSave != null && InspectorFlows.LastCharaSave.Ok && UndoCount(c) == us && !ui.Dialogs.IsModalOpen,
            "OK writes " + Path.GetFileName(charaF) + " (" + (File.Exists(charaF) ? new FileInfo(charaF).Length : 0) + " bytes, thumbnail " + sr?.Thumbnail + " " + sr?.Framing + "), no entry");
        if (File.Exists(charaF))
        {
            var lf = CharaContainer.Load(charaF);
            var man = lf.Value?.LoadedManifest;
            var png = MiPngSize(charaF);
            c.Check(lf.Ok && man != null && man.AppVersion == Application.version && man.Prerelease == null && png.w == 504 && png.h == 704 && sr?.Thumbnail == "render",
                $"the file: app.version {man?.AppVersion} (= {Application.version}), prerelease {(man?.Prerelease ?? "null")}, thumbnail {png.w} × {png.h}");
            c.Check(lf.Ok && MiSame(MiDoc(lf.Value.Chara), MiDoc(MiStored(c, f).Chara)) && MiSame(MiDoc(lf.Value.Coord), MiDoc(MiStored(c, f).Coord)),
                "its chara.json and coord.json = the node's documents");
            var unnamed = man?.Requires?.Where(r => string.IsNullOrEmpty(r.Name)).Select(r => r.Guid).ToList() ?? new List<string>();
            c.Info($"requires: {man?.Requires?.Count} items, {unnamed.Count} without a name" + (unnamed.Count > 0 ? " (" + string.Join(", ", unnamed.Take(5)) + ")" : ""));
            var t0 = File.GetLastWriteTimeUtc(charaF);
            var save0 = InspectorFlows.LastCharaSave;
            tab.SaveCharacterFile();
            yield return PlayModeHarness.WaitFrames(2);
            MiAnswer(c, InspectorFlows.SaveCharaPromptId, null);
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(ui.Dialogs.Current?.Spec.Id == InspectorFlows.OverwriteQuestionId, "saving to the same name asks " + InspectorFlows.OverwriteQuestionId + " («" + ui.Dialogs.Current?.Spec.Message + "»)");
            ui.Dialogs.Press("cancel");
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(!ui.Dialogs.IsModalOpen && ReferenceEquals(InspectorFlows.LastCharaSave, save0) && File.GetLastWriteTimeUtc(charaF) == t0, "Cancel keeps the file");
            System.Threading.Thread.Sleep(20);
            tab.SaveCharacterFile();
            yield return PlayModeHarness.WaitFrames(2);
            MiAnswer(c, InspectorFlows.SaveCharaPromptId, null);
            yield return PlayModeHarness.WaitFrames(2);
            if (ui.Dialogs.Current?.Spec.Id == InspectorFlows.OverwriteQuestionId) ui.Dialogs.Press("ok");
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(!ReferenceEquals(InspectorFlows.LastCharaSave, save0) && InspectorFlows.LastCharaSave.Ok && UndoCount(c) == us, "OK rewrites the file (no entry)");
        }
        // the initial names: unsafe characters, an empty name
        {
            tab.WriteCharacterName("MI/Ch:ara");
            yield return PlayModeHarness.WaitFrames(2);
            string i1 = InspectorFlows.CharaInitialName(ui, f);
            tab.WriteCharacterName("");
            yield return PlayModeHarness.WaitFrames(2);
            string i2 = InspectorFlows.CharaInitialName(ui, f);
            string label = ui.Model.Find(f)?.Label;
            c.Check(i1 == "MI_Ch_ara" && (i2 == InspectorFlows.Safe(label?.Trim(), null) || (string.IsNullOrWhiteSpace(label) && System.Text.RegularExpressions.Regex.IsMatch(i2, @"^\d{4}_\d{4}_\d{4}_\d{2}$"))),
                "initial names: «MI/Ch:ara» → «" + i1 + "», an empty name → the node's name «" + i2 + "»");
            yield return MfUndoTo(c, uName);
        }

        // ---------------------------------------------------------------- coordinate save (female)
        {
            int uc = UndoCount(c);
            string cname0 = MiStored(c, f).Coord.Name;
            MaPress(MfQ<Button>(tab, "file-save-coord"));
            yield return PlayModeHarness.WaitFrames(2);
            var tfo = MiPrompt(c, InspectorFlows.SaveCoordPromptId);
            bool initOk = tfo != null && (string.IsNullOrWhiteSpace(cname0) ? System.Text.RegularExpressions.Regex.IsMatch(tfo.value, @"^\d{4}_\d{4}_\d{4}_\d{2}$") : tfo.value == cname0.Trim());
            c.Check(initOk, "«コーデを保存…» opens the prompt " + InspectorFlows.SaveCoordPromptId + " with «" + tfo?.value + "» (coord.name «" + cname0 + "»)");
            MiAnswer(c, InspectorFlows.SaveCoordPromptId, MiCoord);
            yield return PlayModeHarness.WaitFrames(2);
            var cr = InspectorFlows.LastCoordSave?.Value as CoordSaveResult;
            c.Check(File.Exists(coordF) && InspectorFlows.LastCoordSave.Ok && UndoCount(c) == uc && MiStored(c, f).Coord.Name == cname0,
                "OK writes coordinate/female/" + Path.GetFileName(coordF) + " (" + (File.Exists(coordF) ? new FileInfo(coordF).Length : 0) + " bytes, thumbnail " + cr?.Thumbnail + "), no entry, the node's coord.name unchanged");
            if (File.Exists(coordF))
            {
                var lc = CoordContainer.Load(coordF);
                var png = MiPngSize(coordF);
                var want = MiDoc(MiStored(c, f).Coord);
                if (want != null) want["name"] = MiCoord;
                c.Check(lc.Ok && lc.Value.Coord.Name == MiCoord && lc.Value.Import == null && png.w == 504 && png.h == 704 && cr?.Thumbnail == "render" && MiSame(MiDoc(lc.Value.Coord), want),
                    $"the file: coord.name «{lc.Value?.Coord.Name}», import null, thumbnail {png.w} × {png.h}, coord.json = the node's with the typed name; app.version {lc.Value?.LoadedManifest?.AppVersion}");
            }
            tab.SaveCoordinateFile();
            yield return PlayModeHarness.WaitFrames(2);
            MiAnswer(c, InspectorFlows.SaveCoordPromptId, MiCoord);
            yield return PlayModeHarness.WaitFrames(2);
            bool asked = ui.Dialogs.Current?.Spec.Id == InspectorFlows.OverwriteQuestionId;
            ui.Dialogs.Press("cancel");
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(asked && !ui.Dialogs.IsModalOpen, "saving the outfit to the same name asks first (cancelled)");
            // the Clothes page's «保存…»
            tab.Reveal("clothes", null, InspectorSchemas.ClothesCoordinateSection);
            yield return PlayModeHarness.WaitFrames(3);
            var cs = MfQ<Button>(tab, "clothes-coordinate-save");
            c.Check(cs != null && MfQ<Button>(tab, "clothes-coordinate-load") != null, "the Clothes page's outfit section has «保存…» (clothes-coordinate-save) next to the load button");
            MaPress(cs);
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(MiPrompt(c, InspectorFlows.SaveCoordPromptId) != null, "«保存…» opens the outfit name prompt");
            ui.Dialogs.Press("cancel");
            yield return PlayModeHarness.WaitFrames(2);
            tab.ShowNode("file");
            yield return PlayModeHarness.WaitFrames(2);
        }

        // ---------------------------------------------------------------- the load dialog (female)
        {
            var dlg = tab.OpenCharaLoadDialog();
            var ok = new bool[1];
            yield return WaitFor(() => dlg?.Browser != null && dlg.Browser.Ready, 30, ok);
            yield return PlayModeHarness.WaitFrames(2);
            string hs2Female = paths.Hs2CharaDir(1);
            bool cardInFolder = hs2Female != null && Path.GetFullPath(c.FemaleCard).StartsWith(Path.GetFullPath(hs2Female), StringComparison.OrdinalIgnoreCase);
            c.Check(dlg != null && ui.Dialogs.Current?.Spec.Id == CharaLoadDialog.DialogId && dlg.Browser.Kind == FileBrowserKind.Character && dlg.Browser.Sex == 1
                && dlg.Browser.Lists(charaF) && (!cardInFolder || dlg.Browser.Lists(c.FemaleCard)),
                $"«キャラを読込…» opens {CharaLoadDialog.DialogId}: the female files ({dlg?.Browser?.Index?.Rows.Count}, «{MiChara}» and the HS2 card among them)");
            if (!cardInFolder) c.Warning("the female target card is not under the install's female folder: the dialog's HS2 card listing was not checked");
            c.Check(dlg != null && dlg.Flags == LoadFlags.All && dlg.FlagToggles.Count == 5 && dlg.FlagToggles.Values.All(t => t.value) && MfQ<Toggle>(dlg.Dialog.Element, "chara-load-flag-profile") != null,
                "the five parts «顔 · 体 · 髪 · コーデ · キャラ設定» are ticked (maker.loadFlags default)");
            c.Check(dlg != null && dlg.LoadButton != null && !dlg.LoadButton.enabledSelf && !dlg.LoadEnabled, "«読込» is disabled without a chosen file");
            dlg?.Press(CharaLoadDialog.LoadButtonId);
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(ui.Dialogs.Current?.Spec.Id == CharaLoadDialog.DialogId && dlg?.Note.text == L10n.Get("inspector.charaEdit.file.noFile"), "«読込» without a file keeps the dialog open with the note");
            bool chosen = dlg != null && dlg.Browser.Choose(charaF);
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(chosen && dlg.LoadButton.enabledSelf, "with «" + MiChara + "» chosen «読込» is enabled");
            foreach (var bit in new[] { LoadFlags.Face, LoadFlags.Body, LoadFlags.Hair, LoadFlags.Coordinate, LoadFlags.Profile })
            {
                var t = dlg?.FlagToggles[bit];
                if (t != null) t.value = false;   // as a click does
            }
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(dlg != null && dlg.Flags == LoadFlags.None && !dlg.LoadButton.enabledSelf && ui.Options.String(OptionDefs.MakerLoadFlags) == "", "with no part ticked «読込» is disabled (maker.loadFlags \"\")");
            int un = UndoCount(c);
            dlg?.Press(CharaLoadDialog.LoadButtonId);
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(ui.Dialogs.IsModalOpen && UndoCount(c) == un && dlg?.Note.text == L10n.Get("inspector.charaEdit.file.noFlag"), "«読込» without a part keeps the dialog open, nothing loaded");
            if (dlg != null) dlg.FlagToggles[LoadFlags.Face].value = true;
            yield return PlayModeHarness.WaitFrames(2);
            var stl = CharaEditTab.FileUiState(ui)["loadDialog"] as Dictionary<string, object>;
            c.Check(ui.Options.String(OptionDefs.MakerLoadFlags) == "face" && dlg.LoadButton.enabledSelf && stl != null && (bool)stl["loadEnabled"] && (string)stl["selected"] == MiChara
                && ((List<string>)stl["flags"]).SequenceEqual(new[] { "face" }) && (string)stl["sex"] == "female",
                "«顔» alone → maker.loadFlags \"face\", «読込» enabled; ui.state makerFile.loadDialog {female, [face], " + stl?["selected"] + ", enabled}");
            dlg?.Press("cancel");
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(!ui.Dialogs.IsModalOpen && CharaLoadDialog.Current == null && UndoCount(c) == un, "«キャンセル» closes the dialog, nothing loaded");
        }
        {
            // edits of the face, the hair and the body, then «顔» only from the saved file
            int ul = UndoCount(c);
            MiEdit(c, f, MiFacePath);
            MiEdit(c, f, MiHairPath);
            MiEdit(c, f, MiBodyPath);
            yield return PlayModeHarness.WaitFrames(3);
            var edited = MiDoc(MiStored(c, f).Chara);
            var file = CharaContainer.Load(charaF);
            var fileDoc = file.Ok ? MiDoc(file.Value.Chara) : null;
            c.Check(UndoCount(c) == ul + 3 && fileDoc != null && !MiSame(edited["face"], fileDoc["face"]) && !MiSame(edited["hair"], fileDoc["hair"]), "three edits (face, hair, body) differ from the saved file");
            var dlg = tab.OpenCharaLoadDialog();
            var ok = new bool[1];
            yield return WaitFor(() => dlg?.Browser != null && dlg.Browser.Ready, 30, ok);
            c.Check(dlg != null && dlg.Flags == LoadFlags.Face && dlg.FlagToggles[LoadFlags.Face].value && !dlg.FlagToggles[LoadFlags.Hair].value, "the dialog opens with the remembered parts («顔» only)");
            dlg?.Browser.Choose(charaF);
            yield return PlayModeHarness.WaitFrames(2);
            dlg?.Press(CharaLoadDialog.LoadButtonId);
            yield return PlayModeHarness.WaitFrames(4);
            var after = MiDoc(MiStored(c, f).Chara);
            c.Check(dlg?.LastResult != null && dlg.LastResult.Ok && !ui.Dialogs.IsModalOpen && UndoCount(c) == ul + 4 && c.UndoLabel == "Load character"
                && MiSame(after["face"], fileDoc["face"]) && MiSame(after["hair"], edited["hair"]) && MiSame(after["body"], edited["body"]),
                "«顔» only: one entry \"" + c.UndoLabel + "\", the face = the file's, the hair and body edits stay (" + dlg?.LastResult?.Message + ")");
            // «キャラ設定» only restores the name
            tab = CharaEditTab.Current ?? tab;
            tab.WriteCharacterName("X");
            yield return PlayModeHarness.WaitFrames(2);
            dlg = tab.OpenCharaLoadDialog();
            yield return WaitFor(() => dlg?.Browser != null && dlg.Browser.Ready, 30, ok);
            if (dlg != null) { dlg.FlagToggles[LoadFlags.Face].value = false; dlg.FlagToggles[LoadFlags.Profile].value = true; dlg.Browser.Choose(charaF); }
            yield return PlayModeHarness.WaitFrames(2);
            int up = UndoCount(c);
            dlg?.Press(CharaLoadDialog.LoadButtonId);
            yield return PlayModeHarness.WaitFrames(4);
            c.Check(dlg?.LastResult != null && dlg.LastResult.Ok && UndoCount(c) == up + 1 && MiStored(c, f).Chara.Profile.Name == MiChara && MiSame(MiDoc(MiStored(c, f).Chara)["face"], fileDoc["face"]),
                "«キャラ設定» only after a rename to «X»: the name is «" + MiStored(c, f).Chara.Profile.Name + "» again, one entry");
            // «髪» only from the HS2 card
            tab = CharaEditTab.Current ?? tab;
            dlg = tab.OpenCharaLoadDialog();
            yield return WaitFor(() => dlg?.Browser != null && dlg.Browser.Ready, 30, ok);
            bool listed = dlg != null && dlg.Browser.Choose(c.FemaleCard);
            if (dlg != null) { dlg.FlagToggles[LoadFlags.Profile].value = false; dlg.FlagToggles[LoadFlags.Hair].value = true; }
            yield return PlayModeHarness.WaitFrames(2);
            if (listed)
            {
                int uh = UndoCount(c);
                dlg.Press(CharaLoadDialog.LoadButtonId);
                yield return PlayModeHarness.WaitFrames(4);
                c.Check(dlg.LastResult != null && dlg.LastResult.Ok && UndoCount(c) == uh + 1 && MiSame(MiDoc(MiStored(c, f).Chara)["hair"], fCard["hair"]) && MiSame(MiDoc(MiStored(c, f).Chara)["face"], fileDoc["face"]),
                    "«髪» only from the HS2 card: the hair = the card's import, the face stays, one entry (" + dlg.LastResult?.Message + ")");
            }
            else { c.Warning("the female target card is not listed in the dialog: the card load was not taken"); dlg?.Press("cancel"); }
            yield return PlayModeHarness.WaitFrames(2);
            yield return MfUndoTo(c, ul);
            var back = MiDoc(MiStored(c, f).Chara);
            c.Check(MiStored(c, f).Chara.Profile.Name == MiChara && MiSame(back["face"], fileDoc["face"]) && MiSame(back["hair"], fileDoc["hair"]), "undo to before the edits: the documents are the saved file's again");
        }
        // «コーデを読込…» lists the saved outfit and loads it
        {
            tab = CharaEditTab.Current ?? MfOpenEdit(c, f) ?? tab;
            tab.ShowNode("file");
            yield return PlayModeHarness.WaitFrames(2);
            var cd = tab.OpenFileCoordinateDialog();
            var ok = new bool[1];
            yield return WaitFor(() => cd?.Browser != null && cd.Browser.Ready && cd.Browser.Lists(coordF), 30, ok);
            c.Check(cd != null && cd.Browser.Sex == 1 && cd.Browser.Lists(coordF) && ui.Dialogs.Current?.Spec.Id == CoordinateDialog.DialogId, "«コーデを読込…» lists «" + MiCoord + "» (" + cd?.Browser?.Index?.Rows.Count + " outfit files)");
            int uo = UndoCount(c);
            bool ch = cd != null && cd.Browser.Choose(coordF);
            cd?.Press("all");
            yield return PlayModeHarness.WaitFrames(4);
            c.Check(ch && cd.LastResult != null && cd.LastResult.Ok && UndoCount(c) == uo + 1 && c.UndoLabel == "Load coordinate" && MiStored(c, f).Coord.Name == MiCoord,
                "«" + MiCoord + "» «全て読込み»: one entry \"" + c.UndoLabel + "\", coord.name «" + MiStored(c, f).Coord.Name + "»");
            var okb = new bool[1];
            yield return WaitFor(() => c.App.Runtime.FindChara(f)?.State == RealizationState.Built, 60, okb);
            yield return MfUndoTo(c, uo);
        }

        // ---------------------------------------------------------------- multi-selection
        {
            ui.Ops.Select(new[] { f, m });
            var insp = Inspector(c);
            insp?.Refresh(true);
            insp?.Chara?.ShowTab(CharaEditTab.TabId);
            yield return PlayModeHarness.WaitFrames(3);
            var mt = insp?.Chara?.TabBodies.TryGetValue(CharaEditTab.TabId, out var mb) == true ? mb as CharaEditTab : null;
            mt?.ShowNode("file");
            yield return PlayModeHarness.WaitFrames(3);
            var body = MfQ<VisualElement>(mt, "file-page-body");
            c.Check(mt != null && mt.FileMultiDisabled && body != null && !body.enabledSelf && MhShown(MfQ<VisualElement>(mt, "file-multi-note")) && mt.OpenCharaLoadDialog() == null && !ui.Dialogs.IsModalOpen,
                "two characters selected: the File page is disabled with its note (file-multi-note)");
            ui.Ops.Select(new[] { f });
            yield return PlayModeHarness.WaitFrames(2);
        }

        // ---------------------------------------------------------------- male (HERO107)
        {
            var tm = MfOpenEdit(c, m);
            yield return PlayModeHarness.WaitFrames(4);
            if (tm != null)
            {
                yield return MiNoLater(c, tm, "HERO107");
                tm = MfOpenEdit(c, m) ?? tm;
                tm.ShowNode("character");
                yield return PlayModeHarness.WaitFrames(2);
                c.Check(MfQ<VisualElement>(tm, "field-" + InspectorSchemas.CharaEditPath("profile.futanari")) == null && MiValue(tm, "field-" + InspectorSchemas.CharaEditPath("profile.sex")) == L10n.Get("inspector.charaEdit.character.sex.male"),
                    "HERO107: no futanari line, the sex line reads «" + MiValue(tm, "field-" + InspectorSchemas.CharaEditPath("profile.sex")) + "»");
                tm.ShowNode("file");
                yield return PlayModeHarness.WaitFrames(2);
                tm.SaveCharacterFile();
                yield return PlayModeHarness.WaitFrames(2);
                MiAnswer(c, InspectorFlows.SaveCharaPromptId, MiMale);
                yield return PlayModeHarness.WaitFrames(2);
                var lm = File.Exists(charaM) ? CharaContainer.Load(charaM) : null;
                c.Check(lm != null && lm.Ok && lm.Value.LoadedManifest?.Sex == 0 && InspectorFlows.LastCharaSave.Ok, "HERO107 saved as chara/" + Path.GetFileName(charaM) + " (manifest sex " + lm?.Value?.LoadedManifest?.Sex + ")");
                tm.SaveCoordinateFile();
                yield return PlayModeHarness.WaitFrames(2);
                MiAnswer(c, InspectorFlows.SaveCoordPromptId, MiCoordM);
                yield return PlayModeHarness.WaitFrames(2);
                c.Check(File.Exists(coordM) && InspectorFlows.LastCoordSave.Ok && MiPngSize(coordM).w == 504, "HERO107's outfit saved as coordinate/male/" + Path.GetFileName(coordM));
                // «顔» only from the male file after a face edit
                int um = UndoCount(c);
                MiEdit(c, m, MiFacePath);
                yield return PlayModeHarness.WaitFrames(2);
                var dlg = tm.OpenCharaLoadDialog();
                var ok = new bool[1];
                yield return WaitFor(() => dlg?.Browser != null && dlg.Browser.Ready && dlg.Browser.Lists(charaM), 30, ok);
                c.Check(dlg != null && dlg.Browser.Sex == 0 && dlg.Browser.Lists(charaM) && !dlg.Browser.Lists(charaF), "the male dialog lists «" + MiMale + "» and not «" + MiChara + "» (" + dlg?.Browser?.Index?.Rows.Count + " files)");
                if (dlg != null)
                {
                    foreach (var t in dlg.FlagToggles) t.Value.value = t.Key == LoadFlags.Face;
                    dlg.Browser.Choose(charaM);
                }
                yield return PlayModeHarness.WaitFrames(2);
                dlg?.Press(CharaLoadDialog.LoadButtonId);
                yield return PlayModeHarness.WaitFrames(4);
                var mFile = lm != null && lm.Ok ? MiDoc(lm.Value.Chara) : null;
                c.Check(dlg?.LastResult != null && dlg.LastResult.Ok && UndoCount(c) == um + 2 && mFile != null && MiSame(MiDoc(MiStored(c, m).Chara)["face"], mFile["face"]),
                    "HERO107 «顔» only from its file: the face = the file's, one entry (" + dlg?.LastResult?.Message + ")");
                yield return MfUndoTo(c, um);
            }
            else c.Check(false, "the male's Edit tab");
        }
        // the female dialog does not list the male file
        {
            var tf2 = MfOpenEdit(c, f);
            yield return PlayModeHarness.WaitFrames(3);
            tf2?.ShowNode("file");
            yield return PlayModeHarness.WaitFrames(2);
            var dlg = tf2?.OpenCharaLoadDialog();
            var ok = new bool[1];
            yield return WaitFor(() => dlg?.Browser != null && dlg.Browser.Ready && dlg.Browser.Lists(charaF), 30, ok);
            c.Check(dlg != null && dlg.Browser.Lists(charaF) && !dlg.Browser.Lists(charaM), "the female dialog lists «" + MiChara + "» and not «" + MiMale + "»");
            dlg?.Press("cancel");
            yield return PlayModeHarness.WaitFrames(2);
        }

        // ---------------------------------------------------------------- clean-up
        while (ui.Dialogs.IsModalOpen) ui.Dialogs.CloseCurrent();
        yield return MfUndoTo(c, u00);
        ui.Options.Set(OptionDefs.MakerThumbnailFraming, "view");
        ui.Options.Set(OptionDefs.MakerLoadFlags, OptionDefs.MakerLoadFlagsAll);
        MiDelete(charaF, charaM, coordF, coordM);
        CoordinateBrowser.Invalidate(ui, FileBrowserKind.Character, 0);
        CoordinateBrowser.Invalidate(ui, FileBrowserKind.Character, 1);
        CoordinateBrowser.Invalidate(ui, FileBrowserKind.Coordinate, 0);
        CoordinateBrowser.Invalidate(ui, FileBrowserKind.Coordinate, 1);
        c.Check(UndoCount(c) == u00 && MiStored(c, f)?.Chara?.Profile?.Name == name0, "undo to the start: the history and the name are back («" + name0 + "»)");
        // the save toasts of this group are gone before the next group clicks through ui.input (a toast over a target is picked)
        var okt = new bool[1];
        yield return WaitFor(() => ui.Toasts == null || ui.Toasts.Count == 0, 10, okt);
        if (!okt[0]) c.Warning("toasts still shown at the end of MI (" + ui.Toasts?.Count + ")");
    }
}
