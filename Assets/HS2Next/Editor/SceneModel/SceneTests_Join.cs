// C6: CommandBus.BeginOrJoin and joined transactions. Without an open
// transaction BeginOrJoin is Begin; with one it hands out a joined handle whose steps belong to the open
// transaction, whose Commit records nothing (the outer Commit records one entry for everything) and whose Cancel
// reverts only its own steps (the canonical bytes of the join point come back, earlier steps stay). A join is a
// collapse barrier; joins nest; Begin keeps answering busy; closing the outer transaction closes its joins.
using System.Collections.Generic;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_Join
    {
        public static void Run(TestRunner t)
        {
            NoOpenTransaction(t);
            JoinedCommit(t);
            JoinedCancel(t);
            Barrier(t);
            Nested(t);
            OuterCloses(t);
            Busy(t);
        }

        static CharaNodeState Chara(SceneSession s) => (CharaNodeState)s.Model.Find(Kit.GoldenChara).State;

        static void NoOpenTransaction(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var before = Kit.Bytes(s);
            using (var tx = s.Commands.BeginOrJoin("Plain"))
            {
                t.Check(tx.IsOpen && !tx.IsJoined && s.Commands.ActiveTransaction == tx, "no open transaction: BeginOrJoin opens one");
                tx.Set(Kit.GoldenChara, "chara.face.eyes", 4);
                tx.Set(Kit.GoldenChara, "chara.face.mouth", 2);
                Kit.Ok(t, tx.Commit(), "commit");
            }
            t.Equal(1, s.Commands.UndoList.Count, "one entry");
            t.Equal("Plain", s.Commands.UndoList[0].Label, "entry label");
            Kit.Ok(t, s.Commands.Undo(), "undo");
            t.Check(Kit.Same(before, Kit.Bytes(s)), "undo restores the bytes");
            Kit.Valid(t, s, "BeginOrJoin without a transaction");
        }

        static void JoinedCommit(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var rec = new EventRecorder(s);
            var before = Kit.Bytes(s);
            var outer = s.Commands.Begin("Batch");
            outer.Set(Kit.GoldenChara, "chara.face.eyes", 4);
            byte[] afterJoin;
            using (var j = s.Commands.BeginOrJoin("FK"))
            {
                t.Check(j.IsOpen && j.IsJoined && j.Failure == null, "open transaction: a joined handle");
                t.Check(s.Commands.ActiveTransaction == outer, "the joined handle is not the active transaction");
                Kit.Ok(t, j.Set(Kit.GoldenChara, "chara.anime.speed", 2.5f), "joined set");
                Kit.Ok(t, j.Set(Kit.GoldenItem, "transform.pos", new Vec3(0, 3, 0)), "joined set on another node");
                t.Equal(2, j.StepCount, "joined StepCount counts its own steps");
                t.Equal(3, outer.StepCount, "the joined steps are steps of the open transaction");
                rec.Clear();
                var r = j.Commit();
                t.Check(r.Status == CommandStatus.Ok, "joined commit → Success (" + r + ")");
                t.Check(r.Command is CompositeCommand jc && jc.Children.Count == 2, "joined commit result lists its steps");
                t.Check(!j.IsOpen && outer.IsOpen && s.Commands.ActiveTransaction == outer, "joined commit leaves the outer open");
                t.Equal(0, rec.Batches.Count, "joined commit delivers nothing");
                t.Equal(0, rec.History, "joined commit changes no history");
                Kit.Fails(t, j.Commit(), CommandCodes.Busy, "a committed joined handle is closed");
                afterJoin = Kit.Bytes(s);
            }
            t.Equal(0, s.Commands.UndoList.Count, "joined commit records nothing");
            t.Check(Kit.Same(afterJoin, Kit.Bytes(s)), "dispose after a joined commit keeps the steps");
            outer.Set(Kit.GoldenChara, "chara.face.mouth", 2);
            var after = Kit.Bytes(s);
            Kit.Ok(t, outer.Commit(), "outer commit");
            t.Equal(1, s.Commands.UndoList.Count, "outer commit records one entry for everything");
            t.Equal("Batch", s.Commands.UndoList[0].Label, "the entry has the outer label");
            Kit.Ok(t, s.Commands.Undo(), "undo the outer entry");
            t.Check(Kit.Same(before, Kit.Bytes(s)), "undo reverts the joined steps too");
            Kit.Ok(t, s.Commands.Redo(), "redo the outer entry");
            t.Check(Kit.Same(after, Kit.Bytes(s)), "redo replays the joined steps too");
            Kit.Valid(t, s, "joined commit");
        }

        static void JoinedCancel(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var rec = new EventRecorder(s);
            var before = Kit.Bytes(s);
            s.Selection.Set(new[] { Kit.GoldenItem });
            var outer = s.Commands.Begin("Batch");
            outer.Set(Kit.GoldenChara, "chara.face.eyes", 4);
            outer.Set(Kit.GoldenItem, "transform.pos", new Vec3(1, 0, 0));
            var joinPoint = Kit.Bytes(s);
            long version = s.Commands.Version;
            var j = s.Commands.BeginOrJoin("Pose");
            j.Set(Kit.GoldenChara, "chara.face.mouth", 3);
            j.Set(Kit.GoldenChara, "chara.face.eyes", 7);
            Kit.Ok(t, j.Execute(new RemoveNodesCommand(new[] { Kit.GoldenCamera })), "joined structural step");
            j.Set(null, "effects.bloom.intensity", 9f);
            s.Selection.Set(new[] { Kit.GoldenChara });
            t.Check(s.Model.Find(Kit.GoldenCamera) == null, "the joined removal applied");
            rec.Clear();
            j.Cancel();
            t.Check(Kit.Same(joinPoint, Kit.Bytes(s)), "joined cancel restores the bytes of the join point (" + Kit.Diff(joinPoint, Kit.Bytes(s)) + ")");
            t.Check(rec.Batches.Count == 1 && rec.Batches[0].Origin == ChangeOrigin.Cancel, "one Cancel batch");
            t.Check(s.Commands.Version > version, "the revert bumps the version");
            t.Check(s.Selection.Count == 1 && s.Selection.Contains(Kit.GoldenItem), "the selection of the join point comes back");
            t.Check(!j.IsOpen && outer.IsOpen && s.Commands.ActiveTransaction == outer, "joined cancel leaves the outer open");
            t.Equal(2, outer.StepCount, "joined cancel removes only its own steps");
            t.Equal(4, Chara(s).Face.Eyes, "the earlier step stays");
            j.Cancel();
            t.Check(Kit.Same(joinPoint, Kit.Bytes(s)), "a second cancel does nothing");

            // dispose without commit = cancel
            using (var j2 = s.Commands.BeginOrJoin("Disposed")) j2.Set(Kit.GoldenChara, "chara.face.eyes", 9);
            t.Check(Kit.Same(joinPoint, Kit.Bytes(s)) && outer.IsOpen, "dispose without commit cancels the join only");

            outer.Set(Kit.GoldenChara, "chara.face.mouth", 1);
            var after = Kit.Bytes(s);
            Kit.Ok(t, outer.Commit(), "outer commit after a cancelled join");
            t.Equal(1, s.Commands.UndoList.Count, "one entry");
            Kit.Ok(t, s.Commands.Undo(), "undo");
            t.Check(Kit.Same(before, Kit.Bytes(s)), "undo restores the bytes before the outer");
            Kit.Ok(t, s.Commands.Redo(), "redo");
            t.Check(Kit.Same(after, Kit.Bytes(s)), "redo gives the outer's steps without the cancelled ones");
            t.Check(s.Model.Find(Kit.GoldenCamera) != null, "the node the cancelled join removed is back");
            t.Equal(0, rec.Forgotten.Count, "nothing forgotten");
            Kit.Valid(t, s, "joined cancel");

            // a node added inside a cancelled join is forgotten
            var s2 = Kit.GoldenSession(t);
            var rec2 = new EventRecorder(s2);
            var outer2 = s2.Commands.Begin("Batch");
            outer2.Set(Kit.GoldenChara, "chara.face.eyes", 4);
            var mark = Kit.Bytes(s2);
            var j3 = s2.Commands.BeginOrJoin("Add");
            var add = s2.AddFolder();
            Kit.Ok(t, add, "add inside a join (Execute joins the open transaction)");
            t.Check(s2.Model.Find(add.NewId) != null, "the folder exists");
            j3.Cancel();
            t.Check(s2.Model.Find(add.NewId) == null && Kit.Same(mark, Kit.Bytes(s2)), "joined cancel removes the added folder");
            t.Check(rec2.Forgotten.Contains(add.NewId), "the added folder is forgotten");
            outer2.Cancel();
            t.Equal(0, s2.Commands.UndoList.Count, "outer cancel records nothing");
            Kit.Valid(t, s2, "joined cancel of an add");
        }

        static void Barrier(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var outer = s.Commands.Begin("Batch");
            outer.Set(Kit.GoldenChara, "chara.face.eyes", 4);
            outer.Set(Kit.GoldenChara, "chara.face.eyes", 5);
            t.Equal(1, outer.StepCount, "sets on one path collapse before the join");
            var joinPoint = Kit.Bytes(s);
            var j = s.Commands.BeginOrJoin("Join");
            j.Set(Kit.GoldenChara, "chara.face.eyes", 6);
            t.Equal(2, outer.StepCount, "a set inside the join does not merge into a step before it");
            j.Set(Kit.GoldenChara, "chara.face.eyes", 7);
            t.Equal(2, outer.StepCount, "sets on one path collapse inside the join");
            t.Equal(1, j.StepCount, "one joined step");
            j.Cancel();
            t.Check(Kit.Same(joinPoint, Kit.Bytes(s)), "cancel across the barrier is exact");
            t.Equal(5, Chara(s).Face.Eyes, "the value before the join");
            var j2 = s.Commands.BeginOrJoin("Join 2");
            j2.Set(Kit.GoldenChara, "chara.face.eyes", 8);
            Kit.Ok(t, j2.Commit(), "joined commit");
            outer.Set(Kit.GoldenChara, "chara.face.eyes", 9);
            t.Equal(2, outer.StepCount, "after the joined commit the barrier is gone (the later set merges)");
            Kit.Ok(t, outer.Commit(), "outer commit");
            Kit.Ok(t, s.Commands.Undo(), "undo");
            Kit.Ok(t, s.Commands.Redo(), "redo");
            t.Equal(9, Chara(s).Face.Eyes, "redo replays the last value");
            Kit.Valid(t, s, "barrier");
        }

        static void Nested(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var before = Kit.Bytes(s);
            var outer = s.Commands.Begin("Batch");
            outer.Set(Kit.GoldenChara, "chara.face.eyes", 4);
            var p1 = Kit.Bytes(s);
            var a = s.Commands.BeginOrJoin("A");
            a.Set(Kit.GoldenChara, "chara.face.mouth", 2);
            var p2 = Kit.Bytes(s);
            var b = s.Commands.BeginOrJoin("B");
            t.Check(b.IsJoined && b.IsOpen && a.IsOpen, "a nested join");
            b.Set(Kit.GoldenChara, "chara.face.mouth", 3);
            t.Equal(2, a.StepCount, "the outer join counts the nested steps");
            t.Equal(1, b.StepCount, "the nested join counts its own");
            b.Cancel();
            t.Check(Kit.Same(p2, Kit.Bytes(s)) && a.IsOpen, "nested cancel restores its join point; the enclosing join stays open");
            var c = s.Commands.BeginOrJoin("C");
            c.Set(Kit.GoldenChara, "chara.face.eyebrow", 2);
            Kit.Ok(t, c.Commit(), "nested commit");
            t.Equal(2, a.StepCount, "the committed nested step belongs to the enclosing join");
            a.Cancel();
            t.Check(Kit.Same(p1, Kit.Bytes(s)), "the enclosing cancel also reverts the committed nested step");
            // commit of an enclosing join closes the joins nested in it
            var d = s.Commands.BeginOrJoin("D");
            d.Set(Kit.GoldenChara, "chara.face.mouth", 1);
            var e = s.Commands.BeginOrJoin("E");
            e.Set(Kit.GoldenChara, "chara.face.eyebrow", 1);
            var p3 = Kit.Bytes(s);
            Kit.Ok(t, d.Commit(), "enclosing commit with a nested join open");
            t.Check(!e.IsOpen && !d.IsOpen, "the nested join closed with it");
            e.Cancel();
            t.Check(Kit.Same(p3, Kit.Bytes(s)), "a closed nested join cancels nothing");
            Kit.Ok(t, outer.Commit(), "outer commit");
            t.Equal(1, s.Commands.UndoList.Count, "nested joins: one entry");
            Kit.Ok(t, s.Commands.Undo(), "undo");
            t.Check(Kit.Same(before, Kit.Bytes(s)), "undo restores the bytes");
            Kit.Ok(t, s.Commands.Redo(), "redo");
            t.Check(Kit.Same(p3, Kit.Bytes(s)), "redo restores the committed state");
            Kit.Valid(t, s, "nested joins");
        }

        static void OuterCloses(TestRunner t)
        {
            // outer cancel with a committed join reverts everything; open joins close
            var s = Kit.GoldenSession(t);
            var before = Kit.Bytes(s);
            var outer = s.Commands.Begin("Batch");
            outer.Set(Kit.GoldenChara, "chara.face.eyes", 4);
            var j = s.Commands.BeginOrJoin("J");
            j.Set(Kit.GoldenChara, "chara.face.mouth", 2);
            Kit.Ok(t, j.Commit(), "joined commit");
            var k = s.Commands.BeginOrJoin("K");
            k.Set(Kit.GoldenChara, "chara.face.eyebrow", 2);
            outer.Cancel();
            t.Check(Kit.Same(before, Kit.Bytes(s)), "outer cancel reverts the joined steps");
            t.Check(!k.IsOpen && s.Commands.ActiveTransaction == null, "outer cancel closes the open join");
            Kit.Fails(t, k.Commit(), CommandCodes.Busy, "commit of a join whose transaction closed");
            k.Cancel();
            t.Check(Kit.Same(before, Kit.Bytes(s)), "cancel of a closed join does nothing");
            t.Equal(0, s.Commands.UndoList.Count, "nothing recorded");

            // a new scene closes the open transaction and its joins without reverting (SceneSession.NewScene → Reset)
            var outer2 = s.Commands.Begin("Batch");
            outer2.Set(Kit.GoldenChara, "chara.face.eyes", 4);
            var j2 = s.Commands.BeginOrJoin("J2");
            s.NewScene();
            t.Check(!outer2.IsOpen && !j2.IsOpen && s.Commands.ActiveTransaction == null, "NewScene closes the transaction and its joins");
            var after = s.Commands.BeginOrJoin("After");
            t.Check(after.IsOpen && !after.IsJoined, "afterwards BeginOrJoin opens a new transaction");
            var f = s.AddFolder();
            Kit.Ok(t, after.Commit(), "commit");
            t.Equal(1, s.Commands.UndoList.Count, "one entry after the new scene");
            t.Check(s.Model.Find(f.NewId) != null, "the folder exists");
            Kit.Valid(t, s, "outer closes");
        }

        static void Busy(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var outer = s.Commands.Begin("Batch");
            var nested = s.Commands.Begin("Nested");
            t.Check(!nested.IsOpen && nested.Failure?.Code == CommandCodes.Busy, "Begin while a transaction is open → busy");
            var j = s.Commands.BeginOrJoin("J");
            t.Check(j.IsOpen && j.IsJoined, "BeginOrJoin while a transaction is open joins it");
            Kit.Fails(t, s.Commands.Undo(), CommandCodes.Busy, "undo while a join is open");
            j.Commit();
            outer.Cancel();

            // from an event handler: busy, as Begin
            Transaction fromHandler = null;
            bool once = false;
            s.Events.Changed += b =>
            {
                if (once) return;
                once = true;
                fromHandler = s.Commands.BeginOrJoin("handler");
            };
            s.Set(Kit.GoldenChara, "chara.face.eyes", 3);
            t.Check(fromHandler != null && !fromHandler.IsOpen && fromHandler.Failure?.Code == CommandCodes.Busy, "BeginOrJoin from a handler → busy");
            once = false;
            fromHandler = null;
            var tx = s.Commands.Begin("Open");
            tx.Set(Kit.GoldenChara, "chara.face.eyes", 4);
            t.Check(fromHandler != null && !fromHandler.IsOpen && fromHandler.Failure?.Code == CommandCodes.Busy, "BeginOrJoin from a handler during a transaction → busy");
            tx.Cancel();
            var list = new List<string>();
            foreach (var e in s.Commands.UndoList) list.Add(e.Label);
            t.Equal(1, list.Count, "only the handler test's set is recorded");
            Kit.Valid(t, s, "busy");
        }
    }
}
