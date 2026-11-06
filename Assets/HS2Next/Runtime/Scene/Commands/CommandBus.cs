// The single writer of the scene model: executes commands, records them in
// the undo history (200 entries / 64 MiB by default), merges typed edits that carry the same merge key, runs
// transactions (drags, bridge batches) as one entry, and delivers one change batch per operation after the
// model is consistent. View state (`ui.*`, `scene.camera.current`) is applied without history. Main thread
// only; a call from an event handler answers `busy`. A command other than a property set applied without
// history (bridge `undo: false`) clears the history, which could not be undone consistently past it.
// External entries (a plugin's own state, Phase 6.1) join the same lists without an apply and without a change batch:
// their undo and redo run the party's callbacks, which never clear the history when they fail; an owner's entries
// can be removed at once, the others keeping their order.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes
{
    public enum HistoryMode
    {
        /// <summary>Recorded in the undo history (the default).</summary>
        Record,
        /// <summary>Applied without history (view state, bridge requests with <c>undo: false</c>).</summary>
        None,
    }

    public enum CommandSource { User, Bridge, Script, Plugin, System }

    public struct ExecuteOptions
    {
        public HistoryMode Record;
        /// <summary>Consecutive recorded property sets with the same non-null key merge into one entry.</summary>
        public string MergeKey;
        /// <summary>History label; null = the command's own.</summary>
        public string Label;
        public CommandSource Source;

        /// <summary>Options of an unrecorded operation.</summary>
        public static ExecuteOptions NotRecorded => new ExecuteOptions { Record = HistoryMode.None };
        /// <summary>Recorded options of a given source.</summary>
        public static ExecuteOptions From(CommandSource source) => new ExecuteOptions { Source = source };
    }

    /// <summary>History bounds.</summary>
    public sealed class HistoryLimits
    {
        public int MaxEntries = 200;
        /// <summary>Sum of <see cref="ISceneCommand.EstimatedBytes"/> over the history.</summary>
        public long MaxBytes = 64L << 20;
    }

    /// <summary>One history entry as listed to the UI.</summary>
    public sealed class HistoryEntryInfo
    {
        public string Label { get; }
        public CommandSource Source { get; }
        public long EstimatedBytes { get; }
        /// <summary>The party of an external entry (a plugin's GUID), else null.</summary>
        public string Owner { get; }

        public HistoryEntryInfo(string label, CommandSource source, long estimatedBytes, string owner = null)
        {
            Label = label;
            Source = source;
            EstimatedBytes = estimatedBytes;
            Owner = owner;
        }

        public override string ToString() => Label;
    }

    /// <summary>Outcome of <see cref="CommandBus.RecordExternal"/>.</summary>
    public enum ExternalRecordResult
    {
        /// <summary>A new entry is on the undo list.</summary>
        Recorded = 0,
        /// <summary>Merged into the newest entry (same owner and merge key).</summary>
        Merged = 1,
        /// <summary>Refused: a transaction is open, a command applies (also an undo or redo callback) or a scene event is being delivered.</summary>
        Busy = 2,
    }

    public sealed class CommandBus
    {
        readonly SceneSession session;
        internal readonly History History = new History();
        HistoryLimits limits;
        bool applying;

        internal CommandBus(SceneSession session, HistoryLimits limits)
        {
            this.session = session;
            this.limits = limits ?? new HistoryLimits();
        }

        SceneModel Model => session.Model;
        SelectionModel Selection => session.Selection;
        SceneEvents Events => session.Events;

        /// <summary>True while a command applies or a handler runs: bus calls answer `busy`.</summary>
        internal bool Busy => applying || Events.IsDelivering;
        internal bool Applying => applying;

        static CommandResult BusyResult(string what) => CommandResult.Error(CommandCodes.Busy, what);

        // ---------------- execute ----------------

        /// <summary>Validates and applies a command; records it (or joins the open transaction) unless <c>o.Record</c> is None.</summary>
        public CommandResult Execute(ISceneCommand cmd, ExecuteOptions o = default)
        {
            session.CheckThread();
            if (cmd == null) return CommandResult.Error(CommandCodes.BadArgs, "no command");
            if (Busy) return BusyResult("the bus is busy (called from an event handler or a running command)");
            bool view = IsViewOnly(cmd);
            if (ActiveTransaction != null && !view && o.Record == HistoryMode.Record) return ActiveTransaction.ExecuteStep(cmd);
            return Run(cmd, o, view);
        }

        /// <summary>Sets one field; <paramref name="nodeId"/> null = the scene. `camera.active` = true activates
        /// the camera and deactivates the others.</summary>
        public CommandResult Set(string nodeId, string path, object value, ExecuteOptions o = default) => Execute(MakeSet(nodeId, path, value), o);

        internal ISceneCommand MakeSet(string nodeId, string path, object value)
        {
            if (nodeId != null && path == "camera.active" && value is bool b && b && Model.Find(nodeId)?.Kind == NodeKind.Camera)
                return session.ActiveCameraCommand(nodeId);
            return new SetPropertyCommand(nodeId, path, value);
        }

        /// <summary>One operation outside a transaction: apply, record (or not), deliver.</summary>
        internal CommandResult Run(ISceneCommand cmd, ExecuteOptions o, bool view)
        {
            var selBefore = Selection.Snapshot();
            var sink = new ChangeSink();
            if (!TryApply(cmd, sink, out var error)) return error;
            if (sink.Count == 0) return CommandResult.NoOp(cmd);
            Version++;
            var newIds = SelectAfter(cmd, o.Source);
            var selAfter = Selection.Snapshot();
            var forgotten = new List<TreeNode>();
            bool historyChanged = false;
            var result = cmd;
            if (o.Record == HistoryMode.Record && !view)
            {
                result = Record(cmd, o, selBefore, selAfter, forgotten);
                historyChanged = true;
            }
            else
            {
                SealTop();
                if (!view && !IsPropertyOnly(cmd))
                {
                    historyChanged = History.Undo.Count + History.Redo.Count > 0;
                    History.ClearAll(forgotten);
                }
                Unheld(cmd.HeldNodes, forgotten);
            }
            Deliver(ChangeOrigin.Command, o.Source, sink.Changes, selBefore, selAfter, forgotten, historyChanged);
            return CommandResult.Success(result, newIds);
        }

        /// <summary>Validate + Apply; a failure leaves no trace (primitives validate first, composites revert themselves).</summary>
        internal bool TryApply(ISceneCommand cmd, ChangeSink sink, out CommandResult error)
        {
            error = null;
            applying = true;
            try
            {
                cmd.Validate(Model);
                cmd.Apply(Model, sink);
                return true;
            }
            catch (CommandException e)
            {
                error = CommandResult.Error(e.Code, e.Message, cmd);
                return false;
            }
            catch (Exception e)
            {
                error = Internal(cmd, e);
                return false;
            }
            finally { applying = false; }
        }

        /// <summary>An unexpected exception inside a command: checked model, logged, history cleared.</summary>
        internal CommandResult Internal(ISceneCommand cmd, Exception e)
        {
            applying = false;
            string violation = null;
            try { Model.Validate(); }
            catch (Exception v) { violation = v.Message; }
            SceneEvents.Log?.Invoke("scene command " + cmd?.Label + " failed: " + e + (violation != null ? "\nmodel: " + violation : ""));
            var forgotten = new List<TreeNode>();
            if (ActiveTransaction != null) ActiveTransaction.Abort();
            History.ClearAll(forgotten);
            Selection.PruneSilently();
            Version++;
            foreach (var n in forgotten) Events.RaiseNodeForgotten(n.Id);
            RaiseHistoryChanged();
            return CommandResult.Error(CommandCodes.Internal, e.GetType().Name + ": " + e.Message + (violation != null ? " (" + violation + ")" : ""), cmd);
        }

        /// <summary>Selection after an operation: removed ids leave; adds by the user and duplicates select the new nodes.</summary>
        internal IReadOnlyList<string> SelectAfter(ISceneCommand cmd, CommandSource source)
        {
            Selection.PruneSilently();
            if (cmd is AddNodesCommand add)
            {
                var ids = add.TopIds;
                if (add.IsDuplicate || source == CommandSource.User) Selection.SetSilently(ids);
                return ids;
            }
            return null;
        }

        /// <summary>Records a command (merged into the newest entry when allowed); returns the entry's command.</summary>
        ISceneCommand Record(ISceneCommand cmd, ExecuteOptions o, string[] selBefore, string[] selAfter, List<TreeNode> forgotten)
        {
            var top = History.Top;
            if (o.MergeKey != null && top != null && !top.Sealed && top.MergeKey == o.MergeKey && History.Redo.Count == 0 &&
                IsPropertyOnly(top.Command) && IsPropertyOnly(cmd) && top.Command.TryMerge(cmd))
            {
                top.SelectionAfter = selAfter;
                History.Resize(top);
                Evict(forgotten);
                return top.Command;
            }
            Push(new HistoryEntry
            {
                Command = cmd, Label = o.Label ?? cmd.Label, Source = o.Source, MergeKey = o.MergeKey,
                SelectionBefore = selBefore, SelectionAfter = selAfter,
            }, forgotten);
            return cmd;
        }

        /// <summary>Pushes a recorded entry: the redo branch is disposed, the previous entry sealed, the limits applied.</summary>
        internal void Push(HistoryEntry e, List<TreeNode> forgotten)
        {
            foreach (var r in History.Redo) History.Release(r, forgotten);
            History.Redo.Clear();
            SealTop();
            History.Undo.Add(e);
            History.Hold(e);
            Evict(forgotten);
        }

        void Evict(List<TreeNode> forgotten)
        {
            // The newest entry stays even when it alone is over the byte limit.
            while (History.Undo.Count > 1 && (History.Undo.Count > Math.Max(1, limits.MaxEntries) || History.Bytes > limits.MaxBytes))
            {
                var e = History.Undo[0];
                History.Undo.RemoveAt(0);
                History.Release(e, forgotten);
            }
        }

        internal void SealTop()
        {
            var top = History.Top;
            if (top != null) top.Sealed = true;
        }

        /// <summary>Nodes of a command that are out of the model and held by no entry (and by no open transaction).</summary>
        internal void Unheld(IEnumerable<TreeNode> nodes, List<TreeNode> forgotten)
        {
            foreach (var n in nodes)
                if (!n.InModel && !History.IsHeld(n) && !(ActiveTransaction?.Holds(n) ?? false) && !forgotten.Contains(n)) forgotten.Add(n);
        }

        /// <summary>Delivers one operation: Changed, then SelectionChanged, NodeForgotten, HistoryChanged.</summary>
        internal void Deliver(ChangeOrigin origin, CommandSource source, IReadOnlyList<SceneChange> changes, string[] selBefore, string[] selAfter,
            List<TreeNode> forgotten, bool historyChanged)
        {
            if (changes != null && changes.Count > 0) Events.RaiseChanged(new SceneChangeBatch(origin, source, new List<SceneChange>(changes)));
            if (selBefore != null && selAfter != null && !SelectionModel.Same(selBefore, selAfter)) Events.RaiseSelectionChanged(selBefore, selAfter);
            if (forgotten != null) foreach (var n in forgotten) Events.RaiseNodeForgotten(n.Id);
            if (historyChanged) RaiseHistoryChanged();
        }

        internal void RaiseHistoryChanged()
        {
            var u = History.Top;
            var r = History.Redo.Count > 0 ? History.Redo[History.Redo.Count - 1] : null;
            Events.RaiseHistoryChanged(new HistoryChangedArgs(History.Undo.Count, History.Redo.Count, u?.Label, r?.Label));
        }

        // ---------------- classification ----------------

        /// <summary>Property sets only (directly or in composites): safe to apply without history. A Timeline or constraint edit
        /// counts as one (it replaces a value of the scene and holds no node), so it merges and applies without history as they do.</summary>
        internal static bool IsPropertyOnly(ISceneCommand c)
        {
            if (c is SetPropertyCommand || c is TimelineEditCommand || c is ConstraintEditCommand) return true;
            if (c is CompositeCommand k)
            {
                foreach (var x in k.Children) if (!IsPropertyOnly(x)) return false;
                return true;
            }
            return false;
        }

        /// <summary>Every write of the command is view state.</summary>
        internal static bool IsViewOnly(ISceneCommand c)
        {
            if (c is FieldCommand f) return f.IsViewState;
            if (c is CompositeCommand k)
            {
                if (k.Children.Count == 0) return false;
                foreach (var x in k.Children) if (!IsViewOnly(x)) return false;
                return true;
            }
            return false;
        }

        // ---------------- transactions ----------------

        /// <summary>Opens a transaction. While one is open, or while the bus is busy, Begin returns a closed transaction whose
        /// <see cref="Transaction.Failure"/> is `busy`.</summary>
        public Transaction Begin(string label, ExecuteOptions o = default)
        {
            session.CheckThread();
            if (ActiveTransaction != null) return new Transaction(this, label, o, BusyResult("a transaction is already open"));
            if (Busy) return new Transaction(this, label, o, BusyResult("the bus is busy"));
            var tx = new Transaction(this, label, o, null);
            tx.Open(Selection.Snapshot());
            ActiveTransaction = tx;
            return tx;
        }

        /// <summary>
        /// <see cref="Begin"/> when no transaction is open; else a joined handle of the open one (
        /// 4): its Set / Execute run as steps of the open transaction, its Commit records nothing (the open
        /// transaction's Commit records one entry for everything), its Cancel (or Dispose without Commit) reverts and
        /// removes only the steps added through it. Code that makes one entry on its own and one step of a bridge
        /// batch uses this instead of Begin. Busy while the bus is busy, as Begin.
        /// </summary>
        public Transaction BeginOrJoin(string label, ExecuteOptions o = default)
        {
            session.CheckThread();
            if (ActiveTransaction == null) return Begin(label, o);
            if (Busy) return new Transaction(this, label, o, BusyResult("the bus is busy"));
            return ActiveTransaction.Join(label, o);
        }

        /// <summary>The open transaction, or null.</summary>
        public Transaction ActiveTransaction { get; internal set; }

        internal string[] SelectionSnapshot() => Selection.Snapshot();
        internal void SetSelectionSilently(IEnumerable<string> ids) => Selection.SetSilently(ids);
        internal void CheckThreadFor(Transaction tx) => session.CheckThread();

        /// <summary>Reverts transaction steps in reverse order (Cancel).</summary>
        internal void RevertSteps(List<ISceneCommand> steps, ChangeSink sink)
        {
            applying = true;
            try { for (int i = steps.Count - 1; i >= 0; i--) steps[i].Revert(Model, sink); }
            finally { applying = false; }
        }

        // ---------------- undo / redo ----------------

        public bool CanUndo => History.Undo.Count > 0 && ActiveTransaction == null;
        public bool CanRedo => History.Redo.Count > 0 && ActiveTransaction == null;

        public CommandResult Undo()
        {
            session.CheckThread();
            if (Busy || ActiveTransaction != null) return BusyResult("undo is not possible now (busy or a transaction is open)");
            var e = History.Top;
            if (e == null) return CommandResult.NoOp(null);
            History.Undo.RemoveAt(History.Undo.Count - 1);
            var sink = new ChangeSink();
            applying = true;
            try { e.Command.Revert(Model, sink); }
            catch (Exception ex) when (e.Owner == null)
            {
                History.Redo.Add(e);
                return Internal(e.Command, ex);
            }
            catch (Exception ex) { ExternalFailed(e, "undo", ex); }
            finally { applying = false; }
            History.Redo.Add(e);
            e.Sealed = true;
            SealTop();
            TakeFailure(e);
            var selBefore = Selection.Snapshot();
            if (e.SelectionBefore != null) Selection.SetSilently(e.SelectionBefore);
            Version++;
            var forgotten = RunPendingRemovals();
            Deliver(ChangeOrigin.Undo, CommandSource.User, sink.Changes, selBefore, Selection.Snapshot(), forgotten, true);
            return CommandResult.Success(e.Command);
        }

        public CommandResult Redo()
        {
            session.CheckThread();
            if (Busy || ActiveTransaction != null) return BusyResult("redo is not possible now (busy or a transaction is open)");
            if (History.Redo.Count == 0) return CommandResult.NoOp(null);
            var e = History.Redo[History.Redo.Count - 1];
            History.Redo.RemoveAt(History.Redo.Count - 1);
            var sink = new ChangeSink();
            applying = true;
            try
            {
                e.Command.Validate(Model);
                e.Command.Apply(Model, sink);
            }
            catch (Exception ex) when (e.Owner == null)
            {
                History.Undo.Add(e);
                return Internal(e.Command, ex);
            }
            catch (Exception ex) { ExternalFailed(e, "redo", ex); }
            finally { applying = false; }
            SealTop();
            History.Undo.Add(e);
            e.Sealed = true;
            TakeFailure(e);
            var selBefore = Selection.Snapshot();
            if (e.SelectionAfter != null) Selection.SetSilently(e.SelectionAfter);
            Version++;
            var forgotten = RunPendingRemovals();
            Deliver(ChangeOrigin.Redo, CommandSource.User, sink.Changes, selBefore, Selection.Snapshot(), forgotten, true);
            return CommandResult.Success(e.Command);
        }

        /// <summary>Undo entries, newest first.</summary>
        public IReadOnlyList<HistoryEntryInfo> UndoList
        {
            get
            {
                var l = new List<HistoryEntryInfo>(History.Undo.Count);
                for (int i = History.Undo.Count - 1; i >= 0; i--) l.Add(History.Undo[i].Info);
                return l;
            }
        }

        /// <summary>Redo entries, the next one to redo first.</summary>
        public IReadOnlyList<HistoryEntryInfo> RedoList
        {
            get
            {
                var l = new List<HistoryEntryInfo>(History.Redo.Count);
                for (int i = History.Redo.Count - 1; i >= 0; i--) l.Add(History.Redo[i].Info);
                return l;
            }
        }

        // ---------------- external entries ----------------

        readonly List<string> warnings = new List<string>();
        readonly List<string> pendingRemovals = new List<string>();

        /// <summary>
        /// Records an entry of a party outside the scene model (<paramref name="owner"/>, e.g. a plugin's GUID) for a change it
        /// has already made to its own state: no apply and no change batch, onto the undo list as a recorded command goes:
        /// the redo branch is dropped, the previous entry sealed, the limits applied, the version raised and HistoryChanged
        /// raised. Undo calls the command's Revert and redo its Validate and Apply, with the bus applying (the scene answers
        /// busy meanwhile); an exception there never clears the history (the entry moves to the other list and the step's
        /// warning is kept for <see cref="TakeWarnings"/>). A record with the same non-null merge key as the newest entry,
        /// when that entry is the same owner's, not sealed and nothing can be redone, merges into it through the command's
        /// TryMerge. Busy while a transaction is open, a command applies (also inside an undo or redo callback) or a scene
        /// event is being delivered.
        /// </summary>
        public ExternalRecordResult RecordExternal(string owner, ISceneCommand cmd, string label, string mergeKey = null)
        {
            session.CheckThread();
            if (string.IsNullOrEmpty(owner)) throw new ArgumentException("an external entry needs an owner", nameof(owner));
            if (cmd == null) throw new ArgumentNullException(nameof(cmd));
            if (ActiveTransaction != null || Busy) return ExternalRecordResult.Busy;
            var forgotten = new List<TreeNode>();
            var top = History.Top;
            bool merged = false;
            if (mergeKey != null && top != null && !top.Sealed && top.MergeKey == mergeKey && History.Redo.Count == 0 &&
                top.Owner != null && string.Equals(top.Owner, owner, StringComparison.Ordinal) && top.Command.TryMerge(cmd))
            {
                History.Resize(top);
                Evict(forgotten);
                merged = true;
            }
            else
            {
                Push(new HistoryEntry { Command = cmd, Label = label ?? cmd.Label, Source = CommandSource.Plugin, MergeKey = mergeKey, Owner = owner }, forgotten);
            }
            Version++;
            Deliver(ChangeOrigin.Command, CommandSource.Plugin, null, null, null, forgotten, true);
            return merged ? ExternalRecordResult.Merged : ExternalRecordResult.Recorded;
        }

        /// <summary>
        /// Removes every entry of <paramref name="owner"/> from the undo and redo lists at once (the others keep their order)
        /// and raises HistoryChanged when one was removed; returns the count. Called while an undo or redo step runs (the
        /// owner's callback ended it), the removal happens as soon as that step has placed its entry.
        /// </summary>
        public int RemoveOwner(string owner)
        {
            session.CheckThread();
            if (string.IsNullOrEmpty(owner)) return 0;
            if (applying)
            {
                if (!pendingRemovals.Contains(owner)) pendingRemovals.Add(owner);
                return History.CountOwner(owner);
            }
            var forgotten = new List<TreeNode>();
            int n = History.RemoveOwner(owner, forgotten);
            foreach (var x in forgotten) Events.RaiseNodeForgotten(x.Id);
            if (n > 0) RaiseHistoryChanged();
            return n;
        }

        /// <summary>The entries of <paramref name="owner"/> in the undo and redo lists.</summary>
        public int CountOwner(string owner) => string.IsNullOrEmpty(owner) ? 0 : History.CountOwner(owner);

        /// <summary>True when <see cref="RecordExternal"/> would not answer Busy now (no open transaction, nothing applying or being delivered).</summary>
        public bool AcceptsExternal => ActiveTransaction == null && !Busy;

        /// <summary>The warnings of the undo and redo steps since the last call (an external entry's callback failed); cleared.</summary>
        public IReadOnlyList<string> TakeWarnings()
        {
            if (warnings.Count == 0) return Array.Empty<string>();
            var l = warnings.ToArray();
            warnings.Clear();
            return l;
        }

        void ExternalFailed(HistoryEntry e, string step, Exception ex)
        {
            warnings.Add("plugin " + e.Owner + ": " + step + " failed (" + ex.GetType().Name + ")");
            SceneEvents.Log?.Invoke("history " + step + " of " + e.Owner + " (" + e.Label + ") failed: " + ex);
        }

        void TakeFailure(HistoryEntry e)
        {
            if (e.Owner != null && e.Command is PluginHistoryCommand p && p.TakeFailure() is string f) warnings.Add(f);
        }

        List<TreeNode> RunPendingRemovals()
        {
            if (pendingRemovals.Count == 0) return null;
            var forgotten = new List<TreeNode>();
            foreach (var o in pendingRemovals.ToArray()) History.RemoveOwner(o, forgotten);
            pendingRemovals.Clear();
            return forgotten;
        }

        /// <summary>Disposes every entry (nodes held only by history are forgotten).</summary>
        public void ClearHistory()
        {
            session.CheckThread();
            if (applying) throw new InvalidOperationException("ClearHistory while a command applies");
            var forgotten = new List<TreeNode>();
            bool had = History.Undo.Count + History.Redo.Count > 0;
            History.ClearAll(forgotten);
            foreach (var n in forgotten) Events.RaiseNodeForgotten(n.Id);
            if (had) RaiseHistoryChanged();
        }

        /// <summary>History bounds; setting them evicts at once.</summary>
        public HistoryLimits Limits
        {
            get => limits;
            set
            {
                limits = value ?? new HistoryLimits();
                var forgotten = new List<TreeNode>();
                int before = History.Undo.Count;
                Evict(forgotten);
                foreach (var n in forgotten) Events.RaiseNodeForgotten(n.Id);
                if (History.Undo.Count != before) RaiseHistoryChanged();
            }
        }

        /// <summary>+1 per persisted change, recorded or not.</summary>
        public long Version { get; internal set; }
        /// <summary><see cref="Version"/> differs from the version at the last <see cref="MarkSaved"/> (or load).</summary>
        public bool IsDirty => Version != SavedVersion;
        /// <summary>After a successful save: the current version is the file's; seals the newest entry against merging.</summary>
        public void MarkSaved()
        {
            SavedVersion = Version;
            SealTop();
        }

        internal long SavedVersion;

        // ---------------- silent writes ----------------

        internal void WriteSilently(ChangeOrigin origin, Action<SilentWriter> body)
        {
            session.CheckThread();
            if (applying || Events.IsDeliveringChanged)
                throw new InvalidOperationException("silent writes are not allowed while a command applies or Changed handlers run");
            var w = new SilentWriter(session);
            body?.Invoke(w);
            if (w.Sink.Count == 0) return;
            Version++;
            Deliver(origin, CommandSource.System, w.Sink.Changes, null, null, null, false);
        }

        internal CommandResult SilentSet(ChangeSink sink, string nodeId, string path, object value)
        {
            if (applying) throw new InvalidOperationException("silent writes are not allowed while a command applies");
            var cmd = new SetPropertyCommand(nodeId, path, value);
            int before = sink.Count;
            if (!TryApply(cmd, sink, out var error)) return error;
            return sink.Count == before ? CommandResult.NoOp(cmd) : CommandResult.Success(cmd);
        }

        /// <summary>Applies a silent structural or appearance change of the session (import cache, normalisation).</summary>
        internal void Silent(ChangeOrigin origin, ChangeSink sink)
        {
            if (sink.Count == 0) return;
            Version++;
            Deliver(origin, CommandSource.System, sink.Changes, null, null, null, false);
        }

        /// <summary>History cleared by New/Bind (no events here; the session raises them).</summary>
        internal void Reset(List<TreeNode> forgotten)
        {
            ActiveTransaction?.Abort();
            History.ClearAll(forgotten);
        }
    }
}
