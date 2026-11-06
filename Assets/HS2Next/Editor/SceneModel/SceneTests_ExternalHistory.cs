// KR1: the bus's external entries, a plugin's own state on the scene's undo list
// (CommandBus.RecordExternal, RemoveOwner, CountOwner, TakeWarnings; PluginHistoryCommand). Record, undo and redo with the
// model bytes untouched and the selection left alone; interleaving with recorded host commands; merging (same owner and
// key only, never across a seal or a redo branch; the callback and the state form); the entry and byte limits; the
// version and the dirty flag; RemoveOwner keeping the others' order (also when called from an undo callback); a throwing
// callback that keeps the history and leaves a warning; Busy inside a transaction, during an event delivery and inside an
// undo callback; a new scene clears the entries.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save.Tests;

    public static class SceneTests_ExternalHistory
    {
        const string A = "stp3.test.a", B = "stp3.test.b";

        public static void Run(TestRunner t)
        {
            RecordUndoRedo(t);
            Interleave(t);
            Merging(t);
            Limits(t);
            RemoveOwner(t);
            Throwing(t);
            Busy(t);
            Cleared(t);
        }

        sealed class Counter
        {
            public int Value;
            public PluginHistoryCommand Set(string owner, string label, int to)
            {
                int from = Value;
                Value = to;
                return new PluginHistoryCommand(owner, label, () => Value = from, () => Value = to);
            }
        }

        static void RecordUndoRedo(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var bus = s.Commands;
            var rec = new EventRecorder(s);
            s.Selection.Set(new[] { Kit.GoldenItem });
            rec.Clear();
            var c = new Counter();
            var bytes = Kit.Bytes(s);
            long v0 = bus.Version;
            bool dirty0 = bus.IsDirty;
            var r = bus.RecordExternal(A, c.Set(A, "Set counter", 5), "Set counter");
            t.Equal(ExternalRecordResult.Recorded, r, "record: Recorded");
            t.Equal(v0 + 1, bus.Version, "record: the version + 1");
            t.Check(bus.IsDirty && !dirty0, "record: the scene shows unsaved changes");
            t.Equal(1, rec.History, "record: one HistoryChanged");
            t.Equal(0, rec.Batches.Count, "record: no change batch");
            t.Equal(1, bus.UndoList.Count, "record: one undo entry");
            t.Equal("Set counter", bus.UndoList[0].Label, "record: the label");
            t.Equal(CommandSource.Plugin, bus.UndoList[0].Source, "record: source Plugin");
            t.Equal(A, bus.UndoList[0].Owner, "record: the owner");
            t.Equal(PluginHistoryCommand.BaseBytes, bus.UndoList[0].EstimatedBytes, "record: 512 bytes for the callback form");
            t.Equal(1, bus.CountOwner(A), "record: CountOwner");
            t.Equal(0, bus.CountOwner(B), "record: CountOwner of another owner");
            s.Selection.Set(new[] { Kit.GoldenChara });
            rec.Clear();
            Kit.Ok(t, bus.Undo(), "undo");
            t.Equal(0, c.Value, "undo: the undo callback ran");
            t.Check(Kit.Same(bytes, Kit.Bytes(s)), "undo: the model bytes are unchanged");
            t.Equal(1, s.Selection.Count, "undo: the selection is left as it is");
            t.Check(s.Selection.Contains(Kit.GoldenChara), "undo: the selection is left as it is (id)");
            t.Equal(0, rec.Batches.Count, "undo: no change batch");
            t.Equal(1, rec.History, "undo: one HistoryChanged");
            t.Equal(v0 + 2, bus.Version, "undo: the version + 1");
            t.Equal(1, bus.RedoList.Count, "undo: one redo entry");
            Kit.Ok(t, bus.Redo(), "redo");
            t.Equal(5, c.Value, "redo: the redo callback ran");
            t.Check(Kit.Same(bytes, Kit.Bytes(s)), "redo: the model bytes are unchanged");
            t.Equal(0, bus.TakeWarnings().Count, "no warnings");
            // the state form
            var states = new List<string>();
            var cmd = new PluginHistoryCommand(A, "Set state", "{\"x\":1}", "{\"x\":2}", j => states.Add(j));
            t.Equal(ExternalRecordResult.Recorded, bus.RecordExternal(A, cmd, "Set state"), "state form: Recorded");
            t.Equal(PluginHistoryCommand.BaseBytes + 2L * (7 + 7), bus.UndoList[0].EstimatedBytes, "state form: 512 + twice the two lengths");
            Kit.Ok(t, bus.Undo(), "state form: undo");
            Kit.Ok(t, bus.Redo(), "state form: redo");
            t.Equal("{\"x\":1}|{\"x\":2}", string.Join("|", states), "state form: undo applies before, redo applies after");
            t.Equal(1, cmd.Undos, "state form: one undo call");
            t.Equal(1, cmd.Redos, "state form: one redo call");
            // the label of the bus entry (null = the command's)
            bus.RecordExternal(A, c.Set(A, "own label", 6), null);
            t.Equal("own label", bus.UndoList[0].Label, "a null label takes the command's");
            // a save seals the newest entry and clears the dirty flag
            bus.MarkSaved();
            t.Check(!bus.IsDirty, "after MarkSaved: clean");
            bus.RecordExternal(A, c.Set(A, "after save", 7), "after save");
            t.Check(bus.IsDirty, "a record after a save makes the scene dirty");
        }

        static void Interleave(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var bus = s.Commands;
            var c = new Counter();
            var b0 = Kit.Bytes(s);
            Kit.Ok(t, s.Set(Kit.GoldenItem, "visible", false), "interleave: host set 1");
            var b1 = Kit.Bytes(s);
            bus.RecordExternal(A, c.Set(A, "plugin 1", 1), "plugin 1");
            Kit.Ok(t, s.Set(Kit.GoldenItem, "name", "x"), "interleave: host set 2");
            var b2 = Kit.Bytes(s);
            bus.RecordExternal(B, c.Set(B, "plugin 2", 2), "plugin 2");
            t.Equal("plugin 2|" + bus.UndoList[1].Label + "|plugin 1|" + bus.UndoList[3].Label, string.Join("|", Labels(bus.UndoList)), "interleave: call order");
            Kit.Ok(t, bus.Undo(), "interleave: undo 1");
            t.Check(c.Value == 1 && Kit.Same(b2, Kit.Bytes(s)), "interleave: undo 1 restores the plugin state only");
            Kit.Ok(t, bus.Undo(), "interleave: undo 2");
            t.Check(c.Value == 1 && Kit.Same(b1, Kit.Bytes(s)), "interleave: undo 2 restores the host set");
            Kit.Ok(t, bus.Undo(), "interleave: undo 3");
            t.Check(c.Value == 0 && Kit.Same(b1, Kit.Bytes(s)), "interleave: undo 3 restores the plugin state");
            Kit.Ok(t, bus.Undo(), "interleave: undo 4");
            t.Check(c.Value == 0 && Kit.Same(b0, Kit.Bytes(s)), "interleave: undo 4 restores the first host set");
            for (int i = 0; i < 4; i++) Kit.Ok(t, bus.Redo(), "interleave: redo " + (i + 1));
            t.Check(c.Value == 2 && Kit.Same(b2, Kit.Bytes(s)), "interleave: four redos restore both");
            // a record drops the redo branch
            bus.Undo();
            t.Equal(1, bus.RedoList.Count, "interleave: one redo entry");
            bus.RecordExternal(A, c.Set(A, "plugin 3", 3), "plugin 3");
            t.Equal(0, bus.RedoList.Count, "interleave: a record drops the redo branch");
        }

        static IEnumerable<string> Labels(IReadOnlyList<HistoryEntryInfo> l)
        {
            foreach (var e in l) yield return e.Label;
        }

        static void Merging(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var bus = s.Commands;
            var c = new Counter();
            t.Equal(ExternalRecordResult.Recorded, bus.RecordExternal(A, c.Set(A, "drag", 1), "drag", "k"), "merge: first Recorded");
            t.Equal(ExternalRecordResult.Merged, bus.RecordExternal(A, c.Set(A, "drag", 2), "drag", "k"), "merge: same owner and key Merged");
            t.Equal(ExternalRecordResult.Merged, bus.RecordExternal(A, c.Set(A, "drag", 3), "drag", "k"), "merge: again Merged");
            t.Equal(1, bus.UndoList.Count, "merge: one entry");
            long v = bus.Version;
            Kit.Ok(t, bus.Undo(), "merge: undo");
            t.Equal(0, c.Value, "merge: the first undo stays");
            Kit.Ok(t, bus.Redo(), "merge: redo");
            t.Equal(3, c.Value, "merge: the newest redo is taken");
            // a redo sealed the entry: no merge after it
            t.Equal(ExternalRecordResult.Recorded, bus.RecordExternal(A, c.Set(A, "drag", 4), "drag", "k"), "merge: not into a sealed entry");
            // another owner with the same key
            t.Equal(ExternalRecordResult.Recorded, bus.RecordExternal(B, c.Set(B, "drag", 5), "drag", "k"), "merge: not across owners");
            // a null key never merges
            t.Equal(ExternalRecordResult.Recorded, bus.RecordExternal(B, c.Set(B, "x", 6), "x"), "merge: a null key records");
            t.Equal(ExternalRecordResult.Recorded, bus.RecordExternal(B, c.Set(B, "x", 7), "x"), "merge: a null key records again");
            // a host operation seals the plugin entry
            bus.RecordExternal(A, c.Set(A, "drag", 8), "drag", "m");
            Kit.Ok(t, s.Set(Kit.GoldenItem, "visible", false), "merge: a host set");
            bus.RecordExternal(A, c.Set(A, "drag", 9), "drag", "m");
            t.Equal("drag|" + bus.UndoList[1].Label + "|drag", string.Join("|", new List<string>(Labels(bus.UndoList)).GetRange(0, 3)), "merge: the host set separates the two");
            // a host property set with the same key never merges into the plugin entry
            int n = bus.UndoList.Count;
            Kit.Ok(t, s.Set(Kit.GoldenItem, "name", "y", new ExecuteOptions { MergeKey = "m" }), "merge: host set with the plugin's key");
            Kit.Ok(t, s.Set(Kit.GoldenItem, "name", "z", new ExecuteOptions { MergeKey = "m" }), "merge: host set with the plugin's key again");
            t.Equal(n + 1, bus.UndoList.Count, "merge: the host sets merge with each other, not into the plugin entry");
            // no merge while something can be redone
            var s2 = Kit.GoldenSession(t);
            var c2 = new Counter();
            s2.Commands.RecordExternal(A, c2.Set(A, "a", 1), "a", "k");
            s2.Commands.RecordExternal(A, c2.Set(A, "b", 2), "b", "j");
            s2.Commands.Undo();
            t.Equal(ExternalRecordResult.Recorded, s2.Commands.RecordExternal(A, c2.Set(A, "a", 3), "a", "k"), "merge: not with a redo branch");
            // the state form keeps the first before and takes the new after and apply
            var s3 = Kit.GoldenSession(t);
            var log = new List<string>();
            s3.Commands.RecordExternal(A, new PluginHistoryCommand(A, "w", "1", "2", j => log.Add("old:" + j)), "w", "k");
            t.Equal(ExternalRecordResult.Merged, s3.Commands.RecordExternal(A, new PluginHistoryCommand(A, "w", "2", "33", j => log.Add("new:" + j)), "w", "k"), "merge: state form Merged");
            t.Equal(PluginHistoryCommand.BaseBytes + 2L * (1 + 2), s3.Commands.UndoList[0].EstimatedBytes, "merge: the estimate re-measured");
            s3.Commands.Undo();
            s3.Commands.Redo();
            t.Equal("new:1|new:33", string.Join("|", log), "merge: state form keeps the first before, takes the new after and apply");
            // forms do not merge with each other
            t.Equal(ExternalRecordResult.Recorded, s3.Commands.RecordExternal(A, new PluginHistoryCommand(A, "w", () => { }, () => { }), "w", "q"), "merge: a new key records");
            t.Equal(ExternalRecordResult.Recorded, s3.Commands.RecordExternal(A, new PluginHistoryCommand(A, "w", "1", "2", j => { }), "w", "q"), "merge: the two forms do not merge");
        }

        static void Limits(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var bus = s.Commands;
            var c = new Counter();
            bus.Limits = new HistoryLimits { MaxEntries = 3 };
            for (int i = 1; i <= 5; i++) bus.RecordExternal(A, c.Set(A, "e" + i, i), "e" + i);
            t.Equal("e5|e4|e3", string.Join("|", Labels(bus.UndoList)), "limits: 200-entry rule (here 3): the oldest leave");
            bus.Limits = new HistoryLimits { MaxEntries = 200, MaxBytes = 1200 };
            t.Equal(2, bus.UndoList.Count, "limits: the byte limit evicts at once (2 x 512 <= 1200)");
            var big = new string('x', 1000);
            bus.RecordExternal(A, new PluginHistoryCommand(A, "big", "\"" + big + "\"", "\"" + big + "\"", j => { }), "big");
            t.Equal(1, bus.UndoList.Count, "limits: a large state entry stays alone (the newest entry is kept even over the limit)");
            t.Equal("big", bus.UndoList[0].Label, "limits: the newest stays");
        }

        static void RemoveOwner(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var bus = s.Commands;
            var rec = new EventRecorder(s);
            var c = new Counter();
            bus.RecordExternal(A, c.Set(A, "a1", 1), "a1");
            Kit.Ok(t, s.Set(Kit.GoldenItem, "visible", false), "remove: host 1");
            string h1 = bus.UndoList[0].Label;
            bus.RecordExternal(B, c.Set(B, "b1", 2), "b1");
            bus.RecordExternal(A, c.Set(A, "a2", 3), "a2");
            Kit.Ok(t, s.Set(Kit.GoldenItem, "name", "q"), "remove: host 2");
            string h2 = bus.UndoList[0].Label;
            bus.RecordExternal(A, c.Set(A, "a3", 4), "a3");
            bus.Undo();
            bus.Undo();
            t.Equal(4, bus.CountOwner(A) + bus.CountOwner(B), "remove: 3 + 1 entries before");
            rec.Clear();
            int n = bus.RemoveOwner(A);
            t.Equal(3, n, "remove: three entries of A removed");
            t.Equal("b1|" + h1, string.Join("|", Labels(bus.UndoList)), "remove: the undo list keeps the others' order");
            t.Equal(h2, string.Join("|", Labels(bus.RedoList)), "remove: the redo list keeps the others' order");
            t.Equal(1, rec.History, "remove: one HistoryChanged");
            t.Equal(0, bus.CountOwner(A), "remove: none of A left");
            t.Equal(0, bus.RemoveOwner(A), "remove: again 0");
            rec.Clear();
            bus.RemoveOwner("nobody");
            t.Equal(0, rec.History, "remove: no HistoryChanged when nothing was removed");
            Kit.Ok(t, bus.Redo(), "remove: the host entry redoes");
            Kit.Ok(t, bus.Undo(), "remove: undo after removal");
            Kit.Ok(t, bus.Undo(), "remove: undo of b1");
            t.Equal(1, c.Value, "remove: b1's undo ran (the value before b1)");
            // removal from inside an undo callback (the owner faulted there): after the step, nothing of it is left
            var s2 = Kit.GoldenSession(t);
            var bus2 = s2.Commands;
            bus2.RecordExternal(B, new PluginHistoryCommand(B, "b", () => { }, () => { }), "b");
            bus2.RecordExternal(A, new PluginHistoryCommand(A, "a1", () => { }, () => { }), "a1");
            bus2.RecordExternal(A, new PluginHistoryCommand(A, "a2", () => bus2.RemoveOwner(A), () => { }), "a2");
            Kit.Ok(t, bus2.Undo(), "remove in undo: the step succeeds");
            t.Equal(0, bus2.CountOwner(A), "remove in undo: the owner's entries are gone after the step");
            t.Equal("b", string.Join("|", Labels(bus2.UndoList)), "remove in undo: the other owner's entry stays");
            t.Equal(0, bus2.RedoList.Count, "remove in undo: the undone entry did not reach the redo list");
        }

        static void Throwing(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var bus = s.Commands;
            var c = new Counter();
            Kit.Ok(t, s.Set(Kit.GoldenItem, "visible", false), "throw: host set");
            var faults = new List<string>();
            ExternalCallRunner runner = (step, a) =>
            {
                try { a(); return null; }
                catch (Exception e) { faults.Add(step + ":" + e.GetType().Name); return e; }
            };
            bus.RecordExternal(A, new PluginHistoryCommand(A, "bad", () => throw new InvalidOperationException("u"), () => throw new FormatException("r"), runner), "bad");
            var bytes = Kit.Bytes(s);
            Kit.Ok(t, bus.Undo(), "throw: undo answers ok");
            t.Equal(1, bus.RedoList.Count, "throw: the entry moved to the redo list");
            t.Equal(1, bus.UndoList.Count, "throw: the host entry is kept (history not cleared)");
            t.Check(Kit.Same(bytes, Kit.Bytes(s)), "throw: the model untouched");
            var w = bus.TakeWarnings();
            t.Equal(1, w.Count, "throw: one warning");
            t.Equal("plugin " + A + ": undo failed (InvalidOperationException)", w.Count > 0 ? w[0] : null, "throw: the warning text");
            t.Equal(0, bus.TakeWarnings().Count, "throw: warnings are taken once");
            Kit.Ok(t, bus.Redo(), "throw: redo answers ok");
            t.Equal("redo failed (FormatException)", bus.TakeWarnings()[0].Substring(("plugin " + A + ": ").Length), "throw: the redo warning");
            t.Equal("undo:InvalidOperationException|redo:FormatException", string.Join("|", faults), "throw: the runner saw both (counted as faults)");
            Kit.Ok(t, bus.Undo(), "throw: undo again");
            Kit.Ok(t, bus.Undo(), "throw: the host entry undoes");
            t.Check(!Kit.Same(bytes, Kit.Bytes(s)), "throw: the host undo changed the model");
            // without a runner the command catches itself
            bus.TakeWarnings();
            bus.RecordExternal(B, new PluginHistoryCommand(B, "bad2", () => throw new ArgumentException(), () => { }), "bad2");
            Kit.Ok(t, bus.Undo(), "throw: no runner, undo ok");
            t.Equal("plugin " + B + ": undo failed (ArgumentException)", bus.TakeWarnings()[0], "throw: no runner, the warning");
        }

        static void Busy(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var bus = s.Commands;
            var c = new Counter();
            t.Check(bus.AcceptsExternal, "AcceptsExternal outside a transaction");
            var tx = bus.Begin("drag");
            t.Check(!bus.AcceptsExternal, "AcceptsExternal false inside a transaction");
            t.Equal(ExternalRecordResult.Busy, bus.RecordExternal(A, c.Set(A, "x", 1), "x"), "busy: inside an open transaction");
            tx.Set(Kit.GoldenItem, "visible", false);
            tx.Commit();
            t.Equal(1, bus.UndoList.Count, "busy: nothing recorded in the transaction");
            // during an event delivery
            ExternalRecordResult? inHandler = null;
            Action<SceneChangeBatch> h = b => { if (inHandler == null) inHandler = bus.RecordExternal(A, c.Set(A, "y", 2), "y"); };
            s.Events.Changed += h;
            Kit.Ok(t, s.Set(Kit.GoldenItem, "name", "w"), "busy: a host set with a handler");
            s.Events.Changed -= h;
            t.Equal(ExternalRecordResult.Busy, inHandler ?? ExternalRecordResult.Recorded, "busy: during a Changed delivery");
            ExternalRecordResult? inHistory = null;
            Action<HistoryChangedArgs> hh = a => { if (inHistory == null) inHistory = bus.RecordExternal(A, c.Set(A, "z", 3), "z"); };
            s.Events.HistoryChanged += hh;
            bus.RecordExternal(B, c.Set(B, "b", 4), "b");
            s.Events.HistoryChanged -= hh;
            t.Equal(ExternalRecordResult.Busy, inHistory ?? ExternalRecordResult.Recorded, "busy: during a HistoryChanged delivery");
            // inside an undo or redo callback
            ExternalRecordResult? inUndo = null, inRedo = null;
            bus.RecordExternal(A, new PluginHistoryCommand(A, "cb", () => inUndo = bus.RecordExternal(A, c.Set(A, "u", 5), "u"),
                () => inRedo = bus.RecordExternal(A, c.Set(A, "r", 6), "r")), "cb");
            bus.Undo();
            bus.Redo();
            t.Equal(ExternalRecordResult.Busy, inUndo ?? ExternalRecordResult.Recorded, "busy: inside an undo callback");
            t.Equal(ExternalRecordResult.Busy, inRedo ?? ExternalRecordResult.Recorded, "busy: inside a redo callback");
            // a host command from an undo callback answers busy (a callback restores the plugin's state only)
            CommandResult hostInUndo = null;
            bus.RecordExternal(A, new PluginHistoryCommand(A, "cb2", () => hostInUndo = s.Set(Kit.GoldenItem, "visible", true), () => { }), "cb2");
            bus.Undo();
            t.Check(hostInUndo != null && hostInUndo.Status == CommandStatus.Error && hostInUndo.Code == CommandCodes.Busy, "busy: a scene write from an undo callback answers busy");
            // argument rules
            bool threw = false;
            try { bus.RecordExternal(null, c.Set(A, "n", 7), "n"); }
            catch (ArgumentException) { threw = true; }
            t.Check(threw, "an empty owner throws ArgumentException");
        }

        static void Cleared(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var bus = s.Commands;
            var c = new Counter();
            bus.RecordExternal(A, c.Set(A, "a", 1), "a");
            bus.RecordExternal(A, c.Set(A, "b", 2), "b");
            bus.Undo();
            s.NewScene();
            t.Equal(0, bus.UndoList.Count + bus.RedoList.Count, "a new scene clears the entries");
            t.Equal(0, bus.CountOwner(A), "a new scene: CountOwner 0");
            bus.RecordExternal(A, c.Set(A, "c", 3), "c");
            s.Bind(Kit.GoldenContainer(t, false));
            t.Equal(0, bus.UndoList.Count, "a loaded scene clears the entries");
        }
    }
}
