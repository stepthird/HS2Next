// CB6 on a session: what SceneSession.Duplicate carries. Without an entry that has both ends among the copied nodes, it is the plain add: the
// same command, no list touched. With one, the add and the list are one history entry, the copies are selected, ids are new, the ends
// stand on the copies and everything else of the entry is kept; undo and redo restore nodes and list byte-equal; an open transaction takes
// both as steps; `undo: false` applies as a plain duplicate does. An entry the list cannot take stays behind and the nodes are copied all
// the same: a full list, an entry that cannot act. Synthetic ids only.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_ConstraintCarry
    {
        static string Id(int n) => n.ToString("x32");

        // a folder with a child folder, and a folder beside it
        sealed class Tree
        {
            public SceneSession S;
            public string Top, Child, Beside;
        }

        static Tree Make(TestRunner t)
        {
            var s = Kit.Session();
            var top = s.AddFolder();
            var child = s.AddFolder(top.NewId);
            var beside = s.AddFolder();
            Kit.Ok(t, top, "CB6: a folder");
            Kit.Ok(t, child, "CB6: a child folder");
            Kit.Ok(t, beside, "CB6: a folder beside it");
            return new Tree { S = s, Top = top.NewId, Child = child.NewId, Beside = beside.NewId };
        }

        static ConstraintEntry Entry(int n, string source, string target, string component = ConstraintComponents.Position) => new ConstraintEntry
        {
            Id = Id(n),
            Source = new ConstraintEnd(source),
            Target = new ConstraintEnd(target),
            Components = new List<string> { component },
        };

        static void Put(TestRunner t, SceneSession s, params ConstraintEntry[] entries) =>
            Kit.Ok(t, s.Commands.Execute(ConstraintEditCommand.ReplaceList(new ConstraintList(entries), ConstraintEditCommand.LabelAdd)), "CB6: the list");

        public static void Run(TestRunner t)
        {
            Plain(t);
            Carried(t);
            KeptMembers(t);
            Transaction(t);
            NotRecorded(t);
            LeftBehind(t);
            Counters(t);
        }

        // ---------- the plain path ----------

        static void Plain(TestRunner t)
        {
            var a = Make(t);
            var r = a.S.Duplicate(new[] { a.Top });
            Kit.Ok(t, r, "CB6: a scene with no list duplicates");
            t.Check(r.Command is AddNodesCommand, "CB6: no list: the plain add, no composite");
            t.Check(a.S.Model.Constraints == null && a.S.ConstraintsLeftOut == 0, "CB6: no list: still none, nothing left out");

            var b = Make(t);
            Put(t, b.S, Entry(1, b.Top, b.Beside));
            var list = b.S.Model.Constraints;
            long v = b.S.Commands.Version;
            var outer = b.S.Duplicate(new[] { b.Top });
            Kit.Ok(t, outer, "CB6: an entry with one end outside");
            t.Check(outer.Command is AddNodesCommand, "CB6: one end outside: the plain add");
            t.Check(ReferenceEquals(list, b.S.Model.Constraints) && b.S.ConstraintsLeftOut == 0, "CB6: one end outside: the list is the same object, nothing counted as left out");
            t.Equal(v + 1, b.S.Commands.Version, "CB6: one end outside: one operation");
        }

        // ---------- one entry with the list ----------

        static void Carried(TestRunner t)
        {
            var x = Make(t);
            var s = x.S;
            Put(t, s, Entry(1, x.Top, x.Child), Entry(2, x.Top, x.Beside), Entry(3, x.Beside, x.Top));
            var before = s.Model.Constraints;
            string json = before.ToJson();
            byte[] bytes = Kit.Bytes(s);
            int nodes = s.Model.Count, undo = s.Commands.UndoList.Count;

            var r = s.Duplicate(new[] { x.Top }, new ExecuteOptions { Label = "Duplicate", Source = CommandSource.User });
            Kit.Ok(t, r, "CB6: an inner and two outer entries");
            t.Check(r.Command is CompositeCommand, "CB6: the add and the list are one composite");
            t.Equal(1, r.NewIds.Count, "CB6: the new top id");
            t.Equal(nodes + 2, s.Model.Count, "CB6: the two nodes copied");
            t.Equal(undo + 1, s.Commands.UndoList.Count, "CB6: one history entry");
            t.Equal("Duplicate", s.Commands.UndoList[0].Label, "CB6: labelled «Duplicate»");
            t.Check(s.Selection.Count == 1 && s.Selection.Primary == r.NewId, "CB6: the copy is selected");
            var list = s.Model.Constraints;
            t.Equal(4, list.Count, "CB6: the one inner entry copied");
            t.Check(ReferenceEquals(list.Entries[0], before.Entries[0]) && ReferenceEquals(list.Entries[2], before.Entries[2]), "CB6: the entries before stay, in order, shared");
            var copy = list.Entries[3];
            var top = s.Model.Find(r.NewId);
            t.Check(copy.Id != Id(1) && JsonText.IsId(copy.Id), "CB6: a new id");
            t.Check(copy.Source.Node == top.Id && copy.Target.Node == top.Children[0].Id, "CB6: both ends on the copies");
            t.Check(s.Commands.IsDirty, "CB6: the scene is modified");
            string after = list.ToJson();
            byte[] done = Kit.Bytes(s);

            Kit.Ok(t, s.Commands.Undo(), "CB6: undo");
            t.Equal(nodes, s.Model.Count, "CB6: undo: one step takes the nodes");
            t.Equal(json, s.Model.Constraints.ToJson(), "CB6: undo: the list as before");
            t.Check(Kit.Same(bytes, Kit.Bytes(s)), "CB6: undo: the scene byte-equal");
            Kit.Ok(t, s.Commands.Redo(), "CB6: redo");
            t.Equal(after, s.Model.Constraints.ToJson(), "CB6: redo: the same ids");
            t.Check(Kit.Same(done, Kit.Bytes(s)), "CB6: redo: the scene byte-equal");
            Kit.Valid(t, s, "CB6: the model after a redo");

            // a selected child under a selected parent is one copy, and the entry comes once
            var both = s.Duplicate(new[] { x.Top, x.Child });
            Kit.Ok(t, both, "CB6: parent and child selected");
            t.Equal(5, s.Model.Constraints.Count, "CB6: the entry comes once");

            // copying only the child leaves the entry behind
            int n = s.Model.Constraints.Count;
            var child = s.Duplicate(new[] { x.Child });
            t.Check(child.Command is AddNodesCommand && s.Model.Constraints.Count == n, "CB6: only one end copied: nothing carried");
        }

        static void KeptMembers(TestRunner t)
        {
            var x = Make(t);
            var s = x.S;
            var e = new ConstraintEntry
            {
                Id = Id(7), Label = "grip", On = false, Weight = 0.25f,
                Source = new ConstraintEnd(x.Top, "acc:" + Id(9) + "/ca_slot00#2"),
                Target = new ConstraintEnd(x.Child, null, "LeftHand"),
                Components = new List<string> { ConstraintComponents.Position, ConstraintComponents.Rotation },
                Offset = new ConstraintOffset(new Vec3(1, 2, 3), new Vec3(10, 20, 30), Vec3.One),
            };
            Put(t, s, e);
            var r = s.Duplicate(new[] { x.Top });
            var c = s.Model.Constraints.Entries[1];
            t.Check(c.Label == "grip" && !c.On && c.Weight == 0.25f && c.Source.Bone == e.Source.Bone && c.Target.Ik == "LeftHand", "CB6: label, switch, weight, address and IK target kept");
            t.Check(c.Components.SequenceEqual(e.Components) && c.Offset.Pos.Equals(e.Offset.Pos) && c.Offset.Rot.Equals(e.Offset.Rot), "CB6: components and offsets kept");
            t.Check(!ReferenceEquals(c.Offset, e.Offset) && !ReferenceEquals(c.Source, e.Source), "CB6: nothing shared with the original");
        }

        static void Transaction(TestRunner t)
        {
            var x = Make(t);
            var s = x.S;
            Put(t, s, Entry(1, x.Top, x.Child));
            int undo = s.Commands.UndoList.Count, nodes = s.Model.Count;
            string json = s.Model.Constraints.ToJson();
            using (var tx = s.Commands.Begin("Batch"))
            {
                var r = s.Duplicate(new[] { x.Top });
                Kit.Ok(t, r, "CB6: inside a transaction");
                t.Check(r.NewIds.Count == 1 && s.Model.Constraints.Count == 2, "CB6: inside a transaction: applied at once, the new id answered");
                Kit.Ok(t, tx.Commit(), "CB6: the transaction's commit");
            }
            t.Equal(undo + 1, s.Commands.UndoList.Count, "CB6: inside a transaction: its one entry");
            t.Equal("Batch", s.Commands.UndoList[0].Label, "CB6: inside a transaction: its label");
            Kit.Ok(t, s.Commands.Undo(), "CB6: undo of the transaction");
            t.Check(s.Model.Count == nodes && s.Model.Constraints.ToJson() == json, "CB6: undo of the transaction: nodes and list back");

            // a transaction that is cancelled takes both back
            using (var tx = s.Commands.Begin("Cancelled"))
            {
                Kit.Ok(t, s.Duplicate(new[] { x.Top }), "CB6: inside a transaction to cancel");
                tx.Cancel();
            }
            t.Check(s.Model.Count == nodes && s.Model.Constraints.ToJson() == json, "CB6: a cancelled transaction leaves neither");
        }

        static void NotRecorded(TestRunner t)
        {
            var a = Make(t);
            var plain = a.S.Duplicate(new[] { a.Top }, ExecuteOptions.NotRecorded);
            Kit.Ok(t, plain, "CB6: undo false, plain");
            var x = Make(t);
            Put(t, x.S, Entry(1, x.Top, x.Child));
            var r = x.S.Duplicate(new[] { x.Top }, ExecuteOptions.NotRecorded);
            Kit.Ok(t, r, "CB6: undo false, with an entry");
            t.Check(r.NewIds.Count == 1 && x.S.Model.Constraints.Count == 2, "CB6: undo false: both applied");
            t.Equal(a.S.Commands.UndoList.Count, x.S.Commands.UndoList.Count, "CB6: undo false: the history as after a plain duplicate");
        }

        // ---------- what the list cannot take ----------

        static void LeftBehind(TestRunner t)
        {
            // a file's list: two entries an edit could not make, beside a good one
            var bad = Entry(1, Kit.GoldenChara, Kit.GoldenItem, "wobble");
            var loud = Entry(2, Kit.GoldenChara, Kit.GoldenItem);
            loud.Weight = 1.5f;
            var good = Entry(3, Kit.GoldenChara, Kit.GoldenItem);
            var c = Kit.GoldenContainer(t, true);
            c.Scene.Constraints = ConstraintList.Parse(new ConstraintList(new[] { bad, loud, good }).ToJson());
            var s = Kit.Session();
            s.Bind(c);
            t.Equal(3, s.Model.Constraints.Count, "CB6: the file's three entries");
            int nodes = s.Model.Count;
            var r = s.Duplicate(new[] { Kit.GoldenChara });
            Kit.Ok(t, r, "CB6: entries that cannot act do not stop the duplicate");
            t.Equal(nodes + 4, s.Model.Count, "CB6: the nodes are copied");
            t.Equal(4, s.Model.Constraints.Count, "CB6: the good one is carried");
            t.Equal(2, s.ConstraintsLeftOut, "CB6: two left out, counted");
            t.Equal(3, s.Model.Constraints.Entries.Take(3).Count(e => e.Source.Node == Kit.GoldenChara), "CB6: the file's entries stay");

            var y = Make(t);
            var full = new ConstraintList(Enumerable.Range(1, ConstraintLimits.MaxEntries).Select(i => Entry(i, y.Top, y.Child)));
            Kit.Ok(t, y.S.Commands.Execute(ConstraintEditCommand.ReplaceList(full)), "CB6: a full list");
            var f = y.S.Duplicate(new[] { y.Top });
            Kit.Ok(t, f, "CB6: a full list does not stop the duplicate");
            t.Check(f.Command is AddNodesCommand && y.S.Model.Constraints.Count == ConstraintLimits.MaxEntries, "CB6: a full list: the plain add, the list as it was");
            t.Equal(ConstraintLimits.MaxEntries, y.S.ConstraintsLeftOut, "CB6: a full list: each entry left out, counted");

            var z = Make(t);
            var almost = new ConstraintList(Enumerable.Range(1, ConstraintLimits.MaxEntries - 1).Select(i => Entry(i, z.Top, i < 3 ? z.Child : z.Beside)));
            Kit.Ok(t, z.S.Commands.Execute(ConstraintEditCommand.ReplaceList(almost)), "CB6: a list one short of full");
            var g = z.S.Duplicate(new[] { z.Top });
            Kit.Ok(t, g, "CB6: one place left");
            t.Equal(ConstraintLimits.MaxEntries, z.S.Model.Constraints.Count, "CB6: the first entry fits");
            t.Equal(1, z.S.ConstraintsLeftOut, "CB6: the second is left out");
            var next = z.S.Duplicate(new[] { z.Beside });
            t.Equal(0, z.S.ConstraintsLeftOut, "CB6: the count is of the last duplicate");
        }

        static void Counters(TestRunner t)
        {
            var x = Make(t);
            var s = x.S;
            var list = new ConstraintList(new[] { Entry(1, x.Top, x.Child), Entry(2, x.Top, x.Beside), Entry(3, x.Beside, x.Top), Entry(4, x.Top, x.Child, "wobble") });
            var map = new Dictionary<string, string> { [x.Top] = Id(0x71), [x.Child] = Id(0x72) };
            var carried = s.CarryConstraints(list, map, out int outside, out int refused);
            t.Check(carried != null && carried.Count == 1, "CB7: a file's list: the one with both ends and no problem");
            t.Equal(2, outside, "CB7: two with an end outside");
            t.Equal(1, refused, "CB7: one that cannot act");
            t.Check(carried.Entries[0].Source.Node == Id(0x71) && carried.Entries[0].Target.Node == Id(0x72), "CB7: the ends remapped");
            t.Check(s.CarryConstraints(null, map, out _, out _) == null && s.CarryConstraints(list, null, out _, out _) == null, "CB7: no list or no map: null");
            t.Check(s.Model.Constraints == null, "CB7: the session's list is not touched");
        }
    }
}
