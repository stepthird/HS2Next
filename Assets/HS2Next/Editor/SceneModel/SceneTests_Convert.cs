// M5: byte identity; node ext, chara/coord ext, unknown
// ext blocks, foreign entries and manifest provenance surviving the model; orphan subtrees dropped; accessory
// fill and prune (five cases); card-backed characters at save (Phase 4.6: imported by CharaImport.Importer or the
// session's importer, else saved without a subtree with the error chara.notImported; the retired `hs2next.cardref` block of
// an old file is unknown and preserved, T-F6); active cameras normalised at bind.
// M5+: SceneSession.AddCharaFromContainer (resources, Stored, one history entry,
// undo/redo, save with subtree and provenance, failed add) and the hs2.pose block (SetNodeExtCommand +
// PoseProvenanceExt: set / replace / remove with undo/redo, core namespaces only, known after save → load, duplicate,
// delete → undo, inside a transaction). 2.4: the hs2.pose block on every save path (a session's container
// saved directly before and after a session save or load, a direct load and save, replace / remove / undo seen by a
// direct save, a newer version and a malformed value declined and preserved, the golden scene unchanged).
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Api.Save;
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_Convert
    {
        public static void Run(TestRunner t)
        {
            Row1(t);
            Row2(t);
            Preservation(t);
            Orphans(t);
            Accessories(t);
            CardRef(t);
            Importer(t);
            Cameras(t);
            AddFromContainer(t);
            PoseBlock(t);
            PoseBlockSavePaths(t);
        }

        static JObject SceneJson(byte[] container)
        {
            var raw = ContainerReader.Read(container);
            return DocReader.Parse(raw.Get(SceneDocument.EntryName).Data, SceneDocument.EntryName);
        }

        static JObject NodeJson(JObject scene, string id)
        {
            foreach (JObject n in (JArray)scene["nodes"]) if ((string)n["id"] == id) return n;
            return null;
        }

        static void Row1(TestRunner t)
        {
            var golden = Kit.Golden(t, "scene.json");
            var s = Kit.Session();
            s.Bind(new SceneContainer { Scene = Kit.ReadScene(golden) });
            var back = CanonicalJsonWriter.ToBytes(SceneDocument.EntryName, s.ExportDocument().Write);
            t.Check(Kit.Same(golden, back), "golden scene.json → Bind → ExportDocument → identical bytes " + Kit.Diff(golden, back));
            t.Check(!s.Commands.IsDirty, "bind of the golden changes nothing (not dirty)");
            t.Check(s.Model.Find(Kit.GoldenChara).Appearance.IsMissing, "document-only: the character's appearance is missing");
        }

        static void Row2(TestRunner t)
        {
            var reg = new ExtRegistry();
            var s = Kit.Session();
            s.Bind(Kit.GoldenContainer(t, true));
            t.Check(!s.Commands.IsDirty, "golden container with its subtree: consistent, nothing normalised");
            var b1 = s.ToBytes(Kit.FixedSave(ThumbnailMode.Keep, reg));
            var s2 = Kit.Session(2);
            var lr = s2.Load(b1, new LoadOptions { Registry = reg });
            t.Check(lr.Ok, "load the saved golden container (" + lr + ")");
            var b2 = s2.ToBytes(Kit.FixedSave(ThumbnailMode.Keep, reg));
            t.Check(Kit.Same(b1, b2), "save → load → Bind → save identical bytes (" + b1.Length + " B)");
            t.Check(s2.Model.Find(Kit.GoldenChara).Appearance.Stored != null, "the subtree is the character's stored appearance");
            var path = Path.Combine(t.TempDir, "golden.hs2nscene");
            var sr = s2.Save(path, Kit.FixedSave(ThumbnailMode.Keep, reg));
            t.Check(sr.Ok && Kit.Same(b1, File.ReadAllBytes(path)), "Save writes the same bytes as ToBytes");
        }

        static void Preservation(TestRunner t)
        {
            var reg = new ExtRegistry();
            var c = Kit.GoldenContainer(t, true);
            var nodeExt = new ExtScopeData(ExtScope.Node);
            nodeExt.GetOrAdd("example.files").Files["blob.bin"] = new byte[] { 7, 8, 9 };
            c.NodeExt[Kit.GoldenItem] = nodeExt;
            var sub = c.Charas[Kit.GoldenChara];
            sub.CharaExt.GetOrAdd("example.chara").Files["c.bin"] = new byte[] { 1 };
            sub.CoordExt.GetOrAdd("example.coord").Files["k.bin"] = new byte[] { 2 };
            sub.Import = new ImportProvenance { From = "hs2.card", FileName = "synthetic.png", Sha256 = new string('a', 64), FileSize = 10, Importer = "test", Hs2Version = "test" };
            c.ForeignEntries["foreign/readme.txt"] = DocIo.Utf8("kept");
            var b0 = c.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder, reg));
            var s = Kit.Session();
            var lr = s.Load(b0, new LoadOptions { Registry = reg });
            t.Check(lr.Ok && lr.Findings.Has("ext.unknown"), "load with unknown ext blocks (" + lr + ")");
            s.Set(Kit.GoldenChara, "chara.face.eyes", 3);
            s.Commands.Undo();
            var b1 = s.ToBytes(Kit.FixedSave(ThumbnailMode.Keep, reg));
            t.Check(Kit.Same(b0, b1), "node ext, chara/coord ext files, unknown blocks, foreign entries and provenance survive a load/edit/undo/save");
            var raw = ContainerReader.Read(b1);
            t.Check(raw.Get("nodes/" + Kit.GoldenItem + "/ext/example.files/blob.bin") != null, "node ext file written");
            t.Check(raw.Get("charas/" + Kit.GoldenChara + "/ext/example.chara/c.bin") != null && raw.Get("charas/" + Kit.GoldenChara + "/coord/ext/example.coord/k.bin") != null, "chara/coord ext files written");
            t.Check(raw.Get("foreign/readme.txt") != null, "foreign entry kept");
            var c2 = SceneContainer.Load(b1, new LoadOptions { Registry = reg }).Value;
            t.Check(c2?.Charas[Kit.GoldenChara].Import?.FileName == "synthetic.png", "manifest charas provenance kept");
            t.Check(c2.Scene.Find(Kit.GoldenChara).Ext.Contains("example.plugin") && c2.Scene.Ext.Contains("example.scene.plugin"), "inline node and scene ext kept");
        }

        static void Orphans(TestRunner t)
        {
            var reg = new ExtRegistry();
            var c = Kit.GoldenContainer(t, true);
            string orphan = "f0000000000000000000000000000001";
            c.Charas[orphan] = c.Charas[Kit.GoldenChara].Clone();
            c.NodeExt[orphan] = new ExtScopeData(ExtScope.Node);
            c.NodeExt[orphan].GetOrAdd("example.files").Files["x.bin"] = new byte[] { 1 };
            var b0 = c.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder, reg));
            var s = Kit.Session();
            var lr = s.Load(b0, new LoadOptions { Registry = reg });
            t.Check(lr.Ok, "load with an orphan subtree");
            var b1 = s.ToBytes(Kit.FixedSave(ThumbnailMode.Keep, reg));
            var raw = ContainerReader.Read(b1);
            bool any = false;
            foreach (var e in raw.Entries) if (e.Name.Contains(orphan)) any = true;
            t.Check(!any, "orphan subtree and node ext dropped on save (as in 2.1)");
        }

        static void Accessories(TestRunner t)
        {
            // 1 unknown coordinate: map untouched
            var s = Kit.GoldenSession(t);
            var map = ((CharaNodeState)s.Model.Find(Kit.GoldenChara).State).State.Accessories;
            t.Check(map.Count == 2 && !s.Commands.IsDirty, "unknown coordinate: map as stored");
            t.Check(!s.NormalizeAccessories(Kit.GoldenChara), "unknown coordinate: nothing to normalise");
            // 2 missing ids added true
            var s2 = Kit.Session(2);
            var card = Kit.Card("acc", "UserData/chara/female/acc.png", 3);
            var id = s2.AddChara(new CharaAppearance { Card = card }).NewId;
            var m2 = ((CharaNodeState)s2.Model.Find(id).State).State.Accessories;
            t.Check(m2.Count == 3 && m2[card.AccessoryIds[0]] && m2[card.AccessoryIds[2]], "missing ids added as visible");
            // 3 foreign ids removed, 4 values kept
            var c = Kit.GoldenContainer(t, true);
            var gm = ((CharaNodeState)c.Scene.Find(Kit.GoldenChara).State).State.Accessories;
            gm["ffffffffffffffffffffffffffffffff"] = true;
            var s3 = Kit.Session(3);
            var bf = s3.Bind(c);
            var m3 = ((CharaNodeState)s3.Model.Find(Kit.GoldenChara).State).State.Accessories;
            t.Check(m3.Count == 2 && !m3.ContainsKey("ffffffffffffffffffffffffffffffff"), "a key outside the coordinate is removed");
            t.Check(!m3["3f2a6b1c9d8e4f7a0b1c2d3e4f5a6b7c"] && m3[Kit.GoldenItem], "values of present ids kept");
            t.Check(bf.Has("chara.accessoriesNormalized") && s3.Commands.IsDirty, "normalisation reported; the model differs from the file");
            // 5 appearance change and its undo
            var s4 = Kit.GoldenSession(t, withSubtree: true, seed: 4);
            var before = new SortedDictionary<string, bool>(((CharaNodeState)s4.Model.Find(Kit.GoldenChara).State).State.Accessories, StringComparer.Ordinal);
            var other = Kit.Card("other", "UserData/chara/female/other.png", 1);
            s4.SetAppearance(Kit.GoldenChara, new CharaAppearance { Card = other });
            var m4 = ((CharaNodeState)s4.Model.Find(Kit.GoldenChara).State).State.Accessories;
            t.Check(m4.Count == 1 && m4.ContainsKey(other.AccessoryIds[0]), "an appearance change normalises the map");
            s4.Commands.Undo();
            var m5 = ((CharaNodeState)s4.Model.Find(Kit.GoldenChara).State).State.Accessories;
            bool same = m5.Count == before.Count;
            foreach (var kv in before) same &= m5.TryGetValue(kv.Key, out bool v) && v == kv.Value;
            t.Check(same, "undo of the appearance change restores the old map");
            // guard at save: a set that broke the map is normalised in the file
            s2.Set(id, "chara.state.accessories[\"ffffffffffffffffffffffffffffffff\"]", false);
            var b = s2.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            var accs = (JObject)NodeJson(SceneJson(b), id)["chara"]["state"]["accessories"];
            t.Check(accs.Count == 3 && accs["ffffffffffffffffffffffffffffffff"] == null, "save guard prunes the map");

            // ExportDocument and NodeJson are reads: a normalised copy, no write, no event, no version bump
            var s5 = Kit.GoldenSession(t, withSubtree: true, seed: 5);
            s5.WriteSilently(ChangeOrigin.Capture, w => w.Set(Kit.GoldenChara, "chara.state.accessories[\"ffffffffffffffffffffffffffffffff\"]", true));
            var rec = new EventRecorder(s5);
            long ver = s5.Commands.Version;
            var doc = s5.ExportDocument();
            string nodeJson = s5.NodeJson(Kit.GoldenChara);
            var live = ((CharaNodeState)s5.Model.Find(Kit.GoldenChara).State).State.Accessories;
            var exported = ((CharaNodeState)doc.Find(Kit.GoldenChara).State).State.Accessories;
            t.Check(ver == s5.Commands.Version && rec.Log.Count == 0 && live.Count == 3, "ExportDocument changes nothing (version, events, model)");
            t.Check(exported.Count == 2 && !exported.ContainsKey("ffffffffffffffffffffffffffffffff") && !nodeJson.Contains("ffffffffffffffffffffffffffffffff"),
                "export and NodeJson show the map as a save writes it");
            var exportBytes = CanonicalJsonWriter.ToBytes(SceneDocument.EntryName, doc.Write);
            var saved = s5.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            t.Check(Kit.Same(exportBytes, ContainerReader.Read(saved).Get(SceneDocument.EntryName).Data), "exported bytes = the saved scene.json");
        }

        // The card reference block is retired (Phase 4.6): a card-backed character that cannot be imported at save (no importer,
        // or the import fails) is an error chara.notImported and is saved without a subtree and without a block; it loads as a
        // missing character. The session's own importer (SessionOptions.Importer) serves when CharaImport.Importer is not set.
        // A block of the retired namespace in an old file is an unknown block, kept and written back unchanged (T-F6).
        static void CardRef(TestRunner t)
        {
            var prev = CharaImport.Importer;
            CharaImport.Importer = null;
            try
            {
                var card = Kit.Card("BR test", "UserData/chara/female/test_a.png", 2);
                var s = Kit.Session();
                var id = s.AddChara(new CharaAppearance { Card = card }).NewId;
                var path = Path.Combine(t.TempDir, "cardref.hs2nscene");
                var sr = s.Save(path, Kit.FixedSave(ThumbnailMode.Placeholder));
                Finding nf = null;
                foreach (var f in sr.Findings) if (f.Code == "chara.notImported") nf = f;
                t.Check(sr.Ok && sr.Findings.CountOf("chara.notImported") == 1 && nf.Severity == FindingSeverity.Error,
                    "card node saved without importer: error chara.notImported (" + sr + ")");
                t.Check(s.Findings.Has("chara.notImported", id), "the finding carries the node id");
                var bytes = File.ReadAllBytes(path);
                t.Check(NodeJson(SceneJson(bytes), id)?["ext"] == null, "no card reference block is written");
                t.Check(ContainerReader.Read(bytes).Get("charas/" + id + "/chara.json") == null, "no subtree written");
                var s2 = Kit.Session(2);
                var lr = s2.Load(path);
                var a2 = s2.Model.Find(id)?.Appearance;
                bool missingError = false;
                foreach (var f in lr.Findings) missingError |= f.Code == "scene.charaMissing" && f.Severity == FindingSeverity.Error;
                t.Check(lr.Ok && a2 != null && a2.IsMissing && a2.Card == null && missingError, "reload: the character is missing (scene.charaMissing error)");
                var s3c = Kit.Session(32);
                var lr3c = s3c.Load(DocIo.Utf8("not a container"));
                t.Check(!lr3c.Ok && s3c.Model.Count == 0, "a failed load leaves the session untouched");

                // a card outside the HS2 install: the same error, no separate finding
                var s4 = Kit.Session(4);
                var outside = Kit.Card("outside", null, 1);
                var id4 = s4.AddChara(new CharaAppearance { Card = outside }).NewId;
                var r4 = s4.Save(Path.Combine(t.TempDir, "outside.hs2nscene"), Kit.FixedSave(ThumbnailMode.Placeholder));
                t.Check(r4.Ok && r4.Findings.Has("chara.notImported") && !r4.Findings.Has("chara.appearanceLost"), "card outside HS2: chara.notImported");
                t.Check(NodeJson(SceneJson(File.ReadAllBytes(Path.Combine(t.TempDir, "outside.hs2nscene"))), id4)?["ext"] == null, "no block for a card outside the install");

                // the session's importer when none is registered
                var fake = new FakeImporter(t);
                var s6 = Kit.Session(6, new SessionOptions { Importer = fake });
                t.Check(ReferenceEquals(s6.Importer, fake) && ReferenceEquals(SceneSession.ImporterOf(s6.Model), fake), "SceneSession.Importer / ImporterOf: the session option");
                var id6 = s6.AddChara(new CharaAppearance { Card = card }).NewId;
                var b6 = s6.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
                t.Check(fake.Calls == 1 && ContainerReader.Read(b6).Get("charas/" + id6 + "/chara.json") != null && !s6.Findings.Has("chara.notImported"),
                    "a save imports with SessionOptions.Importer");
            }
            finally { CharaImport.Importer = prev; }
            FixtureCardRef(t);
        }

        // T-F6: the pre-freeze scene fixture repacked so that its character node holds an hs2next.cardref block instead of a
        // subtree (a file of Phase 2.2 to 4.5 that saved a card-backed character): info ext.unknown, the node without
        // appearance and scene.charaMissing; the block and its manifest entry are written back unchanged.
        static void FixtureCardRef(TestRunner t)
        {
            string path = Path.Combine(t.FixturesDir ?? "", "SaveFormat", "fixtures", "v02_dev1", "scene.hs2nscene");
            if (!File.Exists(path)) { t.Check(false, "T-F6: the v02_dev1 scene fixture exists (" + path + ")"); return; }
            const string ns = "hs2next.cardref";
            string id = Kit.GoldenChara;
            var block = DocReader.Parse(DocIo.Utf8("{\"file\": {\"hs2\": \"UserData/chara/female/old_card.png\"}, \"sha256\": \"" + new string('a', 64) +
                "\", \"sex\": 1, \"name\": \"old card\"}"), "block");
            var raw = ContainerReader.Read(File.ReadAllBytes(path));
            var w = new ContainerWriter { Thumbnail = raw.Thumbnail, MainDocument = SceneDocument.EntryName };
            foreach (var e in raw.Entries)
            {
                if (e.Name.StartsWith("charas/" + id + "/", StringComparison.Ordinal)) continue;
                byte[] data = e.Data;
                if (e.Name == SceneDocument.EntryName)
                {
                    var sj = DocReader.Parse(data, e.Name);
                    foreach (var n in (JArray)sj["nodes"])
                        if ((string)n["id"] == id) ((JObject)n["ext"])[ns] = block.DeepClone();
                    data = CanonicalJsonWriter.ToBytes(sj, e.Name);
                }
                else if (e.Name == EntryNames.Manifest)
                {
                    var mj = DocReader.Parse(data, e.Name);
                    var ext = (JArray)mj["ext"];
                    for (int i = ext.Count - 1; i >= 0; i--)
                        if (((string)ext[i]["root"]).StartsWith("charas/", StringComparison.Ordinal)) ext.RemoveAt(i);
                    ext.Insert(0, new JObject { ["ns"] = ns, ["scope"] = "node", ["root"] = "", ["version"] = 1, ["required"] = false });
                    ((JObject)mj["charas"]).RemoveAll();
                    data = CanonicalJsonWriter.ToBytes(mj, e.Name);
                }
                w.Add(e.Name, data);
            }
            var repacked = w.ToBytes();
            var s = Kit.Session(61);
            var lr = s.Load(repacked);
            if (!lr.Ok) { t.Check(false, "T-F6: the repacked fixture loads (" + lr + ")"); return; }
            Finding unknown = null, missing = null;
            foreach (var f in lr.Findings)
            {
                if (f.Code == "ext.unknown" && f.Path != null && f.Path.Contains(ns)) unknown = f;
                if (f.Code == "scene.charaMissing") missing = f;
            }
            t.Check(unknown != null && unknown.Severity == FindingSeverity.Info, "T-F6: the retired block is an unknown block (info ext.unknown)");
            t.Check(missing != null && missing.Severity == FindingSeverity.Error, "T-F6: the node has no subtree: scene.charaMissing (error)");
            var a = s.Model.Find(id)?.Appearance;
            t.Check(a != null && a.IsMissing && a.Card == null, "T-F6: the node has no appearance (no card source from the block)");
            byte[] b1 = s.ToBytes(Kit.FixedSave(ThumbnailMode.Keep));
            var back = NodeJson(SceneJson(b1), id)?["ext"]?[ns];
            t.Check(back != null && JToken.DeepEquals(back, block), "T-F6: the block is written back unchanged");
            var m1 = DocReader.Parse(ContainerReader.Read(b1).Get(EntryNames.Manifest).Data, EntryNames.Manifest);
            bool entry = false;
            foreach (var e in (JArray)m1["ext"])
                entry |= (string)e["ns"] == ns && (string)e["scope"] == "node" && (int)e["version"] == 1 && !(bool)e["required"];
            t.Check(entry, "T-F6: its manifest entry is kept (node scope, version 1, not required)");
            var s2 = Kit.Session(62);
            var lr2 = s2.Load(b1);
            byte[] b2 = lr2.Ok ? s2.ToBytes(Kit.FixedSave(ThumbnailMode.Keep)) : null;
            t.Check(b2 != null && Kit.Same(b1, b2), "T-F6: load → save of the re-saved file is byte-identical");
        }

        sealed class FakeImporter : ICharaImporter
        {
            readonly TestRunner t;
            public int Calls;
            public FakeImporter(TestRunner t) { this.t = t; }

            public CharaContainer Import(CardAppearanceSource source, FindingList findings)
            {
                Calls++;
                var g = Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara];
                g.Import = new ImportProvenance { From = "hs2.card", FileName = Path.GetFileName(source.Hs2Path ?? "x.png"), Sha256 = source.Sha256, FileSize = 1, Importer = "fake", Hs2Version = "test" };
                return new CharaContainer { Subtree = g };
            }
        }

        static void Importer(TestRunner t)
        {
            var prev = CharaImport.Importer;
            var fake = new FakeImporter(t);
            CharaImport.Importer = fake;
            try
            {
                var s = Kit.Session();
                var rec = new EventRecorder(s);
                var card = Kit.Card("imported", "UserData/chara/female/imp.png", 2);
                var id = s.AddChara(new CharaAppearance { Card = card }).NewId;
                rec.Clear();
                var path = Path.Combine(t.TempDir, "imported.hs2nscene");
                var r = s.Save(path, Kit.FixedSave(ThumbnailMode.Placeholder));
                var a = s.Model.Find(id).Appearance;
                t.Check(r.Ok && fake.Calls == 1 && !r.Findings.Has("chara.notImported"), "importer called once, no chara.notImported (" + r + ")");
                t.Check(a.Stored != null && a.Card == card, "the imported subtree is cached as Stored");
                bool capture = false;
                foreach (var b in rec.Batches)
                    foreach (var c in b.Changes) capture |= b.Origin == ChangeOrigin.Capture && c.Kind == SceneChangeKind.AppearanceChanged && c.NodeId == id;
                t.Check(capture && s.Commands.UndoList.Count == 1, "AppearanceChanged with origin Capture, no history entry for it");
                var bytes = File.ReadAllBytes(path);
                var raw = ContainerReader.Read(bytes);
                t.Check(raw.Get("charas/" + id + "/chara.json") != null && NodeJson(SceneJson(bytes), id)?["ext"] == null, "subtree written, no node ext");
                var m = DocReader.Parse(raw.Get(EntryNames.Manifest).Data, EntryNames.Manifest);
                t.Equal("fake", (string)m["charas"]?[id]?["import"]?["importer"], "provenance in manifest.charas");
                var map = ((CharaNodeState)s.Model.Find(id).State).State.Accessories;
                t.Check(map.Count == 2 && map.ContainsKey(Kit.GoldenItem), "accessory map follows the imported coordinate");
                s.Save(path, Kit.FixedSave(ThumbnailMode.Placeholder));
                t.Equal(1, fake.Calls, "a cached subtree is not imported again");
                // an importer that fails: error chara.notImported, saved without a subtree and without a block
                CharaImport.Importer = new ThrowingImporter();
                var s2 = Kit.Session(2);
                var id2 = s2.AddChara(new CharaAppearance { Card = card }).NewId;
                var r2 = s2.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
                bool err = false;
                foreach (var f in s2.Findings) err |= f.Code == "chara.notImported" && f.NodeId == id2 && f.Severity == FindingSeverity.Error;
                t.Check(err && NodeJson(SceneJson(r2), id2)?["ext"] == null && ContainerReader.Read(r2).Get("charas/" + id2 + "/chara.json") == null,
                    "importer failure: error chara.notImported, no subtree, no block");
            }
            finally { CharaImport.Importer = prev; }
        }

        sealed class ThrowingImporter : ICharaImporter
        {
            public CharaContainer Import(CardAppearanceSource source, FindingList findings) => throw new InvalidDataException("synthetic failure");
        }

        static void Cameras(TestRunner t)
        {
            var s = Kit.Session();
            var bf = s.Bind(new SceneContainer { Scene = Kit.ReadScene(Kit.Fixture(t, "active_cameras.json")) });
            bool a1 = ((CameraNodeState)s.Model.Find("a0000000000000000000000000000001").State).Active;
            bool a2 = ((CameraNodeState)s.Model.Find("a0000000000000000000000000000002").State).Active;
            t.Check(!a1 && a2 && bf.Has("scene.multipleActiveCameras") && s.Commands.IsDirty, "several active cameras: the last stays active, reported");
            s.SetActiveCamera("a0000000000000000000000000000003");
            int active = 0;
            foreach (var n in s.Model.PreOrder()) if (((CameraNodeState)n.State).Active) active++;
            t.Equal(1, active, "SetActiveCamera keeps one active");
            s.Set("a0000000000000000000000000000001", "camera.active", true);
            active = 0;
            foreach (var n in s.Model.PreOrder()) if (((CameraNodeState)n.State).Active) active++;
            t.Check(active == 1 && ((CameraNodeState)s.Model.Find("a0000000000000000000000000000001").State).Active, "setting camera.active true is single-active too");
            Kit.Fails(t, s.SetActiveCamera(Kit.GoldenChara), CommandCodes.NotFound, "activate a missing node");
        }
            // ---------------------------------------------------------------- M5+ (2.3)

        /// <summary>A character file with the golden documents, a provenance block and one real image in res/.</summary>
        static CharaContainer GoldenFile(TestRunner t, out string resName)
        {
            var g = Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara];
            var c = new CharaContainer { Subtree = g };
            resName = c.Res.Add(ThumbnailPlaceholder.Png());
            g.Chara.Body.SkinOverlays.Add(new SkinOverlay { Target = "BodyOver", Image = resName });
            g.Import = new ImportProvenance { From = "hs2.chara", FileName = "golden.png", Sha256 = new string('a', 64), FileSize = 1, Importer = "test", Hs2Version = "1.0.0" };
            return c;
        }

        static void AddFromContainer(TestRunner t)
        {
            var s = Kit.Session(31);
            var file = GoldenFile(t, out string res);
            int undo0 = s.Commands.UndoList.Count;
            var r = s.AddCharaFromContainer(file);
            Kit.Ok(t, r, "AddCharaFromContainer");
            var id = r.NewId;
            var n = s.Model.Find(id);
            t.Check(n != null && n.Kind == NodeKind.Chara && n.Appearance?.Stored != null && n.Appearance.Card == null, "the node stores the subtree");
            t.Check(n != null && ReferenceEquals(n.Appearance.Stored, s.Model.File.Charas[id]), "Stored is the scene carrier's subtree (Model.File.Charas[id])");
            t.Check(n != null && !ReferenceEquals(n.Appearance.Stored, file.Subtree), "the subtree is a copy of the file's");
            t.Check(s.Model.File.Res.Contains(res), "the referenced resource is copied into Model.File.Res");
            t.Equal(undo0 + 1, s.Commands.UndoList.Count, "one history entry");
            t.Equal("Add character", s.Commands.UndoList.Count > 0 ? s.Commands.UndoList[s.Commands.UndoList.Count - 1].Label : null, "history label");
            var map = n == null ? null : ((CharaNodeState)n.State).State.Accessories;
            t.Check(map != null && map.Count == 2 && map.ContainsKey(Kit.GoldenItem), "state.accessories normalised from the stored coordinate");
            var stored = n?.Appearance.Stored;
            Kit.Ok(t, s.Commands.Undo(), "undo the add");
            t.Check(s.Model.Find(id) == null && s.Model.File.Res.Contains(res), "undo removes the node; its resources stay for redo");
            Kit.Ok(t, s.Commands.Redo(), "redo the add");
            t.Check(s.Model.Find(id)?.Appearance?.Stored == stored, "redo brings the same subtree back");
            var bytes = s.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            var raw = ContainerReader.Read(bytes);
            t.Check(raw.Get("charas/" + id + "/chara.json") != null && raw.Get("charas/" + id + "/coord/coord.json") != null, "save writes the subtree");
            t.Check(raw.Get(res) != null, "save writes the referenced resource");
            var m = DocReader.Parse(raw.Get(EntryNames.Manifest).Data, EntryNames.Manifest);
            t.Equal("test", (string)m["charas"]?[id]?["import"]?["importer"], "provenance in manifest.charas");
            t.Check(NodeJson(SceneJson(bytes), id)?["ext"]?["hs2next.cardref"] == null, "no hs2next.cardref block");
            var s2 = Kit.Session(32);
            var lr = s2.Load(bytes);
            t.Check(lr.Ok && s2.Model.Find(id)?.Appearance?.Stored != null && s2.Model.File.Res.Get(res) != null, "reload: the node holds the subtree and the resource (" + lr + ")");
            // a failed add leaves no Charas entry
            int before = s.Model.File.Charas.Count;
            var bad = s.AddCharaFromContainer(GoldenFile(t, out _), "ffffffffffffffffffffffffffffffff");
            t.Check(bad.Status == CommandStatus.Error && s.Model.File.Charas.Count == before, "a failed add (missing parent) leaves no Charas entry (" + bad + ")");
            Kit.Fails(t, s.AddCharaFromContainer(null), CommandCodes.BadArgs, "AddCharaFromContainer(null)");
        }

        static PoseProvenance SamplePose(int variant)
        {
            var p = new PoseProvenance { Source = "sample/pose_" + variant + ".dat", Sha256 = new string((char)('a' + variant), 64), GameName = 4 };
            p.Unresolved[331] = new[] { 0f, 0.5f, -0f, 1.25f, 2f, 3f, 1f, 1f, 1f };
            p.Unresolved[1001] = new[] { 0.1f, 0.2f, 0.3f, 0f, 0f, 0f, 1f, 1f, 1f };
            if (variant == 2)
            {
                p.Ik = new PoseProvenance.PoseIkBlock { Enabled = true, Groups = new[] { true, false, true, true, false } };
                p.Ik.Targets[0] = new[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f };
            }
            p.Trailer["hs2pe"] = "AAECAw==";
            return p;
        }

        static string BlockText(SceneSession s, string id)
        {
            var tok = s.Model.Find(id)?.Doc?.Ext?.Get(PoseProvenanceExt.Ns);
            return tok == null ? null : CanonicalJsonWriter.ToText(tok);
        }

        static void PoseBlock(TestRunner t)
        {
            var s = Kit.GoldenSession(t, true, 41);
            var id = Kit.GoldenChara;
            var p1 = SamplePose(1); var p2 = SamplePose(2);
            string j1 = p1.ToJson(), j2 = p2.ToJson();
            var rt = PoseProvenance.Parse(j1);
            t.Check(rt != null && rt.ToJson() == j1 && rt.Unresolved.Count == 2 && rt.Ik == null && rt.Trailer["hs2pe"] == "AAECAw==", "PoseProvenance JSON round trip");
            t.Check(PoseProvenance.Parse(j2)?.ToJson() == j2 && PoseProvenance.Parse(j2).Ik.Targets[0][8] == 9f, "PoseProvenance with IK targets round trip");
            t.Check(PoseProvenance.Parse("{\"source\": 1}") == null && PoseProvenance.Parse("[]") == null, "not a provenance block → null");
            var b0 = Kit.Bytes(s);
            int u0 = s.Commands.UndoList.Count;
            Kit.Ok(t, s.Commands.Execute(new SetNodeExtCommand(id, PoseProvenanceExt.Ns, j1, "Load pose")), "set hs2.pose");
            t.Equal(j1, BlockText(s, id), "the node holds the block (canonical text)");
            t.Equal(u0 + 1, s.Commands.UndoList.Count, "one history entry");
            var b1 = Kit.Bytes(s);
            Kit.Ok(t, s.Commands.Execute(new SetNodeExtCommand(id, PoseProvenanceExt.Ns, j2)), "replace hs2.pose");
            t.Equal(j2, BlockText(s, id), "replaced");
            Kit.NoOp(t, s.Commands.Execute(new SetNodeExtCommand(id, PoseProvenanceExt.Ns, j2)), "setting the same value is a no-op");
            Kit.Ok(t, s.Commands.Undo(), "undo the replace");
            t.Check(Kit.Same(b1, Kit.Bytes(s)), "undo restores the canonical bytes " + Kit.Diff(b1, Kit.Bytes(s)));
            Kit.Ok(t, s.Commands.Redo(), "redo the replace");
            t.Equal(j2, BlockText(s, id), "redo sets the new value again");
            Kit.Ok(t, s.Commands.Execute(new SetNodeExtCommand(id, PoseProvenanceExt.Ns, null)), "remove hs2.pose");
            t.Check(BlockText(s, id) == null, "removed");
            Kit.Ok(t, s.Commands.Undo(), "undo the removal");
            t.Equal(j2, BlockText(s, id), "undo of the removal restores the block");
            Kit.Ok(t, s.Commands.Undo(), "undo the replace again");
            Kit.Ok(t, s.Commands.Undo(), "undo the set");
            t.Check(Kit.Same(b0, Kit.Bytes(s)), "undo to the start restores the original bytes " + Kit.Diff(b0, Kit.Bytes(s)));
            Kit.Fails(t, s.Commands.Execute(new SetNodeExtCommand(id, "my.plugin", j1)), CommandCodes.BadArgs, "a plugin namespace is refused");
            Kit.Fails(t, s.Commands.Execute(new SetNodeExtCommand(id, "hs2.kkex", j1)), CommandCodes.BadArgs, "hs2.kkex is refused");
            Kit.Fails(t, s.Commands.Execute(new SetNodeExtCommand(id, PoseProvenanceExt.Ns, "{not json")), CommandCodes.BadArgs, "invalid JSON is refused");
            Kit.Fails(t, s.Commands.Execute(new SetNodeExtCommand("ffffffffffffffffffffffffffffffff", PoseProvenanceExt.Ns, j1)), CommandCodes.NotFound, "a missing node");

            // inside a transaction (one history entry with a property set)
            int u1 = s.Commands.UndoList.Count;
            bool fk0 = ((CharaNodeState)s.Model.Find(id).State).Fk.Enabled;
            using (var tx = s.Commands.Begin("Load pose"))
            {
                Kit.Ok(t, tx.Set(id, "chara.fk.enabled", !fk0), "transaction: property step");
                Kit.Ok(t, tx.Execute(new SetNodeExtCommand(id, PoseProvenanceExt.Ns, j1)), "transaction: ext step");
                Kit.Ok(t, tx.Commit(), "transaction commit");
            }
            t.Check(s.Commands.UndoList.Count == u1 + 1 && BlockText(s, id) == j1, "the block joins the transaction (one entry)");
            Kit.Ok(t, s.Commands.Undo(), "undo the transaction");
            t.Check(BlockText(s, id) == null && ((CharaNodeState)s.Model.Find(id).State).Fk.Enabled == fk0, "undo of the transaction removes the block and the flag");
            Kit.Ok(t, s.Commands.Redo(), "redo the transaction");

            // save → load: a known namespace, byte-identical block
            var bytes = s.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            t.Check(NodeJson(SceneJson(bytes), id)?["ext"]?[PoseProvenanceExt.Ns] != null, "the block is written inline on the node");
            var s2 = Kit.Session(42);
            var lr = s2.Load(bytes);
            bool unknown = false;
            foreach (var f in lr.Findings) unknown |= (f.Code == "ext.unknown" || f.Code == "ext.declined") && (f.Path ?? "").Contains(PoseProvenanceExt.Ns);
            ExtScopeData nd = null;
            t.Check(lr.Ok && !unknown && s2.Model.File.NodeExt.TryGetValue(id, out nd) && nd.Get(PoseProvenanceExt.Ns) != null && !nd.Get(PoseProvenanceExt.Ns).IsUnknown,
                "after load the block is a known namespace (" + lr + ")");
            t.Equal(j1, BlockText(s2, id), "after load the block is byte-identical");
            var bytes2 = s2.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            t.Check(Kit.Same(SceneBytesOf(bytes), SceneBytesOf(bytes2)), "save → load → save: scene.json identical");
            // read outside a session: a known namespace as well (2.4; in 2.3 the handler declined outside a session)
            var direct = SceneContainer.Load(bytes);
            ExtScopeData dd = null;
            t.Check(direct.Ok && direct.Value.NodeExt.TryGetValue(id, out dd) && dd.Get(PoseProvenanceExt.Ns) != null && !dd.Get(PoseProvenanceExt.Ns).IsUnknown,
                "read outside a session: accepted (known namespace)");

            // duplicate and delete → undo
            var dup = s2.Duplicate(new[] { id });
            Kit.Ok(t, dup, "duplicate the character");
            string copy = dup.NewIds != null && dup.NewIds.Count > 0 ? dup.NewIds[0] : null;
            t.Equal(j1, copy == null ? null : BlockText(s2, copy), "the duplicate carries the block");
            Kit.Ok(t, s2.Delete(new[] { id }), "delete the character");
            Kit.Ok(t, s2.Commands.Undo(), "undo the delete");
            t.Equal(j1, BlockText(s2, id), "delete → undo keeps the block");
        }

        static byte[] SceneBytesOf(byte[] container) => ContainerReader.Read(container).Get(SceneDocument.EntryName).Data;

        // The manifest ext entries of a namespace as "scope 'root' vN [required]" strings.
        static string ManifestEntries(byte[] container, string ns)
        {
            var m = DocReader.Parse(ContainerReader.Read(container).Get(EntryNames.Manifest).Data, EntryNames.Manifest);
            var l = new List<string>();
            if (m["ext"] is JArray a)
                foreach (JObject e in a)
                    if ((string)e["ns"] == ns) l.Add((string)e["scope"] + " '" + (string)e["root"] + "' v" + (int)e["version"] + ((bool)e["required"] ? " required" : ""));
            return string.Join("; ", l);
        }

        static string NodeBlockText(byte[] container, string id, string ns)
        {
            var tok = NodeJson(SceneJson(container), id)?["ext"]?[ns];
            return tok == null ? null : CanonicalJsonWriter.ToText(tok);
        }

        static bool HasFinding(FindingList list, string code, string ns)
        {
            foreach (var f in list) if (f.Code == code && (f.Path ?? "").Contains(ns)) return true;
            return false;
        }

        static SceneNode Node(SceneContainer c, string id)
        {
            foreach (var n in c.Scene.Nodes) if (n.Id == id) return n;
            return null;
        }

        // 2.4: the hs2.pose block survives every save path as a preserved
        // ext block. In 2.3 a container a session had loaded, saved directly with SceneContainer.Save / ToBytes, dropped it
        // (the handler wrote only during a session save, and writing nothing removes a block); since the dispatcher works on
        // the node objects the model shares, that direct save also removed the block from the open session's nodes.
        static void PoseBlockSavePaths(TestRunner t)
        {
            var id = Kit.GoldenChara;
            string ns = PoseProvenanceExt.Ns, j1 = SamplePose(1).ToJson(), j2 = SamplePose(2).ToJson();
            var opts = Kit.FixedSave(ThumbnailMode.Placeholder);

            // a block set in a session; the session's container saved directly, before any session save
            var s = Kit.GoldenSession(t, true, 43);
            Kit.Ok(t, s.Commands.Execute(new SetNodeExtCommand(id, ns, j1, "Load pose")), "save paths: set hs2.pose");
            var direct0 = s.Model.File.ToBytes(opts);
            t.Equal(j1, NodeBlockText(direct0, id, ns), "a block set in a session survives a direct save of the session's container (never saved by the session)");
            t.Equal("node '' v1", ManifestEntries(direct0, ns), "  its manifest entry: node scope, version 1, not required");
            t.Equal(j1, BlockText(s, id), "  and the session's node still holds it after the direct save");
            var bytes = s.ToBytes(opts);
            t.Equal(j1, NodeBlockText(bytes, id, ns), "the session save writes the block");
            t.Check(Kit.Same(SceneBytesOf(direct0), SceneBytesOf(bytes)), "direct and session save: scene.json identical " + Kit.Diff(SceneBytesOf(direct0), SceneBytesOf(bytes)));

            // the container a session loaded, saved directly
            var s2 = Kit.Session(44);
            var lr = s2.Load(bytes);
            t.Check(lr.Ok && !HasFinding(lr.Findings, "ext.unknown", ns) && !HasFinding(lr.Findings, "ext.declined", ns), "session load: hs2.pose is a known namespace (" + lr + ")");
            var direct1 = s2.Model.File.ToBytes(opts);
            t.Equal(j1, NodeBlockText(direct1, id, ns), "the container a session loaded, saved directly with ToBytes: the block is kept");
            t.Check(Kit.Same(bytes, direct1), "  the whole container byte-identical to the session save it was loaded from " + Kit.Diff(bytes, direct1));
            t.Equal(j1, BlockText(s2, id), "  the session's node keeps the block after the direct save");
            string path = Path.Combine(Path.GetTempPath(), "hs2next_posepaths_" + Guid.NewGuid().ToString("N") + ".hs2nscene");
            try
            {
                var sr = s2.Model.File.Save(path, opts);
                t.Check(sr.Ok && Kit.Same(bytes, File.ReadAllBytes(path)), "  SceneContainer.Save to a file: byte-identical as well (" + (sr.Error?.Message ?? "ok") + ")");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            Kit.Ok(t, s2.Commands.Execute(new SetNodeExtCommand(id, ns, j2)), "replace the block in the loaded session");
            t.Equal(j2, NodeBlockText(s2.Model.File.ToBytes(opts), id, ns), "  a direct save writes the replaced value");
            Kit.Ok(t, s2.Commands.Execute(new SetNodeExtCommand(id, ns, null)), "remove the block in the loaded session");
            var direct2 = s2.Model.File.ToBytes(opts);
            t.Check(NodeBlockText(direct2, id, ns) == null && ManifestEntries(direct2, ns) == "", "  a direct save then writes no block and no manifest entry");
            Kit.Ok(t, s2.Commands.Undo(), "undo the removal");
            t.Equal(j2, NodeBlockText(s2.ToBytes(opts), id, ns), "  the session save after the undo writes the block again");

            // a direct load (no session) → direct save → session load
            var d = SceneContainer.Load(bytes);
            t.Check(d.Ok && !HasFinding(d.Findings, "ext.declined", ns) && !HasFinding(d.Findings, "ext.unknown", ns), "direct load: hs2.pose accepted (" + d + ")");
            var direct3 = d.Value.ToBytes(opts);
            t.Check(Kit.Same(bytes, direct3), "direct load → direct save: byte-identical " + Kit.Diff(bytes, direct3));
            var s3 = Kit.Session(46);
            t.Check(s3.Load(direct3).Ok && BlockText(s3, id) == j1, "  and a session reads the block back");

            // a newer version is declined at load and kept with its version on every save path
            var v2 = SceneContainer.Load(bytes).Value;
            var b2 = v2.NodeExt[id].Get(ns);
            b2.Version = 2;
            b2.IsUnknown = true;     // as another writer's block: written back as it is
            var bytesV2 = v2.ToBytes(opts);
            t.Equal("node '' v2", ManifestEntries(bytesV2, ns), "a block of version 2 (made for the test)");
            var s4 = Kit.Session(47);
            var lr4 = s4.Load(bytesV2);
            t.Check(lr4.Ok && HasFinding(lr4.Findings, "ext.declined", ns) && s4.Model.File.NodeExt[id].Get(ns).IsUnknown, "  session load: declined (newer than version 1), unknown (" + lr4 + ")");
            var v2s = s4.ToBytes(opts);
            t.Check(ManifestEntries(v2s, ns) == "node '' v2" && NodeBlockText(v2s, id, ns) == j1, "  session save: kept with version 2 and its value");
            var v2d = SceneContainer.Load(bytesV2);
            t.Check(v2d.Ok && HasFinding(v2d.Findings, "ext.declined", ns) && Kit.Same(bytesV2, v2d.Value.ToBytes(opts)), "  direct load → direct save: declined and byte-identical");

            // a value that is not a provenance object is declined and preserved byte for byte
            var bad = SceneContainer.Load(bytes).Value;
            var badTok = JToken.Parse("{\"source\": 1, \"note\": [1, 2]}");
            Node(bad, id).Ext.Set(ns, badTok);
            var bytesBad = bad.ToBytes(opts);
            string badText = NodeBlockText(bytesBad, id, ns);
            t.Check(badText == CanonicalJsonWriter.ToText(badTok), "a malformed block is written as the document holds it");
            var s5 = Kit.Session(48);
            var lr5 = s5.Load(bytesBad);
            t.Check(lr5.Ok && HasFinding(lr5.Findings, "ext.declined", ns) && NodeBlockText(s5.ToBytes(opts), id, ns) == badText, "  session load: declined; session save keeps it unchanged");
            var badDirect = SceneContainer.Load(bytesBad);
            t.Check(badDirect.Ok && Kit.Same(bytesBad, badDirect.Value.ToBytes(opts)), "  direct load → direct save: byte-identical");

            // the golden scene has no hs2.pose block: nothing is written for it
            var g = Kit.GoldenSession(t, true, 49);
            var gb = g.ToBytes(opts);
            t.Check(NodeBlockText(gb, id, ns) == null && ManifestEntries(gb, ns) == "", "golden scene: no hs2.pose block written");
        }
    }
}
