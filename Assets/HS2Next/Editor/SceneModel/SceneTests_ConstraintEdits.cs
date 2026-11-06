// CM7: the scene's constraints on the bus (ConstraintEditCommand). Each editing operation (add, an entry changed, remove, move, clear) is one
// history entry with its label and raises the version; undo and redo restore the list byte-equal (its JSON); consecutive edits with one
// merge key merge into the first old and the last new, others do not; `undo: false` applies, records nothing and keeps the history; inside
// a transaction (a client batch) the edits join its one entry; ids, limits and component sets are refused with bad_args and leave nothing,
// while a problem an entry came with does not block an edit; a no-op edit is NoOp; the change record is SceneChanged("constraints"); a list
// in the model is never changed in place and an entry reverts by id; a save writes the list (revision 4), a load, NewScene and Bind replace
// it, and CF8 on a session: the last entry removed gives the bytes of before the first add. Synthetic ids only.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_ConstraintEdits
    {
        static string Id(int n) => n.ToString("x32");
        static readonly string NodeA = Id(0xa1), NodeB = Id(0xa2);

        static string Json(SceneSession s) => s.Model.Constraints?.ToJson();

        static ConstraintEntry Entry(int n, params string[] components) => new ConstraintEntry
        {
            Id = Id(n),
            Source = new ConstraintEnd(NodeA),
            Target = new ConstraintEnd(NodeB),
            Components = components.Length > 0 ? components.ToList() : new List<string> { ConstraintComponents.Position },
        };

        static ConstraintList List(params ConstraintEntry[] entries) => new ConstraintList(entries);

        // The list as it stands with one more entry, a new list of the same objects.
        static ConstraintList With(SceneSession s, ConstraintEntry e)
        {
            var l = new ConstraintList(s.Model.Constraints?.Entries ?? new List<ConstraintEntry>());
            l.Entries.Add(e);
            return l;
        }

        static ConstraintEntry EntryOf(SceneSession s, int i) => s.Model.Constraints.Entries[i].Clone();

        static CommandResult Run(SceneSession s, ISceneCommand c, string mergeKey = null, HistoryMode mode = HistoryMode.Record) =>
            s.Commands.Execute(c, new ExecuteOptions { Record = mode, MergeKey = mergeKey, Source = CommandSource.Bridge });

        public static void Run(TestRunner t)
        {
            Entries(t);
            Merges(t);
            NotRecordedAndBatch(t);
            Refusals(t);
            Shared(t);
            FileAndSession(t);
        }

        // ---------- one entry per operation; undo / redo byte-equal ----------

        static void Entries(TestRunner t)
        {
            var s = Kit.Session();
            var rec = new EventRecorder(s);
            t.Check(s.Model.Constraints == null, "CM7: a new scene has no list");
            var states = new List<string> { null };
            var labels = new List<string>();
            long v0 = s.Commands.Version;

            void Step(ISceneCommand c, string label)
            {
                long v = s.Commands.Version;
                int n = s.Commands.UndoList.Count;
                rec.Clear();
                var r = Run(s, c);
                Kit.Ok(t, r, "CM7: " + label);
                t.Check(s.Commands.UndoList.Count == n + 1 && s.Commands.UndoList[0].Label == label, "CM7: " + label + " is one entry labelled «" + label + "»");
                t.Check(s.Commands.Version == v + 1, "CM7: " + label + " raises the version by one");
                t.Check(rec.Batches.Count == 1 && rec.Batches[0].Changes.Count == 1 && rec.Batches[0].Changes[0].Kind == SceneChangeKind.SceneChanged &&
                        rec.Batches[0].Changes[0].Path == ConstraintEditCommand.ChangePath, "CM7: " + label + " reports one SceneChanged(\"constraints\")");
                states.Add(Json(s));
                labels.Add(label);
            }

            Step(ConstraintEditCommand.ReplaceList(List(Entry(1)), ConstraintEditCommand.LabelAdd), ConstraintEditCommand.LabelAdd);
            t.Check(s.Model.Constraints != null && s.Model.Constraints.Count == 1 && s.Commands.IsDirty, "CM7: added, the scene is modified");
            Step(ConstraintEditCommand.ReplaceList(With(s, Entry(2, ConstraintComponents.Rotation)), ConstraintEditCommand.LabelAdd), ConstraintEditCommand.LabelAdd);
            Step(ConstraintEditCommand.ReplaceList(With(s, Entry(3, ConstraintComponents.Parent)), ConstraintEditCommand.LabelAdd), ConstraintEditCommand.LabelAdd);

            var e = EntryOf(s, 0);
            e.On = false;
            e.Label = "Slide";
            Step(ConstraintEditCommand.ReplaceEntry(e), ConstraintEditCommand.LabelEdit);

            e = EntryOf(s, 1);
            e.Offset.Rot = new Vec3(0f, 45f, 0f);
            Step(ConstraintEditCommand.ReplaceEntry(e, ConstraintEditCommand.LabelTakeOffset), ConstraintEditCommand.LabelTakeOffset);

            var moved = new ConstraintList(s.Model.Constraints.Entries.AsEnumerable().Reverse());
            Step(ConstraintEditCommand.ReplaceList(moved, ConstraintEditCommand.LabelReorder), ConstraintEditCommand.LabelReorder);
            t.Equal(Id(3) + "," + Id(2) + "," + Id(1), string.Join(",", s.Model.Constraints.Entries.Select(x => x.Id)), "CM7: reordered");

            var fewer = new ConstraintList(s.Model.Constraints.Entries.Skip(1));
            Step(ConstraintEditCommand.ReplaceList(fewer, ConstraintEditCommand.LabelRemove), ConstraintEditCommand.LabelRemove);
            t.Equal(2, s.Model.Constraints.Count, "CM7: removed");

            Step(ConstraintEditCommand.Clear(), ConstraintEditCommand.LabelClear);
            t.Check(s.Model.Constraints == null, "CM7: cleared, the scene has no list");
            t.Equal(v0 + labels.Count, s.Commands.Version, "CM7: one version step per edit");

            // Undo walks back through every state byte-equal, redo forward.
            bool undoOk = true, redoOk = true;
            for (int i = labels.Count - 1; i >= 0; i--)
            {
                rec.Clear();
                Kit.Ok(t, s.Commands.Undo(), "CM7: undo " + labels[i]);
                if (Json(s) != states[i]) { undoOk = false; t.Info("undo " + labels[i] + " differs"); }
                t.Check(rec.Batches.Count == 1 && rec.Batches[0].Changes.Count == 1 && rec.Batches[0].Changes[0].Path == ConstraintEditCommand.ChangePath, "CM7: undo " + labels[i] + " reports SceneChanged(\"constraints\")");
            }
            t.Check(undoOk && s.Model.Constraints == null, "CM7: undo restores each earlier list byte-equal (back to none)");
            for (int i = 0; i < labels.Count; i++)
            {
                Kit.Ok(t, s.Commands.Redo(), "CM7: redo " + labels[i]);
                if (Json(s) != states[i + 1]) { redoOk = false; t.Info("redo " + labels[i] + " differs"); }
            }
            t.Check(redoOk, "CM7: redo restores each later list byte-equal");

            // The history size is twice the JSON length of before and after.
            s.Commands.Undo();
            s.Commands.Undo();
            Kit.Ok(t, s.Commands.Redo(), "CM7: redo «Remove constraint»");
            var top = s.Commands.UndoList[0];
            long expect = ConstraintEditCommand.BaseBytes + 2L * (states[labels.Count - 2].Length + states[labels.Count - 1].Length);
            t.Check(top.Label == ConstraintEditCommand.LabelRemove && top.EstimatedBytes == expect, "CM7: entry size = 256 + 2 × (before + after JSON lengths) (" + top.EstimatedBytes + ", expected " + expect + ")");
            var one = EntryOf(s, 0);
            string oldEntry = one.ToJson();
            one.Label = "a longer label than before";
            Kit.Ok(t, Run(s, ConstraintEditCommand.ReplaceEntry(one)), "CM7: an entry edit");
            top = s.Commands.UndoList[0];
            expect = ConstraintEditCommand.BaseBytes + 2L * (oldEntry.Length + one.ToJson().Length);
            t.Check(top.EstimatedBytes == expect, "CM7: an entry edit's size counts the entry (" + top.EstimatedBytes + ", expected " + expect + ")");

            // A no-op edit: nothing recorded, the version unchanged.
            long vn = s.Commands.Version;
            int un = s.Commands.UndoList.Count;
            Kit.NoOp(t, Run(s, ConstraintEditCommand.ReplaceList(s.Model.Constraints.Clone())), "CM7: an edit to the same list");
            Kit.NoOp(t, Run(s, ConstraintEditCommand.ReplaceEntry(EntryOf(s, 0))), "CM7: an edit to the same entry");
            t.Check(s.Commands.Version == vn && s.Commands.UndoList.Count == un, "CM7: a no-op edit records nothing and keeps the version");
            var none = Kit.Session(2);
            Kit.NoOp(t, Run(none, ConstraintEditCommand.Clear()), "CM7: clearing no list");
            Kit.NoOp(t, Run(none, ConstraintEditCommand.ReplaceList(new ConstraintList())), "CM7: an empty list on none");

            // An empty list is no list.
            var s2 = Kit.Session(3);
            Kit.Ok(t, Run(s2, ConstraintEditCommand.ReplaceList(List(Entry(1)))), "CM7: a list");
            Kit.Ok(t, Run(s2, ConstraintEditCommand.ReplaceList(new ConstraintList(), ConstraintEditCommand.LabelRemove)), "CM7: its last entry removed");
            t.Check(s2.Model.Constraints == null, "CM7: the model holds null, never an empty list");

            // The normal form: components in writing order, an unused offset at its identity; what the caller gave is not changed.
            var given = Entry(9, ConstraintComponents.Scale, ConstraintComponents.Position);
            given.Offset.Rot = new Vec3(1f, 2f, 3f);
            string text = given.ToJson();
            Kit.Ok(t, Run(s2, ConstraintEditCommand.ReplaceList(List(given))), "CM7: a list with an unused offset");
            var stored = s2.Model.Constraints.Entries[0];
            t.Check(stored.Components[0] == ConstraintComponents.Position && stored.Components[1] == ConstraintComponents.Scale && stored.Offset.Rot.Equals(Vec3.Zero), "CM7: the list is stored in normal form");
            t.Check(given.ToJson() == text && given.Components[0] == ConstraintComponents.Scale, "CM7: the caller's entry is not changed");
            var unused = EntryOf(s2, 0);
            unused.Offset.Rot = new Vec3(4f, 0f, 0f);
            Kit.NoOp(t, Run(s2, ConstraintEditCommand.ReplaceEntry(unused)), "CM7: an entry edit of an unused offset alone changes nothing");
        }

        // ---------- merges ----------

        static void Merges(TestRunner t)
        {
            var s = Kit.Session();
            Kit.Ok(t, Run(s, ConstraintEditCommand.ReplaceList(List(Entry(1), Entry(2)), ConstraintEditCommand.LabelAdd)), "CM7 merge: a list");
            string before = Json(s);
            int n = s.Commands.UndoList.Count;
            // A drag: forty moves of one weight with one merge key: one entry, the first old and the last new.
            for (int i = 1; i <= 40; i++)
            {
                var e = EntryOf(s, 0);
                e.Weight = 1f - i * 0.01f;
                Kit.Ok(t, Run(s, ConstraintEditCommand.ReplaceEntry(e), "weight:" + Id(1)), "CM7 merge: step " + i);
            }
            string dragged = Json(s);
            t.Check(s.Commands.UndoList.Count == n + 1 && s.Commands.UndoList[0].Label == ConstraintEditCommand.LabelEdit && s.Model.Constraints.Entries[0].Weight == 1f - 40 * 0.01f,
                "CM7 merge: consecutive weight steps with one merge key are one entry");
            var size = s.Commands.UndoList[0].EstimatedBytes;
            Kit.Ok(t, s.Commands.Undo(), "CM7 merge: undo the drag");
            t.Equal(before, Json(s), "CM7 merge: its undo gives the first old");
            Kit.Ok(t, s.Commands.Redo(), "CM7 merge: redo the drag");
            t.Equal(dragged, Json(s), "CM7 merge: its redo gives the last new");
            t.Check(size > ConstraintEditCommand.BaseBytes && size < 4000, "CM7 merge: the merged entry's size counts two values, not forty (" + size + ")");

            // List edits with one merge key merge too.
            n = s.Commands.UndoList.Count;
            string beforeLists = Json(s);
            Run(s, ConstraintEditCommand.ReplaceList(new ConstraintList(s.Model.Constraints.Entries.AsEnumerable().Reverse()), ConstraintEditCommand.LabelReorder), "order");
            Run(s, ConstraintEditCommand.ReplaceList(new ConstraintList(s.Model.Constraints.Entries.AsEnumerable().Reverse().Concat(new[] { Entry(3) })), ConstraintEditCommand.LabelReorder), "order");
            t.Equal(n + 1, s.Commands.UndoList.Count, "CM7 merge: list edits with one key are one entry");
            string afterLists = Json(s);
            Kit.Ok(t, s.Commands.Undo(), "CM7 merge: undo them");
            t.Equal(beforeLists, Json(s), "CM7 merge: back to the first old");
            Kit.Ok(t, s.Commands.Redo(), "CM7 merge: redo them");
            t.Equal(afterLists, Json(s), "CM7 merge: on to the last new");

            // No merge: other merge keys, no merge key, another entry with the same key, a list edit after an entry edit.
            n = s.Commands.UndoList.Count;
            void Weight(int i, float w, string key)
            {
                var e = EntryOf(s, i);
                e.Weight = w;
                Run(s, ConstraintEditCommand.ReplaceEntry(e), key);
            }
            Weight(0, 0.1f, "k1");
            Weight(0, 0.2f, "k2");
            Weight(0, 0.3f, null);
            Weight(1, 0.4f, null);
            Weight(1, 0.5f, "same");
            Weight(0, 0.6f, "same");
            Run(s, ConstraintEditCommand.ReplaceList(With(s, Entry(4))), "same");
            t.Equal(n + 7, s.Commands.UndoList.Count, "CM7 merge: different keys, no key, another entry, a list edit: separate entries");

            // A merge key after a save does not merge into the saved entry.
            s.Commands.MarkSaved();
            Weight(0, 0.7f, "x");
            Weight(0, 0.8f, "x");
            t.Equal(n + 8, s.Commands.UndoList.Count, "CM7 merge: two edits with one key after a save merge with each other only");
        }

        // ---------- undo: false and batches ----------

        static void NotRecordedAndBatch(TestRunner t)
        {
            var s = Kit.Session();
            Kit.Ok(t, Run(s, ConstraintEditCommand.ReplaceList(List(Entry(1), Entry(2)), ConstraintEditCommand.LabelAdd)), "CM7 undo:false: a list");
            var e = EntryOf(s, 0);
            e.Label = "recorded";
            Kit.Ok(t, Run(s, ConstraintEditCommand.ReplaceEntry(e)), "CM7 undo:false: a recorded entry edit");
            int n = s.Commands.UndoList.Count;
            long v = s.Commands.Version;
            var quiet = EntryOf(s, 1);
            quiet.On = false;
            Kit.Ok(t, Run(s, ConstraintEditCommand.ReplaceEntry(quiet), null, HistoryMode.None), "CM7 undo:false: an entry edit");
            Kit.Ok(t, Run(s, ConstraintEditCommand.ReplaceList(new ConstraintList(s.Model.Constraints.Entries.AsEnumerable().Reverse())), null, HistoryMode.None), "CM7 undo:false: a list edit");
            t.Check(s.Model.Constraints.Entries[0].Id == Id(2) && !s.Model.Constraints.Entries[0].On, "CM7 undo:false: applied");
            t.Check(s.Commands.UndoList.Count == n && s.Commands.UndoList[0].Label == ConstraintEditCommand.LabelEdit, "CM7 undo:false: nothing recorded, the history kept (not structural)");
            t.Check(s.Commands.Version == v + 2 && s.Commands.IsDirty, "CM7 undo:false: the version still rises (the scene is modified)");
            Kit.Ok(t, s.Commands.Undo(), "CM7 undo:false: the earlier entry still undoes");
            t.Check(s.Model.Constraints.Find(Id(1)).Label == "", "CM7 undo:false: and restores its entry");
            t.Check(!s.Model.Constraints.Find(Id(2)).On && s.Model.Constraints.Entries[0].Id == Id(2), "CM7 undo:false: the edit of the other entry and the order stay: an entry reverts by id");
            Kit.Ok(t, s.Commands.Redo(), "CM7 undo:false: and redoes");
            t.Check(s.Model.Constraints.Find(Id(1)).Label == "recorded" && !s.Model.Constraints.Find(Id(2)).On, "CM7 undo:false: redone by id too");

            // An entry an unrecorded edit removed: undoing an older edit of it does nothing and clears nothing.
            Kit.Ok(t, Run(s, ConstraintEditCommand.ReplaceList(List(s.Model.Constraints.Find(Id(2)))), null, HistoryMode.None), "CM7 undo:false: a removal");
            int entries = s.Commands.UndoList.Count;
            string left = Json(s);
            Kit.Ok(t, s.Commands.Undo(), "CM7 undo:false: undo of an edit of the removed entry");
            t.Equal(left, Json(s), "CM7 undo:false: the list stays");
            t.Equal(entries - 1, s.Commands.UndoList.Count, "CM7 undo:false: the history is not cleared");
            Kit.Ok(t, s.Commands.Undo(), "CM7 undo:false: the first add still undoes");
            t.Check(s.Model.Constraints == null, "CM7 undo:false: a list edit restores the list before it");

            // A client batch: the edits join the transaction's one entry.
            s = Kit.Session();
            Kit.Ok(t, s.AddFolder(), "CM7 batch: a folder");
            string folder = s.Model.Roots[0].Id;
            int count = s.Commands.UndoList.Count;
            string before = Json(s);
            using (var tx = s.Commands.Begin("batch", new ExecuteOptions { Source = CommandSource.Bridge }))
            {
                Kit.Ok(t, tx.Execute(ConstraintEditCommand.ReplaceList(List(Entry(1)), ConstraintEditCommand.LabelAdd)), "CM7 batch: add inside");
                var be = EntryOf(s, 0);
                be.Weight = 0.5f;
                Kit.Ok(t, tx.Execute(ConstraintEditCommand.ReplaceEntry(be)), "CM7 batch: an entry edit inside");
                Kit.Ok(t, tx.Set(folder, "transform.pos", new Vec3(1f, 2f, 3f)), "CM7 batch: a property inside");
                Kit.Ok(t, tx.Commit(), "CM7 batch: commit");
            }
            t.Check(s.Commands.UndoList.Count == count + 1 && s.Commands.UndoList[0].Label == "batch", "CM7 batch: the edits join the batch's one entry");
            string after = Json(s);
            Kit.Ok(t, s.Commands.Undo(), "CM7 batch: undo the batch");
            t.Check(Json(s) == before && s.Model.Constraints == null, "CM7 batch: its undo removes the list again");
            Kit.Ok(t, s.Commands.Redo(), "CM7 batch: redo");
            t.Check(Json(s) == after && s.Model.Constraints.Entries[0].Weight == 0.5f, "CM7 batch: its redo restores both edits");
            count = s.Commands.UndoList.Count;
            using (var tx = s.Commands.Begin("quiet", ExecuteOptions.NotRecorded))
            {
                var qe = EntryOf(s, 0);
                qe.Weight = 0.25f;
                Kit.Ok(t, tx.Execute(ConstraintEditCommand.ReplaceEntry(qe)), "CM7 batch: an edit in an unrecorded batch");
                Kit.Ok(t, tx.Commit(), "CM7 batch: commit the unrecorded batch");
            }
            t.Check(s.Commands.UndoList.Count == count && s.Model.Constraints.Entries[0].Weight == 0.25f, "CM7 batch: an unrecorded batch of constraint edits keeps the history");

            // A cancelled transaction takes its steps back.
            string kept = Json(s);
            using (var tx = s.Commands.Begin("cancelled", new ExecuteOptions { Source = CommandSource.Bridge }))
            {
                var ce = EntryOf(s, 0);
                ce.Label = "gone";
                Kit.Ok(t, tx.Execute(ConstraintEditCommand.ReplaceEntry(ce)), "CM7 batch: an edit in a cancelled batch");
                Kit.Ok(t, tx.Execute(ConstraintEditCommand.ReplaceList(With(s, Entry(5)))), "CM7 batch: a list edit in a cancelled batch");
                tx.Cancel();
            }
            t.Equal(kept, Json(s), "CM7 batch: a cancelled batch leaves the list");
        }

        // ---------- refusals ----------

        static void Refusals(TestRunner t)
        {
            var s = Kit.Session();
            Kit.Fails(t, Run(s, ConstraintEditCommand.ReplaceEntry(Entry(1))), CommandCodes.NotFound, "CM7 refuse: an entry edit without a list");
            Kit.Ok(t, Run(s, ConstraintEditCommand.ReplaceList(List(Entry(1), Entry(2)), ConstraintEditCommand.LabelAdd)), "CM7 refuse: a list");
            string state = Json(s);
            int n = s.Commands.UndoList.Count;
            long v = s.Commands.Version;
            void Refused(ISceneCommand c, string code, string what, string mustContain = null)
            {
                var r = Run(s, c);
                Kit.Fails(t, r, code, "CM7 refuse: " + what);
                if (mustContain != null) t.Check(r.Message != null && r.Message.Contains(mustContain), "CM7 refuse: " + what + " names it (" + r.Message + ")");
            }
            Refused(ConstraintEditCommand.ReplaceEntry(Entry(9)), CommandCodes.NotFound, "an unknown id");
            var e = EntryOf(s, 0);
            e.Weight = 1.5f;
            Refused(ConstraintEditCommand.ReplaceEntry(e), CommandCodes.BadArgs, "a weight over 1", "weight");
            e = EntryOf(s, 0);
            e.Components = new List<string> { ConstraintComponents.Parent, ConstraintComponents.Position };
            Refused(ConstraintEditCommand.ReplaceEntry(e), CommandCodes.BadArgs, "a refused set", "cannot stand together");
            e = EntryOf(s, 0);
            e.Components.Clear();
            Refused(ConstraintEditCommand.ReplaceEntry(e), CommandCodes.BadArgs, "no component", "no component");
            e = EntryOf(s, 0);
            e.Components = new List<string> { "future" };
            Refused(ConstraintEditCommand.ReplaceEntry(e), CommandCodes.BadArgs, "an unknown component", "future");
            e = EntryOf(s, 0);
            e.Label = new string('l', 257);
            Refused(ConstraintEditCommand.ReplaceEntry(e), CommandCodes.BadArgs, "a label over 256 characters", "label");
            e = EntryOf(s, 0);
            e.Target = new ConstraintEnd(NodeB, bone: "a/b");
            Refused(ConstraintEditCommand.ReplaceEntry(e), CommandCodes.BadArgs, "an address that is not canonical", "canonical");
            e = EntryOf(s, 0);
            e.Target = new ConstraintEnd(NodeB, bone: "a", ik: "Body");
            Refused(ConstraintEditCommand.ReplaceEntry(e), CommandCodes.BadArgs, "bone and ik both", "bone and an IK target");
            e = EntryOf(s, 0);
            e.Offset.Pos = new Vec3(float.NaN, 0f, 0f);
            Refused(ConstraintEditCommand.ReplaceEntry(e), CommandCodes.BadArgs, "a NaN offset", "offset");
            var l = s.Model.Constraints.Clone();
            l.Entries.Add(Entry(1));
            Refused(ConstraintEditCommand.ReplaceList(l), CommandCodes.BadArgs, "a repeated id", "twice");
            l = s.Model.Constraints.Clone();
            l.Entries.Add(new ConstraintEntry { Id = "abc", Source = new ConstraintEnd(NodeA), Target = new ConstraintEnd(NodeB), Components = { ConstraintComponents.Position } });
            Refused(ConstraintEditCommand.ReplaceList(l), CommandCodes.BadArgs, "a bad id", "id");
            l = s.Model.Constraints.Clone();
            for (int i = 0; i < ConstraintLimits.MaxEntries; i++) l.Entries.Add(Entry(0x1000 + i));
            Refused(ConstraintEditCommand.ReplaceList(l), CommandCodes.BadArgs, "more than 256 constraints", "256");
            l = s.Model.Constraints.Clone();
            l.Entries.Add(null);
            Refused(ConstraintEditCommand.ReplaceList(l), CommandCodes.BadArgs, "a null entry", "null");
            t.Check(Json(s) == state && s.Commands.UndoList.Count == n && s.Commands.Version == v, "CM7 refuse: refusals leave the list, the history and the version as they were");
            t.Throws<ArgumentNullException>(() => ConstraintEditCommand.ReplaceEntry(null), "CM7 refuse: a null entry to ReplaceEntry");

            // Exactly the limit is allowed.
            var full = new ConstraintList();
            for (int i = 0; i < ConstraintLimits.MaxEntries; i++) full.Entries.Add(Entry(0x2000 + i));
            Kit.Ok(t, Run(s, ConstraintEditCommand.ReplaceList(full)), "CM7 refuse: 256 entries are allowed");

            // A file's entries: what they came with does not block an edit; a new problem is refused.
            var file = ConstraintList.Parse("[" +
                "{\"id\": \"" + Id(1) + "\", \"weight\": 5, \"source\": {\"node\": \"" + NodeA + "\"}, \"target\": {\"node\": \"" + NodeB + "\"}, \"components\": [\"position\"]}, " +
                "{\"id\": \"" + Id(2) + "\", \"source\": {\"node\": \"" + NodeA + "\"}, \"target\": {\"node\": \"" + NodeB + "\"}, \"components\": [\"future\"]}, " +
                "{\"id\": \"" + Id(3) + "\", \"source\": {\"node\": \"" + NodeA + "\", \"bone\": \"hair:0/x#0\"}, \"target\": {\"node\": \"" + NodeB + "\"}, \"components\": [\"parent\", \"position\"]}" +
                "]");
            var s2 = Kit.Session(2);
            var fc = new SceneContainer();
            fc.Scene.Constraints = file;
            s2.Bind(fc);
            t.Equal(3, s2.Model.Constraints.Count, "CM7 file: a bound list");
            var w = EntryOf(s2, 0);
            w.Label = "a label on a weight of 5";
            Kit.Ok(t, Run(s2, ConstraintEditCommand.ReplaceEntry(w)), "CM7 file: a weight out of range does not block another member");
            var w2 = EntryOf(s2, 0);
            w2.Weight = 6f;
            Kit.Fails(t, Run(s2, ConstraintEditCommand.ReplaceEntry(w2)), CommandCodes.BadArgs, "CM7 file: another such weight is refused");
            var w3 = EntryOf(s2, 0);
            w3.Weight = 0.5f;
            Kit.Ok(t, Run(s2, ConstraintEditCommand.ReplaceEntry(w3)), "CM7 file: a weight back in range is taken");
            var bad = EntryOf(s2, 1);
            bad.On = false;
            Kit.Ok(t, Run(s2, ConstraintEditCommand.ReplaceEntry(bad)), "CM7 file: a `bad` entry can be switched");
            t.Equal("future", s2.Model.Constraints.Entries[1].Components[0], "CM7 file: and keeps its unknown component");
            var fixedSet = EntryOf(s2, 1);
            fixedSet.Components = new List<string> { ConstraintComponents.Rotation };
            Kit.Ok(t, Run(s2, ConstraintEditCommand.ReplaceEntry(fixedSet)), "CM7 file: and given a good set");
            var otherBad = EntryOf(s2, 1);
            otherBad.Components = new List<string> { "other" };
            Kit.Fails(t, Run(s2, ConstraintEditCommand.ReplaceEntry(otherBad)), CommandCodes.BadArgs, "CM7 file: but not a new bad one");
            Kit.Ok(t, Run(s2, ConstraintEditCommand.ReplaceList(new ConstraintList(s2.Model.Constraints.Entries.AsEnumerable().Reverse()))), "CM7 file: a list holding a `bad` entry reorders");
            Kit.Ok(t, Run(s2, ConstraintEditCommand.ReplaceList(new ConstraintList(s2.Model.Constraints.Entries.Skip(1)))), "CM7 file: and shrinks");
            var bigFile = new ConstraintList();
            for (int i = 0; i < 300; i++) bigFile.Entries.Add(Entry(0x3000 + i));
            var s3 = Kit.Session(3);
            var bc = new SceneContainer();
            bc.Scene.Constraints = bigFile;
            s3.Bind(bc);
            var edited = EntryOf(s3, 280);
            edited.On = false;
            Kit.Ok(t, Run(s3, ConstraintEditCommand.ReplaceEntry(edited)), "CM7 file: an entry beyond the limit edits");
            Kit.Ok(t, Run(s3, ConstraintEditCommand.ReplaceList(new ConstraintList(s3.Model.Constraints.Entries.Skip(10)))), "CM7 file: a list over the limit shrinks");
            Kit.Fails(t, Run(s3, ConstraintEditCommand.ReplaceList(With(s3, Entry(0x4000)).Clone())), CommandCodes.BadArgs, "CM7 file: but cannot grow");
        }

        // ---------- what is shared ----------

        static void Shared(TestRunner t)
        {
            var s = Kit.Session();
            Kit.Ok(t, Run(s, ConstraintEditCommand.ReplaceList(List(Entry(1), Entry(2)), ConstraintEditCommand.LabelAdd)), "CM7 shared: a list");
            var held = s.Model.Constraints;
            string heldText = held.ToJson();
            var heldEntry = held.Entries[0];

            var e = EntryOf(s, 0);
            e.Weight = 0.5f;
            Kit.Ok(t, Run(s, ConstraintEditCommand.ReplaceEntry(e)), "CM7 shared: an entry edit");
            t.Check(!ReferenceEquals(held, s.Model.Constraints), "CM7 shared: an edit puts a new list in the model, so a reference tells a change");
            t.Equal(heldText, held.ToJson(), "CM7 shared: the list a reader holds is not changed");
            t.Check(ReferenceEquals(held.Entries[1], s.Model.Constraints.Entries[1]) && heldEntry.Weight == 1f, "CM7 shared: entries not edited are the same objects, the edited one a new object");

            var after = s.Model.Constraints;
            Kit.NoOp(t, Run(s, ConstraintEditCommand.ReplaceEntry(EntryOf(s, 0))), "CM7 shared: a no-op");
            t.Check(ReferenceEquals(after, s.Model.Constraints), "CM7 shared: a no-op keeps the list object");
            Kit.Ok(t, s.Commands.Undo(), "CM7 shared: undo");
            t.Equal(heldText, Json(s), "CM7 shared: undo restores the value");
            t.Check(!ReferenceEquals(after, s.Model.Constraints), "CM7 shared: and a new reference");

            // A move builds a new list of the same objects: the entries are not changed by being stored.
            var reversed = new ConstraintList(s.Model.Constraints.Entries.AsEnumerable().Reverse());
            var first = reversed.Entries[0];
            string firstText = first.ToJson();
            var cmd = ConstraintEditCommand.ReplaceList(reversed, ConstraintEditCommand.LabelReorder);
            Kit.Ok(t, Run(s, cmd), "CM7 shared: a move");
            t.Check(ReferenceEquals(first, s.Model.Constraints.Entries[0]) && first.ToJson() == firstText, "CM7 shared: an entry in normal form is stored as it is, shared with the list before");
            t.Check(cmd.AfterList != null && !cmd.IsEntryEdit && cmd.EntryId == null && cmd.AfterEntry == null && cmd.ToString().Contains("Reorder"), "CM7 shared: a list edit's accessors");
            var ec = ConstraintEditCommand.ReplaceEntry(Entry(1));
            t.Check(ec.IsEntryEdit && ec.EntryId == Id(1) && ec.AfterEntry != null && ec.AfterList == null && ec.ToString().Contains(Id(1)) && !ec.HeldNodes.Any(), "CM7 shared: an entry edit's accessors");
            t.Check(ConstraintEditCommand.Clear().Label == "Clear constraints", "CM7 shared: labels");
            t.Equal("Add constraint|Constraint|Remove constraint|Clear constraints|Reorder constraints|Take offset",
                string.Join("|", ConstraintEditCommand.LabelAdd, ConstraintEditCommand.LabelEdit, ConstraintEditCommand.LabelRemove, ConstraintEditCommand.LabelClear, ConstraintEditCommand.LabelReorder, ConstraintEditCommand.LabelTakeOffset), "CM7 shared: the six labels");
            t.Equal("constraints", ConstraintEditCommand.ChangePath, "CM7 shared: the change path");
        }

        // ---------- the file and the session ----------

        static void FileAndSession(TestRunner t)
        {
            var s = Kit.Session();
            Kit.Ok(t, s.AddFolder(), "CM7 file: a folder");
            Kit.Ok(t, s.AddFolder(), "CM7 file: another folder");
            string a = s.Model.Roots[0].Id, b = s.Model.Roots[1].Id;
            var blank = s.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            var e = new ConstraintEntry { Id = Id(1), Source = new ConstraintEnd(a), Target = new ConstraintEnd(b), Components = { ConstraintComponents.Position } };
            Kit.Ok(t, Run(s, ConstraintEditCommand.ReplaceList(List(e), ConstraintEditCommand.LabelAdd)), "CM7 file: add");
            var bytes = s.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            var man = Newtonsoft.Json.Linq.JObject.Parse(Encoding.UTF8.GetString(ContainerReader.Read(bytes).Get("manifest.json").Data));
            var sj = Newtonsoft.Json.Linq.JObject.Parse(Encoding.UTF8.GetString(ContainerReader.Read(bytes).Get("scene.json").Data));
            t.Check((int)man["formatVersion"] == 4 && string.Join(",", sj.Properties().Select(p => p.Name)) == "nodes,scene,constraints", "CM7 file: a scene with an entry saves as revision 4, the member after scene");
            t.Check(Kit.Text(Kit.Bytes(s)).Contains("\"constraints\": ["), "CM7 file: the model's canonical scene.json carries the list");
            string json = Json(s);
            var s2 = Kit.Session(2);
            var l = s2.Load(bytes);
            t.Check(l.Ok && Json(s2) == json && !s2.Commands.IsDirty && s2.Commands.UndoList.Count == 0, "CM7 file: a load binds the list (equal JSON), not modified, no history");
            t.Check(Kit.Same(s2.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder)), bytes), "CM7 file: load and save give the same bytes");
            s2.NewScene();
            t.Check(s2.Model.Constraints == null, "CM7 file: NewScene removes it");

            // CF8 on a session: the last entry removed gives the bytes of before the first add.
            Kit.Ok(t, Run(s, ConstraintEditCommand.ReplaceList(new ConstraintList(), ConstraintEditCommand.LabelRemove)), "CM7 file: remove the last entry");
            var again = s.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            t.Check(Kit.Same(blank, again), "CM7 file: the file is what it was before the first add\n    " + Kit.Diff(ContainerReader.Read(blank).Get("scene.json").Data, ContainerReader.Read(again).Get("scene.json").Data));
            t.Check((int)Newtonsoft.Json.Linq.JObject.Parse(Encoding.UTF8.GetString(ContainerReader.Read(again).Get("manifest.json").Data))["formatVersion"] == 1, "CM7 file: at revision 1");
            Kit.Ok(t, s.Commands.Undo(), "CM7 file: undo the removal");
            Kit.Ok(t, s.Commands.Undo(), "CM7 file: undo the add");
            t.Check(Kit.Same(blank, s.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder))), "CM7 file: undone to nothing, the same bytes");

            // Bind of a document with an empty list: the model holds null.
            var c = new SceneContainer();
            c.Scene.Constraints = new ConstraintList();
            var s3 = Kit.Session(3);
            s3.Bind(c);
            t.Check(s3.Model.Constraints == null, "CM7 file: a document's empty list binds as none");
        }
    }
}
