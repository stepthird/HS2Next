// SceneRuntimeTest, Phase 2.3 cases (package C): characters added from HS2 cards are
// imported at once and built from documents. Since Phase 4.6 an importer is always available
// in a Unity runtime, a failed import fails the add, a card-backed node (model API only) is built through its import, and
// the hs2next.cardref block is retired.
//   R7b  importer registered: two cards added hold Stored (no card), save writes two subtrees with provenance and no
//        hs2next.cardref, reload builds both by SubtreeCharaBuilder, save → load → save byte identity after 30 ticks
//   R10  import at add: accessory ids = the card formula, hiding one id hides exactly its slot, undo / redo of the add,
//        duplicate clones Stored (bytes equal, ids kept), import warnings in the scene findings with the node id,
//        AddCharaFromContainer ≤ 20 ms (fails at 40 ms)
//   R11  two card-backed nodes added through the model API: built by CardCharaBuilder through the import (no card
//        resolver constructed); the save imports them (subtrees, provenance, no hs2next.cardref, no chara.notImported)
//        without a rebuild; reload builds from documents
//   R12  SceneOps.AddCharaFromFile(.hs2nchara) → Built; a res image without bytes → build.resMissing, build continues
//   R13  an importer that throws → the add fails (bad_args), nothing is added, no history entry, chara.importFailed in
//        the caller's findings
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HS2Next.Chara.Documents;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;

public static partial class SceneRuntimeTest
{
    /// <summary>The retired card-reference namespace (Phase 4.6): files written now hold no such block.</summary>
    const string CardRefNs = "hs2next.cardref";

    static string[] TargetCards()
    {
        var t = TestTargets.Load();
        return new[] { TestTargets.Abs(t.visual.female), TestTargets.Abs(t.visual.male) };
    }

    static int ContainerCharas(byte[] scene, out int cardRefs, out bool provenance)
    {
        var raw = ContainerReader.Read(scene);
        var probe = SceneContainer.Load(scene);
        cardRefs = probe.Ok ? probe.Value.Scene.Nodes.Count(n => n.Ext != null && n.Ext.Contains(CardRefNs)) : -1;
        provenance = probe.Ok && probe.Value.Charas.Count > 0 && probe.Value.Charas.Values.All(c => c.Import != null && c.Import.Sha256.Length == 64);
        return probe.Ok ? probe.Value.Charas.Count : -1;
    }

    // ================================================================ R7b

    static void R7b(RuntimeServices services)
    {
        Section("R7b", "import at add, save → load (importer registered)");
        var cards = TargetCards();
        var s = NewSession(services, 0x7b7bUL);
        var rt = new SceneRuntime(s, services, null, false).AddStandardParts();
        try
        {
            using (ImportSetup.Register(services))
            {
                var a = rt.Ops.AddCharaFromCard(cards[0]);
                var b = rt.Ops.AddCharaFromCard(cards[1]);
                Check("both cards added", a.Ok && b.Ok, a + " / " + b);
                if (!a.Ok || !b.Ok) return;
                s.Set(b.NewId, "transform.pos", new Vec3(10f, 0f, 0f));
                s.SetAnimation(a.NewId, Fixture.Idle);
                rt.Apply();
                var na = s.Model.Find(a.NewId); var nb = s.Model.Find(b.NewId);
                Check("added characters hold Stored, no card", na.Appearance.Stored != null && na.Appearance.Card == null && nb.Appearance.Stored != null && nb.Appearance.Card == null);
                var ca = rt.FindChara(a.NewId); var cb = rt.FindChara(b.NewId);
                Check("both Built by SubtreeCharaBuilder", ca?.State == RealizationState.Built && cb?.State == RealizationState.Built && ca.Builder == SubtreeCharaBuilder.Instance && cb.Builder == SubtreeCharaBuilder.Instance,
                      ca + " / " + cb);
                bool cached = services.Cards.Entries.Any(e => e.Nodes.Contains(a.NewId) || e.Nodes.Contains(b.NewId));
                Check("imported nodes reference no card cache entry", !cached, services.Cards.Count + " entries in the shared cache");
                Info($"builds: {ca?.BuildMilliseconds:F0} ms / {cb?.BuildMilliseconds:F0} ms; findings: " + string.Join("; ", s.Findings.Where(f => f.NodeId == a.NewId || f.NodeId == b.NewId).Select(f => f.ToString()).Take(12)));
                int n0 = s.Findings.CountOf("chara.notImported");
                var s1 = rt.Ops.ToBytes(FixedSave());
                int charas = ContainerCharas(s1, out int refs, out bool prov);
                Check("S1: two charas/<id>/ subtrees with provenance, no hs2next.cardref, no chara.notImported",
                      charas == 2 && refs == 0 && prov && s.Findings.CountOf("chara.notImported") == n0, $"subtrees {charas}, cardrefs {refs}, provenance {prov}");
                using (var x = new LoadedScene(services, s1))
                {
                    Check("load S1 in a new session", x.Load.Ok, x.Load.ToString());
                    if (!x.Load.Ok) return;
                    var cs = x.Runtime.Charas().ToList();
                    Check("both Built from documents after load", cs.Count == 2 && cs.All(c => c.State == RealizationState.Built && c.Builder == SubtreeCharaBuilder.Instance && c.Stored != null),
                          string.Join("; ", cs.Select(c => c.ToString())));
                    var again = x.Runtime.Ops.ToBytes(FixedSave());
                    Check("ToBytes after load = S1", Same(s1, again), Diff(s1, again));
                    for (int i = 0; i < 30; i++) x.Runtime.Tick(1f / 30f);
                    var s2 = x.Runtime.Ops.ToBytes(FixedSave());
                    using (var y = new LoadedScene(services, s2))
                    {
                        var s3 = y.Load.Ok ? y.Runtime.Ops.ToBytes(FixedSave()) : null;
                        Check("30 ticks, S2, reload, S3 = S2", y.Load.Ok && Same(s2, s3), Diff(s2, s3));
                    }
                }
            }
        }
        finally { rt.Dispose(); }
    }

