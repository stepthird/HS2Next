// Maker file tests of Phase 4.6 package A (save, thumbnails, the card path): rows
// MakerFileTest, .render, .timing, .userfiles and CoordApply.all; measurements VU1–VU5, VU7.
//   Run (headless):
//     VT1  every app save path writes app.version = Application.version and names the requires of installed zipmods: a scene
//          save (SceneOps), SaveChara, SaveCoordinate, a pose fragment, a standalone character import (CharaFileImport with the
//          app's options) and a standalone coordinate import (CoordFileImport); a mod accessory added by an edit is named
//     VT2  coordinate save of BR-Chan and HERO107 (headless: the placeholder with a warning): the whole file = the node's
//          coordinate (coord.json with the typed name, ext data, the res subset it references, the app's manifest), import
//          null, reload → save byte-identical, the node's documents and history untouched
//     VT3  ExtractCoordinate on 4.4's coordinate cases: an HS2 coordinate card worn in mode All (female: the SFW card of
//          MakerClothesTest WT7; male: the first readable male coordinate card), then saved: coord.json = the worn document,
//          the provenance of the card not copied
//     VT10 the card path: AddCharaFromCard without a registered importer imports (Stored, the subtree builder), a throwing
//          importer fails the add and adds nothing, a card-backed node (model API) builds through its import (CardCharaBuilder,
//          the import cached across a rebuild), its save writes a subtree and no hs2next.cardref block; no CardPartResolver
//          is constructed during the whole run (no runtime card build)
//     VT11 accessorySlotsSwapped = the changed slots: an add at BR-Chan's free slot 5 reports [5], an add at HERO107's slot 0
//          reports [0], a removal reports its slot
//   RunRender (graphics; edit mode with the URP asset of the render tests):
//     VT4  the coordinate thumbnails of BR-Chan's and HERO107's worn coordinates (MakerThumbnails.Coordinate): 504 × 704 RGB,
//          not the placeholder, equal bytes on two renders, the Maker pose row played (no thumbnail.poseFallback; VU5: a limb
//          bone > 1° away from the rest pose), hair objects inactive at the capture and active after, isolation (another
//          character at the origin, a Studio item and a light, fog on: bytes equal to the empty-scene render; a map cannot load
//          in edit mode); CoordFileImport writes a rendered thumbnail; CharaFileImport's shot is isolated the same way
//     VT5  Front = the pre-4.6 capture sequence (pose scope, own renderers on the free layer, other lights and fog off) byte
//          for byte, and SaveChara's thumbnail = it; View isolates (HERO107 between the display camera and BR-Chan: equal
//          bytes with and without him); RenderCharaThumbnail's textures encode to the same bytes as the captures
//   RunTiming (graphics, its own Unity session): VU1 coordinate thumbnail cold / warm (female, male), VU4 front / view
//     capture and the preview, character save (front, with the write), coordinate save (thumbnail + write) against the phase's targets.
//   RunUserFiles (headless, -Full): every .hs2n* file under the project's UserData, Spike/out and Cache loads with no error and
//     no warning (the frozen reader).
//   RunCoordApply (headless, -Full row CoordApply.all; -limit N = the first N files of each sex; -sex female|male and
//     -files "a.png|b.png;c.png" = only those files of that sex's folder, in that order, each ';' group on a fresh node):
//     every installed coordinate
//     card (female onto BR-Chan, male onto HERO107; the folder decides the sex as 4.4's loader) worn in mode All → the
//     objects and materials digest equals a fresh build of the merged documents → ExtractCoordinate saved to a .hs2ncoord
//     (placeholder, fixed clock) → reload → coord.json bytes equal the worn document's; bundles released every 10 files
//     (the live node rebuilt, OpenBundleCount logged); failures listed per file with the digest lines that differ (the
//     first 5 failures) (VU7).
// Run: -executeMethod MakerFileTest.Run [-only VT1,VT2]; .RunRender; .RunTiming [-reps N]; .RunUserFiles; .RunCoordApply
//      [-limit N]. Reports: Spike/out/maker/maker_file_report.txt, maker_file_render_report.txt, maker_file_timing_report.txt,
//      maker_file_userfiles_report.txt, maker_file_coordapply_report.txt; test files under Spike/out/maker/file/.
// Content rule: numbers only (hashes, sizes, PNG headers, document bytes); no image is opened; SFW targets; coordinate
// cards appear as file names and counts only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next.Chara.Documents;
using HS2Next.Chara.Edit;
using HS2Next.Content;
using HS2Next.Import;
using HS2Next.Motion.Face;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ColorAdjustments = UnityEngine.Rendering.Universal.ColorAdjustments;
using static MakerTestKit;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class MakerFileTest
{
    static readonly DateTime Clock0 = new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc);
    static readonly Func<DateTime> FixedClock = () => Clock0;
    static string Dir => Path.Combine(OutDir, "file");
    const string StarEar = "illusion.hs2#0";   // 352 row 0
    const string FemaleCoordCase = "ガーリーブラウス.png";   // MakerClothesTest WT7's SFW coordinate card

    static string CoordDirOf(string sex) => Path.Combine(TestTargets.UserData, "coordinate", sex);

    static byte[] CoordBytes(CoordDocument d) => CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, d.Write);
    static byte[] CharaBytes(CharaDocument d) => CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, d.Write);

    static SaveOptions Placeholder(MakerCoreTest.Harness h) =>
        AppSaveOptions.Complete(new SaveOptions { Thumbnail = ThumbnailMode.Placeholder, Clock = FixedClock }, h.Services);

    /// <summary>Width, height and colour type of a PNG from its IHDR (no decode); (-1, -1, -1) when it is not a PNG.</summary>
    static (int w, int h, int type) PngHeader(byte[] png)
    {
        if (png == null || png.Length < 26 || png[0] != 0x89 || png[1] != (byte)'P' || png[12] != (byte)'I' || png[13] != (byte)'H') return (-1, -1, -1);
        int Be(int o) => (png[o] << 24) | (png[o + 1] << 16) | (png[o + 2] << 8) | png[o + 3];
        return (Be(16), Be(20), png[25]);
    }

    static string Hash(byte[] b) => b == null ? "null" : Short(ResStore.Sha256Hex(b), 12);

    static bool IsRgb504x704(byte[] png) { var p = PngHeader(png); return p.w == 504 && p.h == 704 && p.type == 2; }

    static string Semver(string v) => Regex.IsMatch(v ?? "", @"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$") ? "semver" : "not semver";

    // ================================================================ Run (headless)

    public static void Run()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        Rep.AppendLine($"maker file (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, Application.version {Application.version})");
        Directory.CreateDirectory(Dir);
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        try
        {
            using (var h = MakerCoreTest.Harness.Open())
            {
                if (Want("VT1")) Guard("VT1 app.version and named requires on every save path", () => VT1(h));
                if (Want("VT2")) Guard("VT2 coordinate save", () => VT2(h));
                if (Want("VT3")) Guard("VT3 ExtractCoordinate on 4.4's coordinate cases", () => VT3(h));
                if (Want("VT10")) Guard("VT10 the card path", () => VT10(h));
                if (Want("VT11")) Guard("VT11 accessorySlotsSwapped", () => VT11(h));
                if (Want("VT6")) Guard("VT6 head names (HeadNameFacts)", () => HeadNamesCase(h));
                if (Want("VT6")) Guard("VT6 / VT7 load flags (BR-Chan)", () => VT6(h, FemaleCard, MaleCard, "BR-Chan"));
                if (Want("VT6")) Guard("VT6 / VT7 load flags (HERO107)", () => VT6(h, MaleCard, FemaleCard, "HERO107"));
                if (Want("VT8")) Guard("VT8 card-backed first edit", () => VT8(h));
            }
        }
        finally { FaceSetup.Seed = fseed; }
        Finish("maker_file_report.txt", "MakerFileTest VT1-VT3, VT6-VT8, VT10, VT11 (Phase 4.6)", sw);
    }

    // ---------------------------------------------------------------- VT1

    sealed class Written
    {
        public string What, Path;
        public Manifest Manifest;
        public string Error;
    }

    static Written Read(string what, string path)
    {
        var w = new Written { What = what, Path = path };
        try
        {
            ContainerBase c = null;
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext == SaveFormats.CharaExtension) { var r = CharaContainer.Load(path); c = r.Value; if (!r.Ok) w.Error = r.Error.Message; }
            else if (ext == SaveFormats.CoordExtension) { var r = CoordContainer.Load(path); c = r.Value; if (!r.Ok) w.Error = r.Error.Message; }
            else if (ext == SaveFormats.SceneExtension) { var r = SceneContainer.Load(path); c = r.Value; if (!r.Ok) w.Error = r.Error.Message; }
            else { var r = PoseContainer.Load(path); c = r.Value; if (!r.Ok) w.Error = r.Error.Message; }
            w.Manifest = c?.LoadedManifest;
        }
        catch (Exception e) { w.Error = e.GetType().Name + ": " + e.Message; }
        return w;
    }

    /// <summary>The requires entries of installed zipmods with a manifest name, and how many of them carry that name.</summary>
    static (int installed, int named, string bad) Named(MakerCoreTest.Harness h, Manifest m)
    {
        int installed = 0, named = 0;
        string bad = null;
        foreach (var e in m?.Requires ?? new List<RequiresEntry>())
        {
            if (e?.Guid == null || !h.Catalog.ZipmodsByGuid.TryGetValue(e.Guid, out var z) || z == null || z.Name == null) continue;
            installed++;
            if (e.Name == z.Name) named++;
            else bad ??= e.Guid + " name \"" + e.Name + "\" (zipmod \"" + z.Name + "\")";
        }
        return (installed, named, bad);
    }

    static void VT1(MakerCoreTest.Harness h)
    {
        string want = Application.version;
        Info($"Application.version {want} ({Semver(want)})");
        if (Semver(want) != "semver") Warn("VT1 Application.version is a semantic version", want + " (PlayerSettings.bundleVersion; T sets 0.3.0 in round 2)");
        var written = new List<Written>();
        string modGuid = null, modName = null;
        using (var n = NodeOf(h, FemaleCard))
        {
            // a mod accessory row of a named zipmod the character does not require yet, added by an edit
            var before = Read("before", Save(n, "vt1_before" + SaveFormats.CharaExtension));
            var required = new HashSet<string>((before.Manifest?.Requires ?? new List<RequiresEntry>()).Select(e => e.Guid), StringComparer.OrdinalIgnoreCase);
            var row = h.Catalog.EntriesOf(352).FirstOrDefault(e => !e.IsVanilla && e.Zipmod?.Name != null && e.Zipmod.Guid != null && !required.Contains(e.Zipmod.Guid));
            if (!Check("VT1 a mod row of category 352 from a named zipmod not required by BR-Chan exists", row != null)) return;
            modGuid = row.Zipmod.Guid; modName = row.Zipmod.Name;
            int slot = StudioOps.AccessorySlotsOf(n.Session.Model.Find(n.Id)).LowestFree();
            var add = n.Ops.AddAccessory(n.Id, 352, row.Key.ToString(), null, null, User, slot);
            n.Apply();
            Check("VT1 the mod accessory added by an edit (one entry)", add.Ok, add.Message + " " + row.Key);
            Info($"VT1 mod row {row.Key} of \"{modName}\" in slot {slot}");

            string scene = Path.Combine(Dir, "vt1_scene" + SaveFormats.SceneExtension);
            var sr = n.Runtime.Ops.Save(scene, new SaveOptions { Thumbnail = ThumbnailMode.Placeholder, Clock = FixedClock });
            Check("VT1 scene save (SceneOps.Save without an app version)", sr.Ok, sr.ToString());
            written.Add(Read("scene save", scene));

            string chara = Path.Combine(Dir, "vt1_chara" + SaveFormats.CharaExtension);
            var cr = n.Ops.SaveChara(n.Id, chara, false, CharaThumbnailFraming.Front, FixedClock);
            Check("VT1 SaveChara", cr.Ok, cr.Message);
            written.Add(Read("SaveChara", chara));

            string coord = Path.Combine(Dir, "vt1_coord" + SaveFormats.CoordExtension);
            var co = n.Ops.SaveCoordinate(n.Id, coord, "VT1", false, FixedClock);
            Check("VT1 SaveCoordinate", co.Ok, co.Message);
            written.Add(Read("SaveCoordinate", coord));

            string pose = Path.Combine(Dir, "vt1_pose" + SaveFormats.PoseExtension);
            var pr = PoseFragmentOps.Save(n.Runtime, n.Id, pose, "VT1", null, true, null);
            Check("VT1 pose fragment save (PoseFragmentOps, default options)", pr.Ok, pr.Error);
            written.Add(Read("pose fragment", pose));
        }
        var importer = ImportSetup.ImporterFor(h.Catalog);
        string imp = Path.Combine(Dir, "vt1_import" + SaveFormats.CharaExtension);
        var ir = CharaFileImport.ImportToFile(importer, FemaleCard, imp, h.Catalog, null, AppSaveOptions.Complete(new SaveOptions { Clock = FixedClock }, h.Services));
        Check("VT1 standalone character import (CharaFileImport with the app's options)", ir.Ok, ir.ToString());
        written.Add(Read("CharaFileImport", imp));
        string coordCard = Path.Combine(CoordDirOf("female"), FemaleCoordCase);
        if (File.Exists(coordCard))
        {
            string cimp = Path.Combine(Dir, "vt1_import" + SaveFormats.CoordExtension);
            var cir = CoordFileImport.ImportToFile(importer as Hs2Importer, coordCard, cimp, h.Services, null, new SaveOptions { Clock = FixedClock });
            Check("VT1 standalone coordinate import (CoordFileImport)", cir.Ok && cir.Thumbnail == null, cir.ToString());
            written.Add(Read("CoordFileImport", cimp));
        }
        else Warn("VT1 coordinate import", "the SFW coordinate card is not installed: " + FemaleCoordCase);

        foreach (var w in written.Where(x => x.What != "before"))
        {
            if (!Check($"VT1 {w.What}: the file reads", w.Error == null && w.Manifest != null, w.Error)) continue;
            Check($"VT1 {w.What}: app.version = Application.version ({want}), prerelease null", w.Manifest.AppVersion == want && w.Manifest.Prerelease == null,
                  $"app.version {w.Manifest.AppVersion}, prerelease {w.Manifest.Prerelease ?? "null"}");
            var (installed, named, bad) = Named(h, w.Manifest);
            Check($"VT1 {w.What}: every requires item of an installed named zipmod carries its manifest name", named == installed, $"{named} / {installed} named, {w.Manifest.Requires.Count} items{(bad != null ? "; first unnamed " + bad : "")}");
            if (w.What == "scene save" || w.What == "SaveChara" || w.What == "SaveCoordinate")
            {
                var e = w.Manifest.Requires.FirstOrDefault(x => string.Equals(x.Guid, modGuid, StringComparison.OrdinalIgnoreCase));
                Check($"VT1 {w.What}: the mod added by the edit is required with its name", e != null && e.Name == modName, e == null ? "missing" : "name " + e.Name);
            }
        }
    }

    static string Save(MakerEditTest.Node n, string file)
    {
        string path = Path.Combine(Dir, file);
        n.Ops.SaveChara(n.Id, path, false, CharaThumbnailFraming.Front, FixedClock);
        return path;
    }

    // ---------------------------------------------------------------- VT2 / VT3

    /// <summary>The coordinate file a save of <paramref name="n"/>'s worn coordinate with <paramref name="name"/> must give.</summary>
    static byte[] ExpectedCoordFile(MakerCoreTest.Harness h, MakerEditTest.Node n, string name)
    {
        var ex = n.Session.Model.File.ExtractCharacter(n.Id).ExtractCoordinate();
        if (name != null) ex.Coord.Name = name;
        return ex.ToBytes(Placeholder(h));
    }

    static void CoordSaveCase(MakerCoreTest.Harness h, MakerEditTest.Node n, string label, string name, bool headlessRender)
    {
        string path = Path.Combine(Dir, "vt_" + label + SaveFormats.CoordExtension);
        var coord0 = CoordBytes(n.Stored.Coord);
        var chara0 = CharaBytes(n.Stored.Chara);
        int undo0 = n.Session.Commands.UndoList.Count;
        var expected = ExpectedCoordFile(h, n, name);
        var r = n.Ops.SaveCoordinate(n.Id, path, name, headlessRender, FixedClock);
        var v = r.Value as CoordSaveResult;
        Check($"{label}: SaveCoordinate ok, placeholder{(headlessRender ? " with the no-device warning" : "")}",
              r.Ok && v != null && v.Thumbnail == "placeholder" && (!headlessRender || r.Warnings.Any(w => w.Contains("no graphics device"))),
              r.Message + " " + string.Join("; ", r.Warnings));
        if (v == null) return;
        var file = File.ReadAllBytes(path);
        Check($"{label}: the whole file = the node's coordinate with the name (coord.json, ext data, res subset, manifest)", file.SequenceEqual(expected),
              $"{file.Length} / {expected.Length} bytes");
        var lr = CoordContainer.Load(path);
        if (!Check($"{label}: the file loads with no warning", lr.Ok && !lr.Findings.Any(f => f.Severity != FindingSeverity.Info), lr.ToString())) return;
        var c = lr.Value;
        var want = CharaSubtree.CloneCoord(n.Stored.Coord);
        if (name != null) want.Name = name;
        Check($"{label}: coord.json = the node's {(name != null ? "with the typed name" : "(its own name)")}; import null; sex {want.Sex}",
              CoordBytes(c.Coord).SequenceEqual(CoordBytes(want)) && c.Import == null && c.Coord.Name == (name ?? n.Stored.Coord.Name) && c.Coord.Sex == want.Sex,
              $"name \"{c.Coord.Name}\", import {(c.Import == null ? "null" : c.Import.FileName)}");
        var refs = CharaSubtree.ReferencedResources(c.Coord);
        Check($"{label}: res/ = the referenced subset", c.Res.Names.All(refs.Contains) && refs.All(x => c.Res.Contains(x)), $"{c.Res.Count} entries, {refs.Count} referenced");
        var again = c.ToBytes(Placeholder(h));
        Check($"{label}: reload → save byte-identical", again.SequenceEqual(file), DocIoDiff(again, file));
        Check($"{label}: the node's documents and history untouched (the name lives in the file only)",
              CoordBytes(n.Stored.Coord).SequenceEqual(coord0) && CharaBytes(n.Stored.Chara).SequenceEqual(chara0) && n.Session.Commands.UndoList.Count == undo0);
    }

    static string DocIoDiff(byte[] a, byte[] b) => a.SequenceEqual(b) ? "identical" : $"{a.Length} / {b.Length} bytes";

    static void VT2(MakerCoreTest.Harness h)
    {
        using (var n = NodeOf(h, FemaleCard)) CoordSaveCase(h, n, "VT2 BR-Chan", "VT2 Coord", true);
        using (var n = NodeOf(h, MaleCard)) CoordSaveCase(h, n, "VT2 HERO107", "VT2 Coord M", false);
        using (var n = NodeOf(h, FemaleCard)) CoordSaveCase(h, n, "VT2 BR-Chan (name null)", null, false);
        h.ReleaseBundles();
    }

    /// <summary>The first file of <paramref name="sex"/>'s coordinate folder that the importer reads as a coordinate card.</summary>
    static string FirstCoordinate(MakerCoreTest.Harness h, string sex, string preferred = null)
    {
        string dir = CoordDirOf(sex);
        if (preferred != null && File.Exists(Path.Combine(dir, preferred))) return Path.Combine(dir, preferred);
        if (!Directory.Exists(dir)) return null;
        foreach (var f in Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.Ordinal).Take(40))
        {
            try { if (Hs2Importer.Detect(File.ReadAllBytes(f)) == Hs2SourceKind.Coord) return f; }
            catch (Exception) { }
        }
        return null;
    }

    static void VT3(MakerCoreTest.Harness h)
    {
        foreach (var (sex, card, label) in new[] { ("female", FemaleCard, "BR-Chan"), ("male", MaleCard, "HERO107") })
        {
            string file = FirstCoordinate(h, sex, sex == "female" ? FemaleCoordCase : null);
            if (file == null) { Warn("VT3 " + sex, "no readable coordinate card in " + sex); continue; }
            using (var n = NodeOf(h, card))
            {
                var r = n.Ops.ApplyCoordinateFile(new[] { n.Id }, file, CoordinateApplyMode.All, User);
                n.Apply();
                bool imported = (r.Value as MakerResult)?.Coordinate?.Imported == true;
                if (!Check($"VT3 {label}: {Rel(file)} worn in mode All (imported)", r.Ok && imported, r.Message)) continue;
                CoordSaveCase(h, n, "VT3 " + label, null, false);
            }
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- VT10

    sealed class ThrowingImporter : ICharaImporter
    {
        public CharaContainer Import(CardAppearanceSource source, FindingList findings) => throw new InvalidDataException("synthetic import failure");
    }

    static void VT10(MakerCoreTest.Harness h)
    {
        var prev = CharaImport.Importer;
        var s = SceneRuntimeTest.NewSession(h.Services, 0x4610UL);
        var rt = new SceneRuntime(s, h.Services, null, false);
        try
        {
            CharaImport.Importer = null;   // nothing registered: the runtime's importer is the catalog's
            var a = rt.Ops.AddCharaFromCard(FemaleCard);
            var ca = a.Ok ? rt.FindChara(a.NewId) : null;
            Check("VT10 AddCharaFromCard without a registered importer imports (Stored, no card, the subtree builder)",
                  ca?.State == RealizationState.Built && ca.Node.Appearance.Stored != null && ca.Node.Appearance.Card == null && ca.Builder == SubtreeCharaBuilder.Instance, a + " " + ca);

            CharaImport.Importer = new ThrowingImporter();
            int count0 = s.Model.Count, undo0 = s.Commands.UndoList.Count;
            var found = new FindingList();
            var bad = rt.Ops.AddCharaFromCard(MaleCard, importFindings: found);
            Check("VT10 a failing import fails the add (bad_args), adds nothing, no entry, chara.importFailed",
                  bad.Status == CommandStatus.Error && bad.Code == CommandCodes.BadArgs && s.Model.Count == count0 && s.Commands.UndoList.Count == undo0 && found.Has("chara.importFailed"), bad.ToString());
            CharaImport.Importer = null;

            int imports0 = CardCharaBuilder.ImportCount;
            var src = h.Services.CardSources.FromFile(MaleCard);
            var b = s.AddChara(new CharaAppearance { Card = src });
            rt.Apply();
            var cb = b.Ok ? rt.FindChara(b.NewId) : null;
            Check("VT10 a card-backed node (model API) builds through its import: CardCharaBuilder, one import",
                  cb?.State == RealizationState.Built && cb.Builder == CardCharaBuilder.Instance && cb.Card == src && CardCharaBuilder.ImportCount == imports0 + 1,
                  $"{cb}; imports {CardCharaBuilder.ImportCount - imports0}");
            // a rebuild of the same source (delete with no graveyard, undo) reuses the import
            var set = rt.Settings;
            int mc = set.GraveyardMaxCharas;
            set.GraveyardMaxCharas = 0;
            try
            {
                rt.Ops.Delete(new[] { b.NewId });
                s.Commands.Undo();
                rt.Apply();
            }
            finally { set.GraveyardMaxCharas = mc; }
            var cb2 = rt.FindChara(b.NewId);
            Check("VT10 a rebuild of the same card source reuses the cached import", cb2 != null && cb2 != cb && cb2.State == RealizationState.Built && CardCharaBuilder.ImportCount == imports0 + 1,
                  $"imports {CardCharaBuilder.ImportCount - imports0}");
            // its save: a subtree, no hs2next.cardref block
            var bytes = rt.Ops.ToBytes(Placeholder(h));
            var probe = SceneContainer.Load(bytes);
            int refs = probe.Ok ? probe.Value.Scene.Nodes.Count(x => x.Ext != null && x.Ext.Contains("hs2next.cardref")) : -1;
            Check("VT10 the save writes both characters as subtrees, no hs2next.cardref block, no chara.notImported",
                  probe.Ok && probe.Value.Charas.Count == 2 && refs == 0 && !s.Findings.Has("chara.notImported"), $"subtrees {(probe.Ok ? probe.Value.Charas.Count : -1)}, cardrefs {refs}");
        }
        finally
        {
            CharaImport.Importer = prev;
            rt.Dispose();
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- VT6 / VT7 load flags, VT8 card-backed first edit

    static readonly LoadFlags[] Singles = { LoadFlags.Face, LoadFlags.Body, LoadFlags.Hair, LoadFlags.Coordinate, LoadFlags.Profile };

    static void HeadNamesCase(MakerCoreTest.Harness h)
    {
        var facts = new HeadNameFacts(h.Catalog);
        using (var n = NodeOf(h, FemaleCard))
        {
            var head = n.Stored.Chara.Face.Head;
            var names = facts.Of(1, head);
            var real = n.Real.Context;
            var built = new HashSet<string>(StringComparer.Ordinal);
            if (real.HeadBone) foreach (var t in real.HeadBone.GetComponentsInChildren<Transform>(true)) built.Add(t.name);
            int inBuilt = built.Count(names.Contains);
            Check("VT6 HeadNameFacts.Of(BR-Chan's head): the head prefab's transforms, renderers and materials plus p_cf_head_bone (o_head, cf_m_skin_head*, no _create suffix)",
                  names.Count > 92 && names.Contains("o_head") && names.Any(x => x.StartsWith("cf_m_skin_head", StringComparison.Ordinal))
                  && !names.Any(x => x.EndsWith("_create", StringComparison.Ordinal)),
                  $"{names.Count} names, {inBuilt} of the built head's {built.Count} transforms");
            Check("VT6 HeadNameFacts: cached per head ref (one prefab read for two calls), an unknown head answers the vanilla names",
                  ReferenceEquals(facts.Of(1, head), names) && facts.Loads == 1 && facts.Of(1, "unknown.guid#12345").Count == HeadNames.Vanilla.Of(1, null).Count, "loads " + facts.Loads);
            Check("VT6 the body's renderer o_body_cf is not a head name", !names.Contains("o_body_cf"));
        }
        h.ReleaseBundles();
    }

    static List<(string, object)> EditEverySection(float k, string name) => new List<(string, object)>
    {
        ("appearance.chara.face.shape[0]", 0.11f + k),
        ("appearance.chara.body.shape[0]", 0.22f + k),
        ("appearance.chara.body.skin.color", new Color4(0.31f + k * 0.1f, 0.52f, 0.43f, 1f)),
        ("appearance.chara.hair.back.baseColor", new Color4(0.2f, 0.3f + k * 0.1f, 0.6f, 1f)),
        ("appearance.coord.clothes.top.colors[0].base", new Color4(0.7f, 0.2f, 0.1f + k * 0.1f, 1f)),
        ("appearance.chara.profile.name", name),
    };

    /// <summary>The documents a load of <paramref name="f"/> from <paramref name="src"/> into <paramref name="target"/> must give
    /// (the sections rule; plugin lists split by HeadNames' classes, the target's kept entries then the source's taken ones).</summary>
    static (CharaDocument chara, CoordDocument coord) Expected(CharaSubtree target, CharaSubtree src, LoadFlags f, IHeadNames names)
    {
        if ((f & LoadFlags.All) == LoadFlags.All) return (CharaSubtree.CloneChara(src.Chara), CharaSubtree.CloneCoord(src.Coord));
        var r = CharaSubtree.CloneChara(target.Chara);
        var s = CharaSubtree.CloneChara(src.Chara);
        bool face = (f & LoadFlags.Face) != 0, body = (f & LoadFlags.Body) != 0;
        int sex = r.Profile.Sex;
        var tHead = names.Of(sex, r.Face.Head);
        var sHead = names.Of(sex, s.Face.Head);
        var overlays = r.Body.SkinOverlays.Where(o => HeadNames.IsFace(o) ? !face : !body).Concat(s.Body.SkinOverlays.Where(o => HeadNames.IsFace(o) ? face : body)).ToList();
        var mods = r.Body.BoneMods.Where(m => HeadNames.IsFace(m, tHead) ? !face : !body).Concat(s.Body.BoneMods.Where(m => HeadNames.IsFace(m, sHead) ? face : body)).ToList();
        var mats = r.MaterialOverrides.Where(i => HeadNames.IsFace(i, tHead) ? !face : !body).Concat(s.MaterialOverrides.Where(i => HeadNames.IsFace(i, sHead) ? face : body)).ToList();
        if (body) { r.Body = s.Body; r.StudioOverlays.Skin = s.StudioOverlays.Skin; }
        r.Body.SkinOverlays = overlays;
        r.Body.BoneMods = mods;
        r.MaterialOverrides = mats;
        if (face) { r.Face = s.Face; r.StudioOverlays.Eyes = s.StudioOverlays.Eyes; }
        if ((f & LoadFlags.Hair) != 0) r.Hair = s.Hair;
        if ((f & LoadFlags.Profile) != 0) { r.Profile.Name = s.Profile.Name; r.Profile.Futanari = s.Profile.Futanari; r.Profile.Hs2 = s.Profile.Hs2; }
        var coord = CharaSubtree.CloneCoord(target.Coord);
        if ((f & LoadFlags.Coordinate) != 0)
        {
            coord = CharaSubtree.CloneCoord(src.Coord);
            coord.Sex = target.Coord.Sex;
            r.StudioOverlays.Clothes = s.StudioOverlays.Clothes;
        }
        return (r, coord);
    }

    /// <summary>The node's snapshot vs a fresh node of its current stored documents (the node's appearance; the scene file's
    /// subtree map is filled from it at save only, so after a whole-appearance load it still holds the old subtree).</summary>
    static HS2Next.Tests.MakerCore.SnapshotDiff VsFreshStored(MakerCoreTest.Harness h, MakerEditTest.Node n)
    {
        var c = new CharaContainer { Subtree = n.Stored.Clone() };
        c.Subtree.ManifestCharaUnknown = null;
        c.Res.CopyFrom(n.Session.Model.File.Res, c.Subtree.ReferencedResources());
        var s = n.Snapshot();
        using (var fresh = MakerEditTest.NewNode(h, c, n.Label + " (fresh)"))
        {
            if (!fresh.Ok) throw new Exception("fresh node: " + fresh.Error);
            return HS2Next.Tests.MakerCore.CharaSnapshot.Compare(fresh.Snapshot(), s, 12);
        }
    }

    static string FirstDiff(byte[] a, byte[] b)
    {
        if (a.SequenceEqual(b)) return null;
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        string Around(byte[] x) { int from = Math.Max(0, Math.Min(i, x.Length) - 60); return Encoding.UTF8.GetString(x, from, Math.Min(x.Length, i + 60) - from); }
        return $"at byte {i}: …{Around(a)}… vs …{Around(b)}…";
    }

    static void VT6(MakerCoreTest.Harness h, string card, string otherCard, string label)
    {
        // the second source: a copy changed in every section, written as a character file
        string file = Path.Combine(Dir, "vt6_source_" + label + SaveFormats.CharaExtension);
        using (var c = NodeOf(h, card))
        {
            var e = c.Ops.EditAppearance(new[] { c.Id }, EditEverySection(0.4f, "VT6 Source"), User);
            c.Apply();
            var sv = c.Ops.SaveChara(c.Id, file, false, CharaThumbnailFraming.Front, FixedClock);
            if (!Check($"VT6 {label}: the changed copy written as a character file", e.Ok && sv.Ok, e.Message + " " + sv.Message)) return;
        }
        var fromFile = CharaContainer.Load(file).Value.Subtree;
        var fromCard = MakerEditTest.Import(h, card, out string err)?.Subtree;
        if (!Check($"VT6 {label}: the card imported as the first source", fromCard != null, err)) return;
        var names = HeadNames.Current;
        using (var n = NodeOf(h, card))
        {
            var ids = new[] { n.Id };
            var edit = n.Ops.EditAppearance(ids, EditEverySection(0.2f, "VT6 Target"), User);
            n.Apply();
            if (!Check($"VT6 {label}: the target edited in every section", edit.Ok, edit.Message)) return;
            var target = n.Stored.Clone();
            var chara0 = CharaBytes(n.Stored.Chara); var coord0 = CoordBytes(n.Stored.Coord);
            Info($"VT6 {label} plugin lists: skin overlays {target.Chara.Body.SkinOverlays.Count}, bone modifiers {target.Chara.Body.BoneMods.Count}, MaterialEditor character items {target.Chara.MaterialOverrides.Count}");
            foreach (var (path, src, what) in new[] { (card, fromCard, "the HS2 card"), (file, fromFile, "the character file") })
                foreach (var f in Singles.Concat(new[] { LoadFlags.All }))
                {
                    int u0 = n.Session.Commands.UndoList.Count;
                    var exBefore = SubtreeBytes(n);
                    var r = n.Ops.LoadCharacter(ids, path, f, User);
                    n.Apply();
                    if (f == LoadFlags.All)
                        Check($"VT6 {label} {f} from {what}: the scene file's subtree of the node is the loaded one at once (a character save before the next scene save)",
                              SubtreeBytes(n).SequenceEqual(StoredBytes(n)));
                    var (ec, eo) = Expected(target, src, f, names);
                    var ac = CharaBytes(n.Stored.Chara); var ao = CoordBytes(n.Stored.Coord);
                    string dc = FirstDiff(ac, CharaBytes(ec)), dd = FirstDiff(ao, CoordBytes(eo));
                    var load = (r.Value as MakerResult)?.Load;
                    Check($"VT6 {label} {f} from {what}: one entry \"Load character\", the documents = the expected merge",
                          r.Ok && n.Session.Commands.UndoList.Count == u0 + 1 && load != null && load.Flags == f && dc == null && dd == null,
                          $"{r.Message} {(dc != null ? "chara " + dc : "")} {(dd != null ? "coord " + dd : "")}");
                    var d = VsFreshStored(h, n);
                    Check($"VT7 {label} {f} from {what}: the realization = a fresh build of the merged documents", d != null && d.Equal, d?.ToString());
                    n.Session.Commands.Undo();
                    n.Apply();
                    Check($"VT6 {label} {f} from {what}: undo restores the documents byte for byte", CharaBytes(n.Stored.Chara).SequenceEqual(chara0) && CoordBytes(n.Stored.Coord).SequenceEqual(coord0));
                    if (f == LoadFlags.All)
                        Check($"VT6 {label} {f} from {what}: undo restores the scene file's subtree of the node", SubtreeBytes(n).SequenceEqual(exBefore));
                }
            // the other sex: refused unless all five
            int u1 = n.Session.Commands.UndoList.Count;
            var bad = n.Ops.LoadCharacter(ids, otherCard, LoadFlags.Face, User);
            Check($"VT6 {label}: a partial load from the other sex is refused (bad_args path, {StudioOps.LoadOtherSexCode}), no entry",
                  !bad.Ok && bad.Arg == "path" && bad.Message.Contains(StudioOps.LoadOtherSexCode) && n.Session.Commands.UndoList.Count == u1, bad.Message);
            var allOther = n.Ops.LoadCharacter(ids, otherCard, LoadFlags.All, User);
            n.Apply();
            Check($"VT6 {label}: all five from the other sex change the character (sex follows the source)", allOther.Ok && n.Stored.Chara.Profile.Sex != target.Chara.Profile.Sex && n.Real?.State == RealizationState.Built, allOther.Message);
            n.Session.Commands.Undo();
            n.Apply();
            Check($"VT6 {label}: undo of the other-sex load restores the documents", CharaBytes(n.Stored.Chara).SequenceEqual(chara0) && CoordBytes(n.Stored.Coord).SequenceEqual(coord0));
            // Change character (the same table as a whole-character load)
            var exChange = SubtreeBytes(n);
            var ch = n.Ops.ChangeCharacter(ids, file, null, User);
            n.Apply();
            Check($"VT6 {label}: Change character places the file's subtree in the scene file too", ch.Ok && SubtreeBytes(n).SequenceEqual(StoredBytes(n))
                  && CharaBytes(n.Stored.Chara).SequenceEqual(CharaBytes(fromFile.Chara)), ch.Message);
            n.Session.Commands.Undo();
            n.Apply();
            Check($"VT6 {label}: undo of Change character restores the documents and the scene file's subtree",
                  CharaBytes(n.Stored.Chara).SequenceEqual(chara0) && CoordBytes(n.Stored.Coord).SequenceEqual(coord0) && SubtreeBytes(n).SequenceEqual(exChange));
        }
        h.ReleaseBundles();
    }

    // the chara and coord documents of the scene file's subtree of the node (what ExtractCharacter returns), or nothing
    static byte[] SubtreeBytes(MakerEditTest.Node n)
    {
        var t = n.Session.Model.File.ExtractCharacter(n.Id)?.Subtree;
        return t == null ? Array.Empty<byte>() : CharaBytes(t.Chara).Concat(CoordBytes(t.Coord)).ToArray();
    }

    static byte[] StoredBytes(MakerEditTest.Node n) => CharaBytes(n.Stored.Chara).Concat(CoordBytes(n.Stored.Coord)).ToArray();

    static void VT8(MakerCoreTest.Harness h)
    {
        var prev = CharaImport.Importer;
        CharaImport.Importer = h.Importer;   // the harness's importer: a fixed clock, so two imports give the same provenance
        SceneRuntime rt0 = null, rt = null;
        try
        {
            // the save before the edit, in a twin session (the save imports the card-backed node into its file)
            var s0 = SceneRuntimeTest.NewSession(h.Services, 0x4608UL);
            rt0 = new SceneRuntime(s0, h.Services, null, false);
            s0.AddChara(new CharaAppearance { Card = h.Services.CardSources.FromFile(FemaleCard) });
            rt0.Apply();
            var before = rt0.Ops.ToBytes(Placeholder(h));

            var s = SceneRuntimeTest.NewSession(h.Services, 0x4608UL);
            rt = new SceneRuntime(s, h.Services, null, false);
            var add = s.AddChara(new CharaAppearance { Card = h.Services.CardSources.FromFile(FemaleCard) });
            rt.Apply();
            var id = add.NewId;
            var real = rt.FindChara(id);
            var root0 = real?.Root;
            int imports0 = CardCharaBuilder.ImportCount;
            if (!Check("VT8 a card-backed node (model API) built by CardCharaBuilder", real?.State == RealizationState.Built && real.Builder == CardCharaBuilder.Instance, real?.ToString())) return;
            var ops = StudioOps.For(rt);
            int u0 = s.Commands.UndoList.Count;
            var e = ops.EditAppearance(new[] { id }, new List<(string, object)> { ("appearance.chara.face.shape[0]", 0.81f) }, User);
            rt.Apply();
            real = rt.FindChara(id);
            Check("VT8 the first edit imports in the same entry (one entry, the node stores the subtree, rebuilt by the subtree builder)",
                  e.Ok && s.Commands.UndoList.Count == u0 + 1 && s.Model.Find(id).Appearance.Stored != null && s.Model.File.Charas.ContainsKey(id)
                  && real.Builder == SubtreeCharaBuilder.Instance && real.Root != root0, e.Message);
            var after = rt.Ops.ToBytes(Placeholder(h));
            s.Commands.Undo();
            rt.Apply();
            real = rt.FindChara(id);
            Check("VT8 undo: card-backed again, the file's subtree removed, built from the cached import",
                  s.Model.Find(id).Appearance.Stored == null && s.Model.Find(id).Appearance.Card != null && !s.Model.File.Charas.ContainsKey(id)
                  && real.Builder == CardCharaBuilder.Instance && CardCharaBuilder.ImportCount == imports0, $"imports {CardCharaBuilder.ImportCount - imports0}");
            var undone = rt.Ops.ToBytes(Placeholder(h));
            Check("VT8 card-backed → edit → undo → save = the save before the edit", undone.SequenceEqual(before), $"{undone.Length} / {before.Length} bytes");
            s.Commands.Redo();
            rt.Apply();
            var redone = rt.Ops.ToBytes(Placeholder(h));
            Check("VT8 redo → save = the save after the edit", redone.SequenceEqual(after), $"{redone.Length} / {after.Length} bytes");
        }
        finally
        {
            CharaImport.Importer = prev;
            rt0?.Dispose();
            rt?.Dispose();
        }
        h.ReleaseBundles();
    }

    // ---------------------------------------------------------------- VT11

    static List<int> Swapped(MakerEditTest.Node n) => n.Real?.LastEditFlush?.AccessorySlotsSwapped?.ToList() ?? new List<int>();

    static void VT11(MakerCoreTest.Harness h)
    {
        foreach (var (card, label, slotWanted) in new[] { (FemaleCard, "BR-Chan", 5), (MaleCard, "HERO107", 0) })
            using (var n = NodeOf(h, card))
            {
                var rule = StudioOps.AccessorySlotsOf(n.Session.Model.Find(n.Id));
                int slot = rule.EntryAt(slotWanted) == null ? slotWanted : rule.LowestFree();
                if (slot != slotWanted) Warn("VT11 " + label, $"slot {slotWanted} is worn; slot {slot} used");
                var r = n.Ops.AddAccessory(n.Id, 352, StarEar, null, null, User, slot);
                n.Apply();
                var sw = Swapped(n);
                Check($"VT11 {label}: an add at slot {slot} reports [{slot}] in accessorySlotsSwapped", r.Ok && sw.SequenceEqual(new[] { slot }), "[" + string.Join(", ", sw) + "] " + r.Message);
                var d = VsFresh(h, n);
                Check($"VT11 {label}: the realization = a fresh node after the add", d != null && d.Equal, d?.ToString());
                var e = StudioOps.AccessorySlotsOf(n.Session.Model.Find(n.Id)).EntryAt(slot);
                var rm = e != null ? n.Ops.RemoveAccessory(n.Id, e.Id, User) : null;
                n.Apply();
                sw = Swapped(n);
                Check($"VT11 {label}: removing it reports [{slot}]", rm != null && rm.Ok && sw.SequenceEqual(new[] { slot }), "[" + string.Join(", ", sw) + "] " + rm?.Message);
            }
        h.ReleaseBundles();
    }

    // ================================================================ RunRender (graphics)

    sealed class UrpScope : IDisposable
    {
        readonly RenderPipelineAsset prevDefault, prevQuality;
        readonly bool prevAsync;
        public readonly UniversalRenderPipelineAsset Asset;
        public UrpScope()
        {
            prevDefault = GraphicsSettings.defaultRenderPipeline; prevQuality = QualitySettings.renderPipeline; prevAsync = ShaderUtil.allowAsyncCompilation;
            Asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HS2Next.App.AppPipeline.MeasureAssetPath);
            ShaderUtil.allowAsyncCompilation = false;
            GraphicsSettings.defaultRenderPipeline = Asset; QualitySettings.renderPipeline = Asset;
        }
        public void Dispose()
        {
            GraphicsSettings.defaultRenderPipeline = prevDefault; QualitySettings.renderPipeline = prevQuality; ShaderUtil.allowAsyncCompilation = prevAsync;
        }
    }

    /// <summary>
    /// A scene that is not the subject: a character at the origin, a vanilla item and a light, fog on, and an environment of
    /// its own as a loaded map brings one (maps load only in play mode): a global volume (exposure and saturation), a
    /// reflection probe around the origin with a red cubemap, a red flat ambient and a red custom default reflection.
    /// </summary>
    sealed class Clutter : IDisposable
    {
        public MakerEditTest.Node Node;
        readonly bool fog;
        readonly AmbientMode ambientMode;
        readonly Color ambientLight;
        readonly SphericalHarmonicsL2 ambientProbe;
        readonly DefaultReflectionMode reflMode;
        readonly Texture refl;
        readonly float reflIntensity;
        readonly List<Object> owned = new List<Object>();
        public string Log;
        public Clutter(MakerCoreTest.Harness h, string card, bool environment = true)
        {
            fog = RenderSettings.fog;
            ambientMode = RenderSettings.ambientMode; ambientLight = RenderSettings.ambientLight; ambientProbe = RenderSettings.ambientProbe;
            reflMode = RenderSettings.defaultReflectionMode; refl = RenderSettings.customReflectionTexture; reflIntensity = RenderSettings.reflectionIntensity;
            Node = NodeOf(h, card);
            var i = Node.Runtime.Ops.AddItem(ContentId.Vanilla(399));
            var l = Node.Runtime.Ops.AddLight(ContentId.Vanilla(2));
            Node.Apply();
            RenderSettings.fog = true;
            string env = "";
            if (environment)
            {
                var red = new Cubemap(16, TextureFormat.RGBA32, true) { hideFlags = HideFlags.HideAndDontSave };
                for (int f = 0; f < 6; f++)
                {
                    var px = new Color[16 * 16];
                    for (int k = 0; k < px.Length; k++) px[k] = new Color(0.9f, 0.1f, 0.1f, 1f);
                    red.SetPixels(px, (CubemapFace)f);
                }
                red.Apply(true, false);
                owned.Add(red);
                var vgo = new GameObject("MakerFileTest clutter volume");
                var vol = vgo.AddComponent<Volume>();
                vol.isGlobal = true;
                vol.priority = 100;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                var ca = profile.Add<ColorAdjustments>(true);
                ca.postExposure.Override(1.5f);
                ca.saturation.Override(-80f);
                vol.sharedProfile = profile;
                owned.Add(profile); owned.Add(vgo);
                var pgo = new GameObject("MakerFileTest clutter probe");
                var probe = pgo.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Custom;
                probe.customBakedTexture = red;
                probe.size = new Vector3(100f, 100f, 100f);
                probe.importance = 100;
                owned.Add(pgo);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(1f, 0.2f, 0.2f);
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = red;
                RenderSettings.reflectionIntensity = 0.4f;
                env = ", a global volume (exposure 1.5, saturation -80), a red reflection probe, a red flat ambient and default reflection";
            }
            Log = $"clutter: {Rel(card)} at the origin, item 399 {(i.Ok ? "added" : i.ToString())}, light 2 {(l.Ok ? "added" : l.ToString())}, fog on{env}, enabled lights {Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude).Count(x => x.enabled)}";
        }
        public void Dispose()
        {
            RenderSettings.fog = fog;
            RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambientLight; RenderSettings.ambientProbe = ambientProbe;
            RenderSettings.defaultReflectionMode = reflMode; RenderSettings.customReflectionTexture = refl; RenderSettings.reflectionIntensity = reflIntensity;
            foreach (var o in owned) if (o) Object.DestroyImmediate(o);
            Node?.Dispose();
        }
    }

    public static void RunRender()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        Rep.AppendLine($"maker file render (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName})");
        Directory.CreateDirectory(Dir);
        bool device = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
        Check("graphics device present", device, SystemInfo.graphicsDeviceType.ToString());
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        try
        {
            if (device)
                using (var urp = new UrpScope())
                using (var h = MakerCoreTest.Harness.Open())
                {
                    Check("URP asset of the render tests active", urp.Asset != null && GraphicsSettings.currentRenderPipeline == urp.Asset);
                    h.Services.Db ??= h.Catalog.LoadStudioLists();   // the clutter's item and light rows
                    var settings = new ThumbnailSettings { Pipeline = urp.Asset };
                    if (Want("VT4")) Guard("VT4 coordinate thumbnails", () => VT4(h, settings));
                    if (Want("VT4")) Guard("VT4 standalone imports", () => VT4Imports(h, settings));
                    if (Want("VT5")) Guard("VT5 character framings", () => VT5(h, settings));
                }
        }
        finally { FaceSetup.Seed = fseed; }
        Finish("maker_file_render_report.txt", "MakerFileTest.render VT4, VT5 (Phase 4.6)", sw);
    }

    static CoordContainer WornCoordinate(MakerCoreTest.Harness h, string card)
    {
        using (var n = NodeOf(h, card)) return n.Session.Model.File.ExtractCharacter(n.Id).ExtractCoordinate();
    }

    static void VT4(MakerCoreTest.Harness h, ThumbnailSettings settings)
    {
        var placeholder = ThumbnailPlaceholder.Png();
        foreach (var (card, label) in new[] { (FemaleCard, "BR-Chan"), (MaleCard, "HERO107") })
        {
            var coord = WornCoordinate(h, card);
            var f1 = new FindingList();
            var p1 = MakerThumbnails.Coordinate(coord, h.Services, settings, f1, out var i1);
            var p2 = MakerThumbnails.Coordinate(coord, h.Services, settings, new FindingList(), out var i2);
            Info($"VT4 {label}: {i1}; {Hash(p1)} / {Hash(p2)}, {p1?.Length} bytes; findings: {string.Join("; ", f1.Select(f => f.ToString()))}");
            Check($"VT4 {label}: 504 × 704 RGB PNG, not the placeholder", IsRgb504x704(p1) && !p1.SequenceEqual(placeholder), $"{PngHeader(p1)}");
            Check($"VT4 {label}: two renders give the same bytes", p1 != null && p2 != null && p1.SequenceEqual(p2), $"{Hash(p1)} / {Hash(p2)}");
            Check($"VT4 {label}: the Maker pose row 0 played (no thumbnail.poseFallback)", i1.PoseRow != null && !f1.Has("thumbnail.poseFallback"), i1.PoseRow ?? i1.PoseFallback);
            Check($"VU5 {label}: a limb bone > 1° away from the rest pose", i1.MaxLimbDeltaDeg > 1.0, $"{i1.MaxLimbDeltaDeg:F2}° ({i1.MaxLimbBone})");
            Check($"VT4 {label}: hair objects inactive at the capture and active after", i1.HairObjects > 0 && i1.HairActiveAtCapture == 0 && i1.HairActiveAfter == i1.HairObjects,
                  $"hair {i1.HairObjects}, at capture {i1.HairActiveAtCapture}, after {i1.HairActiveAfter}");
            Check($"VT4 {label}: the coordinate's sex decides the build", i1.Sex == (coord.Coord.Sex == 0 ? 0 : 1), "sex " + i1.Sex);
            byte[] p3;
            string log;
            using (var c = new Clutter(h, FemaleCard))
            {
                log = c.Log;
                p3 = MakerThumbnails.Coordinate(coord, h.Services, settings, new FindingList(), out var i3);
                Info($"VT4 {label} with clutter: {i3}");
            }
            Check($"VT4 {label}: isolated (another character at the origin, an item, a light, fog, a volume, a reflection probe, the ambient and the reflection: bytes equal to the empty-scene render)", p3 != null && p3.SequenceEqual(p1), $"{log}; {Hash(p3)} vs {Hash(p1)}");
            Check($"VT4 {label}: no stray objects left in the scene", GameObject.Find("hs2next_coordinate_thumbnail") == null);
        }
        h.ReleaseBundles();
    }

    static void VT4Imports(MakerCoreTest.Harness h, ThumbnailSettings settings)
    {
        string file = FirstCoordinate(h, "female", FemaleCoordCase);
        var importer = ImportSetup.ImporterFor(h.Catalog) as Hs2Importer;
        if (file != null)
        {
            string outFile = Path.Combine(Dir, "vt4_coord_import" + SaveFormats.CoordExtension);
            var r = CoordFileImport.ImportToFile(importer, file, outFile, h.Services, settings, new SaveOptions { Clock = FixedClock });
            var lr = r.Ok ? CoordContainer.Load(outFile) : null;
            var thumb = lr?.Value?.Thumbnail;
            Check("VT4 CoordFileImport writes a rendered thumbnail (504 × 704 RGB, not the placeholder, = the result's)",
                  r.Ok && r.Thumbnail != null && IsRgb504x704(thumb) && thumb.SequenceEqual(r.Thumbnail) && !thumb.SequenceEqual(ThumbnailPlaceholder.Png()),
                  $"{r}; {r.ThumbnailInfo}");
        }
        else Warn("VT4 CoordFileImport", "no readable female coordinate card");
        // the importer's thumbnail hook (ImportThumbnails): both kinds rendered at import
        var hooked = new Hs2Importer(h.Catalog, new ImportOptions { AppVersion = AppSaveOptions.Version, Clock = FixedClock, Thumbnails = new ImportThumbnails(h.Services, settings) });
        var ic = hooked.ImportChara(FemaleCard);
        Check("VT4 ImportThumbnails.RenderChara at a character import: 504 × 704 RGB", ic.Ok && IsRgb504x704(ic.Container.Thumbnail), ic.Ok ? Hash(ic.Container.Thumbnail) : "import failed");
        if (file != null)
        {
            var ik = hooked.ImportCoord(file);
            Check("VT4 ImportThumbnails.RenderCoord at a coordinate import: 504 × 704 RGB, = MakerThumbnails.Coordinate of the imported file",
                  ik.Ok && IsRgb504x704(ik.Container.Thumbnail) && ik.Container.Thumbnail.SequenceEqual(MakerThumbnails.Coordinate(ik.Container, h.Services, settings, null) ?? new byte[0]),
                  ik.Ok ? Hash(ik.Container.Thumbnail) : "import failed");
        }
        // CharaFileImport's front shot is isolated
        var cc = MakerEditTest.Import(h, FemaleCard, out string err);
        if (!Check("VT4 BR-Chan imported for the standalone shot", cc != null, err)) return;
        var a = CharaFileImport.RenderThumbnail(cc, h.Catalog, settings);
        byte[] b;
        using (var c = new Clutter(h, MaleCard)) b = CharaFileImport.RenderThumbnail(cc, h.Catalog, settings);
        Check("VT4 CharaFileImport's shot: 504 × 704 RGB and isolated (equal bytes with HERO107, an item, a light and fog in the scene)", IsRgb504x704(a) && b != null && a.SequenceEqual(b), $"{Hash(a)} / {Hash(b)}");
        h.ReleaseBundles();
    }

    /// <summary>The front capture as it was before Phase 4.6 (the oracle of "unchanged bytes").</summary>
    static byte[] OldFront(CharaRealization r, ThumbnailSettings settings)
    {
        var rt = r.Runtime;
        var map = rt.GetPart<MapRealization>();
        int layer = CharaThumbnail.FreeLayer(rt.Root.transform, map != null ? map.Scene : default);
        if (layer < 0) return null;
        using (StudioDefaultPoseScope.Enter(r.Root, rt.Services.Db))
        {
            var renderers = VisibilityApplier.OwnRenderers(r.Holder);
            var oldLayers = new int[renderers.Count];
            var lightsOff = new List<Light>();
            bool fog = RenderSettings.fog;
            try
            {
                for (int i = 0; i < renderers.Count; i++) { oldLayers[i] = renderers[i].gameObject.layer; renderers[i].gameObject.layer = layer; }
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude)) if (l.enabled) { l.enabled = false; lightsOff.Add(l); }
                RenderSettings.fog = false;
                var s = settings ?? new ThumbnailSettings();
                var shot = new ThumbnailSettings { Background = s.Background, Pipeline = s.Pipeline, CharaLighting = true, NearClip = s.NearClip, FarClip = s.FarClip, CullingMask = 1 << layer };
                var rot = r.Holder.rotation;
                var offset = r.Sex == 0 ? ThumbnailCapture.CoordCameraMale : ThumbnailCapture.CoordCameraFemale;
                return ThumbnailCapture.Capture(r.Holder.position + rot * offset, rot * Quaternion.Euler(ThumbnailCapture.CoordCameraEuler), ThumbnailCapture.CropFov(ThumbnailCapture.Hs2Fov), shot);
            }
            finally
            {
                RenderSettings.fog = fog;
                foreach (var l in lightsOff) if (l) l.enabled = true;
                for (int i = 0; i < renderers.Count; i++) if (renderers[i]) renderers[i].gameObject.layer = oldLayers[i];
            }
        }
    }

    static byte[] Encode(OpResult r, out CharaThumbnailRender v)
    {
        v = r?.Value as CharaThumbnailRender;
        if (v?.Texture == null) return null;
        try { return v.Texture.EncodeToPNG(); }
        finally { Object.DestroyImmediate(v.Texture); }
    }

    static void VT5(MakerCoreTest.Harness h, ThumbnailSettings settings)
    {
        using (var n = NodeOf(h, FemaleCard))
        {
            n.Runtime.AddStandardParts();   // the display camera (View), the map part and the chara light
            n.Apply();
            n.Runtime.Tick(0f);   // the display camera takes HS2's reset view
            n.Ops.ThumbnailRenderSettings = settings;
            var real = n.Real;
            var oracle = OldFront(real, settings);
            var front = CharaThumbnail.Capture(real, settings);
            Info($"VT5 the pre-4.6 capture sequence (the scene's environment): {Hash(oracle)}; Front with its own environment: {Hash(front)}");
            // the shot does not depend on what ran before it: the scene stepped 30 frames (physics, blink, look), then two shots
            for (int i = 0; i < 30; i++) n.Runtime.Tick(1f / 30f);
            var after1 = CharaThumbnail.Capture(real, settings);
            var after2 = CharaThumbnail.Capture(real, settings);
            Check("VT5 Front is independent of the history: after 30 scene frames two shots are equal and equal the shot before them",
                  after1 != null && after1.SequenceEqual(after2) && after1.SequenceEqual(front), $"{Hash(after1)} / {Hash(after2)} / {Hash(front)}");
            byte[] cl;
            using (var c = new Clutter(h, MaleCard)) cl = CharaThumbnail.Capture(real, settings);
            Check("VT5 Front is isolated from the scene's environment (a volume, a reflection probe, the ambient and the reflection)", cl != null && cl.SequenceEqual(front), Hash(cl));
            var pf = Encode(n.Ops.RenderCharaThumbnail(n.Id, CharaThumbnailFraming.Front), out var vf);
            Check("VT5 RenderCharaThumbnail(Front) encodes to the same bytes", pf != null && pf.SequenceEqual(front) && vf.Framing == CharaThumbnailFraming.Front, Hash(pf));
            string path = Path.Combine(Dir, "vt5_front" + SaveFormats.CharaExtension);
            var sr = n.Ops.SaveChara(n.Id, path, true, CharaThumbnailFraming.Front, FixedClock);
            var thumb = sr.Ok ? CharaContainer.Load(path).Value?.Thumbnail : null;
            Check("VT5 SaveChara(Front): the file's thumbnail = the capture; framing front", thumb != null && thumb.SequenceEqual(front) && (sr.Value as CharaSaveResult)?.Framing == "front", sr.Message);

            var cam = n.Runtime.GetPart<SceneViewCamera>()?.Camera;
            if (!Check("VT5 the display camera exists", cam)) return;
            Info($"VT5 view: camera {cam.transform.position} rot {cam.transform.eulerAngles} fov {cam.fieldOfView}");
            var view = CharaThumbnail.CaptureView(real, cam, settings);
            var pv = Encode(n.Ops.RenderCharaThumbnail(n.Id, CharaThumbnailFraming.View), out var vv);
            Check("VT5 View: 504 × 704 RGB, differs from Front, RenderCharaThumbnail(View) encodes to the same bytes",
                  IsRgb504x704(view) && !view.SequenceEqual(front) && pv != null && pv.SequenceEqual(view) && vv.Framing == CharaThumbnailFraming.View, $"{Hash(view)} / {Hash(pv)}");
            // HERO107 between the camera and BR-Chan
            var add = n.Runtime.Ops.AddCharaFromCard(MaleCard);
            if (!Check("VT5 HERO107 added", add.Ok, add.ToString())) return;
            var mid = Vector3.Lerp(cam.transform.position, real.Holder.position + Vector3.up * 8f, 0.5f);
            n.Session.Set(add.NewId, "transform.pos", new Vec3(mid.x, 0f, mid.z));
            n.Apply();
            var view2 = CharaThumbnail.CaptureView(real, cam, settings);
            byte[] viewEnv;
            using (var c = new Clutter(h, FemaleCard)) viewEnv = CharaThumbnail.CaptureView(real, cam, settings);
            Check("VT5 View is isolated from the scene's environment too", viewEnv != null && viewEnv.SequenceEqual(view), Hash(viewEnv));
            Check("VT5 View isolates: equal bytes with HERO107 in front of the camera", view2 != null && view2.SequenceEqual(view), $"HERO107 at ({mid.x:F1}, 0, {mid.z:F1}); {Hash(view2)} vs {Hash(view)}");
            var front2 = CharaThumbnail.Capture(real, settings);
            Check("VT5 Front isolates too: equal bytes with HERO107 in the scene", front2 != null && front2.SequenceEqual(front), Hash(front2));
            string pathV = Path.Combine(Dir, "vt5_view" + SaveFormats.CharaExtension);
            var sv = n.Ops.SaveChara(n.Id, pathV, true, CharaThumbnailFraming.View, FixedClock);
            var tv = sv.Ok ? CharaContainer.Load(pathV).Value?.Thumbnail : null;
            Check("VT5 SaveChara(View): the file's thumbnail = the view capture; framing view", tv != null && tv.SequenceEqual(view) && (sv.Value as CharaSaveResult)?.Framing == "view", sv.Message);
        }
        using (var n = NodeOf(h, FemaleCard))   // no display camera: View falls back to Front
        {
            n.Ops.ThumbnailRenderSettings = settings;
            var r = n.Ops.RenderCharaThumbnail(n.Id, CharaThumbnailFraming.View);
            var p = Encode(r, out var v);
            Check("VT5 without a display camera View falls back to Front with thumbnail.noView", p != null && v.Framing == CharaThumbnailFraming.Front && r.Warnings.Any(w => w.StartsWith("thumbnail.noView", StringComparison.Ordinal)),
                  string.Join("; ", r.Warnings));
        }
        h.ReleaseBundles();
    }

    // ================================================================ RunTiming (graphics, own session)

    public static void RunTiming()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        Rep.AppendLine($"maker file timing (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName})");
        Directory.CreateDirectory(Dir);
        bool device = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
        Check("graphics device present", device, SystemInfo.graphicsDeviceType.ToString());
        int reps = Reps(5);
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        try
        {
            if (device)
                using (var urp = new UrpScope())
                using (var h = MakerCoreTest.Harness.Open())
                {
                    h.Services.Db ??= h.Catalog.LoadStudioLists();
                    var settings = new ThumbnailSettings { Pipeline = urp.Asset };
                    Guard("VU1 coordinate thumbnail", () => VU1(h, settings, reps));
                    Guard("VU4 captures, preview and saves", () => VU4(h, settings, reps));
                    Guard("VU2 partial-load times", () => VU2(h, reps));
                    Guard("VU3 card-backed first edit", () => VU3(h, reps));
                }
        }
        finally { FaceSetup.Seed = fseed; }
        Finish("maker_file_timing_report.txt", "MakerFileTest.timing VU1-VU4 (Phase 4.6)", sw);
    }

    static void VU1(MakerCoreTest.Harness h, ThumbnailSettings settings, int reps)
    {
        bool first = true;
        foreach (var (card, label) in new[] { (FemaleCard, "female (BR-Chan's coordinate)"), (MaleCard, "male (HERO107's coordinate)") })
        {
            var coord = WornCoordinate(h, card);
            var t = Stopwatch.StartNew();
            MakerThumbnails.Coordinate(coord, h.Services, settings, null, out var i0);
            double cold = t.Elapsed.TotalMilliseconds;
            var warm = new List<double>();
            CoordThumbnailInfo last = null;
            for (int i = 0; i < reps; i++)
            {
                t.Restart();
                MakerThumbnails.Coordinate(coord, h.Services, settings, null, out last);
                warm.Add(t.Elapsed.TotalMilliseconds);
            }
            Info($"VU1 {label}: cold {i0}; warm {last}; open bundles {h.Catalog.OpenBundleCount}");
            Target($"VU1 coordinate thumbnail {label}, {(first ? "cold (first in the session)" : "first of this sex")}", cold, 3000);
            Target($"VU1 coordinate thumbnail {label}, warm (median of {reps})", Median(warm), 1000);
            first = false;
        }
        h.ReleaseBundles();
    }

    static void VU2(MakerCoreTest.Harness h, int reps)
    {
        using (var n = NodeOf(h, FemaleCard))
        {
            var ids = new[] { n.Id };
            n.Ops.EditAppearance(ids, EditEverySection(0.2f, "VU2"), User);
            n.Apply();
            foreach (var f in Singles.Concat(new[] { LoadFlags.All }))
            {
                var times = new List<double>();
                int rebuilds0 = n.Real.EditRebuilds;
                for (int i = 0; i <= reps; i++)
                {
                    var t = Stopwatch.StartNew();
                    var r = n.Ops.LoadCharacter(ids, FemaleCard, f, User);
                    n.Apply();
                    if (i > 0) times.Add(t.Elapsed.TotalMilliseconds);   // the first is the warm-up
                    if (!r.Ok) { Check($"VU2 {f}: the load runs", false, r.Message); break; }
                    n.Session.Commands.Undo();
                    n.Apply();
                }
                string note = $"edit rebuilds {n.Real.EditRebuilds - rebuilds0} over {reps + 1} loads and undos";
                if (f == LoadFlags.All) Info($"VU2 all five (as Change character, a rebuild): median {F(Median(times))} ms; {note}");
                else Target($"VU2 partial load {f} on BR-Chan, op + flush (median of {reps}; {note})", Median(times), f == LoadFlags.Coordinate ? 300 : 50);
            }
        }
        h.ReleaseBundles();
    }

    static void VU3(MakerCoreTest.Harness h, int reps)
    {
        var list = new List<double>();
        for (int i = 0; i < Math.Max(1, reps / 2); i++)
        {
            var s = SceneRuntimeTest.NewSession(h.Services, 0x4603UL);
            var rt = new SceneRuntime(s, h.Services, null, false);
            try
            {
                var add = s.AddChara(new CharaAppearance { Card = h.Services.CardSources.FromFile(FemaleCard) });
                rt.Apply();
                var t = Stopwatch.StartNew();
                var e = StudioOps.For(rt).EditAppearance(new[] { add.NewId }, new List<(string, object)> { ("appearance.chara.face.shape[0]", 0.81f) }, User);
                rt.Apply();
                list.Add(t.Elapsed.TotalMilliseconds);
                if (!e.Ok) { Check("VU3 the first edit runs", false, e.Message); break; }
            }
            finally { rt.Dispose(); }
        }
        Info($"VU3 card-backed first edit on BR-Chan (import + rebuild + the edit; recorded, no target): median {F(Median(list))} ms over {list.Count} ({string.Join(", ", list.Select(F))})");
        h.ReleaseBundles();
    }

    static void VU4(MakerCoreTest.Harness h, ThumbnailSettings settings, int reps)
    {
        using (var n = NodeOf(h, FemaleCard))
        {
            n.Runtime.AddStandardParts();
            n.Apply();
            n.Runtime.Tick(0f);
            n.Ops.ThumbnailRenderSettings = settings;
            var real = n.Real;
            var cam = n.Runtime.GetPart<SceneViewCamera>()?.Camera;
            CharaThumbnail.Capture(real, settings);   // warm-up
            Target($"VU4 front capture (median of {reps})", CpuMs(() => CharaThumbnail.Capture(real, settings), reps), 60);
            if (cam) Target($"VU4 view capture (median of {reps})", CpuMs(() => CharaThumbnail.CaptureView(real, cam, settings), reps), 60);
            Target($"VU4 preview render RenderCharaThumbnail(View) incl. the texture (median of {reps})", CpuMs(() =>
            {
                var v = n.Ops.RenderCharaThumbnail(n.Id, CharaThumbnailFraming.View).Value as CharaThumbnailRender;
                if (v?.Texture) Object.DestroyImmediate(v.Texture);
            }, reps), 60);
            string path = Path.Combine(Dir, "vu4_front" + SaveFormats.CharaExtension);
            Target($"character save (front, with the file write; median of {reps})", CpuMs(() => n.Ops.SaveChara(n.Id, path, true, CharaThumbnailFraming.Front, FixedClock), reps), 300);
            string cpath = Path.Combine(Dir, "vu4_coord" + SaveFormats.CoordExtension);
            n.Ops.SaveCoordinate(n.Id, cpath, "VU4", true, FixedClock);   // warm-up
            Target($"coordinate save (thumbnail + write; warm median of {reps})", CpuMs(() => n.Ops.SaveCoordinate(n.Id, cpath, "VU4", true, FixedClock), reps), 1500);
        }
        h.ReleaseBundles();
    }

    // ================================================================ RunUserFiles (headless, -Full)

    static IEnumerable<string> Walk(string dir)
    {
        if (!Directory.Exists(dir)) yield break;
        var stack = new Stack<string>();
        stack.Push(dir);
        while (stack.Count > 0)
        {
            string d = stack.Pop();
            string[] files, dirs;
            try { files = Directory.GetFiles(d); dirs = Directory.GetDirectories(d); }
            catch (Exception) { continue; }
            foreach (var f in files) yield return f;
            foreach (var s in dirs)
            {
                try { if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0) continue; }   // never follow a junction
                catch (Exception) { continue; }
                stack.Push(s);
            }
        }
    }

    public static void RunUserFiles()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        Rep.AppendLine($"maker file user files (Unity {Application.unityVersion}; the frozen reader: prerelease {SaveFormats.Prerelease ?? "null"})");
        string root = ContentSettings.ProjectRoot;
        var exts = new[] { SaveFormats.CharaExtension, SaveFormats.CoordExtension, SaveFormats.SceneExtension, SaveFormats.PoseExtension };
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var bad = new List<string>();
        int total = 0;
        foreach (var top in new[] { "UserData", Path.Combine("Spike", "out"), "Cache" })
            foreach (var f in Walk(Path.Combine(root, top)))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (!exts.Contains(ext)) continue;
                total++;
                counts[top + " " + ext] = counts.TryGetValue(top + " " + ext, out var c) ? c + 1 : 1;
                var w = new FindingList();
                string error = null;
                try
                {
                    if (ext == SaveFormats.CharaExtension) { var r = CharaContainer.Load(f); w = r.Findings; if (!r.Ok) error = r.Error.Message; }
                    else if (ext == SaveFormats.CoordExtension) { var r = CoordContainer.Load(f); w = r.Findings; if (!r.Ok) error = r.Error.Message; }
                    else if (ext == SaveFormats.SceneExtension) { var r = SceneContainer.Load(f); w = r.Findings; if (!r.Ok) error = r.Error.Message; }
                    else { var r = PoseContainer.Load(f); w = r.Findings; if (!r.Ok) error = r.Error.Message; }
                }
                catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
                var noisy = w.Where(x => x.Severity != FindingSeverity.Info).ToList();
                if (error != null || noisy.Count > 0)
                    bad.Add(f.Substring(root.Length).TrimStart('\\', '/') + ": " + (error ?? string.Join("; ", noisy.Take(3).Select(x => x.ToString()))));
            }
        foreach (var kv in counts) Info($"{kv.Key}: {kv.Value}");
        foreach (var b in bad) Info("FAILED " + b);
        Check($"every .hs2n* file ({total}) loads with no error and no warning", total > 0 && bad.Count == 0, $"{bad.Count} failed");
        Finish("maker_file_userfiles_report.txt", "MakerFileTest.userfiles (Phase 4.6, the frozen reader)", sw);
    }

    // ================================================================ RunCoordApply (headless, -Full row CoordApply.all)

    public static void RunCoordApply()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        int limit = int.Parse(Arg("-limit", "0"), CultureInfo.InvariantCulture);
        string onlySex = Arg("-sex");
        var only = Arg("-files")?.Split(';').Select(g => g.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0).ToList()).Where(g => g.Count > 0).ToList();
        Rep.AppendLine($"CoordApply.all (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}){(limit > 0 ? ", -limit " + limit + " per sex" : "")}" +
                       (only != null ? ", -files " + string.Join(" ; ", only.Select(g => string.Join(" | ", g))) : "") + (onlySex != null ? ", -sex " + onlySex : ""));
        Directory.CreateDirectory(Dir);
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        try
        {
            using (var h = MakerCoreTest.Harness.Open())
            {
                foreach (var (sex, card, label) in new[] { ("female", FemaleCard, "BR-Chan"), ("male", MaleCard, "HERO107") })
                    if (onlySex == null || onlySex == sex)
                        Guard("CoordApply " + sex, () => CoordApplySex(h, sex, card, label, limit, only));
            }
        }
        finally { FaceSetup.Seed = fseed; }
        Finish("maker_file_coordapply_report.txt", "CoordApply.all: every installed coordinate worn in mode All = a fresh build; saved and reloaded (Phase 4.6, VU7)", sw);
    }

    /// <summary>The lines of a digest text, each prefixed with its renderer and material slot (the "renderer" and " slot" lines
    /// of the materials and objects texts), so a differing property names where it is.</summary>
    static List<string> InContext(string text)
    {
        var l = new List<string>();
        string renderer = "", slot = "";
        foreach (var raw in text.Split('\n'))
        {
            string x = raw.TrimEnd('\r');
            if (x.Length == 0) continue;
            if (x.StartsWith("renderer ", StringComparison.Ordinal)) { renderer = x; slot = ""; l.Add(x); continue; }
            if (x.StartsWith(" slot ", StringComparison.Ordinal)) { slot = x.Trim(); continue; }
            l.Add((renderer.Length > 0 ? renderer + " | " : "") + (slot.Length > 0 ? slot + " | " : "") + x.Trim());
        }
        return l;
    }

    /// <summary>The lines only one of two digest texts holds (as multisets, each line with its renderer and slot), at most
    /// <paramref name="max"/> of each side.</summary>
    static string DigestDiff(string live, string fresh, int max = 12)
    {
        var a = InContext(live);
        var b = InContext(fresh);
        var inB = b.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var onlyA = new List<string>();
        foreach (var x in a)
        {
            if (inB.TryGetValue(x, out int c) && c > 0) inB[x] = c - 1;
            else onlyA.Add(x);
        }
        var onlyB = inB.Where(kv => kv.Value > 0).SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value)).ToList();
        var sb = new StringBuilder();
        sb.Append($"{onlyA.Count} line(s) only live, {onlyB.Count} only fresh");
        foreach (var x in onlyA.Take(max)) sb.Append("\n        live : ").Append(Short(x, 400));
        foreach (var x in onlyB.Take(max)) sb.Append("\n        fresh: ").Append(Short(x, 400));
        return sb.ToString();
    }

    /// <summary>The coordinates of one sex's folder worn in mode All on <paramref name="card"/> (CoordApply.all; AllCardsTest's
    /// coordinate step): <paramref name="every"/> k > 1 takes every k-th file of the folder's order (the impact runs' sample).</summary>
    internal static void CoordApplySex(MakerCoreTest.Harness h, string sex, string card, string label, int limit, List<List<string>> only = null, int every = 1)
    {
        string dir = CoordDirOf(sex);
        var files = Directory.Exists(dir) ? Walk(dir).Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.Ordinal).ToList() : new List<string>();
        var groupStarts = new HashSet<int>();   // indices that start on a fresh node (the -files groups)
        if (only != null)
        {
            files = new List<string>();
            foreach (var g in only)
            {
                groupStarts.Add(files.Count);
                files.AddRange(g.Select(x => Path.Combine(dir, x.Replace('/', Path.DirectorySeparatorChar))).Where(File.Exists));
            }
        }
        if (every > 1) files = files.Where((f, i) => i % every == 0).ToList();
        if (limit > 0) files = files.Take(limit).ToList();
        int applied = 0, notCoord = 0, otherSex = 0, failed = 0;
        var failures = new List<string>();
        var t = Stopwatch.StartNew();
        string tmp = Path.Combine(Dir, "coordapply" + SaveFormats.CoordExtension);
        MakerEditTest.Node n = null;
        try
        {
            for (int k = 0; k < files.Count; k++)
            {
                if (k > 0 && (only == null ? k % 10 == 0 : groupStarts.Contains(k)))   // every 10 files (or each -files group): the live node rebuilt after the bundles are released
                {
                    n?.Dispose(); n = null;
                    h.ReleaseBundles();
                    Info($"{sex}: {k} files, {t.Elapsed.TotalSeconds:F0} s, open bundles after the release {h.Catalog.OpenBundleCount}");
                }
                var f = files[k];
                if (n == null) n = NodeOf(h, card);
                string rel = f.Substring(dir.Length).TrimStart('\\', '/');
                try
                {
                    var found = new FindingList();
                    var src = n.Ops.LoadCoordinate(f, found, out bool imported, out var fail, n.Real?.Sex);
                    if (src == null) { notCoord++; continue; }
                    if ((src.Coord.Sex == 0 ? "male" : "female") != sex) { otherSex++; continue; }
                    var r = n.Ops.ApplyCoordinate(new[] { n.Id }, src, CoordinateApplyMode.All, User);
                    n.Apply();
                    if (!r.Ok) { failed++; failures.Add(rel + ": apply " + r.Message); continue; }
                    applied++;
                    var live = n.Real.Context;
                    string why = null;
                    using (var fresh = MakerEditTest.NewNode(h, n.Session.Model.File.ExtractCharacter(n.Id), label + " (fresh)"))
                    {
                        if (!fresh.Ok) why = "fresh build: " + fresh.Error;
                        else
                        {
                            var fc = fresh.Real.Context;
                            string lo = FaceDigest.ObjectsText(live, "all"), fo = FaceDigest.ObjectsText(fc, "all");
                            string lm = FaceDigest.MaterialsText(live, "all"), fm = FaceDigest.MaterialsText(fc, "all");
                            bool detail = failed < 5;
                            if (fo != lo) why = "objects digest differs" + (detail ? ": " + DigestDiff(lo, fo) : "");
                            else if (fm != lm) why = "materials digest differs" + (detail ? ": " + DigestDiff(lm, fm) : "");
                        }
                    }
                    if (why == null)
                    {
                        var ex = n.Session.Model.File.ExtractCharacter(n.Id).ExtractCoordinate();
                        var sr = ex.Save(tmp, new SaveOptions { Thumbnail = ThumbnailMode.Placeholder, Clock = FixedClock, AppVersion = AppSaveOptions.Version });
                        if (!sr.Ok) why = "save: " + sr.Error.Message;
                        else
                        {
                            var lr = CoordContainer.Load(tmp);
                            if (!lr.Ok) why = "reload: " + lr.Error.Message;
                            else if (!CoordBytes(lr.Value.Coord).SequenceEqual(CoordBytes(n.Stored.Coord))) why = "reloaded coord.json differs from the worn document";
                        }
                    }
                    if (why != null) { failed++; failures.Add(rel + ": " + why); }
                }
                catch (Exception e) { failed++; failures.Add(rel + ": " + e.GetType().Name + ": " + e.Message); }
            }
        }
        finally { n?.Dispose(); h.ReleaseBundles(); }
        foreach (var x in failures) Info("FAILED " + x);
        Info($"{sex}: {files.Count} files: applied {applied}, not coordinate cards {notCoord}, other sex {otherSex}, failed {failed}; {t.Elapsed.TotalMinutes:F1} min");
        Check($"CoordApply {sex} onto {label}: every coordinate worn = a fresh build and saves / reloads equal", applied > 0 && failed == 0, $"{applied} applied, {failed} failed");
    }
}
