// An item's bake and cut blocks on the bus (BakeEditCommand) and the store's half of a rename. BE0 each edit is one history entry that undo and redo restore bit
// for bit, with one change record NodeChanged(id, "item.bake") and one NodeChanged(id, "item.cut") for each block that changed; a block in the model is replaced,
// never changed in place, and shares nothing with the caller or the history; a drag merges by key; with `undo: false` nothing is recorded and the history stays;
// refusals: no node, not an item, a limit grown past; a file's excess may stay; a saved session holds the blocks and their blobs, and a cleared one neither.
// BE3 (store part) a rename writes the store's index; the scene's items that carry the old name and the bake's id take the new name in one entry, which undo
// takes back while the index keeps the new name. Synthetic ids, names and blobs only; the store lives under the run's temporary folder.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Scenes.Bake;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_Bake
    {
        static string Id(int n) => n.ToString("x32");

        static ItemNodeState St(SceneSession s, string id) => (ItemNodeState)s.Model.Find(id).State;

        static CommandResult Run(SceneSession s, ISceneCommand c, string key = null, HistoryMode mode = HistoryMode.Record) =>
            s.Commands.Execute(c, new ExecuteOptions { MergeKey = key, Record = mode });

        static string Json(ItemBake b) => b?.ToJson();
        static string Json(ItemCut c) => c?.ToJson();
        static string Both(SceneSession s, string id) => (Json(St(s, id).Bake) ?? "-") + "|" + (Json(St(s, id).Cut) ?? "-");

        /// <summary>A small bake: a triangle mesh, a 1 x 1 texture and a main of one node and one renderer (this suite does not hold the save tests' helpers).</summary>
        sealed class Small
        {
            public byte[] Main, Mesh, Texture;
            public BakeMain Doc;
            public ItemBake Block;
            public IEnumerable<KeyValuePair<string, byte[]>> ByName() => new[] { Main, Mesh, Texture }.Select(b => new KeyValuePair<string, byte[]>(BakeBlob.Sha256(b), b));
        }

        static Small MakeSmall(string id, int seed, int? no = null)
        {
            var mesh = new BakeMeshData { VertexCount = 3, Positions = new[] { 0f, 0f, 0f, 1f, 0f, seed, 0f, 1f, 0f } };
            mesh.SubMeshes.Add(new BakeSubMesh { Topology = BakeSubMesh.Triangles, Indices = new[] { 0, 1, 2 } });
            var tex = new BakeTextureData { Width = 1, Height = 1, Format = "R8G8B8A8_SRGB", Levels = { new byte[] { (byte)seed, 2, 3, 255 } } };
            var s = new Small { Mesh = mesh.ToBlob(), Texture = tex.ToBlob() };
            s.Doc = new BakeMain { Id = id, Kind = BakeKinds.Parts, Created = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc), Fk = { "root" } };
            s.Doc.Nodes.Add(new BakeMainNode { Name = "root" });
            s.Doc.Meshes.Add(new BakeMainMesh { Blob = BakeBlob.Sha256(s.Mesh), Name = "m" });
            s.Doc.Textures.Add(new BakeMainTexture { Blob = BakeBlob.Sha256(s.Texture), Name = "t" });
            var mat = new BakeMainMaterial { Name = "mat", Shader = "HS2Next/Standard" };
            mat.Props["_MainTex"] = BakeMainProp.Texture(0, new Vec4(1f, 1f, 0f, 0f));
            s.Doc.Materials.Add(mat);
            s.Doc.Renderers.Add(new BakeMainRenderer { Node = 0, Mesh = 0, Materials = new[] { 0 } });
            s.Main = s.Doc.ToBlob();
            s.Block = s.Doc.ToItemBake(BakeBlob.Sha256(s.Main), no);
            return s;
        }

        static ItemCut CutOf(ItemBake source)
        {
            var c = new ItemCut { Source = CutSource.Of(source), Pose = new SortedDictionary<string, Vec3>(StringComparer.Ordinal) { ["root"] = new Vec3(10f, 0f, 0f) } };
            c.Steps.Add(new CutStep { Shape = CutShapes.Plane, Pos = new Vec3(0f, 0.5f, 0f), Keep = CutShapes.Front, Ragged = new CutRagged { Amp = 0.1f, Seed = 7 } });
            c.Steps.Add(new CutStep { Shape = CutShapes.Box, Size = new Vec3(0.5f, 0.5f, 0.5f), Keep = CutShapes.Inside });
            return c;
        }

        // The blobs and blocks: a source bake, a piece's result, a cut of the source.
        static void Blocks(out ItemBake source, out ItemBake result, out ItemCut cut, out List<byte[]> blobs)
        {
            var a = MakeSmall(Id(0xa1), 51, 73100001);
            var b = MakeSmall(Id(0xa2), 52);
            source = a.Block;
            result = b.Block;
            cut = CutOf(a.Block);
            blobs = new List<byte[]> { a.Main, a.Mesh, a.Texture, b.Main, b.Mesh, b.Texture };
        }

        static SceneSession NewSession(out string item, out string folder)
        {
            var s = Kit.Session();
            item = s.AddItem(new ItemNodeState { Ref = "hs2next.bake#73100001", Group = 731406 }).NewIds[0];
            folder = s.AddFolder().NewIds[0];
            return s;
        }

        public static void Run(TestRunner t)
        {
            Steps(t);
            Merge(t);
            Refusals(t);
            Saved(t);
            BE3(t);
        }

        static void Steps(TestRunner t)
        {
            var s = NewSession(out string id, out _);
            Blocks(out var source, out var result, out var cut, out _);
            int n0 = s.Commands.UndoList.Count;
            var states = new List<string> { Both(s, id) };
            var rec = new EventRecorder(s);
            t.Equal("item.bake|item.cut", BakeEditCommand.BakePath + "|" + BakeEditCommand.CutPath, "BE0: the change paths");
            t.Equal("Edit cut", BakeEditCommand.LabelEditCut, "BE0: the history label");

            void Step(ItemBake bake, ItemCut c, string label, params string[] paths)
            {
                rec.Clear();
                Kit.Ok(t, Run(s, new BakeEditCommand(id, bake, c, label)), "BE0: " + label);
                states.Add(Both(s, id));
                var changes = rec.Batches.SelectMany(x => x.Changes).ToList();
                t.Equal(string.Join(",", paths), string.Join(",", changes.Where(x => x.Kind == SceneChangeKind.NodeChanged && x.NodeId == id).Select(x => x.Path)), "BE0: " + label + ": its change records");
                t.Equal(label, s.Commands.UndoList[0].Label, "BE0: " + label + ": its history label");
            }
            Step(source, null, "Bake to item", BakeEditCommand.BakePath);
            t.Equal(Json(source), Json(St(s, id).Bake), "BE0: the block in place");
            Step(result, cut, BakeEditCommand.LabelEditCut, BakeEditCommand.BakePath, BakeEditCommand.CutPath);
            var c2 = cut.Clone();
            c2.Steps[0].Ragged.Amp = 0.25f;
            Step(result, c2, BakeEditCommand.LabelEditCut, BakeEditCommand.CutPath);
            Step(null, c2, "Drop result", BakeEditCommand.BakePath);
            Step(null, null, "Clear", BakeEditCommand.CutPath);
            t.Check(St(s, id).Bake == null && St(s, id).Cut == null, "BE0: null blocks leave none");
            t.Equal(n0 + 5, s.Commands.UndoList.Count, "BE0: one entry per edit");
            for (int i = 4; i >= 0; i--)
            {
                rec.Clear();
                Kit.Ok(t, s.Commands.Undo(), "BE0: undo " + i);
                t.Equal(states[i], Both(s, id), "BE0: undo " + i + " restores both blocks bit for bit");
                t.Check(rec.Batches.SelectMany(x => x.Changes).Any(x => x.Kind == SceneChangeKind.NodeChanged), "BE0: undo " + i + " reports it");
            }
            for (int i = 0; i < 5; i++)
            {
                Kit.Ok(t, s.Commands.Redo(), "BE0: redo " + i);
                t.Equal(states[i + 1], Both(s, id), "BE0: redo " + i);
            }
            // Copies: neither the caller's objects nor the model's are shared.
            Kit.Ok(t, s.Commands.Undo(), "BE0: undo the clear");
            var mine = St(s, id).Cut;
            var c3 = c2.Clone();
            c3.Steps.RemoveAt(1);
            var edit = new BakeEditCommand(id, result, c3);
            c3.Steps.Clear();
            result.Data.Clear();
            Kit.Ok(t, Run(s, edit), "BE0: an edit whose caller's objects change after it was made");
            t.Check(St(s, id).Cut.Steps.Count == 1 && St(s, id).Bake.Data.Count == 2 && !ReferenceEquals(St(s, id).Bake, result) && !ReferenceEquals(St(s, id).Cut, c3) && !ReferenceEquals(St(s, id).Cut, mine),
                "BE0: it holds its own copies and puts new objects in the model");
            string after = Both(s, id);
            St(s, id).Cut.Steps.Clear();
            Kit.Ok(t, s.Commands.Undo(), "BE0: undo after the model's block was changed in place by someone else");
            t.Equal(states[4], Both(s, id), "BE0: undo restores the history's own copy");
            Kit.Ok(t, s.Commands.Redo(), "BE0: redo");
            t.Equal(after, Both(s, id), "BE0: redo puts the history's own copy back");
            Kit.Ok(t, s.Commands.Undo(), "BE0: undo");
            // Size, no-op.
            int entries = s.Commands.UndoList.Count;
            Kit.NoOp(t, Run(s, new BakeEditCommand(id, St(s, id).Bake, St(s, id).Cut)), "BE0: an edit to the same blocks");
            t.Equal(entries, s.Commands.UndoList.Count, "BE0: a no-op records nothing");
            t.Equal("Edit cut", new BakeEditCommand(id, null, null, null).Label, "BE0: a null label is the default");
            string bakeBefore = Json(St(s, id).Bake), cutBefore = Json(St(s, id).Cut);
            Kit.Ok(t, Run(s, new BakeEditCommand(id, source, null)), "BE0: one more edit");
            long expect = BakeEditCommand.BaseBytes + 2L * ((bakeBefore?.Length ?? 0) + (cutBefore?.Length ?? 0) + Json(St(s, id).Bake).Length);
            t.Equal(expect, s.Commands.UndoList[0].EstimatedBytes, "BE0: entry size = 256 + 2 x (the JSON lengths before and after)");
        }

        static void Merge(TestRunner t)
        {
            var s = NewSession(out string id, out _);
            Blocks(out var source, out var result, out var cut, out _);
            Kit.Ok(t, Run(s, new BakeEditCommand(id, result, cut)), "BE0 merge: a first edit");
            int n = s.Commands.UndoList.Count;
            string before = Both(s, id);
            for (int i = 1; i <= 20; i++)
            {
                var c = cut.Clone();
                c.Steps[0].Ragged.Amp = i * 0.01f;
                Kit.Ok(t, Run(s, new BakeEditCommand(id, result, c), "drag"), "BE0 merge: step " + i);
            }
            t.Check(s.Commands.UndoList.Count == n + 1 && St(s, id).Cut.Steps[0].Ragged.Amp == 20 * 0.01f, "BE0 merge: a drag of twenty steps is one entry");
            Kit.Ok(t, s.Commands.Undo(), "BE0 merge: undo");
            t.Equal(before, Both(s, id), "BE0 merge: undo goes back before the drag");
            // undo: false
            n = s.Commands.UndoList.Count;
            Kit.Ok(t, Run(s, new BakeEditCommand(id, source, null), null, HistoryMode.None), "BE0 undo:false: applied");
            t.Check(s.Commands.UndoList.Count == n && St(s, id).Cut == null && Json(St(s, id).Bake) == Json(source), "BE0 undo:false: nothing recorded, the history kept");
            // Inside a batch, with another node's property set.
            n = s.Commands.UndoList.Count;
            using (var tx = s.Commands.Begin("batch", new ExecuteOptions { Source = CommandSource.Bridge }))
            {
                Kit.Ok(t, tx.Execute(new BakeEditCommand(id, result, cut)), "BE0 batch: an edit inside");
                Kit.Ok(t, tx.Execute(new SetPropertyCommand(id, "name", "Piece")), "BE0 batch: a name inside");
                Kit.Ok(t, tx.Commit(), "BE0 batch: commit");
            }
            t.Check(s.Commands.UndoList.Count == n + 1 && St(s, id).Cut != null && s.Model.Find(id).Doc.Name == "Piece", "BE0 batch: one entry");
        }

        static void Refusals(TestRunner t)
        {
            var s = NewSession(out string id, out string folder);
            Blocks(out var source, out var result, out var cut, out _);
            Kit.Fails(t, Run(s, new BakeEditCommand(Id(0xeeee), source, null)), CommandCodes.NotFound, "BE0: no such node");
            Kit.Fails(t, Run(s, new BakeEditCommand(folder, source, null)), CommandCodes.WrongKind, "BE0: a folder is no item");
            var many = cut.Clone();
            while (many.Steps.Count <= BakeLimits.MaxSteps) many.Steps.Add(new CutStep { Shape = CutShapes.Plane, Keep = CutShapes.Back });
            var r = Run(s, new BakeEditCommand(id, result, many));
            Kit.Fails(t, r, CommandCodes.BadArgs, "BE0: 65 steps");
            t.Check(r.Message.Contains(BakeFindings.CutLimit), "BE0: the refusal names the limit (" + r.Message + ")");
            var data = result.Clone();
            while (data.Data.Count <= BakeLimits.MaxData) data.Data.Add(ResStore.NameOf(Encoding.ASCII.GetBytes("HNBG" + data.Data.Count)));
            var rd = Run(s, new BakeEditCommand(id, data, null));
            Kit.Fails(t, rd, CommandCodes.BadArgs, "BE0: 1,025 data entries");
            t.Check(rd.Message.Contains(BakeFindings.Limit), "BE0: the refusal names the limit (" + rd.Message + ")");
            var srcData = cut.Clone();
            srcData.Source.Data = new List<string>(data.Data);
            Kit.Fails(t, Run(s, new BakeEditCommand(id, result, srcData)), CommandCodes.BadArgs, "BE0: a source of 1,025 data entries");
            var noId = result.Clone();
            noId.Id = null;
            Kit.Fails(t, Run(s, new BakeEditCommand(id, noId, null)), CommandCodes.BadArgs, "BE0: a block that cannot be written");
            t.Check(St(s, id).Bake == null && St(s, id).Cut == null, "BE0: refused edits change nothing");
            // A file's excess may stay, not grow (the document's writer writes what a command refuses).
            var fileScene = new SceneContainer { Title = "excess" };
            fileScene.Scene.Nodes.Add(new SceneNode(id, new ItemNodeState { Ref = "hs2next.bake#73100001", Group = 731406, Cut = many.Clone() }) { Order = 0 });
            var hand = fileScene.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder, new ExtRegistry()));
            var s2 = Kit.Session();
            var l = s2.Load(hand, new LoadOptions { Registry = new ExtRegistry() });
            t.Check(l.Ok && l.Findings.Has(BakeFindings.CutLimit), "BE0: a file with 65 steps loads, cut.limit (" + l + ")");
            if (!l.Ok) return;
            var same = St(s2, id).Cut.Clone();
            same.Steps[0].Keep = CutShapes.Back;
            Kit.Ok(t, Run(s2, new BakeEditCommand(id, result, same)), "BE0: an edit that keeps the 65 steps");
            same.Steps.RemoveAt(64);
            Kit.Ok(t, Run(s2, new BakeEditCommand(id, result, same)), "BE0: an edit that shrinks them");
            same.Steps.Add(new CutStep { Shape = CutShapes.Box, Keep = CutShapes.Inside });
            same.Steps.Add(new CutStep { Shape = CutShapes.Box, Keep = CutShapes.Inside });
            Kit.Fails(t, Run(s2, new BakeEditCommand(id, result, same)), CommandCodes.BadArgs, "BE0: an edit that grows past the limit");
        }

        static void Saved(TestRunner t)
        {
            var s = NewSession(out string id, out _);
            Blocks(out var source, out var result, out var cut, out var blobs);
            var opts = Kit.FixedSave(ThumbnailMode.Placeholder, new ExtRegistry());
            var b0 = s.ToBytes(opts);
            int FormatOf(byte[] f) => (int)DocReader.Parse(ContainerReader.Read(f).Get("manifest.json").Data, "manifest.json")["formatVersion"];
            int Blobs(byte[] f) => ContainerReader.Read(f).Entries.Count(e => e.Name.EndsWith(".bin", StringComparison.Ordinal));
            t.Check(FormatOf(b0) == 1 && Blobs(b0) == 0, "BE0 file: no block: revision 1, no blob");
            foreach (var b in blobs) s.Model.File.Res.AddBlob(b);
            Kit.Ok(t, Run(s, new BakeEditCommand(id, result, cut)), "BE0 file: a piece");
            var b1 = s.ToBytes(opts);
            t.Check(FormatOf(b1) == 5 && Blobs(b1) == 6, "BE0 file: revision 5, the result's and the source's blobs (" + Blobs(b1) + ")");
            var s2 = Kit.Session();
            var l = s2.Load(b1, new LoadOptions { Registry = new ExtRegistry() });
            t.Check(l.Ok, "BE0 file: it loads (" + l + ")");
            if (l.Ok)
            {
                t.Equal(Both(s, id), Both(s2, id), "BE0 file: the blocks come back");
                t.Check(!s2.Commands.IsDirty && s2.Model.File.Res.Count == 6, "BE0 file: a load is not an edit; the session holds the six blobs");
                t.Check(Kit.Same(b1, s2.ToBytes(opts)), "BE0 file: and saves the same bytes");
            }
            Kit.Ok(t, Run(s, new BakeEditCommand(id, null, null)), "BE0 file: cleared");
            var b2 = s.ToBytes(opts);
            t.Check(FormatOf(b2) == 1 && Kit.Same(b2, b0), "BE0 file: back to revision 1, the file as before, no blob");
            Kit.Ok(t, s.Commands.Undo(), "BE0 file: undo of the clearing");
            t.Check(Kit.Same(s.ToBytes(opts), b1), "BE0 file: the blobs stayed in the session for the undo");
        }

        // ---------------- BE3: the store's half of a rename ----------------

        static void BE3(TestRunner t)
        {
            string dir = Path.Combine(t.TempDir ?? Path.GetTempPath(), "be3_store");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            var store = BakeStore.Open(dir);
            var a = MakeSmall(Id(0xa5), 55);
            var row = store.Add(new BakeStoreRow { Id = Id(0xa5), Name = "Shoes", Kind = "parts", Main = BakeBlob.Sha256(a.Main), Data = a.Doc.DataBlobs() }, a.ByName());
            var s = Kit.Session();
            string Add(string name, string bakeId)
            {
                var b = a.Block.Clone();
                b.Id = bakeId;
                b.No = row.Number;
                string id = s.AddItem(new ItemNodeState { Ref = "hs2next.bake#" + row.Number, Group = 731406, Bake = b }).NewIds[0];
                Kit.Ok(t, s.Commands.Set(id, "name", name), "BE3: name " + name);
                return id;
            }
            string n1 = Add("Shoes", Id(0xa5)), n2 = Add("Shoes", Id(0xa6)), n3 = Add("Shoes", Id(0xa5)), n4 = Add("Boots of old", Id(0xa5));
            int hadOld = new[] { n1, n2, n3, n4 }.Count(n => s.Model.Find(n).Doc.Name == "Shoes");
            t.Info("BE3: nodes that had the old name: " + hadOld + ", of them the bake's: 2");
            t.Check(hadOld >= 1, "BE3: a node had the old name");

            int entries = s.Commands.UndoList.Count;
            string old = store.Rename(row.Number, "Boots");
            t.Equal("Shoes", old, "BE3: the store's rename returns the old name");
            t.Equal("Boots", BakeStore.Open(dir).Find(row.Number).Name, "BE3: and writes the index");
            var items = s.Model.PreOrder().Where(n => n.State is ItemNodeState).Select(n => (n.Id, n.Doc.Name, ((ItemNodeState)n.State).Bake?.Id)).ToList();
            var targets = BakeStore.RenameTargets(items, row.Id, old);
            t.Equal(string.Join(",", new[] { n1, n3 }), string.Join(",", targets), "BE3: the items of that bake with the old name");
            Kit.Ok(t, Run(s, new CompositeCommand("Rename bake", targets.Select(n => (ISceneCommand)new SetPropertyCommand(n, "name", "Boots")))), "BE3: the items renamed in one entry");
            t.Check(s.Commands.UndoList.Count == entries + 1 && s.Model.Find(n1).Doc.Name == "Boots" && s.Model.Find(n3).Doc.Name == "Boots" && s.Model.Find(n2).Doc.Name == "Shoes" && s.Model.Find(n4).Doc.Name == "Boots of old",
                "BE3: those two take the new name, the others keep theirs");
            Kit.Ok(t, s.Commands.Undo(), "BE3: undo");
            t.Check(s.Model.Find(n1).Doc.Name == "Shoes" && s.Model.Find(n3).Doc.Name == "Shoes", "BE3: undo renames the nodes back");
            t.Equal("Boots", BakeStore.Open(dir).Find(row.Number).Name, "BE3: the index stays");
            Kit.Ok(t, s.Commands.Redo(), "BE3: redo");
            t.Check(s.Model.Find(n1).Doc.Name == "Boots" && BakeStore.Open(dir).Find(row.Number).Name == "Boots", "BE3: redo: the nodes renamed again, the index as it is");
        }
    }
}