    // ================================================================ R10

    sealed class WarningImporter : ICharaImporter
    {
        readonly ICharaImporter inner;
        public WarningImporter(ICharaImporter inner) { this.inner = inner; }
        public CharaContainer Import(CardAppearanceSource source, FindingList findings)
        {
            var c = inner.Import(source, findings);
            findings.Warning("test.importWarning", "card.png: test", "synthetic warning of the import");
            findings.Info("test.importInfo", "card.png: test", "synthetic info of the import");
            return c;
        }
    }

    static void R10(RuntimeServices services)
    {
        Section("R10", "import at add: accessory ids, visibility, undo / redo, duplicate, findings");
        var cards = TargetCards();
        var s = NewSession(services, 0x1010UL);
        var rt = new SceneRuntime(s, services, null, false).AddStandardParts();
        var prev = CharaImport.Importer;
        try
        {
            CharaImport.Importer = new WarningImporter(ImportSetup.ImporterFor(services.Catalog));
            var importFindings = new FindingList();
            int undo0 = s.Commands.UndoList.Count;
            var r = rt.Ops.AddCharaFromCard(cards[0], importFindings: importFindings);
            Check("add BR-Chan with the importer", r.Ok, r.ToString());
            if (!r.Ok) return;
            string id = r.NewId;
            var c = rt.FindChara(id);
            Check("one history entry", s.Commands.UndoList.Count == undo0 + 1, (s.Commands.UndoList.Count - undo0) + " entries");
            Check("import warnings in the scene findings with the node id, info only in the caller's report",
                  s.Findings.Has("test.importWarning", id) && !s.Findings.Has("test.importInfo", id) && importFindings.Has("test.importWarning") && importFindings.Has("test.importInfo"));
            string sha = ResStore.Sha256Hex(File.ReadAllBytes(cards[0]));
            var slots = c?.Context?.Accessories?.Where(a => a != null && !a.IsEmpty && a.Object).ToList() ?? new List<HS2Next.Chara.AccessorySlot>();
            bool idsOk = slots.Count > 0 && slots.All(a => c.AccessoryIdOf(a) == CardAppearanceSource.AccessoryId(sha, a.Slot));
            Check("accessory ids equal the card formula (slots = the card's)", idsOk, $"{slots.Count} slots: " + string.Join(", ", slots.Take(4).Select(a => a.Slot + "→" + c.AccessoryIdOf(a))));
            var stored = s.Model.Find(id).Appearance.Stored;
            Check("the imported ids are the coordinate's", slots.All(a => stored.Coord.Accessories.Any(e => e.Id == c.AccessoryIdOf(a) && e.ImportSlot == a.Slot)));
            if (slots.Count > 0)
            {
                var hide = slots[0];
                string hid = c.AccessoryIdOf(hide);
                s.Set(id, "chara.state.accessories[" + FieldPath.Quote(hid) + "]", false);
                rt.Apply();
                Check("hiding one id hides exactly its ca_slotNN", !hide.Object.activeSelf && slots.Skip(1).All(a => a.Object.activeSelf), hide.Object.name);
                s.Commands.Undo();
                rt.Apply();
                Check("undo shows it again", hide.Object.activeSelf);
            }
            // undo / redo of the add
            Check("undo the add", s.Commands.Undo().Ok && s.Model.Find(id) == null);
            rt.Apply();
            var re = s.Commands.Redo();
            rt.Apply();
            Check("redo the add: the node returns with the same subtree, Built", re.Ok && s.Model.Find(id)?.Appearance?.Stored == stored && rt.FindChara(id)?.State == RealizationState.Built);
            // duplicate
            var d = rt.Ops.Duplicate(new[] { id });
            Check("duplicate", d.Ok, d.ToString());
            if (d.Ok)
            {
                var copy = s.Model.Find(d.NewId);
                var cs = copy.Appearance.Stored;
                bool clone = cs != null && !ReferenceEquals(cs, stored)
                    && CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, cs.Chara.Write).SequenceEqual(CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, stored.Chara.Write))
                    && CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, cs.Coord.Write).SequenceEqual(CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, stored.Coord.Write));
                Check("duplicate clones Stored (bytes equal, accessory ids kept)", clone);
                var cc = rt.FindChara(d.NewId);
                Check("the duplicate is Built with the same accessory ids", cc?.State == RealizationState.Built
                      && cc.Context.Accessories.Where(a => a != null && !a.IsEmpty && a.Object).Select(a => cc.AccessoryIdOf(a)).SequenceEqual(slots.Select(a => c.AccessoryIdOf(a))));
            }
            // AddCharaFromContainer timing (≤ 20 ms for a 150 KB character)
            var imported = ImportSetup.ImporterFor(services.Catalog).Import(new CardAppearanceSource { FullPath = cards[0], Sha256 = sha, ReadBytes = () => File.ReadAllBytes(cards[0]) }, new FindingList());
            var times = new List<double>();
            long size = 0;
            for (int i = 0; i < 5; i++)
            {
                var sw = Stopwatch.StartNew();
                var ar = s.AddCharaFromContainer(imported);
                times.Add(sw.Elapsed.TotalMilliseconds);
                if (i == 0 && ar.Ok) size = CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, imported.Chara.Write).Length + CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, imported.Coord.Write).Length;
            }
            Check("AddCharaFromContainer ≤ 20 ms (fails at 40 ms)", Median(times) <= 40, $"median {Median(times):F1} ms for {size / 1024} KiB of documents (+{imported.Res.TotalBytes / 1024} KiB res)");
        }
        finally
        {
            CharaImport.Importer = prev;
            rt.Dispose();
        }
    }

    // ================================================================ R11

    static void R11(RuntimeServices services)
    {
        Section("R11", "card-backed nodes (model API): built through the import, imported at save");
        var cards = TargetCards();
        var s = NewSession(services, 0x1111UL);
        var rt = new SceneRuntime(s, services, null, false).AddStandardParts();
        try
        {
            var a = s.AddChara(new CharaAppearance { Card = services.CardSources.FromFile(cards[0]) });
            var b = s.AddChara(new CharaAppearance { Card = services.CardSources.FromFile(cards[1]) });
            rt.Apply();
            Check("two card-backed nodes added through the model API", a.Ok && b.Ok, a + " / " + b);
            if (!a.Ok || !b.Ok) return;
            var cs = rt.Charas().ToList();
            Check("both Built by CardCharaBuilder through the import",
                  cs.Count == 2 && cs.All(c => c.State == RealizationState.Built && c.Card != null && c.Node.Appearance.Stored == null && c.Builder == CardCharaBuilder.Instance),
                  string.Join("; ", cs.Select(c => c.ToString())));
            int n0 = s.Findings.CountOf("chara.notImported");
            byte[] bytes;
            using (ImportSetup.Register(services))   // the session imports at save with the registered importer (or its own option)
                bytes = rt.Ops.ToBytes(FixedSave());
            int charas = ContainerCharas(bytes, out int refs, out bool prov);
            Check("the save imports them: two subtrees with provenance, no hs2next.cardref, no chara.notImported", charas == 2 && refs == 0 && prov && s.Findings.CountOf("chara.notImported") == n0,
                  $"subtrees {charas}, cardrefs {refs}");
            Check("the realizations were not rebuilt by the import at save", rt.Charas().All(c => c.Builder == CardCharaBuilder.Instance && c.Node.Appearance.Stored != null));
            using (var y = new LoadedScene(services, bytes, 0x1112UL))
            {
                var ys = y.Runtime.Charas().ToList();
                Check("reload builds from documents", y.Load.Ok && ys.Count == 2 && ys.All(c => c.State == RealizationState.Built && c.Builder == SubtreeCharaBuilder.Instance),
                      string.Join("; ", ys.Select(c => c.ToString())));
            }
        }
        finally { rt.Dispose(); }
    }

    // ================================================================ R12

    static void R12(RuntimeServices services)
    {
        Section("R12", "AddCharaFromFile (.hs2nchara)");
        var cards = TargetCards();
        string dir = Path.Combine(Path.GetTempPath(), "hs2next_r12");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "female.hs2nchara"), broken = Path.Combine(dir, "female_missing_res.hs2nchara");
        var importer = ImportSetup.ImporterFor(services.Catalog);
        var fr = CharaFileImport.ImportToFile(importer, cards[0], file, services.Catalog, null);
        Check("import to a character file (placeholder thumbnail headless)", fr.Ok && fr.Thumbnail == null, fr.ToString());
        if (!fr.Ok) return;
        // the same file with a skin overlay whose res name has no bytes
        var c = CharaContainer.Load(file).Value;
        c.Chara.Body.SkinOverlays.Add(new SkinOverlay { Target = "FaceOver", Image = "res/" + new string('0', 64) + ".png" });
        var sr = c.Save(broken, new SaveOptions { Thumbnail = ThumbnailMode.Placeholder });
        Check("save the file with a dangling res reference", sr.Ok, sr.ToString());
        var s = NewSession(services, 0x1212UL);
        var rt = new SceneRuntime(s, services, null, false).AddStandardParts();
        try
        {
            var a = rt.Ops.AddCharaFromFile(file);
            var ca = a.Ok ? rt.FindChara(a.NewId) : null;
            Check("AddCharaFromFile → Built from documents", ca?.State == RealizationState.Built && ca.Builder == SubtreeCharaBuilder.Instance, a + " " + ca);
            var b = rt.Ops.AddCharaFromFile(broken);
            var cb = b.Ok ? rt.FindChara(b.NewId) : null;
            Check("a res image without bytes → build.resMissing, the build continues", cb?.State == RealizationState.Built && s.Findings.Has("build.resMissing", b.NewId), b + " " + cb);
            var bad = rt.Ops.AddCharaFromFile(Path.Combine(dir, "does_not_exist.hs2nchara"));
            Check("an unreadable file → bad_args", bad.Status == CommandStatus.Error && bad.Code == CommandCodes.BadArgs, bad.ToString());
        }
        finally { rt.Dispose(); }
    }

    // ================================================================ R13

    sealed class ThrowingImporter : ICharaImporter
    {
        public CharaContainer Import(CardAppearanceSource source, FindingList findings) => throw new InvalidDataException("synthetic import failure");
    }

    static void R13(RuntimeServices services)
    {
        Section("R13", "an importer that throws");
        var cards = TargetCards();
        var s = NewSession(services, 0x1313UL);
        var rt = new SceneRuntime(s, services, null, false).AddStandardParts();
        var prev = CharaImport.Importer;
        try
        {
            CharaImport.Importer = new ThrowingImporter();
            int count0 = s.Model.Count, undo0 = s.Commands.UndoList.Count;
            var found = new FindingList();
            var r = rt.Ops.AddCharaFromCard(cards[0], importFindings: found);
            Check("the add fails with bad_args, nothing added, no history entry",
                  r.Status == CommandStatus.Error && r.Code == CommandCodes.BadArgs && s.Model.Count == count0 && s.Commands.UndoList.Count == undo0 && !rt.Charas().Any(), r.ToString());
            Check("chara.importFailed in the caller's findings, naming the importer's message", found.Has("chara.importFailed") && found.Any(x => x.Code == "chara.importFailed" && x.Message.Contains("synthetic import failure")),
                  string.Join("; ", found.Select(x => x.ToString())));
        }
        finally
        {
            CharaImport.Importer = prev;
            rt.Dispose();
        }
    }
}
