// M3: one batch per operation, change order (removal top first then pre-order,
// additions in pre-order), origins, buffered delivery (handlers see a model that passes Validate), handler
// isolation, bus calls from handlers answering busy, the thread check, and the other events.
using System;
using System.Collections.Generic;
using System.Threading;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_Events
    {
        public static void Run(TestRunner t)
        {
            Batches(t);
            Order(t);
            Origins(t);
            Delivery(t);
            Isolation(t);
            Reentrancy(t);
            Threads(t);
            Lifecycle(t);
            Filters(t);
        }

        static void Batches(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var rec = new EventRecorder(s);
            s.Set(Kit.GoldenChara, "chara.face.eyes", 3);
            t.Equal(1, rec.Batches.Count, "set: one batch");
            rec.Clear();
            s.Delete(new[] { Kit.GoldenChara });
            t.Equal(1, rec.Batches.Count, "delete of a subtree: one batch");
            rec.Clear();
            s.Commands.Undo();
            t.Equal(1, rec.Batches.Count, "undo: one batch");
            rec.Clear();
            s.Commands.Redo();
            t.Equal(1, rec.Batches.Count, "redo: one batch");
            rec.Clear();
            s.Set(Kit.GoldenFolder, "chara.face.eyes", 1);
            s.Set(Kit.GoldenFolder, "name", s.Model.Find(Kit.GoldenFolder).Doc.Name);
            t.Equal(0, rec.Batches.Count, "errors and NoOps deliver nothing");
            rec.Clear();
            s.SetMap("test.mod#5");
            t.Check(rec.Batches.Count == 1 && rec.Batches[0].Changes.Count == 3, "composite: one batch with its three changes");
            rec.Clear();
            using (var tx = s.Commands.Begin("tx"))
            {
                tx.Set(Kit.GoldenFolder, "transform.pos", new Vec3(1, 0, 0));
                tx.Set(Kit.GoldenFolder, "transform.pos", new Vec3(2, 0, 0));
                tx.Commit();
            }
            t.Check(rec.Batches.Count == 3 && rec.Batches[0].Origin == ChangeOrigin.Preview && rec.Batches[2].Origin == ChangeOrigin.Command,
                "transaction: one Preview batch per update, one batch at commit");
        }

        static List<string> Ids(SceneChangeBatch b, SceneChangeKind k)
        {
            var l = new List<string>();
            foreach (var c in b.Changes) if (c.Kind == k) l.Add(c.NodeId);
            return l;
        }

        static void Order(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var rec = new EventRecorder(s);
            s.Delete(new[] { Kit.GoldenFolder, Kit.GoldenChara });
            var removed = Ids(rec.Batches[0], SceneChangeKind.NodeRemoved);
            var expect = new[] { Kit.GoldenChara, Kit.GoldenCamera, Kit.GoldenItem, Kit.GoldenLight, Kit.GoldenFolder, Kit.GoldenRoute, Kit.GoldenOpaque, Kit.GoldenOpaqueChild };
            t.Equal(string.Join(",", expect), string.Join(",", removed), "removal: each top first, then its subtree in pre-order");
            rec.Clear();
            s.Commands.Undo();
            var added = Ids(rec.Batches[0], SceneChangeKind.NodeAdded);
            t.Equal(8, added.Count, "undo re-adds 8 nodes");
            t.Check(added.Count > 3 && added[0] == Kit.GoldenChara && added[1] == Kit.GoldenCamera && added[2] == Kit.GoldenItem && added[3] == Kit.GoldenLight, "additions in pre-order");
            rec.Clear();
            var r = s.Duplicate(new[] { Kit.GoldenChara });
            var dup = Ids(rec.Batches[0], SceneChangeKind.NodeAdded);
            t.Check(dup.Count == 4 && dup[0] == r.NewId, "duplicate: the copy first, then its subtree");
            rec.Clear();
            s.Move(Kit.GoldenItem, null, null, 0);
            var mv = rec.Batches[0].Changes[0];
            t.Check(mv.Kind == SceneChangeKind.NodeMoved && mv.NodeId == Kit.GoldenItem && mv.OldParentId == Kit.GoldenChara && mv.OldPoint == "N_Hand_R" && mv.OldIndex == 0,
                "NodeMoved carries the old parent, point and index");
            rec.Clear();
            s.SetAppearance(Kit.GoldenChara, new CharaAppearance { Card = Kit.Card("x", "x.png", 1) });
            t.Check(rec.Batches[0].Changes[0].Kind == SceneChangeKind.AppearanceChanged, "appearance change record");
        }

        static void Origins(TestRunner t)
        {
            var s = Kit.GoldenSession(t, withSubtree: false);
            var rec = new EventRecorder(s);
            s.Set(Kit.GoldenChara, "chara.face.eyes", 3);
            s.Commands.Undo();
            s.Commands.Redo();
            var tx = s.Commands.Begin("o");
            tx.Set(Kit.GoldenChara, "chara.face.eyes", 4);
            tx.Cancel();
            s.WriteSilently(ChangeOrigin.Capture, w => w.Set(Kit.GoldenChara, "chara.anime.time", 0.75f));
            s.SetAppearance(Kit.GoldenChara, new CharaAppearance { Card = Kit.Card("o", "o.png", 2) });
            rec.Clear();
            s.WriteSilently(ChangeOrigin.Capture, w => w.Set(Kit.GoldenChara, "chara.state.accessories", new SortedDictionary<string, bool>(StringComparer.Ordinal)));
            t.Check(s.NormalizeAccessories(Kit.GoldenChara), "normalisation after a capture that broke the map");
            t.Check(rec.Batches.Count == 2 && rec.Batches[1].Origin == ChangeOrigin.Normalize, "normalisation batch origin Normalize");
            var seq = new List<ChangeOrigin>();
            var s2 = Kit.GoldenSession(t);
            s2.Events.Changed += b => seq.Add(b.Origin);
            s2.Set(Kit.GoldenChara, "chara.face.eyes", 3);
            s2.Commands.Undo();
            s2.Commands.Redo();
            var tx2 = s2.Commands.Begin("o");
            tx2.Set(Kit.GoldenChara, "chara.face.eyes", 4);
            tx2.Cancel();
            s2.WriteSilently(ChangeOrigin.Capture, w => w.Set(Kit.GoldenChara, "chara.anime.time", 0.75f));
            t.Equal("Command,Undo,Redo,Preview,Cancel,Capture", string.Join(",", seq), "origins in order");
            long v = s2.Commands.Version;
            s2.WriteSilently(ChangeOrigin.Capture, w => w.Set(Kit.GoldenChara, "chara.anime.time", 0.75f));
            t.Equal(v, s2.Commands.Version, "a silent write of an equal value changes nothing");
            t.Check(!s2.Commands.CanRedo && s2.Commands.UndoList.Count == 1, "silent writes stay out of history");
        }

        static void Delivery(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            int invalid = 0, batches = 0;
            s.Events.Changed += b =>
            {
                batches++;
                try { s.Model.Validate(); }
                catch (Exception) { invalid++; }
                // the model is complete when handlers run: every added node is findable, every removed one gone
                foreach (var c in b.Changes)
                {
                    if (c.Kind == SceneChangeKind.NodeAdded && s.Model.Find(c.NodeId) == null) invalid++;
                    if (c.Kind == SceneChangeKind.NodeRemoved && s.Model.Find(c.NodeId) != null) invalid++;
                }
            };
            s.Delete(new[] { Kit.GoldenFolder });
            s.Commands.Undo();
            s.Duplicate(new[] { Kit.GoldenChara, Kit.GoldenRoute });
            s.Move(Kit.GoldenItem, Kit.GoldenFolder, null, 0);
            s.Commands.Undo();
            using (var tx = s.Commands.Begin("t"))
            {
                tx.Execute(new RemoveNodesCommand(new[] { Kit.GoldenCamera }));
                tx.Set(Kit.GoldenChara, "visible", false);
            }
            t.Check(batches >= 7 && invalid == 0, "handlers see a model that passes Validate (" + batches + " batches, " + invalid + " problems)");
        }

        static void Isolation(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var logs = new List<string>();
            var prev = SceneEvents.Log;
            SceneEvents.Log = m => logs.Add(m);
            try
            {
                int second = 0;
                s.Events.Changed += b => throw new InvalidOperationException("handler failure");
                s.Events.Changed += b => second++;
                s.Set(Kit.GoldenChara, "chara.face.eyes", 3);
                s.Set(Kit.GoldenChara, "chara.face.eyes", 4);
                t.Equal(2, second, "a throwing handler does not stop the next one");
                t.Equal(2, s.Events.HandlerFaults, "both faults counted");
                t.Equal(1, logs.Count, "logged once per (handler, exception type)");
                t.Equal(4, ((CharaNodeState)s.Model.Find(Kit.GoldenChara).State).Face.Eyes, "the model is unaffected");
                Kit.Valid(t, s, "isolation");
            }
            finally { SceneEvents.Log = prev; }
        }

        static void Reentrancy(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            CommandResult inner = null, undo = null;
            Transaction tx = null;
            Exception silent = null;
            bool once = false;
            s.Events.Changed += b =>
            {
                if (once) return;
                once = true;
                inner = s.Set(Kit.GoldenChara, "chara.face.mouth", 2);
                undo = s.Commands.Undo();
                tx = s.Commands.Begin("inner");
                try { s.WriteSilently(ChangeOrigin.Capture, w => w.Set(Kit.GoldenChara, "chara.anime.time", 0.5f)); }
                catch (InvalidOperationException e) { silent = e; }
            };
            s.Set(Kit.GoldenChara, "chara.face.eyes", 3);
            Kit.Fails(t, inner, CommandCodes.Busy, "bus call from a handler");
            Kit.Fails(t, undo, CommandCodes.Busy, "undo from a handler");
            t.Check(tx != null && !tx.IsOpen && tx.Failure?.Code == CommandCodes.Busy, "Begin from a handler → busy");
            t.Check(silent != null, "silent write from a Changed handler throws");
            t.Equal(1, s.Commands.UndoList.Count, "only the outer set was recorded");
            var s2 = Kit.GoldenSession(t);
            CommandResult fromHistory = null;
            s2.Events.HistoryChanged += a => { if (fromHistory == null) fromHistory = s2.AddFolder(); };
            s2.Set(Kit.GoldenChara, "chara.face.eyes", 3);
            Kit.Fails(t, fromHistory, CommandCodes.Busy, "bus call from a HistoryChanged handler");
            // a Saving handler may capture
            var s3 = Kit.GoldenSession(t);
            s3.Events.Saving += () => s3.WriteSilently(ChangeOrigin.Capture, w => w.Set(Kit.GoldenChara, "chara.anime.time", 0.125f));
            s3.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            t.Equal(0.125f, ((CharaNodeState)s3.Model.Find(Kit.GoldenChara).State).Anime.Time, "Saving handlers capture silently");
        }

        static void Threads(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            Exception caught = null, caughtUndo = null;
            var th = new Thread(() =>
            {
                try { s.Set(Kit.GoldenChara, "chara.face.eyes", 3); }
                catch (Exception e) { caught = e; }
                try { s.Commands.Undo(); }
                catch (Exception e) { caughtUndo = e; }
            });
            th.Start();
            th.Join();
            t.Check(caught is InvalidOperationException, "a set from another thread throws InvalidOperationException");
            t.Check(caughtUndo is InvalidOperationException, "undo from another thread throws");
            t.Equal(0, s.Commands.UndoList.Count, "nothing applied from the other thread");
        }

        static void Lifecycle(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var rec = new EventRecorder(s);
            s.Selection.Set(new[] { Kit.GoldenChara });
            t.Equal(1, rec.Selection, "selection change raised");
            s.Set(Kit.GoldenChara, "chara.face.eyes", 3);
            t.Check(rec.History >= 1, "history change raised");
            rec.Clear();
            s.NewScene();
            t.Check(rec.Log.IndexOf("cleared") == 0 && rec.Log[rec.Log.Count - 1] == "loaded", "NewScene: Cleared first, Loaded last (" + string.Join(",", rec.Log) + ")");
            t.Check(rec.Selection == 1 && s.Selection.Count == 0, "the selection empties");
            rec.Clear();
            s.Bind(Kit.GoldenContainer(t, true));
            t.Check(rec.Log.IndexOf("cleared") == 0 && rec.Log[rec.Log.Count - 1] == "loaded", "Bind: Cleared first, Loaded last");
            rec.Clear();
            s.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            t.Check(rec.Saving == 1 && rec.Saved == 1 && rec.Log[0] == "saving" && rec.Log[rec.Log.Count - 1] == "saved", "Saving and Saved around ToBytes");
            rec.Clear();
            var r = s.Save(System.IO.Path.Combine(t.TempDir, "events.hs2nscene"), Kit.FixedSave(ThumbnailMode.Placeholder));
            t.Check(r.Ok && rec.Saving == 1 && rec.Saved == 1, "Saving and Saved around Save");
            t.Check(!s.Commands.IsDirty, "saved: not dirty");
            s.Set(Kit.GoldenChara, "chara.face.eyes", 1);
            t.Check(s.Commands.IsDirty, "an edit makes it dirty");

            // a node held by history is forgotten before a reload that brings the same id back
            var s2 = Kit.Session(2);
            var r2 = new EventRecorder(s2);
            string x = s2.AddFolder().NewId;
            var bytes = s2.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            s2.Delete(new[] { x });
            r2.Clear();
            string atLoaded = null;
            s2.Events.Loaded += a => atLoaded = s2.Model.Find(x)?.Id;
            s2.Load(bytes);
            int fi = r2.Log.IndexOf("forgotten:" + x), ci = r2.Log.IndexOf("cleared"), li = r2.Log.IndexOf("loaded");
            t.Check(ci == 0 && fi > ci && fi < li && r2.Forgotten.Count == 1, "Cleared, then the old model's forgotten ids, then Loaded (" + string.Join(",", r2.Log) + ")");
            t.Check(atLoaded == x && s2.Model.Find(x) != null, "the reloaded node with the same id is live");
        }

        static void Filters(TestRunner t)
        {
            var c = new SceneChange(SceneChangeKind.NodeChanged, "a", "chara.face.eyes");
            t.Check(c.Touches("a", "chara.face") && c.Touches("a", "chara") && c.Touches("a", null) && c.Touches("a", "chara.face.eyes"), "a field change touches its prefixes");
            t.Check(!c.Touches("a", "chara.faces") && !c.Touches("b", "chara.face") && !c.Touches(null, "chara.face"), "not other paths, nodes or the scene");
            var w = new SceneChange(SceneChangeKind.NodeChanged, "a", "chara.face");
            t.Check(w.Touches("a", "chara.face.eyes"), "a write of an object touches its members");
            var m = new SceneChange(SceneChangeKind.NodeMoved, "a");
            t.Check(m.Touches("a", "transform"), "structural changes touch every path of the node");
            var sc = new SceneChange(SceneChangeKind.SceneChanged, null, "effects.bloom.intensity");
            t.Check(sc.Touches(null, "effects") && !sc.Touches(null, "map"), "scene changes by scene path");
            t.Check(FieldPath.IsPrefixOf("chara.hands", "chara.hands[1]") && FieldPath.IsPrefixOf("chara.fk.bones", "chara.fk.bones[\"x\"]") && !FieldPath.IsPrefixOf("chara.hand", "chara.hands"),
                "prefixes stop at step boundaries");
            var b = new SceneChangeBatch(ChangeOrigin.Command, CommandSource.User, new[] { c, m });
            t.Check(b.Touches("a", "chara.face") && b.IsStructural, "batch filters");
        }
    }
}
